using Geotrack.Application.DTOs.Analytics;

namespace Geotrack.Application.Interfaces.Service;

public interface IAnalyticsReadService
{
    Task<IReadOnlyList<AnalyticsTopEntityDto>> GetTopEntitiesAsync(
        DateTime fromUtc,
        DateTime toUtc,
        string? eventType,
        int limit,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<AnalyticsTopDestinationDto>> GetTopDestinationsAsync(
        DateTime fromUtc,
        DateTime toUtc,
        int limit,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<AnalyticsOutboundProviderClickDto>> GetOutboundProviderClicksAsync(
        DateTime fromUtc,
        DateTime toUtc,
        int limit,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<AnalyticsSearchTrendDto>> GetSearchTrendsAsync(
        DateTime fromUtc,
        DateTime toUtc,
        int limit,
        CancellationToken cancellationToken);
}
