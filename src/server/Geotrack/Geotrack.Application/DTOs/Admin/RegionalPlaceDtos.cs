namespace Geotrack.Application.DTOs.Admin;

public class RegionalPlaceListItemDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Summary { get; set; }
    public string? InternalReferenceId { get; set; }
    public string? Description { get; set; }
    public string? Subtitle { get; set; }
    public string? Address { get; set; }
    public string? PlaceType { get; set; }
    public int? PlaceTypeId { get; set; }
    public string? StayType { get; set; }
    public int? StayTypeId { get; set; }
    public string Status { get; set; } = "Published";
    public int RegionId { get; set; }
    public string RegionDisplayName { get; set; } = string.Empty;
    public int DestinationId { get; set; }
    public string DestinationName { get; set; } = string.Empty;
    public int LocationId { get; set; }
    public string LocationName { get; set; } = string.Empty;
    public int? CreatedByUserId { get; set; }
    public List<RegionalPlaceTagDto> Tags { get; set; } = new();
    public DateTime? LastUpdatedAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? PublishedAt { get; set; }
    public string? ImageUrl { get; set; }
    public List<RegionalEntityImageDto> Images { get; set; } = new();
    public string? DetailRoute { get; set; }
    public string? EditRoute { get; set; }
}

public class RegionalPlaceDetailDto : RegionalPlaceListItemDto
{
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public string? OpeningHours { get; set; }
    public string? ContactEmail { get; set; }
    public string? PhoneNumber { get; set; }
    public string? Website { get; set; }
    public string? AdmissionPriceRange { get; set; }
    public string? BookingExternalLink { get; set; }
    public string? BestTimeToVisit { get; set; }
    public string? VisitorTips { get; set; }
    public string? AccessNotes { get; set; }
    public string? AccessibilityNotes { get; set; }
    public bool OperatingHoursEnabled { get; set; }
    public List<RegionalPlaceOperatingHourDto> OperatingHours { get; set; } = new();
}

public class RegionalPlaceListResponseDto
{
    public List<RegionalPlaceListItemDto> Items { get; set; } = new();
    public int TotalCount { get; set; }
    public int Skip { get; set; }
    public int Take { get; set; }
}

public class RegionalPlaceUpdateRequestDto
{
    public string? Name { get; set; }
    public string? Summary { get; set; }
    public string? Subtitle { get; set; }
    public string? Description { get; set; }
    public string? Status { get; set; }
    public string? Address { get; set; }
    public string? StreetAddress { get; set; }
    public string? OpeningHours { get; set; }
    public string? ContactEmail { get; set; }
    public string? PhoneNumber { get; set; }
    public string? ContactPhone { get; set; }
    public string? Website { get; set; }
    public string? AdmissionPriceRange { get; set; }
    public string? BookingExternalLink { get; set; }
    public string? BestTimeToVisit { get; set; }
    public string? VisitorTips { get; set; }
    public string? AccessNotes { get; set; }
    public string? AccessibilityNotes { get; set; }
    public bool? OperatingHoursEnabled { get; set; }
    public List<RegionalPlaceOperatingHourDto>? OperatingHours { get; set; }
    public int? PlaceTypeId { get; set; }
    public int? StayTypeId { get; set; }
    public int? LocationId { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public List<int>? TagIds { get; set; }
    public List<RegionalEntityImageInputDto>? Images { get; set; }
}

public class RegionalPlaceCreateRequestDto
{
    public int RegionId { get; set; }
    public int LocationId { get; set; }
    public int? PlaceTypeId { get; set; }
    public int? StayTypeId { get; set; }
    public string? Name { get; set; }
    public string? Summary { get; set; }
    public string? Description { get; set; }
    public string? Status { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public string? StreetAddress { get; set; }
    public string? BestTimeToVisit { get; set; }
    public string? VisitorTips { get; set; }
    public string? AccessNotes { get; set; }
    public string? AccessibilityNotes { get; set; }
    public string? Website { get; set; }
    public string? ContactPhone { get; set; }
    public string? AdmissionPriceRange { get; set; }
    public string? BookingExternalLink { get; set; }
    public bool OperatingHoursEnabled { get; set; }
    public List<RegionalPlaceOperatingHourDto> OperatingHours { get; set; } = new();
    public List<int> TagIds { get; set; } = new();
    public List<RegionalEntityImageInputDto> Images { get; set; } = new();

    // Present only so accidental/tampered clients can be rejected explicitly.
    public string? InternalReferenceId { get; set; }
}

public class RegionalPlaceOperatingHourDto
{
    public string DayOfWeek { get; set; } = string.Empty;
    public bool IsOpen { get; set; }
    public string? OpenTime { get; set; }
    public string? CloseTime { get; set; }
}

public class RegionalPlaceParentLocationDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public int DestinationId { get; set; }
    public string DestinationName { get; set; } = string.Empty;
    public string Status { get; set; } = "Published";
}

public class RegionalPlaceTagDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
}

public class RegionalPlaceTypeDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
}
