using Geotrack.Application.DTOs.Admin;

namespace Geotrack.Application.Interfaces.Service;

public interface IRegionalAnalyticsService
{
    Task<RegionalAnalyticsOverviewDto> GetOverviewAsync(
        int userId,
        RegionalAnalyticsOverviewQuery query,
        CancellationToken cancellationToken);

    Task<RegionalMonthlyVisitsDto> GetMonthlyVisitsAsync(
        int userId,
        RegionalMonthlyVisitsQuery query,
        CancellationToken cancellationToken);

    Task<RegionalTagViewsAnalyticsResponseDto> GetTagViewsAsync(
        int userId,
        RegionalTagViewsQuery query,
        CancellationToken cancellationToken);

    Task<RegionalAnalyticsEntityDetailDto> GetEntityDetailAsync(
        int userId,
        RegionalAnalyticsEntityDetailQuery query,
        CancellationToken cancellationToken);

    Task<string> ExportCsvAsync(
        int userId,
        RegionalAnalyticsOverviewQuery query,
        CancellationToken cancellationToken);
}
