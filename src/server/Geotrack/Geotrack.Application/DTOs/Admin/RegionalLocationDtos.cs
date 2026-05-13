namespace Geotrack.Application.DTOs.Admin;

public class RegionalLocationListItemDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Summary { get; set; }
    public string? Description { get; set; }
    public string? LocationType { get; set; }
    public int? LocationTypeId { get; set; }
    public string Status { get; set; } = "Published";
    public int RegionId { get; set; }
    public string RegionDisplayName { get; set; } = string.Empty;
    public int DestinationId { get; set; }
    public string DestinationName { get; set; } = string.Empty;
    public DateTime? LastUpdatedAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public string? ImageUrl { get; set; }
    public string? DetailRoute { get; set; }
    public string? EditRoute { get; set; }
}

public class RegionalLocationDetailDto : RegionalLocationListItemDto
{
    public double Latitude { get; set; }
    public double Longitude { get; set; }
}

public class RegionalLocationListResponseDto
{
    public List<RegionalLocationListItemDto> Items { get; set; } = new();
    public int TotalCount { get; set; }
    public int Skip { get; set; }
    public int Take { get; set; }
}

public class RegionalLocationUpdateRequestDto
{
    public string? Name { get; set; }
    public int? DestinationId { get; set; }
    public int? LocationTypeId { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
}
