using Geotrack.Application.DTOs.Agent;

namespace Geotrack.Application.Interfaces.Service;

public interface IAgentDatabaseCapabilityService
{
    Task<AgentDatabaseCapabilitiesDto> GetCapabilitiesAsync(CancellationToken cancellationToken = default);
}

public interface IAgentTaxonomyProvider
{
    Task<AgentTaxonomyDto> GetTaxonomyAsync(CancellationToken cancellationToken = default);
}

public interface IAgentAreaResolver
{
    Task<AgentAreaResolutionDto?> ResolveFromTextAsync(
        string text,
        bool includeEntities = false,
        CancellationToken cancellationToken = default);

    Task<AgentAreaResolutionDto?> ResolveFromContextAsync(
        AgentContextOptionsDto? contextOptions,
        CancellationToken cancellationToken = default);

    Task<AgentAreaResolutionDto?> ResolveByIdAsync(
        string areaType,
        int areaId,
        CancellationToken cancellationToken = default);
}

public interface IAgentSemanticSearchService
{
    bool IsEnabled { get; }

    Task<AgentSemanticSearchResponseDto> SearchAsync(
        AgentSemanticSearchRequestDto request,
        CancellationToken cancellationToken = default);
}
