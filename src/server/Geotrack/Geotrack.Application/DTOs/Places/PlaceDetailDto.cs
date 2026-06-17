using Geotrack.Application.DTOs.Images;
using Geotrack.Application.DTOs.Reviews;

namespace Geotrack.Application.DTOs.Places;

public class PlaceDetailDto
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Location { get; set; }
    public int? LocationId { get; set; }
    public string? LocationName { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public string? HeroImage { get; set; }
    public List<EntityImageDto> Images { get; set; } = new();
    public List<TagLabelDto> Tags { get; set; } = new();
    public double? Rating { get; set; }
    public string? ReviewCount { get; set; }
    public string? Distance { get; set; }
    public string? OpenStatus { get; set; }
    public string? OpeningHours { get; set; }
    public bool OperatingHoursEnabled { get; set; }
    public List<PlaceOperatingHourDto>? OperatingHours { get; set; }
    public List<OpeningHoursDayDto>? OpeningHoursSchedule { get; set; }
    public string? Website { get; set; }
    public string? About { get; set; }
    public List<ReviewDto> Reviews { get; set; } = new();
    public List<PlaceRelatedEventDto> RelatedEvents { get; set; } = new();
    public List<SimilarPlaceDto> Similar { get; set; } = new();
    public List<SimilarPlaceDto> SimilarInDestination { get; set; } = new();
    public List<SimilarPlaceDto> SimilarInTerritory { get; set; } = new();
    public bool IsSaved { get; set; }
    public bool IsOwnable { get; set; }
    public bool IsOwnedByCurrentUser { get; set; }
    public bool HasPendingOwnershipClaim { get; set; }
    public int? OwnerContributionId { get; set; }
    public string ContentType { get; set; } = "place";
    public string? Category { get; set; }
    public string? ContactEmail { get; set; }
    public string? PhoneNumber { get; set; }
    public string? ContactPhone { get; set; }
    public string? AdmissionPriceRange { get; set; }
    public string? BookingExternalLink { get; set; }
    public List<AmenityDto> Amenities { get; set; } = new();
    public string? BestTimeToVisit { get; set; }
    public string? VisitorTips { get; set; }
    public string? AccessNotes { get; set; }
    public string? AccessibilityNotes { get; set; }
}

public class OpeningHoursDayDto
{
    public string Day { get; set; } = string.Empty;
    public string Hours { get; set; } = string.Empty;
}

public class PlaceOperatingHourDto
{
    public string DayOfWeek { get; set; } = string.Empty;
    public bool IsOpen { get; set; }
    public string? OpenTime { get; set; }
    public string? CloseTime { get; set; }
}

public class TagLabelDto
{
    public string Label { get; set; } = string.Empty;
}

public class PlaceRelatedEventDto
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Subtitle { get; set; }
    public string? Image { get; set; }
}

public class SimilarPlaceDto
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Image { get; set; }
    public double? Rating { get; set; }
    public string? Distance { get; set; }
    public int? DestinationId { get; set; }
    public string? DestinationName { get; set; }
    public bool IsSaved { get; set; }
}
