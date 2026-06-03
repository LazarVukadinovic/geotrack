namespace Geotrack.Application.Options;

public class AgentOptions
{
    public int MaxPromptLength { get; set; } = 4000;
    public int MaxConversationTurns { get; set; } = 8;
    public int MaxContextItems { get; set; } = 8;
    public int RequestTimeoutSeconds { get; set; } = 20;
    public int MaxToolResults { get; set; } = 10;
}
