namespace Geotrack.Application.DTOs.Admin;

public class RegionalAssignedRegionDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string? TerritoryName { get; set; }
    public string? StatusLabel { get; set; }
}

public class RegionalDashboardRegionDto : RegionalAssignedRegionDto
{
}

public class RegionalDashboardStatsDto
{
    public int PendingRequests { get; set; }
    public int TotalDestinations { get; set; }
    public int DestinationsAddedLastMonth { get; set; }
    public int TotalPlaces { get; set; }
    public int PlacesAddedLastMonth { get; set; }
    public int TotalEvents { get; set; }
    public int EventsAddedLastMonth { get; set; }
}

public class RegionalRequestQueuePreviewItemDto
{
    public string RequestKey { get; set; } = string.Empty;
    public int RequestId { get; set; }
    public string Source { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string SubmittedByDisplayName { get; set; } = string.Empty;
    public string EntityType { get; set; } = "Place";
    public string ContributionType { get; set; } = string.Empty;
    public DateTime SubmittedAt { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? ThumbnailImageUrl { get; set; }
    public string DetailRoute { get; set; } = string.Empty;
}

public class RegionalDashboardActivityItemDto
{
    public string Id { get; set; } = string.Empty;
    public string EntityType { get; set; } = string.Empty;
    public int? EntityId { get; set; }
    public string Action { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string? ActorDisplayName { get; set; }
    public string? ActorInitials { get; set; }
    public DateTime OccurredAt { get; set; }
    public string? Status { get; set; }
    public string? DetailRoute { get; set; }
}

public class RegionalDashboardDto
{
    public RegionalDashboardRegionDto ActiveRegion { get; set; } = new();
    public RegionalDashboardStatsDto Stats { get; set; } = new();
    public List<RegionalRequestQueuePreviewItemDto> RequestQueuePreview { get; set; } = new();
    public List<RegionalDashboardActivityItemDto> RecentActivity { get; set; } = new();
}

public class RegionalDashboardActivityResponseDto
{
    public List<RegionalDashboardActivityItemDto> Items { get; set; } = new();
    public int Skip { get; set; }
    public int Take { get; set; }
    public bool HasMore { get; set; }
}
