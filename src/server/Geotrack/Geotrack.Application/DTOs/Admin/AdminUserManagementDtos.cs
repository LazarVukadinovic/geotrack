namespace Geotrack.Application.DTOs.Admin;

public class AdminUserListRequestDto
{
    public string? Tab { get; set; }
    public string? Search { get; set; }
    public string? Status { get; set; }
    public string? Sort { get; set; }
    public int Skip { get; set; }
    public int Take { get; set; } = 25;
}

public class AdminUserListResponseDto
{
    public List<AdminUserListItemDto> Items { get; set; } = new();
    public int TotalCount { get; set; }
    public int Skip { get; set; }
    public int Take { get; set; }
    public int? NextSkip { get; set; }
    public bool HasMore { get; set; }
    public AdminUserListCountsDto Counts { get; set; } = new();
}

public class AdminUserListCountsDto
{
    public int AppUsersCount { get; set; }
    public int TerritoryManagersCount { get; set; }
    public int ActiveCount { get; set; }
    public int SuspendedCount { get; set; }
    public int DeletedCount { get; set; }
}

public class AdminUserListItemDto
{
    public int UserId { get; set; }
    public string Id { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? AvatarUrl { get; set; }
    public string Initials { get; set; } = string.Empty;
    public string Status { get; set; } = "Active";
    public List<string> Roles { get; set; } = new();
    public bool IsAdmin { get; set; }
    public bool IsCurrentUser { get; set; }
    public int ReviewsCount { get; set; }
    public int ReportedCount { get; set; }
    public DateTime JoinedAt { get; set; }
    public DateTime? LastActiveAt { get; set; }
    public List<AdminUserAssignedTerritoryDto> AssignedTerritories { get; set; } = new();
    public int AssignedTerritoryCount { get; set; }
    public bool CanAssignTerritoryManager { get; set; }
    public bool CanSuspend { get; set; }
    public bool CanUnsuspend { get; set; }
    public bool CanDelete { get; set; }
}

public class AdminUserAssignedTerritoryDto
{
    public int TerritoryId { get; set; }
    public string TerritoryName { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string? CountryCode { get; set; }
}

public class AdminUserAssignTerritoryManagerRequestDto
{
    public int TerritoryId { get; set; }
}

public class AdminUserPreviewDto
{
    public AdminUserPreviewProfileDto User { get; set; } = new();
    public List<AdminUserRoleDto> Roles { get; set; } = new();
    public AdminUserAccountSummaryDto AccountSummary { get; set; } = new();
    public AdminUserPreviewSectionDto<AdminUserReportPreviewDto> ReportsMade { get; set; } = new();
    public AdminUserPreviewSectionDto<AdminUserReportPreviewDto> ReportsAgainstUser { get; set; } = new();
    public AdminUserPreviewSectionDto<AdminUserRecentReviewDto> RecentReviews { get; set; } = new();
    public AdminUserPreviewSectionDto<AdminUserModerationHistoryItemDto> ModerationHistory { get; set; } = new();
    public AdminUserPreviewPermissionsDto Permissions { get; set; } = new();
}

public class AdminUserPreviewProfileDto
{
    public int UserId { get; set; }
    public string Id { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? AvatarUrl { get; set; }
    public string Initials { get; set; } = string.Empty;
    public string Status { get; set; } = "Active";
    public List<string> Roles { get; set; } = new();
    public bool IsCurrentUser { get; set; }
    public bool IsAdmin { get; set; }
    public bool HasElevatedAccess { get; set; }
    public string AccountType { get; set; } = "App User";
    public DateTime JoinedAt { get; set; }
    public DateTime? LastActiveAt { get; set; }
    public DateTime? LastLoginAt { get; set; }
    public List<AdminUserAssignedTerritoryDto> AssignedTerritories { get; set; } = new();
    public List<AdminUserAssignedRegionDto> AssignedRegions { get; set; } = new();
}

public class AdminUserAssignedRegionDto
{
    public int RegionId { get; set; }
    public string RegionName { get; set; } = string.Empty;
    public int TerritoryId { get; set; }
    public string TerritoryName { get; set; } = string.Empty;
    public string? CountryCode { get; set; }
    public DateTime? AssignedAt { get; set; }
}

public class AdminUserRoleDto
{
    public string RoleName { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string AssignedScope { get; set; } = string.Empty;
    public DateTime? AssignedAt { get; set; }
    public List<AdminUserAccessScopeDto> Scopes { get; set; } = new();
}

public class AdminUserAccessScopeDto
{
    public string ScopeType { get; set; } = string.Empty;
    public int? ScopeId { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string? CountryCode { get; set; }
    public DateTime? AssignedAt { get; set; }
}

public class AdminUserAccountSummaryDto
{
    public int ReviewsCount { get; set; }
    public int ReportsMadeCount { get; set; }
    public int ReportedContentCount { get; set; }
    public int PendingReportsAgainstUserCount { get; set; }
    public int AcceptedReportsAgainstUserCount { get; set; }
    public int RejectedReportsAgainstUserCount { get; set; }
    public int ContributionsCount { get; set; }
    public int PendingContributionsCount { get; set; }
    public int FavoritesCount { get; set; }
    public int CollectionsCount { get; set; }
    public int SavedItemsCount { get; set; }
    public int OwnedPlacesCount { get; set; }
    public int ActivityEventsCount { get; set; }
    public int KnownSuspensionCount { get; set; }
    public DateTime? LastKnownActivityAt { get; set; }
    public string RiskLevel { get; set; } = "Low";
    public string RiskSummary { get; set; } = "No pending moderation risk.";
}

public class AdminUserPreviewSectionDto<T>
{
    public List<T> Items { get; set; } = new();
    public int TotalCount { get; set; }
    public int Skip { get; set; }
    public int Take { get; set; }
    public bool HasMore { get; set; }
}

public class AdminUserReportPreviewDto
{
    public int ReportId { get; set; }
    public string ContentType { get; set; } = "Review";
    public string TargetType { get; set; } = string.Empty;
    public int ReviewId { get; set; }
    public int EntityId { get; set; }
    public string EntityName { get; set; } = string.Empty;
    public string RegionName { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? Decision { get; set; }
    public string ActionTaken { get; set; } = string.Empty;
    public string ContentExcerpt { get; set; } = string.Empty;
    public int ReporterUserId { get; set; }
    public string ReporterName { get; set; } = string.Empty;
    public int ReviewAuthorUserId { get; set; }
    public string ReviewAuthorName { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime? ModerationDecisionDate { get; set; }
    public string? DetailRoute { get; set; }
}

public class AdminUserRecentReviewDto
{
    public int ReviewId { get; set; }
    public string TargetType { get; set; } = string.Empty;
    public int EntityId { get; set; }
    public string EntityName { get; set; } = string.Empty;
    public double Rating { get; set; }
    public string CommentExcerpt { get; set; } = string.Empty;
    public int ReportCount { get; set; }
    public string Status { get; set; } = "Visible";
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? DetailRoute { get; set; }
}

public class AdminUserModerationHistoryItemDto
{
    public string EventId { get; set; } = string.Empty;
    public string EventType { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public int? ActorUserId { get; set; }
    public string? ActorDisplayName { get; set; }
    public DateTime CreatedAt { get; set; }
    public string? Note { get; set; }
    public string Source { get; set; } = string.Empty;
    public int? SourceId { get; set; }
}

public class AdminUserPreviewPermissionsDto
{
    public bool CanAssignTerritoryManager { get; set; }
    public bool CanSuspend { get; set; }
    public bool CanUnsuspend { get; set; }
    public bool CanDelete { get; set; }
    public bool CanViewReports { get; set; }
    public bool CanViewModerationHistory { get; set; }
}
