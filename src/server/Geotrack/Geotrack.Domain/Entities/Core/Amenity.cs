namespace Geotrack.Domain.Entities.Core;

public class Amenity
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Icon { get; set; } = string.Empty; // pool, wifi, breakfast, parking, spa, gym, restaurant, ac

    public ICollection<PlaceAmenity> PlaceAmenities { get; set; } = new List<PlaceAmenity>();
}
