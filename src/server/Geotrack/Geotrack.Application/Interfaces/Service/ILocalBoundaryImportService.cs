using Geotrack.Application.DTOs.Admin;

namespace Geotrack.Application.Interfaces.Service;

public interface ILocalBoundaryImportService
{
    Task<BoundaryImportSummaryDto> ImportLocalBoundariesAsync(
        string admLevel,
        bool forceRefresh = false,
        CancellationToken cancellationToken = default);

    Task<BoundaryReconciliationReportDto> GetAdm0ReconciliationAsync(
        CancellationToken cancellationToken = default);

    Task<BoundaryCandidateDiagnosticsDto> GetRegionCandidateDiagnosticsAsync(
        string countryIso3,
        string admLevel,
        CancellationToken cancellationToken = default);
}
