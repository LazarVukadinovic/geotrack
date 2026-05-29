namespace Geotrack.Application.DTOs.Admin;

public class RegionSyncSummaryDto
{
    public int TerritoryId { get; set; }
    public string TerritoryName { get; set; } = string.Empty;
    public string CountryIso3 { get; set; } = string.Empty;
    public int TotalCandidates { get; set; }
    public int ExistingLinkedCount { get; set; }
    public int LinkedExistingCount { get; set; }
    public int CreatedCount { get; set; }
    public int UpdatedCount { get; set; }
    public int SkippedCount { get; set; }
    public int AmbiguousCount { get; set; }
    public int FailedCount { get; set; }
    public bool DryRun { get; set; }
    public List<RegionSyncItemDto> Items { get; set; } = new();
}

public class RegionSyncItemDto
{
    public int CandidateId { get; set; }
    public string CandidateName { get; set; } = string.Empty;
    public string CandidateExternalKey { get; set; } = string.Empty;
    public int? RegionId { get; set; }
    public string? RegionName { get; set; }
    public string Status { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
}
