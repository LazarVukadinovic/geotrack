namespace Geotrack.Application.DTOs.Analytics;

public class AnalyticsEventDto
{
    public string EventType { get; set; } = string.Empty;

    public string? EntityType { get; set; }
    public int? EntityId { get; set; }

    public int? DestinationId { get; set; }

    public string? Route { get; set; }

    public Dictionary<string, object?>? Metadata { get; set; }

    public DateTime? ClientOccurredAt { get; set; }
}
