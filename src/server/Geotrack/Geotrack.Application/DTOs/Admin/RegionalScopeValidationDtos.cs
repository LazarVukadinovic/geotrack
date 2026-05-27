namespace Geotrack.Application.DTOs.Admin;

public static class RegionalScopeValidationModes
{
    public const string InsideAssignedRegion = nameof(InsideAssignedRegion);
    public const string AllowedWithinToleranceInsideAssignedCountry = nameof(AllowedWithinToleranceInsideAssignedCountry);
    public const string RejectedOutsideAssignedCountry = nameof(RejectedOutsideAssignedCountry);
    public const string AllowedWithinToleranceOutsideRegion = nameof(AllowedWithinToleranceOutsideRegion);
    public const string RejectedInsideAnotherRegion = nameof(RejectedInsideAnotherRegion);
    public const string RejectedTooFarOutsideRegion = nameof(RejectedTooFarOutsideRegion);
    public const string RejectedMissingAssignedRegion = nameof(RejectedMissingAssignedRegion);
    public const string RejectedMissingAssignedCountryBoundary = nameof(RejectedMissingAssignedCountryBoundary);
    public const string RejectedMissingRegionBoundary = nameof(RejectedMissingRegionBoundary);
    public const string RejectedMissingBoundary = nameof(RejectedMissingBoundary);
}

public class RegionalScopeValidationRequestDto
{
    public int RegionId { get; set; }
    public string? EntityType { get; set; }
    public string? OperationType { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public int? DestinationId { get; set; }
    public int? LocationId { get; set; }
    public int? PlaceId { get; set; }
    public string? ParentEntityType { get; set; }
}

public class RegionalScopeValidationResultDto
{
    public bool IsAllowed { get; set; }
    public string ValidationMode { get; set; } = RegionalScopeValidationModes.RejectedTooFarOutsideRegion;
    public int? AssignedRegionId { get; set; }
    public string? AssignedRegionName { get; set; }
    public int? AssignedCountryOrTerritoryId { get; set; }
    public string? AssignedCountryOrTerritoryName { get; set; }
    public int? MatchedOtherRegionId { get; set; }
    public string? MatchedOtherRegionName { get; set; }
    public double? DistanceFromAssignedRegionMeters { get; set; }
    public string Message { get; set; } = string.Empty;
}
