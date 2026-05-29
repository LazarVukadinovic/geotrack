namespace Geotrack.Application.DTOs.Admin;

public class TerritoryAssignedTerritoryDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string? CountryCode { get; set; }
    public string? StatusLabel { get; set; }
}

public class TerritorySidebarSummaryDto
{
    public List<TerritoryAssignedTerritoryDto> AssignedTerritories { get; set; } = new();
    public TerritoryAssignedTerritoryDto? ActiveTerritory { get; set; }
}

public class TerritoryDashboardStatsDto
{
    public int TotalRegions { get; set; }
    public int RegionsAddedLastMonth { get; set; }
    public int ActiveUsers { get; set; }
    public double ActiveUsersChangePercent { get; set; }
    public int TotalDestinations { get; set; }
    public int DestinationsAddedLastMonth { get; set; }
    public int TotalLocations { get; set; }
    public int LocationsAddedLastMonth { get; set; }
    public int TotalPlaces { get; set; }
    public int PlacesAddedLastMonth { get; set; }
    public int TotalEvents { get; set; }
    public int EventsAddedLastMonth { get; set; }
}

public class TerritoryRegionHealthDto
{
    public int RegionId { get; set; }
    public string RegionName { get; set; } = string.Empty;
    public string? ManagerDisplayName { get; set; }
    public int DestinationsCount { get; set; }
    public int LocationsCount { get; set; }
    public int PlacesCount { get; set; }
    public int StaysCount { get; set; }
    public int EventsCount { get; set; }
    public int PendingRequestsCount { get; set; }
    public int HealthScore { get; set; }
    public TerritoryHealthDto Health { get; set; } = new();
    public int ActivityScore { get; set; }
    public List<TerritoryRegionManagerDto> AssignedManagers { get; set; } = new();
    public List<string> AssignedManagersPreview { get; set; } = new();
}

public class TerritoryTopPerformingRegionDto
{
    public int RegionId { get; set; }
    public string RegionName { get; set; } = string.Empty;
    public int VisitsThisMonth { get; set; }
    public double ChangePercent { get; set; }
    public int Rank { get; set; }
}

public class TerritoryTopContentItemDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string RegionName { get; set; } = string.Empty;
    public int MetricValue { get; set; }
    public string MetricLabel { get; set; } = string.Empty;
    public string? ImageUrl { get; set; }
    public string? DetailRoute { get; set; }
    public DateTime? StartsAt { get; set; }
}

public class TerritoryTopContentDto
{
    public List<TerritoryTopContentItemDto> TopDestinations { get; set; } = new();
    public List<TerritoryTopContentItemDto> TopLocations { get; set; } = new();
    public List<TerritoryTopContentItemDto> TopPlaces { get; set; } = new();
    public List<TerritoryTopContentItemDto> UpcomingEvents { get; set; } = new();
}

public class TerritoryDashboardDto
{
    public TerritoryAssignedTerritoryDto? ActiveTerritory { get; set; }
    public TerritoryDashboardStatsDto Stats { get; set; } = new();
    public List<TerritoryRegionHealthDto> RegionsHealth { get; set; } = new();
    public List<TerritoryTopPerformingRegionDto> TopPerformingRegions { get; set; } = new();
    public TerritoryTopContentDto TopContent { get; set; } = new();
}

public class TerritoryRegionDirectoryItemDto
{
    public int Id { get; set; }
    public int RegionId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string RegionName { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string TerritoryName { get; set; } = string.Empty;
    public int TerritoryId { get; set; }
    public string TerritoryDisplayName { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public List<string> ManagerNames { get; set; } = new();
    public int DestinationsCount { get; set; }
    public int LocationsCount { get; set; }
    public int PlacesCount { get; set; }
    public int StaysCount { get; set; }
    public int EventsCount { get; set; }
    public int PendingRequestsCount { get; set; }
    public int AssignedPersonsCount { get; set; }
    public int ActiveManagersCount { get; set; }
    public string? ManagerDisplayName { get; set; }
    public List<TerritoryRegionManagerDto> AssignedManagers { get; set; } = new();
    public int? HealthScore { get; set; }
    public TerritoryHealthDto? Health { get; set; }
    public int ActivityScore { get; set; }
    public string? ImageUrl { get; set; }
    public string? DetailRoute { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class TerritoryRegionDirectoryResponseDto
{
    public TerritoryAssignedTerritoryDto? Territory { get; set; }
    public List<TerritoryRegionDirectoryItemDto> Items { get; set; } = new();
    public int TotalCount { get; set; }
    public int Skip { get; set; }
    public int Take { get; set; }
    public bool HasMore { get; set; }
}

public class TerritoryRegionMapDto
{
    public TerritoryRegionMapTerritoryDto? Territory { get; set; }
    public TerritoryRegionMapViewportDto Viewport { get; set; } = new();
    public List<TerritoryRegionMapRegionDto> Regions { get; set; } = new();
    public string? EmptyStateMessage { get; set; }
    public string? BoundaryWarningMessage { get; set; }
}

public class TerritoryRegionMapTerritoryDto
{
    public int TerritoryId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string? CountryCode { get; set; }
    public string? CountryIso3 { get; set; }
    public bool HasBoundary { get; set; }
    public string? GeometrySource { get; set; }
    public string? BoundaryMissingReason { get; set; }
    public TerritoryRegionGeometryDto? GeometryGeoJson { get; set; }
    public TerritoryRegionMapViewportDto Viewport { get; set; } = new();
}

public class TerritoryRegionMapRegionDto
{
    public int RegionId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public int TerritoryId { get; set; }
    public string TerritoryDisplayName { get; set; } = string.Empty;
    public string Status { get; set; } = "Inactive";
    public int? HealthScore { get; set; }
    public TerritoryHealthDto? Health { get; set; }
    public List<TerritoryRegionManagerDto> AssignedManagers { get; set; } = new();
    public string? ManagerDisplayName { get; set; }
    public TerritoryRegionMapContentCountsDto ContentCounts { get; set; } = new();
    public bool HasBoundary { get; set; }
    public string? GeometrySource { get; set; }
    public string? BoundaryMissingReason { get; set; }
    public TerritoryRegionGeometryDto? GeometryGeoJson { get; set; }
    public TerritoryRegionMapViewportDto Viewport { get; set; } = new();
    public double? CenterLatitude { get; set; }
    public double? CenterLongitude { get; set; }
    public string? DetailRoute { get; set; }
}

public class TerritoryRegionMapContentCountsDto
{
    public int Destinations { get; set; }
    public int Locations { get; set; }
    public int Places { get; set; }
    public int Stays { get; set; }
    public int Events { get; set; }
    public int Pending { get; set; }
}

public class TerritoryRegionBoundaryCoordinateDto
{
    public double Latitude { get; set; }
    public double Longitude { get; set; }
}

public class TerritoryRegionBoundaryDto
{
    public string? ShapeType { get; set; }
    public List<TerritoryRegionBoundaryCoordinateDto> Coordinates { get; set; } = new();
}

public class TerritoryRegionCreateRequestDto
{
    public int TerritoryId { get; set; }
    public string? Name { get; set; }
    public string? CandidateExternalKey { get; set; }
    public string? BoundaryCandidateId { get; set; }
    public string? BoundaryExternalKey { get; set; }
    public string? ShapeGroup { get; set; }
    public string? ShapeId { get; set; }
    public string? AdmLevel { get; set; }
    public string? BoundarySource { get; set; }
    public long? OsmRelationId { get; set; }
    public int? OsmAdminLevel { get; set; }
    public TerritoryRegionBoundaryDto? Boundary { get; set; }
}

public class TerritoryRegionCreateResponseDto
{
    public int Id { get; set; }
    public int RegionId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public int TerritoryId { get; set; }
    public string TerritoryDisplayName { get; set; } = string.Empty;
    public string Status { get; set; } = "Inactive";
    public string? BoundarySource { get; set; }
    public string? BoundaryShapeType { get; set; }
    public string? BoundaryShapeGroup { get; set; }
    public string? BoundaryShapeId { get; set; }
    public string? BoundaryExternalKey { get; set; }
    public string? BoundaryAdmLevel { get; set; }
    public string? BoundaryReleaseType { get; set; }
    public int? BoundaryCandidateId { get; set; }
    public TerritoryRegionGeometryDto? BoundaryGeometryGeoJson { get; set; }
    public DateTime CreatedAt { get; set; }
    public string? DetailRoute { get; set; }
}

public class TerritoryRegionCreateContextDto
{
    public TerritoryAssignedTerritoryDto? Territory { get; set; }
    public TerritoryRegionGeometryDto? TerritoryBoundaryGeoJson { get; set; }
    public List<TerritoryRegionMapItemDto> ExistingRegions { get; set; } = new();
    public TerritoryRegionMapViewportDto Viewport { get; set; } = new();
    public bool CanCreate { get; set; }
    public string? BoundaryRequiredMessage { get; set; }
}

public class TerritoryRegionMapItemDto
{
    public string FeatureKind { get; set; } = "ExistingRegion";
    public int Id { get; set; }
    public int RegionId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public TerritoryRegionGeometryDto? GeometryGeoJson { get; set; }
    public double? CenterLatitude { get; set; }
    public double? CenterLongitude { get; set; }
}

public class TerritoryRegionGeometryDto
{
    public string Type { get; set; } = string.Empty;
    public object Coordinates { get; set; } = Array.Empty<double>();
}

public class TerritoryRegionMapBoundsDto
{
    public double North { get; set; }
    public double South { get; set; }
    public double East { get; set; }
    public double West { get; set; }
}

public class TerritoryRegionMapViewportDto
{
    public double? CenterLatitude { get; set; }
    public double? CenterLongitude { get; set; }
    public double? Zoom { get; set; }
    public TerritoryRegionMapBoundsDto? Bounds { get; set; }
    public bool IsEmpty { get; set; }
}

public class TerritoryRegionDetailDto
{
    public TerritoryRegionDetailRegionDto Region { get; set; } = new();
    public List<TerritoryRegionManagerDto> Managers { get; set; } = new();
    public TerritoryRegionSummaryDto Summary { get; set; } = new();
    public TerritoryHealthDto Health { get; set; } = new();
    public TerritoryRegionContentPreviewDto Content { get; set; } = new();
    public TerritoryRegionAnalyticsDto Analytics { get; set; } = new();
}

public class TerritoryRegionDetailRegionDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Status { get; set; } = string.Empty;
    public int TerritoryId { get; set; }
    public string TerritoryDisplayName { get; set; } = string.Empty;
    public string? ImageUrl { get; set; }
    public double? CenterLatitude { get; set; }
    public double? CenterLongitude { get; set; }
}

public class TerritoryRegionManagerDto
{
    public int UserId { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Initials { get; set; } = string.Empty;
    public string? AvatarUrl { get; set; }
    public string Role { get; set; } = string.Empty;
    public DateTime AssignedAt { get; set; }
}

public class TerritoryRegionSummaryDto
{
    public int DestinationsCount { get; set; }
    public int LocationsCount { get; set; }
    public int PlacesCount { get; set; }
    public int StaysCount { get; set; }
    public int EventsCount { get; set; }
    public int PendingRequestsCount { get; set; }
    public int ActiveUsers { get; set; }
    public int Visits { get; set; }
    public int Saves { get; set; }
    public int Reviews { get; set; }
    public double AverageRating { get; set; }
}

public class TerritoryHealthDto
{
    public int? Score { get; set; }
    public string Status { get; set; } = "insufficient_data";
    public string StatusLabel { get; set; } = "Insufficient data";
    public string? PeriodLabel { get; set; }
    public DateTime CalculatedAt { get; set; }
    public List<TerritoryHealthInsightDto> Drivers { get; set; } = new();
    public List<TerritoryHealthInsightDto> Issues { get; set; } = new();
    public List<TerritoryHealthInsightDto> Recommendations { get; set; } = new();
}

public class TerritoryHealthInsightDto
{
    public string? Key { get; set; }
    public string Label { get; set; } = string.Empty;
    public string? Title { get; set; }
    public string Detail { get; set; } = string.Empty;
    public string? Message { get; set; }
    public string Severity { get; set; } = "info";
    public int? ScoreContribution { get; set; }
    public int? MaxPoints { get; set; }
    public string? Status { get; set; }
    public string? RecommendationKey { get; set; }
    public int? Priority { get; set; }
    public string? ActionRoute { get; set; }
}

public class TerritoryRegionContentPreviewDto
{
    public List<TerritoryRegionContentItemDto> Destinations { get; set; } = new();
    public List<TerritoryRegionContentItemDto> Locations { get; set; } = new();
    public List<TerritoryRegionContentItemDto> Places { get; set; } = new();
    public List<TerritoryRegionContentItemDto> Stays { get; set; } = new();
    public List<TerritoryRegionContentItemDto> Events { get; set; } = new();
}

public class TerritoryRegionAnalyticsDto
{
    public List<TerritoryRegionTopEntityDto> TopEntities { get; set; } = new();
    public List<TerritoryRegionActivityDto> RecentActivity { get; set; } = new();
}

public class TerritoryRegionTopEntityDto
{
    public string EntityType { get; set; } = string.Empty;
    public int EntityId { get; set; }
    public string Name { get; set; } = string.Empty;
    public int Views { get; set; }
    public string? ImageUrl { get; set; }
}

public class TerritoryRegionActivityDto
{
    public string EventType { get; set; } = string.Empty;
    public string? EntityType { get; set; }
    public int? EntityId { get; set; }
    public string? EntityName { get; set; }
    public DateTime OccurredAt { get; set; }
}

public class TerritoryRegionContentResponseDto
{
    public string EntityType { get; set; } = string.Empty;
    public List<TerritoryRegionContentItemDto> Items { get; set; } = new();
    public int TotalCount { get; set; }
    public int Skip { get; set; }
    public int Take { get; set; }
    public bool HasMore { get; set; }
}

public class TerritoryRegionContentItemDto
{
    public int Id { get; set; }
    public string EntityType { get; set; } = string.Empty;
    public string? PublicDetailRoute { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Summary { get; set; }
    public string? Description { get; set; }
    public string Status { get; set; } = string.Empty;
    public int RegionId { get; set; }
    public string RegionName { get; set; } = string.Empty;
    public int? DestinationId { get; set; }
    public string? DestinationName { get; set; }
    public int? LocationId { get; set; }
    public string? LocationName { get; set; }
    public int? PlaceId { get; set; }
    public string? PlaceName { get; set; }
    public string? ParentName { get; set; }
    public string? TypeName { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public DateTime? StartsAt { get; set; }
    public int LocationsCount { get; set; }
    public int PlacesCount { get; set; }
    public int StaysCount { get; set; }
    public int EventsCount { get; set; }
    public int Views { get; set; }
    public int Saves { get; set; }
    public int ExternalClicks { get; set; }
    public int ReviewsCount { get; set; }
    public double? AverageRating { get; set; }
    public string? ImageUrl { get; set; }
    public TerritoryHealthDto? Health { get; set; }
}

public class TerritoryContentDirectoryItemDto
{
    public int Id { get; set; }
    public string EntityType { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string RegionName { get; set; } = string.Empty;
    public string? ParentName { get; set; }
    public string? TypeName { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public DateTime? StartsAt { get; set; }
    public int ViewsLast30Days { get; set; }
    public string? ImageUrl { get; set; }
    public string? DetailRoute { get; set; }
    public string? PublicDetailRoute { get; set; }
}

public class TerritoryContentDirectoryResponseDto
{
    public string EntityType { get; set; } = string.Empty;
    public List<TerritoryContentDirectoryItemDto> Items { get; set; } = new();
    public int TotalCount { get; set; }
    public int Skip { get; set; }
    public int Take { get; set; }
    public bool HasMore { get; set; }
}
