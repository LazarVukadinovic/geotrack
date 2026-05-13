namespace Geotrack.Application.DTOs.Admin;

public class RegionalDestinationListItemDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Summary { get; set; }
    public string? Description { get; set; }
    public string Status { get; set; } = "Published";
    public int RegionId { get; set; }
    public string RegionDisplayName { get; set; } = string.Empty;
    public int DestinationTypeId { get; set; }
    public string? DestinationTypeName { get; set; }
    public DateTime? LastUpdatedAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public string? ImageUrl { get; set; }
    public string? DetailRoute { get; set; }
    public string? EditRoute { get; set; }
}

public class RegionalDestinationDetailDto : RegionalDestinationListItemDto
{
    public double Latitude { get; set; }
    public double Longitude { get; set; }
}

public class RegionalDestinationListResponseDto
{
    public List<RegionalDestinationListItemDto> Items { get; set; } = new();
    public int TotalCount { get; set; }
    public int Skip { get; set; }
    public int Take { get; set; }
}

public class RegionalDestinationUpdateRequestDto
{
    public string? Name { get; set; }
    public string? Description { get; set; }
    public int? DestinationTypeId { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
}
