using Geotrack.Application.DTOs.Admin;

namespace Geotrack.Application.Interfaces.Service;

public interface IGeoBoundariesService
{
    Task<GeoBoundariesImportResultDto> ImportAdm0BoundaryAsync(
        string iso3,
        int? territoryId = null,
        CancellationToken cancellationToken = default,
        bool forceRefresh = false);

    Task<GeoBoundariesBatchImportResultDto> ImportMissingAdm0BoundariesAsync(
        CancellationToken cancellationToken = default,
        bool forceRefresh = false);

    Task<GeoBoundariesBatchImportResultDto> ImportAllAdm0BoundariesAsync(
        CancellationToken cancellationToken = default,
        bool forceRefresh = false);
}

public class GeoBoundariesImportResultDto
{
    public int TerritoryId { get; set; }
    public string? Iso2 { get; set; }
    public string Iso3 { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string SourceUrl { get; set; } = string.Empty;
    public string? License { get; set; }
    public string ReleaseType { get; set; } = "gbOpen";
    public string AdmLevel { get; set; } = "ADM0";
    public AdminTerritoryGeometryDto BoundaryGeoJson { get; set; } = new();
    public bool Skipped { get; set; }
}

public class GeoBoundariesBatchImportResultDto
{
    public string ReleaseType { get; set; } = "gbOpen";
    public string AdmLevel { get; set; } = "ADM0";
    public int RequestedCount { get; set; }
    public int ImportedCount { get; set; }
    public int SkippedCount { get; set; }
    public int FailedCount { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime CompletedAt { get; set; }
    public List<GeoBoundariesImportResultDto> Results { get; set; } = new();
    public List<GeoBoundariesImportFailureDto> Failures { get; set; } = new();
}

public class GeoBoundariesImportFailureDto
{
    public int? TerritoryId { get; set; }
    public string Iso3 { get; set; } = string.Empty;
    public string? Name { get; set; }
    public string Error { get; set; } = string.Empty;
}
