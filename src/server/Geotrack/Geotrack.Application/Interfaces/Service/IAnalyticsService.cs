using Geotrack.Application.DTOs.Analytics;

namespace Geotrack.Application.Interfaces.Service;

public interface IAnalyticsService
{
    Task IngestBatchAsync(
        AnalyticsEventBatchDto batch,
        AnalyticsContext context,
        CancellationToken cancellationToken);
}
