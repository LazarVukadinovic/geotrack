namespace Geotrack.Application.DTOs.Admin;

public class RegionalMapResponseDto
{
    public RegionalMapViewportDto Viewport { get; set; } = new();
    public List<RegionalMapMarkerDto> Markers { get; set; } = new();
    public List<RegionalMapZoneDto> Zones { get; set; } = new();
    public string? EmptyStateMessage { get; set; }
}

public class RegionalMapViewportDto
{
    public double? CenterLatitude { get; set; }
    public double? CenterLongitude { get; set; }
    public double? Zoom { get; set; }
    public RegionalMapBoundsDto? Bounds { get; set; }
    public bool IsEmpty { get; set; }
}

public class RegionalMapBoundsDto
{
    public double North { get; set; }
    public double South { get; set; }
    public double East { get; set; }
    public double West { get; set; }
}

public class RegionalMapMarkerDto
{
    public int Id { get; set; }
    public string EntityType { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? Subtitle { get; set; }
    public string? CategoryLabel { get; set; }
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public string? ImageUrl { get; set; }
    public string Status { get; set; } = "Published";
    public int RegionId { get; set; }
    public int? DestinationId { get; set; }
    public int? LocationId { get; set; }
    public int? PlaceId { get; set; }
    public string? DetailRoute { get; set; }
    public string? EditRoute { get; set; }
}

public class RegionalMapZoneDto
{
    public int Id { get; set; }
    public string EntityType { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string? Status { get; set; }
    public RegionalMapGeometryDto? GeometryGeoJson { get; set; }
    public double? CenterLatitude { get; set; }
    public double? CenterLongitude { get; set; }
    public string? DetailRoute { get; set; }
    public string? EditRoute { get; set; }
}

public class RegionalMapGeometryDto
{
    public string Type { get; set; } = string.Empty;
    public object Coordinates { get; set; } = Array.Empty<object>();
}

public class RegionalMapSearchResponseDto
{
    public List<RegionalMapSearchResultDto> Items { get; set; } = new();
}

public class RegionalMapSearchResultDto
{
    public int? Id { get; set; }
    public string EntityType { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? Subtitle { get; set; }
    public string? CategoryLabel { get; set; }
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public string? ImageUrl { get; set; }
    public string? DetailRoute { get; set; }
}
