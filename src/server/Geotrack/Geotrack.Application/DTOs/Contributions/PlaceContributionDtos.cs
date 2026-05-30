namespace Geotrack.Application.DTOs.Contributions;


public class ContributionPlaceTypeDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsOwnable { get; set; }
}

public class PlaceContributionImageDto
{
    public int Id { get; set; }
    public string Url { get; set; } = string.Empty;
    public string? AltText { get; set; }
    public string? Caption { get; set; }
    public bool IsMain { get; set; }
    public string ImageRole { get; set; } = "gallery";
    public int SortOrder { get; set; }
    public string ImagePosition { get; set; } = "50% 50%";
    public string ImageSize { get; set; } = "cover";
}

public class UpsertPlaceContributionImageDto
{
    public string Url { get; set; } = string.Empty;
    public string? AltText { get; set; }
    public string? Caption { get; set; }
    public bool IsMain { get; set; }
    public string ImageRole { get; set; } = "gallery";
    public int SortOrder { get; set; }
    public string ImagePosition { get; set; } = "50% 50%";
    public string ImageSize { get; set; } = "cover";
}

public class PlaceContributionListItemDto
{
    public int Id { get; set; }
    public string Type { get; set; } = string.Empty;
    public string EntityType { get; set; } = "Place";
    public string Name { get; set; } = string.Empty;
    public string? Subtitle { get; set; }
    public string? PlaceType { get; set; }
    public string? StayType { get; set; }
    public string? Location { get; set; }
    public string? CardImageUrl { get; set; }
    public string? MainImageUrl { get; set; }
    public string? CoverImageUrl { get; set; }
    public List<string> GalleryImageUrls { get; set; } = new();
    public string Status { get; set; } = string.Empty;
    public DateTime SubmittedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public int? PlaceId { get; set; }
    public int? PublishedPlaceId { get; set; }
    public bool HasOwnershipProofDocument { get; set; }
}

public class PlaceContributionDetailDto : PlaceContributionListItemDto
{
    public int? PlaceTypeId { get; set; }
    public int? LocationId { get; set; }
    public int? RestaurantTypeId { get; set; }
    public int? StayTypeId { get; set; }
    public string? Address { get; set; }
    public string? OpeningHours { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public string Description { get; set; } = string.Empty;
    public string? ContactEmail { get; set; }
    public string? PhoneNumber { get; set; }
    public string? Phone { get; set; }
    public string? Website { get; set; }
    public decimal? PricePerNight { get; set; }
    public string? CheckInTime { get; set; }
    public string? CheckOutTime { get; set; }
    public string? City { get; set; }
    public string? Region { get; set; }
    public string? Country { get; set; }
    public string ImagePosition { get; set; } = "50% 50%";
    public string ImageSize { get; set; } = "cover";
    public List<string> Tags { get; set; } = new();
    public List<string> Amenities { get; set; } = new();
    public List<string> StayAmenities { get; set; } = new();
    public List<ContributionBookingPartnerDto> BookingPartners { get; set; } = new();
    public List<PlaceContributionImageDto> Images { get; set; } = new();
    public string? ReviewNote { get; set; }
    public DateTime? ReviewedAt { get; set; }
    public string? OwnershipProofDocumentUrl { get; set; }
    public string? OwnershipProofOriginalFileName { get; set; }
    public string? OwnershipProofContentType { get; set; }
    public DateTime? OwnershipProofUploadedAt { get; set; }
    public bool HasPendingOwnershipClaim { get; set; }
}

public class UpsertPlaceContributionDto
{
    public string Type { get; set; } = "CreatePlace";
    public int? PlaceId { get; set; }
    public int? PlaceTypeId { get; set; }
    public string? PlaceType { get; set; }
    public int? LocationId { get; set; }
    public int? RestaurantTypeId { get; set; }
    public int? StayTypeId { get; set; }
    public string? StayType { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Subtitle { get; set; }
    public string? Address { get; set; }
    public string? OpeningHours { get; set; }
    public string Description { get; set; } = string.Empty;
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public string? ContactEmail { get; set; }
    public string? PhoneNumber { get; set; }
    public string? Phone { get; set; }
    public string? Website { get; set; }
    public decimal? PricePerNight { get; set; }
    public string? CheckInTime { get; set; }
    public string? CheckOutTime { get; set; }
    public string? City { get; set; }
    public string? Region { get; set; }
    public string? Country { get; set; }
    public string? ImagePosition { get; set; }
    public string? ImageSize { get; set; }
    public string? CardImagePosition { get; set; }
    public string? CardImageSize { get; set; }
    public List<string> Tags { get; set; } = new();
    public List<string> Amenities { get; set; } = new();
    public List<string> StayAmenities { get; set; } = new();
    public List<ContributionBookingPartnerDto> BookingPartners { get; set; } = new();
    public List<UpsertPlaceContributionImageDto>? Images { get; set; }
}

public class ContributionBookingPartnerDto
{
    public string Id { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
}

public class PlaceContributionResponseDto
{
    public PlaceContributionDetailDto Contribution { get; set; } = new();
}

public class GetPlaceContributionsResponseDto
{
    public List<PlaceContributionListItemDto> Contributions { get; set; } = new();
}

public class ReviewPlaceContributionRequestDto
{
    public string? Note { get; set; }
}