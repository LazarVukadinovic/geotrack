namespace Geotrack.Domain.Entities.Users;

public class UserNotification
{
    public int Id { get; set; }

    public int UserId { get; set; }
    public User User { get; set; } = null!;

    public string Type { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;

    public string? RelatedEntityType { get; set; }
    public int? RelatedEntityId { get; set; }
    public string? ActionUrl { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime? ReadAt { get; set; }
}
