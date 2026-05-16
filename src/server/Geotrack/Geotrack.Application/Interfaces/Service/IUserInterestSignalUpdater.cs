using Geotrack.Domain.Entities.Analytics;

namespace Geotrack.Application.Interfaces.Service;

/// <summary>
/// Applies inferred-interest score deltas to explicit user-facing interest topics,
/// derived from the tags of the entity touched by an analytics event. Legacy
/// tag-level signals can still be maintained by the implementation for
/// backwards compatibility.
/// The caller owns the surrounding transaction/SaveChanges.
/// </summary>
public interface IUserInterestSignalUpdater
{
    Task ApplyAsync(int userId, AnalyticsEvent enrichedEvent, CancellationToken cancellationToken);
}
