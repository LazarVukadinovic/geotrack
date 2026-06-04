using Geotrack.Application.DTOs.Agent;
using Geotrack.Application.Interfaces.Service;

namespace Geotrack.Infrastructure.Services.Agent;

public sealed class NoOpAgentSemanticSearchService : IAgentSemanticSearchService
{
    public bool IsEnabled => false;

    public Task<AgentSemanticSearchResponseDto> SearchAsync(
        AgentSemanticSearchRequestDto request,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new AgentSemanticSearchResponseDto
        {
            IsEnabled = false
        });
}
