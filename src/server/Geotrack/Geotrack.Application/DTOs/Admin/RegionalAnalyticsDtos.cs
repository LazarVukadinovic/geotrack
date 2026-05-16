namespace Geotrack.Application.DTOs.Admin;

public class RegionalAnalyticsOverviewQuery
{
    public int RegionId { get; set; }
    public string? Period { get; set; }
    public string? EntityType { get; set; }
    public string? Search { get; set; }
    public int Skip { get; set; }
    public int Take { get; set; } = 25;
    public string? Sort { get; set; }
}

public class RegionalMonthlyVisitsQuery
{
    public int RegionId { get; set; }
    public int SelectedYearsBack { get; set; } = 1;
    public string? EntityType { get; set; }
    public string? Search { get; set; }
}

public class RegionalAnalyticsOverviewDto
{
    public RegionalAssignedRegionDto ActiveRegion { get; set; } = new();
    public RegionalAnalyticsPeriodDto Period { get; set; } = new();
    public RegionalAnalyticsTotalsDto Totals { get; set; } = new();
    public RegionalAnalyticsComparisonDto Comparison { get; set; } = new();
    public List<RegionalAnalyticsTrendPointDto> PageViewsTrend { get; set; } = new();
    public List<RegionalAnalyticsEntityMetricDto> TopPerformingEntities { get; set; } = new();
    public List<RegionalAnalyticsDistributionItemDto> EntityTypeDistribution { get; set; } = new();
    public RegionalAnalyticsEngagementFunnelDto EngagementFunnel { get; set; } = new();
    public RegionalAnalyticsActionableInsightsDto ActionableInsights { get; set; } = new();
    public RegionalVisitorBehaviorAnalyticsDto VisitorBehavior { get; set; } = new();
    public List<RegionalAnalyticsEntityMetricDto> EntityRows { get; set; } = new();
    public RegionalAnalyticsPaginationDto Pagination { get; set; } = new();
    public int PendingRequestsCount { get; set; }
}

public class RegionalMonthlyVisitsDto
{
    public RegionalAssignedRegionDto ActiveRegion { get; set; } = new();
    public int SelectedYearsBack { get; set; }
    public List<string> MonthLabels { get; set; } = new();
    public List<RegionalMonthlyVisitsSeriesDto> Series { get; set; } = new();
    public int TotalVisits { get; set; }
    public string VisitMetric { get; set; } = "entity_view";
    public List<string> VisitEventTypes { get; set; } = new();
    public string TimeZone { get; set; } = "UTC";
}

public class RegionalMonthlyVisitsSeriesDto
{
    public string YearLabel { get; set; } = string.Empty;
    public int Year { get; set; }
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public List<int> MonthlyVisitCounts { get; set; } = new();
    public int TotalVisits { get; set; }
}

public class RegionalAnalyticsPeriodDto
{
    public string Key { get; set; } = "30d";
    public DateTime? StartDate { get; set; }
    public DateTime EndDate { get; set; }
}

public class RegionalAnalyticsTotalsDto
{
    public int PageViews { get; set; }
    public int UniqueVisitors { get; set; }
    public int Saves { get; set; }
    public double AverageRating { get; set; }
    public int ExternalClicks { get; set; }
    public int TotalReviews { get; set; }
}

public class RegionalAnalyticsComparisonDto
{
    public double PageViewsChangePercent { get; set; }
    public double UniqueVisitorsChangePercent { get; set; }
    public double SavesChangePercent { get; set; }
    public double AverageRatingChangePercent { get; set; }
    public double ExternalClicksChangePercent { get; set; }
    public double TotalReviewsChangePercent { get; set; }
}

public class RegionalAnalyticsTrendPointDto
{
    public DateTime Date { get; set; }
    public int Views { get; set; }
    public int UniqueVisitors { get; set; }
}

public class RegionalAnalyticsEntityMetricDto
{
    public string EntityType { get; set; } = string.Empty;
    public int EntityId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string RegionDisplayName { get; set; } = string.Empty;
    public string? ImageUrl { get; set; }
    public string? CategoryLabel { get; set; }
    public int PageViews { get; set; }
    public int UniqueVisitors { get; set; }
    public int Saves { get; set; }
    public int ExternalClicks { get; set; }
    public double AverageRating { get; set; }
    public int ReviewCount { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? DetailRoute { get; set; }
}

public class RegionalAnalyticsDistributionItemDto
{
    public string EntityType { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public int Count { get; set; }
}

public class RegionalAnalyticsEngagementFunnelDto
{
    public int PageViews { get; set; }
    public int UniqueVisitors { get; set; }
    public int Saves { get; set; }
    public int ExternalClicks { get; set; }
    public int Reviews { get; set; }
}

public class RegionalAnalyticsActionableInsightsDto
{
    public RegionalAnalyticsEntityMetricDto? TopViewedLocation { get; set; }
    public RegionalAnalyticsEntityMetricDto? MostSaved { get; set; }
    public RegionalAnalyticsEntityMetricDto? HighestPerformingEvent { get; set; }
    public List<string> Messages { get; set; } = new();
}

public class RegionalVisitorBehaviorAnalyticsDto
{
    public int LoggedInVisitors { get; set; }
    public int AnonymousVisitors { get; set; }
    public int ReturningVisitors { get; set; }
    public double ReturningVisitorRatePercent { get; set; }
    public RegionalTimeSpentEntityDto? TopTimeSpentEntity { get; set; }
    public List<RegionalTimeSpentEntityDto> TopTimeSpentPages { get; set; } = new();
    public bool HasTimeSpentData { get; set; }
    public List<string> Notes { get; set; } = new();
}

public class RegionalTimeSpentEntityDto
{
    public string PageOrEntityName { get; set; } = string.Empty;
    public string EntityType { get; set; } = string.Empty;
    public int? EntityId { get; set; }
    public string? RouteOrPage { get; set; }
    public double AverageTimeSpentSeconds { get; set; }
    public double TotalTimeSpentSeconds { get; set; }
    public int VisitCount { get; set; }
}

public class RegionalAnalyticsPaginationDto
{
    public int TotalCount { get; set; }
    public int Skip { get; set; }
    public int Take { get; set; }
    public bool HasMore { get; set; }
}
