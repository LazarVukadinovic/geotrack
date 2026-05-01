using Geotrack.Application.Analytics;
using Geotrack.Application.Interfaces.Service;
using Geotrack.Domain.Entities.Analytics;
using Microsoft.EntityFrameworkCore;

namespace Geotrack.Infrastructure.Services;

public class UserInterestSignalUpdater : IUserInterestSignalUpdater
{
    private const double ScoreCap = 1000.0;
    private const double RedisplayEvidenceThreshold = 10.0;

    // Per-event-type weights. Events not present here record raw rows but do
    // NOT contribute interest deltas — they don't reliably indicate interest
    // in a specific tag set (page_view, login_completed, agent_prompt_sent, etc.).
    private static readonly IReadOnlyDictionary<string, double> Weights =
        new Dictionary<string, double>(StringComparer.Ordinal)
        {
            [AnalyticsEventTypes.DetailView] = 0.75,
            [AnalyticsEventTypes.SearchPerformed] = 1.0,
            [AnalyticsEventTypes.FilterApplied] = 1.0,
            [AnalyticsEventTypes.MapMarkerOpened] = 0.75,
            [AnalyticsEventTypes.AgentRecommendationOpened] = 2.5,
            [AnalyticsEventTypes.DirectionsStarted] = 3.5,
            [AnalyticsEventTypes.OutboundLinkClicked] = 5.0,
            [AnalyticsEventTypes.FavoriteAdded] = 5.0,
            [AnalyticsEventTypes.CollectionItemAdded] = 5.0,
            [AnalyticsEventTypes.ReviewCreated] = 7.0,
            [AnalyticsEventTypes.FavoriteRemoved] = -3.0,
            [AnalyticsEventTypes.CollectionItemRemoved] = -3.0,
        };

    private static readonly IReadOnlyDictionary<string, TimeSpan> ScoringCooldowns =
        new Dictionary<string, TimeSpan>(StringComparer.Ordinal)
        {
            [AnalyticsEventTypes.DetailView] = TimeSpan.FromMinutes(20),
            [AnalyticsEventTypes.MapMarkerOpened] = TimeSpan.FromMinutes(5),
            [AnalyticsEventTypes.AgentRecommendationOpened] = TimeSpan.FromMinutes(10),
            [AnalyticsEventTypes.DirectionsStarted] = TimeSpan.FromMinutes(10),
            [AnalyticsEventTypes.OutboundLinkClicked] = TimeSpan.FromMinutes(5),
        };

    private readonly AppDbContext _db;

    public UserInterestSignalUpdater(AppDbContext db)
    {
        _db = db;
    }

    public async Task ApplyAsync(int userId, AnalyticsEvent enrichedEvent, CancellationToken cancellationToken)
    {
        if (!Weights.TryGetValue(enrichedEvent.EventType, out var weight))
        {
            return;
        }

        if (enrichedEvent.EntityType is null || enrichedEvent.EntityId is null)
        {
            return;
        }

        var tagIds = await ResolveTagIdsAsync(enrichedEvent.EntityType, enrichedEvent.EntityId.Value, cancellationToken);
        if (tagIds.Count == 0)
        {
            return;
        }

        var reason = $"{enrichedEvent.EventType}:{enrichedEvent.EntityType}#{enrichedEvent.EntityId.Value}";
        var now = DateTime.UtcNow;

        // Load existing signals from the DB for this user across the touched tags.
        // Note: this also primes the ChangeTracker so subsequent calls within the
        // same batch (same DbContext) see them via .Local without another query.
        var fromDb = await _db.UserInterestSignals
            .Where(s => s.UserId == userId && tagIds.Contains(s.TagId))
            .ToListAsync(cancellationToken);

        // Combine with pending-Added entities so multiple events in the same batch
        // touching the same (user, tag) pair update one row instead of trying to
        // insert duplicates that would violate the unique index.
        var byTagId = _db.UserInterestSignals.Local
            .Where(s => s.UserId == userId && tagIds.Contains(s.TagId))
            .GroupBy(s => s.TagId)
            .ToDictionary(g => g.Key, g => g.First());

        foreach (var dbSignal in fromDb)
        {
            byTagId[dbSignal.TagId] = dbSignal;
        }

        foreach (var tagId in tagIds)
        {
            if (byTagId.TryGetValue(tagId, out var signal))
            {
                if (ShouldSkipScoring(signal, enrichedEvent.EventType, reason, now, weight))
                {
                    continue;
                }

                signal.Score = Math.Clamp(signal.Score + weight, -ScoreCap, ScoreCap);
                if (signal.IsDismissed && weight > 0)
                {
                    signal.EvidenceSinceDismissal = Math.Clamp(
                        signal.EvidenceSinceDismissal + weight,
                        0.0,
                        ScoreCap);

                    if (signal.EvidenceSinceDismissal >= RedisplayEvidenceThreshold && signal.Score > 0)
                    {
                        signal.IsDismissed = false;
                        signal.DismissedAt = null;
                        signal.DismissedAtScore = null;
                        signal.EvidenceSinceDismissal = 0.0;
                    }
                }
                signal.LastInteractionAt = now;
                signal.LastReason = reason;
            }
            else
            {
                var fresh = new UserInterestSignal
                {
                    UserId = userId,
                    TagId = tagId,
                    Score = Math.Clamp(weight, -ScoreCap, ScoreCap),
                    Source = "inferred",
                    LastInteractionAt = now,
                    LastReason = reason,
                };
                _db.UserInterestSignals.Add(fresh);
                byTagId[tagId] = fresh;
            }
        }
    }

    private static bool ShouldSkipScoring(
        UserInterestSignal signal,
        string eventType,
        string reason,
        DateTime now,
        double weight)
    {
        if (weight <= 0)
        {
            return false;
        }

        return ScoringCooldowns.TryGetValue(eventType, out var cooldown) &&
            string.Equals(signal.LastReason, reason, StringComparison.Ordinal) &&
            now - signal.LastInteractionAt < cooldown;
    }

    private async Task<List<int>> ResolveTagIdsAsync(string entityType, int entityId, CancellationToken ct)
    {
        return entityType switch
        {
            AnalyticsEntityTypes.Place or AnalyticsEntityTypes.Hotel =>
                await _db.PlaceTags
                    .Where(pt => pt.PlaceId == entityId)
                    .Select(pt => pt.TagId)
                    .ToListAsync(ct),

            AnalyticsEntityTypes.Event =>
                await _db.EventTags
                    .Where(et => et.EventId == entityId)
                    .Select(et => et.TagId)
                    .ToListAsync(ct),

            AnalyticsEntityTypes.Destination =>
                await _db.DestinationTags
                    .Where(dt => dt.DestinationId == entityId)
                    .Select(dt => dt.TagId)
                    .ToListAsync(ct),

            _ => new List<int>(),
        };
    }
}
