namespace Geotrack.Application.DTOs.Admin;

public class AdminDashboardDto
{
    public AdminDashboardCurrentUserDto CurrentUser { get; set; } = new();
    public AdminDashboardStatsDto Stats { get; set; } = new();
    public AdminDashboardQuickLinksDto QuickLinks { get; set; } = new();
    public AdminDashboardBadgesDto Badges { get; set; } = new();
}

public class AdminDashboardCurrentUserDto
{
    public int Id { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? AvatarUrl { get; set; }
    public string RoleDisplayName { get; set; } = "Administrator";
}

public class AdminDashboardStatsDto
{
    public int TerritoriesCount { get; set; }
    public int AssignedTerritoriesCount { get; set; }
    public int AppUsersCount { get; set; }
    public int SuspendedUsersCount { get; set; }
    public int ReviewsCount { get; set; }
    public int ReportedReviewsCount { get; set; }
    public int PendingReportsCount { get; set; }
    public int SupportTicketsCount { get; set; }
    public int OpenSupportTicketsCount { get; set; }
    public int InProgressSupportTicketsCount { get; set; }
    public int ResolvedSupportTicketsCount { get; set; }
    public int DismissedSupportTicketsCount { get; set; }
    public int UnresolvedSupportTicketsCount { get; set; }
    public int HighPrioritySupportTicketsCount { get; set; }
}

public class AdminDashboardQuickLinksDto
{
    public string TerritoryManagementRoute { get; set; } = "/admin-territories";
    public string UserManagementRoute { get; set; } = "/admin-users";
    public string ReviewManagementRoute { get; set; } = string.Empty;
    public string SupportTicketsRoute { get; set; } = "/admin-support-tickets";
    public string PendingReportsRoute { get; set; } = string.Empty;
}

public class AdminDashboardBadgesDto
{
    public int PendingReportsCount { get; set; }
    public int SupportReportsCount { get; set; }
}

public class ContentModeratorDashboardDto
{
    public AdminDashboardCurrentUserDto CurrentUser { get; set; } = new();
    public ContentModeratorDashboardStatsDto Stats { get; set; } = new();
    public ContentModeratorDashboardQuickLinksDto QuickLinks { get; set; } = new();
    public ContentModeratorDashboardBadgesDto Badges { get; set; } = new();
}

public class ContentModeratorDashboardStatsDto
{
    public int DestinationsCount { get; set; }
    public int LocationsCount { get; set; }
    public int PlacesCount { get; set; }
    public int EventsCount { get; set; }
    public int SuspendedDestinationsCount { get; set; }
    public int SuspendedLocationsCount { get; set; }
    public int SuspendedPlacesCount { get; set; }
    public int SuspendedEventsCount { get; set; }
    public int DeletedDestinationsCount { get; set; }
    public int DeletedLocationsCount { get; set; }
    public int DeletedPlacesCount { get; set; }
    public int DeletedEventsCount { get; set; }
    public int PendingReviewReportsCount { get; set; }
    public int ReportedReviewsCount { get; set; }
    public int ActiveReviewsCount { get; set; }
    public int ResolvedReportsCount { get; set; }
    public int RemovedReviewsCount { get; set; }
}

public class ContentModeratorDashboardQuickLinksDto
{
    public string DestinationsReviewRoute { get; set; } = "/content-moderator/destinations";
    public string LocationsReviewRoute { get; set; } = "/content-moderator/locations";
    public string PlacesReviewRoute { get; set; } = "/content-moderator/places";
    public string EventsReviewRoute { get; set; } = "/content-moderator/events";
    public string ReviewManagementRoute { get; set; } = "/content-moderator/review-management";
    public string PendingReportsRoute { get; set; } = "/content-moderator/review-management?status=reported";
}

public class ContentModeratorDashboardBadgesDto
{
    public int PendingReviewReportsCount { get; set; }
}
