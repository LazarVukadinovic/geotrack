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
}

public class AdminDashboardQuickLinksDto
{
    public string TerritoryManagementRoute { get; set; } = "/admin-territories";
    public string UserManagementRoute { get; set; } = "/admin-users";
    public string ReviewManagementRoute { get; set; } = "/admin-reviews";
    public string PendingReportsRoute { get; set; } = "/admin-reviews?status=reported";
}

public class AdminDashboardBadgesDto
{
    public int PendingReportsCount { get; set; }
}
