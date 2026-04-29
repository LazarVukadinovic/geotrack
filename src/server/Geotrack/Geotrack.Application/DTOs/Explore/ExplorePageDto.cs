namespace Geotrack.Application.DTOs.Explore;

public class ExplorePageDto
{
    public ExploreDestinationDto? SelectedDestination { get; set; }
    public List<ExploreEventDto> EventsThisWeek { get; set; } = new();
    public List<ExploreStayDto> TopStays { get; set; } = new();
    public List<ExploreNearbyItemDto> PopularNearby { get; set; } = new();
    public List<ExploreAttractionDto> MustSeeAttractions { get; set; } = new();
    public List<ExploreLandmarkCategoryDto> LandmarkCategories { get; set; } = new();
    public List<ExploreCuratedItemDto> CuratedForYou { get; set; } = new();
    public List<ExploreLocalFavoriteDto> LocalFavorites { get; set; } = new();
}

public class ExploreEventDto
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Subtitle { get; set; }
    public string? DateLabel { get; set; }
    public string? Image { get; set; }
}

public class ExploreStayDto
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Image { get; set; }
    public double? Rating { get; set; }
    public string? ReviewCount { get; set; }
    public string? Distance { get; set; }
    public decimal PricePerNight { get; set; }
    public bool IsSaved { get; set; }
}

public class ExploreNearbyItemDto
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? CategoryLabel { get; set; }
    public string? Image { get; set; }
    public double? Rating { get; set; }
    public string? ReviewCount { get; set; }
    public string? Distance { get; set; }
    public bool IsSaved { get; set; }
}

public class ExploreAttractionDto
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Type { get; set; }
    public string? Image { get; set; }
}

public class ExploreLandmarkCategoryDto
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Image { get; set; }
    public string? CategoryKey { get; set; }
}

public class ExploreCuratedItemDto
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Subtitle { get; set; }
    public string? CategoryLabel { get; set; }
    public string? Image { get; set; }
    public bool IsSaved { get; set; }
}

public class ExploreLocalFavoriteDto
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Subtitle { get; set; }
    public string? Description { get; set; }
    public string? Category { get; set; }
    public string? Type { get; set; }
    public string? Location { get; set; }
    public string? Image { get; set; }
    public double? Rating { get; set; }
    public string? ReviewCount { get; set; }
    public bool IsSaved { get; set; }
    public string? OpeningHours { get; set; }
    public bool IsRecommended { get; set; }
}

public class ExploreRecommendationsPageDto
{
    public ExploreDestinationDto? SelectedDestination { get; set; }
    public List<ExploreRecommendationFilterDto> Filters { get; set; } = new();
    public List<ExploreRecommendationItemDto> Items { get; set; } = new();
    public bool HasSignals { get; set; }
    public string? EmptyReason { get; set; }
    public int Skip { get; set; }
    public int Take { get; set; }
    public int NextSkip { get; set; }
    public bool HasMore { get; set; }
}

public class ExploreRecommendationFilterDto
{
    public int TagId { get; set; }
    public string Label { get; set; } = string.Empty;
    public int Count { get; set; }
}

public class ExploreRecommendationItemDto
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Reason { get; set; }
    public string? Description { get; set; }
    public string? Location { get; set; }
    public string? Image { get; set; }
    public double? Rating { get; set; }
    public string? ReviewCount { get; set; }
    public string? Distance { get; set; }
    public string? OpeningHours { get; set; }
    public string CategoryLabel { get; set; } = string.Empty;
    public int? MatchedTagId { get; set; }
    public bool IsSaved { get; set; }
}

public class LocalPlacesCategoryDto
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Image { get; set; }
    public string CategoryKey { get; set; } = string.Empty;
    public string? Subtitle { get; set; }
}
