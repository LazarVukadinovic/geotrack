using System.Collections.Concurrent;
using System.Data;
using Geotrack.Application.DTOs.Agent;
using Geotrack.Application.Interfaces.Service;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Geotrack.Infrastructure.Services.Agent;

public sealed class AgentDatabaseCapabilityService : IAgentDatabaseCapabilityService
{
    private static readonly ConcurrentDictionary<string, Lazy<Task<AgentDatabaseCapabilitiesDto>>> Cache = new(StringComparer.Ordinal);
    private readonly AppDbContext _db;
    private readonly ILogger<AgentDatabaseCapabilityService> _logger;

    public AgentDatabaseCapabilityService(
        AppDbContext db,
        ILogger<AgentDatabaseCapabilityService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public Task<AgentDatabaseCapabilitiesDto> GetCapabilitiesAsync(CancellationToken cancellationToken = default)
    {
        var key = _db.Database.GetConnectionString() ?? _db.Database.ProviderName ?? "default";
        var lazy = Cache.GetOrAdd(
            key,
            _ => new Lazy<Task<AgentDatabaseCapabilitiesDto>>(
                () => LoadCapabilitiesAsync(cancellationToken),
                LazyThreadSafetyMode.ExecutionAndPublication));
        return lazy.Value;
    }

    private async Task<AgentDatabaseCapabilitiesDto> LoadCapabilitiesAsync(CancellationToken cancellationToken)
    {
        var result = new AgentDatabaseCapabilitiesDto();
        try
        {
            var connection = _db.Database.GetDbConnection();
            var openedHere = connection.State != ConnectionState.Open;
            if (openedHere)
            {
                await connection.OpenAsync(cancellationToken);
            }

            try
            {
                await using var command = connection.CreateCommand();
                command.CommandText = """
                    SELECT extname, extversion
                    FROM pg_extension
                    WHERE extname IN ('postgis', 'pg_trgm', 'unaccent', 'vector')
                    """;

                await using var reader = await command.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                {
                    var name = reader.GetString(0);
                    var version = reader.IsDBNull(1) ? string.Empty : reader.GetString(1);
                    result.ExtensionVersions[name] = version;
                }
            }
            finally
            {
                if (openedHere)
                {
                    await connection.CloseAsync();
                }
            }

            result.SupportsPostgis = result.ExtensionVersions.ContainsKey("postgis");
            result.SupportsPgTrgm = result.ExtensionVersions.ContainsKey("pg_trgm");
            result.SupportsUnaccent = result.ExtensionVersions.ContainsKey("unaccent");
            result.SupportsVector = result.ExtensionVersions.ContainsKey("vector");

            _logger.LogInformation(
                "Ask Agent DB capabilities detected. postgis={SupportsPostgis}; pg_trgm={SupportsPgTrgm}; unaccent={SupportsUnaccent}; vector={SupportsVector}.",
                result.SupportsPostgis,
                result.SupportsPgTrgm,
                result.SupportsUnaccent,
                result.SupportsVector);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Ask Agent could not detect PostgreSQL extension capabilities. Falling back to app-level normalized search.");
        }

        return result;
    }
}

public sealed class DisabledAgentDatabaseCapabilityService : IAgentDatabaseCapabilityService
{
    private static readonly AgentDatabaseCapabilitiesDto Capabilities = new();

    public Task<AgentDatabaseCapabilitiesDto> GetCapabilitiesAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(Capabilities);
}
