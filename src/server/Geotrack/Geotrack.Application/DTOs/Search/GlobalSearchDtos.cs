namespace Geotrack.Application.DTOs.Search;

public class GlobalSearchResponseDto
{
    public string Query { get; set; } = string.Empty;
    public string Language { get; set; } = "en";
    public GlobalSearchIntentDto Intent { get; set; } = new();
    public List<SearchFilterGroupDto> FilterGroups { get; set; } = new();
    public SearchResultItemDto? TopResult { get; set; }
    public List<SearchResultGroupDto> Groups { get; set; } = new();
    public int TotalCount { get; set; }
}

public class GlobalSearchIntentDto
{
    public SearchIntentDestinationDto? Destination { get; set; }
    public List<string> Activities { get; set; } = new();
    public List<string> ContentTypes { get; set; } = new();
    public bool UsesCurrentLocation { get; set; }
}

public class SearchIntentDestinationDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public double Latitude { get; set; }
    public double Longitude { get; set; }
}

public class SearchResultGroupDto
{
    public string Key { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public List<SearchResultItemDto> Items { get; set; } = new();
}

public class SearchResultItemDto
{
    public int Id { get; set; }
    public string EntityType { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? Subtitle { get; set; }
    public string? Description { get; set; }
    public string? Image { get; set; }
    public string? Route { get; set; }
    public int? DestinationId { get; set; }
    public string? DestinationName { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public string? CategoryLabel { get; set; }
    public List<string> Tags { get; set; } = new();
    public double? Rating { get; set; }
    public string? ReviewCount { get; set; }
    public string? Distance { get; set; }
    public bool CanOpenInExplore { get; set; }
    public double Score { get; set; }
    public List<string> MatchedTerms { get; set; } = new();
}

public class SearchFilterGroupDto
{
    public string Key { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public List<SearchFilterOptionDto> Options { get; set; } = new();
}

public class SearchFilterOptionDto
{
    public string Id { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string? Domain { get; set; }
    public int? Count { get; set; }
}

public class SearchMetadataDto
{
    public string Language { get; set; } = "en";
    public List<SearchFilterGroupDto> FilterGroups { get; set; } = new();
}
