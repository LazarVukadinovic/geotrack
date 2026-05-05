namespace Geotrack.Domain.Entities.Core;

public class Stay
{
    public int PlaceId { get; set; }
    public Place Place { get; set; } = null!;

    public int StayTypeId { get; set; }
    public StayType StayType { get; set; } = null!;

    public decimal? PricePerNight { get; set; }

    public string? CheckInTime { get; set; }
    public string? CheckOutTime { get; set; }

    public ICollection<StayStayAmenity> StayAmenities { get; set; } = new List<StayStayAmenity>();

    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}