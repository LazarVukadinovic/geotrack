namespace Geotrack.Application.DTOs.Explore;

public class ExploreSearchResponseDto
{
    public int DestinationId { get; set; }
    public string Query { get; set; } = string.Empty;
    public List<ExploreSearchResultItemDto> Items { get; set; } = new();
    public int TotalCount { get; set; }
    public int Skip { get; set; }
    public int Take { get; set; }
    public int NextSkip { get; set; }
    public bool HasMore { get; set; }
}

public class ExploreSearchResultItemDto
{
    public int Id { get; set; }

    // Internal type. Frontend may still display this as "Place" or "Area".
    public string EntityType { get; set; } = string.Empty; // place, location

    // More precise UI kind. Stay is still a place, but routes to /stays/{id}.
    public string PlaceKind { get; set; } = string.Empty; // place, stay, area

    public string DisplayType { get; set; } = string.Empty; // Place, Area
    public string Title { get; set; } = string.Empty;
    public string? Subtitle { get; set; }
    public string? Description { get; set; }
    public string? CategoryLabel { get; set; }
    public string? Image { get; set; }
    public string DetailRoute { get; set; } = string.Empty;
    public int DestinationId { get; set; }
    public string? LocationName { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public double? Rating { get; set; }
    public string? ReviewCount { get; set; }
    public List<string> Tags { get; set; } = new();
    public double Score { get; set; }
}
