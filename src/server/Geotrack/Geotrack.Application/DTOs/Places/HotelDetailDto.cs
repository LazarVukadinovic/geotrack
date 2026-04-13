using Geotrack.Application.DTOs.Reviews;

namespace Geotrack.Application.DTOs.Places;

public class HotelDetailDto
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Location { get; set; }
    public string? HeroImage { get; set; }
    public List<TagLabelDto> Tags { get; set; } = new();
    public double Rating { get; set; }
    public string ReviewCount { get; set; } = "0";
    public string? Distance { get; set; }
    public decimal PricePerNight { get; set; }
    public string? About { get; set; }
    public List<BookingPartnerDto> BookingPartners { get; set; } = new();
    public List<AmenityDto> Amenities { get; set; } = new();
    public List<ReviewDto> Reviews { get; set; } = new();
    public List<SimilarPlaceDto> Similar { get; set; } = new();
    public bool IsSaved { get; set; }
}

public class BookingPartnerDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Variant { get; set; } = "dark";
}

public class AmenityDto
{
    public int Id { get; set; }
    public string Label { get; set; } = string.Empty;
    public string Icon { get; set; } = string.Empty;
}
