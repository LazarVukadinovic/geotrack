namespace Geotrack.Application.DTOs.GeoTrackTools;

public class GeoTrackToolSearchRequest
{
    public string? Query { get; set; }
    public string? Location { get; set; }
    public int? DestinationId { get; set; }
    public List<string> Tags { get; set; } = new();
    public List<string> Categories { get; set; } = new();
    public List<string> PlaceTypes { get; set; } = new();
    public List<string> StayTypes { get; set; } = new();
    public List<string> EventCategories { get; set; } = new();
    public int? Limit { get; set; }
    public bool UpcomingOnly { get; set; }
}

public class GeoTrackMapFiltersRequest
{
    public double North { get; set; }
    public double South { get; set; }
    public double East { get; set; }
    public double West { get; set; }
    public double Zoom { get; set; } = 13;
}

public class GeoTrackToolItemDto
{
    public int Id { get; set; }
    public string EntityType { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? Location { get; set; }
    public string? Destination { get; set; }
    public string? Category { get; set; }
    public string? DetailRoute { get; set; }
    public string? ImageUrl { get; set; }
    public double? Rating { get; set; }
    public int? ReviewCount { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public DateTime? StartsAt { get; set; }
    public DateTime? EndsAt { get; set; }
    public List<string> Tags { get; set; } = new();
}

public class GeoTrackMapFiltersDto
{
    public List<GeoTrackMapFilterDto> Categories { get; set; } = new();
}

public class GeoTrackMapFilterDto
{
    public string Id { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public int Count { get; set; }
}

public class GeoTrackAgentContextDto
{
    public List<string> ToolsUsed { get; set; } = new();
    public GeoTrackAgentQueryPlanDto? Plan { get; set; }
    public List<GeoTrackToolSearchTraceDto> Searches { get; set; } = new();
    public string? LocationUsed { get; set; }
    public int? DestinationId { get; set; }
    public List<string> FiltersUsed { get; set; } = new();
    public bool SearchAttempted { get; set; }
    public bool NoResults { get; set; }
    public bool ClarificationNeeded { get; set; }
    public string? ClarificationQuestion { get; set; }
    public int ResultCount { get; set; }
    public List<GeoTrackToolItemDto> Places { get; set; } = new();
    public List<GeoTrackToolItemDto> Stays { get; set; } = new();
    public List<GeoTrackToolItemDto> Events { get; set; } = new();
}

public class GeoTrackAgentQueryPlanDto
{
    public string Intent { get; set; } = "general_help";
    public string Language { get; set; } = "en";
    public string OriginalMessage { get; set; } = string.Empty;
    public string NormalizedMessage { get; set; } = string.Empty;
    public List<string> EntityTypes { get; set; } = new();
    public string? LocationText { get; set; }
    public string? LocationUsed { get; set; }
    public int? DestinationId { get; set; }
    public string? DestinationName { get; set; }
    public List<string> Tags { get; set; } = new();
    public List<string> Categories { get; set; } = new();
    public List<string> PlaceTypes { get; set; } = new();
    public List<string> StayTypes { get; set; } = new();
    public List<string> EventCategories { get; set; } = new();
    public List<string> QueryTerms { get; set; } = new();
    public string? QueryText { get; set; }
    public string RankingIntent { get; set; } = "all";
    public bool UsesCurrentContext { get; set; }
    public bool SearchCatalog { get; set; }
    public bool ClarificationNeeded { get; set; }
    public string? ClarificationQuestion { get; set; }
}

public class GeoTrackToolSearchTraceDto
{
    public string Tool { get; set; } = string.Empty;
    public string SearchType { get; set; } = string.Empty;
    public string? Query { get; set; }
    public string? Location { get; set; }
    public int? DestinationId { get; set; }
    public List<string> Filters { get; set; } = new();
    public int ResultCount { get; set; }
}
