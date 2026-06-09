namespace Geotrack.Application.Options;

public class AgentOptions
{
    public int MaxPromptLength { get; set; } = 4000;
    public int MaxConversationTurns { get; set; } = 8;
    public int MaxContextItems { get; set; } = 8;
    public int RequestTimeoutSeconds { get; set; } = 20;
    public int MaxToolResults { get; set; } = 10;
    public AgentNearbySearchOptions NearbySearch { get; set; } = new();
}

public class AgentNearbySearchOptions
{
    public double InitialRadiusKm { get; set; } = 10;
    public List<double> ExpansionRadiiKm { get; set; } = [25, 50, 100];
    public double MaxRadiusKm { get; set; } = 100;
    public int MaxResults { get; set; } = 10;
}
