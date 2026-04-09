namespace Geotrack.Domain.Entities;

public class LocationReview
{
    public int Id { get; set; }

    public int UserId { get; set; }
    public User User { get; set; } = null!;

    public int LocationId { get; set; }
    public Location Location { get; set; } = null!;

    public int Rating { get; set; } // 1-5
    public string Comment { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}