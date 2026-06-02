using Geotrack.Application.DTOs.Analytics;

namespace Geotrack.Application.Interfaces.Service;

public interface IAnalyticsRetentionService
{
    Task<AnalyticsRetentionCleanupResultDto> CleanupExpiredAsync(
        DateTime utcNow,
        bool execute,
        CancellationToken cancellationToken = default);
}
