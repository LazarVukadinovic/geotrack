namespace Geotrack.Application.DTOs.Destinations;

public class DestinationDiscoverySearchResponseDto
{
    public string Query { get; set; } = string.Empty;
    public List<DestinationDiscoverySearchItemDto> Items { get; set; } = new();
    public int TotalCount { get; set; }
    public int Skip { get; set; }
    public int Take { get; set; }
    public int NextSkip { get; set; }
    public bool HasMore { get; set; }
}

public class DestinationDiscoverySearchItemDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Summary { get; set; }
    public string? Description { get; set; }
    public string? ImageUrl { get; set; }
    public string DetailRoute { get; set; } = string.Empty;
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public int DestinationTypeId { get; set; }
    public string DestinationTypeName { get; set; } = string.Empty;
    public List<DestinationDiscoveryTagDto> Tags { get; set; } = new();
    public List<string> MatchedBy { get; set; } = new();
    public int MatchedLocationCount { get; set; }
    public int MatchedPlaceCount { get; set; }
    public double Score { get; set; }
}

public class DestinationDiscoveryTagDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public int? TagTypeId { get; set; }
    public string? TagTypeSlug { get; set; }
}

public class DestinationDiscoveryFiltersDto
{
    public List<DestinationDiscoveryDestinationTypeFilterDto> DestinationTypeFilters { get; set; } = new();
    public List<DestinationDiscoveryTagTypeFilterDto> TagTypeFilters { get; set; } = new();
}

public class DestinationDiscoveryDestinationTypeFilterDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int Count { get; set; }
}

public class DestinationDiscoveryTagTypeFilterDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public int Order { get; set; }
    public List<DestinationDiscoveryTagFilterOptionDto> Options { get; set; } = new();
}

public class DestinationDiscoveryTagFilterOptionDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public int Count { get; set; }
}
