namespace Geotrack.Application.DTOs.Admin;

public class AdminReviewListRequestDto
{
    public string? Status { get; set; }
    public string? Type { get; set; }
    public string? Search { get; set; }
    public string? Sort { get; set; }
    public int Skip { get; set; }
    public int Take { get; set; } = 25;
}

public class AdminReviewListResponseDto
{
    public List<AdminReviewListItemDto> Items { get; set; } = new();
    public int TotalCount { get; set; }
    public int Skip { get; set; }
    public int Take { get; set; }
    public int? NextSkip { get; set; }
    public bool HasMore { get; set; }
    public AdminReviewListCountsDto Counts { get; set; } = new();
}

public class AdminReviewListCountsDto
{
    public int ReportedCount { get; set; }
    public int ActiveCount { get; set; }
    public int AllCount { get; set; }
}

public class AdminReviewListItemDto
{
    public string ReviewKey { get; set; } = string.Empty;
    public int ReviewId { get; set; }
    public AdminReviewAuthorDto Author { get; set; } = new();
    public double Rating { get; set; }
    public int MaxRating { get; set; } = 5;
    public string Content { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string Status { get; set; } = "Active";
    public AdminReviewedEntityDto ReviewedEntity { get; set; } = new();
    public AdminReviewReportSummaryDto Reports { get; set; } = new();
    public AdminReviewPermissionsDto Permissions { get; set; } = new();
}

public class AdminReviewDetailDto
{
    public string ReviewKey { get; set; } = string.Empty;
    public int ReviewId { get; set; }
    public AdminReviewAuthorDto Author { get; set; } = new();
    public double Rating { get; set; }
    public int MaxRating { get; set; } = 5;
    public string Content { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string Status { get; set; } = "Active";
    public AdminReviewedEntityDto ReviewedEntity { get; set; } = new();
    public List<AdminReviewReportDto> AllReports { get; set; } = new();
    public AdminReviewDetailReportSummaryDto ReportSummary { get; set; } = new();
    public List<AdminReviewModerationHistoryDto> ModerationHistory { get; set; } = new();
    public AdminReviewPermissionsDto Permissions { get; set; } = new();
}

public class AdminReviewAuthorDto
{
    public int UserId { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? AvatarUrl { get; set; }
    public string Initials { get; set; } = string.Empty;
    public string Status { get; set; } = "Active";
}

public class AdminReviewedEntityDto
{
    public int EntityId { get; set; }
    public string EntityType { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Region { get; set; }
    public string? Location { get; set; }
    public string? Destination { get; set; }
}

public class AdminReviewReportSummaryDto
{
    public int ReportCount { get; set; }
    public int PendingReportCount { get; set; }
    public string? LatestReportReason { get; set; }
    public DateTime? LatestReportCreatedAt { get; set; }
    public string? LatestReportStatus { get; set; }
}

public class AdminReviewDetailReportSummaryDto
{
    public int TotalReports { get; set; }
    public int PendingReports { get; set; }
    public int AcceptedReports { get; set; }
    public int RejectedReports { get; set; }
}

public class AdminReviewReportDto
{
    public int ReportId { get; set; }
    public AdminReviewReporterDto Reporter { get; set; } = new();
    public string Reason { get; set; } = string.Empty;
    public string? Details { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime? DecidedAt { get; set; }
    public AdminReviewModeratorDto? DecidedBy { get; set; }
    public string? Decision { get; set; }
    public string? ModeratorNote { get; set; }
}

public class AdminReviewReporterDto
{
    public int UserId { get; set; }
    public string DisplayName { get; set; } = string.Empty;
}

public class AdminReviewModeratorDto
{
    public int UserId { get; set; }
    public string DisplayName { get; set; } = string.Empty;
}

public class AdminReviewModerationHistoryDto
{
    public string Action { get; set; } = string.Empty;
    public AdminReviewModeratorDto? Moderator { get; set; }
    public DateTime CreatedAt { get; set; }
    public string? Note { get; set; }
    public int? ReportId { get; set; }
}

public class AdminReviewPermissionsDto
{
    public bool CanDeleteReview { get; set; }
    public bool CanSuspendAuthor { get; set; }
    public bool CanUnsuspendAuthor { get; set; }
    public bool CanDeleteAuthor { get; set; }
    public bool CanRemoveReviewAndSuspendAuthor { get; set; }
    public bool CanRemoveReviewAndDeleteAuthor { get; set; }
}

public class AdminReviewModerationRequestDto
{
    public string? Note { get; set; }
}

public class AdminReviewActionResultDto
{
    public string ReviewKey { get; set; } = string.Empty;
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public AdminReviewListCountsDto Counts { get; set; } = new();
}
