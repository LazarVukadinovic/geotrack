namespace Geotrack.Application.DTOs.Admin;

public class RegionalEventListItemDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Summary { get; set; }
    public string? Description { get; set; }
    public string? EventType { get; set; }
    public int? EventTypeId { get; set; }
    public string? Category { get; set; }
    public string Status { get; set; } = "Published";
    public int RegionId { get; set; }
    public string RegionDisplayName { get; set; } = string.Empty;
    public int DestinationId { get; set; }
    public string DestinationName { get; set; } = string.Empty;
    public int LocationId { get; set; }
    public string LocationName { get; set; } = string.Empty;
    public int? PlaceId { get; set; }
    public string? PlaceName { get; set; }
    public string? Address { get; set; }
    public DateTime? StartsAt { get; set; }
    public DateTime? EndsAt { get; set; }
    public DateTime? LastUpdatedAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public string? TicketPrice { get; set; }
    public string? Duration { get; set; }
    public string? HostedBy { get; set; }
    public string? VenueDescription { get; set; }
    public string? ArrivalTips { get; set; }
    public string? Accessibility { get; set; }
    public string? ImageUrl { get; set; }
    public string? DetailRoute { get; set; }
    public string? EditRoute { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
}

public class RegionalEventDetailDto : RegionalEventListItemDto
{
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
    public string? Name { get; set; }
    public string? Description { get; set; }
    public int? EventTypeId { get; set; }
    public int? LocationId { get; set; }
    public int? PlaceId { get; set; }
    public DateTime? StartsAt { get; set; }
    public DateTime? EndsAt { get; set; }
    public string? TicketPrice { get; set; }
    public string? Duration { get; set; }
    public string? HostedBy { get; set; }
    public string? VenueDescription { get; set; }
    public string? ArrivalTips { get; set; }
    public string? Accessibility { get; set; }
}
