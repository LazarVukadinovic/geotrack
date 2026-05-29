using Geotrack.Application.DTOs.Admin;

namespace Geotrack.Application.Interfaces.Service;

public interface ILocalBoundaryImportService
{
    Task<BoundaryImportSummaryDto> ImportLocalBoundariesAsync(
        string admLevel,
        bool forceRefresh = false,
        string? countryIso3 = null,
        int? take = null,
        CancellationToken cancellationToken = default);

    Task<BoundaryReconciliationReportDto> GetAdm0ReconciliationAsync(
        CancellationToken cancellationToken = default);

    Task<BoundaryCandidateDiagnosticsDto> GetRegionCandidateDiagnosticsAsync(
        string countryIso3,
        string admLevel,
        CancellationToken cancellationToken = default);
}
