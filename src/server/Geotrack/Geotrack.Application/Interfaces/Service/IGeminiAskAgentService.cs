using Geotrack.Application.DTOs.Agent;

namespace Geotrack.Application.Interfaces.Service;

public interface IGeminiAskAgentService
{
    string ProviderId { get; }
    string Model { get; }
    bool HasAvailableKey { get; }
    Task<AgentProviderChatResult> GenerateAsync(
        AgentProviderChatRequest request,
        CancellationToken cancellationToken = default);
}
