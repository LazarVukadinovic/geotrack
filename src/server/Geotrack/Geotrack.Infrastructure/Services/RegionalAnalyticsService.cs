using System.Globalization;
using System.Text;
using System.Text.Json;
using Geotrack.Application.Analytics;
using Geotrack.Application.DTOs.Admin;
using Geotrack.Application.Interfaces.Service;
using Geotrack.Application.Security;
using Geotrack.Domain.Entities.Core;
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
    private const int DestinationRankingTake = 5;
    private const int DefaultTagViewsTake = 15;
    private const int MaxTagViewsTake = 50;
    private const int MaxSelectedTagIds = 50;
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
        var aggregationMode = ResolveAggregationMode(entityType);
        var sort = NormalizeSort(query.Sort);
        var skip = Math.Max(0, query.Skip);
        var take = Math.Clamp(query.Take <= 0 ? DefaultTake : query.Take, 1, MaxTake);

        var entities = await LoadEntitiesAsync(
            query.RegionId,
            activeRegion.DisplayName,
            entityType,
            query.Search,
            cancellationToken);

        var current = await BuildMetricSetAsync(entities, period.StartDate, period.EndDate, cancellationToken, aggregationMode);
        var previousTotals = await BuildPreviousTotalsAsync(entities, period, cancellationToken, aggregationMode);
        var rows = SortRows(current.Rows.Where(HasEngagement).ToList(), sort);
        var pageRows = rows.Skip(skip).Take(take).ToList();
        var destinationRanking = entityType is "all" or AnalyticsEntityTypes.Destination
            ? BuildDestinationRanking(current.Rows, query.RegionId)
            : await BuildDestinationRankingAsync(
                query.RegionId,
                activeRegion.DisplayName,
                query.Search,
                period,
                cancellationToken);
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
            DestinationRanking = destinationRanking,
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
        var aggregationMode = ResolveAggregationMode(entityType);
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
            : await LoadMonthlyVisitCountsAsync(entities, earliestWindowStart, rangeEndExclusive, aggregationMode, cancellationToken);

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

    public async Task<RegionalTagViewsAnalyticsResponseDto> GetTagViewsAsync(
        int userId,
        RegionalTagViewsQuery query,
        CancellationToken cancellationToken)
    {
        if (query.RegionId <= 0)
            throw new InvalidOperationException("regionId is required.");

        var activeRegion = await GetAssignedRegionAsync(userId, query.RegionId);
        var period = ResolveTagViewsPeriod(query.Period);
        var entityType = NormalizeEntityTypeFilter(query.EntityType);
        var take = Math.Clamp(query.Take <= 0 ? DefaultTagViewsTake : query.Take, 1, MaxTagViewsTake);
        var selectedTagIds = NormalizeSelectedTagIds(query.TagIds);

        var availableTags = await LoadAvailableTagViewsTagsAsync(query.RegionId, cancellationToken);
        var availableByTagId = availableTags.ToDictionary(tag => tag.TagId);

        if (selectedTagIds.Any(tagId => !availableByTagId.ContainsKey(tagId)))
        {
            throw new InvalidOperationException("One or more selected tags are not available for the active region.");
        }

        var aggregateRows = await LoadTagViewAggregatesAsync(
            query.RegionId,
            entityType,
            selectedTagIds,
            period.StartDate,
            period.EndDate,
            cancellationToken);

        var viewsByTagId = aggregateRows
            .GroupBy(row => row.TagId)
            .ToDictionary(group => group.Key, group => group.Sum(row => row.Views));

        var totalViews = viewsByTagId.Values.Sum();
        var items = selectedTagIds.Count > 0
            ? selectedTagIds
                .Select(tagId => BuildTagViewsItem(availableByTagId[tagId], viewsByTagId.GetValueOrDefault(tagId), totalViews))
                .ToList()
            : aggregateRows
                .GroupBy(row => row.TagId)
                .Select(group => BuildTagViewsItem(availableByTagId[group.Key], group.Sum(row => row.Views), totalViews))
                .Where(item => item.Views > 0)
                .OrderByDescending(item => item.Views)
                .ThenBy(item => item.TagName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.TagId)
                .Take(take)
                .ToList();

        return new RegionalTagViewsAnalyticsResponseDto
        {
            Period = period.Key,
            RegionId = query.RegionId,
            RegionDisplayName = activeRegion.DisplayName,
            TotalViews = totalViews,
            AvailableTags = availableTags,
            Items = items
        };
    }

    public async Task<RegionalAnalyticsEntityDetailDto> GetEntityDetailAsync(
        int userId,
        RegionalAnalyticsEntityDetailQuery query,
        CancellationToken cancellationToken)
    {
        if (query.RegionId <= 0)
            throw new InvalidOperationException("regionId is required.");

        if (query.EntityId <= 0)
            throw new InvalidOperationException("entityId is required.");

        var activeRegion = await GetAssignedRegionAsync(userId, query.RegionId);
        var entityType = NormalizeEntityTypeForDetail(query.EntityType);
        var period = ResolvePeriod(query.Period);

        var detail = entityType switch
        {
            AnalyticsEntityTypes.Destination => await LoadDestinationDetailAsync(query.RegionId, query.EntityId, activeRegion.DisplayName, cancellationToken),
            AnalyticsEntityTypes.Location => await LoadLocationDetailAsync(query.RegionId, query.EntityId, activeRegion.DisplayName, cancellationToken),
            AnalyticsEntityTypes.Place => await LoadPlaceDetailAsync(query.RegionId, query.EntityId, activeRegion.DisplayName, AnalyticsEntityTypes.Place, cancellationToken),
            AnalyticsEntityTypes.Hotel => await LoadPlaceDetailAsync(query.RegionId, query.EntityId, activeRegion.DisplayName, AnalyticsEntityTypes.Hotel, cancellationToken),
            AnalyticsEntityTypes.Event => await LoadEventDetailAsync(query.RegionId, query.EntityId, activeRegion.DisplayName, cancellationToken),
            _ => throw new InvalidOperationException("Invalid entity type.")
        };

        var projection = new EntityProjection
        {
            EntityType = detail.EntityType,
            EntityId = detail.EntityId,
            Name = detail.Name,
            RegionDisplayName = detail.RegionDisplayName,
            ImageUrl = detail.ImageUrl,
            CategoryLabel = DetailCategoryLabel(detail),
            Status = detail.Status,
            DetailRoute = DetailRoute(detail.EntityType, detail.EntityId)
        };

        var detailAggregationMode = ResolveAggregationMode(detail.EntityType);
        var current = await BuildMetricSetAsync(new[] { projection }, period.StartDate, period.EndDate, cancellationToken, detailAggregationMode);
        var currentMetric = current.Rows.Single();
        RegionalAnalyticsEntityMetricDto? previousMetric = null;

        if (period.StartDate.HasValue && period.Duration.HasValue)
        {
            var previousEnd = period.StartDate.Value;
            var previousStart = previousEnd - period.Duration.Value;
            var previous = await BuildMetricSetAsync(new[] { projection }, previousStart, previousEnd, cancellationToken, detailAggregationMode);
            previousMetric = previous.Rows.Single();
        }

        detail.Analytics = new RegionalAnalyticsEntityDetailMetricsDto
        {
            Views = currentMetric.PageViews,
            UniqueVisitors = currentMetric.UniqueVisitors,
            Saves = currentMetric.Saves,
            ExternalClicks = currentMetric.ExternalClicks,
            Reviews = currentMetric.ReviewCount,
            AverageRating = currentMetric.AverageRating,
            RatingCount = currentMetric.ReviewCount,
            TrendChangePercent = previousMetric is null ? 0 : PercentChange(currentMetric.PageViews, previousMetric.PageViews),
            LastViewedAt = current.ViewEvents
                .OrderByDescending(evt => evt.OccurredAt)
                .Select(evt => (DateTime?)evt.OccurredAt)
                .FirstOrDefault()
        };

        if (IsStayDetail(detail))
        {
            detail.TypeSpecific.ExternalPartnerClicks = await BuildExternalPartnerClicksAsync(
                detail.EntityId,
                detail.TypeSpecific.BookingPartners,
                period.StartDate,
                period.EndDate,
                cancellationToken);
        }

        return detail;
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

    private async Task<RegionalAnalyticsEntityDetailDto> LoadDestinationDetailAsync(
        int regionId,
        int entityId,
        string regionDisplayName,
        CancellationToken cancellationToken)
    {
        var row = await _db.Destinations
            .AsNoTracking()
            .Where(destination => destination.Id == entityId)
            .Select(destination => new
            {
                destination.Id,
                destination.Name,
                destination.Summary,
                destination.Description,
                destination.Status,
                destination.RegionId,
                DestinationType = destination.DestinationType.Name,
                destination.BoundaryNote,
                destination.BoundaryShapeType,
                destination.Latitude,
                destination.Longitude,
                destination.CreatedAt,
                destination.UpdatedAt,
                destination.PublishedAt,
                GalleryImages = destination.Images
                    .Where(image => image.TargetType == EntityImageTargetType.Destination)
                    .OrderByDescending(image => image.IsMain)
                    .ThenBy(image => image.SortOrder)
                    .ThenBy(image => image.CreatedAt)
                    .ThenBy(image => image.Id)
                    .Select(image => image.Url)
                    .ToList(),
                Tags = destination.DestinationTags
                    .OrderBy(destinationTag => destinationTag.Tag.Name)
                    .Select(destinationTag => new RegionalAnalyticsEntityTagDto
                    {
                        Id = destinationTag.TagId,
                        Name = destinationTag.Tag.Name,
                        Slug = destinationTag.Tag.Slug
                    })
                    .ToList(),
                LocationsCount = destination.Locations.Count(),
                PlacesCount = destination.Locations.SelectMany(location => location.Places).Count(),
                EventsCount = destination.Events.Count()
            })
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new KeyNotFoundException("Destination not found.");

        EnsureEntityInRegion(row.RegionId, regionId, "Destination");

        var gallery = NormalizeGallery(row.GalleryImages);
        return new RegionalAnalyticsEntityDetailDto
        {
            EntityType = AnalyticsEntityTypes.Destination,
            EntityId = row.Id,
            Name = row.Name,
            Title = row.Name,
            Status = NormalizeStatusLabel(row.Status),
            RegionId = row.RegionId,
            RegionDisplayName = regionDisplayName,
            DestinationId = row.Id,
            DestinationName = row.Name,
            Description = NullIfWhiteSpace(row.Description),
            Summary = NullIfWhiteSpace(row.Summary) ?? BuildSummary(row.Description),
            ImageUrl = gallery.FirstOrDefault(),
            GalleryImages = gallery,
            Tags = row.Tags,
            Latitude = row.Latitude,
            Longitude = row.Longitude,
            CreatedAt = row.CreatedAt,
            UpdatedAt = row.UpdatedAt,
            PublishedAt = row.PublishedAt,
            TypeSpecific = new RegionalAnalyticsEntityTypeSpecificDto
            {
                DestinationType = row.DestinationType,
                BoundaryNote = NullIfWhiteSpace(row.BoundaryNote),
                BoundaryShapeType = NullIfWhiteSpace(row.BoundaryShapeType),
                LocationsCount = row.LocationsCount,
                PlacesCount = row.PlacesCount,
                EventsCount = row.EventsCount
            }
        };
    }

    private async Task<RegionalAnalyticsEntityDetailDto> LoadLocationDetailAsync(
        int regionId,
        int entityId,
        string regionDisplayName,
        CancellationToken cancellationToken)
    {
        var row = await _db.Locations
            .AsNoTracking()
            .Where(location => location.Id == entityId)
            .Select(location => new
            {
                location.Id,
                location.Name,
                location.Summary,
                location.Description,
                location.Status,
                RegionId = location.Destination.RegionId,
                location.DestinationId,
                DestinationName = location.Destination.Name,
                LocationType = location.LocationType.Name,
                location.BoundaryNote,
                location.BoundaryShapeType,
                location.Latitude,
                location.Longitude,
                location.CreatedAt,
                location.UpdatedAt,
                location.PublishedAt,
                GalleryImages = location.Images
                    .Where(image => image.TargetType == EntityImageTargetType.Location)
                    .OrderByDescending(image => image.IsMain)
                    .ThenBy(image => image.SortOrder)
                    .ThenBy(image => image.CreatedAt)
                    .ThenBy(image => image.Id)
                    .Select(image => image.Url)
                    .ToList(),
                Tags = location.LocationTags
                    .OrderBy(locationTag => locationTag.Tag.Name)
                    .Select(locationTag => new RegionalAnalyticsEntityTagDto
                    {
                        Id = locationTag.TagId,
                        Name = locationTag.Tag.Name,
                        Slug = locationTag.Tag.Slug
                    })
                    .ToList(),
                PlacesCount = location.Places.Count()
            })
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new KeyNotFoundException("Location not found.");

        EnsureEntityInRegion(row.RegionId, regionId, "Location");

        var eventsCount = await _db.Events
            .AsNoTracking()
            .CountAsync(evt =>
                evt.Destination.RegionId == regionId &&
                (evt.LocationId == row.Id ||
                 (evt.PlaceId.HasValue && evt.Place != null && evt.Place.LocationId == row.Id)),
                cancellationToken);

        var gallery = NormalizeGallery(row.GalleryImages);
        return new RegionalAnalyticsEntityDetailDto
        {
            EntityType = AnalyticsEntityTypes.Location,
            EntityId = row.Id,
            Name = row.Name,
            Title = row.Name,
            Status = NormalizeStatusLabel(row.Status),
            RegionId = row.RegionId,
            RegionDisplayName = regionDisplayName,
            DestinationId = row.DestinationId,
            DestinationName = row.DestinationName,
            LocationId = row.Id,
            LocationName = row.Name,
            Description = NullIfWhiteSpace(row.Description),
            Summary = NullIfWhiteSpace(row.Summary) ?? BuildSummary(row.Description),
            ImageUrl = gallery.FirstOrDefault(),
            GalleryImages = gallery,
            Tags = row.Tags,
            Latitude = row.Latitude,
            Longitude = row.Longitude,
            CreatedAt = row.CreatedAt,
            UpdatedAt = row.UpdatedAt,
            PublishedAt = row.PublishedAt,
            TypeSpecific = new RegionalAnalyticsEntityTypeSpecificDto
            {
                LocationType = row.LocationType,
                ParentDestination = row.DestinationName,
                BoundaryNote = NullIfWhiteSpace(row.BoundaryNote),
                BoundaryShapeType = NullIfWhiteSpace(row.BoundaryShapeType),
                PlacesCount = row.PlacesCount,
                EventsCount = eventsCount
            }
        };
    }

    private async Task<RegionalAnalyticsEntityDetailDto> LoadPlaceDetailAsync(
        int regionId,
        int entityId,
        string regionDisplayName,
        string analyticsEntityType,
        CancellationToken cancellationToken)
    {
        var row = await _db.Places
            .AsNoTracking()
            .Where(place => place.Id == entityId)
            .Select(place => new
            {
                place.Id,
                place.Name,
                place.Subtitle,
                place.Description,
                place.Status,
                place.Address,
                place.Latitude,
                place.Longitude,
                place.CreatedAt,
                place.UpdatedAt,
                place.PublishedAt,
                place.Website,
                place.BookingExternalLink,
                ContactPhone = place.Number,
                place.ContactEmail,
                place.AdmissionPriceRange,
                place.OpeningHours,
                place.OperatingHoursJson,
                place.BestTimeToVisit,
                place.VisitorTips,
                place.AccessNotes,
                place.AccessibilityNotes,
                PlaceType = place.PlaceType.Name,
                StayType = place.Stay != null ? place.Stay.StayType.Name : null,
                RegionId = place.Location.Destination.RegionId,
                place.LocationId,
                LocationName = place.Location.Name,
                DestinationId = place.Location.DestinationId,
                DestinationName = place.Location.Destination.Name,
                GalleryImages = place.Images
                    .Where(image => image.TargetType == EntityImageTargetType.Place)
                    .OrderByDescending(image => image.IsMain)
                    .ThenBy(image => image.SortOrder)
                    .ThenBy(image => image.CreatedAt)
                    .ThenBy(image => image.Id)
                    .Select(image => image.Url)
                    .ToList(),
                Tags = place.PlaceTags
                    .OrderBy(placeTag => placeTag.Tag.Name)
                    .Select(placeTag => new RegionalAnalyticsEntityTagDto
                    {
                        Id = placeTag.TagId,
                        Name = placeTag.Tag.Name,
                        Slug = placeTag.Tag.Slug
                    })
                    .ToList()
            })
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new KeyNotFoundException("Place not found.");

        EnsureEntityInRegion(row.RegionId, regionId, "Place");

        var placeAmenities = await _db.PlaceAmenities
            .AsNoTracking()
            .Where(item => item.PlaceId == row.Id)
            .Select(item => item.Amenity.Name)
            .ToListAsync(cancellationToken);

        var stayAmenities = await _db.StayStayAmenities
            .AsNoTracking()
            .Where(item => item.StayPlaceId == row.Id)
            .Select(item => item.StayAmenity.Name)
            .ToListAsync(cancellationToken);

        var bookingPartners = await _db.PlaceBookingPartners
            .AsNoTracking()
            .Where(item => item.PlaceId == row.Id)
            .OrderBy(item => item.BookingPartner.Name)
            .Select(item => new RegionalAnalyticsEntityLinkDto
            {
                Label = item.BookingPartner.Name,
                Url = item.Url
            })
            .ToListAsync(cancellationToken);

        var gallery = NormalizeGallery(row.GalleryImages);
        return new RegionalAnalyticsEntityDetailDto
        {
            EntityType = analyticsEntityType,
            EntityId = row.Id,
            Name = row.Name,
            Title = row.Name,
            Subtitle = NullIfWhiteSpace(row.Subtitle),
            Status = NormalizeStatusLabel(row.Status),
            RegionId = row.RegionId,
            RegionDisplayName = regionDisplayName,
            DestinationId = row.DestinationId,
            DestinationName = row.DestinationName,
            LocationId = row.LocationId,
            LocationName = row.LocationName,
            PlaceId = row.Id,
            PlaceName = row.Name,
            Description = NullIfWhiteSpace(row.Description),
            Summary = NullIfWhiteSpace(row.Subtitle) ?? BuildSummary(row.Description),
            ImageUrl = gallery.FirstOrDefault(),
            GalleryImages = gallery,
            Tags = row.Tags,
            Address = NullIfWhiteSpace(row.Address),
            Latitude = row.Latitude,
            Longitude = row.Longitude,
            CreatedAt = row.CreatedAt,
            UpdatedAt = row.UpdatedAt,
            PublishedAt = row.PublishedAt,
            TypeSpecific = new RegionalAnalyticsEntityTypeSpecificDto
            {
                PlaceType = row.PlaceType,
                StayType = NullIfWhiteSpace(row.StayType),
                ParentLocation = row.LocationName,
                ParentDestination = row.DestinationName,
                Website = NullIfWhiteSpace(row.Website),
                BookingExternalLink = NullIfWhiteSpace(row.BookingExternalLink),
                ContactPhone = NullIfWhiteSpace(row.ContactPhone),
                ContactEmail = NullIfWhiteSpace(row.ContactEmail),
                PriceRange = NullIfWhiteSpace(row.AdmissionPriceRange),
                OpeningHours = NullIfWhiteSpace(row.OpeningHours),
                OperatingHours = DeserializeOperatingHours(row.OperatingHoursJson),
                Amenities = placeAmenities
                    .Concat(stayAmenities)
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
                    .ToList(),
                BookingPartners = bookingPartners,
                BestTimeToVisit = NullIfWhiteSpace(row.BestTimeToVisit),
                VisitorTips = NullIfWhiteSpace(row.VisitorTips),
                AccessNotes = NullIfWhiteSpace(row.AccessNotes),
                AccessibilityNotes = NullIfWhiteSpace(row.AccessibilityNotes)
            }
        };
    }

    private async Task<RegionalAnalyticsEntityDetailDto> LoadEventDetailAsync(
        int regionId,
        int entityId,
        string regionDisplayName,
        CancellationToken cancellationToken)
    {
        var row = await _db.Events
            .AsNoTracking()
            .Where(evt => evt.Id == entityId)
            .Select(evt => new
            {
                evt.Id,
                evt.Name,
                evt.Summary,
                evt.Description,
                evt.Status,
                RegionId = evt.Destination.RegionId,
                evt.DestinationId,
                DestinationName = evt.Destination.Name,
                evt.LocationId,
                LocationName = evt.Location != null ? evt.Location.Name : null,
                evt.PlaceId,
                PlaceName = evt.Place != null ? evt.Place.Name : null,
                Address = evt.Place != null ? evt.Place.Address : null,
                Latitude = evt.Place != null ? evt.Place.Latitude : evt.Location != null ? evt.Location.Latitude : evt.Destination.Latitude,
                Longitude = evt.Place != null ? evt.Place.Longitude : evt.Location != null ? evt.Location.Longitude : evt.Destination.Longitude,
                EventType = evt.EventType.Name,
                Category = evt.EventType.Name,
                evt.ParentEntityType,
                evt.VenueName,
                StartsAt = evt.StartDate,
                EndsAt = evt.EndDate,
                evt.IsAllDay,
                evt.IsRecurring,
                evt.TicketPrice,
                evt.IsFree,
                evt.ExternalTicketLink,
                OrganizerName = evt.HostedBy,
                evt.ContactEmail,
                evt.AttendanceLimit,
                evt.AgeRestriction,
                evt.Duration,
                evt.VenueDescription,
                evt.ArrivalTips,
                evt.Accessibility,
                evt.CreatedAt,
                evt.UpdatedAt,
                evt.PublishedAt,
                GalleryImages = evt.Images
                    .Where(image => image.TargetType == EntityImageTargetType.Event)
                    .OrderByDescending(image => image.IsMain)
                    .ThenBy(image => image.SortOrder)
                    .ThenBy(image => image.CreatedAt)
                    .ThenBy(image => image.Id)
                    .Select(image => image.Url)
                    .ToList(),
                Tags = evt.EventTags
                    .OrderBy(eventTag => eventTag.Tag.Name)
                    .Select(eventTag => new RegionalAnalyticsEntityTagDto
                    {
                        Id = eventTag.TagId,
                        Name = eventTag.Tag.Name,
                        Slug = eventTag.Tag.Slug
                    })
                    .ToList()
            })
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new KeyNotFoundException("Event not found.");

        EnsureEntityInRegion(row.RegionId, regionId, "Event");

        var gallery = NormalizeGallery(row.GalleryImages);
        return new RegionalAnalyticsEntityDetailDto
        {
            EntityType = AnalyticsEntityTypes.Event,
            EntityId = row.Id,
            Name = row.Name,
            Title = row.Name,
            Status = NormalizeStatusLabel(row.Status),
            RegionId = row.RegionId,
            RegionDisplayName = regionDisplayName,
            DestinationId = row.DestinationId,
            DestinationName = row.DestinationName,
            LocationId = row.LocationId,
            LocationName = row.LocationName,
            PlaceId = row.PlaceId,
            PlaceName = row.PlaceName,
            Description = NullIfWhiteSpace(row.Description),
            Summary = NullIfWhiteSpace(row.Summary) ?? BuildSummary(row.Description),
            ImageUrl = gallery.FirstOrDefault(),
            GalleryImages = gallery,
            Tags = row.Tags,
            Address = NullIfWhiteSpace(row.Address),
            Latitude = row.Latitude,
            Longitude = row.Longitude,
            CreatedAt = row.CreatedAt,
            UpdatedAt = row.UpdatedAt,
            PublishedAt = row.PublishedAt,
            TypeSpecific = new RegionalAnalyticsEntityTypeSpecificDto
            {
                EventType = row.EventType,
                Category = row.Category,
                ParentEntityType = NullIfWhiteSpace(row.ParentEntityType),
                ParentDestination = row.DestinationName,
                ParentLocation = NullIfWhiteSpace(row.LocationName),
                ParentPlace = NullIfWhiteSpace(row.PlaceName),
                VenueName = NullIfWhiteSpace(row.VenueName),
                StartsAt = row.StartsAt,
                EndsAt = row.EndsAt,
                AllDay = row.IsAllDay,
                IsRecurring = row.IsRecurring,
                TicketPrice = NullIfWhiteSpace(row.TicketPrice),
                IsFree = row.IsFree,
                ExternalTicketLink = NullIfWhiteSpace(row.ExternalTicketLink),
                OrganizerName = NullIfWhiteSpace(row.OrganizerName),
                ContactEmail = NullIfWhiteSpace(row.ContactEmail),
                AttendanceLimit = row.AttendanceLimit,
                AgeRestriction = NullIfWhiteSpace(row.AgeRestriction),
                Duration = NullIfWhiteSpace(row.Duration),
                VenueDescription = NullIfWhiteSpace(row.VenueDescription),
                ArrivalTips = NullIfWhiteSpace(row.ArrivalTips),
                Accessibility = NullIfWhiteSpace(row.Accessibility)
            }
        };
    }

    private async Task<List<RegionalAnalyticsExternalPartnerClickDto>> BuildExternalPartnerClicksAsync(
        int placeId,
        IReadOnlyList<RegionalAnalyticsEntityLinkDto> bookingPartners,
        DateTime? fromUtc,
        DateTime toUtc,
        CancellationToken cancellationToken)
    {
        var partnerDefinitions = BuildExternalPartnerDefinitions(bookingPartners);
        if (partnerDefinitions.Count == 0)
        {
            return new List<RegionalAnalyticsExternalPartnerClickDto>();
        }

        var eventRows = await ApplyAnalyticsPeriod(_db.AnalyticsEvents.AsNoTracking(), fromUtc, toUtc)
            .Where(evt =>
                evt.EventType == AnalyticsEventTypes.OutboundLinkClicked &&
                evt.EntityId == placeId &&
                (evt.EntityType == AnalyticsEntityTypes.Place || evt.EntityType == AnalyticsEntityTypes.Hotel))
            .Select(evt => new ExternalPartnerClickEventRow
            {
                MetadataJson = evt.MetadataJson
            })
            .ToListAsync(cancellationToken);

        var countsByDefinition = partnerDefinitions.ToDictionary(definition => definition.Id, _ => 0);
        var uniqueHosts = partnerDefinitions
            .Where(definition => !string.IsNullOrWhiteSpace(definition.Host))
            .GroupBy(definition => definition.Host!, StringComparer.Ordinal)
            .Where(group => group.Count() == 1)
            .ToDictionary(
                group => group.Key,
                group => group.Single(),
                StringComparer.Ordinal);

        foreach (var row in eventRows)
        {
            var metadata = ParsePartnerClickMetadata(row.MetadataJson);
            var matchedPartner = MatchExternalPartnerClick(partnerDefinitions, uniqueHosts, metadata);

            if (matchedPartner is not null)
            {
                countsByDefinition[matchedPartner.Id]++;
            }
        }

        var totalAttributedClicks = countsByDefinition.Values.Sum();

        return partnerDefinitions
            .Select(definition =>
            {
                var count = countsByDefinition[definition.Id];
                return new RegionalAnalyticsExternalPartnerClickDto
                {
                    PartnerKey = definition.PartnerKey,
                    PartnerLabel = definition.PartnerLabel,
                    Url = definition.Url,
                    ClickCount = count,
                    SharePercent = totalAttributedClicks <= 0
                        ? 0
                        : Math.Round((double)count / totalAttributedClicks * 100, 1, MidpointRounding.AwayFromZero)
                };
            })
            .ToList();
    }

    private static List<ExternalPartnerDefinition> BuildExternalPartnerDefinitions(
        IReadOnlyList<RegionalAnalyticsEntityLinkDto> bookingPartners)
    {
        return bookingPartners
            .Select((partner, index) =>
            {
                var url = NullIfWhiteSpace(partner.Url);
                if (url is null)
                {
                    return null;
                }

                var label = NullIfWhiteSpace(partner.Label) ?? "Other External Link";
                var host = ExtractHost(url);
                var partnerKey = ResolvePartnerKey(label, host);
                var providerSlug = partnerKey switch
                {
                    "booking" => "booking",
                    "airbnb" => "airbnb",
                    _ => SlugifyPartnerProvider(label)
                };

                return new ExternalPartnerDefinition(
                    index,
                    partnerKey,
                    label,
                    url,
                    providerSlug,
                    host,
                    PartnerSortOrder(partnerKey));
            })
            .Where(definition => definition is not null)
            .Cast<ExternalPartnerDefinition>()
            .GroupBy(
                definition => (
                    Key: definition.PartnerKey,
                    Label: definition.PartnerLabel.ToLowerInvariant(),
                    Url: definition.Url?.ToLowerInvariant() ?? string.Empty))
            .Select(group => group.First())
            .OrderBy(definition => definition.SortOrder)
            .ThenBy(definition => definition.PartnerLabel, StringComparer.OrdinalIgnoreCase)
            .ThenBy(definition => definition.Id)
            .ToList();
    }

    private static ExternalPartnerDefinition? MatchExternalPartnerClick(
        IReadOnlyList<ExternalPartnerDefinition> partnerDefinitions,
        IReadOnlyDictionary<string, ExternalPartnerDefinition> uniqueHosts,
        PartnerClickMetadata metadata)
    {
        if (!string.IsNullOrWhiteSpace(metadata.Host) &&
            uniqueHosts.TryGetValue(metadata.Host!, out var hostMatchedPartner))
        {
            return hostMatchedPartner;
        }

        if (metadata.PartnerKey is "booking" or "airbnb")
        {
            var partnerKeyMatch = partnerDefinitions.FirstOrDefault(definition => definition.PartnerKey == metadata.PartnerKey);
            if (partnerKeyMatch is not null)
            {
                return partnerKeyMatch;
            }
        }

        if (metadata.Provider is "booking" or "airbnb")
        {
            var providerKeyMatch = partnerDefinitions.FirstOrDefault(definition => definition.PartnerKey == metadata.Provider);
            if (providerKeyMatch is not null)
            {
                return providerKeyMatch;
            }
        }

        if (!string.IsNullOrWhiteSpace(metadata.Provider))
        {
            var providerMatch = partnerDefinitions.FirstOrDefault(definition =>
                definition.ProviderSlug == metadata.Provider &&
                definition.PartnerKey == "external");

            if (providerMatch is not null)
            {
                return providerMatch;
            }
        }

        if (metadata.PartnerKey == "external")
        {
            var externalPartners = partnerDefinitions
                .Where(definition => definition.PartnerKey == "external")
                .ToList();

            if (externalPartners.Count == 1)
            {
                return externalPartners[0];
            }
        }

        return null;
    }

    private static PartnerClickMetadata ParsePartnerClickMetadata(string? metadataJson)
    {
        if (string.IsNullOrWhiteSpace(metadataJson))
        {
            return new PartnerClickMetadata(null, null, null);
        }

        try
        {
            using var document = JsonDocument.Parse(metadataJson);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return new PartnerClickMetadata(null, null, null);
            }

            var provider = ReadMetadataString(root, "provider");
            var partnerKey = ReadMetadataString(root, "partnerKey");
            var host = NormalizeHost(ReadMetadataString(root, "host"));

            return new PartnerClickMetadata(
                NormalizeProviderToken(provider),
                NormalizeProviderToken(partnerKey),
                host);
        }
        catch (JsonException)
        {
            return new PartnerClickMetadata(null, null, null);
        }
    }

    private static string? ReadMetadataString(JsonElement root, string propertyName)
    {
        return root.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? NullIfWhiteSpace(value.GetString())
            : null;
    }

    private static string ResolvePartnerKey(string label, string? host)
    {
        var token = NormalizeToken(label);

        if (token is "booking" or "bookingcom" || IsHostOrSubdomain(host, "booking.com"))
        {
            return "booking";
        }

        if (token == "airbnb" || IsHostOrSubdomain(host, "airbnb.com"))
        {
            return "airbnb";
        }

        return "external";
    }

    private static int PartnerSortOrder(string partnerKey) =>
        partnerKey switch
        {
            "booking" => 0,
            "airbnb" => 1,
            _ => 2
        };

    private static string? ExtractHost(string? url)
    {
        if (string.IsNullOrWhiteSpace(url) ||
            !Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri))
        {
            return null;
        }

        return NormalizeHost(uri.Host);
    }

    private static string? NormalizeHost(string? host)
    {
        if (string.IsNullOrWhiteSpace(host))
        {
            return null;
        }

        var normalized = host.Trim().TrimEnd('.').ToLowerInvariant();
        return normalized.StartsWith("www.", StringComparison.Ordinal)
            ? normalized[4..]
            : normalized;
    }

    private static bool IsHostOrSubdomain(string? host, string rootDomain)
    {
        if (string.IsNullOrWhiteSpace(host))
        {
            return false;
        }

        return host.Equals(rootDomain, StringComparison.Ordinal) ||
            host.EndsWith("." + rootDomain, StringComparison.Ordinal);
    }

    private static string? NormalizeProviderToken(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim().ToLowerInvariant();
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }

    private static string SlugifyPartnerProvider(string value)
    {
        var builder = new StringBuilder(value.Length);
        var previousDash = false;

        foreach (var character in value.Trim().ToLowerInvariant())
        {
            if ((character >= 'a' && character <= 'z') || (character >= '0' && character <= '9'))
            {
                builder.Append(character);
                previousDash = false;
            }
            else if (!previousDash)
            {
                builder.Append('-');
                previousDash = true;
            }
        }

        var slug = builder.ToString().Trim('-');
        return string.IsNullOrWhiteSpace(slug) ? "other" : slug;
    }

    private static bool IsStayDetail(RegionalAnalyticsEntityDetailDto detail)
    {
        if (detail.EntityType == AnalyticsEntityTypes.Hotel)
        {
            return true;
        }

        if (detail.EntityType != AnalyticsEntityTypes.Place)
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(detail.TypeSpecific.StayType))
        {
            return true;
        }

        var placeType = NormalizeToken(detail.TypeSpecific.PlaceType ?? string.Empty);
        return placeType is "stay" or "stays" or "hotel" or "hotels" or "accommodation" or "accommodations" or "lodging";
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

        if (entityType == AnalyticsEntityTypes.Hotel)
        {
            var query = _db.Places
                .AsNoTracking()
                .Where(place => place.Location.Destination.RegionId == regionId && place.Stay != null);

            if (like is not null)
            {
                query = query.Where(place =>
                    EF.Functions.ILike(place.Name, like) ||
                    EF.Functions.ILike(place.Status, like) ||
                    EF.Functions.ILike(place.PlaceType.Name, like) ||
                    EF.Functions.ILike(place.Stay!.StayType.Name, like));
            }

            entities.AddRange(await query
                .Select(place => new EntityProjection
                {
                    EntityType = AnalyticsEntityTypes.Hotel,
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
                    CategoryLabel = place.Stay != null ? place.Stay.StayType.Name : place.PlaceType.Name,
                    Status = place.Status,
                    DetailRoute = "/stays/" + place.Id
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
        MetricAggregationMode aggregationMode,
        CancellationToken cancellationToken)
    {
        var monthlyRows = (await LoadAnalyticsMetricEventsAsync(
                entities,
                fromUtc,
                toUtc,
                aggregationMode,
                cancellationToken))
            .Where(evt => ViewEventTypes.Contains(evt.EventType))
            .GroupBy(evt => new { evt.OccurredAt.Year, evt.OccurredAt.Month })
            .Select(group => new MonthlyVisitAggregate
            {
                Year = group.Key.Year,
                Month = group.Key.Month,
                Count = group.Count()
            })
            .ToList();

        return monthlyRows.ToDictionary(row => (row.Year, row.Month), row => row.Count);
    }

    private async Task<List<AnalyticsMetricEventRow>> LoadAnalyticsMetricEventsAsync(
        IReadOnlyList<EntityProjection> entities,
        DateTime? fromUtc,
        DateTime toUtc,
        MetricAggregationMode aggregationMode,
        CancellationToken cancellationToken)
    {
        return aggregationMode switch
        {
            MetricAggregationMode.DestinationRollup => await LoadDestinationRollupMetricEventsAsync(
                EntityIds(entities, AnalyticsEntityTypes.Destination),
                fromUtc,
                toUtc,
                cancellationToken),
            MetricAggregationMode.LocationRollup => await LoadLocationRollupMetricEventsAsync(
                EntityIds(entities, AnalyticsEntityTypes.Location),
                fromUtc,
                toUtc,
                cancellationToken),
            _ => await LoadDirectMetricEventsAsync(entities, fromUtc, toUtc, cancellationToken)
        };
    }

    private async Task<List<AnalyticsMetricEventRow>> LoadDirectMetricEventsAsync(
        IReadOnlyList<EntityProjection> entities,
        DateTime? fromUtc,
        DateTime toUtc,
        CancellationToken cancellationToken)
    {
        var keys = entities.Select(entity => new EntityKey(entity.EntityType, entity.EntityId)).ToHashSet();
        var destinationIds = EntityIds(entities, AnalyticsEntityTypes.Destination);
        var locationIds = EntityIds(entities, AnalyticsEntityTypes.Location);
        var placeIds = EntityIds(entities, AnalyticsEntityTypes.Place);
        var hotelIds = EntityIds(entities, AnalyticsEntityTypes.Hotel);
        var eventIds = EntityIds(entities, AnalyticsEntityTypes.Event);

        var analyticsEvents = await MetricEventBaseQuery(fromUtc, toUtc)
            .Where(evt =>
                (evt.EntityType == AnalyticsEntityTypes.Destination && destinationIds.Contains(evt.EntityId!.Value)) ||
                (evt.EntityType == AnalyticsEntityTypes.Location && locationIds.Contains(evt.EntityId!.Value)) ||
                (evt.EntityType == AnalyticsEntityTypes.Place && placeIds.Contains(evt.EntityId!.Value)) ||
                (evt.EntityType == AnalyticsEntityTypes.Hotel && (placeIds.Contains(evt.EntityId!.Value) || hotelIds.Contains(evt.EntityId!.Value))) ||
                (evt.EntityType == AnalyticsEntityTypes.Event && eventIds.Contains(evt.EntityId!.Value)))
            .Select(evt => new AnalyticsMetricEventRow
            {
                EntityType = evt.EntityType!,
                EntityId = evt.EntityId!.Value,
                EventType = evt.EventType,
                UserId = evt.UserId,
                AnonymousSessionId = evt.AnonymousSessionId,
                OccurredAt = evt.OccurredAt,
                Route = evt.Route,
                DurationMs = evt.DurationMs
            })
            .ToListAsync(cancellationToken);

        foreach (var row in analyticsEvents)
        {
            if (row.EntityType == AnalyticsEntityTypes.Hotel &&
                !keys.Contains(new EntityKey(AnalyticsEntityTypes.Hotel, row.EntityId)) &&
                keys.Contains(new EntityKey(AnalyticsEntityTypes.Place, row.EntityId)))
            {
                row.EntityType = AnalyticsEntityTypes.Place;
            }
        }

        return analyticsEvents
            .Where(evt => keys.Contains(new EntityKey(evt.EntityType, evt.EntityId)))
            .ToList();
    }

    private async Task<List<AnalyticsMetricEventRow>> LoadDestinationRollupMetricEventsAsync(
        IReadOnlyCollection<int> destinationIds,
        DateTime? fromUtc,
        DateTime toUtc,
        CancellationToken cancellationToken)
    {
        if (destinationIds.Count == 0)
        {
            return new List<AnalyticsMetricEventRow>();
        }

        var rows = new List<AnalyticsMetricEventRow>();
        var baseEvents = MetricEventBaseQuery(fromUtc, toUtc);

        rows.AddRange(await baseEvents
            .Where(evt => evt.EntityType == AnalyticsEntityTypes.Destination && destinationIds.Contains(evt.EntityId!.Value))
            .Select(evt => ToMetricEventRow(evt, AnalyticsEntityTypes.Destination, evt.EntityId!.Value))
            .ToListAsync(cancellationToken));

        rows.AddRange(await (
            from evt in baseEvents
            join location in _db.Locations
                .AsNoTracking()
                .Where(location => destinationIds.Contains(location.DestinationId))
                on evt.EntityId!.Value equals location.Id
            where evt.EntityType == AnalyticsEntityTypes.Location
            select ToMetricEventRow(evt, AnalyticsEntityTypes.Destination, location.DestinationId))
            .ToListAsync(cancellationToken));

        rows.AddRange(await (
            from evt in baseEvents
            join place in _db.Places
                .AsNoTracking()
                .Where(place => destinationIds.Contains(place.Location.DestinationId))
                on evt.EntityId!.Value equals place.Id
            where evt.EntityType == AnalyticsEntityTypes.Place || evt.EntityType == AnalyticsEntityTypes.Hotel
            select ToMetricEventRow(evt, AnalyticsEntityTypes.Destination, place.Location.DestinationId))
            .ToListAsync(cancellationToken));

        rows.AddRange(await (
            from evt in baseEvents
            join eventEntity in _db.Events
                .AsNoTracking()
                .Where(eventEntity => destinationIds.Contains(eventEntity.DestinationId))
                on evt.EntityId!.Value equals eventEntity.Id
            where evt.EntityType == AnalyticsEntityTypes.Event
            select ToMetricEventRow(evt, AnalyticsEntityTypes.Destination, eventEntity.DestinationId))
            .ToListAsync(cancellationToken));

        return rows;
    }

    private async Task<List<AnalyticsMetricEventRow>> LoadLocationRollupMetricEventsAsync(
        IReadOnlyCollection<int> locationIds,
        DateTime? fromUtc,
        DateTime toUtc,
        CancellationToken cancellationToken)
    {
        if (locationIds.Count == 0)
        {
            return new List<AnalyticsMetricEventRow>();
        }

        var rows = new List<AnalyticsMetricEventRow>();
        var baseEvents = MetricEventBaseQuery(fromUtc, toUtc);

        rows.AddRange(await baseEvents
            .Where(evt => evt.EntityType == AnalyticsEntityTypes.Location && locationIds.Contains(evt.EntityId!.Value))
            .Select(evt => ToMetricEventRow(evt, AnalyticsEntityTypes.Location, evt.EntityId!.Value))
            .ToListAsync(cancellationToken));

        rows.AddRange(await (
            from evt in baseEvents
            join place in _db.Places
                .AsNoTracking()
                .Where(place => locationIds.Contains(place.LocationId))
                on evt.EntityId!.Value equals place.Id
            where evt.EntityType == AnalyticsEntityTypes.Place || evt.EntityType == AnalyticsEntityTypes.Hotel
            select ToMetricEventRow(evt, AnalyticsEntityTypes.Location, place.LocationId))
            .ToListAsync(cancellationToken));

        rows.AddRange(await (
            from evt in baseEvents
            join eventEntity in _db.Events
                .AsNoTracking()
                .Where(eventEntity => eventEntity.LocationId.HasValue && locationIds.Contains(eventEntity.LocationId.Value))
                on evt.EntityId!.Value equals eventEntity.Id
            where evt.EntityType == AnalyticsEntityTypes.Event
            select ToMetricEventRow(evt, AnalyticsEntityTypes.Location, eventEntity.LocationId!.Value))
            .ToListAsync(cancellationToken));

        rows.AddRange(await (
            from evt in baseEvents
            join eventEntity in _db.Events
                .AsNoTracking()
                .Where(eventEntity => !eventEntity.LocationId.HasValue && eventEntity.PlaceId.HasValue)
                on evt.EntityId!.Value equals eventEntity.Id
            join place in _db.Places
                .AsNoTracking()
                .Where(place => locationIds.Contains(place.LocationId))
                on eventEntity.PlaceId!.Value equals place.Id
            where evt.EntityType == AnalyticsEntityTypes.Event
            select ToMetricEventRow(evt, AnalyticsEntityTypes.Location, place.LocationId))
            .ToListAsync(cancellationToken));

        return rows;
    }

    private IQueryable<AnalyticsEvent> MetricEventBaseQuery(DateTime? fromUtc, DateTime toUtc) =>
        ApplyAnalyticsPeriod(_db.AnalyticsEvents.AsNoTracking(), fromUtc, toUtc)
            .Where(evt =>
                evt.EntityId.HasValue &&
                evt.EntityType != null &&
                (ViewEventTypes.Contains(evt.EventType) ||
                 ExternalClickEventTypes.Contains(evt.EventType) ||
                 evt.EventType == AnalyticsEventTypes.PageDuration));

    private static AnalyticsMetricEventRow ToMetricEventRow(
        AnalyticsEvent evt,
        string entityType,
        int entityId) =>
        new()
        {
            EntityType = entityType,
            EntityId = entityId,
            EventType = evt.EventType,
            UserId = evt.UserId,
            AnonymousSessionId = evt.AnonymousSessionId,
            OccurredAt = evt.OccurredAt,
            Route = evt.Route,
            DurationMs = evt.DurationMs
        };

    private async Task<MetricSet> BuildMetricSetAsync(
        IReadOnlyList<EntityProjection> entities,
        DateTime? fromUtc,
        DateTime toUtc,
        CancellationToken cancellationToken,
        MetricAggregationMode aggregationMode = MetricAggregationMode.Direct)
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

        var destinationIds = EntityIds(entities, AnalyticsEntityTypes.Destination);
        var locationIds = EntityIds(entities, AnalyticsEntityTypes.Location);
        var placeIds = EntityIds(entities, AnalyticsEntityTypes.Place);
        var hotelIds = EntityIds(entities, AnalyticsEntityTypes.Hotel);
        var eventIds = EntityIds(entities, AnalyticsEntityTypes.Event);

        var analyticsEvents = await LoadAnalyticsMetricEventsAsync(
            entities,
            fromUtc,
            toUtc,
            aggregationMode,
            cancellationToken);

        ApplyAnalyticsEventMetrics(rows, analyticsEvents);
        await ApplySaveMetricsAsync(rows, destinationIds, locationIds, placeIds, hotelIds, eventIds, aggregationMode, fromUtc, toUtc, cancellationToken);
        await ApplyReviewMetricsAsync(rows, destinationIds, locationIds, placeIds, hotelIds, eventIds, aggregationMode, fromUtc, toUtc, cancellationToken);

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
        CancellationToken cancellationToken,
        MetricAggregationMode aggregationMode)
    {
        if (!period.StartDate.HasValue || !period.Duration.HasValue)
            return null;

        var previousEnd = period.StartDate.Value;
        var previousStart = previousEnd - period.Duration.Value;
        var previous = await BuildMetricSetAsync(entities, previousStart, previousEnd, cancellationToken, aggregationMode);
        return previous.Totals;
    }

    private async Task<List<RegionalDestinationRankingItemDto>> BuildDestinationRankingAsync(
        int regionId,
        string regionDisplayName,
        string? search,
        PeriodWindow period,
        CancellationToken cancellationToken)
    {
        var destinations = await LoadEntitiesAsync(
            regionId,
            regionDisplayName,
            AnalyticsEntityTypes.Destination,
            search,
            cancellationToken);

        var destinationMetrics = await BuildMetricSetAsync(
            destinations,
            period.StartDate,
            period.EndDate,
            cancellationToken,
            MetricAggregationMode.DestinationRollup);

        return BuildDestinationRanking(destinationMetrics.Rows, regionId);
    }

    private static List<RegionalDestinationRankingItemDto> BuildDestinationRanking(
        IReadOnlyList<RegionalAnalyticsEntityMetricDto> rows,
        int regionId)
    {
        var topDestinations = rows
            .Where(row => row.EntityType == AnalyticsEntityTypes.Destination && row.PageViews > 0)
            .OrderByDescending(row => row.PageViews)
            .ThenByDescending(row => row.UniqueVisitors)
            .ThenBy(row => row.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(row => row.EntityId)
            .Take(DestinationRankingTake)
            .ToList();

        var topValue = topDestinations.FirstOrDefault()?.PageViews ?? 0;

        return topDestinations
            .Select((row, index) => new RegionalDestinationRankingItemDto
            {
                DestinationId = row.EntityId,
                DestinationName = row.Name,
                RegionId = regionId,
                RegionDisplayName = row.RegionDisplayName,
                Visits = row.PageViews,
                Rank = index + 1,
                PercentageOfTopValue = topValue <= 0
                    ? 0
                    : Math.Round((double)row.PageViews / topValue * 100, 1, MidpointRounding.AwayFromZero),
                ImageUrl = row.ImageUrl,
                DetailRoute = row.DetailRoute
            })
            .ToList();
    }

    private async Task<List<RegionalTagViewsAvailableTagDto>> LoadAvailableTagViewsTagsAsync(
        int regionId,
        CancellationToken cancellationToken)
    {
        var rows = new List<TagScopeRow>();

        rows.AddRange(await _db.DestinationTags
            .AsNoTracking()
            .Where(destinationTag => destinationTag.Destination.RegionId == regionId)
            .Select(destinationTag => new TagScopeRow
            {
                TagId = destinationTag.TagId,
                TagName = destinationTag.Tag.Name,
                EntityScope = AnalyticsEntityTypes.Destination
            })
            .ToListAsync(cancellationToken));

        rows.AddRange(await _db.LocationTags
            .AsNoTracking()
            .Where(locationTag => locationTag.Location.Destination.RegionId == regionId)
            .Select(locationTag => new TagScopeRow
            {
                TagId = locationTag.TagId,
                TagName = locationTag.Tag.Name,
                EntityScope = AnalyticsEntityTypes.Location
            })
            .ToListAsync(cancellationToken));

        rows.AddRange(await _db.PlaceTags
            .AsNoTracking()
            .Where(placeTag => placeTag.Place.Location.Destination.RegionId == regionId)
            .Select(placeTag => new TagScopeRow
            {
                TagId = placeTag.TagId,
                TagName = placeTag.Tag.Name,
                EntityScope = AnalyticsEntityTypes.Place
            })
            .ToListAsync(cancellationToken));

        rows.AddRange(await _db.EventTags
            .AsNoTracking()
            .Where(eventTag => eventTag.Event.Destination.RegionId == regionId)
            .Select(eventTag => new TagScopeRow
            {
                TagId = eventTag.TagId,
                TagName = eventTag.Tag.Name,
                EntityScope = AnalyticsEntityTypes.Event
            })
            .ToListAsync(cancellationToken));

        return rows
            .GroupBy(row => row.TagId)
            .Select(group => new RegionalTagViewsAvailableTagDto
            {
                TagId = group.Key,
                Name = group.Select(row => row.TagName).FirstOrDefault() ?? string.Empty,
                EntityScopes = group
                    .Select(row => row.EntityScope)
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(TagScopeOrder)
                    .ThenBy(scope => scope, StringComparer.Ordinal)
                    .ToList()
            })
            .OrderBy(tag => tag.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(tag => tag.TagId)
            .ToList();
    }

    private async Task<List<TagViewAggregateRow>> LoadTagViewAggregatesAsync(
        int regionId,
        string entityType,
        IReadOnlyCollection<int> selectedTagIds,
        DateTime? fromUtc,
        DateTime toUtc,
        CancellationToken cancellationToken)
    {
        var rows = new List<TagViewAggregateRow>();

        if (entityType is "all" or AnalyticsEntityTypes.Destination)
        {
            rows.AddRange(await LoadDestinationTagViewAggregatesAsync(regionId, selectedTagIds, fromUtc, toUtc, cancellationToken));
        }

        if (entityType is "all" or AnalyticsEntityTypes.Location)
        {
            rows.AddRange(await LoadLocationTagViewAggregatesAsync(regionId, selectedTagIds, fromUtc, toUtc, cancellationToken));
        }

        if (entityType is "all" or AnalyticsEntityTypes.Place)
        {
            rows.AddRange(await LoadPlaceTagViewAggregatesAsync(regionId, selectedTagIds, fromUtc, toUtc, cancellationToken));
        }

        if (entityType is "all" or AnalyticsEntityTypes.Event)
        {
            rows.AddRange(await LoadEventTagViewAggregatesAsync(regionId, selectedTagIds, fromUtc, toUtc, cancellationToken));
        }

        return rows;
    }

    private async Task<List<TagViewAggregateRow>> LoadDestinationTagViewAggregatesAsync(
        int regionId,
        IReadOnlyCollection<int> selectedTagIds,
        DateTime? fromUtc,
        DateTime toUtc,
        CancellationToken cancellationToken)
    {
        var query =
            from analyticsEvent in ApplyTagViewPeriod(_db.AnalyticsEvents.AsNoTracking(), fromUtc, toUtc)
            join destinationTag in _db.DestinationTags
                .AsNoTracking()
                .Where(destinationTag => destinationTag.Destination.RegionId == regionId)
                on analyticsEvent.EntityId!.Value equals destinationTag.DestinationId
            where analyticsEvent.EntityId.HasValue &&
                analyticsEvent.EntityType == AnalyticsEntityTypes.Destination &&
                ViewEventTypes.Contains(analyticsEvent.EventType)
            select new
            {
                destinationTag.TagId,
                TagName = destinationTag.Tag.Name
            };

        if (selectedTagIds.Count > 0)
        {
            query = query.Where(row => selectedTagIds.Contains(row.TagId));
        }

        return await query
            .GroupBy(row => new { row.TagId, row.TagName })
            .Select(group => new TagViewAggregateRow
            {
                TagId = group.Key.TagId,
                TagName = group.Key.TagName,
                EntityScope = AnalyticsEntityTypes.Destination,
                Views = group.Count()
            })
            .ToListAsync(cancellationToken);
    }

    private async Task<List<TagViewAggregateRow>> LoadLocationTagViewAggregatesAsync(
        int regionId,
        IReadOnlyCollection<int> selectedTagIds,
        DateTime? fromUtc,
        DateTime toUtc,
        CancellationToken cancellationToken)
    {
        var query =
            from analyticsEvent in ApplyTagViewPeriod(_db.AnalyticsEvents.AsNoTracking(), fromUtc, toUtc)
            join locationTag in _db.LocationTags
                .AsNoTracking()
                .Where(locationTag => locationTag.Location.Destination.RegionId == regionId)
                on analyticsEvent.EntityId!.Value equals locationTag.LocationId
            where analyticsEvent.EntityId.HasValue &&
                analyticsEvent.EntityType == AnalyticsEntityTypes.Location &&
                ViewEventTypes.Contains(analyticsEvent.EventType)
            select new
            {
                locationTag.TagId,
                TagName = locationTag.Tag.Name
            };

        if (selectedTagIds.Count > 0)
        {
            query = query.Where(row => selectedTagIds.Contains(row.TagId));
        }

        return await query
            .GroupBy(row => new { row.TagId, row.TagName })
            .Select(group => new TagViewAggregateRow
            {
                TagId = group.Key.TagId,
                TagName = group.Key.TagName,
                EntityScope = AnalyticsEntityTypes.Location,
                Views = group.Count()
            })
            .ToListAsync(cancellationToken);
    }

    private async Task<List<TagViewAggregateRow>> LoadPlaceTagViewAggregatesAsync(
        int regionId,
        IReadOnlyCollection<int> selectedTagIds,
        DateTime? fromUtc,
        DateTime toUtc,
        CancellationToken cancellationToken)
    {
        var query =
            from analyticsEvent in ApplyTagViewPeriod(_db.AnalyticsEvents.AsNoTracking(), fromUtc, toUtc)
            join placeTag in _db.PlaceTags
                .AsNoTracking()
                .Where(placeTag => placeTag.Place.Location.Destination.RegionId == regionId)
                on analyticsEvent.EntityId!.Value equals placeTag.PlaceId
            where analyticsEvent.EntityId.HasValue &&
                (analyticsEvent.EntityType == AnalyticsEntityTypes.Place ||
                 analyticsEvent.EntityType == AnalyticsEntityTypes.Hotel) &&
                ViewEventTypes.Contains(analyticsEvent.EventType)
            select new
            {
                placeTag.TagId,
                TagName = placeTag.Tag.Name
            };

        if (selectedTagIds.Count > 0)
        {
            query = query.Where(row => selectedTagIds.Contains(row.TagId));
        }

        return await query
            .GroupBy(row => new { row.TagId, row.TagName })
            .Select(group => new TagViewAggregateRow
            {
                TagId = group.Key.TagId,
                TagName = group.Key.TagName,
                EntityScope = AnalyticsEntityTypes.Place,
                Views = group.Count()
            })
            .ToListAsync(cancellationToken);
    }

    private async Task<List<TagViewAggregateRow>> LoadEventTagViewAggregatesAsync(
        int regionId,
        IReadOnlyCollection<int> selectedTagIds,
        DateTime? fromUtc,
        DateTime toUtc,
        CancellationToken cancellationToken)
    {
        var query =
            from analyticsEvent in ApplyTagViewPeriod(_db.AnalyticsEvents.AsNoTracking(), fromUtc, toUtc)
            join eventTag in _db.EventTags
                .AsNoTracking()
                .Where(eventTag => eventTag.Event.Destination.RegionId == regionId)
                on analyticsEvent.EntityId!.Value equals eventTag.EventId
            where analyticsEvent.EntityId.HasValue &&
                analyticsEvent.EntityType == AnalyticsEntityTypes.Event &&
                ViewEventTypes.Contains(analyticsEvent.EventType)
            select new
            {
                eventTag.TagId,
                TagName = eventTag.Tag.Name
            };

        if (selectedTagIds.Count > 0)
        {
            query = query.Where(row => selectedTagIds.Contains(row.TagId));
        }

        return await query
            .GroupBy(row => new { row.TagId, row.TagName })
            .Select(group => new TagViewAggregateRow
            {
                TagId = group.Key.TagId,
                TagName = group.Key.TagName,
                EntityScope = AnalyticsEntityTypes.Event,
                Views = group.Count()
            })
            .ToListAsync(cancellationToken);
    }

    private static RegionalTagViewsItemDto BuildTagViewsItem(
        RegionalTagViewsAvailableTagDto tag,
        int views,
        int totalViews)
    {
        return new RegionalTagViewsItemDto
        {
            TagId = tag.TagId,
            TagName = tag.Name,
            Views = views,
            PercentageOfTotal = totalViews <= 0
                ? 0
                : Math.Round((double)views / totalViews * 100, 1, MidpointRounding.AwayFromZero),
            EntityScopes = tag.EntityScopes.ToList()
        };
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
        IReadOnlyCollection<int> hotelIds,
        IReadOnlyCollection<int> eventIds,
        MetricAggregationMode aggregationMode,
        DateTime? fromUtc,
        DateTime toUtc,
        CancellationToken cancellationToken)
    {
        var byKey = rows.ToDictionary(row => new EntityKey(row.EntityType, row.EntityId));

        if (aggregationMode == MetricAggregationMode.DestinationRollup)
        {
            ApplySaveRollupRows(
                byKey,
                await LoadDestinationRollupSaveRowsAsync(destinationIds, fromUtc, toUtc, cancellationToken));
            return;
        }

        if (aggregationMode == MetricAggregationMode.LocationRollup)
        {
            ApplySaveRollupRows(
                byKey,
                await LoadLocationRollupSaveRowsAsync(locationIds, fromUtc, toUtc, cancellationToken));
            return;
        }

        var favoriteQuery = ApplyCreatedPeriod(_db.Favorites.AsNoTracking(), fromUtc, toUtc)
            .Where(favorite =>
                (favorite.EntityType == AnalyticsEntityTypes.Place && (placeIds.Contains(favorite.EntityId) || hotelIds.Contains(favorite.EntityId))) ||
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

        foreach (var group in favoriteRows
                     .Where(row => row.EntityType == AnalyticsEntityTypes.Place && hotelIds.Contains(row.EntityId))
                     .GroupBy(row => new EntityKey(AnalyticsEntityTypes.Hotel, row.EntityId)))
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

        var hotelCollectionSaves = await ApplyCreatedPeriod(_db.CollectionItems.AsNoTracking(), fromUtc, toUtc)
            .Where(item => item.PlaceId.HasValue && hotelIds.Contains(item.PlaceId.Value))
            .Select(item => new { EntityId = item.PlaceId!.Value, item.Collection.UserId })
            .Distinct()
            .ToListAsync(cancellationToken);

        foreach (var group in hotelCollectionSaves.GroupBy(row => new EntityKey(AnalyticsEntityTypes.Hotel, row.EntityId)))
        {
            if (byKey.TryGetValue(group.Key, out var metric))
            {
                metric.Saves = group.Count();
            }
        }
    }

    private async Task<List<SaveMetricRow>> LoadDestinationRollupSaveRowsAsync(
        IReadOnlyCollection<int> destinationIds,
        DateTime? fromUtc,
        DateTime toUtc,
        CancellationToken cancellationToken)
    {
        var rows = new List<SaveMetricRow>();

        rows.AddRange(await ApplyCreatedPeriod(_db.CollectionItems.AsNoTracking(), fromUtc, toUtc)
            .Where(item => item.DestinationId.HasValue && destinationIds.Contains(item.DestinationId.Value))
            .Select(item => new SaveMetricRow(
                AnalyticsEntityTypes.Destination,
                item.DestinationId!.Value,
                AnalyticsEntityTypes.Destination,
                item.DestinationId!.Value,
                item.Collection.UserId))
            .ToListAsync(cancellationToken));

        rows.AddRange(await ApplyCreatedPeriod(_db.CollectionItems.AsNoTracking(), fromUtc, toUtc)
            .Where(item => item.LocationId.HasValue && destinationIds.Contains(item.Location!.DestinationId))
            .Select(item => new SaveMetricRow(
                AnalyticsEntityTypes.Destination,
                item.Location!.DestinationId,
                AnalyticsEntityTypes.Location,
                item.LocationId!.Value,
                item.Collection.UserId))
            .ToListAsync(cancellationToken));

        rows.AddRange(await (
            from favorite in ApplyCreatedPeriod(_db.Favorites.AsNoTracking(), fromUtc, toUtc)
            join place in _db.Places
                .AsNoTracking()
                .Where(place => destinationIds.Contains(place.Location.DestinationId))
                on favorite.EntityId equals place.Id
            where favorite.EntityType == AnalyticsEntityTypes.Place
            select new SaveMetricRow(
                AnalyticsEntityTypes.Destination,
                place.Location.DestinationId,
                AnalyticsEntityTypes.Place,
                place.Id,
                favorite.UserId))
            .ToListAsync(cancellationToken));

        rows.AddRange(await (
            from item in ApplyCreatedPeriod(_db.CollectionItems.AsNoTracking(), fromUtc, toUtc)
            join place in _db.Places
                .AsNoTracking()
                .Where(place => destinationIds.Contains(place.Location.DestinationId))
                on item.PlaceId!.Value equals place.Id
            where item.PlaceId.HasValue
            select new SaveMetricRow(
                AnalyticsEntityTypes.Destination,
                place.Location.DestinationId,
                AnalyticsEntityTypes.Place,
                place.Id,
                item.Collection.UserId))
            .ToListAsync(cancellationToken));

        rows.AddRange(await (
            from favorite in ApplyCreatedPeriod(_db.Favorites.AsNoTracking(), fromUtc, toUtc)
            join eventEntity in _db.Events
                .AsNoTracking()
                .Where(eventEntity => destinationIds.Contains(eventEntity.DestinationId))
                on favorite.EntityId equals eventEntity.Id
            where favorite.EntityType == AnalyticsEntityTypes.Event
            select new SaveMetricRow(
                AnalyticsEntityTypes.Destination,
                eventEntity.DestinationId,
                AnalyticsEntityTypes.Event,
                eventEntity.Id,
                favorite.UserId))
            .ToListAsync(cancellationToken));

        rows.AddRange(await (
            from item in ApplyCreatedPeriod(_db.CollectionItems.AsNoTracking(), fromUtc, toUtc)
            join eventEntity in _db.Events
                .AsNoTracking()
                .Where(eventEntity => destinationIds.Contains(eventEntity.DestinationId))
                on item.EventId!.Value equals eventEntity.Id
            where item.EventId.HasValue
            select new SaveMetricRow(
                AnalyticsEntityTypes.Destination,
                eventEntity.DestinationId,
                AnalyticsEntityTypes.Event,
                eventEntity.Id,
                item.Collection.UserId))
            .ToListAsync(cancellationToken));

        return rows.Distinct().ToList();
    }

    private async Task<List<SaveMetricRow>> LoadLocationRollupSaveRowsAsync(
        IReadOnlyCollection<int> locationIds,
        DateTime? fromUtc,
        DateTime toUtc,
        CancellationToken cancellationToken)
    {
        var rows = new List<SaveMetricRow>();

        rows.AddRange(await ApplyCreatedPeriod(_db.CollectionItems.AsNoTracking(), fromUtc, toUtc)
            .Where(item => item.LocationId.HasValue && locationIds.Contains(item.LocationId.Value))
            .Select(item => new SaveMetricRow(
                AnalyticsEntityTypes.Location,
                item.LocationId!.Value,
                AnalyticsEntityTypes.Location,
                item.LocationId!.Value,
                item.Collection.UserId))
            .ToListAsync(cancellationToken));

        rows.AddRange(await (
            from favorite in ApplyCreatedPeriod(_db.Favorites.AsNoTracking(), fromUtc, toUtc)
            join place in _db.Places
                .AsNoTracking()
                .Where(place => locationIds.Contains(place.LocationId))
                on favorite.EntityId equals place.Id
            where favorite.EntityType == AnalyticsEntityTypes.Place
            select new SaveMetricRow(
                AnalyticsEntityTypes.Location,
                place.LocationId,
                AnalyticsEntityTypes.Place,
                place.Id,
                favorite.UserId))
            .ToListAsync(cancellationToken));

        rows.AddRange(await (
            from item in ApplyCreatedPeriod(_db.CollectionItems.AsNoTracking(), fromUtc, toUtc)
            join place in _db.Places
                .AsNoTracking()
                .Where(place => locationIds.Contains(place.LocationId))
                on item.PlaceId!.Value equals place.Id
            where item.PlaceId.HasValue
            select new SaveMetricRow(
                AnalyticsEntityTypes.Location,
                place.LocationId,
                AnalyticsEntityTypes.Place,
                place.Id,
                item.Collection.UserId))
            .ToListAsync(cancellationToken));

        rows.AddRange(await (
            from favorite in ApplyCreatedPeriod(_db.Favorites.AsNoTracking(), fromUtc, toUtc)
            join eventEntity in _db.Events
                .AsNoTracking()
                .Where(eventEntity => eventEntity.LocationId.HasValue && locationIds.Contains(eventEntity.LocationId.Value))
                on favorite.EntityId equals eventEntity.Id
            where favorite.EntityType == AnalyticsEntityTypes.Event
            select new SaveMetricRow(
                AnalyticsEntityTypes.Location,
                eventEntity.LocationId!.Value,
                AnalyticsEntityTypes.Event,
                eventEntity.Id,
                favorite.UserId))
            .ToListAsync(cancellationToken));

        rows.AddRange(await (
            from item in ApplyCreatedPeriod(_db.CollectionItems.AsNoTracking(), fromUtc, toUtc)
            join eventEntity in _db.Events
                .AsNoTracking()
                .Where(eventEntity => eventEntity.LocationId.HasValue && locationIds.Contains(eventEntity.LocationId.Value))
                on item.EventId!.Value equals eventEntity.Id
            where item.EventId.HasValue
            select new SaveMetricRow(
                AnalyticsEntityTypes.Location,
                eventEntity.LocationId!.Value,
                AnalyticsEntityTypes.Event,
                eventEntity.Id,
                item.Collection.UserId))
            .ToListAsync(cancellationToken));

        rows.AddRange(await (
            from favorite in ApplyCreatedPeriod(_db.Favorites.AsNoTracking(), fromUtc, toUtc)
            join eventEntity in _db.Events
                .AsNoTracking()
                .Where(eventEntity => !eventEntity.LocationId.HasValue && eventEntity.PlaceId.HasValue)
                on favorite.EntityId equals eventEntity.Id
            join place in _db.Places
                .AsNoTracking()
                .Where(place => locationIds.Contains(place.LocationId))
                on eventEntity.PlaceId!.Value equals place.Id
            where favorite.EntityType == AnalyticsEntityTypes.Event
            select new SaveMetricRow(
                AnalyticsEntityTypes.Location,
                place.LocationId,
                AnalyticsEntityTypes.Event,
                eventEntity.Id,
                favorite.UserId))
            .ToListAsync(cancellationToken));

        rows.AddRange(await (
            from item in ApplyCreatedPeriod(_db.CollectionItems.AsNoTracking(), fromUtc, toUtc)
            join eventEntity in _db.Events
                .AsNoTracking()
                .Where(eventEntity => !eventEntity.LocationId.HasValue && eventEntity.PlaceId.HasValue)
                on item.EventId!.Value equals eventEntity.Id
            join place in _db.Places
                .AsNoTracking()
                .Where(place => locationIds.Contains(place.LocationId))
                on eventEntity.PlaceId!.Value equals place.Id
            where item.EventId.HasValue
            select new SaveMetricRow(
                AnalyticsEntityTypes.Location,
                place.LocationId,
                AnalyticsEntityTypes.Event,
                eventEntity.Id,
                item.Collection.UserId))
            .ToListAsync(cancellationToken));

        return rows.Distinct().ToList();
    }

    private static void ApplySaveRollupRows(
        IReadOnlyDictionary<EntityKey, RegionalAnalyticsEntityMetricDto> rows,
        IEnumerable<SaveMetricRow> saveRows)
    {
        foreach (var group in saveRows
                     .Distinct()
                     .GroupBy(row => new EntityKey(row.ParentEntityType, row.ParentEntityId)))
        {
            if (rows.TryGetValue(group.Key, out var metric))
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
        IReadOnlyCollection<int> hotelIds,
        IReadOnlyCollection<int> eventIds,
        MetricAggregationMode aggregationMode,
        DateTime? fromUtc,
        DateTime toUtc,
        CancellationToken cancellationToken)
    {
        var byKey = rows.ToDictionary(row => new EntityKey(row.EntityType, row.EntityId));

        if (aggregationMode == MetricAggregationMode.DestinationRollup)
        {
            ApplyReviewRollupRows(
                byKey,
                await LoadDestinationRollupReviewRowsAsync(destinationIds, fromUtc, toUtc, cancellationToken));
            return;
        }

        if (aggregationMode == MetricAggregationMode.LocationRollup)
        {
            ApplyReviewRollupRows(
                byKey,
                await LoadLocationRollupReviewRowsAsync(locationIds, fromUtc, toUtc, cancellationToken));
            return;
        }

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

        var hotelReviews = await ApplyCreatedPeriod(_db.PlaceReviews.AsNoTracking(), fromUtc, toUtc)
            .Where(review => hotelIds.Contains(review.PlaceId))
            .GroupBy(review => review.PlaceId)
            .Select(group => new { EntityId = group.Key, Count = group.Count(), Average = group.Average(review => review.Rating) })
            .ToListAsync(cancellationToken);

        foreach (var row in hotelReviews)
        {
            ApplyReviewAggregate(byKey, AnalyticsEntityTypes.Hotel, row.EntityId, row.Count, row.Average);
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

    private async Task<List<ReviewMetricRow>> LoadDestinationRollupReviewRowsAsync(
        IReadOnlyCollection<int> destinationIds,
        DateTime? fromUtc,
        DateTime toUtc,
        CancellationToken cancellationToken)
    {
        var rows = new List<ReviewMetricRow>();

        rows.AddRange(await ApplyCreatedPeriod(_db.DestinationReviews.AsNoTracking(), fromUtc, toUtc)
            .Where(review => destinationIds.Contains(review.DestinationId))
            .GroupBy(review => review.DestinationId)
            .Select(group => new ReviewMetricRow(
                AnalyticsEntityTypes.Destination,
                group.Key,
                group.Count(),
                group.Average(review => review.Rating)))
            .ToListAsync(cancellationToken));

        rows.AddRange(await ApplyCreatedPeriod(_db.LocationReviews.AsNoTracking(), fromUtc, toUtc)
            .Where(review => destinationIds.Contains(review.Location.DestinationId))
            .GroupBy(review => review.Location.DestinationId)
            .Select(group => new ReviewMetricRow(
                AnalyticsEntityTypes.Destination,
                group.Key,
                group.Count(),
                group.Average(review => review.Rating)))
            .ToListAsync(cancellationToken));

        rows.AddRange(await ApplyCreatedPeriod(_db.PlaceReviews.AsNoTracking(), fromUtc, toUtc)
            .Where(review => destinationIds.Contains(review.Place.Location.DestinationId))
            .GroupBy(review => review.Place.Location.DestinationId)
            .Select(group => new ReviewMetricRow(
                AnalyticsEntityTypes.Destination,
                group.Key,
                group.Count(),
                group.Average(review => review.Rating)))
            .ToListAsync(cancellationToken));

        rows.AddRange(await ApplyCreatedPeriod(_db.EventReviews.AsNoTracking(), fromUtc, toUtc)
            .Where(review => destinationIds.Contains(review.Event.DestinationId))
            .GroupBy(review => review.Event.DestinationId)
            .Select(group => new ReviewMetricRow(
                AnalyticsEntityTypes.Destination,
                group.Key,
                group.Count(),
                group.Average(review => review.Rating)))
            .ToListAsync(cancellationToken));

        return rows;
    }

    private async Task<List<ReviewMetricRow>> LoadLocationRollupReviewRowsAsync(
        IReadOnlyCollection<int> locationIds,
        DateTime? fromUtc,
        DateTime toUtc,
        CancellationToken cancellationToken)
    {
        var rows = new List<ReviewMetricRow>();

        rows.AddRange(await ApplyCreatedPeriod(_db.LocationReviews.AsNoTracking(), fromUtc, toUtc)
            .Where(review => locationIds.Contains(review.LocationId))
            .GroupBy(review => review.LocationId)
            .Select(group => new ReviewMetricRow(
                AnalyticsEntityTypes.Location,
                group.Key,
                group.Count(),
                group.Average(review => review.Rating)))
            .ToListAsync(cancellationToken));

        rows.AddRange(await ApplyCreatedPeriod(_db.PlaceReviews.AsNoTracking(), fromUtc, toUtc)
            .Where(review => locationIds.Contains(review.Place.LocationId))
            .GroupBy(review => review.Place.LocationId)
            .Select(group => new ReviewMetricRow(
                AnalyticsEntityTypes.Location,
                group.Key,
                group.Count(),
                group.Average(review => review.Rating)))
            .ToListAsync(cancellationToken));

        rows.AddRange(await ApplyCreatedPeriod(_db.EventReviews.AsNoTracking(), fromUtc, toUtc)
            .Where(review => review.Event.LocationId.HasValue && locationIds.Contains(review.Event.LocationId.Value))
            .GroupBy(review => review.Event.LocationId!.Value)
            .Select(group => new ReviewMetricRow(
                AnalyticsEntityTypes.Location,
                group.Key,
                group.Count(),
                group.Average(review => review.Rating)))
            .ToListAsync(cancellationToken));

        rows.AddRange(await ApplyCreatedPeriod(_db.EventReviews.AsNoTracking(), fromUtc, toUtc)
            .Where(review =>
                !review.Event.LocationId.HasValue &&
                review.Event.PlaceId.HasValue &&
                locationIds.Contains(review.Event.Place!.LocationId))
            .GroupBy(review => review.Event.Place!.LocationId)
            .Select(group => new ReviewMetricRow(
                AnalyticsEntityTypes.Location,
                group.Key,
                group.Count(),
                group.Average(review => review.Rating)))
            .ToListAsync(cancellationToken));

        return rows;
    }

    private static void ApplyReviewRollupRows(
        IReadOnlyDictionary<EntityKey, RegionalAnalyticsEntityMetricDto> rows,
        IEnumerable<ReviewMetricRow> reviewRows)
    {
        foreach (var group in reviewRows.GroupBy(row => new EntityKey(row.ParentEntityType, row.ParentEntityId)))
        {
            var count = group.Sum(row => row.Count);
            if (count <= 0 || !rows.TryGetValue(group.Key, out var metric))
            {
                continue;
            }

            var totalRating = group.Sum(row => row.Average * row.Count);
            metric.ReviewCount = count;
            metric.AverageRating = Math.Round(totalRating / count, 2, MidpointRounding.AwayFromZero);
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

    private static PeriodWindow ResolveTagViewsPeriod(string? value)
    {
        var key = NormalizeToken(value ?? "1m");
        var now = DateTime.UtcNow;

        return key switch
        {
            "24h" or "last24h" => new PeriodWindow("24h", now.AddHours(-24), now, TimeSpan.FromHours(24)),
            "7d" or "last7days" => new PeriodWindow("7d", now.AddDays(-7), now, TimeSpan.FromDays(7)),
            "1m" or "30d" or "lastmonth" or "last30days" => new PeriodWindow("1m", now.AddMonths(-1), now, now - now.AddMonths(-1)),
            "3m" or "90d" or "last3months" or "last90days" => new PeriodWindow("3m", now.AddMonths(-3), now, now - now.AddMonths(-3)),
            "6m" or "last6months" => new PeriodWindow("6m", now.AddMonths(-6), now, now - now.AddMonths(-6)),
            "1y" or "12m" or "lastyear" => new PeriodWindow("1y", now.AddYears(-1), now, now - now.AddYears(-1)),
            _ => throw new InvalidOperationException("Invalid period filter.")
        };
    }

    private static List<int> NormalizeSelectedTagIds(IEnumerable<int>? tagIds)
    {
        var normalized = (tagIds ?? Enumerable.Empty<int>())
            .Distinct()
            .ToList();

        if (normalized.Any(tagId => tagId <= 0))
        {
            throw new InvalidOperationException("tagIds must contain positive integer values.");
        }

        if (normalized.Count > MaxSelectedTagIds)
        {
            throw new InvalidOperationException("Too many tagIds were selected.");
        }

        return normalized;
    }

    private static string NormalizeEntityTypeFilter(string? value)
    {
        var normalized = NormalizeToken(value ?? "all");
        return normalized switch
        {
            "" or "all" or "allentities" => "all",
            "destination" or "destinations" => AnalyticsEntityTypes.Destination,
            "location" or "locations" => AnalyticsEntityTypes.Location,
            "place" or "places" => AnalyticsEntityTypes.Place,
            "hotel" or "hotels" or "stay" or "stays" => AnalyticsEntityTypes.Hotel,
            "event" or "events" => AnalyticsEntityTypes.Event,
            _ => throw new InvalidOperationException("Invalid entity type filter.")
        };
    }

    private static string NormalizeEntityTypeForDetail(string? value)
    {
        var normalized = NormalizeToken(value ?? string.Empty);
        return normalized switch
        {
            "destination" or "destinations" => AnalyticsEntityTypes.Destination,
            "location" or "locations" => AnalyticsEntityTypes.Location,
            "place" or "places" => AnalyticsEntityTypes.Place,
            "hotel" or "hotels" or "stay" or "stays" => AnalyticsEntityTypes.Hotel,
            "event" or "events" => AnalyticsEntityTypes.Event,
            _ => throw new InvalidOperationException("Invalid entity type.")
        };
    }

    private static MetricAggregationMode ResolveAggregationMode(string entityType) =>
        entityType switch
        {
            AnalyticsEntityTypes.Destination => MetricAggregationMode.DestinationRollup,
            AnalyticsEntityTypes.Location => MetricAggregationMode.LocationRollup,
            _ => MetricAggregationMode.Direct
        };

    private static string? DetailCategoryLabel(RegionalAnalyticsEntityDetailDto detail) =>
        detail.EntityType switch
        {
            AnalyticsEntityTypes.Destination => detail.TypeSpecific.DestinationType,
            AnalyticsEntityTypes.Location => detail.TypeSpecific.LocationType,
            AnalyticsEntityTypes.Hotel => detail.TypeSpecific.StayType ?? detail.TypeSpecific.PlaceType,
            AnalyticsEntityTypes.Place => detail.TypeSpecific.PlaceType,
            AnalyticsEntityTypes.Event => detail.TypeSpecific.EventType,
            _ => null
        };

    private static string? DetailRoute(string entityType, int entityId) =>
        entityType switch
        {
            AnalyticsEntityTypes.Destination => "/destinations/detail/" + entityId,
            AnalyticsEntityTypes.Location => "/locations/detail/" + entityId,
            AnalyticsEntityTypes.Hotel => "/stays/detail/" + entityId,
            AnalyticsEntityTypes.Place => "/places/detail/" + entityId,
            AnalyticsEntityTypes.Event => "/events/detail/" + entityId,
            _ => null
        };

    private static void EnsureEntityInRegion(int entityRegionId, int requestedRegionId, string entityLabel)
    {
        if (entityRegionId != requestedRegionId)
        {
            throw new UnauthorizedAccessException(entityLabel + " is not in the active assigned region.");
        }
    }

    private static List<string> NormalizeGallery(IEnumerable<string?> imageUrls) =>
        imageUrls
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToList();

    private static string NormalizeStatusLabel(string? status) =>
        string.IsNullOrWhiteSpace(status) ? "Published" : status.Trim();

    private static string? NullIfWhiteSpace(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? BuildSummary(string? description)
    {
        if (string.IsNullOrWhiteSpace(description))
            return null;

        var normalized = string.Join(" ", description.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return normalized.Length <= 160 ? normalized : normalized[..157] + "...";
    }

    private static List<RegionalPlaceOperatingHourDto> DeserializeOperatingHours(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return new List<RegionalPlaceOperatingHourDto>();

        try
        {
            return JsonSerializer.Deserialize<List<RegionalPlaceOperatingHourDto>>(
                    json,
                    new JsonSerializerOptions(JsonSerializerDefaults.Web))
                ?? new List<RegionalPlaceOperatingHourDto>();
        }
        catch (JsonException)
        {
            return new List<RegionalPlaceOperatingHourDto>();
        }
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

    private static IQueryable<AnalyticsEvent> ApplyTagViewPeriod(
        IQueryable<AnalyticsEvent> query,
        DateTime? fromUtc,
        DateTime toUtc)
    {
        return ApplyAnalyticsPeriod(query, fromUtc, toUtc)
            .Where(item => item.EntityId.HasValue && ViewEventTypes.Contains(item.EventType));
    }

    private static int TagScopeOrder(string scope) =>
        scope switch
        {
            AnalyticsEntityTypes.Destination => 0,
            AnalyticsEntityTypes.Location => 1,
            AnalyticsEntityTypes.Place => 2,
            AnalyticsEntityTypes.Hotel => 3,
            AnalyticsEntityTypes.Event => 4,
            _ => 5
        };

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
            AnalyticsEntityTypes.Hotel => "Stays",
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

    private enum MetricAggregationMode
    {
        Direct,
        DestinationRollup,
        LocationRollup
    }

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

    private sealed class TagScopeRow
    {
        public int TagId { get; set; }
        public string TagName { get; set; } = string.Empty;
        public string EntityScope { get; set; } = string.Empty;
    }

    private sealed class TagViewAggregateRow
    {
        public int TagId { get; set; }
        public string TagName { get; set; } = string.Empty;
        public string EntityScope { get; set; } = string.Empty;
        public int Views { get; set; }
    }

    private sealed record ExternalPartnerDefinition(
        int Id,
        string PartnerKey,
        string PartnerLabel,
        string? Url,
        string ProviderSlug,
        string? Host,
        int SortOrder);

    private sealed record PartnerClickMetadata(string? Provider, string? PartnerKey, string? Host);

    private sealed record SaveMetricRow(
        string ParentEntityType,
        int ParentEntityId,
        string SavedEntityType,
        int SavedEntityId,
        int UserId);

    private sealed record ReviewMetricRow(
        string ParentEntityType,
        int ParentEntityId,
        int Count,
        double Average);

    private sealed class ExternalPartnerClickEventRow
    {
        public string MetadataJson { get; set; } = "{}";
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
