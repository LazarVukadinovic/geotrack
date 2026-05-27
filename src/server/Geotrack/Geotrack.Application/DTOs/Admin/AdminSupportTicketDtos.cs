namespace Geotrack.Application.DTOs.Admin;

public class AdminSupportTicketListRequestDto
{
    public string? Status { get; set; }
    public string? Type { get; set; }
    public string? Category { get; set; }
    public string? Priority { get; set; }
    public string? Search { get; set; }
    public string? Sort { get; set; }
    public int Skip { get; set; }
    public int Take { get; set; } = 25;
}

public class AdminSupportTicketListResponseDto
{
    public List<AdminSupportTicketListItemDto> Items { get; set; } = new();
    public int TotalCount { get; set; }
    public int Skip { get; set; }
    public int Take { get; set; }
    public int? NextSkip { get; set; }
    public bool HasMore { get; set; }
    public AdminSupportTicketListCountsDto Counts { get; set; } = new();
}

public class AdminSupportTicketListCountsDto
{
    public int OpenCount { get; set; }
    public int InProgressCount { get; set; }
    public int ResolvedCount { get; set; }
    public int DismissedCount { get; set; }
    public int AllCount { get; set; }
}

public class AdminSupportTicketListItemDto
{
    public int TicketId { get; set; }
    public string ReferenceCode { get; set; } = string.Empty;
    public AdminSupportTicketReporterDto Reporter { get; set; } = new();
    public string Category { get; set; } = string.Empty;
    public string CategoryLabel { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public string MessagePreview { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string Priority { get; set; } = string.Empty;
    public string? PageUrl { get; set; }
    public string? AppRoute { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public DateTime? ResolvedAt { get; set; }
    public bool HasAttachment { get; set; }
    public string? InternalNotePreview { get; set; }
    public AdminSupportTicketPermissionsDto Permissions { get; set; } = new();
}

public class AdminSupportTicketDetailDto
{
    public int TicketId { get; set; }
    public string ReferenceCode { get; set; } = string.Empty;
    public AdminSupportTicketReporterDto Reporter { get; set; } = new();
    public string Category { get; set; } = string.Empty;
    public string CategoryLabel { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string Priority { get; set; } = string.Empty;
    public string? AppRoute { get; set; }
    public string? PageUrl { get; set; }
    public string? UserAgent { get; set; }
    public string? DeviceInfo { get; set; }
    public string? Browser { get; set; }
    public string? Platform { get; set; }
    public string? AttachmentUrl { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public DateTime? ResolvedAt { get; set; }
    public AdminSupportTicketAdminSummaryDto? ResolvedBy { get; set; }
    public AdminSupportTicketAdminSummaryDto? AssignedAdmin { get; set; }
    public string? InternalNote { get; set; }
    public string? ResolutionMessage { get; set; }
    public List<AdminSupportTicketHistoryItemDto> StatusHistory { get; set; } = new();
    public AdminSupportTicketPermissionsDto Permissions { get; set; } = new();
}

public class AdminSupportTicketReporterDto
{
    public int? UserId { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? AvatarUrl { get; set; }
    public string Initials { get; set; } = string.Empty;
    public bool IsAnonymous { get; set; }
}

public class AdminSupportTicketAdminSummaryDto
{
    public int UserId { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
}

public class AdminSupportTicketHistoryItemDto
{
    public string Action { get; set; } = string.Empty;
    public AdminSupportTicketAdminSummaryDto? Actor { get; set; }
    public DateTime CreatedAt { get; set; }
    public string? Note { get; set; }
}

public class AdminSupportTicketPermissionsDto
{
    public bool CanView { get; set; }
    public bool CanMarkInProgress { get; set; }
    public bool CanResolve { get; set; }
    public bool CanDismiss { get; set; }
    public bool CanReopen { get; set; }
    public bool CanAddNote { get; set; }
}

public class AdminSupportTicketStatusUpdateRequestDto
{
    public string Status { get; set; } = string.Empty;
    public string? InternalNote { get; set; }
    public string? ResolutionMessage { get; set; }
}

public class AdminSupportTicketNoteRequestDto
{
    public string? InternalNote { get; set; }
}
