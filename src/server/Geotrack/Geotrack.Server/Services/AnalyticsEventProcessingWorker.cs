using Geotrack.Application.Interfaces.Service;
using Geotrack.Application.Analytics;
using Geotrack.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Geotrack.Server.Services;

public sealed class AnalyticsEventProcessingWorker : BackgroundService
{
    private readonly IAnalyticsProcessingQueue _queue;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<AnalyticsEventProcessingWorker> _logger;

    public AnalyticsEventProcessingWorker(
        IAnalyticsProcessingQueue queue,
        IServiceScopeFactory scopeFactory,
        ILogger<AnalyticsEventProcessingWorker> logger)
    {
        _queue = queue;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var job in _queue.ReadAllAsync(stoppingToken))
        {
            try
            {
                await ProcessJobAsync(job, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Analytics derived processing failed for user {UserId} and {EventCount} event(s).",
                    job.UserId,
                    job.AnalyticsEventIds.Count);
            }
        }
    }

    private async Task ProcessJobAsync(
        AnalyticsProcessingJob job,
        CancellationToken cancellationToken)
    {
        if (job.UserId <= 0 || job.AnalyticsEventIds.Count == 0)
        {
            return;
        }

        var eventIds = job.AnalyticsEventIds
            .Where(id => id > 0)
            .Distinct()
            .ToArray();

        if (eventIds.Length == 0)
        {
            return;
        }

        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var signalUpdater = scope.ServiceProvider.GetRequiredService<IUserInterestSignalUpdater>();

        var events = await db.AnalyticsEvents
            .AsNoTracking()
            .Where(e => e.UserId == job.UserId && eventIds.Contains(e.Id))
            .OrderBy(e => e.Id)
            .ToListAsync(cancellationToken);

        foreach (var analyticsEvent in events)
        {
            await signalUpdater.ApplyAsync(job.UserId, analyticsEvent, cancellationToken);
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}
