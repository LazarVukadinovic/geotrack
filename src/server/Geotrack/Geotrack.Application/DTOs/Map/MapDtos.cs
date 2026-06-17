namespace Geotrack.Application.DTOs.Map;

public class MapSearchResultDto
{
    public int? Id { get; set; }
    public string? Type { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? Subtitle { get; set; }
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public string? ParentName { get; set; }
}

public class MapReverseGeocodeDto
{
    public string Address { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string Region { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;
    public string DisplayLabel { get; set; } = string.Empty;
}

public class MapRouteResponseDto
{
    public List<MapCoordinateDto> Coordinates { get; set; } = new();
    public double? DistanceMeters { get; set; }
    public double? DurationSeconds { get; set; }
}

public class MapCoordinateDto
{
    public double Latitude { get; set; }
    public double Longitude { get; set; }
}

public class GetMapEntitiesResponseDto
{
    public string? FocusLabel { get; set; }
    public List<MapEntityMarkerDto> Markers { get; set; } = new();
}

public class GetMapMarkersResponseDto
{
    public string Mode { get; set; } = string.Empty;
    public List<MapEntityMarkerDto> Markers { get; set; } = new();
    public List<MapFilterOptionDto> RecommendedTags { get; set; } = new();
}

public class MapFilterOptionDto
{
    public int Id { get; set; }
    public string Label { get; set; } = string.Empty;
    public int Count { get; set; }
    public double Score { get; set; }
}

public class MapEntityMarkerDto
{
    public string EntityType { get; set; } = string.Empty;
    public int EntityId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Subtitle { get; set; }
    public string? CategoryLabel { get; set; }
    public int? PlaceTypeId { get; set; }
    public string? PlaceTypeLabel { get; set; }
    public List<int> TagIds { get; set; } = new();
    public List<string> TagLabels { get; set; } = new();
    public string? ImageUrl { get; set; }
    public string? DetailRoute { get; set; }
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public string? Distance { get; set; }
    public bool IsSaved { get; set; }
}
