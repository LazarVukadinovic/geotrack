namespace Geotrack.Domain.Entities.Users;

public class AdminNotification
{
    public int Id { get; set; }

    public string AudienceRole { get; set; } = "Admin";
    public string Type { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string Severity { get; set; } = "Info";

    public string TargetType { get; set; } = "System";
    public int? TargetId { get; set; }
    public string? TargetRoute { get; set; }
    public string? MetadataJson { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }

    public ICollection<AdminNotificationReadState> ReadStates { get; set; } = new List<AdminNotificationReadState>();
}
