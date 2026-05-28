using System.Text.Json;
using System.Text.Json.Serialization;

namespace Geotrack.Application.DTOs.Admin;

public class AdminTerritoryPageRequest
{
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 100;
    public string? Search { get; set; }
    public string? SortField { get; set; }
    public string? SortDir { get; set; }
    public List<string> CountryFilter { get; set; } = new();
}

public class AdminTerritoryPageResponseDto
{
    public List<AdminTerritoryDto> Items { get; set; } = new();
    public int Total { get; set; }
}

public class AdminTerritoryDto
{
    public int TerritoryId { get; set; }
    public int CountryId { get; set; }
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string InternalReference { get; set; } = string.Empty;
    public string TerritoryKind { get; set; } = "Country";
    public string? CountryCode { get; set; }
    public string? Iso2 { get; set; }
    public string? Iso3 { get; set; }
    public List<string> Countries { get; set; } = new();
    public int? AssignedManagerUserId { get; set; }
    public string? AssignedManagerId { get; set; }
    public string? AssignedManagerName { get; set; }
    public string? AssignedManagerEmail { get; set; }
    public string Status { get; set; } = "Unassigned";
    public string? BoundarySource { get; set; }
    public string? BoundarySourceUrl { get; set; }
    public string? BoundaryShapeType { get; set; }
    public string? BoundaryShapeGroup { get; set; }
    public string? BoundaryShapeId { get; set; }
    public string? BoundaryExternalKey { get; set; }
    public string? BoundaryAdmLevel { get; set; }
    public string? BoundaryReleaseType { get; set; }
    public string? BoundaryLicense { get; set; }
    public DateTime? BoundaryImportedAt { get; set; }
    public bool HasBoundary { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class AdminTerritoryDetailDto : AdminTerritoryDto
{
    public AdminTerritoryAssignedManagerDto? AssignedManager { get; set; }
    public AdminTerritoryGeometryDto? BoundaryGeoJson { get; set; }
    public AdminTerritoryMapViewportDto Viewport { get; set; } = new();
    public string? BoundaryRequiredMessage { get; set; }
    public bool CanAssign { get; set; }
}

public class AdminTerritoryAssignedManagerDto
{
    public int UserId { get; set; }
    public string Id { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? AvatarUrl { get; set; }
    public string Initials { get; set; } = string.Empty;
    public List<string> Roles { get; set; } = new();
    public DateTime AssignedAt { get; set; }
}

public class AdminTerritoryGeometryDto
{
    public string Type { get; set; } = string.Empty;
    public object Coordinates { get; set; } = Array.Empty<double>();
}

public class AdminTerritoryMapBoundsDto
{
    public double North { get; set; }
    public double South { get; set; }
    public double East { get; set; }
    public double West { get; set; }
}

public class AdminTerritoryMapViewportDto
{
    public double? CenterLatitude { get; set; }
    public double? CenterLongitude { get; set; }
    public double? Zoom { get; set; }
    public AdminTerritoryMapBoundsDto? Bounds { get; set; }
    public bool IsEmpty { get; set; }
}

public class AdminTerritoryManagerSearchResponseDto
{
    public List<AdminTerritoryManagerCandidateDto> Items { get; set; } = new();
}

public class AdminTerritoryManagerCandidateDto
{
    public int UserId { get; set; }
    public string Id { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? AvatarUrl { get; set; }
    public string Initials { get; set; } = string.Empty;
    public List<string> Roles { get; set; } = new();
    public List<AdminTerritoryManagerAssignedTerritoryDto> AssignedTerritories { get; set; } = new();
    public string Status { get; set; } = "Active";
    public bool IsActive { get; set; }
    public bool CanAssign { get; set; }
    public string? IneligibilityReason { get; set; }
}

public class AdminTerritoryManagerAssignedTerritoryDto
{
    public int TerritoryId { get; set; }
    public string TerritoryName { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string? CountryCode { get; set; }
}

public class AdminCreateTerritoryRequestDto
{
    public string? Name { get; set; }
    public string? CountryCode { get; set; }
    public int? AssignedManagerUserId { get; set; }
    public AdminCountryBoundaryDto? Boundary { get; set; }
}

public class AdminAssignTerritoryManagerRequestDto
{
    public int? AssignedManagerUserId { get; set; }
    public int? ManagerUserId { get; set; }
}

public class AdminCountryBoundaryDto
{
    public string? Type { get; set; }
    public JsonElement Coordinates { get; set; }
    public string? Source { get; set; }
}

public class AdminTerritoryMapFeatureCollectionDto
{
    public string Type { get; set; } = "FeatureCollection";
    public List<AdminTerritoryMapFeatureDto> Features { get; set; } = new();
    public string Attribution { get; set; } =
        "Boundary data provided by geoBoundaries (gbOpen), licensed under CC BY 4.0.";
    public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;

    [JsonIgnore]
    public string? ETag { get; set; }

    [JsonIgnore]
    public DateTime? LastModified { get; set; }
}

public class AdminTerritoryMapBoundaryFeatureCollectionDto
{
    public string Type { get; set; } = "FeatureCollection";
    public List<AdminTerritoryMapBoundaryFeatureDto> Features { get; set; } = new();
    public string Attribution { get; set; } =
        "Boundary data provided by geoBoundaries (gbOpen), licensed under CC BY 4.0.";
    public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;

    [JsonIgnore]
    public string? ETag { get; set; }

    [JsonIgnore]
    public DateTime? LastModified { get; set; }
}

public class AdminTerritoryMapBoundaryFeatureDto
{
    public string Type { get; set; } = "Feature";
    public AdminTerritoryGeometryDto? Geometry { get; set; }
    public AdminTerritoryMapBoundaryPropertiesDto Properties { get; set; } = new();
}

public class AdminTerritoryMapBoundaryPropertiesDto
{
    public int? TerritoryId { get; set; }
    public int? CountryId { get; set; }
    public string TerritoryKind { get; set; } = "Country";
    public string? Iso2 { get; set; }
    public string? Iso3 { get; set; }
    public string? CountryCode { get; set; }
    public string Name { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string? ShapeGroup { get; set; }
    public string? ShapeType { get; set; }
    public string? ShapeId { get; set; }
    public bool HasDisplayBoundary { get; set; }
    public AdminTerritoryMapViewportDto Viewport { get; set; } = new();
}

public class AdminTerritoryMapStatusCollectionDto
{
    public List<AdminTerritoryMapStatusDto> Items { get; set; } = new();
    public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;

    [JsonIgnore]
    public string? ETag { get; set; }

    [JsonIgnore]
    public DateTime? LastModified { get; set; }
}

public class AdminTerritoryMapStatusDto
{
    public int? TerritoryId { get; set; }
    public int? CountryId { get; set; }
    public string TerritoryKind { get; set; } = "Country";
    public string? Iso2 { get; set; }
    public string? Iso3 { get; set; }
    public string? CountryCode { get; set; }
    public string Name { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Status { get; set; } = "MissingBoundary";
    public string? ShapeGroup { get; set; }
    public string? ShapeType { get; set; }
    public string? ShapeId { get; set; }
    public AdminTerritoryMapAssignedManagerDto? AssignedManager { get; set; }
    public bool HasBoundary { get; set; }
    public bool HasValidationBoundary { get; set; }
    public bool HasDisplayBoundary { get; set; }
    public bool CanAssign { get; set; }
    public bool CanEdit { get; set; }
    public string? BoundaryRequiredMessage { get; set; }
    public string? BoundaryMissingReason { get; set; }
}

public class AdminTerritoryMapFeatureDto
{
    public string Type { get; set; } = "Feature";
    public AdminTerritoryGeometryDto? Geometry { get; set; }
    public AdminTerritoryMapFeaturePropertiesDto Properties { get; set; } = new();
}

public class AdminTerritoryMapFeaturePropertiesDto
{
    public int? TerritoryId { get; set; }
    public int? CountryId { get; set; }
    public string TerritoryKind { get; set; } = "Country";
    public string? Iso2 { get; set; }
    public string? Iso3 { get; set; }
    public string? CountryCode { get; set; }
    public string Name { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Status { get; set; } = "MissingBoundary";
    public string? ShapeGroup { get; set; }
    public string? ShapeType { get; set; }
    public string? ShapeId { get; set; }
    public AdminTerritoryMapAssignedManagerDto? AssignedManager { get; set; }
    public bool HasBoundary { get; set; }
    public bool HasValidationBoundary { get; set; }
    public bool HasDisplayBoundary { get; set; }
    public bool CanAssign { get; set; }
    public bool CanEdit { get; set; }
    public string? BoundaryRequiredMessage { get; set; }
    public string? BoundaryMissingReason { get; set; }
    public string? BoundarySource { get; set; }
    public string? BoundarySourceUrl { get; set; }
    public string? BoundaryShapeType { get; set; }
    public string? BoundaryShapeGroup { get; set; }
    public string? BoundaryShapeId { get; set; }
    public string? BoundaryExternalKey { get; set; }
    public string? BoundaryAdmLevel { get; set; }
    public string? BoundaryReleaseType { get; set; }
    public string? BoundaryLicense { get; set; }
    public DateTime? BoundaryImportedAt { get; set; }
    public AdminTerritoryMapViewportDto Viewport { get; set; } = new();
    public DateTime? CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

public class AdminTerritoryMapAssignedManagerDto
{
    public int UserId { get; set; }
    public string Id { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Initials { get; set; } = string.Empty;
}

public class AdminImportTerritoryBoundaryRequestDto
{
    public string? Iso3 { get; set; }
}

public class AdminImportTerritoryBoundariesRequestDto
{
    public bool ForceRefresh { get; set; }
}

public class BoundaryImportSummaryDto
{
    public string Source { get; set; } = "geoBoundaries";
    public string AdmLevel { get; set; } = "ADM0";
    public string SourcePath { get; set; } = string.Empty;
    public int TotalFeatures { get; set; }
    public int ProcessedCount { get; set; }
    public int ImportedCount { get; set; }
    public int UpdatedCount { get; set; }
    public int SkippedCount { get; set; }
    public int FailedCount { get; set; }
    public int ImportedCountriesCount { get; set; }
    public int UpdatedCountriesCount { get; set; }
    public int SkippedCountriesCount { get; set; }
    public int FailedCountriesCount { get; set; }
    public int ImportedDisputedCount { get; set; }
    public int UpdatedDisputedCount { get; set; }
    public int SkippedDisputedCount { get; set; }
    public int FailedDisputedCount { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime CompletedAt { get; set; }
    public long DurationMs { get; set; }
    public bool ItemsTruncated { get; set; }
    public List<BoundaryImportItemDto> Items { get; set; } = new();
}

public class BoundaryImportItemDto
{
    public string Kind { get; set; } = "Country";
    public string? Iso3 { get; set; }
    public string? ShapeGroup { get; set; }
    public string? Name { get; set; }
    public string? ShapeId { get; set; }
    public string? ShapeType { get; set; }
    public string Status { get; set; } = "Skipped";
    public string? Message { get; set; }
    public long DurationMs { get; set; }
}

public class BoundaryDisplayGeometryRebuildResultDto
{
    public int ScannedCount { get; set; }
    public int TotalChecked { get; set; }
    public int RebuiltCount { get; set; }
    public int SkippedCount { get; set; }
    public int FailedCount { get; set; }
    public bool ForceRefresh { get; set; }
    public bool OnlyMissing { get; set; } = true;
    public string? Iso3 { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime CompletedAt { get; set; }
    public List<BoundaryDisplayGeometryRebuildItemDto> Items { get; set; } = new();
}

public class BoundaryDisplayGeometryRebuildItemDto
{
    public int TerritoryId { get; set; }
    public string? Iso3 { get; set; }
    public string Name { get; set; } = string.Empty;
    public string TerritoryKind { get; set; } = "Country";
    public string Status { get; set; } = "Skipped";
    public string Message { get; set; } = string.Empty;
}

public class BoundaryGeometryDebugDto
{
    public int TerritoryId { get; set; }
    public string? Iso3 { get; set; }
    public string Name { get; set; } = string.Empty;
    public string TerritoryKind { get; set; } = "Country";
    public string? FullGeometryType { get; set; }
    public int FullGeometryNumGeometries { get; set; }
    public int FullGeometryCoordinateCount { get; set; }
    public int CoordinateCountFull { get; set; }
    public AdminTerritoryMapBoundsDto? FullGeometryEnvelope { get; set; }
    public double? FullArea { get; set; }
    public double? FullGeometryArea { get; set; }
    public double? LargestFullPolygonArea { get; set; }
    public AdminTerritoryMapBoundsDto? LargestFullPolygonEnvelope { get; set; }
    public bool IsFullGeometryValid { get; set; }
    public string? DisplayGeometryType { get; set; }
    public int DisplayGeometryNumGeometries { get; set; }
    public int DisplayGeometryCoordinateCount { get; set; }
    public int CoordinateCountDisplay { get; set; }
    public AdminTerritoryMapBoundsDto? DisplayGeometryEnvelope { get; set; }
    public double? DisplayArea { get; set; }
    public double? DisplayGeometryArea { get; set; }
    public double? DisplayAreaRatio { get; set; }
    public double? FullToDisplayAreaRatio { get; set; }
    public double? LargestDisplayPolygonArea { get; set; }
    public AdminTerritoryMapBoundsDto? LargestDisplayPolygonEnvelope { get; set; }
    public bool IsDisplayGeometryValid { get; set; }
    public bool HasAntimeridianCrossing { get; set; }
    public bool CrossesAntimeridian { get; set; }
    public AdminTerritoryMapViewportDto Viewport { get; set; } = new();
}

public class BoundaryReconciliationReportDto
{
    public string Source { get; set; } = "geoBoundaries";
    public string AdmLevel { get; set; } = "ADM0";
    public string SourcePath { get; set; } = string.Empty;
    public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;
    public int SourceCountryFeatureCount { get; set; }
    public int SourceDisputedFeatureCount { get; set; }
    public int CountriesWithFullBoundary { get; set; }
    public int CountriesMissingFullBoundary { get; set; }
    public int CountriesWithFullBoundaryMissingDisplayGeometry { get; set; }
    public int DisputedTerritoriesImported { get; set; }
    public List<BoundaryReconciliationItemDto> MissingFullBoundary { get; set; } = new();
    public List<BoundaryReconciliationItemDto> MissingDisplayGeometry { get; set; } = new();
    public List<BoundaryReconciliationItemDto> DisputedTerritories { get; set; } = new();
    public List<BoundaryReconciliationConflictDto> DuplicateNameConflicts { get; set; } = new();
    public List<BoundarySourceFeatureReconciliationDto> SourceFeaturesMissingInDb { get; set; } = new();
    public List<BoundaryReconciliationItemDto> DbTerritoriesNotFoundInSource { get; set; } = new();
}

public class BoundaryReconciliationItemDto
{
    public int? TerritoryId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string TerritoryKind { get; set; } = "Country";
    public string? Iso2 { get; set; }
    public string? Iso3 { get; set; }
    public string? ShapeGroup { get; set; }
    public string? ShapeType { get; set; }
    public string? ShapeId { get; set; }
    public bool HasBoundary { get; set; }
    public bool HasValidationBoundary { get; set; }
    public bool HasDisplayBoundary { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? BoundaryMissingReason { get; set; }
}

public class BoundarySourceFeatureReconciliationDto
{
    public string Kind { get; set; } = "Country";
    public string? Iso3 { get; set; }
    public string? ShapeGroup { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? ShapeType { get; set; }
    public string? ShapeId { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? Message { get; set; }
}

public class BoundaryReconciliationConflictDto
{
    public string Name { get; set; } = string.Empty;
    public string SourceKind { get; set; } = "Country";
    public string? SourceIso3 { get; set; }
    public string? SourceShapeGroup { get; set; }
    public string? SourceShapeType { get; set; }
    public int TerritoryId { get; set; }
    public string TerritoryKind { get; set; } = "Country";
    public string? TerritoryIso3 { get; set; }
    public string? TerritoryShapeGroup { get; set; }
    public string Message { get; set; } = string.Empty;
}

public class BoundaryRegionCandidateResponseDto
{
    public string Source { get; set; } = "geoBoundaries";
    public string AdmLevel { get; set; } = "ADM1";
    public string Iso3 { get; set; } = string.Empty;
    public int ScannedFeatures { get; set; }
    public int MatchedFeatures { get; set; }
    public int ReturnedCount { get; set; }
    public List<BoundaryRegionCandidateDto> Items { get; set; } = new();
    public string Attribution { get; set; } =
        "Boundary data provided by geoBoundaries (gbOpen), licensed under CC BY 4.0.";
}

public class BoundaryRegionCandidateDto
{
    public string FeatureKind { get; set; } = "Adm1Candidate";
    public string CandidateId { get; set; } = string.Empty;
    public int BoundaryCandidateId { get; set; }
    public string ExternalKey { get; set; } = string.Empty;
    public string BoundaryExternalKey { get; set; } = string.Empty;
    public string AdmLevel { get; set; } = string.Empty;
    public string Iso3 { get; set; } = string.Empty;
    public string CountryIso3 { get; set; } = string.Empty;
    public string? ShapeGroup { get; set; }
    public string? ShapeId { get; set; }
    public string ShapeName { get; set; } = string.Empty;
    public string ShapeType { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Status { get; set; } = "Available";
    public bool HasBoundary { get; set; }
    public bool HasDisplayBoundary { get; set; }
    public bool CanSelect { get; set; }
    public int? ExistingRegionId { get; set; }
    public string? ExistingRegionName { get; set; }
    public bool IsInsideAssignedTerritory { get; set; }
    public string? ValidationMessage { get; set; }
    public TerritoryRegionMapViewportDto Viewport { get; set; } = new();
    public TerritoryRegionGeometryDto? GeometryGeoJson { get; set; }
}

public class BoundaryCandidateDiagnosticsDto
{
    public string Source { get; set; } = "geoBoundaries";
    public string AdmLevel { get; set; } = "ADM1";
    public string CountryIso3 { get; set; } = string.Empty;
    public int? TerritoryId { get; set; }
    public string? TerritoryName { get; set; }
    public int TotalCandidatesForIso3 { get; set; }
    public int CandidatesWithBoundaryGeometry { get; set; }
    public int CandidatesWithDisplayGeometry { get; set; }
    public int CandidatesPassingStrictCovers { get; set; }
    public int CandidatesPassingIntersects { get; set; }
    public int CandidatesFilteredOutBySpatialCheck { get; set; }
    public int CandidatesReturned { get; set; }
    public int ExistingRegionMatches { get; set; }
}
