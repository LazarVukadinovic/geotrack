namespace Geotrack.Application.Options;

public class AgentOptions
{
    public bool AllowProjectDemoProvider { get; set; }
    public int MaxPromptLength { get; set; } = 4000;
    public int MaxConversationTurns { get; set; } = 8;
    public int MaxContextItems { get; set; } = 8;
    public int RequestTimeoutSeconds { get; set; } = 20;
    public int MaxToolResults { get; set; } = 10;
    public Dictionary<string, AgentProviderOptions> Providers { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public class AgentProviderOptions
{
    public bool Enabled { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = string.Empty;
    public string DefaultModel { get; set; } = string.Empty;
    public List<AgentModelOptions> Models { get; set; } = new();
    public string? ApiKey { get; set; }
    public bool SupportsSavedKey { get; set; } = true;
    public bool SupportsSessionKey { get; set; } = true;
    public bool SupportsProjectDemoKey { get; set; } = true;
}

public class AgentModelOptions
{
    public string Id { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public bool IsDefault { get; set; }
}
