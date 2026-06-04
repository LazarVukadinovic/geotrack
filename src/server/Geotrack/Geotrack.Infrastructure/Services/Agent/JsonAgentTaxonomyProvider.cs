using System.Text.Json;
using Geotrack.Application.DTOs.Agent;
using Geotrack.Application.Interfaces.Service;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Geotrack.Infrastructure.Services.Agent;

public sealed class JsonAgentTaxonomyProvider : IAgentTaxonomyProvider
{
    private const string RelativeTaxonomyPath = "Data/Agent/agent-taxonomy.json";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private readonly IHostEnvironment _environment;
    private readonly ILogger<JsonAgentTaxonomyProvider> _logger;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private AgentTaxonomyDto? _cached;

    public JsonAgentTaxonomyProvider(
        IHostEnvironment environment,
        ILogger<JsonAgentTaxonomyProvider> logger)
    {
        _environment = environment;
        _logger = logger;
    }

    public async Task<AgentTaxonomyDto> GetTaxonomyAsync(CancellationToken cancellationToken = default)
    {
        if (_cached is not null)
        {
            return _cached;
        }

        await _lock.WaitAsync(cancellationToken);
        try
        {
            if (_cached is not null)
            {
                return _cached;
            }

            var path = ResolveTaxonomyPath();
            if (!File.Exists(path))
            {
                throw new InvalidOperationException(
                    $"Ask Agent taxonomy file was not found at {RelativeTaxonomyPath}.");
            }

            await using var stream = File.OpenRead(path);
            var taxonomy = await JsonSerializer.DeserializeAsync<AgentTaxonomyDto>(
                stream,
                JsonOptions,
                cancellationToken);

            Validate(taxonomy);
            _cached = taxonomy!;
            _logger.LogInformation(
                "Ask Agent taxonomy {TaxonomyVersion} loaded with {ConceptCount} concept(s).",
                _cached.Version,
                _cached.Concepts.Count);
            return _cached;
        }
        finally
        {
            _lock.Release();
        }
    }

    private string ResolveTaxonomyPath()
    {
        var contentRootPath = Path.Combine(
            _environment.ContentRootPath,
            RelativeTaxonomyPath.Replace('/', Path.DirectorySeparatorChar));
        if (File.Exists(contentRootPath))
        {
            return contentRootPath;
        }

        return Path.Combine(
            AppContext.BaseDirectory,
            RelativeTaxonomyPath.Replace('/', Path.DirectorySeparatorChar));
    }

    private static void Validate(AgentTaxonomyDto? taxonomy)
    {
        if (taxonomy is null ||
            string.IsNullOrWhiteSpace(taxonomy.Version) ||
            taxonomy.Concepts.Count == 0)
        {
            throw new InvalidOperationException("Ask Agent taxonomy is empty or invalid.");
        }

        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var concept in taxonomy.Concepts)
        {
            if (string.IsNullOrWhiteSpace(concept.Id) ||
                string.IsNullOrWhiteSpace(concept.Category) ||
                string.IsNullOrWhiteSpace(concept.DisplayLabel))
            {
                throw new InvalidOperationException("Ask Agent taxonomy contains a concept without id, category, or display label.");
            }

            if (!ids.Add(concept.Id))
            {
                throw new InvalidOperationException($"Ask Agent taxonomy contains duplicate concept id {concept.Id}.");
            }
        }
    }
}
