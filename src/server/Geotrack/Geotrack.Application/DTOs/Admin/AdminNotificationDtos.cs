namespace Geotrack.Application.DTOs.Admin;

public class AdminNotificationListRequestDto
{
    public bool IncludeRead { get; set; } = true;
    public int Skip { get; set; }
    public int Take { get; set; } = 25;
}

public class AdminNotificationListDto
{
    public List<AdminNotificationDto> Items { get; set; } = new();
    public int UnreadCount { get; set; }
    public int TotalCount { get; set; }
    public int Skip { get; set; }
    public int Take { get; set; }
    public int? NextSkip { get; set; }
    public bool HasMore { get; set; }
}

public class AdminNotificationDto
{
    public int Id { get; set; }
    public string Type { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string Severity { get; set; } = string.Empty;
    public bool IsRead { get; set; }
    public DateTime? ReadAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public string TargetType { get; set; } = string.Empty;
    public int? TargetId { get; set; }
    public string? TargetRoute { get; set; }
    public string? MetadataSummary { get; set; }
}

public class AdminNotificationUnreadCountDto
{
    public int UnreadCount { get; set; }
}

public class AdminNotificationMutationDto
{
    public int UnreadCount { get; set; }
    public DateTime? ReadAt { get; set; }
}
