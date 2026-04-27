using System.Text.Json;
using Geotrack.Application.Analytics;
using Geotrack.Application.DTOs.Analytics;
using Geotrack.Application.Interfaces.Service;
using Geotrack.Domain.Entities.Analytics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Geotrack.Infrastructure.Services;

/// <summary>
/// Best-effort analytics ingestion. Validates and enriches each event in a batch,
/// upserts the anonymous session if present, persists raw events, and updates
/// inferred-interest scores for authenticated users. Synchronous within the
/// request — there is no hosted-service infrastructure in this repo. To upgrade
/// later: replace the direct repository writes with a Channel&lt;AnalyticsEvent&gt;
/// + a BackgroundService consumer that drains in a fresh DI scope.
/// </summary>
public class AnalyticsService : IAnalyticsService
{
    private const int MaxBatchSize = 50;
    private const int MaxMetadataChars = 4096;
    private const int MaxMetadataKeys = 30;
    private const int MaxRouteLength = 300;

    private static readonly JsonSerializerOptions MetadataJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly AppDbContext _db;
    private readonly IUserInterestSignalUpdater _signalUpdater;
    private readonly ILogger<AnalyticsService> _logger;

    public AnalyticsService(
        AppDbContext db,
        IUserInterestSignalUpdater signalUpdater,
        ILogger<AnalyticsService> logger)
    {
        _db = db;
        _signalUpdater = signalUpdater;
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
            // No identity at all — nothing to attribute the events to.
            return;
        }

        if (batch.Events.Count > MaxBatchSize)
        {
            _logger.LogWarning("Analytics batch exceeded max size ({Count}); truncating to {Max}.",
                batch.Events.Count, MaxBatchSize);
        }

        var now = DateTime.UtcNow;

        // 1. Anonymous session upsert (single FindAsync per batch).
        if (context.AnonymousSessionId is { } sessionId)
        {
            await UpsertAnonymousSessionAsync(sessionId, batch, context, now, cancellationToken);
        }

        // 2. Build, validate, and enrich each event. Drop invalid; keep valid.
        var enrichedEvents = new List<AnalyticsEvent>();
        var seenDedupedEvents = new HashSet<string>(StringComparer.Ordinal);
        var capped = batch.Events.Take(MaxBatchSize);
        foreach (var dto in capped)
        {
            var enriched = TryEnrich(dto, context, now);
            if (enriched is null)
            {
                continue;
            }

            if (ShouldDropDuplicate(enriched) &&
                !seenDedupedEvents.Add(BuildDuplicateKey(enriched)))
            {
                _logger.LogDebug(
                    "Dropping duplicate analytics event in batch: {EventType} {EntityType}#{EntityId}.",
                    enriched.EventType,
                    enriched.EntityType,
                    enriched.EntityId);
                continue;
            }

            _db.AnalyticsEvents.Add(enriched);
            enrichedEvents.Add(enriched);
        }

        if (enrichedEvents.Count == 0)
        {
            return;
        }

        // 3. Update inferred-interest signals for authenticated events only.
        if (context.UserId is { } userId)
        {
            foreach (var ev in enrichedEvents)
            {
                await _signalUpdater.ApplyAsync(userId, ev, cancellationToken);
            }
        }

        // 4. One save for the whole batch.
        await _db.SaveChangesAsync(cancellationToken);
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
                .Select(e => Truncate(e.Route, MaxRouteLength))
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

    private AnalyticsEvent? TryEnrich(AnalyticsEventDto dto, AnalyticsContext context, DateTime now)
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
            _logger.LogWarning("Dropping analytics event with disallowed entity type {EntityType}.", dto.EntityType);
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

        var metadataJson = SerializeMetadata(dto.Metadata);
        if (metadataJson is null)
        {
            _logger.LogWarning(
                "Dropping analytics event {EventType} whose metadata exceeds size or shape limits.",
                eventType);
            return null;
        }

        return new AnalyticsEvent
        {
            UserId = context.UserId,
            AnonymousSessionId = context.AnonymousSessionId,
            EventType = eventType!,
            EntityType = string.IsNullOrWhiteSpace(entityType) ? null : entityType,
            EntityId = dto.EntityId,
            DestinationId = dto.DestinationId,
            Route = NormalizeRoute(dto.Route),
            MetadataJson = metadataJson,
            OccurredAt = now,
            ClientOccurredAt = dto.ClientOccurredAt,
        };
    }

    private static string? SerializeMetadata(Dictionary<string, object?>? metadata)
    {
        if (metadata is null || metadata.Count == 0)
        {
            return "{}";
        }

        if (metadata.Count > MaxMetadataKeys)
        {
            return null;
        }

        var json = JsonSerializer.Serialize(metadata, MetadataJsonOptions);
        return json.Length > MaxMetadataChars ? null : json;
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

        var withoutQuery = route.Split('?', 2)[0].Split('#', 2)[0];
        var normalized = string.Join(
            '/',
            withoutQuery
                .Split('/')
                .Select(segment => int.TryParse(segment, out _) ? ":id" : segment));

        return Truncate(normalized, MaxRouteLength);
    }

    private static bool ShouldDropDuplicate(AnalyticsEvent analyticsEvent) =>
        analyticsEvent.EventType is
            AnalyticsEventTypes.PageView or
            AnalyticsEventTypes.DetailView or
            AnalyticsEventTypes.MapMarkerOpened or
            AnalyticsEventTypes.DirectionsStarted or
            AnalyticsEventTypes.OutboundLinkClicked;

    private static string BuildDuplicateKey(AnalyticsEvent analyticsEvent) =>
        string.Join('|',
            analyticsEvent.EventType,
            analyticsEvent.EntityType,
            analyticsEvent.EntityId,
            analyticsEvent.DestinationId,
            analyticsEvent.Route,
            analyticsEvent.MetadataJson);

    private static string? Truncate(string? value, int maxLength)
    {
        if (value is null)
        {
            return null;
        }
        return value.Length <= maxLength ? value : value[..maxLength];
    }
}
