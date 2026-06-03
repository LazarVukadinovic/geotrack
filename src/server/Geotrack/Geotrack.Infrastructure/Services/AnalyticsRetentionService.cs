using Geotrack.Application.DTOs.Analytics;
using Geotrack.Application.Interfaces.Service;
using Geotrack.Application.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Geotrack.Infrastructure.Services;

public sealed class AnalyticsRetentionService : IAnalyticsRetentionService
{
    private readonly AppDbContext _db;
    private readonly AnalyticsRetentionOptions _options;
    private readonly ILogger<AnalyticsRetentionService> _logger;

    public AnalyticsRetentionService(
        AppDbContext db,
        IOptions<AnalyticsRetentionOptions> options,
        ILogger<AnalyticsRetentionService> logger)
    {
        _db = db;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<AnalyticsRetentionCleanupResultDto> CleanupExpiredAsync(
        DateTime utcNow,
        bool execute,
        CancellationToken cancellationToken = default)
    {
        var retentionDays = Math.Clamp(_options.RetentionDays, 1, 3650);
        var batchSize = Math.Clamp(_options.BatchSize, 1, 10_000);
        var cutoffUtc = DateTime.SpecifyKind(utcNow, DateTimeKind.Utc).AddDays(-retentionDays);

        var eventQuery = _db.AnalyticsEvents
            .Where(evt => evt.OccurredAt < cutoffUtc)
            .OrderBy(evt => evt.OccurredAt)
            .ThenBy(evt => evt.Id)
            .Take(batchSize);

        var eventIds = await eventQuery
            .Select(evt => evt.Id)
            .ToListAsync(cancellationToken);

        var sessionQuery = _db.AnonymousSessions
            .Where(session => session.LastSeenAt < cutoffUtc)
            .Where(session => !_db.AnalyticsEvents.Any(evt => evt.AnonymousSessionId == session.Id))
            .OrderBy(session => session.LastSeenAt)
            .ThenBy(session => session.Id)
            .Take(batchSize);

        var sessionIds = await sessionQuery
            .Select(session => session.Id)
            .ToListAsync(cancellationToken);

        if (!execute)
        {
            return BuildResult(retentionDays, cutoffUtc, eventIds.Count, sessionIds.Count, execute);
        }

        var deletedEvents = eventIds.Count == 0
            ? 0
            : await _db.AnalyticsEvents
                .Where(evt => eventIds.Contains(evt.Id))
                .ExecuteDeleteAsync(cancellationToken);

        var deletedSessions = sessionIds.Count == 0
            ? 0
            : await _db.AnonymousSessions
                .Where(session => sessionIds.Contains(session.Id))
                .ExecuteDeleteAsync(cancellationToken);

        _logger.LogInformation(
            "Analytics retention cleanup completed: {DeletedAnalyticsEvents} event(s), {DeletedAnonymousSessions} anonymous session(s), retention {RetentionDays} days, cutoff {CutoffUtc}.",
            deletedEvents,
            deletedSessions,
            retentionDays,
            cutoffUtc);

        return BuildResult(retentionDays, cutoffUtc, deletedEvents, deletedSessions, execute);
    }

    private static AnalyticsRetentionCleanupResultDto BuildResult(
        int retentionDays,
        DateTime cutoffUtc,
        int deletedEvents,
        int deletedSessions,
        bool execute) =>
        new()
        {
            RetentionDays = retentionDays,
            CutoffUtc = cutoffUtc,
            DeletedAnalyticsEvents = deletedEvents,
            DeletedAnonymousSessions = deletedSessions,
            Execute = execute
        };
}
