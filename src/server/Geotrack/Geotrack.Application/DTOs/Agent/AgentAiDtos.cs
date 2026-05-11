namespace Geotrack.Application.DTOs.Agent;

public class AgentProvidersResponseDto
{
    public List<AgentProviderMetadataDto> Providers { get; set; } = new();
    public AgentMcpMetadataDto Mcp { get; set; } = new();
}

public class AgentMcpMetadataDto
{
    public bool Enabled { get; set; }
    public string? Url { get; set; }
    public string Transport { get; set; } = "remote-http";
    public bool PrivateToolsEnabled { get; set; }
}

public class AgentProviderMetadataDto
{
    public string Id { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public bool Enabled { get; set; }
    public List<AgentProviderModelDto> Models { get; set; } = new();
    public string? DefaultModel { get; set; }
    public bool SupportsSavedKey { get; set; }
    public bool SupportsSessionKey { get; set; }
    public bool ProjectDemoKeyEnabled { get; set; }
    public bool HasSavedKey { get; set; }
    public string? KeyLast4 { get; set; }
    public DateTime? KeyUpdatedAt { get; set; }
}

public class AgentProviderModelDto
{
    public string Id { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public bool IsDefault { get; set; }
}

public class AgentApiKeyMetadataDto
{
    public string Provider { get; set; } = string.Empty;
    public string KeyLast4 { get; set; } = string.Empty;
    public string? DisplayName { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? LastUsedAt { get; set; }
}

public class SaveAgentApiKeyRequestDto
{
    public string Provider { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
    public string? DisplayName { get; set; }
}

public class TestAgentApiKeyRequestDto
{
    public string Provider { get; set; } = string.Empty;
    public string? ApiKey { get; set; }
    public string? Model { get; set; }
    public string KeyMode { get; set; } = AgentKeyModes.Session;
}

public class TestAgentApiKeyResponseDto
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
}

public class AgentChatRequestDto
{
    public string Message { get; set; } = string.Empty;
    public string Provider { get; set; } = string.Empty;
    public string? Model { get; set; }
    public string KeyMode { get; set; } = AgentKeyModes.Saved;
    public string? SessionApiKey { get; set; }
    public string? ConversationId { get; set; }
    public AgentContextOptionsDto? ContextOptions { get; set; }
}

public class AgentContextOptionsDto
{
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public int? DestinationId { get; set; }
    public string? CurrentCity { get; set; }
    public string? CurrentDestinationName { get; set; }
    public string? CurrentLocationName { get; set; }
    public string? Language { get; set; }
}

public class AgentChatResponseDto
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string ConversationId { get; set; } = Guid.NewGuid().ToString();
    public string Role { get; set; } = "assistant";
    public string Type { get; set; } = "text";
    public string Text { get; set; } = string.Empty;
    public string Provider { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public List<string> ToolsUsed { get; set; } = new();
    public List<AgentResultCardDto> Cards { get; set; } = new();
    public AgentUsageMetadataDto? Usage { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}

public class AgentResultCardDto
{
    public string EntityType { get; set; } = string.Empty;
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Subtitle { get; set; }
    public string? Description { get; set; }
    public string? ImageUrl { get; set; }
    public string? LocationName { get; set; }
    public double? Rating { get; set; }
    public int? ReviewCount { get; set; }
    public double? DistanceKm { get; set; }
    public List<string> Tags { get; set; } = new();
    public string? DateText { get; set; }
    public string? StayType { get; set; }
    public string? PlaceType { get; set; }
    public string DetailRoute { get; set; } = string.Empty;
}

public class AgentUsageMetadataDto
{
    public int? PromptTokens { get; set; }
    public int? CompletionTokens { get; set; }
    public int? TotalTokens { get; set; }
}

public static class AgentKeyModes
{
    public const string Saved = "saved";
    public const string Session = "session";
    public const string ProjectDemo = "project-demo";

    public static bool IsKnown(string? value) =>
        string.Equals(value, Saved, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(value, Session, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(value, ProjectDemo, StringComparison.OrdinalIgnoreCase);
}
