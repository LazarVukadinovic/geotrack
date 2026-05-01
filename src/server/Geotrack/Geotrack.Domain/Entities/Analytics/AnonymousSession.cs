namespace Geotrack.Domain.Entities.Analytics;

public class AnonymousSession
{
    public Guid Id { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime LastSeenAt { get; set; }

    public string? EntryRoute { get; set; }
    public string? CoarseRegion { get; set; }
    public string? CoarseCountry { get; set; }

    public string UserAgentFamily { get; set; } = "other";
}
