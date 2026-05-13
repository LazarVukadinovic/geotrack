namespace Geotrack.Application.DTOs.Explore;

public class ExplorePageDto
{
    public ExploreDestinationDto? SelectedDestination { get; set; }

    /// <summary>
    /// The Explore feed. The backend owns section meaning, ordering, titles and card routes.
    /// The frontend should render this list generically.
    /// </summary>
    public List<ExploreFeedSectionDto> Sections { get; set; } = new();
}

public class ExploreFeedSectionDto
{
    public int? TagTypeId { get; set; }
    public string Key { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? Subtitle { get; set; }

    /// <summary>
    /// UI hint only. Examples: horizontal-cards, grid, compact-list.
    /// </summary>
    public string Layout { get; set; } = "horizontal-cards";

    /// <summary>
    /// UI hint only. Examples: place-card, stay-card, event-card.
    /// </summary>
    public string CardType { get; set; } = "place-card";

    public string? MoreRoute { get; set; }
    public int Priority { get; set; }
    public List<ExploreFeedSectionItemDto> Items { get; set; } = new();
}

public class ExploreFeedSectionItemDto
{
    public int Id { get; set; }
    public string EntityType { get; set; } = "place";
    public string DetailRoute { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;
    public string? Subtitle { get; set; }
    public string? Description { get; set; }
    public string? CategoryLabel { get; set; }
    public string? Image { get; set; }

    public double? Rating { get; set; }
    public string? ReviewCount { get; set; }
    public string? Distance { get; set; }
    public string? DateLabel { get; set; }
    public string? OpeningHours { get; set; }
    public decimal? PricePerNight { get; set; }

    public bool IsSaved { get; set; }
    public List<ExploreFeedSectionTagDto> Tags { get; set; } = new();
}

public class ExploreFeedSectionTagDto
{
    public int TagId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
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
    public string? CategoryLabel { get; set; } = string.Empty;
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

public class LocalPlacesCategoriesPageDto
{
    public List<LocalPlacesCategoryDto> Items { get; set; } = new();
    public int Skip { get; set; }
    public int Take { get; set; }
    public int NextSkip { get; set; }
    public bool HasMore { get; set; }
}

// Kept only because some older helper methods/services may still reference this type.
// ExplorePageDto no longer exposes landmarkCategories; the feed is Sections[].
public class ExploreLandmarkCategoryDto
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Image { get; set; }
    public string? CategoryKey { get; set; }
}
