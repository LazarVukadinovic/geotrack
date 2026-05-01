namespace Geotrack.Domain.Entities.Users;

public class Favorite
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public User User { get; set; } = null!;

    public string EntityType { get; set; } = string.Empty; // "place", "event"
    public int EntityId { get; set; }

    public DateTime CreatedAt { get; set; }
}
