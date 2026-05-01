namespace Geotrack.Application.Analytics;

/// <summary>
/// Allowed values for AnalyticsEvent.EntityType. Hotel is intentionally distinct
/// from Place even though both back onto the Place table — the surfaces
/// (/stays/:id vs /places/:id) are analytically meaningful.
/// </summary>
public static class AnalyticsEntityTypes
{
    public const string Place = "place";
    public const string Hotel = "hotel";
    public const string Event = "event";
    public const string Destination = "destination";
    public const string Region = "region";
    public const string Collection = "collection";
    public const string AgentCard = "agent_card";

    public static readonly IReadOnlySet<string> AllowedSet = new HashSet<string>(StringComparer.Ordinal)
    {
        Place, Hotel, Event, Destination, Region, Collection, AgentCard,
    };

    public static bool IsAllowed(string? entityType) =>
        entityType is null || AllowedSet.Contains(entityType);
}
