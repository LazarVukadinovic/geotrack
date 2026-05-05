using Geotrack.Domain.Entities.Core;

namespace Geotrack.Domain.Entities.Users;

public class UserPlace
{
    public int UserId { get; set; }
    public User User { get; set; } = null!;

    public int PlaceId { get; set; }
    public Place Place { get; set; } = null!;

    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}