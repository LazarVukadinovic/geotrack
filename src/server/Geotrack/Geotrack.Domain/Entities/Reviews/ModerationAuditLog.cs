using Geotrack.Domain.Entities.Users;

namespace Geotrack.Domain.Entities.Reviews;

public class ModerationAuditLog
{
    public int Id { get; set; }

    public int ModeratorUserId { get; set; }
    public User ModeratorUser { get; set; } = null!;

    public string TargetType { get; set; } = string.Empty;
    public int TargetId { get; set; }
    public string Action { get; set; } = string.Empty;
    public string? PreviousStatus { get; set; }
    public string? NewStatus { get; set; }
    public string? Reason { get; set; }
    public string? InternalNote { get; set; }
    public DateTime CreatedAt { get; set; }
    public string? CorrelationId { get; set; }
    public string? SourceArea { get; set; }
    public string? MetadataJson { get; set; }
}
