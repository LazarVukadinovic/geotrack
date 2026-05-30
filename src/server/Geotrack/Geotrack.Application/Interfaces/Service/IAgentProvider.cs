using Geotrack.Application.DTOs.Agent;

namespace Geotrack.Application.Interfaces.Service;

public interface IAgentProvider
{
    string Id { get; }
    AgentProviderMetadataDto GetMetadata(int? userId);
    Task<AgentProviderChatResult> GenerateAsync(AgentProviderChatRequest request, CancellationToken cancellationToken = default);
}

public class AgentProviderChatRequest
{
    public string ApiKey { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public string SystemPrompt { get; set; } = string.Empty;
    public string UserMessage { get; set; } = string.Empty;
    public TimeSpan Timeout { get; set; }
}

public class AgentProviderChatResult
{
    public string Text { get; set; } = string.Empty;
    public AgentUsageMetadataDto? Usage { get; set; }
}
