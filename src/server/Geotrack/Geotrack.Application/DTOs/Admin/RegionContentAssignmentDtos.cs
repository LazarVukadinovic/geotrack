namespace Geotrack.Application.DTOs.Admin;

public class RegionContentAssignmentSummaryDto
{
    public int TerritoryId { get; set; }
    public string TerritoryName { get; set; } = string.Empty;
    public int TotalContentChecked { get; set; }
    public int AlreadyAssignedCount { get; set; }
    public int WouldUpdateCount { get; set; }
    public int UpdatedCount { get; set; }
    public int SkippedCount { get; set; }
    public int AmbiguousCount { get; set; }
    public int FailedCount { get; set; }
    public bool DryRun { get; set; }
    public List<RegionContentAssignmentItemDto> Items { get; set; } = new();
}

public class RegionContentAssignmentItemDto
{
    public string ContentType { get; set; } = string.Empty;
    public int ContentId { get; set; }
    public string ContentName { get; set; } = string.Empty;
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public int? CurrentRegionId { get; set; }
    public string? CurrentRegionName { get; set; }
    public int? MatchedRegionId { get; set; }
    public string? MatchedRegionName { get; set; }
    public string Status { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
}
