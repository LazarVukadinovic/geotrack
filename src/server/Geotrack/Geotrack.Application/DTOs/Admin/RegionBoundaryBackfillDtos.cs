namespace Geotrack.Application.DTOs.Admin;

public class RegionBoundaryBackfillSummaryDto
{
    public int TotalRegionsChecked { get; set; }
    public int MatchedCount { get; set; }
    public int UpdatedCount { get; set; }
    public int SkippedCount { get; set; }
    public int AmbiguousCount { get; set; }
    public int FailedCount { get; set; }
    public List<RegionBoundaryBackfillItemDto> Items { get; set; } = new();
}

public class RegionBoundaryBackfillItemDto
{
    public int RegionId { get; set; }
    public string RegionName { get; set; } = string.Empty;
    public string? CountryIso3 { get; set; }
    public int? MatchedCandidateId { get; set; }
    public string? MatchedCandidateName { get; set; }
    public string? MatchedExternalKey { get; set; }
    public string Status { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
}
