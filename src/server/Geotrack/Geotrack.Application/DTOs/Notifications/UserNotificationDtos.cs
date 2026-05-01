namespace Geotrack.Application.DTOs.Notifications;

public class UserNotificationDto
{
    public int Id { get; set; }
    public string Type { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string? RelatedEntityType { get; set; }
    public int? RelatedEntityId { get; set; }
    public string? ActionUrl { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? ReadAt { get; set; }
    public bool IsRead { get; set; }
}

public class GetUserNotificationsResponseDto
{
    public List<UserNotificationDto> Notifications { get; set; } = new();
    public int UnreadCount { get; set; }
    public int Skip { get; set; }
    public int Take { get; set; }
    public int NextSkip { get; set; }
    public bool HasMore { get; set; }
}

public class UserNotificationUnreadCountDto
{
    public int UnreadCount { get; set; }
}
