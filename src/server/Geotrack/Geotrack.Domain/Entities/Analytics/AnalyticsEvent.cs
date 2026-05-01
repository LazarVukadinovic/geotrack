using Geotrack.Domain.Entities.Core;
using Geotrack.Domain.Entities.Users;

namespace Geotrack.Domain.Entities.Analytics;

public class AnalyticsEvent
{
    public int Id { get; set; }

    public int? UserId { get; set; }
    public User? User { get; set; }

    public Guid? AnonymousSessionId { get; set; }
    public AnonymousSession? AnonymousSession { get; set; }

    public string EventType { get; set; } = string.Empty;

    public string? EntityType { get; set; }
    public int? EntityId { get; set; }

    public int? DestinationId { get; set; }
    public Destination? Destination { get; set; }

    public string? Route { get; set; }

    public string MetadataJson { get; set; } = "{}";

    public DateTime OccurredAt { get; set; }
    public DateTime? ClientOccurredAt { get; set; }
}
