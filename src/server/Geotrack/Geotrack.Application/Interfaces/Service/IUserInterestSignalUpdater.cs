using Geotrack.Domain.Entities.Analytics;

namespace Geotrack.Application.Interfaces.Service;

/// <summary>
/// Applies inferred-interest score deltas to <c>UserInterestSignals</c> for an
/// authenticated user, derived from the tags of the entity touched by an event.
/// Strictly separate from explicit <c>UserTag</c> selections.
/// The caller owns the surrounding transaction/SaveChanges.
/// </summary>
public interface IUserInterestSignalUpdater
{
    Task ApplyAsync(int userId, AnalyticsEvent enrichedEvent, CancellationToken cancellationToken);
}
