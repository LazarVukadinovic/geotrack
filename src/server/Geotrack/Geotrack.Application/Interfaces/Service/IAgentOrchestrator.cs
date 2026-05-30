using Geotrack.Application.DTOs.Agent;

namespace Geotrack.Application.Interfaces.Service;

public interface IAgentOrchestrator
{
    Task<AgentProvidersResponseDto> GetProvidersAsync(int? userId, CancellationToken cancellationToken = default);
    Task<AgentChatResponseDto> ChatAsync(AgentChatRequestDto request, int userId, CancellationToken cancellationToken = default);
}
