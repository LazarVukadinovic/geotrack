using Geotrack.Server;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Geotrack.IntegrationTests.Infrastructure;

internal sealed class GeotrackApiFactory : WebApplicationFactory<Program>, IAsyncDisposable
{
    private readonly string _databaseName = $"geotrack_batch11_{Guid.NewGuid():N}";
    private readonly string _environmentName;
    private readonly IReadOnlyDictionary<string, string?> _configurationOverrides;

    private string? _baseConnectionString;
    private string? _testConnectionString;
    private string? _previousConnectionStringEnvironmentValue;
    private string? _previousJwtKeyEnvironmentValue;
    private string TestDataProtectionKeyDirectory =>
        Path.Combine(AppContext.BaseDirectory, ".dataprotection-keys", _databaseName);

    private GeotrackApiFactory(
        string environmentName,
        IReadOnlyDictionary<string, string?>? configurationOverrides)
    {
        _environmentName = environmentName;
        _configurationOverrides = configurationOverrides ?? new Dictionary<string, string?>();
    }

    static GeotrackApiFactory()
    {
#pragma warning disable CS0618
        NpgsqlConnection.GlobalTypeMapper.UseNetTopologySuite();
#pragma warning restore CS0618
    }

    public static async Task<GeotrackApiFactory> CreateAsync(
        string environmentName = "Development",
        IReadOnlyDictionary<string, string?>? configurationOverrides = null)
    {
        var factory = new GeotrackApiFactory(environmentName, configurationOverrides);
        await factory.InitializeAsync();
        return factory;
    }

    private async Task InitializeAsync()
    {
        var config = new ConfigurationBuilder()
            .AddUserSecrets<Program>(optional: true)
            .AddEnvironmentVariables()
            .Build();

        _baseConnectionString = config.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException(
                "ConnectionStrings:DefaultConnection is required in user-secrets or the environment to run integration tests.");

        ConfigureEarlyProductionEnvironment(config);

        _testConnectionString = new NpgsqlConnectionStringBuilder(_baseConnectionString)
        {
            Database = _databaseName,
        }.ConnectionString;

        await using var connection = new NpgsqlConnection(BuildAdminConnectionString());
        await connection.OpenAsync();

        await using var create = connection.CreateCommand();
        create.CommandText = $"CREATE DATABASE \"{_databaseName}\"";
        await create.ExecuteNonQueryAsync();

        await EnsurePostgisEnabledAsync();
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        builder.UseEnvironment(_environmentName);
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["ConnectionStrings:DefaultConnection"] = _testConnectionString,
                });

            if (_configurationOverrides.Count > 0)
            {
                config.AddInMemoryCollection(_configurationOverrides);
            }
        });
        builder.ConfigureLogging(logging =>
        {
            logging.ClearProviders();
            logging.AddConsole();
        });
        builder.ConfigureServices(services =>
        {
            var keyDirectory = new DirectoryInfo(TestDataProtectionKeyDirectory);
            keyDirectory.Create();

            services.AddDataProtection()
                .PersistKeysToFileSystem(keyDirectory)
                .SetApplicationName($"GeoTrack.IntegrationTests.{_databaseName}");
        });

        return base.CreateHost(builder);
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();

        if (string.IsNullOrWhiteSpace(_baseConnectionString))
            return;

        await using var connection = new NpgsqlConnection(BuildAdminConnectionString());
        await connection.OpenAsync();

        await using (var terminate = connection.CreateCommand())
        {
            terminate.CommandText = """
                SELECT pg_terminate_backend(pid)
                FROM pg_stat_activity
                WHERE datname = @databaseName
                  AND pid <> pg_backend_pid();
                """;
            terminate.Parameters.AddWithValue("databaseName", _databaseName);
            await terminate.ExecuteNonQueryAsync();
        }

        await using var drop = connection.CreateCommand();
        drop.CommandTimeout = 120;
        drop.CommandText = $"DROP DATABASE IF EXISTS \"{_databaseName}\"";
        await drop.ExecuteNonQueryAsync();

        RestoreEarlyProductionEnvironment();
    }

    private void ConfigureEarlyProductionEnvironment(IConfiguration config)
    {
        if (_environmentName != Environments.Production)
        {
            return;
        }

        var jwtKey = config["Jwt:Key"]
            ?? throw new InvalidOperationException(
                "Jwt:Key is required in user-secrets or the environment to run production-mode integration tests.");

        _previousConnectionStringEnvironmentValue =
            Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection");
        _previousJwtKeyEnvironmentValue =
            Environment.GetEnvironmentVariable("Jwt__Key");

        Environment.SetEnvironmentVariable("ConnectionStrings__DefaultConnection", _baseConnectionString);
        Environment.SetEnvironmentVariable("Jwt__Key", jwtKey);
    }

    private void RestoreEarlyProductionEnvironment()
    {
        if (_environmentName != Environments.Production)
        {
            return;
        }

        Environment.SetEnvironmentVariable(
            "ConnectionStrings__DefaultConnection",
            _previousConnectionStringEnvironmentValue);
        Environment.SetEnvironmentVariable("Jwt__Key", _previousJwtKeyEnvironmentValue);
    }

    private string BuildAdminConnectionString()
    {
        var builder = new NpgsqlConnectionStringBuilder(_baseConnectionString)
        {
            Database = "postgres",
        };

        return builder.ConnectionString;
    }

    private async Task EnsurePostgisEnabledAsync()
    {
        await using var connection = new NpgsqlConnection(_testConnectionString);
        await connection.OpenAsync();

        try
        {
            await using (var enable = connection.CreateCommand())
            {
                enable.CommandText = "CREATE EXTENSION IF NOT EXISTS postgis";
                await enable.ExecuteNonQueryAsync();
            }

            await using var verify = connection.CreateCommand();
            verify.CommandText = "SELECT PostGIS_Version()";
            var version = await verify.ExecuteScalarAsync() as string;

            if (string.IsNullOrWhiteSpace(version))
            {
                throw new InvalidOperationException(
                    "PostGIS was not available in the integration-test database after CREATE EXTENSION. Install PostGIS for the local PostgreSQL server and make sure the test user can enable it.");
            }
        }
        catch (PostgresException ex) when (ReferencesPostgis(ex))
        {
            throw new InvalidOperationException(
                "Integration tests require PostGIS in each temporary database. Install PostGIS for the local PostgreSQL server and ensure the configured database user can run `CREATE EXTENSION IF NOT EXISTS postgis;`.",
                ex);
        }
    }

    private static bool ReferencesPostgis(PostgresException ex)
    {
        return ex.MessageText.Contains("postgis", StringComparison.OrdinalIgnoreCase)
            || ex.Detail?.Contains("postgis", StringComparison.OrdinalIgnoreCase) == true
            || ex.Hint?.Contains("postgis", StringComparison.OrdinalIgnoreCase) == true;
    }
}
