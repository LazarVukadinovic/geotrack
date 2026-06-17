namespace Geotrack.Domain.Entities.Users;

public class SupportRequest
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public string Category { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string Status { get; set; } = SupportRequestStatuses.Open;
    public string Priority { get; set; } = SupportRequestPriorities.Normal;
    public string? PageUrl { get; set; }
    public string? UserAgent { get; set; }
    public string? DeviceInfo { get; set; }
    public string? Browser { get; set; }
    public string? Platform { get; set; }
    public string? AttachmentUrl { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public DateTime? ResolvedAt { get; set; }
    public int? ResolvedByUserId { get; set; }
    public string? InternalNote { get; set; }
    public string? ResolutionMessage { get; set; }

    public User? User { get; set; }
    public User? ResolvedByUser { get; set; }
}
