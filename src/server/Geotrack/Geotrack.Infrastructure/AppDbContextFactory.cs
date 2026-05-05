using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Geotrack.Infrastructure;

public class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    private const string UserSecretsId = "364ea66a-6c55-4eaf-ab93-e5af079e07b2";

    public AppDbContext CreateDbContext(string[] args)
    {
        var connectionString = ResolveConnectionString();
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "ConnectionStrings:DefaultConnection could not be resolved for design-time AppDbContext creation. Set the Geotrack.Server user-secrets value or the ConnectionStrings__DefaultConnection environment variable.");
        }

        var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
        optionsBuilder.UseNpgsql(
            connectionString,
            options => options.UseNetTopologySuite());

        return new AppDbContext(optionsBuilder.Options);
    }

    private static string? ResolveConnectionString()
    {
        var envConnectionString = Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection");
        if (!string.IsNullOrWhiteSpace(envConnectionString))
        {
            return envConnectionString;
        }

        var secretsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Microsoft",
            "UserSecrets",
            UserSecretsId,
            "secrets.json");

        if (TryReadConnectionString(secretsPath, out var secretConnectionString))
        {
            return secretConnectionString;
        }

        foreach (var serverDirectory in EnumerateServerDirectories())
        {
            if (TryReadConnectionString(Path.Combine(serverDirectory, "appsettings.json"), out var appSettingsConnectionString))
            {
                return appSettingsConnectionString;
            }

            var environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
            if (!string.IsNullOrWhiteSpace(environment)
                && TryReadConnectionString(
                    Path.Combine(serverDirectory, $"appsettings.{environment}.json"),
                    out var environmentConnectionString))
            {
                return environmentConnectionString;
            }

            if (TryReadConnectionString(
                Path.Combine(serverDirectory, "appsettings.Development.json"),
                out var developmentConnectionString))
            {
                return developmentConnectionString;
            }
        }

        return null;
    }

    private static bool TryReadConnectionString(string path, out string? connectionString)
    {
        connectionString = null;
        if (!File.Exists(path))
        {
            return false;
        }

        using var stream = File.OpenRead(path);
        using var document = JsonDocument.Parse(stream);
        var root = document.RootElement;

        if (root.TryGetProperty("ConnectionStrings", out var connectionStrings)
            && connectionStrings.ValueKind == JsonValueKind.Object
            && connectionStrings.TryGetProperty("DefaultConnection", out var nestedValue)
            && nestedValue.ValueKind == JsonValueKind.String)
        {
            connectionString = nestedValue.GetString();
            return !string.IsNullOrWhiteSpace(connectionString);
        }

        if (root.TryGetProperty("ConnectionStrings:DefaultConnection", out var flatValue)
            && flatValue.ValueKind == JsonValueKind.String)
        {
            connectionString = flatValue.GetString();
            return !string.IsNullOrWhiteSpace(connectionString);
        }

        return false;
    }

    private static IEnumerable<string> EnumerateServerDirectories()
    {
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var current = new DirectoryInfo(Directory.GetCurrentDirectory());

        while (current is not null)
        {
            foreach (var candidate in new[]
                     {
                         Path.Combine(current.FullName, "Geotrack.Server"),
                         Path.Combine(current.FullName, "server", "Geotrack", "Geotrack.Server")
                     })
            {
                if (!visited.Add(candidate))
                {
                    continue;
                }

                if (File.Exists(Path.Combine(candidate, "Geotrack.Server.csproj")))
                {
                    yield return candidate;
                }
            }

            current = current.Parent;
        }
    }
}
