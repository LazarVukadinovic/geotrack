using Geotrack.Application.DTOs.Agent;

namespace Geotrack.Application.Interfaces.Service;

public interface IGeminiAskAgentService
{
    string ProviderId { get; }
    string Model { get; }
    bool HasAvailableKey { get; }
    Task<GeminiTextGenerationResult> GenerateAsync(
        GeminiTextGenerationRequest request,
        CancellationToken cancellationToken = default);
}

public class GeminiTextGenerationRequest
{
    public string ApiKey { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public string SystemPrompt { get; set; } = string.Empty;
    public string UserMessage { get; set; } = string.Empty;
    public TimeSpan Timeout { get; set; }
}

public class GeminiTextGenerationResult
{
    public string Text { get; set; } = string.Empty;
    public AgentUsageMetadataDto? Usage { get; set; }
}
