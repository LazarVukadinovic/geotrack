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
    public int EventsCount { get; set; }
    public int PendingRequestsCount { get; set; }
    public int ActivityScore { get; set; }
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
    public string Name { get; set; } = string.Empty;
    public string TerritoryName { get; set; } = string.Empty;
    public int DestinationsCount { get; set; }
    public int LocationsCount { get; set; }
    public int PlacesCount { get; set; }
    public int EventsCount { get; set; }
    public int PendingRequestsCount { get; set; }
    public int ActiveManagersCount { get; set; }
    public string? ManagerDisplayName { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class TerritoryRegionDirectoryResponseDto
{
    public List<TerritoryRegionDirectoryItemDto> Items { get; set; } = new();
    public int TotalCount { get; set; }
    public int Skip { get; set; }
    public int Take { get; set; }
    public bool HasMore { get; set; }
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
