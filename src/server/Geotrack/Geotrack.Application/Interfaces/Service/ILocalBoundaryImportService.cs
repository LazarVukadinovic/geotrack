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
}
