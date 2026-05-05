namespace Geotrack.Domain.Entities.Core;

public class BookingPartner
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Variant { get; set; } = "dark"; // dark, brand, muted

    public ICollection<PlaceBookingPartner> PlaceBookingPartners { get; set; } = new List<PlaceBookingPartner>();
}
