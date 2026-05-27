using System.Globalization;
using System.Text;
using Geotrack.Application.Analytics;
using Geotrack.Application.DTOs.Admin;
using Geotrack.Application.Interfaces.Service;
using Geotrack.Application.Security;
using Microsoft.EntityFrameworkCore;

namespace Geotrack.Infrastructure.Services;

public sealed class TerritoryAnalyticsService : ITerritoryAnalyticsService
{
    private const int DefaultTake = 5;
    private const int MaxTake = 100;
    private const int ChartSeriesTake = 8;
    private const string PublishedStatus = "Published";

    private static readonly string[] ViewEventTypes =
    {
        AnalyticsEventTypes.PageView,
        AnalyticsEventTypes.DetailView
    };

    private static readonly string[] ExternalClickEventTypes =
    {
        AnalyticsEventTypes.OutboundLinkClicked,
        AnalyticsEventTypes.DirectionsStarted
    };

    private readonly AppDbContext _db;
    private readonly IHealthScoreService _healthScores;

    public TerritoryAnalyticsService(AppDbContext db, IHealthScoreService healthScores)
    {
        _db = db;
        _healthScores = healthScores;
    }

    public async Task<TerritoryAnalyticsOverviewDto> GetOverviewAsync(
        int userId,
        TerritoryAnalyticsOverviewQuery query,
        CancellationToken cancellationToken)
    {
        query ??= new TerritoryAnalyticsOverviewQuery();
        var activeTerritory = await GetAuthorizedTerritoryAsync(userId, query.TerritoryId, cancellationToken);
        var period = ResolvePeriod(query.Period);
        var healthPeriod = new HealthScorePeriod(period.Key, BuildPeriodLabel(period.Key), period.StartDate, period.EndDate);
        var status = NormalizeStatusFilter(query.Status);
        var skip = Math.Max(0, query.Skip);
        var take = Math.Clamp(query.Take <= 0 ? DefaultTake : query.Take, 1, MaxTake);

        if (activeTerritory is null)
        {
            return new TerritoryAnalyticsOverviewDto
            {
                Filters = BuildFilters(period, status, new List<int>()),
                RegionalPerformance = new TerritoryAnalyticsRegionalPerformanceResponseDto
                {
                    Skip = skip,
                    Take = take
                }
            };
        }

        var selectedRegionIds = await ResolveSelectedRegionIdsAsync(
            activeTerritory.Id,
            query.RegionIds,
            cancellationToken);

        var regions = await _db.Regions
            .AsNoTracking()
            .Where(region => selectedRegionIds.Contains(region.Id))
            .OrderBy(region => region.Name)
            .Select(region => new RegionProjection(region.Id, region.Name))
            .ToListAsync(cancellationToken);

        var current = await BuildMetricSetAsync(
            selectedRegionIds,
            status,
            period.StartDate,
            period.EndDate,
            cancellationToken);

        var previous = await BuildMetricSetAsync(
            selectedRegionIds,
            status,
            period.PreviousStartDate,
            period.StartDate,
            cancellationToken);

        var regionManagers = await LoadRegionManagersAsync(selectedRegionIds, cancellationToken);
        var contentCounts = BuildContentCounts(current.Entities);
        var currentRegionMetrics = BuildRegionMetrics(regions, current, contentCounts);
        var previousRegionMetrics = BuildRegionMetrics(regions, previous, BuildContentCounts(previous.Entities));
        var healthScores = await _healthScores.GetRegionHealthScoresAsync(
            selectedRegionIds,
            healthPeriod,
            cancellationToken);
        var sortedPerformance = SortRegionalPerformance(
            BuildRegionalPerformanceItems(regions, currentRegionMetrics, regionManagers, contentCounts, healthScores),
            query.Sort);

        return new TerritoryAnalyticsOverviewDto
        {
            ActiveTerritory = activeTerritory,
            Filters = BuildFilters(period, status, selectedRegionIds),
            Totals = BuildTotals(current),
            Comparison = BuildComparison(current, previous),
            TopRegion = BuildTopRegion(currentRegionMetrics, healthScores),
            FastestGrowing = BuildFastestGrowing(currentRegionMetrics, previousRegionMetrics),
            TopEntities = BuildTopEntities(current.EntityMetrics),
            PageViewsOverTime = BuildPageViewsOverTime(period, regions, current.ViewEvents, currentRegionMetrics),
            TopDestinations = BuildTopContent(current.EntityMetrics, AnalyticsEntityTypes.Destination),
            TopLocations = BuildTopContent(current.EntityMetrics, AnalyticsEntityTypes.Location),
            TopPlaces = BuildTopContent(current.EntityMetrics, AnalyticsEntityTypes.Place),
            TopEvents = BuildTopEvents(current.EntityMetrics),
            ContentQualityByRegion = BuildContentQuality(regions, currentRegionMetrics),
            RegionalPerformance = new TerritoryAnalyticsRegionalPerformanceResponseDto
            {
                Items = sortedPerformance.Skip(skip).Take(take).ToList(),
                TotalCount = sortedPerformance.Count,
                Skip = skip,
                Take = take,
                HasMore = skip + take < sortedPerformance.Count
            }
        };
    }

    public async Task<string> ExportCsvAsync(
        int userId,
        TerritoryAnalyticsOverviewQuery query,
        CancellationToken cancellationToken)
    {
        query ??= new TerritoryAnalyticsOverviewQuery();
        var exportQuery = new TerritoryAnalyticsOverviewQuery
        {
            TerritoryId = query.TerritoryId,
            Period = query.Period,
            RegionIds = query.RegionIds,
            Status = query.Status,
            Skip = 0,
            Take = MaxTake,
            Sort = query.Sort
        };

        var overview = await GetOverviewAsync(userId, exportQuery, cancellationToken);
        var csv = new StringBuilder();

        AppendCsvRow(csv, "Territory Analytics Export");
        AppendCsvRow(csv, "Territory", overview.ActiveTerritory?.DisplayName ?? overview.ActiveTerritory?.Name ?? string.Empty);
        AppendCsvRow(csv, "Period", overview.Filters.Period);
        AppendCsvRow(csv, "Start date", overview.Filters.StartDate.ToString("O", CultureInfo.InvariantCulture));
        AppendCsvRow(csv, "End date", overview.Filters.EndDate.ToString("O", CultureInfo.InvariantCulture));
        AppendCsvRow(csv, "Status", overview.Filters.Status);
        AppendCsvRow(csv, "Region ids", string.Join(";", overview.Filters.SelectedRegionIds));
        AppendCsvRow(csv);

        AppendCsvRow(csv, "Totals");
        AppendCsvRow(csv, "Total Page Views", "Unique Visitors", "External Link Clicks", "Total Saves", "Total Reviews", "Average Rating");
        AppendCsvRow(
            csv,
            overview.Totals.TotalPageViews,
            overview.Totals.UniqueVisitors,
            overview.Totals.ExternalLinkClicks,
            overview.Totals.TotalSaves,
            overview.Totals.TotalReviews,
            overview.Totals.AverageRating);
        AppendCsvRow(csv);

        AppendCsvRow(csv, "Regional Performance");
        AppendCsvRow(
            csv,
            "Region",
            "Page Views",
            "Visitors",
            "Saves",
            "Average Rating",
            "Destinations",
            "Locations",
            "Places",
            "Events",
            "Status",
            "Health Score",
            "Manager Status",
            "Managers");
        foreach (var row in overview.RegionalPerformance.Items)
        {
            AppendCsvRow(
                csv,
                row.RegionName,
                row.PageViews,
                row.Visitors,
                row.Saves,
                row.AverageRating,
                row.DestinationsCount,
                row.LocationsCount,
                row.PlacesCount,
                row.EventsCount,
                row.StatusLabel,
                row.HealthScore,
                row.ManagerStatus,
                string.Join("; ", row.ManagerNames));
        }
        AppendCsvRow(csv);

        AppendTopContentSection(csv, "Top Destinations", overview.TopDestinations);
        AppendTopContentSection(csv, "Top Locations", overview.TopLocations);
        AppendTopContentSection(csv, "Top Places", overview.TopPlaces);
        AppendTopContentSection(csv, "Top Events", overview.TopEvents);

        AppendCsvRow(csv, "Content Quality By Region");
        AppendCsvRow(csv, "Region", "Total Reviews", "Average Rating");
        foreach (var row in overview.ContentQualityByRegion)
        {
            AppendCsvRow(csv, row.RegionName, row.TotalReviews, row.AverageRating);
        }

        return csv.ToString();
    }

    private async Task<TerritoryAssignedTerritoryDto?> GetAuthorizedTerritoryAsync(
        int userId,
        int? territoryId,
        CancellationToken cancellationToken)
    {
        await EnsureTerritoryManagerRoleAsync(userId, cancellationToken);

        var assignedTerritories = await _db.TerritoryManagerTerritories
            .AsNoTracking()
            .Where(assignment => assignment.UserId == userId)
            .OrderBy(assignment => assignment.Territory.Name)
            .ThenBy(assignment => assignment.TerritoryId)
            .Select(assignment => new TerritoryAssignedTerritoryDto
            {
                Id = assignment.TerritoryId,
                Name = assignment.Territory.Name,
                DisplayName = assignment.Territory.Name,
                StatusLabel = "Assigned"
            })
            .ToListAsync(cancellationToken);

        if (assignedTerritories.Count == 0)
        {
            if (territoryId.HasValue)
                throw new UnauthorizedAccessException("Territory is not assigned to the current territory manager.");

            return null;
        }

        var activeTerritory = territoryId.HasValue
            ? assignedTerritories.SingleOrDefault(territory => territory.Id == territoryId.Value)
            : assignedTerritories[0];

        return activeTerritory
            ?? throw new UnauthorizedAccessException("Territory is not assigned to the current territory manager.");
    }

    private async Task EnsureTerritoryManagerRoleAsync(int userId, CancellationToken cancellationToken)
    {
        var roles = await _db.UserRoles
            .AsNoTracking()
            .Where(userRole => userRole.UserId == userId)
            .Select(userRole => userRole.Role.Name)
            .ToListAsync(cancellationToken);

        var normalizedRoles = roles.Select(RoleNames.Normalize).ToList();
        if (!normalizedRoles.Contains(RoleNames.TerritoryManager) &&
            !normalizedRoles.Contains(RoleNames.TerritoryAssociate))
        {
            throw new UnauthorizedAccessException("Territory administrator role is required.");
        }
    }

    private async Task<List<int>> ResolveSelectedRegionIdsAsync(
        int territoryId,
        IReadOnlyCollection<int>? requestedRegionIds,
        CancellationToken cancellationToken)
    {
        var territoryRegionIds = await _db.Regions
            .AsNoTracking()
            .Where(region => region.TerritoryId == territoryId)
            .OrderBy(region => region.Name)
            .Select(region => region.Id)
            .ToListAsync(cancellationToken);

        if (requestedRegionIds is null || requestedRegionIds.Count == 0)
            return territoryRegionIds;

        var normalized = requestedRegionIds.Distinct().ToList();
        if (normalized.Any(id => id <= 0))
            throw new InvalidOperationException("regionIds must contain positive values.");

        var invalid = normalized.Except(territoryRegionIds).Any();
        if (invalid)
            throw new UnauthorizedAccessException("One or more selected regions are not in the assigned territory.");

        return normalized;
    }

    private async Task<MetricSet> BuildMetricSetAsync(
        IReadOnlyCollection<int> regionIds,
        string status,
        DateTime fromUtc,
        DateTime toUtc,
        CancellationToken cancellationToken)
    {
        if (regionIds.Count == 0)
            return MetricSet.Empty();

        var entities = await LoadEntitiesAsync(regionIds, status, cancellationToken);
        var entityMetrics = entities.Select(entity => new EntityMetric(entity)).ToList();
        var analyticsEvents = await LoadAnalyticsEventsAsync(entities, fromUtc, toUtc, cancellationToken);
        var viewEvents = analyticsEvents.Where(row => ViewEventTypes.Contains(row.EventType)).ToList();
        var externalEvents = analyticsEvents.Where(row => ExternalClickEventTypes.Contains(row.EventType)).ToList();

        ApplyAnalyticsEventMetrics(entityMetrics, analyticsEvents);
        await ApplySaveMetricsAsync(entityMetrics, entities, fromUtc, toUtc, cancellationToken);
        await ApplyReviewMetricsAsync(entityMetrics, entities, fromUtc, toUtc, cancellationToken);

        return new MetricSet(entities, entityMetrics, viewEvents, externalEvents);
    }

    private async Task<List<EntityProjection>> LoadEntitiesAsync(
        IReadOnlyCollection<int> regionIds,
        string status,
        CancellationToken cancellationToken)
    {
        var destinationQuery = ApplyStatusFilter(
            _db.Destinations.AsNoTracking().Where(destination => regionIds.Contains(destination.RegionId)),
            status);

        var destinations = await destinationQuery
            .Select(destination => new EntityProjection(
                AnalyticsEntityTypes.Destination,
                destination.Id,
                destination.RegionId,
                destination.Region.Name,
                destination.Name,
                destination.Status,
                destination.Images
                    .Where(image => image.TargetType == Domain.Entities.Core.EntityImageTargetType.Destination)
                    .OrderByDescending(image => image.IsMain)
                    .ThenBy(image => image.SortOrder)
                    .ThenBy(image => image.CreatedAt)
                    .ThenBy(image => image.Id)
                    .Select(image => image.Url)
                    .FirstOrDefault(),
                "/destinations/detail/" + destination.Id,
                "/explore?destinationId=" + destination.Id,
                null))
            .ToListAsync(cancellationToken);

        var locationQuery = ApplyStatusFilter(
            _db.Locations.AsNoTracking().Where(location => regionIds.Contains(location.Destination.RegionId)),
            status);

        var locations = await locationQuery
            .Select(location => new EntityProjection(
                AnalyticsEntityTypes.Location,
                location.Id,
                location.Destination.RegionId,
                location.Destination.Region.Name,
                location.Name,
                location.Status,
                location.Images
                    .Where(image => image.TargetType == Domain.Entities.Core.EntityImageTargetType.Location)
                    .OrderByDescending(image => image.IsMain)
                    .ThenBy(image => image.SortOrder)
                    .ThenBy(image => image.CreatedAt)
                    .ThenBy(image => image.Id)
                    .Select(image => image.Url)
                    .FirstOrDefault(),
                "/locations/detail/" + location.Id,
                null,
                null))
            .ToListAsync(cancellationToken);

        var placeQuery = ApplyStatusFilter(
            _db.Places.AsNoTracking().Where(place => regionIds.Contains(place.Location.Destination.RegionId)),
            status);

        var places = await placeQuery
            .Select(place => new EntityProjection(
                AnalyticsEntityTypes.Place,
                place.Id,
                place.Location.Destination.RegionId,
                place.Location.Destination.Region.Name,
                place.Name,
                place.Status,
                place.Images
                    .Where(image => image.TargetType == Domain.Entities.Core.EntityImageTargetType.Place)
                    .OrderByDescending(image => image.IsMain)
                    .ThenBy(image => image.SortOrder)
                    .ThenBy(image => image.CreatedAt)
                    .ThenBy(image => image.Id)
                    .Select(image => image.Url)
                    .FirstOrDefault(),
                "/places/detail/" + place.Id,
                place.Stay == null ? "/places/" + place.Id : "/stays/" + place.Id,
                null))
            .ToListAsync(cancellationToken);

        var eventQuery = ApplyStatusFilter(
            _db.Events.AsNoTracking().Where(evt => regionIds.Contains(evt.Destination.RegionId)),
            status);

        var events = await eventQuery
            .Select(evt => new EntityProjection(
                AnalyticsEntityTypes.Event,
                evt.Id,
                evt.Destination.RegionId,
                evt.Destination.Region.Name,
                evt.Name,
                evt.Status,
                evt.Images
                    .Where(image => image.TargetType == Domain.Entities.Core.EntityImageTargetType.Event)
                    .OrderByDescending(image => image.IsMain)
                    .ThenBy(image => image.SortOrder)
                    .ThenBy(image => image.CreatedAt)
                    .ThenBy(image => image.Id)
                    .Select(image => image.Url)
                    .FirstOrDefault(),
                "/events/detail/" + evt.Id,
                "/events/" + evt.Id,
                evt.StartDate))
            .ToListAsync(cancellationToken);

        return destinations
            .Concat(locations)
            .Concat(places)
            .Concat(events)
            .ToList();
    }

    private async Task<List<AnalyticsMetricEventRow>> LoadAnalyticsEventsAsync(
        IReadOnlyList<EntityProjection> entities,
        DateTime fromUtc,
        DateTime toUtc,
        CancellationToken cancellationToken)
    {
        if (entities.Count == 0)
            return new List<AnalyticsMetricEventRow>();

        var destinationIds = EntityIds(entities, AnalyticsEntityTypes.Destination);
        var locationIds = EntityIds(entities, AnalyticsEntityTypes.Location);
        var placeIds = EntityIds(entities, AnalyticsEntityTypes.Place);
        var eventIds = EntityIds(entities, AnalyticsEntityTypes.Event);
        var regionByDestinationId = entities
            .Where(entity => entity.EntityType == AnalyticsEntityTypes.Destination)
            .ToDictionary(entity => entity.EntityId, entity => entity.RegionId);
        var entityByKey = entities.ToDictionary(entity => new EntityKey(entity.EntityType, entity.EntityId));

        var eventTypes = ViewEventTypes.Concat(ExternalClickEventTypes).ToArray();
        var rawEvents = await _db.AnalyticsEvents
            .AsNoTracking()
            .Where(evt => evt.OccurredAt >= fromUtc && evt.OccurredAt < toUtc)
            .Where(evt => eventTypes.Contains(evt.EventType))
            .Where(evt =>
                (evt.EntityId.HasValue && evt.EntityType == AnalyticsEntityTypes.Destination && destinationIds.Contains(evt.EntityId.Value)) ||
                (evt.EntityId.HasValue && evt.EntityType == AnalyticsEntityTypes.Location && locationIds.Contains(evt.EntityId.Value)) ||
                (evt.EntityId.HasValue && evt.EntityType == AnalyticsEntityTypes.Place && placeIds.Contains(evt.EntityId.Value)) ||
                (evt.EntityId.HasValue && evt.EntityType == AnalyticsEntityTypes.Hotel && placeIds.Contains(evt.EntityId.Value)) ||
                (evt.EntityId.HasValue && evt.EntityType == AnalyticsEntityTypes.Event && eventIds.Contains(evt.EntityId.Value)) ||
                (evt.DestinationId.HasValue && destinationIds.Contains(evt.DestinationId.Value)))
            .Select(evt => new
            {
                evt.Id,
                evt.EventType,
                evt.EntityType,
                evt.EntityId,
                evt.DestinationId,
                evt.UserId,
                evt.AnonymousSessionId,
                evt.OccurredAt
            })
            .ToListAsync(cancellationToken);

        var rows = new List<AnalyticsMetricEventRow>(rawEvents.Count);
        foreach (var evt in rawEvents.OrderBy(evt => evt.Id))
        {
            var entityType = NormalizeAnalyticsEntityType(evt.EntityType);
            var entityId = evt.EntityId;
            var regionId = 0;

            if (entityId.HasValue &&
                entityType is not null &&
                entityByKey.TryGetValue(new EntityKey(entityType, entityId.Value), out var entity))
            {
                regionId = entity.RegionId;
            }
            else if (evt.DestinationId.HasValue &&
                     regionByDestinationId.TryGetValue(evt.DestinationId.Value, out var destinationRegionId))
            {
                entityType = AnalyticsEntityTypes.Destination;
                entityId = evt.DestinationId.Value;
                regionId = destinationRegionId;
            }

            if (entityType is null || !entityId.HasValue || regionId <= 0)
                continue;

            rows.Add(new AnalyticsMetricEventRow(
                evt.Id,
                evt.EventType,
                entityType,
                entityId.Value,
                regionId,
                evt.UserId,
                evt.AnonymousSessionId,
                evt.OccurredAt));
        }

        return rows
            .GroupBy(row => row.EventId)
            .Select(group => group.First())
            .ToList();
    }

    private static void ApplyAnalyticsEventMetrics(
        IReadOnlyList<EntityMetric> rows,
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
        IReadOnlyList<EntityMetric> rows,
        IReadOnlyList<EntityProjection> entities,
        DateTime fromUtc,
        DateTime toUtc,
        CancellationToken cancellationToken)
    {
        var byKey = rows.ToDictionary(row => new EntityKey(row.EntityType, row.EntityId));
        var destinationIds = EntityIds(entities, AnalyticsEntityTypes.Destination);
        var locationIds = EntityIds(entities, AnalyticsEntityTypes.Location);
        var placeIds = EntityIds(entities, AnalyticsEntityTypes.Place);
        var eventIds = EntityIds(entities, AnalyticsEntityTypes.Event);

        var favoriteRows = await _db.Favorites
            .AsNoTracking()
            .Where(favorite => favorite.CreatedAt >= fromUtc && favorite.CreatedAt < toUtc)
            .Where(favorite =>
                (favorite.EntityType == AnalyticsEntityTypes.Destination && destinationIds.Contains(favorite.EntityId)) ||
                (favorite.EntityType == AnalyticsEntityTypes.Location && locationIds.Contains(favorite.EntityId)) ||
                (favorite.EntityType == AnalyticsEntityTypes.Place && placeIds.Contains(favorite.EntityId)) ||
                (favorite.EntityType == AnalyticsEntityTypes.Hotel && placeIds.Contains(favorite.EntityId)) ||
                (favorite.EntityType == AnalyticsEntityTypes.Event && eventIds.Contains(favorite.EntityId)))
            .Select(favorite => new
            {
                EntityType = favorite.EntityType == AnalyticsEntityTypes.Hotel ? AnalyticsEntityTypes.Place : favorite.EntityType,
                favorite.EntityId,
                favorite.UserId
            })
            .Distinct()
            .ToListAsync(cancellationToken);

        foreach (var group in favoriteRows.GroupBy(row => new EntityKey(row.EntityType, row.EntityId)))
        {
            if (byKey.TryGetValue(group.Key, out var metric))
            {
                metric.Saves += group.Count();
            }
        }

        var collectionRows = await _db.CollectionItems
            .AsNoTracking()
            .Where(item => item.CreatedAt >= fromUtc && item.CreatedAt < toUtc)
            .Where(item =>
                (item.DestinationId.HasValue && destinationIds.Contains(item.DestinationId.Value)) ||
                (item.LocationId.HasValue && locationIds.Contains(item.LocationId.Value)) ||
                (item.PlaceId.HasValue && placeIds.Contains(item.PlaceId.Value)) ||
                (item.EventId.HasValue && eventIds.Contains(item.EventId.Value)))
            .Select(item => new
            {
                item.DestinationId,
                item.LocationId,
                item.PlaceId,
                item.EventId,
                item.Collection.UserId
            })
            .Distinct()
            .ToListAsync(cancellationToken);

        foreach (var item in collectionRows)
        {
            EntityKey? key = null;
            if (item.DestinationId.HasValue)
                key = new EntityKey(AnalyticsEntityTypes.Destination, item.DestinationId.Value);
            else if (item.LocationId.HasValue)
                key = new EntityKey(AnalyticsEntityTypes.Location, item.LocationId.Value);
            else if (item.PlaceId.HasValue)
                key = new EntityKey(AnalyticsEntityTypes.Place, item.PlaceId.Value);
            else if (item.EventId.HasValue)
                key = new EntityKey(AnalyticsEntityTypes.Event, item.EventId.Value);

            if (key.HasValue && byKey.TryGetValue(key.Value, out var metric))
            {
                metric.Saves += 1;
            }
        }
    }

    private async Task ApplyReviewMetricsAsync(
        IReadOnlyList<EntityMetric> rows,
        IReadOnlyList<EntityProjection> entities,
        DateTime fromUtc,
        DateTime toUtc,
        CancellationToken cancellationToken)
    {
        var byKey = rows.ToDictionary(row => new EntityKey(row.EntityType, row.EntityId));
        var destinationIds = EntityIds(entities, AnalyticsEntityTypes.Destination);
        var locationIds = EntityIds(entities, AnalyticsEntityTypes.Location);
        var placeIds = EntityIds(entities, AnalyticsEntityTypes.Place);
        var eventIds = EntityIds(entities, AnalyticsEntityTypes.Event);

        var destinationReviews = await _db.DestinationReviews
            .AsNoTracking()
            .Where(review => review.CreatedAt >= fromUtc && review.CreatedAt < toUtc)
            .Where(review => destinationIds.Contains(review.DestinationId))
            .GroupBy(review => review.DestinationId)
            .Select(group => new { EntityId = group.Key, Count = group.Count(), Average = group.Average(review => review.Rating) })
            .ToListAsync(cancellationToken);

        foreach (var row in destinationReviews)
        {
            ApplyReviewAggregate(byKey, AnalyticsEntityTypes.Destination, row.EntityId, row.Count, row.Average);
        }

        var locationReviews = await _db.LocationReviews
            .AsNoTracking()
            .Where(review => review.CreatedAt >= fromUtc && review.CreatedAt < toUtc)
            .Where(review => locationIds.Contains(review.LocationId))
            .GroupBy(review => review.LocationId)
            .Select(group => new { EntityId = group.Key, Count = group.Count(), Average = group.Average(review => review.Rating) })
            .ToListAsync(cancellationToken);

        foreach (var row in locationReviews)
        {
            ApplyReviewAggregate(byKey, AnalyticsEntityTypes.Location, row.EntityId, row.Count, row.Average);
        }

        var placeReviews = await _db.PlaceReviews
            .AsNoTracking()
            .Where(review => review.CreatedAt >= fromUtc && review.CreatedAt < toUtc)
            .Where(review => placeIds.Contains(review.PlaceId))
            .GroupBy(review => review.PlaceId)
            .Select(group => new { EntityId = group.Key, Count = group.Count(), Average = group.Average(review => review.Rating) })
            .ToListAsync(cancellationToken);

        foreach (var row in placeReviews)
        {
            ApplyReviewAggregate(byKey, AnalyticsEntityTypes.Place, row.EntityId, row.Count, row.Average);
        }

        var eventReviews = await _db.EventReviews
            .AsNoTracking()
            .Where(review => review.CreatedAt >= fromUtc && review.CreatedAt < toUtc)
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
        IReadOnlyDictionary<EntityKey, EntityMetric> rows,
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

    private async Task<Dictionary<int, List<string>>> LoadRegionManagersAsync(
        IReadOnlyCollection<int> regionIds,
        CancellationToken cancellationToken)
    {
        var rows = await _db.RegionalManagerRegions
            .AsNoTracking()
            .Where(assignment => regionIds.Contains(assignment.RegionId))
            .OrderBy(assignment => assignment.User.DisplayName)
            .ThenBy(assignment => assignment.User.Email)
            .Select(assignment => new
            {
                assignment.RegionId,
                assignment.User.DisplayName,
                assignment.User.FirstName,
                assignment.User.LastName,
                assignment.User.Email
            })
            .ToListAsync(cancellationToken);

        return rows
            .GroupBy(row => row.RegionId)
            .ToDictionary(
                group => group.Key,
                group => group
                    .Select(row => BuildDisplayName(row.DisplayName, row.FirstName, row.LastName, row.Email))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList());
    }

    private static Dictionary<int, ContentCounts> BuildContentCounts(IEnumerable<EntityProjection> entities) =>
        entities
            .GroupBy(entity => entity.RegionId)
            .ToDictionary(
                group => group.Key,
                group => new ContentCounts(
                    group.Count(entity => entity.EntityType == AnalyticsEntityTypes.Destination),
                    group.Count(entity => entity.EntityType == AnalyticsEntityTypes.Location),
                    group.Count(entity => entity.EntityType == AnalyticsEntityTypes.Place),
                    group.Count(entity => entity.EntityType == AnalyticsEntityTypes.Event)));

    private static Dictionary<int, RegionMetric> BuildRegionMetrics(
        IReadOnlyList<RegionProjection> regions,
        MetricSet metricSet,
        IReadOnlyDictionary<int, ContentCounts> contentCounts)
    {
        var viewEventsByRegion = metricSet.ViewEvents
            .GroupBy(row => row.RegionId)
            .ToDictionary(group => group.Key, group => group.ToList());

        var externalClicksByRegion = metricSet.ExternalEvents
            .GroupBy(row => row.RegionId)
            .ToDictionary(group => group.Key, group => group.Count());

        var entityMetricsByRegion = metricSet.EntityMetrics
            .GroupBy(row => row.RegionId)
            .ToDictionary(group => group.Key, group => group.ToList());

        return regions.ToDictionary(
            region => region.RegionId,
            region =>
            {
                var viewEvents = viewEventsByRegion.GetValueOrDefault(region.RegionId) ?? new List<AnalyticsMetricEventRow>();
                var entityMetrics = entityMetricsByRegion.GetValueOrDefault(region.RegionId) ?? new List<EntityMetric>();
                var reviewCount = entityMetrics.Sum(row => row.ReviewCount);
                var averageRating = WeightedAverageRating(entityMetrics);
                var pageViews = viewEvents.Count;
                var uniqueVisitors = CountUniqueVisitors(viewEvents);
                var saves = entityMetrics.Sum(row => row.Saves);
                var externalClicks = externalClicksByRegion.GetValueOrDefault(region.RegionId);
                var counts = contentCounts.GetValueOrDefault(region.RegionId, new ContentCounts(0, 0, 0, 0));

                return new RegionMetric(
                    region.RegionId,
                    region.RegionName,
                    pageViews,
                    uniqueVisitors,
                    externalClicks,
                    saves,
                    reviewCount,
                    averageRating,
                    counts);
            });
    }

    private static List<TerritoryAnalyticsRegionalPerformanceItemDto> BuildRegionalPerformanceItems(
        IReadOnlyList<RegionProjection> regions,
        IReadOnlyDictionary<int, RegionMetric> regionMetrics,
        IReadOnlyDictionary<int, List<string>> managersByRegion,
        IReadOnlyDictionary<int, ContentCounts> contentCounts,
        IReadOnlyDictionary<int, TerritoryHealthDto> healthScores)
    {
        return regions.Select(region =>
        {
            var metric = regionMetrics.GetValueOrDefault(region.RegionId)
                ?? RegionMetric.Empty(region.RegionId, region.RegionName);
            var managers = managersByRegion.GetValueOrDefault(region.RegionId) ?? new List<string>();
            var counts = contentCounts.GetValueOrDefault(region.RegionId, new ContentCounts(0, 0, 0, 0));
            var health = healthScores.GetValueOrDefault(region.RegionId);
            var healthScore = health?.Score ?? 0;

            return new TerritoryAnalyticsRegionalPerformanceItemDto
            {
                RegionId = region.RegionId,
                RegionName = region.RegionName,
                PageViews = metric.PageViews,
                Visitors = metric.UniqueVisitors,
                Saves = metric.Saves,
                AverageRating = metric.AverageRating,
                DestinationsCount = counts.Destinations,
                LocationsCount = counts.Locations,
                PlacesCount = counts.Places,
                EventsCount = counts.Events,
                StatusLabel = counts.Total > 0 ? "Active" : "Inactive",
                HealthScore = healthScore,
                Health = health,
                QualityScore = healthScore,
                ManagerStatus = managers.Count > 0 ? "Assigned" : "Unassigned",
                ManagerNames = managers
            };
        }).ToList();
    }

    private static List<TerritoryAnalyticsRegionalPerformanceItemDto> SortRegionalPerformance(
        IReadOnlyList<TerritoryAnalyticsRegionalPerformanceItemDto> rows,
        string? sort)
    {
        var normalized = NormalizeSort(sort);
        IOrderedEnumerable<TerritoryAnalyticsRegionalPerformanceItemDto> ordered = normalized switch
        {
            "views_asc" => rows.OrderBy(row => row.PageViews).ThenBy(row => row.RegionName, StringComparer.OrdinalIgnoreCase),
            "visitors_desc" => rows.OrderByDescending(row => row.Visitors).ThenBy(row => row.RegionName, StringComparer.OrdinalIgnoreCase),
            "visitors_asc" => rows.OrderBy(row => row.Visitors).ThenBy(row => row.RegionName, StringComparer.OrdinalIgnoreCase),
            "saves_desc" => rows.OrderByDescending(row => row.Saves).ThenBy(row => row.RegionName, StringComparer.OrdinalIgnoreCase),
            "saves_asc" => rows.OrderBy(row => row.Saves).ThenBy(row => row.RegionName, StringComparer.OrdinalIgnoreCase),
            "rating_desc" => rows.OrderByDescending(row => row.AverageRating).ThenBy(row => row.RegionName, StringComparer.OrdinalIgnoreCase),
            "rating_asc" => rows.OrderBy(row => row.AverageRating).ThenBy(row => row.RegionName, StringComparer.OrdinalIgnoreCase),
            "health_desc" => rows.OrderByDescending(row => row.HealthScore).ThenBy(row => row.RegionName, StringComparer.OrdinalIgnoreCase),
            "health_asc" => rows.OrderBy(row => row.HealthScore).ThenBy(row => row.RegionName, StringComparer.OrdinalIgnoreCase),
            "name_asc" => rows.OrderBy(row => row.RegionName, StringComparer.OrdinalIgnoreCase),
            "name_desc" => rows.OrderByDescending(row => row.RegionName, StringComparer.OrdinalIgnoreCase),
            _ => rows.OrderByDescending(row => row.PageViews).ThenBy(row => row.RegionName, StringComparer.OrdinalIgnoreCase)
        };

        return ordered.ToList();
    }

    private static TerritoryAnalyticsTotalsDto BuildTotals(MetricSet metricSet)
    {
        var reviews = metricSet.EntityMetrics.Sum(row => row.ReviewCount);

        return new TerritoryAnalyticsTotalsDto
        {
            TotalPageViews = metricSet.ViewEvents.Count,
            UniqueVisitors = CountUniqueVisitors(metricSet.ViewEvents),
            ExternalLinkClicks = metricSet.ExternalEvents.Count,
            TotalSaves = metricSet.EntityMetrics.Sum(row => row.Saves),
            TotalReviews = reviews,
            AverageRating = WeightedAverageRating(metricSet.EntityMetrics)
        };
    }

    private static TerritoryAnalyticsComparisonDto BuildComparison(MetricSet current, MetricSet previous)
    {
        var currentTotals = BuildTotals(current);
        var previousTotals = BuildTotals(previous);

        return new TerritoryAnalyticsComparisonDto
        {
            PageViewsChangePercent = PercentChange(currentTotals.TotalPageViews, previousTotals.TotalPageViews),
            UniqueVisitorsChangePercent = PercentChange(currentTotals.UniqueVisitors, previousTotals.UniqueVisitors),
            ExternalClicksChangePercent = PercentChange(currentTotals.ExternalLinkClicks, previousTotals.ExternalLinkClicks),
            SavesChangePercent = PercentChange(currentTotals.TotalSaves, previousTotals.TotalSaves),
            ReviewsChangePercent = PercentChange(currentTotals.TotalReviews, previousTotals.TotalReviews),
            AverageRatingChange = Math.Round(
                currentTotals.AverageRating - previousTotals.AverageRating,
                2,
                MidpointRounding.AwayFromZero)
        };
    }

    private static TerritoryAnalyticsRegionHighlightDto? BuildTopRegion(
        IReadOnlyDictionary<int, RegionMetric> regionMetrics,
        IReadOnlyDictionary<int, TerritoryHealthDto> healthScores)
    {
        var top = regionMetrics.Values
            .Select(row => new
            {
                Metric = row,
                Health = healthScores.GetValueOrDefault(row.RegionId)
            })
            .Where(row => row.Health?.Score.HasValue == true)
            .OrderByDescending(row => row.Health!.Score!.Value)
            .ThenByDescending(row => row.Metric.PageViews)
            .ThenBy(row => row.Metric.RegionName, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();

        if (top is null)
            return null;

        var score = top.Health!.Score.GetValueOrDefault();
        return new TerritoryAnalyticsRegionHighlightDto
        {
            RegionId = top.Metric.RegionId,
            RegionName = top.Metric.RegionName,
            Score = score,
            HealthScore = score,
            StatusLabel = top.Health.StatusLabel,
            PeriodLabel = top.Health.PeriodLabel,
            Description = "Highest region health score in the selected period based on content coverage, completeness, engagement, moderation, and freshness.",
            Visits = top.Metric.PageViews,
            UniqueVisitors = top.Metric.UniqueVisitors
        };
    }

    private static TerritoryAnalyticsFastestGrowingDto? BuildFastestGrowing(
        IReadOnlyDictionary<int, RegionMetric> current,
        IReadOnlyDictionary<int, RegionMetric> previous)
    {
        var growth = current.Values
            .Select(row =>
            {
                var previousViews = previous.GetValueOrDefault(row.RegionId)?.PageViews ?? 0;
                return new
                {
                    Region = row,
                    PreviousViews = previousViews,
                    GrowthPercent = previousViews > 0
                        ? PercentChange(row.PageViews, previousViews)
                        : 0
                };
            })
            .Where(row => row.PreviousViews > 0 && row.GrowthPercent > 0)
            .OrderByDescending(row => row.GrowthPercent)
            .ThenByDescending(row => row.Region.PageViews)
            .FirstOrDefault();

        return growth is null
            ? null
            : new TerritoryAnalyticsFastestGrowingDto
            {
                RegionId = growth.Region.RegionId,
                RegionName = growth.Region.RegionName,
                GrowthPercent = growth.GrowthPercent,
                MetricLabel = "page view growth"
            };
    }

    private static List<TerritoryAnalyticsTopEntityDto> BuildTopEntities(
        IReadOnlyList<EntityMetric> rows) =>
        rows
            .Where(row => row.PageViews > 0 || row.Saves > 0 || row.ExternalClicks > 0 || row.ReviewCount > 0)
            .OrderByDescending(row => row.PageViews)
            .ThenByDescending(row => row.Saves)
            .ThenByDescending(row => row.ExternalClicks)
            .ThenBy(row => row.Name, StringComparer.OrdinalIgnoreCase)
            .Take(5)
            .Select((row, index) => new TerritoryAnalyticsTopEntityDto
            {
                EntityType = row.EntityType,
                EntityId = row.EntityId,
                Name = row.Name,
                RegionName = row.RegionName,
                Rank = index + 1,
                MetricValue = row.PageViews,
                MetricLabel = "views",
                ImageUrl = row.ImageUrl,
                DetailRoute = row.DetailRoute,
                PublicRoute = row.PublicRoute
            })
            .ToList();

    private static List<TerritoryAnalyticsTopContentDto> BuildTopContent(
        IReadOnlyList<EntityMetric> rows,
        string entityType) =>
        rows
            .Where(row => row.EntityType == entityType && row.PageViews > 0)
            .OrderByDescending(row => row.PageViews)
            .ThenBy(row => row.Name, StringComparer.OrdinalIgnoreCase)
            .Take(5)
            .Select(row => new TerritoryAnalyticsTopContentDto
            {
                Id = row.EntityId,
                Name = row.Name,
                RegionName = row.RegionName,
                Views = row.PageViews,
                AverageRating = row.AverageRating > 0 ? row.AverageRating : null,
                ImageUrl = row.ImageUrl,
                DetailRoute = row.DetailRoute
            })
            .ToList();

    private static List<TerritoryAnalyticsTopEventDto> BuildTopEvents(
        IReadOnlyList<EntityMetric> rows) =>
        rows
            .Where(row => row.EntityType == AnalyticsEntityTypes.Event && (row.PageViews > 0 || row.Saves > 0))
            .OrderByDescending(row => row.PageViews)
            .ThenByDescending(row => row.Saves)
            .ThenBy(row => row.Name, StringComparer.OrdinalIgnoreCase)
            .Take(5)
            .Select(row => new TerritoryAnalyticsTopEventDto
            {
                Id = row.EntityId,
                Name = row.Name,
                RegionName = row.RegionName,
                Views = row.PageViews,
                AverageRating = row.AverageRating > 0 ? row.AverageRating : null,
                ImageUrl = row.ImageUrl,
                DetailRoute = row.DetailRoute,
                Saves = row.Saves,
                EventDate = row.EventDate
            })
            .ToList();

    private static TerritoryAnalyticsPageViewsOverTimeDto BuildPageViewsOverTime(
        PeriodWindow period,
        IReadOnlyList<RegionProjection> regions,
        IReadOnlyList<AnalyticsMetricEventRow> viewEvents,
        IReadOnlyDictionary<int, RegionMetric> regionMetrics)
    {
        var buckets = BuildBuckets(period);
        var selectedRegionIds = regionMetrics.Values
            .OrderByDescending(region => region.PageViews)
            .ThenBy(region => region.RegionName, StringComparer.OrdinalIgnoreCase)
            .Take(ChartSeriesTake)
            .Select(region => region.RegionId)
            .ToHashSet();

        if (regions.Count <= ChartSeriesTake)
        {
            selectedRegionIds = regions.Select(region => region.RegionId).ToHashSet();
        }

        var series = regions
            .Where(region => selectedRegionIds.Contains(region.RegionId))
            .Select(region => new TerritoryAnalyticsPageViewsSeriesDto
            {
                RegionId = region.RegionId,
                RegionName = region.RegionName,
                Values = buckets
                    .Select(bucket => viewEvents.Count(evt =>
                        evt.RegionId == region.RegionId &&
                        evt.OccurredAt >= bucket.Start &&
                        evt.OccurredAt < bucket.End))
                    .ToList()
            })
            .ToList();

        return new TerritoryAnalyticsPageViewsOverTimeDto
        {
            MonthLabels = buckets.Select(bucket => bucket.Label).ToList(),
            Series = series
        };
    }

    private static List<TerritoryAnalyticsContentQualityDto> BuildContentQuality(
        IReadOnlyList<RegionProjection> regions,
        IReadOnlyDictionary<int, RegionMetric> regionMetrics) =>
        regions.Select(region =>
        {
            var metric = regionMetrics.GetValueOrDefault(region.RegionId);
            return new TerritoryAnalyticsContentQualityDto
            {
                RegionId = region.RegionId,
                RegionName = region.RegionName,
                TotalReviews = metric?.ReviewCount ?? 0,
                AverageRating = metric?.AverageRating ?? 0
            };
        }).ToList();

    private static TerritoryAnalyticsFiltersDto BuildFilters(
        PeriodWindow period,
        string status,
        IEnumerable<int> selectedRegionIds) =>
        new()
        {
            Period = period.Key,
            StartDate = period.StartDate,
            EndDate = period.EndDate,
            SelectedRegionIds = selectedRegionIds.Distinct().Where(id => id > 0).ToList(),
            Status = status
        };

    private static double WeightedAverageRating(IEnumerable<EntityMetric> rows)
    {
        var ratedRows = rows.Where(row => row.ReviewCount > 0).ToList();
        if (ratedRows.Count == 0)
            return 0;

        var reviewCount = ratedRows.Sum(row => row.ReviewCount);
        if (reviewCount == 0)
            return 0;

        var weighted = ratedRows.Sum(row => row.AverageRating * row.ReviewCount) / reviewCount;
        return Math.Round(weighted, 2, MidpointRounding.AwayFromZero);
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

    private static Dictionary<Guid, int> BuildLinkedAnonymousSessions(IEnumerable<AnalyticsMetricEventRow> rows) =>
        rows
            .Where(row => row.UserId.HasValue && row.AnonymousSessionId.HasValue)
            .GroupBy(row => row.AnonymousSessionId!.Value)
            .Where(group => group.Select(row => row.UserId!.Value).Distinct().Count() == 1)
            .ToDictionary(
                group => group.Key,
                group => group.Select(row => row.UserId!.Value).First());

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

    private static PeriodWindow ResolvePeriod(string? value)
    {
        var key = NormalizeToken(value ?? "1m");
        var now = DateTime.UtcNow;
        var normalized = key switch
        {
            "" or "1m" or "30d" or "lastmonth" or "last30days" => "1m",
            "24h" or "last24h" or "last24hours" => "24h",
            "7d" or "last7days" => "7d",
            "3m" or "90d" or "last3months" or "last90days" => "3m",
            "6m" or "last6months" => "6m",
            "1y" or "12m" or "lastyear" or "last12months" => "1y",
            _ => throw new InvalidOperationException("Invalid period filter.")
        };

        var start = normalized switch
        {
            "24h" => now.AddHours(-24),
            "7d" => now.AddDays(-7),
            "1m" => now.AddMonths(-1),
            "3m" => now.AddMonths(-3),
            "6m" => now.AddMonths(-6),
            "1y" => now.AddYears(-1),
            _ => now.AddMonths(-6)
        };

        return new PeriodWindow(normalized, start, now);
    }

    private static string BuildPeriodLabel(string key) =>
        key switch
        {
            "24h" => "Last 24 hours",
            "7d" => "Last 7 days",
            "1m" => "Last 30 days",
            "3m" => "Last 90 days",
            "6m" => "Last 6 months",
            "1y" => "Last 12 months",
            _ => "Selected period"
        };

    private static List<TimeBucket> BuildBuckets(PeriodWindow period)
    {
        if (period.Key == "24h")
        {
            var first = new DateTime(period.EndDate.Year, period.EndDate.Month, period.EndDate.Day, period.EndDate.Hour, 0, 0, DateTimeKind.Utc)
                .AddHours(-23);

            return Enumerable.Range(0, 24)
                .Select(index =>
                {
                    var start = first.AddHours(index);
                    return new TimeBucket(start, start.AddHours(1), start.ToString("HH:00", CultureInfo.InvariantCulture));
                })
                .ToList();
        }

        if (period.Key is "7d" or "1m")
        {
            var count = period.Key == "7d" ? 7 : 30;
            var first = period.EndDate.Date.AddDays(-(count - 1));
            return Enumerable.Range(0, count)
                .Select(index =>
                {
                    var start = first.AddDays(index);
                    return new TimeBucket(start, start.AddDays(1), start.ToString("MMM d", CultureInfo.InvariantCulture));
                })
                .ToList();
        }

        var monthCount = period.Key == "3m" ? 3 : period.Key == "1y" ? 12 : 6;
        var firstMonth = new DateTime(period.EndDate.Year, period.EndDate.Month, 1, 0, 0, 0, DateTimeKind.Utc)
            .AddMonths(-(monthCount - 1));

        return Enumerable.Range(0, monthCount)
            .Select(index =>
            {
                var start = firstMonth.AddMonths(index);
                return new TimeBucket(start, start.AddMonths(1), start.ToString("MMM", CultureInfo.InvariantCulture));
            })
            .ToList();
    }

    private static string NormalizeStatusFilter(string? value)
    {
        var normalized = NormalizeToken(value ?? "active");
        return normalized switch
        {
            "" or "active" or "published" => "active",
            "inactive" or "draft" or "unpublished" => "inactive",
            "all" => "all",
            _ => throw new InvalidOperationException("Invalid status filter.")
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
            "rating" or "ratingdesc" or "avgratingdesc" => "rating_desc",
            "ratingasc" or "avgratingasc" => "rating_asc",
            "health" or "healthdesc" or "healthscoredesc" or "quality" or "qualitydesc" or "qualityscoredesc" => "health_desc",
            "healthasc" or "healthscoreasc" or "qualityasc" or "qualityscoreasc" => "health_asc",
            "name" or "nameasc" or "az" => "name_asc",
            "namedesc" or "za" => "name_desc",
            _ => throw new InvalidOperationException("Invalid sort filter.")
        };
    }

    private static string NormalizeToken(string value) =>
        new(value
            .Trim()
            .ToLowerInvariant()
            .Where(char.IsLetterOrDigit)
            .ToArray());

    private static string? NormalizeAnalyticsEntityType(string? value) =>
        value switch
        {
            AnalyticsEntityTypes.Destination => AnalyticsEntityTypes.Destination,
            AnalyticsEntityTypes.Location => AnalyticsEntityTypes.Location,
            AnalyticsEntityTypes.Place => AnalyticsEntityTypes.Place,
            AnalyticsEntityTypes.Hotel => AnalyticsEntityTypes.Place,
            AnalyticsEntityTypes.Event => AnalyticsEntityTypes.Event,
            _ => null
        };

    private static List<int> EntityIds(IEnumerable<EntityProjection> entities, string entityType) =>
        entities
            .Where(entity => entity.EntityType == entityType)
            .Select(entity => entity.EntityId)
            .Distinct()
            .ToList();

    private static IQueryable<T> ApplyStatusFilter<T>(IQueryable<T> query, string status)
        where T : class
    {
        return status switch
        {
            "active" => query.Where(entity => EF.Property<string>(entity, "Status") == PublishedStatus),
            "inactive" => query.Where(entity => EF.Property<string>(entity, "Status") != PublishedStatus),
            _ => query
        };
    }

    private static string BuildDisplayName(
        string? displayName,
        string? firstName,
        string? lastName,
        string? email)
    {
        if (!string.IsNullOrWhiteSpace(displayName))
            return displayName.Trim();

        var fullName = string.Join(" ", new[] { firstName, lastName }.Where(part => !string.IsNullOrWhiteSpace(part)));
        return string.IsNullOrWhiteSpace(fullName) ? email ?? "Assigned manager" : fullName;
    }

    private static void AppendTopContentSection(
        StringBuilder csv,
        string title,
        IEnumerable<TerritoryAnalyticsTopContentDto> rows)
    {
        AppendCsvRow(csv, title);
        AppendCsvRow(csv, "Name", "Region", "Views", "Average Rating");
        foreach (var row in rows)
        {
            AppendCsvRow(csv, row.Name, row.RegionName, row.Views, row.AverageRating);
        }
        AppendCsvRow(csv);
    }

    private static void AppendCsvRow(StringBuilder csv, params object?[] fields)
    {
        csv.AppendLine(string.Join(",", fields.Select(EscapeCsv)));
    }

    private static string EscapeCsv(object? value)
    {
        if (value is null)
            return string.Empty;

        var text = Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
        return text.Contains(',') || text.Contains('"') || text.Contains('\n') || text.Contains('\r')
            ? "\"" + text.Replace("\"", "\"\"", StringComparison.Ordinal) + "\""
            : text;
    }

    private sealed record PeriodWindow(string Key, DateTime StartDate, DateTime EndDate)
    {
        public DateTime PreviousStartDate => StartDate - (EndDate - StartDate);
    }

    private sealed record TimeBucket(DateTime Start, DateTime End, string Label);
    private sealed record RegionProjection(int RegionId, string RegionName);
    private readonly record struct EntityKey(string EntityType, int EntityId);
    private sealed record ContentCounts(int Destinations, int Locations, int Places, int Events)
    {
        public int Total => Destinations + Locations + Places + Events;
    }

    private sealed record RegionMetric(
        int RegionId,
        string RegionName,
        int PageViews,
        int UniqueVisitors,
        int ExternalClicks,
        int Saves,
        int ReviewCount,
        double AverageRating,
        ContentCounts ContentCounts)
    {
        public static RegionMetric Empty(int regionId, string regionName) =>
            new(regionId, regionName, 0, 0, 0, 0, 0, 0, new ContentCounts(0, 0, 0, 0));
    }

    private sealed record EntityProjection(
        string EntityType,
        int EntityId,
        int RegionId,
        string RegionName,
        string Name,
        string Status,
        string? ImageUrl,
        string? DetailRoute,
        string? PublicRoute,
        DateTime? EventDate);

    private sealed class EntityMetric
    {
        public EntityMetric(EntityProjection entity)
        {
            EntityType = entity.EntityType;
            EntityId = entity.EntityId;
            RegionId = entity.RegionId;
            RegionName = entity.RegionName;
            Name = entity.Name;
            ImageUrl = entity.ImageUrl;
            DetailRoute = entity.DetailRoute;
            PublicRoute = entity.PublicRoute;
            EventDate = entity.EventDate;
        }

        public string EntityType { get; }
        public int EntityId { get; }
        public int RegionId { get; }
        public string RegionName { get; }
        public string Name { get; }
        public string? ImageUrl { get; }
        public string? DetailRoute { get; }
        public string? PublicRoute { get; }
        public DateTime? EventDate { get; }
        public int PageViews { get; set; }
        public int UniqueVisitors { get; set; }
        public int ExternalClicks { get; set; }
        public int Saves { get; set; }
        public int ReviewCount { get; set; }
        public double AverageRating { get; set; }
    }

    private sealed record AnalyticsMetricEventRow(
        int EventId,
        string EventType,
        string EntityType,
        int EntityId,
        int RegionId,
        int? UserId,
        Guid? AnonymousSessionId,
        DateTime OccurredAt);

    private sealed record MetricSet(
        List<EntityProjection> Entities,
        List<EntityMetric> EntityMetrics,
        List<AnalyticsMetricEventRow> ViewEvents,
        List<AnalyticsMetricEventRow> ExternalEvents)
    {
        public static MetricSet Empty() =>
            new(new List<EntityProjection>(), new List<EntityMetric>(), new List<AnalyticsMetricEventRow>(), new List<AnalyticsMetricEventRow>());
    }
}
