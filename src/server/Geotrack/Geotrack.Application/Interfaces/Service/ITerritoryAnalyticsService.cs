using Geotrack.Application.DTOs.Admin;

namespace Geotrack.Application.Interfaces.Service;

public interface ITerritoryAnalyticsService
{
    Task<TerritoryAnalyticsOverviewDto> GetOverviewAsync(
        int userId,
        TerritoryAnalyticsOverviewQuery query,
        CancellationToken cancellationToken);

    Task<string> ExportCsvAsync(
        int userId,
        TerritoryAnalyticsOverviewQuery query,
        CancellationToken cancellationToken);
}
