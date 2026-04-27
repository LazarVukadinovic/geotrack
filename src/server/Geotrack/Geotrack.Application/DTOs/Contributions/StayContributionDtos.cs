namespace Geotrack.Application.DTOs.Contributions;

public class StayContributionListItemDto
{
    public int Id { get; set; }
    public string EntityType { get; set; } = "Stay";
    public string Name { get; set; } = string.Empty;
    public string StayType { get; set; } = string.Empty;
    public string Location { get; set; } = string.Empty;
    public string? CoverImageUrl { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTime SubmittedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public int? PublishedPlaceId { get; set; }
}

public class StayContributionDetailDto : StayContributionListItemDto
{
    public string Address { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string Region { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public string ImagePosition { get; set; } = "50% 50%";
    public string ImageSize { get; set; } = "cover";
    public string Description { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string ContactEmail { get; set; } = string.Empty;
    public string Website { get; set; } = string.Empty;
    public decimal? PricePerNight { get; set; }
    public List<string> Tags { get; set; } = new();
    public List<string> Amenities { get; set; } = new();
    public List<ContributionBookingPartnerDto> BookingPartners { get; set; } = new();
    public string? ReviewNote { get; set; }
    public DateTime? ReviewedAt { get; set; }
}

public class UpsertStayContributionDto
{
    public string Name { get; set; } = string.Empty;
    public string StayType { get; set; } = "Hotel";
    public string Address { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string Region { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public string ImagePosition { get; set; } = "50% 50%";
    public string ImageSize { get; set; } = "cover";
    public string Description { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string ContactEmail { get; set; } = string.Empty;
    public string Website { get; set; } = string.Empty;
    public decimal? PricePerNight { get; set; }
    public List<string> Tags { get; set; } = new();
    public List<string> Amenities { get; set; } = new();
    public List<ContributionBookingPartnerDto> BookingPartners { get; set; } = new();
}

public class ContributionBookingPartnerDto
{
    public string Id { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
}

public class StayContributionResponseDto
{
    public StayContributionDetailDto Contribution { get; set; } = new();
}

public class GetStayContributionsResponseDto
{
    public List<StayContributionListItemDto> Contributions { get; set; } = new();
}

public class ReviewStayContributionRequestDto
{
    public string? Note { get; set; }
}
