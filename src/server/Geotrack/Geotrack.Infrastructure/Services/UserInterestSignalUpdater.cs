using Geotrack.Application.Analytics;
using Geotrack.Application.Interfaces.Service;
using Geotrack.Domain.Entities.Analytics;
using Geotrack.Domain.Entities.Interests;
using Microsoft.EntityFrameworkCore;

namespace Geotrack.Infrastructure.Services;

public class UserInterestSignalUpdater : IUserInterestSignalUpdater
{
    private const double ScoreCap = 1000.0;
    private const double RedisplayEvidenceThreshold = 10.0;

    // Per-event-type weights. Events not present here record raw rows but do
    // NOT contribute interest deltas â€” they don't reliably indicate interest
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

        // Keep legacy tag-level signals populated for existing analytics tests and for backwards compatibility.
        await ApplyTagSignalsAsync(userId, tagIds, weight, enrichedEvent.EventType, reason, now, cancellationToken);

        // New system: map content tags to user-facing interest topics and score those topics.
        var topicWeights = await ResolveInterestTopicWeightsAsync(tagIds, cancellationToken);
        if (topicWeights.Count > 0)
        {
            await ApplyTopicSignalsAsync(userId, topicWeights, weight, enrichedEvent.EventType, reason, now, cancellationToken);
        }
    }

    private async Task ApplyTagSignalsAsync(
        int userId,
        IReadOnlyCollection<int> tagIds,
        double weight,
        string eventType,
        string reason,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var fromDb = await _db.UserInterestSignals
            .Where(s => s.UserId == userId && tagIds.Contains(s.TagId))
            .ToListAsync(cancellationToken);

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
                ApplyScore(signal, weight, eventType, reason, now);
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

    private async Task ApplyTopicSignalsAsync(
        int userId,
        IReadOnlyDictionary<int, double> topicWeights,
        double eventWeight,
        string eventType,
        string reason,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var topicIds = topicWeights.Keys.ToList();

        var fromDb = await _db.UserInterestTopicSignals
            .Where(s => s.UserId == userId && topicIds.Contains(s.InterestTopicId))
            .ToListAsync(cancellationToken);

        var byTopicId = _db.UserInterestTopicSignals.Local
            .Where(s => s.UserId == userId && topicIds.Contains(s.InterestTopicId))
            .GroupBy(s => s.InterestTopicId)
            .ToDictionary(g => g.Key, g => g.First());

        foreach (var dbSignal in fromDb)
        {
            byTopicId[dbSignal.InterestTopicId] = dbSignal;
        }

        foreach (var (topicId, topicWeight) in topicWeights)
        {
            var delta = eventWeight * topicWeight;

            if (byTopicId.TryGetValue(topicId, out var signal))
            {
                ApplyScore(signal, delta, eventType, reason, now);
            }
            else
            {
                var fresh = new UserInterestTopicSignal
                {
                    UserId = userId,
                    InterestTopicId = topicId,
                    Score = Math.Clamp(delta, -ScoreCap, ScoreCap),
                    Source = "inferred",
                    LastInteractionAt = now,
                    LastReason = reason,
                };
                _db.UserInterestTopicSignals.Add(fresh);
                byTopicId[topicId] = fresh;
            }
        }
    }

    private static void ApplyScore(UserInterestSignal signal, double weight, string eventType, string reason, DateTime now)
    {
        if (ShouldSkipScoring(signal.LastReason, signal.LastInteractionAt, eventType, reason, now, weight))
        {
            return;
        }

        signal.Score = Math.Clamp(signal.Score + weight, -ScoreCap, ScoreCap);
        if (signal.IsDismissed && weight > 0)
        {
            signal.EvidenceSinceDismissal = Math.Clamp(signal.EvidenceSinceDismissal + weight, 0.0, ScoreCap);

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

    private static void ApplyScore(UserInterestTopicSignal signal, double weight, string eventType, string reason, DateTime now)
    {
        if (ShouldSkipScoring(signal.LastReason, signal.LastInteractionAt, eventType, reason, now, weight))
        {
            return;
        }

        signal.Score = Math.Clamp(signal.Score + weight, -ScoreCap, ScoreCap);
        if (signal.IsDismissed && weight > 0)
        {
            signal.EvidenceSinceDismissal = Math.Clamp(signal.EvidenceSinceDismissal + weight, 0.0, ScoreCap);

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

    private static bool ShouldSkipScoring(
        string? lastReason,
        DateTime lastInteractionAt,
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
            string.Equals(lastReason, reason, StringComparison.Ordinal) &&
            now - lastInteractionAt < cooldown;
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

            AnalyticsEntityTypes.Location =>
                await _db.LocationTags
                    .Where(lt => lt.LocationId == entityId)
                    .Select(lt => lt.TagId)
                    .ToListAsync(ct),

            _ => new List<int>(),
        };
    }

    private async Task<Dictionary<int, double>> ResolveInterestTopicWeightsAsync(
        IReadOnlyCollection<int> tagIds,
        CancellationToken ct)
    {
        return await _db.InterestTopicTags
            .Where(link => tagIds.Contains(link.TagId) && link.InterestTopic.IsActive)
            .GroupBy(link => link.InterestTopicId)
            .Select(group => new
            {
                InterestTopicId = group.Key,
                Weight = group.Sum(link => link.Weight)
            })
            .ToDictionaryAsync(x => x.InterestTopicId, x => Math.Max(0.1, x.Weight), ct);
    }
}
