using System.Globalization;
using System.Text;
using Geotrack.Application.Analytics;
using Geotrack.Application.DTOs.Admin;
using Geotrack.Application.Interfaces.Service;
using Geotrack.Application.Security;
using Microsoft.EntityFrameworkCore;
using AnalyticsEvent = Geotrack.Domain.Entities.Analytics.AnalyticsEvent;
using CollectionItem = Geotrack.Domain.Entities.Users.CollectionItem;
using DestinationReview = Geotrack.Domain.Entities.Reviews.DestinationReview;
using EventReview = Geotrack.Domain.Entities.Reviews.EventReview;
using Favorite = Geotrack.Domain.Entities.Users.Favorite;
using LocationReview = Geotrack.Domain.Entities.Reviews.LocationReview;
using PlaceReview = Geotrack.Domain.Entities.Reviews.PlaceReview;

namespace Geotrack.Infrastructure.Services;

public sealed class RegionalAnalyticsService : IRegionalAnalyticsService
{
    private const int DefaultTake = 25;
    private const int MaxTake = 100;
    private const int ExportTake = 5000;
    private const int MaxMonthlyVisitYearsBack = 3;
    private const int MinDurationMs = 1_000;
    private const int MaxDurationMs = 30 * 60 * 1_000;
    private static readonly TimeSpan ReturningVisitorMinimumGap = TimeSpan.FromSeconds(30);

    private static readonly string[] ViewEventTypes =
    {
        AnalyticsEventTypes.DetailView,
        AnalyticsEventTypes.PageView
    };

    private static readonly string[] ExternalClickEventTypes =
    {
        AnalyticsEventTypes.OutboundLinkClicked,
        AnalyticsEventTypes.DirectionsStarted
    };

    private readonly AppDbContext _db;
    private readonly IRegionalDashboardService _regionalDashboard;

    public RegionalAnalyticsService(AppDbContext db, IRegionalDashboardService regionalDashboard)
    {
        _db = db;
        _regionalDashboard = regionalDashboard;
    }

    public async Task<RegionalAnalyticsOverviewDto> GetOverviewAsync(
        int userId,
        RegionalAnalyticsOverviewQuery query,
        CancellationToken cancellationToken)
    {
        if (query.RegionId <= 0)
            throw new InvalidOperationException("regionId is required.");

        var activeRegion = await GetAssignedRegionAsync(userId, query.RegionId);
        var period = ResolvePeriod(query.Period);
        var entityType = NormalizeEntityTypeFilter(query.EntityType);
        var sort = NormalizeSort(query.Sort);
        var skip = Math.Max(0, query.Skip);
        var take = Math.Clamp(query.Take <= 0 ? DefaultTake : query.Take, 1, MaxTake);

        var entities = await LoadEntitiesAsync(
            query.RegionId,
            activeRegion.DisplayName,
            entityType,
            query.Search,
            cancellationToken);

        var current = await BuildMetricSetAsync(entities, period.StartDate, period.EndDate, cancellationToken);
        var previousTotals = await BuildPreviousTotalsAsync(entities, period, cancellationToken);
        var rows = SortRows(current.Rows.Where(HasEngagement).ToList(), sort);
        var pageRows = rows.Skip(skip).Take(take).ToList();
        var pendingRequestsCount = await GetPendingRequestsCountAsync(userId, query.RegionId);

        return new RegionalAnalyticsOverviewDto
        {
            ActiveRegion = activeRegion,
            Period = new RegionalAnalyticsPeriodDto
            {
                Key = period.Key,
                StartDate = period.StartDate,
                EndDate = period.EndDate
            },
            Totals = current.Totals,
            Comparison = BuildComparison(current.Totals, previousTotals),
            PageViewsTrend = BuildTrend(current.ViewEvents, period.StartDate, period.EndDate),
            TopPerformingEntities = rows
                .OrderByDescending(row => row.PageViews)
                .ThenByDescending(row => row.UniqueVisitors)
                .ThenByDescending(row => row.Saves)
                .ThenBy(row => row.Name, StringComparer.OrdinalIgnoreCase)
                .Take(5)
                .ToList(),
            EntityTypeDistribution = BuildDistribution(entities, entityType),
            EngagementFunnel = new RegionalAnalyticsEngagementFunnelDto
            {
                PageViews = current.Totals.PageViews,
                UniqueVisitors = current.Totals.UniqueVisitors,
                Saves = current.Totals.Saves,
                ExternalClicks = current.Totals.ExternalClicks,
                Reviews = current.Totals.TotalReviews
            },
            ActionableInsights = BuildActionableInsights(rows, entities.Count),
            VisitorBehavior = BuildVisitorBehavior(current.ViewEvents, current.DurationEvents, current.Rows),
            EntityRows = pageRows,
            Pagination = new RegionalAnalyticsPaginationDto
            {
                TotalCount = rows.Count,
                Skip = skip,
                Take = take,
                HasMore = skip + take < rows.Count
            },
            PendingRequestsCount = pendingRequestsCount
        };
    }

    public async Task<RegionalMonthlyVisitsDto> GetMonthlyVisitsAsync(
        int userId,
        RegionalMonthlyVisitsQuery query,
        CancellationToken cancellationToken)
    {
        if (query.RegionId <= 0)
            throw new InvalidOperationException("regionId is required.");

        if (query.SelectedYearsBack is < 1 or > MaxMonthlyVisitYearsBack)
            throw new InvalidOperationException("selectedYearsBack must be between 1 and 3.");

        var activeRegion = await GetAssignedRegionAsync(userId, query.RegionId);
        var entityType = NormalizeEntityTypeFilter(query.EntityType);
        var entities = await LoadEntitiesAsync(
            query.RegionId,
            activeRegion.DisplayName,
            entityType,
            query.Search,
            cancellationToken);

        var now = DateTime.UtcNow;
        var currentMonthStart = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var latestWindowStart = currentMonthStart.AddMonths(-11);
        var earliestWindowStart = latestWindowStart.AddMonths(-12 * (query.SelectedYearsBack - 1));
        var rangeEndExclusive = currentMonthStart.AddMonths(1);
        var monthLabels = Enumerable
            .Range(0, 12)
            .Select(index => latestWindowStart.AddMonths(index).ToString("MMM", CultureInfo.InvariantCulture))
            .ToList();

        var visitsByMonth = entities.Count == 0
            ? new Dictionary<(int Year, int Month), int>()
            : await LoadMonthlyVisitCountsAsync(entities, earliestWindowStart, rangeEndExclusive, cancellationToken);

        var series = new List<RegionalMonthlyVisitsSeriesDto>();
        for (var seriesIndex = 0; seriesIndex < query.SelectedYearsBack; seriesIndex++)
        {
            var windowStart = latestWindowStart.AddMonths(-12 * (query.SelectedYearsBack - 1 - seriesIndex));
            var counts = Enumerable
                .Range(0, 12)
                .Select(monthIndex =>
                {
                    var month = windowStart.AddMonths(monthIndex);
                    return visitsByMonth.TryGetValue((month.Year, month.Month), out var count) ? count : 0;
                })
                .ToList();

            series.Add(new RegionalMonthlyVisitsSeriesDto
            {
                YearLabel = BuildMonthlyVisitYearLabel(windowStart),
                Year = windowStart.AddMonths(11).Year,
                StartDate = windowStart,
                EndDate = windowStart.AddMonths(12).AddTicks(-1),
                MonthlyVisitCounts = counts,
                TotalVisits = counts.Sum()
            });
        }

        return new RegionalMonthlyVisitsDto
        {
            ActiveRegion = activeRegion,
            SelectedYearsBack = query.SelectedYearsBack,
            MonthLabels = monthLabels,
            Series = series,
            TotalVisits = series.Sum(item => item.TotalVisits),
            VisitMetric = "entity_view",
            VisitEventTypes = ViewEventTypes.ToList(),
            TimeZone = "UTC"
        };
    }

    public async Task<string> ExportCsvAsync(
        int userId,
        RegionalAnalyticsOverviewQuery query,
        CancellationToken cancellationToken)
    {
        query.Skip = 0;
        query.Take = ExportTake;

        var overview = await GetOverviewAsync(userId, query, cancellationToken);
        var builder = new StringBuilder();

        builder.AppendLine("Region,Period,StartDate,EndDate,EntityType,EntityId,Name,Status,PageViews,UniqueVisitors,Saves,ExternalClicks,AverageRating,ReviewCount");

        foreach (var row in overview.EntityRows)
        {
            builder.AppendCsv(overview.ActiveRegion.DisplayName);
            builder.AppendCsv(overview.Period.Key);
            builder.AppendCsv(overview.Period.StartDate?.ToString("O", CultureInfo.InvariantCulture) ?? string.Empty);
            builder.AppendCsv(overview.Period.EndDate.ToString("O", CultureInfo.InvariantCulture));
            builder.AppendCsv(row.EntityType);
            builder.AppendCsv(row.EntityId.ToString(CultureInfo.InvariantCulture));
            builder.AppendCsv(row.Name);
            builder.AppendCsv(row.Status);
            builder.AppendCsv(row.PageViews.ToString(CultureInfo.InvariantCulture));
            builder.AppendCsv(row.UniqueVisitors.ToString(CultureInfo.InvariantCulture));
            builder.AppendCsv(row.Saves.ToString(CultureInfo.InvariantCulture));
            builder.AppendCsv(row.ExternalClicks.ToString(CultureInfo.InvariantCulture));
            builder.AppendCsv(row.AverageRating.ToString("0.##", CultureInfo.InvariantCulture));
            builder.AppendCsv(row.ReviewCount.ToString(CultureInfo.InvariantCulture), last: true);
            builder.AppendLine();
        }

        var behavior = overview.VisitorBehavior;
        var top = behavior.TopTimeSpentEntity;
        builder.AppendLine();
        builder.AppendLine("VisitorBehavior,LoggedInVisitors,AnonymousVisitors,ReturningVisitors,ReturningVisitorRatePercent,TopTimeSpentEntity,TopTimeSpentEntityType,TopTimeSpentEntityId,AverageTimeSpentSeconds,TotalTimeSpentSeconds,TimeSpentVisitCount");
        builder.AppendCsv("Visitor Behavior");
        builder.AppendCsv(behavior.LoggedInVisitors.ToString(CultureInfo.InvariantCulture));
        builder.AppendCsv(behavior.AnonymousVisitors.ToString(CultureInfo.InvariantCulture));
        builder.AppendCsv(behavior.ReturningVisitors.ToString(CultureInfo.InvariantCulture));
        builder.AppendCsv(behavior.ReturningVisitorRatePercent.ToString("0.##", CultureInfo.InvariantCulture));
        builder.AppendCsv(top?.PageOrEntityName ?? string.Empty);
        builder.AppendCsv(top?.EntityType ?? string.Empty);
        builder.AppendCsv(top?.EntityId?.ToString(CultureInfo.InvariantCulture) ?? string.Empty);
        builder.AppendCsv(top?.AverageTimeSpentSeconds.ToString("0.##", CultureInfo.InvariantCulture) ?? string.Empty);
        builder.AppendCsv(top?.TotalTimeSpentSeconds.ToString("0.##", CultureInfo.InvariantCulture) ?? string.Empty);
        builder.AppendCsv(top?.VisitCount.ToString(CultureInfo.InvariantCulture) ?? string.Empty, last: true);
        builder.AppendLine();

        return builder.ToString();
    }

    private async Task<RegionalAssignedRegionDto> GetAssignedRegionAsync(int userId, int regionId)
    {
        var assignedRegions = await _regionalDashboard.GetAssignedRegionsAsync(userId);
        return assignedRegions.SingleOrDefault(region => region.Id == regionId)
            ?? throw new UnauthorizedAccessException("Region is not assigned to the current regional manager.");
    }

    private async Task<int> GetPendingRequestsCountAsync(int userId, int regionId)
    {
        var summary = await _regionalDashboard.GetSidebarSummaryAsync(userId, regionId);
        return summary.PendingRequestsCount;
    }

    private async Task<List<EntityProjection>> LoadEntitiesAsync(
        int regionId,
        string regionDisplayName,
        string entityType,
        string? search,
        CancellationToken cancellationToken)
    {
        var entities = new List<EntityProjection>();
        var normalizedSearch = search?.Trim();
        var like = string.IsNullOrWhiteSpace(normalizedSearch) ? null : "%" + normalizedSearch + "%";

        if (entityType is "all" or AnalyticsEntityTypes.Destination)
        {
            var query = _db.Destinations
                .AsNoTracking()
                .Where(destination => destination.RegionId == regionId);

            if (like is not null)
            {
                query = query.Where(destination =>
                    EF.Functions.ILike(destination.Name, like) ||
                    EF.Functions.ILike(destination.Status, like) ||
                    EF.Functions.ILike(destination.DestinationType.Name, like));
            }

            entities.AddRange(await query
                .Select(destination => new EntityProjection
                {
                    EntityType = AnalyticsEntityTypes.Destination,
                    EntityId = destination.Id,
                    Name = destination.Name,
                    RegionDisplayName = regionDisplayName,
                    ImageUrl = destination.Images
                        .OrderByDescending(image => image.IsMain)
                        .ThenBy(image => image.SortOrder)
                        .ThenBy(image => image.CreatedAt)
                        .ThenBy(image => image.Id)
                        .Select(image => image.Url)
                        .FirstOrDefault(),
                    CategoryLabel = destination.DestinationType.Name,
                    Status = destination.Status,
                    DetailRoute = "/destinations/detail/" + destination.Id
                })
                .ToListAsync(cancellationToken));
        }

        if (entityType is "all" or AnalyticsEntityTypes.Location)
        {
            var query = _db.Locations
                .AsNoTracking()
                .Where(location => location.Destination.RegionId == regionId);

            if (like is not null)
            {
                query = query.Where(location =>
                    EF.Functions.ILike(location.Name, like) ||
                    EF.Functions.ILike(location.Status, like) ||
                    EF.Functions.ILike(location.LocationType.Name, like));
            }

            entities.AddRange(await query
                .Select(location => new EntityProjection
                {
                    EntityType = AnalyticsEntityTypes.Location,
                    EntityId = location.Id,
                    Name = location.Name,
                    RegionDisplayName = regionDisplayName,
                    ImageUrl = location.Images
                        .OrderByDescending(image => image.IsMain)
                        .ThenBy(image => image.SortOrder)
                        .ThenBy(image => image.CreatedAt)
                        .ThenBy(image => image.Id)
                        .Select(image => image.Url)
                        .FirstOrDefault(),
                    CategoryLabel = location.LocationType.Name,
                    Status = location.Status,
                    DetailRoute = "/locations/detail/" + location.Id
                })
                .ToListAsync(cancellationToken));
        }

        if (entityType is "all" or AnalyticsEntityTypes.Place)
        {
            var query = _db.Places
                .AsNoTracking()
                .Where(place => place.Location.Destination.RegionId == regionId);

            if (like is not null)
            {
                query = query.Where(place =>
                    EF.Functions.ILike(place.Name, like) ||
                    EF.Functions.ILike(place.Status, like) ||
                    EF.Functions.ILike(place.PlaceType.Name, like));
            }

            entities.AddRange(await query
                .Select(place => new EntityProjection
                {
                    EntityType = AnalyticsEntityTypes.Place,
                    EntityId = place.Id,
                    Name = place.Name,
                    RegionDisplayName = regionDisplayName,
                    ImageUrl = place.Images
                        .OrderByDescending(image => image.IsMain)
                        .ThenBy(image => image.SortOrder)
                        .ThenBy(image => image.CreatedAt)
                        .ThenBy(image => image.Id)
                        .Select(image => image.Url)
                        .FirstOrDefault(),
                    CategoryLabel = place.PlaceType.Name,
                    Status = place.Status,
                    DetailRoute = "/places/detail/" + place.Id
                })
                .ToListAsync(cancellationToken));
        }

        if (entityType is "all" or AnalyticsEntityTypes.Event)
        {
            var query = _db.Events
                .AsNoTracking()
                .Where(evt => evt.Destination.RegionId == regionId);

            if (like is not null)
            {
                query = query.Where(evt =>
                    EF.Functions.ILike(evt.Name, like) ||
                    EF.Functions.ILike(evt.Status, like) ||
                    EF.Functions.ILike(evt.EventType.Name, like));
            }

            entities.AddRange(await query
                .Select(evt => new EntityProjection
                {
                    EntityType = AnalyticsEntityTypes.Event,
                    EntityId = evt.Id,
                    Name = evt.Name,
                    RegionDisplayName = regionDisplayName,
                    ImageUrl = evt.Images
                        .OrderByDescending(image => image.IsMain)
                        .ThenBy(image => image.SortOrder)
                        .ThenBy(image => image.CreatedAt)
                        .ThenBy(image => image.Id)
                        .Select(image => image.Url)
                        .FirstOrDefault(),
                    CategoryLabel = evt.EventType.Name,
                    Status = evt.Status,
                    DetailRoute = "/events/detail/" + evt.Id
                })
                .ToListAsync(cancellationToken));
        }

        return entities
            .OrderBy(entity => entity.EntityType, StringComparer.Ordinal)
            .ThenBy(entity => entity.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(entity => entity.EntityId)
            .ToList();
    }

    private async Task<Dictionary<(int Year, int Month), int>> LoadMonthlyVisitCountsAsync(
        IReadOnlyList<EntityProjection> entities,
        DateTime fromUtc,
        DateTime toUtc,
        CancellationToken cancellationToken)
    {
        var destinationIds = EntityIds(entities, AnalyticsEntityTypes.Destination);
        var locationIds = EntityIds(entities, AnalyticsEntityTypes.Location);
        var placeIds = EntityIds(entities, AnalyticsEntityTypes.Place);
        var eventIds = EntityIds(entities, AnalyticsEntityTypes.Event);

        var monthlyRows = await _db.AnalyticsEvents
            .AsNoTracking()
            .Where(evt =>
                evt.OccurredAt >= fromUtc &&
                evt.OccurredAt < toUtc &&
                evt.EntityId.HasValue &&
                evt.EntityType != null &&
                ViewEventTypes.Contains(evt.EventType) &&
                ((evt.EntityType == AnalyticsEntityTypes.Destination && destinationIds.Contains(evt.EntityId.Value)) ||
                 (evt.EntityType == AnalyticsEntityTypes.Location && locationIds.Contains(evt.EntityId.Value)) ||
                 ((evt.EntityType == AnalyticsEntityTypes.Place || evt.EntityType == AnalyticsEntityTypes.Hotel) && placeIds.Contains(evt.EntityId.Value)) ||
                 (evt.EntityType == AnalyticsEntityTypes.Event && eventIds.Contains(evt.EntityId.Value))))
            .GroupBy(evt => new { evt.OccurredAt.Year, evt.OccurredAt.Month })
            .Select(group => new MonthlyVisitAggregate
            {
                Year = group.Key.Year,
                Month = group.Key.Month,
                Count = group.Count()
            })
            .ToListAsync(cancellationToken);

        return monthlyRows.ToDictionary(row => (row.Year, row.Month), row => row.Count);
    }

    private async Task<MetricSet> BuildMetricSetAsync(
        IReadOnlyList<EntityProjection> entities,
        DateTime? fromUtc,
        DateTime toUtc,
        CancellationToken cancellationToken)
    {
        var rows = entities.Select(entity => new RegionalAnalyticsEntityMetricDto
        {
            EntityType = entity.EntityType,
            EntityId = entity.EntityId,
            Name = entity.Name,
            RegionDisplayName = entity.RegionDisplayName,
            ImageUrl = entity.ImageUrl,
            CategoryLabel = entity.CategoryLabel,
            Status = entity.Status,
            DetailRoute = entity.DetailRoute
        }).ToList();

        if (entities.Count == 0)
        {
            return new MetricSet(
                rows,
                new RegionalAnalyticsTotalsDto(),
                new List<AnalyticsMetricEventRow>(),
                new List<AnalyticsMetricEventRow>());
        }

        var keys = entities.Select(entity => new EntityKey(entity.EntityType, entity.EntityId)).ToHashSet();
        var destinationIds = EntityIds(entities, AnalyticsEntityTypes.Destination);
        var locationIds = EntityIds(entities, AnalyticsEntityTypes.Location);
        var placeIds = EntityIds(entities, AnalyticsEntityTypes.Place);
        var eventIds = EntityIds(entities, AnalyticsEntityTypes.Event);

        var eventQuery = ApplyAnalyticsPeriod(_db.AnalyticsEvents.AsNoTracking(), fromUtc, toUtc)
            .Where(evt =>
                evt.EntityId.HasValue &&
                evt.EntityType != null &&
                (ViewEventTypes.Contains(evt.EventType) ||
                 ExternalClickEventTypes.Contains(evt.EventType) ||
                 evt.EventType == AnalyticsEventTypes.PageDuration) &&
                ((evt.EntityType == AnalyticsEntityTypes.Destination && destinationIds.Contains(evt.EntityId.Value)) ||
                 (evt.EntityType == AnalyticsEntityTypes.Location && locationIds.Contains(evt.EntityId.Value)) ||
                 ((evt.EntityType == AnalyticsEntityTypes.Place || evt.EntityType == AnalyticsEntityTypes.Hotel) && placeIds.Contains(evt.EntityId.Value)) ||
                 (evt.EntityType == AnalyticsEntityTypes.Event && eventIds.Contains(evt.EntityId.Value))));

        var analyticsEvents = (await eventQuery
                .Select(evt => new AnalyticsMetricEventRow
                {
                    EntityType = evt.EntityType == AnalyticsEntityTypes.Hotel ? AnalyticsEntityTypes.Place : evt.EntityType!,
                    EntityId = evt.EntityId!.Value,
                    EventType = evt.EventType,
                    UserId = evt.UserId,
                    AnonymousSessionId = evt.AnonymousSessionId,
                    OccurredAt = evt.OccurredAt,
                    Route = evt.Route,
                    DurationMs = evt.DurationMs
                })
                .ToListAsync(cancellationToken))
            .Where(evt => keys.Contains(new EntityKey(evt.EntityType, evt.EntityId)))
            .ToList();

        ApplyAnalyticsEventMetrics(rows, analyticsEvents);
        await ApplySaveMetricsAsync(rows, destinationIds, locationIds, placeIds, eventIds, fromUtc, toUtc, cancellationToken);
        await ApplyReviewMetricsAsync(rows, destinationIds, locationIds, placeIds, eventIds, fromUtc, toUtc, cancellationToken);

        var viewEvents = analyticsEvents.Where(evt => ViewEventTypes.Contains(evt.EventType)).ToList();
        var durationEvents = analyticsEvents
            .Where(evt => evt.EventType == AnalyticsEventTypes.PageDuration && NormalizeDurationMs(evt.DurationMs).HasValue)
            .ToList();
        var visitorCount = CountUniqueVisitors(viewEvents);
        var reviewRows = rows.Where(row => row.ReviewCount > 0).ToList();
        var totalReviews = rows.Sum(row => row.ReviewCount);
        var averageRating = totalReviews == 0
            ? 0
            : Math.Round(
                reviewRows.Sum(row => row.AverageRating * row.ReviewCount) / totalReviews,
                2,
                MidpointRounding.AwayFromZero);

        return new MetricSet(
            rows,
            new RegionalAnalyticsTotalsDto
            {
                PageViews = rows.Sum(row => row.PageViews),
                UniqueVisitors = visitorCount,
                Saves = rows.Sum(row => row.Saves),
                AverageRating = averageRating,
                ExternalClicks = rows.Sum(row => row.ExternalClicks),
                TotalReviews = totalReviews
            },
            viewEvents,
            durationEvents);
    }

    private async Task<RegionalAnalyticsTotalsDto?> BuildPreviousTotalsAsync(
        IReadOnlyList<EntityProjection> entities,
        PeriodWindow period,
        CancellationToken cancellationToken)
    {
        if (!period.StartDate.HasValue || !period.Duration.HasValue)
            return null;

        var previousEnd = period.StartDate.Value;
        var previousStart = previousEnd - period.Duration.Value;
        var previous = await BuildMetricSetAsync(entities, previousStart, previousEnd, cancellationToken);
        return previous.Totals;
    }

    private static void ApplyAnalyticsEventMetrics(
        List<RegionalAnalyticsEntityMetricDto> rows,
        IReadOnlyList<AnalyticsMetricEventRow> events)
    {
        var byKey = rows.ToDictionary(row => new EntityKey(row.EntityType, row.EntityId));

        foreach (var group in events.Where(evt => ViewEventTypes.Contains(evt.EventType))
                     .GroupBy(evt => new EntityKey(evt.EntityType, evt.EntityId)))
        {
            if (byKey.TryGetValue(group.Key, out var row))
            {
                row.PageViews = group.Count();
                row.UniqueVisitors = CountUniqueVisitors(group);
            }
        }

        foreach (var group in events.Where(evt => ExternalClickEventTypes.Contains(evt.EventType))
                     .GroupBy(evt => new EntityKey(evt.EntityType, evt.EntityId)))
        {
            if (byKey.TryGetValue(group.Key, out var row))
            {
                row.ExternalClicks = group.Count();
            }
        }
    }

    private async Task ApplySaveMetricsAsync(
        List<RegionalAnalyticsEntityMetricDto> rows,
        IReadOnlyCollection<int> destinationIds,
        IReadOnlyCollection<int> locationIds,
        IReadOnlyCollection<int> placeIds,
        IReadOnlyCollection<int> eventIds,
        DateTime? fromUtc,
        DateTime toUtc,
        CancellationToken cancellationToken)
    {
        var byKey = rows.ToDictionary(row => new EntityKey(row.EntityType, row.EntityId));

        var favoriteQuery = ApplyCreatedPeriod(_db.Favorites.AsNoTracking(), fromUtc, toUtc)
            .Where(favorite =>
                (favorite.EntityType == AnalyticsEntityTypes.Place && placeIds.Contains(favorite.EntityId)) ||
                (favorite.EntityType == AnalyticsEntityTypes.Event && eventIds.Contains(favorite.EntityId)));

        var favoriteRows = await favoriteQuery
            .Select(favorite => new
            {
                EntityType = favorite.EntityType,
                favorite.EntityId,
                favorite.UserId
            })
            .Distinct()
            .ToListAsync(cancellationToken);

        foreach (var group in favoriteRows.GroupBy(row => new EntityKey(row.EntityType, row.EntityId)))
        {
            if (byKey.TryGetValue(group.Key, out var metric))
            {
                metric.Saves = group.Count();
            }
        }

        var destinationSaves = await ApplyCreatedPeriod(_db.CollectionItems.AsNoTracking(), fromUtc, toUtc)
            .Where(item => item.DestinationId.HasValue && destinationIds.Contains(item.DestinationId.Value))
            .Select(item => new { EntityId = item.DestinationId!.Value, item.Collection.UserId })
            .Distinct()
            .ToListAsync(cancellationToken);

        foreach (var group in destinationSaves.GroupBy(row => new EntityKey(AnalyticsEntityTypes.Destination, row.EntityId)))
        {
            if (byKey.TryGetValue(group.Key, out var metric))
            {
                metric.Saves = group.Count();
            }
        }

        var locationSaves = await ApplyCreatedPeriod(_db.CollectionItems.AsNoTracking(), fromUtc, toUtc)
            .Where(item => item.LocationId.HasValue && locationIds.Contains(item.LocationId.Value))
            .Select(item => new { EntityId = item.LocationId!.Value, item.Collection.UserId })
            .Distinct()
            .ToListAsync(cancellationToken);

        foreach (var group in locationSaves.GroupBy(row => new EntityKey(AnalyticsEntityTypes.Location, row.EntityId)))
        {
            if (byKey.TryGetValue(group.Key, out var metric))
            {
                metric.Saves = group.Count();
            }
        }
    }

    private async Task ApplyReviewMetricsAsync(
        List<RegionalAnalyticsEntityMetricDto> rows,
        IReadOnlyCollection<int> destinationIds,
        IReadOnlyCollection<int> locationIds,
        IReadOnlyCollection<int> placeIds,
        IReadOnlyCollection<int> eventIds,
        DateTime? fromUtc,
        DateTime toUtc,
        CancellationToken cancellationToken)
    {
        var byKey = rows.ToDictionary(row => new EntityKey(row.EntityType, row.EntityId));

        var destinationReviews = await ApplyCreatedPeriod(_db.DestinationReviews.AsNoTracking(), fromUtc, toUtc)
            .Where(review => destinationIds.Contains(review.DestinationId))
            .GroupBy(review => review.DestinationId)
            .Select(group => new { EntityId = group.Key, Count = group.Count(), Average = group.Average(review => review.Rating) })
            .ToListAsync(cancellationToken);

        foreach (var row in destinationReviews)
        {
            ApplyReviewAggregate(byKey, AnalyticsEntityTypes.Destination, row.EntityId, row.Count, row.Average);
        }

        var locationReviews = await ApplyCreatedPeriod(_db.LocationReviews.AsNoTracking(), fromUtc, toUtc)
            .Where(review => locationIds.Contains(review.LocationId))
            .GroupBy(review => review.LocationId)
            .Select(group => new { EntityId = group.Key, Count = group.Count(), Average = group.Average(review => review.Rating) })
            .ToListAsync(cancellationToken);

        foreach (var row in locationReviews)
        {
            ApplyReviewAggregate(byKey, AnalyticsEntityTypes.Location, row.EntityId, row.Count, row.Average);
        }

        var placeReviews = await ApplyCreatedPeriod(_db.PlaceReviews.AsNoTracking(), fromUtc, toUtc)
            .Where(review => placeIds.Contains(review.PlaceId))
            .GroupBy(review => review.PlaceId)
            .Select(group => new { EntityId = group.Key, Count = group.Count(), Average = group.Average(review => review.Rating) })
            .ToListAsync(cancellationToken);

        foreach (var row in placeReviews)
        {
            ApplyReviewAggregate(byKey, AnalyticsEntityTypes.Place, row.EntityId, row.Count, row.Average);
        }

        var eventReviews = await ApplyCreatedPeriod(_db.EventReviews.AsNoTracking(), fromUtc, toUtc)
            .Where(review => eventIds.Contains(review.EventId))
            .GroupBy(review => review.EventId)
            .Select(group => new { EntityId = group.Key, Count = group.Count(), Average = group.Average(review => review.Rating) })
            .ToListAsync(cancellationToken);

        foreach (var row in eventReviews)
        {
            ApplyReviewAggregate(byKey, AnalyticsEntityTypes.Event, row.EntityId, row.Count, row.Average);
        }
    }

    private static void ApplyReviewAggregate(
        IReadOnlyDictionary<EntityKey, RegionalAnalyticsEntityMetricDto> rows,
        string entityType,
        int entityId,
        int count,
        double average)
    {
        if (!rows.TryGetValue(new EntityKey(entityType, entityId), out var row))
            return;

        row.ReviewCount = count;
        row.AverageRating = Math.Round(average, 2, MidpointRounding.AwayFromZero);
    }

    private static List<RegionalAnalyticsEntityMetricDto> SortRows(
        IReadOnlyList<RegionalAnalyticsEntityMetricDto> rows,
        string? sort)
    {
        var normalized = NormalizeSort(sort);
        IOrderedEnumerable<RegionalAnalyticsEntityMetricDto> ordered = normalized switch
        {
            "views_asc" => rows.OrderBy(row => row.PageViews).ThenBy(row => row.Name, StringComparer.OrdinalIgnoreCase),
            "visitors_desc" => rows.OrderByDescending(row => row.UniqueVisitors).ThenBy(row => row.Name, StringComparer.OrdinalIgnoreCase),
            "visitors_asc" => rows.OrderBy(row => row.UniqueVisitors).ThenBy(row => row.Name, StringComparer.OrdinalIgnoreCase),
            "saves_desc" => rows.OrderByDescending(row => row.Saves).ThenBy(row => row.Name, StringComparer.OrdinalIgnoreCase),
            "saves_asc" => rows.OrderBy(row => row.Saves).ThenBy(row => row.Name, StringComparer.OrdinalIgnoreCase),
            "rating_desc" => rows.OrderByDescending(row => row.AverageRating).ThenByDescending(row => row.ReviewCount).ThenBy(row => row.Name, StringComparer.OrdinalIgnoreCase),
            "rating_asc" => rows.OrderBy(row => row.AverageRating).ThenBy(row => row.Name, StringComparer.OrdinalIgnoreCase),
            "name_asc" => rows.OrderBy(row => row.Name, StringComparer.OrdinalIgnoreCase),
            "name_desc" => rows.OrderByDescending(row => row.Name, StringComparer.OrdinalIgnoreCase),
            "external_desc" => rows.OrderByDescending(row => row.ExternalClicks).ThenBy(row => row.Name, StringComparer.OrdinalIgnoreCase),
            "reviews_desc" => rows.OrderByDescending(row => row.ReviewCount).ThenBy(row => row.Name, StringComparer.OrdinalIgnoreCase),
            _ => rows.OrderByDescending(row => row.PageViews).ThenBy(row => row.Name, StringComparer.OrdinalIgnoreCase)
        };

        return ordered.ThenBy(row => row.EntityId).ToList();
    }

    private static List<RegionalAnalyticsDistributionItemDto> BuildDistribution(
        IReadOnlyList<EntityProjection> entities,
        string entityType)
    {
        if (entityType == "all")
        {
            return entities
                .GroupBy(entity => entity.EntityType)
                .Select(group => new RegionalAnalyticsDistributionItemDto
                {
                    EntityType = group.Key,
                    Label = ToDisplayLabel(group.Key),
                    Count = group.Count()
                })
                .OrderByDescending(item => item.Count)
                .ThenBy(item => item.Label, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        return entities
            .GroupBy(entity => string.IsNullOrWhiteSpace(entity.CategoryLabel) ? ToDisplayLabel(entity.EntityType) : entity.CategoryLabel!)
            .Select(group => new RegionalAnalyticsDistributionItemDto
            {
                EntityType = entityType,
                Label = group.Key,
                Count = group.Count()
            })
            .OrderByDescending(item => item.Count)
            .ThenBy(item => item.Label, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static RegionalAnalyticsActionableInsightsDto BuildActionableInsights(
        IReadOnlyList<RegionalAnalyticsEntityMetricDto> rows,
        int inventoryCount)
    {
        var topViewedLocation = rows
            .Where(row => row.EntityType == AnalyticsEntityTypes.Location && row.PageViews > 0)
            .OrderByDescending(row => row.PageViews)
            .ThenBy(row => row.Name, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();

        var mostSaved = rows
            .Where(row => row.Saves > 0)
            .OrderByDescending(row => row.Saves)
            .ThenByDescending(row => row.PageViews)
            .ThenBy(row => row.Name, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();

        var highestPerformingEvent = rows
            .Where(row => row.EntityType == AnalyticsEntityTypes.Event && (row.PageViews > 0 || row.Saves > 0 || row.ExternalClicks > 0 || row.ReviewCount > 0))
            .OrderByDescending(row => row.PageViews)
            .ThenByDescending(row => row.Saves)
            .ThenByDescending(row => row.ExternalClicks)
            .ThenByDescending(row => row.AverageRating)
            .ThenBy(row => row.Name, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();

        var messages = new List<string>();
        if (rows.Count == 0)
        {
            messages.Add(inventoryCount == 0
                ? "No entities match the current filters."
                : "No engagement has been collected for the current filters yet.");
        }
        else if (rows.All(row => row.PageViews == 0 && row.Saves == 0 && row.ExternalClicks == 0 && row.ReviewCount == 0))
        {
            messages.Add("No engagement has been collected for the current filters yet.");
        }

        return new RegionalAnalyticsActionableInsightsDto
        {
            TopViewedLocation = topViewedLocation,
            MostSaved = mostSaved,
            HighestPerformingEvent = highestPerformingEvent,
            Messages = messages
        };
    }

    private static RegionalVisitorBehaviorAnalyticsDto BuildVisitorBehavior(
        IReadOnlyList<AnalyticsMetricEventRow> viewEvents,
        IReadOnlyList<AnalyticsMetricEventRow> durationEvents,
        IReadOnlyList<RegionalAnalyticsEntityMetricDto> rows)
    {
        var linkedAnonymousSessions = BuildLinkedAnonymousSessions(viewEvents);

        var loggedInVisitors = viewEvents
            .Where(row => row.UserId.HasValue)
            .Select(row => row.UserId!.Value)
            .Distinct()
            .Count();

        var anonymousVisitors = viewEvents
            .Where(row =>
                row.UserId is null &&
                row.AnonymousSessionId.HasValue &&
                !linkedAnonymousSessions.ContainsKey(row.AnonymousSessionId.Value))
            .Select(row => row.AnonymousSessionId!.Value)
            .Distinct()
            .Count();

        // Returning visitor definition: a visitor has at least two region-scoped
        // view events in the selected period, separated by 30+ seconds or on a
        // different entity/page. This avoids counting immediate duplicate emits
        // as a return when explicit cross-session ids are not available.
        var returningVisitors = viewEvents
            .GroupBy(row => BuildVisitorKey(row, linkedAnonymousSessions))
            .Where(group => group.Key is not null)
            .Count(group => IsReturningVisitor(group.OrderBy(row => row.OccurredAt).ToList()));

        var totalVisitors = CountUniqueVisitors(viewEvents);
        var returningRate = totalVisitors == 0
            ? 0
            : Math.Round((double)returningVisitors / totalVisitors * 100, 1, MidpointRounding.AwayFromZero);

        var topTimeSpentPages = BuildTimeSpentPages(durationEvents, rows);
        var notes = new List<string>();

        if (totalVisitors == 0)
        {
            notes.Add("Not enough visitor behavior data yet.");
        }
        else if (topTimeSpentPages.Count == 0)
        {
            notes.Add("Time-spent analytics will appear after visitors leave tracked entity pages.");
        }

        return new RegionalVisitorBehaviorAnalyticsDto
        {
            LoggedInVisitors = loggedInVisitors,
            AnonymousVisitors = anonymousVisitors,
            ReturningVisitors = returningVisitors,
            ReturningVisitorRatePercent = returningRate,
            TopTimeSpentEntity = topTimeSpentPages.FirstOrDefault(),
            TopTimeSpentPages = topTimeSpentPages,
            HasTimeSpentData = topTimeSpentPages.Count > 0,
            Notes = notes
        };
    }

    private static bool IsReturningVisitor(IReadOnlyList<AnalyticsMetricEventRow> orderedEvents)
    {
        if (orderedEvents.Count < 2)
        {
            return false;
        }

        var first = orderedEvents[0];
        return orderedEvents.Skip(1).Any(row =>
            row.OccurredAt - first.OccurredAt >= ReturningVisitorMinimumGap ||
            row.EntityType != first.EntityType ||
            row.EntityId != first.EntityId ||
            !string.Equals(row.Route, first.Route, StringComparison.Ordinal));
    }

    private static List<RegionalTimeSpentEntityDto> BuildTimeSpentPages(
        IReadOnlyList<AnalyticsMetricEventRow> durationEvents,
        IReadOnlyList<RegionalAnalyticsEntityMetricDto> rows)
    {
        var rowByKey = rows.ToDictionary(row => new EntityKey(row.EntityType, row.EntityId));

        return durationEvents
            .Select(row => new
            {
                Row = row,
                DurationMs = NormalizeDurationMs(row.DurationMs)
            })
            .Where(row => row.DurationMs.HasValue)
            .GroupBy(row => new EntityKey(row.Row.EntityType, row.Row.EntityId))
            .Select(group =>
            {
                if (!rowByKey.TryGetValue(group.Key, out var entity))
                {
                    return null;
                }

                var durations = group.Select(row => row.DurationMs!.Value).ToList();
                var totalSeconds = durations.Sum() / 1000.0;
                var visitCount = durations.Count;
                var averageSeconds = visitCount == 0 ? 0 : totalSeconds / visitCount;

                return new RegionalTimeSpentEntityDto
                {
                    PageOrEntityName = entity.Name,
                    EntityType = entity.EntityType,
                    EntityId = entity.EntityId,
                    RouteOrPage = group.Select(row => row.Row.Route).FirstOrDefault(route => !string.IsNullOrWhiteSpace(route)) ?? entity.DetailRoute,
                    AverageTimeSpentSeconds = Math.Round(averageSeconds, 1, MidpointRounding.AwayFromZero),
                    TotalTimeSpentSeconds = Math.Round(totalSeconds, 1, MidpointRounding.AwayFromZero),
                    VisitCount = visitCount
                };
            })
            .Where(item => item is not null)
            .Cast<RegionalTimeSpentEntityDto>()
            .OrderByDescending(item => item.AverageTimeSpentSeconds)
            .ThenByDescending(item => item.TotalTimeSpentSeconds)
            .ThenByDescending(item => item.VisitCount)
            .ThenBy(item => item.PageOrEntityName, StringComparer.OrdinalIgnoreCase)
            .Take(5)
            .ToList();
    }

    private static List<RegionalAnalyticsTrendPointDto> BuildTrend(
        IReadOnlyList<AnalyticsMetricEventRow> viewEvents,
        DateTime? fromUtc,
        DateTime toUtc)
    {
        var start = (fromUtc ?? toUtc.AddDays(-29)).Date;
        var end = toUtc.Date;
        var dayCount = Math.Clamp((int)Math.Ceiling((toUtc - start).TotalDays), 1, 366);

        if (dayCount > 366)
        {
            start = end.AddDays(-365);
            dayCount = 366;
        }

        var viewsByDate = viewEvents
            .Where(evt => evt.OccurredAt >= start)
            .GroupBy(evt => evt.OccurredAt.Date)
            .ToDictionary(group => group.Key, group => group.ToList());

        var points = new List<RegionalAnalyticsTrendPointDto>();
        for (var i = 0; i < dayCount; i++)
        {
            var date = start.AddDays(i);
            viewsByDate.TryGetValue(date, out var eventsForDate);
            eventsForDate ??= new List<AnalyticsMetricEventRow>();

            points.Add(new RegionalAnalyticsTrendPointDto
            {
                Date = date,
                Views = eventsForDate.Count,
                UniqueVisitors = CountUniqueVisitors(eventsForDate)
            });
        }

        return points;
    }

    private static RegionalAnalyticsComparisonDto BuildComparison(
        RegionalAnalyticsTotalsDto current,
        RegionalAnalyticsTotalsDto? previous)
    {
        if (previous is null)
        {
            return new RegionalAnalyticsComparisonDto();
        }

        return new RegionalAnalyticsComparisonDto
        {
            PageViewsChangePercent = PercentChange(current.PageViews, previous.PageViews),
            UniqueVisitorsChangePercent = PercentChange(current.UniqueVisitors, previous.UniqueVisitors),
            SavesChangePercent = PercentChange(current.Saves, previous.Saves),
            AverageRatingChangePercent = PercentChange(current.AverageRating, previous.AverageRating),
            ExternalClicksChangePercent = PercentChange(current.ExternalClicks, previous.ExternalClicks),
            TotalReviewsChangePercent = PercentChange(current.TotalReviews, previous.TotalReviews)
        };
    }

    private static double PercentChange(double current, double previous)
    {
        if (Math.Abs(previous) < 0.0001)
            return Math.Abs(current) < 0.0001 ? 0 : 100;

        return Math.Round(((current - previous) / previous) * 100, 1, MidpointRounding.AwayFromZero);
    }

    private static int CountUniqueVisitors(IEnumerable<AnalyticsMetricEventRow> rows)
    {
        var materializedRows = rows as IReadOnlyList<AnalyticsMetricEventRow> ?? rows.ToList();
        var linkedAnonymousSessions = BuildLinkedAnonymousSessions(materializedRows);

        return materializedRows
            .Select(row => BuildVisitorKey(row, linkedAnonymousSessions))
            .Where(key => key is not null)
            .Distinct(StringComparer.Ordinal)
            .Count();
    }

    private static Dictionary<Guid, int> BuildLinkedAnonymousSessions(IEnumerable<AnalyticsMetricEventRow> rows)
    {
        // Authenticated events retain the anonymous session id, so an anonymous
        // pre-login view can be counted as the same visitor when the link is unambiguous.
        return rows
            .Where(row => row.UserId.HasValue && row.AnonymousSessionId.HasValue)
            .GroupBy(row => row.AnonymousSessionId!.Value)
            .Where(group => group.Select(row => row.UserId!.Value).Distinct().Count() == 1)
            .ToDictionary(
                group => group.Key,
                group => group.Select(row => row.UserId!.Value).First());
    }

    private static string? BuildVisitorKey(
        AnalyticsMetricEventRow row,
        IReadOnlyDictionary<Guid, int> linkedAnonymousSessions)
    {
        if (row.UserId.HasValue)
            return "user:" + row.UserId.Value.ToString(CultureInfo.InvariantCulture);

        if (row.AnonymousSessionId is { } anonymousSessionId &&
            linkedAnonymousSessions.TryGetValue(anonymousSessionId, out var linkedUserId))
        {
            return "user:" + linkedUserId.ToString(CultureInfo.InvariantCulture);
        }

        return row.AnonymousSessionId.HasValue
            ? "anon:" + row.AnonymousSessionId.Value.ToString("N")
            : null;
    }

    private static int? NormalizeDurationMs(int? durationMs)
    {
        if (!durationMs.HasValue || durationMs.Value < MinDurationMs)
        {
            return null;
        }

        return Math.Min(durationMs.Value, MaxDurationMs);
    }

    private static PeriodWindow ResolvePeriod(string? value)
    {
        var key = NormalizeToken(value ?? "30d");
        var now = DateTime.UtcNow;

        return key switch
        {
            "" or "30d" or "last30days" => new PeriodWindow("30d", now.AddDays(-30), now, TimeSpan.FromDays(30)),
            "7d" or "last7days" => new PeriodWindow("7d", now.AddDays(-7), now, TimeSpan.FromDays(7)),
            "90d" or "last90days" => new PeriodWindow("90d", now.AddDays(-90), now, TimeSpan.FromDays(90)),
            "all" or "alltime" => new PeriodWindow("all", null, now, null),
            _ => throw new InvalidOperationException("Invalid period filter.")
        };
    }

    private static string NormalizeEntityTypeFilter(string? value)
    {
        var normalized = NormalizeToken(value ?? "all");
        return normalized switch
        {
            "" or "all" or "allentities" => "all",
            "destination" or "destinations" => AnalyticsEntityTypes.Destination,
            "location" or "locations" => AnalyticsEntityTypes.Location,
            "place" or "places" or "hotel" or "hotels" or "stay" or "stays" => AnalyticsEntityTypes.Place,
            "event" or "events" => AnalyticsEntityTypes.Event,
            _ => throw new InvalidOperationException("Invalid entity type filter.")
        };
    }

    private static string NormalizeSort(string? value)
    {
        var normalized = NormalizeToken(value ?? "views_desc");
        return normalized switch
        {
            "" or "views" or "pageviews" or "pageviewsdesc" or "viewsdesc" => "views_desc",
            "viewsasc" or "pageviewsasc" => "views_asc",
            "visitors" or "visitorsdesc" or "uniquevisitors" or "uniquevisitorsdesc" => "visitors_desc",
            "visitorsasc" or "uniquevisitorsasc" => "visitors_asc",
            "saves" or "savesdesc" => "saves_desc",
            "savesasc" => "saves_asc",
            "rating" or "ratingdesc" or "reviewavg" or "reviewavgdesc" => "rating_desc",
            "ratingasc" or "reviewavgasc" => "rating_asc",
            "name" or "nameasc" => "name_asc",
            "namedesc" => "name_desc",
            "external" or "externaldesc" or "externalclicks" or "externalclicksdesc" => "external_desc",
            "reviews" or "reviewsdesc" or "reviewcount" or "reviewcountdesc" => "reviews_desc",
            _ => throw new InvalidOperationException("Invalid sort filter.")
        };
    }

    private static bool HasEngagement(RegionalAnalyticsEntityMetricDto row) =>
        row.PageViews > 0 ||
        row.UniqueVisitors > 0 ||
        row.Saves > 0 ||
        row.ExternalClicks > 0 ||
        row.ReviewCount > 0;

    private static string NormalizeToken(string value) =>
        value.Trim()
            .Replace("-", string.Empty, StringComparison.Ordinal)
            .Replace("_", string.Empty, StringComparison.Ordinal)
            .Replace(" ", string.Empty, StringComparison.Ordinal)
            .ToLowerInvariant();

    private static IQueryable<AnalyticsEvent> ApplyAnalyticsPeriod(
        IQueryable<AnalyticsEvent> query,
        DateTime? fromUtc,
        DateTime toUtc)
    {
        if (fromUtc.HasValue)
        {
            query = query.Where(item => item.OccurredAt >= fromUtc.Value);
        }

        return query.Where(item => item.OccurredAt < toUtc);
    }

    private static IQueryable<T> ApplyCreatedPeriod<T>(
        IQueryable<T> query,
        DateTime? fromUtc,
        DateTime toUtc)
        where T : class
    {
        if (typeof(T) == typeof(Favorite))
        {
            var favorites = (IQueryable<Favorite>)query;
            if (fromUtc.HasValue)
                favorites = favorites.Where(item => item.CreatedAt >= fromUtc.Value);
            return (IQueryable<T>)favorites.Where(item => item.CreatedAt < toUtc);
        }

        if (typeof(T) == typeof(CollectionItem))
        {
            var collectionItems = (IQueryable<CollectionItem>)query;
            if (fromUtc.HasValue)
                collectionItems = collectionItems.Where(item => item.CreatedAt >= fromUtc.Value);
            return (IQueryable<T>)collectionItems.Where(item => item.CreatedAt < toUtc);
        }

        if (typeof(T) == typeof(DestinationReview))
        {
            var reviews = (IQueryable<DestinationReview>)query;
            if (fromUtc.HasValue)
                reviews = reviews.Where(item => item.CreatedAt >= fromUtc.Value);
            return (IQueryable<T>)reviews.Where(item => item.CreatedAt < toUtc);
        }

        if (typeof(T) == typeof(LocationReview))
        {
            var reviews = (IQueryable<LocationReview>)query;
            if (fromUtc.HasValue)
                reviews = reviews.Where(item => item.CreatedAt >= fromUtc.Value);
            return (IQueryable<T>)reviews.Where(item => item.CreatedAt < toUtc);
        }

        if (typeof(T) == typeof(PlaceReview))
        {
            var reviews = (IQueryable<PlaceReview>)query;
            if (fromUtc.HasValue)
                reviews = reviews.Where(item => item.CreatedAt >= fromUtc.Value);
            return (IQueryable<T>)reviews.Where(item => item.CreatedAt < toUtc);
        }

        if (typeof(T) == typeof(EventReview))
        {
            var reviews = (IQueryable<EventReview>)query;
            if (fromUtc.HasValue)
                reviews = reviews.Where(item => item.CreatedAt >= fromUtc.Value);
            return (IQueryable<T>)reviews.Where(item => item.CreatedAt < toUtc);
        }

        return query;
    }

    private static HashSet<int> EntityIds(IEnumerable<EntityProjection> entities, string entityType) =>
        entities
            .Where(entity => entity.EntityType == entityType)
            .Select(entity => entity.EntityId)
            .ToHashSet();

    private static string ToDisplayLabel(string entityType) =>
        entityType switch
        {
            AnalyticsEntityTypes.Destination => "Destinations",
            AnalyticsEntityTypes.Location => "Locations",
            AnalyticsEntityTypes.Place => "Places",
            AnalyticsEntityTypes.Event => "Events",
            _ => entityType
        };

    private static string BuildMonthlyVisitYearLabel(DateTime windowStart)
    {
        var windowEnd = windowStart.AddMonths(11);
        return windowStart.Year == windowEnd.Year
            ? windowEnd.Year.ToString(CultureInfo.InvariantCulture)
            : windowStart.Year.ToString(CultureInfo.InvariantCulture) + "-" + windowEnd.Year.ToString(CultureInfo.InvariantCulture);
    }

    private sealed record PeriodWindow(string Key, DateTime? StartDate, DateTime EndDate, TimeSpan? Duration);

    private sealed record EntityKey(string EntityType, int EntityId);

    private sealed class EntityProjection
    {
        public string EntityType { get; set; } = string.Empty;
        public int EntityId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string RegionDisplayName { get; set; } = string.Empty;
        public string? ImageUrl { get; set; }
        public string? CategoryLabel { get; set; }
        public string Status { get; set; } = string.Empty;
        public string? DetailRoute { get; set; }
    }

    private sealed class MonthlyVisitAggregate
    {
        public int Year { get; set; }
        public int Month { get; set; }
        public int Count { get; set; }
    }

    private sealed class AnalyticsMetricEventRow
    {
        public string EntityType { get; set; } = string.Empty;
        public int EntityId { get; set; }
        public string EventType { get; set; } = string.Empty;
        public int? UserId { get; set; }
        public Guid? AnonymousSessionId { get; set; }
        public DateTime OccurredAt { get; set; }
        public string? Route { get; set; }
        public int? DurationMs { get; set; }
    }

    private sealed record MetricSet(
        List<RegionalAnalyticsEntityMetricDto> Rows,
        RegionalAnalyticsTotalsDto Totals,
        List<AnalyticsMetricEventRow> ViewEvents,
        List<AnalyticsMetricEventRow> DurationEvents);
}

internal static class RegionalAnalyticsCsvExtensions
{
    public static void AppendCsv(this StringBuilder builder, string value, bool last = false)
    {
        var escaped = value.Replace("\"", "\"\"", StringComparison.Ordinal);
        builder.Append('"').Append(escaped).Append('"');
        if (!last)
        {
            builder.Append(',');
        }
    }
}
