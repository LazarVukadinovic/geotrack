using System.Text.Json;
using Geotrack.Application.Analytics;
using Geotrack.Application.DTOs.Analytics;
using Geotrack.Application.Interfaces.Service;
using Microsoft.EntityFrameworkCore;

namespace Geotrack.Infrastructure.Services;

public sealed class AnalyticsReadService : IAnalyticsReadService
{
    private readonly AppDbContext _db;

    public AnalyticsReadService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<AnalyticsTopEntityDto>> GetTopEntitiesAsync(
        DateTime fromUtc,
        DateTime toUtc,
        string? eventType,
        int limit,
        CancellationToken cancellationToken)
    {
        var normalizedEventType = NormalizeOptionalToken(eventType);
        var cappedLimit = ClampLimit(limit);

        var query = _db.AnalyticsEvents
            .AsNoTracking()
            .Where(e => e.OccurredAt >= fromUtc && e.OccurredAt < toUtc && e.EntityId != null);

        if (!string.IsNullOrWhiteSpace(normalizedEventType))
        {
            query = query.Where(e => e.EventType == normalizedEventType);
        }

        return await query
            .GroupBy(e => new { e.EventType, e.EntityType, e.EntityId })
            .Select(g => new AnalyticsTopEntityDto(
                g.Key.EventType,
                g.Key.EntityType,
                g.Key.EntityId,
                g.Count(),
                g.Max(e => e.OccurredAt)))
            .OrderByDescending(item => item.Count)
            .ThenByDescending(item => item.LastOccurredAt)
            .Take(cappedLimit)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<AnalyticsTopDestinationDto>> GetTopDestinationsAsync(
        DateTime fromUtc,
        DateTime toUtc,
        int limit,
        CancellationToken cancellationToken)
    {
        var cappedLimit = ClampLimit(limit);

        return await _db.AnalyticsEvents
            .AsNoTracking()
            .Where(e => e.OccurredAt >= fromUtc && e.OccurredAt < toUtc && e.DestinationId != null)
            .GroupBy(e => e.DestinationId!.Value)
            .Select(g => new AnalyticsTopDestinationDto(
                g.Key,
                g.Count(),
                g.Max(e => e.OccurredAt)))
            .OrderByDescending(item => item.Count)
            .ThenByDescending(item => item.LastOccurredAt)
            .Take(cappedLimit)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<AnalyticsOutboundProviderClickDto>> GetOutboundProviderClicksAsync(
        DateTime fromUtc,
        DateTime toUtc,
        int limit,
        CancellationToken cancellationToken)
    {
        var cappedLimit = ClampLimit(limit);
        var rows = await _db.AnalyticsEvents
            .AsNoTracking()
            .Where(e => e.OccurredAt >= fromUtc &&
                        e.OccurredAt < toUtc &&
                        e.EventType == AnalyticsEventTypes.OutboundLinkClicked)
            .Select(e => new
            {
                e.EntityType,
                e.EntityId,
                e.MetadataJson,
                e.OccurredAt,
            })
            .ToListAsync(cancellationToken);

        return rows
            .GroupBy(row => new
            {
                Provider = ExtractProvider(row.MetadataJson),
                row.EntityType,
                row.EntityId,
            })
            .Select(g => new AnalyticsOutboundProviderClickDto(
                g.Key.Provider,
                g.Key.EntityType,
                g.Key.EntityId,
                g.Count(),
                g.Max(row => row.OccurredAt)))
            .OrderByDescending(item => item.Count)
            .ThenByDescending(item => item.LastOccurredAt)
            .Take(cappedLimit)
            .ToList();
    }

    public async Task<IReadOnlyList<AnalyticsSearchTrendDto>> GetSearchTrendsAsync(
        DateTime fromUtc,
        DateTime toUtc,
        int limit,
        CancellationToken cancellationToken)
    {
        var cappedLimit = ClampLimit(limit);

        return await _db.AnalyticsEvents
            .AsNoTracking()
            .Where(e => e.OccurredAt >= fromUtc &&
                        e.OccurredAt < toUtc &&
                        e.EventType == AnalyticsEventTypes.SearchPerformed)
            .GroupBy(e => e.Route)
            .Select(g => new AnalyticsSearchTrendDto(
                g.Key,
                g.Count(),
                g.Max(e => e.OccurredAt)))
            .OrderByDescending(item => item.Count)
            .ThenByDescending(item => item.LastOccurredAt)
            .Take(cappedLimit)
            .ToListAsync(cancellationToken);
    }

    private static int ClampLimit(int limit) => Math.Clamp(limit, 1, 100);

    private static string? NormalizeOptionalToken(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToLowerInvariant();

    private static string ExtractProvider(string metadataJson)
    {
        try
        {
            using var document = JsonDocument.Parse(metadataJson);
            if (document.RootElement.TryGetProperty("provider", out var provider) &&
                provider.ValueKind == JsonValueKind.String)
            {
                var normalized = provider.GetString()?.Trim().ToLowerInvariant();
                return string.IsNullOrWhiteSpace(normalized) ? "other" : normalized;
            }
        }
        catch (JsonException)
        {
            return "other";
        }

        return "other";
    }
}
