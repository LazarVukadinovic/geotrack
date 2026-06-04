using System.Data;
using Geotrack.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Geotrack.Server;

internal static class PostgisStartup
{
    private static readonly RequiredExtension[] RequiredExtensions =
    [
        new("postgis", "PostGIS", "location-aware Explore and nearby-content queries", IsHardRequirement: true),
        new("pg_trgm", "pg_trgm", "fuzzy public catalog search", IsHardRequirement: false),
        new("unaccent", "unaccent", "diacritics-insensitive public catalog search", IsHardRequirement: false)
    ];

    private const string OptionalVectorExtensionName = "vector";

    internal static async Task EnsureReadyAsync(IServiceProvider services, bool attemptEnable, CancellationToken cancellationToken = default)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var logger = scope.ServiceProvider
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger(nameof(PostgisStartup));

        try
        {
            if (attemptEnable)
            {
                foreach (var extension in RequiredExtensions)
                {
                    try
                    {
                        await db.Database.ExecuteSqlRawAsync(
                            GetCreateExtensionSql(extension.Name),
                            cancellationToken);
                    }
                    catch (PostgresException ex) when (IsDuplicateExtensionName(ex))
                    {
                        // A parallel test/dev bootstrap may already have created it; verify below.
                    }
                }
            }

            await using var connection = db.Database.GetDbConnection();
            if (connection.State != ConnectionState.Open)
            {
                await connection.OpenAsync(cancellationToken);
            }

            foreach (var extension in RequiredExtensions)
            {
                var version = await GetExtensionVersionAsync(connection, extension.Name, cancellationToken);
                if (string.IsNullOrWhiteSpace(version))
                {
                    if (attemptEnable || extension.IsHardRequirement)
                    {
                        throw new InvalidOperationException(
                            $"{extension.DisplayName} is required for GeoTrack's {extension.Purpose}. Install the PostgreSQL extension package if needed, then run `CREATE EXTENSION IF NOT EXISTS \"{extension.Name}\";` for this database or restart the app in Development so bootstrap can do it automatically.");
                    }

                    logger.LogWarning(
                        "PostgreSQL extension {ExtensionName} is not enabled. {ExtensionPurpose} may use fallback behavior or fail if a feature requires it.",
                        extension.Name,
                        extension.Purpose);
                    continue;
                }

                logger.LogInformation(
                    "PostgreSQL extension {ExtensionName} is available with version {ExtensionVersion}.",
                    extension.Name,
                    version);
            }

            var vectorVersion = await GetExtensionVersionAsync(connection, OptionalVectorExtensionName, cancellationToken);
            logger.LogInformation(
                "PostgreSQL extension capability check completed. vector available: {VectorAvailable}.",
                !string.IsNullOrWhiteSpace(vectorVersion));
        }
        catch (PostgresException ex) when (ReferencesKnownExtension(ex))
        {
            throw new InvalidOperationException(
                "GeoTrack requires PostgreSQL extensions postgis, pg_trgm, and unaccent for local/public catalog search. Install the needed PostgreSQL extension packages and ensure the database user can run `CREATE EXTENSION IF NOT EXISTS ...;` once.",
                ex);
        }
    }

    private static async Task<string?> GetExtensionVersionAsync(
        System.Data.Common.DbConnection connection,
        string extensionName,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT extversion FROM pg_extension WHERE extname = @extensionName";

        var parameter = command.CreateParameter();
        parameter.ParameterName = "extensionName";
        parameter.Value = extensionName;
        command.Parameters.Add(parameter);

        return await command.ExecuteScalarAsync(cancellationToken) as string;
    }

    private static bool ReferencesKnownExtension(PostgresException ex)
    {
        return RequiredExtensions.Any(extension =>
            ReferencesExtension(ex, extension.Name) ||
            ReferencesExtension(ex, extension.DisplayName));
    }

    private static bool ReferencesExtension(PostgresException ex, string extensionName)
    {
        return ex.MessageText.Contains(extensionName, StringComparison.OrdinalIgnoreCase)
            || ex.Detail?.Contains(extensionName, StringComparison.OrdinalIgnoreCase) == true
            || ex.Hint?.Contains(extensionName, StringComparison.OrdinalIgnoreCase) == true;
    }

    private static bool IsDuplicateExtensionName(PostgresException ex)
    {
        return ex.SqlState == PostgresErrorCodes.UniqueViolation
            && string.Equals(ex.ConstraintName, "pg_extension_name_index", StringComparison.OrdinalIgnoreCase);
    }

    private static string GetCreateExtensionSql(string extensionName)
    {
        return extensionName switch
        {
            "postgis" => "CREATE EXTENSION IF NOT EXISTS postgis",
            "pg_trgm" => "CREATE EXTENSION IF NOT EXISTS pg_trgm",
            "unaccent" => "CREATE EXTENSION IF NOT EXISTS unaccent",
            _ => throw new InvalidOperationException($"Unsupported PostgreSQL extension bootstrap request: {extensionName}.")
        };
    }

    private sealed record RequiredExtension(
        string Name,
        string DisplayName,
        string Purpose,
        bool IsHardRequirement);
}
