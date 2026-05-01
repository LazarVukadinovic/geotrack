using Geotrack.Server;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Geotrack.IntegrationTests.Infrastructure;

internal sealed class GeotrackApiFactory : WebApplicationFactory<Program>, IAsyncDisposable
{
    private readonly string _databaseName = $"geotrack_batch11_{Guid.NewGuid():N}";

    private string? _baseConnectionString;
    private string? _testConnectionString;

    public static async Task<GeotrackApiFactory> CreateAsync()
    {
        var factory = new GeotrackApiFactory();
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
        builder.UseEnvironment("Development");
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["ConnectionStrings:DefaultConnection"] = _testConnectionString,
                });
        });
        builder.ConfigureLogging(logging =>
        {
            logging.ClearProviders();
            logging.AddConsole();
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
        drop.CommandText = $"DROP DATABASE IF EXISTS \"{_databaseName}\"";
        await drop.ExecuteNonQueryAsync();
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
