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

public class RegionalTagViewsQuery
{
    public int RegionId { get; set; }
    public string? Period { get; set; }
    public List<int> TagIds { get; set; } = new();
    public int Take { get; set; } = 15;
    public string? EntityType { get; set; }
}

public class RegionalAnalyticsEntityDetailQuery
{
    public int RegionId { get; set; }
    public string? EntityType { get; set; }
    public int EntityId { get; set; }
    public string? Period { get; set; }
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
    public List<RegionalDestinationRankingItemDto> DestinationRanking { get; set; } = new();
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

public class RegionalTagViewsAnalyticsResponseDto
{
    public string Period { get; set; } = "1m";
    public int RegionId { get; set; }
    public string RegionDisplayName { get; set; } = string.Empty;
    public int TotalViews { get; set; }
    public List<RegionalTagViewsAvailableTagDto> AvailableTags { get; set; } = new();
    public List<RegionalTagViewsItemDto> Items { get; set; } = new();
}

public class RegionalTagViewsAvailableTagDto
{
    public int TagId { get; set; }
    public string Name { get; set; } = string.Empty;
    public List<string> EntityScopes { get; set; } = new();
}

public class RegionalTagViewsItemDto
{
    public int TagId { get; set; }
    public string TagName { get; set; } = string.Empty;
    public int Views { get; set; }
    public double PercentageOfTotal { get; set; }
    public List<string> EntityScopes { get; set; } = new();
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

public class RegionalDestinationRankingItemDto
{
    public int DestinationId { get; set; }
    public string DestinationName { get; set; } = string.Empty;
    public int RegionId { get; set; }
    public string RegionDisplayName { get; set; } = string.Empty;
    public int Visits { get; set; }
    public int Rank { get; set; }
    public double PercentageOfTopValue { get; set; }
    public string? ImageUrl { get; set; }
    public string? DetailRoute { get; set; }
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

public class RegionalAnalyticsEntityDetailDto
{
    public string EntityType { get; set; } = string.Empty;
    public int EntityId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Title { get; set; }
    public string? Subtitle { get; set; }
    public string Status { get; set; } = string.Empty;
    public int RegionId { get; set; }
    public string RegionDisplayName { get; set; } = string.Empty;
    public int? DestinationId { get; set; }
    public string? DestinationName { get; set; }
    public int? LocationId { get; set; }
    public string? LocationName { get; set; }
    public int? PlaceId { get; set; }
    public string? PlaceName { get; set; }
    public string? Description { get; set; }
    public string? Summary { get; set; }
    public string? ImageUrl { get; set; }
    public List<string> GalleryImages { get; set; } = new();
    public List<RegionalAnalyticsEntityTagDto> Tags { get; set; } = new();
    public string? Address { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public DateTime? PublishedAt { get; set; }
    public RegionalAnalyticsEntityDetailMetricsDto Analytics { get; set; } = new();
    public RegionalAnalyticsEntityTypeSpecificDto TypeSpecific { get; set; } = new();
}

public class RegionalAnalyticsEntityDetailMetricsDto
{
    public int Views { get; set; }
    public int UniqueVisitors { get; set; }
    public int Saves { get; set; }
    public int ExternalClicks { get; set; }
    public int Reviews { get; set; }
    public double AverageRating { get; set; }
    public int RatingCount { get; set; }
    public double TrendChangePercent { get; set; }
    public DateTime? LastViewedAt { get; set; }
}

public class RegionalAnalyticsEntityTypeSpecificDto
{
    public string? DestinationType { get; set; }
    public string? LocationType { get; set; }
    public string? PlaceType { get; set; }
    public string? StayType { get; set; }
    public string? EventType { get; set; }
    public string? Category { get; set; }
    public string? ParentEntityType { get; set; }
    public string? ParentDestination { get; set; }
    public string? ParentLocation { get; set; }
    public string? ParentPlace { get; set; }
    public string? BoundaryNote { get; set; }
    public string? BoundaryShapeType { get; set; }
    public int? LocationsCount { get; set; }
    public int? PlacesCount { get; set; }
    public int? EventsCount { get; set; }
    public string? Website { get; set; }
    public string? BookingExternalLink { get; set; }
    public string? ContactPhone { get; set; }
    public string? ContactEmail { get; set; }
    public string? PriceRange { get; set; }
    public string? OpeningHours { get; set; }
    public List<RegionalPlaceOperatingHourDto> OperatingHours { get; set; } = new();
    public List<string> Amenities { get; set; } = new();
    public List<RegionalAnalyticsEntityLinkDto> BookingPartners { get; set; } = new();
    public List<RegionalAnalyticsExternalPartnerClickDto> ExternalPartnerClicks { get; set; } = new();
    public string? BestTimeToVisit { get; set; }
    public string? VisitorTips { get; set; }
    public string? AccessNotes { get; set; }
    public string? AccessibilityNotes { get; set; }
    public string? VenueName { get; set; }
    public DateTime? StartsAt { get; set; }
    public DateTime? EndsAt { get; set; }
    public bool? AllDay { get; set; }
    public bool? IsRecurring { get; set; }
    public string? TicketPrice { get; set; }
    public bool? IsFree { get; set; }
    public string? ExternalTicketLink { get; set; }
    public string? OrganizerName { get; set; }
    public int? AttendanceLimit { get; set; }
    public string? AgeRestriction { get; set; }
    public string? Duration { get; set; }
    public string? VenueDescription { get; set; }
    public string? ArrivalTips { get; set; }
    public string? Accessibility { get; set; }
}

public class RegionalAnalyticsEntityTagDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
}

public class RegionalAnalyticsEntityLinkDto
{
    public string Label { get; set; } = string.Empty;
    public string? Url { get; set; }
}

public class RegionalAnalyticsExternalPartnerClickDto
{
    public string PartnerKey { get; set; } = string.Empty;
    public string PartnerLabel { get; set; } = string.Empty;
    public string? Url { get; set; }
    public int ClickCount { get; set; }
    public double SharePercent { get; set; }
}

public class RegionalAnalyticsPaginationDto
{
    public int TotalCount { get; set; }
    public int Skip { get; set; }
    public int Take { get; set; }
    public bool HasMore { get; set; }
}
