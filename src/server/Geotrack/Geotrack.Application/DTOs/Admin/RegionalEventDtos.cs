namespace Geotrack.Application.DTOs.Admin;

public class RegionalEventListItemDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Summary { get; set; }
    public string? Description { get; set; }
    public string? InternalReferenceId { get; set; }
    public string? EventType { get; set; }
    public int? EventTypeId { get; set; }
    public string? Category { get; set; }
    public string Status { get; set; } = "Published";
    public string ParentEntityType { get; set; } = "Location";
    public int RegionId { get; set; }
    public string RegionDisplayName { get; set; } = string.Empty;
    public int DestinationId { get; set; }
    public string DestinationName { get; set; } = string.Empty;
    public int? LocationId { get; set; }
    public string? LocationName { get; set; }
    public int? PlaceId { get; set; }
    public string? PlaceName { get; set; }
    public string? Address { get; set; }
    public DateTime? StartsAt { get; set; }
    public DateTime? EndsAt { get; set; }
    public bool IsAllDay { get; set; }
    public bool IsRecurring { get; set; }
    public DateTime? LastUpdatedAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public int? CreatedByUserId { get; set; }
    public DateTime? PublishedAt { get; set; }
    public string? VenueName { get; set; }
    public string? TicketPrice { get; set; }
    public bool IsFree { get; set; }
    public string? ExternalTicketLink { get; set; }
    public string? Duration { get; set; }
    public string? HostedBy { get; set; }
    public string? ContactEmail { get; set; }
    public string? ContactPhone { get; set; }
    public int? AttendanceLimit { get; set; }
    public string? AgeRestriction { get; set; }
    public string? VenueDescription { get; set; }
    public string? ArrivalTips { get; set; }
    public string? Accessibility { get; set; }
    public List<RegionalEventTagDto> Tags { get; set; } = new();
    public string? ImageUrl { get; set; }
    public List<RegionalEntityImageDto> Images { get; set; } = new();
    public string? DetailRoute { get; set; }
    public string? EditRoute { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
}

public class RegionalEventDetailDto : RegionalEventListItemDto
{
    public bool CanDelete { get; set; }
}

public class RegionalEventListResponseDto
{
    public List<RegionalEventListItemDto> Items { get; set; } = new();
    public int TotalCount { get; set; }
    public int Skip { get; set; }
    public int Take { get; set; }
}

public class RegionalEventUpdateRequestDto
{
    public string? ParentEntityType { get; set; }
    public int? DestinationId { get; set; }
    public string? Name { get; set; }
    public string? Summary { get; set; }
    public string? Description { get; set; }
    public string? Status { get; set; }
    public int? EventTypeId { get; set; }
    public int? LocationId { get; set; }
    public int? PlaceId { get; set; }
    public string? VenueName { get; set; }
    public bool? IsAllDay { get; set; }
    public DateTime? StartsAt { get; set; }
    public DateTime? EndsAt { get; set; }
    public bool? IsRecurring { get; set; }
    public bool? IsFree { get; set; }
    public string? TicketPrice { get; set; }
    public string? ExternalTicketLink { get; set; }
    public string? Duration { get; set; }
    public string? HostedBy { get; set; }
    public string? OrganizerName { get; set; }
    public string? ContactEmail { get; set; }
    public string? ContactPhone { get; set; }
    public int? AttendanceLimit { get; set; }
    public bool ClearAttendanceLimit { get; set; }
    public string? AgeRestriction { get; set; }
    public string? VenueDescription { get; set; }
    public string? ArrivalTips { get; set; }
    public string? Accessibility { get; set; }
    public List<int>? TagIds { get; set; }
    public List<RegionalEntityImageInputDto>? Images { get; set; }
}

public record class RegionalEventCreateRequestDto
{
    public int RegionId { get; set; }
    public string? ParentEntityType { get; set; }
    public int? DestinationId { get; set; }
    public int? LocationId { get; set; }
    public int? PlaceId { get; set; }
    public int? EventTypeId { get; set; }
    public string? Name { get; set; }
    public string? Summary { get; set; }
    public string? Description { get; set; }
    public string? Status { get; set; }
    public string? VenueName { get; set; }
    public bool IsAllDay { get; set; }
    public DateTime? StartsAt { get; set; }
    public DateTime? EndsAt { get; set; }
    public bool IsRecurring { get; set; }
    public bool IsFree { get; set; }
    public string? TicketPrice { get; set; }
    public string? ExternalTicketLink { get; set; }
    public string? OrganizerName { get; set; }
    public string? ContactEmail { get; set; }
    public string? ContactPhone { get; set; }
    public int? AttendanceLimit { get; set; }
    public string? AgeRestriction { get; set; }
    public string? VenueDescription { get; set; }
    public string? ArrivalTips { get; set; }
    public string? Accessibility { get; set; }
    public List<int> TagIds { get; set; } = new();
    public List<RegionalEntityImageInputDto> Images { get; set; } = new();

    public string? CardImageUrl { get; set; }
    public string? CoverImageUrl { get; set; }
    public List<string> GalleryImageUrls { get; set; } = new();

    // Present only so accidental/tampered clients can be rejected explicitly.
    public string? InternalReferenceId { get; set; }
}

public class RegionalEventTagDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
}

public class RegionalEventTypeDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
}

public class RegionalEventParentDestinationDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Status { get; set; } = "Published";
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
}

public class RegionalEventParentLocationDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public int DestinationId { get; set; }
    public string DestinationName { get; set; } = string.Empty;
    public string Status { get; set; } = "Published";
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
}

public class RegionalEventParentPlaceDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public int LocationId { get; set; }
    public string LocationName { get; set; } = string.Empty;
    public int DestinationId { get; set; }
    public string DestinationName { get; set; } = string.Empty;
    public string? Address { get; set; }
    public string Status { get; set; } = "Published";
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
}
