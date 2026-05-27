using Geotrack.Application.DTOs.Admin;

namespace Geotrack.Application.Interfaces.Service;

public interface IAdminTerritoryManagementService
{
    Task<AdminTerritoryPageResponseDto> GetTerritoriesAsync(AdminTerritoryPageRequest request, CancellationToken cancellationToken = default);

    Task<AdminTerritoryMapFeatureCollectionDto> GetTerritoryMapAsync(CancellationToken cancellationToken = default);

    Task<AdminTerritoryMapBoundaryFeatureCollectionDto> GetTerritoryMapBoundariesAsync(CancellationToken cancellationToken = default);

    Task<AdminTerritoryMapStatusCollectionDto> GetTerritoryMapStatusAsync(CancellationToken cancellationToken = default);

    Task<AdminTerritoryDetailDto> GetTerritoryDetailAsync(
        int territoryId,
        CancellationToken cancellationToken = default);

    Task<AdminTerritoryManagerSearchResponseDto> SearchManagerCandidatesAsync(
        string? query,
        int take,
        int? countryTerritoryId = null,
        CancellationToken cancellationToken = default);

    Task<AdminTerritoryManagerSearchResponseDto> GetAssignableManagersAsync(
        int territoryId,
        string? query,
        int take,
        CancellationToken cancellationToken = default);

    Task<AdminTerritoryDto> CreateTerritoryAsync(AdminCreateTerritoryRequestDto request, CancellationToken cancellationToken = default);

    Task<AdminTerritoryDetailDto> AssignManagerAsync(
        int territoryId,
        AdminAssignTerritoryManagerRequestDto request,
        CancellationToken cancellationToken = default);

    Task<AdminTerritoryDetailDto> ImportBoundaryAsync(
        int territoryId,
        CancellationToken cancellationToken = default);

    Task<AdminTerritoryDetailDto> ImportBoundaryAsync(
        AdminImportTerritoryBoundaryRequestDto request,
        CancellationToken cancellationToken = default);

    Task<GeoBoundariesBatchImportResultDto> ImportMissingBoundariesAsync(
        AdminImportTerritoryBoundariesRequestDto request,
        CancellationToken cancellationToken = default);

    Task<GeoBoundariesBatchImportResultDto> ImportAllBoundariesAsync(
        AdminImportTerritoryBoundariesRequestDto request,
        CancellationToken cancellationToken = default);

    Task<BoundaryDisplayGeometryRebuildResultDto> RebuildDisplayGeometryAsync(
        bool onlyMissing = true,
        string? iso3 = null,
        bool forceRefresh = false,
        CancellationToken cancellationToken = default);

    Task<BoundaryGeometryDebugDto> GetBoundaryDebugAsync(
        string iso3,
        CancellationToken cancellationToken = default);

    Task DeleteTerritoryAsync(int territoryId, CancellationToken cancellationToken = default);
}
