using Geotrack.Application.DTOs.Admin;

namespace Geotrack.Application.Interfaces.Service;

public interface IHealthScoreService
{
    HealthScorePeriod ResolvePeriod(string? period, DateTime? nowUtc = null);

    Task<IReadOnlyDictionary<int, TerritoryHealthDto>> GetRegionHealthScoresAsync(
        IReadOnlyCollection<int> regionIds,
        HealthScorePeriod period,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyDictionary<int, TerritoryHealthDto>> GetDestinationHealthScoresAsync(
        IReadOnlyCollection<int> destinationIds,
        int? regionId,
        HealthScorePeriod period,
        CancellationToken cancellationToken = default);
}

public sealed record HealthScorePeriod(string Key, string Label, DateTime Start, DateTime End);
