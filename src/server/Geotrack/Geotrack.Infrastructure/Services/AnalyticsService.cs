using System.Text.Json;
using Geotrack.Application.Analytics;
using Geotrack.Application.DTOs.Analytics;
using Geotrack.Application.Interfaces.Service;
using Geotrack.Domain.Entities.Analytics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Geotrack.Infrastructure.Services;

/// <summary>
/// Best-effort analytics ingestion. Validates and enriches each event in a
/// batch, upserts the anonymous session if present, persists accepted raw
/// events, and queues derived processing for authenticated users. Inferred
/// interests are intentionally processed from accepted raw rows, not directly
/// from untrusted client payloads.
/// </summary>
public class AnalyticsService : IAnalyticsService
{
    private const int MaxBatchSize = 50;
    private const int MaxMetadataChars = 4096;
    private const int MaxMetadataKeys = 30;
    private const int MaxMetadataKeyLength = 80;
    private const int MaxRouteLength = 300;
    private const int MinDurationMs = 1_000;
    private const int MaxDurationMs = 30 * 60 * 1_000;

    private static readonly IReadOnlyDictionary<string, TimeSpan> DedupeWindows =
        new Dictionary<string, TimeSpan>(StringComparer.Ordinal)
        {
            [AnalyticsEventTypes.PageView] = TimeSpan.FromSeconds(30),
            [AnalyticsEventTypes.DetailView] = TimeSpan.FromMinutes(2),
            [AnalyticsEventTypes.MapMarkerOpened] = TimeSpan.FromSeconds(20),
            [AnalyticsEventTypes.DirectionsStarted] = TimeSpan.FromSeconds(45),
            [AnalyticsEventTypes.SearchPerformed] = TimeSpan.FromSeconds(15),
            [AnalyticsEventTypes.FilterApplied] = TimeSpan.FromSeconds(60),
        };

    private static readonly string[] DedupeEventTypes = DedupeWindows.Keys.ToArray();
    private static readonly TimeSpan MaxDedupeWindow = DedupeWindows.Values.Max();
    private static readonly string[] SensitiveMetadataKeyTokens =
    [
        "apikey",
        "api_key",
        "authorization",
        "email",
        "password",
        "phone",
        "prompt",
        "refresh",
        "secret",
        "token"
    ];

    private static readonly IReadOnlySet<string> InterestSignalCandidateEventTypes =
        new HashSet<string>(StringComparer.Ordinal)
        {
            AnalyticsEventTypes.DetailView,
            AnalyticsEventTypes.SearchPerformed,
            AnalyticsEventTypes.FilterApplied,
            AnalyticsEventTypes.MapMarkerOpened,
            AnalyticsEventTypes.AgentRecommendationOpened,
            AnalyticsEventTypes.DirectionsStarted,
            AnalyticsEventTypes.OutboundLinkClicked,
            AnalyticsEventTypes.FavoriteAdded,
            AnalyticsEventTypes.CollectionItemAdded,
            AnalyticsEventTypes.ReviewCreated,
            AnalyticsEventTypes.FavoriteRemoved,
            AnalyticsEventTypes.CollectionItemRemoved,
        };

    private static readonly JsonSerializerOptions MetadataJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly AppDbContext _db;
    private readonly IAnalyticsProcessingQueue _processingQueue;
    private readonly ILogger<AnalyticsService> _logger;

    public AnalyticsService(
        AppDbContext db,
        IAnalyticsProcessingQueue processingQueue,
        ILogger<AnalyticsService> logger)
    {
        _db = db;
        _processingQueue = processingQueue;
        _logger = logger;
    }

    public async Task IngestBatchAsync(
        AnalyticsEventBatchDto batch,
        AnalyticsContext context,
        CancellationToken cancellationToken)
    {
        if (batch is null || batch.Events is null || batch.Events.Count == 0)
        {
            return;
        }

        if (context.UserId is null && context.AnonymousSessionId is null)
        {
            return;
        }

        if (context.UserId is { } analyticsUserId &&
            !await IsAnalyticsEnabledForUserAsync(analyticsUserId, cancellationToken))
        {
            return;
        }

        if (batch.Events.Count > MaxBatchSize)
        {
            _logger.LogWarning(
                "Analytics batch exceeded max size ({Count}); truncating to {Max}.",
                batch.Events.Count,
                MaxBatchSize);
        }

        var now = DateTime.UtcNow;

        if (context.AnonymousSessionId is { } sessionId)
        {
            await UpsertAnonymousSessionAsync(sessionId, batch, context, now, cancellationToken);
        }

        var recentDedupeEvents = await LoadRecentDedupeEventsAsync(context, now, cancellationToken);
        var seenDedupeFingerprints = new HashSet<string>(StringComparer.Ordinal);
        var enrichedEvents = new List<AnalyticsEvent>();

        foreach (var dto in batch.Events.Take(MaxBatchSize))
        {
            var enriched = await TryEnrichAsync(dto, context, now, cancellationToken);
            if (enriched is null)
            {
                continue;
            }

            if (ShouldDropNoisyDuplicate(enriched, recentDedupeEvents, seenDedupeFingerprints, now))
            {
                continue;
            }

            _db.AnalyticsEvents.Add(enriched);
            enrichedEvents.Add(enriched);
        }

        if (enrichedEvents.Count == 0)
        {
            return;
        }

        await _db.SaveChangesAsync(cancellationToken);

        if (context.UserId is { } userId)
        {
            var derivedEventIds = enrichedEvents
                .Where(ShouldQueueForDerivedProcessing)
                .Select(e => e.Id)
                .Where(id => id > 0)
                .ToArray();

            if (derivedEventIds.Length > 0 &&
                !_processingQueue.TryEnqueue(new AnalyticsProcessingJob(userId, derivedEventIds)))
            {
                _logger.LogWarning(
                    "Analytics derived-processing queue is full; raw events saved but {EventCount} event(s) were not queued for user {UserId}.",
                    derivedEventIds.Length,
                    userId);
            }
        }
    }

    private async Task UpsertAnonymousSessionAsync(
        Guid sessionId,
        AnalyticsEventBatchDto batch,
        AnalyticsContext context,
        DateTime now,
        CancellationToken ct)
    {
        var session = await _db.AnonymousSessions.FindAsync(new object?[] { sessionId }, ct);
        if (session is null)
        {
            var entryRoute = batch.Events
                .Select(e => NormalizeRoute(e.Route))
                .FirstOrDefault(r => !string.IsNullOrWhiteSpace(r));

            _db.AnonymousSessions.Add(new AnonymousSession
            {
                Id = sessionId,
                CreatedAt = now,
                LastSeenAt = now,
                EntryRoute = entryRoute,
                CoarseRegion = Truncate(context.CoarseRegion, 120),
                CoarseCountry = Truncate(context.CoarseCountry, 120),
                UserAgentFamily = string.IsNullOrWhiteSpace(context.UserAgentFamily) ? "other" : context.UserAgentFamily,
            });
        }
        else
        {
            session.LastSeenAt = now;
            if (string.IsNullOrWhiteSpace(session.CoarseRegion) && !string.IsNullOrWhiteSpace(context.CoarseRegion))
            {
                session.CoarseRegion = Truncate(context.CoarseRegion, 120);
            }
            if (string.IsNullOrWhiteSpace(session.CoarseCountry) && !string.IsNullOrWhiteSpace(context.CoarseCountry))
            {
                session.CoarseCountry = Truncate(context.CoarseCountry, 120);
            }
        }
    }

    private async Task<bool> IsAnalyticsEnabledForUserAsync(int userId, CancellationToken ct)
    {
        return await _db.Users
            .AsNoTracking()
            .Where(user => user.Id == userId)
            .Select(user => (bool?)user.AnalyticsEnabled)
            .SingleOrDefaultAsync(ct) ?? false;
    }

    private async Task<AnalyticsEvent?> TryEnrichAsync(
        AnalyticsEventDto dto,
        AnalyticsContext context,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var eventType = NormalizeToken(dto.EventType);
        if (!AnalyticsEventTypes.IsAllowed(eventType))
        {
            _logger.LogWarning("Dropping analytics event with disallowed type {EventType}.", dto.EventType);
            return null;
        }

        var entityType = NormalizeToken(dto.EntityType);
        if (!AnalyticsEntityTypes.IsAllowed(entityType))
        {
            _logger.LogWarning(
                "Dropping analytics event {EventType} with disallowed entity type {EntityType}.",
                eventType,
                dto.EntityType);
            return null;
        }

        if (dto.EntityId is <= 0)
        {
            _logger.LogWarning(
                "Dropping analytics event {EventType} with invalid entity id {EntityId}.",
                eventType,
                dto.EntityId);
            return null;
        }

        if (dto.DestinationId is <= 0)
        {
            _logger.LogWarning(
                "Dropping analytics event {EventType} with invalid destination id {DestinationId}.",
                eventType,
                dto.DestinationId);
            return null;
        }

        var durationMs = NormalizeDurationMs(eventType!, dto.DurationMs);
        if (eventType == AnalyticsEventTypes.PageDuration && durationMs is null)
        {
            _logger.LogDebug(
                "Dropping analytics duration event with missing or too-short duration {DurationMs}.",
                dto.DurationMs);
            return null;
        }

        var metadataJson = SerializeMetadata(dto.Metadata);
        if (metadataJson is null)
        {
            _logger.LogWarning(
                "Dropping analytics event {EventType} whose metadata exceeds size or shape limits.",
                eventType);
            return null;
        }

        var destinationId = await ResolveDestinationIdAsync(entityType, dto.EntityId, dto.DestinationId, cancellationToken);

        return new AnalyticsEvent
        {
            UserId = context.UserId,
            AnonymousSessionId = context.AnonymousSessionId,
            EventType = eventType!,
            EntityType = string.IsNullOrWhiteSpace(entityType) ? null : entityType,
            EntityId = dto.EntityId,
            DestinationId = destinationId,
            Route = NormalizeRoute(dto.Route),
            DurationMs = durationMs,
            MetadataJson = metadataJson,
            OccurredAt = now,
            ClientOccurredAt = NormalizeClientOccurredAt(dto.ClientOccurredAt, now),
        };
    }

    private async Task<int?> ResolveDestinationIdAsync(
        string? entityType,
        int? entityId,
        int? suppliedDestinationId,
        CancellationToken cancellationToken)
    {
        if (!entityId.HasValue || entityId.Value <= 0)
        {
            return suppliedDestinationId is > 0 ? suppliedDestinationId : null;
        }

        var derivedDestinationId = entityType switch
        {
            AnalyticsEntityTypes.Destination => await _db.Destinations
                .AsNoTracking()
                .Where(destination => destination.Id == entityId.Value)
                .Select(destination => (int?)destination.Id)
                .SingleOrDefaultAsync(cancellationToken),
            AnalyticsEntityTypes.Location => await _db.Locations
                .AsNoTracking()
                .Where(location => location.Id == entityId.Value)
                .Select(location => (int?)location.DestinationId)
                .SingleOrDefaultAsync(cancellationToken),
            AnalyticsEntityTypes.Place or AnalyticsEntityTypes.Hotel => await _db.Places
                .AsNoTracking()
                .Where(place => place.Id == entityId.Value)
                .Select(place => (int?)place.Location.DestinationId)
                .SingleOrDefaultAsync(cancellationToken),
            AnalyticsEntityTypes.Event => await _db.Events
                .AsNoTracking()
                .Where(eventEntity => eventEntity.Id == entityId.Value)
                .Select(eventEntity => (int?)eventEntity.DestinationId)
                .SingleOrDefaultAsync(cancellationToken),
            _ => null
        };

        return derivedDestinationId ?? (suppliedDestinationId is > 0 ? suppliedDestinationId : null);
    }

    private async Task<List<RecentDedupeEvent>> LoadRecentDedupeEventsAsync(
        AnalyticsContext context,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var cutoff = now - MaxDedupeWindow;
        var query = _db.AnalyticsEvents
            .AsNoTracking()
            .Where(e => e.OccurredAt >= cutoff && DedupeEventTypes.Contains(e.EventType));

        if (context.UserId is { } userId && context.AnonymousSessionId is { } sessionId)
        {
            query = query.Where(e => e.UserId == userId || e.AnonymousSessionId == sessionId);
        }
        else if (context.UserId is { } userOnlyId)
        {
            query = query.Where(e => e.UserId == userOnlyId);
        }
        else if (context.AnonymousSessionId is { } sessionOnlyId)
        {
            query = query.Where(e => e.AnonymousSessionId == sessionOnlyId);
        }
        else
        {
            return new List<RecentDedupeEvent>();
        }

        return await query
            .Select(e => new RecentDedupeEvent(
                e.EventType,
                e.EntityType,
                e.EntityId,
                e.DestinationId,
                e.Route,
                e.MetadataJson,
                e.OccurredAt))
            .ToListAsync(cancellationToken);
    }

    private bool ShouldDropNoisyDuplicate(
        AnalyticsEvent analyticsEvent,
        List<RecentDedupeEvent> recentDedupeEvents,
        HashSet<string> seenDedupeFingerprints,
        DateTime now)
    {
        if (!DedupeWindows.TryGetValue(analyticsEvent.EventType, out var window))
        {
            return false;
        }

        var fingerprint = BuildDedupeFingerprint(
            analyticsEvent.EventType,
            analyticsEvent.EntityType,
            analyticsEvent.EntityId,
            analyticsEvent.DestinationId,
            analyticsEvent.Route,
            analyticsEvent.MetadataJson);

        if (!seenDedupeFingerprints.Add(fingerprint))
        {
            _logger.LogDebug(
                "Dropping duplicate analytics event inside batch: {EventType} {EntityType}#{EntityId} route {Route}.",
                analyticsEvent.EventType,
                analyticsEvent.EntityType,
                analyticsEvent.EntityId,
                analyticsEvent.Route);
            return true;
        }

        var cutoff = now - window;
        var hasRecentDuplicate = recentDedupeEvents.Any(recent =>
            recent.OccurredAt >= cutoff &&
            BuildDedupeFingerprint(
                recent.EventType,
                recent.EntityType,
                recent.EntityId,
                recent.DestinationId,
                recent.Route,
                recent.MetadataJson) == fingerprint);

        if (!hasRecentDuplicate)
        {
            return false;
        }

        _logger.LogDebug(
            "Dropping duplicate analytics event within {WindowSeconds}s window: {EventType} {EntityType}#{EntityId} route {Route}.",
            (int)window.TotalSeconds,
            analyticsEvent.EventType,
            analyticsEvent.EntityType,
            analyticsEvent.EntityId,
            analyticsEvent.Route);
        return true;
    }

    private static bool ShouldQueueForDerivedProcessing(AnalyticsEvent analyticsEvent) =>
        InterestSignalCandidateEventTypes.Contains(analyticsEvent.EventType) &&
        analyticsEvent.EntityType is not null &&
        analyticsEvent.EntityId is not null;

    private static string? SerializeMetadata(Dictionary<string, object?>? metadata)
    {
        if (metadata is null || metadata.Count == 0)
        {
            return "{}";
        }

        var sanitized = SanitizeMetadata(metadata);
        if (sanitized is null)
        {
            return null;
        }

        if (sanitized.Count == 0)
        {
            return "{}";
        }

        if (sanitized.Count > MaxMetadataKeys)
        {
            return null;
        }

        try
        {
            var json = JsonSerializer.Serialize(sanitized, MetadataJsonOptions);
            return json.Length > MaxMetadataChars ? null : json;
        }
        catch (JsonException)
        {
            return null;
        }
        catch (NotSupportedException)
        {
            return null;
        }
    }

    private static Dictionary<string, object?>? SanitizeMetadata(Dictionary<string, object?> metadata)
    {
        var sanitized = new Dictionary<string, object?>(StringComparer.Ordinal);

        foreach (var pair in metadata)
        {
            var key = pair.Key?.Trim();
            if (string.IsNullOrWhiteSpace(key) || key.Length > MaxMetadataKeyLength)
            {
                return null;
            }

            if (IsSensitiveMetadataKey(key))
            {
                continue;
            }

            sanitized[key] = pair.Value;
        }

        return sanitized;
    }

    private static bool IsSensitiveMetadataKey(string key)
    {
        var compact = key
            .Trim()
            .ToLowerInvariant()
            .Replace("-", string.Empty)
            .Replace("_", string.Empty)
            .Replace(".", string.Empty)
            .Replace(" ", string.Empty);

        return SensitiveMetadataKeyTokens.Any(token =>
            compact.Contains(
                token.Replace("_", string.Empty),
                StringComparison.Ordinal));
    }

    private static string? NormalizeToken(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Trim().ToLowerInvariant();
    }

    private static string? NormalizeRoute(string? route)
    {
        if (string.IsNullOrWhiteSpace(route))
        {
            return null;
        }

        var trimmed = route.Trim();
        if (Uri.TryCreate(trimmed, UriKind.Absolute, out var absoluteUri))
        {
            trimmed = absoluteUri.PathAndQuery;
        }

        var withoutQuery = trimmed.Split('?', 2)[0].Split('#', 2)[0];
        var normalized = string.Join(
            '/',
            withoutQuery
                .Split('/')
                .Select(segment => int.TryParse(segment, out _) ? ":id" : segment));

        return Truncate(normalized, MaxRouteLength);
    }

    private static DateTime? NormalizeClientOccurredAt(DateTime? clientOccurredAt, DateTime serverNow)
    {
        if (clientOccurredAt is null)
        {
            return null;
        }

        var value = clientOccurredAt.Value.Kind switch
        {
            DateTimeKind.Local => clientOccurredAt.Value.ToUniversalTime(),
            DateTimeKind.Unspecified => DateTime.SpecifyKind(clientOccurredAt.Value, DateTimeKind.Utc),
            _ => clientOccurredAt.Value,
        };

        if (value > serverNow.AddMinutes(5) || value < serverNow.AddDays(-30))
        {
            return null;
        }

        return value;
    }

    private static int? NormalizeDurationMs(string eventType, int? durationMs)
    {
        if (eventType != AnalyticsEventTypes.PageDuration)
        {
            return null;
        }

        if (!durationMs.HasValue || durationMs.Value < MinDurationMs)
        {
            return null;
        }

        return Math.Min(durationMs.Value, MaxDurationMs);
    }

    private static string BuildDedupeFingerprint(
        string eventType,
        string? entityType,
        int? entityId,
        int? destinationId,
        string? route,
        string metadataJson)
    {
        var metadataPart = eventType is AnalyticsEventTypes.SearchPerformed or AnalyticsEventTypes.FilterApplied
            ? BuildMetadataFingerprint(metadataJson)
            : string.Empty;

        return string.Join(
            '|',
            eventType,
            entityType,
            entityId,
            destinationId,
            route,
            metadataPart);
    }

    private static string BuildMetadataFingerprint(string metadataJson)
    {
        try
        {
            using var document = JsonDocument.Parse(metadataJson);
            return CanonicalizeJson(document.RootElement);
        }
        catch (JsonException)
        {
            return metadataJson;
        }
    }

    private static string CanonicalizeJson(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Object => "{" + string.Join(
                ',',
                element.EnumerateObject()
                    .OrderBy(property => property.Name, StringComparer.Ordinal)
                    .Select(property => $"{property.Name}:{CanonicalizeJson(property.Value)}")) + "}",
            JsonValueKind.Array => "[" + string.Join(',', element.EnumerateArray().Select(CanonicalizeJson)) + "]",
            JsonValueKind.String => element.GetString() ?? string.Empty,
            JsonValueKind.Number => element.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            JsonValueKind.Null => "null",
            _ => string.Empty,
        };
    }

    private static string? Truncate(string? value, int maxLength)
    {
        if (value is null)
        {
            return null;
        }
        return value.Length <= maxLength ? value : value[..maxLength];
    }

    private sealed record RecentDedupeEvent(
        string EventType,
        string? EntityType,
        int? EntityId,
        int? DestinationId,
        string? Route,
        string MetadataJson,
        DateTime OccurredAt);
}
