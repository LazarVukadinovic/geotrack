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
    public DateTime CompletedAt { get; set; }
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

public class TerritoryRegionSetupStatusDto
{
    public int TerritoryId { get; set; }
    public string TerritoryName { get; set; } = string.Empty;
    public string CountryIso3 { get; set; } = string.Empty;
    public bool RequiresInitialSync { get; set; }
    public bool OnboardingCompleted { get; set; }
    public bool HasSyncedRegions { get; set; }
    public int SyncedRegionCount { get; set; }
    public int CandidateCount { get; set; }
    public bool HasCandidateData { get; set; }
    public bool CanSync { get; set; }
    public bool CanUpdate { get; set; }
    public bool UpdateAvailable { get; set; }
    public bool MissingCandidateData { get; set; }
    public DateTime? RegionSetupCompletedAt { get; set; }
    public DateTime? LastSyncedAt { get; set; }
    public string? CandidateVersionHash { get; set; }
    public string? SyncedVersionHash { get; set; }
    public string Message { get; set; } = string.Empty;
}
