using Geotrack.Application.DTOs.Admin;

namespace Geotrack.Application.Interfaces.Service;

public interface IAdmBoundaryRegionSyncService
{
    Task<TerritoryRegionSetupStatusDto> GetRegionSetupStatusForTerritoryManagerAsync(
        int userId,
        int? territoryId = null,
        CancellationToken cancellationToken = default);

    Task<RegionSyncSummaryDto> SyncRegionsFromAdm1CandidatesAsync(
        int territoryId,
        bool dryRun = true,
        bool forceUpdateGeometry = false,
        bool createMissingRegions = true,
        bool linkExistingRegions = true,
        CancellationToken cancellationToken = default);

    Task<RegionSyncSummaryDto> SyncRegionsFromAdm1CandidatesForTerritoryManagerAsync(
        int userId,
        int? territoryId = null,
        bool dryRun = true,
        bool forceUpdateGeometry = false,
        bool createMissingRegions = true,
        bool linkExistingRegions = true,
        CancellationToken cancellationToken = default);
}
