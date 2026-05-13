namespace Geotrack.Application.DTOs.Admin;

public class RegionalPlaceListItemDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Summary { get; set; }
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
    public DateTime? LastUpdatedAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public string? ImageUrl { get; set; }
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
    public string? Subtitle { get; set; }
    public string? Description { get; set; }
    public string? Address { get; set; }
    public string? OpeningHours { get; set; }
    public string? ContactEmail { get; set; }
    public string? PhoneNumber { get; set; }
    public int? PlaceTypeId { get; set; }
    public int? StayTypeId { get; set; }
    public int? LocationId { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
}
