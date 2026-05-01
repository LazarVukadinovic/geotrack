using System.Data;
using Geotrack.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Geotrack.Server;

internal static class PostgisStartup
{
    internal static async Task EnsureReadyAsync(IServiceProvider services, bool attemptEnable, CancellationToken cancellationToken = default)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        try
        {
            if (attemptEnable)
            {
                await db.Database.ExecuteSqlRawAsync("CREATE EXTENSION IF NOT EXISTS postgis", cancellationToken);
            }

            await using var connection = db.Database.GetDbConnection();
            if (connection.State != ConnectionState.Open)
            {
                await connection.OpenAsync(cancellationToken);
            }

            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT extversion FROM pg_extension WHERE extname = 'postgis'";
            var version = await command.ExecuteScalarAsync(cancellationToken) as string;

            if (string.IsNullOrWhiteSpace(version))
            {
                throw new InvalidOperationException(
                    "PostGIS is required for GeoTrack's location-aware Explore queries. Install the PostGIS extension for your PostgreSQL server, then run `CREATE EXTENSION IF NOT EXISTS postgis;` for this database or restart the app in Development so bootstrap can do it automatically.");
            }
        }
        catch (PostgresException ex) when (ReferencesPostgis(ex))
        {
            throw new InvalidOperationException(
                "GeoTrack now requires PostGIS for spatial Explore queries, but PostgreSQL could not enable it. Install the PostGIS extension for your PostgreSQL server and ensure the database user can run `CREATE EXTENSION IF NOT EXISTS postgis;` once.",
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
