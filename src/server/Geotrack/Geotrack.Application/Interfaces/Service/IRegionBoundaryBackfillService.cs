using Geotrack.Application.DTOs.Admin;

namespace Geotrack.Application.Interfaces.Service;

public interface IRegionBoundaryBackfillService
{
    Task<RegionBoundaryBackfillSummaryDto> BackfillBoundaryCandidatesAsync(
        string? countryIso3 = null,
        bool dryRun = true,
        bool force = false,
        CancellationToken cancellationToken = default);
}
