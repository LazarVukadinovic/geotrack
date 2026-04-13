namespace Geotrack.Domain.Entities.Core;

public class PlaceBookingPartner
{
    public int PlaceId { get; set; }
    public Place Place { get; set; } = null!;

    public int BookingPartnerId { get; set; }
    public BookingPartner BookingPartner { get; set; } = null!;
}
