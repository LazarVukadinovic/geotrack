namespace Geotrack.Domain.Entities;

public class PlaceReview
{
    public int Id { get; set; }

    public int UserId { get; set; }
    public User User { get; set; } = null!;

    public int PlaceId { get; set; }
    public Place Place { get; set; } = null!;

    public int Rating { get; set; } // 1-5
    public string Comment { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}