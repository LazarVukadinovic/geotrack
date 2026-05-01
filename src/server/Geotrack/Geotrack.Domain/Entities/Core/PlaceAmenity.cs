namespace Geotrack.Domain.Entities.Core;

public class PlaceAmenity
{
    public int PlaceId { get; set; }
    public Place Place { get; set; } = null!;

    public int AmenityId { get; set; }
    public Amenity Amenity { get; set; } = null!;
}
