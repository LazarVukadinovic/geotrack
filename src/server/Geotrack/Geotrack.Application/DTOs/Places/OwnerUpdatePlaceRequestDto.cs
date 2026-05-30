namespace Geotrack.Application.DTOs.Places;

public class OwnerUpdatePlaceRequestDto
{
    public string? Name { get; set; }
    public string? Description { get; set; }
    public string? OpeningHours { get; set; }
    public string? Website { get; set; }
    public string? ContactEmail { get; set; }
    public string? PhoneNumber { get; set; }
    public string? AdmissionPriceRange { get; set; }
    public string? BookingExternalLink { get; set; }
    public List<string>? Tags { get; set; }
    public List<string>? Amenities { get; set; }
    public List<OwnerUpdateBookingPartnerDto>? BookingPartners { get; set; }
    public string? BestTimeToVisit { get; set; }
    public string? VisitorTips { get; set; }
    public string? AccessNotes { get; set; }
    public string? AccessibilityNotes { get; set; }
}

public class OwnerUpdateBookingPartnerDto
{
    public string Id { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
}
