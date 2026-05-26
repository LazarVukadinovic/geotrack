namespace Geotrack.Domain.Entities.Users;

public class AdminNotificationReadState
{
    public int AdminNotificationId { get; set; }
    public AdminNotification AdminNotification { get; set; } = null!;

    public int UserId { get; set; }
    public User User { get; set; } = null!;

    public DateTime ReadAt { get; set; }
}
