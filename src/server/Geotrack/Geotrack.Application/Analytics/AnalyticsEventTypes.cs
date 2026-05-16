namespace Geotrack.Application.Analytics;

/// <summary>
/// Canonical taxonomy of analytics event types. Stable strings — adding a new
/// constant is safe, renaming an existing one is a breaking change for stored
/// rows and admin read queries.
/// </summary>
public static class AnalyticsEventTypes
{
    public const string PageView = "page_view";
    public const string DetailView = "detail_view";
    public const string PageDuration = "page_duration";

    public const string SearchPerformed = "search_performed";
    public const string FilterApplied = "filter_applied";

    public const string MapMarkerOpened = "map_marker_opened";
    public const string DirectionsStarted = "directions_started";
    public const string OutboundLinkClicked = "outbound_link_clicked";

    public const string FavoriteAdded = "favorite_added";
    public const string FavoriteRemoved = "favorite_removed";

    public const string CollectionCreated = "collection_created";
    public const string CollectionItemAdded = "collection_item_added";
    public const string CollectionItemRemoved = "collection_item_removed";

    public const string ReviewCreated = "review_created";

    public const string ContributionCreated = "contribution_created";
    public const string ContributionUpdated = "contribution_updated";
    public const string ContributionDeleted = "contribution_deleted";

    public const string AgentPromptSent = "agent_prompt_sent";
    public const string AgentRecommendationOpened = "agent_recommendation_opened";

    public const string LoginCompleted = "login_completed";
    public const string RegisterCompleted = "register_completed";
    public const string InterestsCompleted = "interests_completed";
    public const string LogoutCompleted = "logout_completed";

    public static readonly IReadOnlySet<string> AllowedSet = new HashSet<string>(StringComparer.Ordinal)
    {
        PageView, DetailView, PageDuration,
        SearchPerformed, FilterApplied,
        MapMarkerOpened, DirectionsStarted, OutboundLinkClicked,
        FavoriteAdded, FavoriteRemoved,
        CollectionCreated, CollectionItemAdded, CollectionItemRemoved,
        ReviewCreated,
        ContributionCreated, ContributionUpdated, ContributionDeleted,
        AgentPromptSent, AgentRecommendationOpened,
        LoginCompleted, RegisterCompleted, InterestsCompleted, LogoutCompleted,
    };

    public static bool IsAllowed(string? eventType) =>
        !string.IsNullOrWhiteSpace(eventType) && AllowedSet.Contains(eventType);
}
