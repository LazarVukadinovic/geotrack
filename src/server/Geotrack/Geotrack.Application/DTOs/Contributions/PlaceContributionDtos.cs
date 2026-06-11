namespace Geotrack.Application.DTOs.Contributions;


public class ContributionPlaceTypeDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsOwnable { get; set; }
}

public class ContributionStayTypeDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
}

public class ContributionFeatureOptionDto
{
    public string Id { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
}

public class ContributionFeatureOptionsDto
{
    public List<ContributionFeatureOptionDto> Characteristics { get; set; } = new();
    public List<ContributionFeatureOptionDto> Amenities { get; set; } = new();
    public List<ContributionFeatureOptionDto> StayAmenities { get; set; } = new();
    public Dictionary<string, List<string>> CharacteristicIdsByPlaceType { get; set; } = new();
    public Dictionary<string, List<string>> AmenityIdsByPlaceType { get; set; } = new();
}

public class ResolveContributionLocationDto
{
    public int? LocationId { get; set; }
    public string? LocationName { get; set; }
    public string? DestinationName { get; set; }
    public string? RegionName { get; set; }
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
    public string? SubmittedByDisplayName { get; set; }
    public string? SubmittedByInitials { get; set; }
}

public class PlaceContributionDetailDto : PlaceContributionListItemDto
{
    public int? PlaceTypeId { get; set; }
    public int? LocationId { get; set; }
    public int? RestaurantTypeId { get; set; }
    public int? StayTypeId { get; set; }
    public string? Address { get; set; }
    public string? OpeningHours { get; set; }
    public bool OperatingHoursEnabled { get; set; }
    public List<ContributionOperatingHourDto> OperatingHours { get; set; } = new();
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
    public int? SuggestedRegionId { get; set; }
    public string? SuggestedRegionName { get; set; }
    public string? RegionDisplayName { get; set; }
    public string? SuggestedDestinationName { get; set; }
    public string? SuggestedLocationName { get; set; }
    public string ImagePosition { get; set; } = "50% 50%";
    public string ImageSize { get; set; } = "cover";
    public string? CardImagePosition { get; set; }
    public string? CardImageSize { get; set; }
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
    public string? SubmittedByDisplayName { get; set; }
    public string? SubmittedByInitials { get; set; }
    public int? CreatedDestinationId { get; set; }
    public int? CreatedLocationId { get; set; }
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
    public bool OperatingHoursEnabled { get; set; }
    public List<ContributionOperatingHourDto>? OperatingHours { get; set; }
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
    public string? OwnershipProofDocumentUrl { get; set; }
    public string? OwnershipProofOriginalFileName { get; set; }
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

public class ContributionOperatingHourDto
{
    public string DayOfWeek { get; set; } = string.Empty;
    public bool IsOpen { get; set; }
    public string? OpenTime { get; set; }
    public string? CloseTime { get; set; }
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

public class ContentManagerApproveContributionDto
{
    public int RegionId { get; set; }
    /// <summary>Use an existing destination in the region (takes precedence over NewDestinationName).</summary>
    public int? ExistingDestinationId { get; set; }
    /// <summary>Name for a new destination to create when no existing destination is chosen.</summary>
    public string? NewDestinationName { get; set; }
    /// <summary>Confirmed geographic centre for a newly created destination.</summary>
    public double? NewDestinationLatitude { get; set; }
    public double? NewDestinationLongitude { get; set; }
    /// <summary>Name for the location/area inside the chosen destination.</summary>
    public string? NewLocationName { get; set; }
    public string? Note { get; set; }
}

public class ContributionRegionOptionsDto
{
    public List<ContributionRegionOptionDto> Regions { get; set; } = new();
}

public class ContributionLocationOptionsDto
{
    public List<ContributionLocationOptionDto> Locations { get; set; } = new();
}

public class ContributionRegionOptionDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? TerritoryName { get; set; }
    public bool IsSuggested { get; set; }
}

public class ContributionLocationOptionDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int DestinationId { get; set; }
    public string DestinationName { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
}
