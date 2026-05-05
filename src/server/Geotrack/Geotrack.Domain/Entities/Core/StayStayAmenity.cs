namespace Geotrack.Domain.Entities.Core;

public class StayStayAmenity
{
    public int StayPlaceId { get; set; }
    public Stay Stay { get; set; } = null!;

    public int StayAmenityId { get; set; }
    public StayAmenity StayAmenity { get; set; } = null!;
}