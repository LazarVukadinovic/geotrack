namespace Geotrack.Application.DTOs.Admin;

public class RegionalDestinationBoundarySuggestionDto
{
    public string Source { get; set; } = string.Empty;
    public string? ExternalKey { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string? Category { get; set; }
    public string? ProviderType { get; set; }
    public string GeometryType { get; set; } = string.Empty;
    public string GeoJson { get; set; } = string.Empty;
    public double CenterLatitude { get; set; }
    public double CenterLongitude { get; set; }
}

public class RegionalDestinationBoundarySuggestionsDto
{
    public List<RegionalDestinationBoundarySuggestionDto> Items { get; set; } = new();
}

public class DestinationCoordinateResolutionDto
{
    public bool IsResolved { get; set; }
    public bool IsAmbiguous { get; set; }
    public int? DestinationId { get; set; }
    public string? DestinationName { get; set; }
    public int? RegionId { get; set; }
    public string? RegionName { get; set; }
    public string Message { get; set; } = string.Empty;
}
