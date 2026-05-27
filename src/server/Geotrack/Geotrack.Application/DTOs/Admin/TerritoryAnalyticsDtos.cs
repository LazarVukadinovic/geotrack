namespace Geotrack.Application.DTOs.Admin;

public class TerritoryAnalyticsOverviewQuery
{
    public int? TerritoryId { get; set; }
    public string? Period { get; set; }
    public List<int> RegionIds { get; set; } = new();
    public string? Status { get; set; }
    public int Skip { get; set; }
    public int Take { get; set; } = 5;
    public string? Sort { get; set; }
}

public class TerritoryAnalyticsOverviewDto
{
    public TerritoryAssignedTerritoryDto? ActiveTerritory { get; set; }
    public TerritoryAnalyticsFiltersDto Filters { get; set; } = new();
    public TerritoryAnalyticsTotalsDto Totals { get; set; } = new();
    public TerritoryAnalyticsComparisonDto Comparison { get; set; } = new();
    public TerritoryAnalyticsRegionHighlightDto? TopRegion { get; set; }
    public TerritoryAnalyticsFastestGrowingDto? FastestGrowing { get; set; }
    public List<TerritoryAnalyticsTopEntityDto> TopEntities { get; set; } = new();
    public TerritoryAnalyticsPageViewsOverTimeDto PageViewsOverTime { get; set; } = new();
    public List<TerritoryAnalyticsTopContentDto> TopDestinations { get; set; } = new();
    public List<TerritoryAnalyticsTopContentDto> TopLocations { get; set; } = new();
    public List<TerritoryAnalyticsTopContentDto> TopPlaces { get; set; } = new();
    public List<TerritoryAnalyticsTopEventDto> TopEvents { get; set; } = new();
    public List<TerritoryAnalyticsContentQualityDto> ContentQualityByRegion { get; set; } = new();
    public TerritoryAnalyticsRegionalPerformanceResponseDto RegionalPerformance { get; set; } = new();
}

public class TerritoryAnalyticsFiltersDto
{
    public string Period { get; set; } = string.Empty;
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public List<int> SelectedRegionIds { get; set; } = new();
    public string Status { get; set; } = "active";
}

public class TerritoryAnalyticsTotalsDto
{
    public int TotalPageViews { get; set; }
    public int UniqueVisitors { get; set; }
    public int ExternalLinkClicks { get; set; }
    public int TotalSaves { get; set; }
    public int TotalReviews { get; set; }
    public double AverageRating { get; set; }
}

public class TerritoryAnalyticsComparisonDto
{
    public double PageViewsChangePercent { get; set; }
    public double UniqueVisitorsChangePercent { get; set; }
    public double ExternalClicksChangePercent { get; set; }
    public double SavesChangePercent { get; set; }
    public double ReviewsChangePercent { get; set; }
    public double AverageRatingChange { get; set; }
}

public class TerritoryAnalyticsRegionHighlightDto
{
    public int RegionId { get; set; }
    public string RegionName { get; set; } = string.Empty;
    public int Score { get; set; }
    public int HealthScore { get; set; }
    public string? StatusLabel { get; set; }
    public string? PeriodLabel { get; set; }
    public string Description { get; set; } = string.Empty;
    public int Visits { get; set; }
    public int UniqueVisitors { get; set; }
}

public class TerritoryAnalyticsFastestGrowingDto
{
    public int RegionId { get; set; }
    public string RegionName { get; set; } = string.Empty;
    public double GrowthPercent { get; set; }
    public string MetricLabel { get; set; } = string.Empty;
}

public class TerritoryAnalyticsTopEntityDto
{
    public string EntityType { get; set; } = string.Empty;
    public int EntityId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string RegionName { get; set; } = string.Empty;
    public int Rank { get; set; }
    public int MetricValue { get; set; }
    public string MetricLabel { get; set; } = string.Empty;
    public string? ImageUrl { get; set; }
    public string? DetailRoute { get; set; }
    public string? PublicRoute { get; set; }
}

public class TerritoryAnalyticsPageViewsOverTimeDto
{
    public List<string> MonthLabels { get; set; } = new();
    public List<TerritoryAnalyticsPageViewsSeriesDto> Series { get; set; } = new();
}

public class TerritoryAnalyticsPageViewsSeriesDto
{
    public int RegionId { get; set; }
    public string RegionName { get; set; } = string.Empty;
    public List<int> Values { get; set; } = new();
}

public class TerritoryAnalyticsTopContentDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string RegionName { get; set; } = string.Empty;
    public int Views { get; set; }
    public double? AverageRating { get; set; }
    public string? ImageUrl { get; set; }
    public string? DetailRoute { get; set; }
}

public class TerritoryAnalyticsTopEventDto : TerritoryAnalyticsTopContentDto
{
    public int? Registrations { get; set; }
    public int? Saves { get; set; }
    public DateTime? EventDate { get; set; }
}

public class TerritoryAnalyticsContentQualityDto
{
    public int RegionId { get; set; }
    public string RegionName { get; set; } = string.Empty;
    public int TotalReviews { get; set; }
    public double AverageRating { get; set; }
}

public class TerritoryAnalyticsRegionalPerformanceResponseDto
{
    public List<TerritoryAnalyticsRegionalPerformanceItemDto> Items { get; set; } = new();
    public int TotalCount { get; set; }
    public int Skip { get; set; }
    public int Take { get; set; }
    public bool HasMore { get; set; }
}

public class TerritoryAnalyticsRegionalPerformanceItemDto
{
    public int RegionId { get; set; }
    public string RegionName { get; set; } = string.Empty;
    public string? RegionCode { get; set; }
    public int PageViews { get; set; }
    public int Visitors { get; set; }
    public int Saves { get; set; }
    public double AverageRating { get; set; }
    public int DestinationsCount { get; set; }
    public int LocationsCount { get; set; }
    public int PlacesCount { get; set; }
    public int EventsCount { get; set; }
    public string StatusLabel { get; set; } = string.Empty;
    public int HealthScore { get; set; }
    public TerritoryHealthDto? Health { get; set; }
    public int QualityScore { get; set; }
    public string ManagerStatus { get; set; } = string.Empty;
    public List<string> ManagerNames { get; set; } = new();
}
