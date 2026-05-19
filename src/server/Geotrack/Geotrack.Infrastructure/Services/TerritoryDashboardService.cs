using Geotrack.Application.Analytics;
using Geotrack.Application.DTOs.Admin;
using Geotrack.Application.Interfaces.Service;
using Geotrack.Application.Security;
using Geotrack.Domain.Entities.Core;
using Geotrack.Domain.Entities.Reviews;
using Geotrack.Domain.Entities.Users;
using Microsoft.EntityFrameworkCore;

namespace Geotrack.Infrastructure.Services;

public sealed class TerritoryDashboardService : ITerritoryDashboardService
{
    private const int DefaultTopTake = 3;
    private const int DetailPreviewTake = 5;
    private const int DirectoryMaxTake = 100;
    private const string PublishedStatus = "Published";

    private static readonly string[] VisitEventTypes =
    {
        AnalyticsEventTypes.PageView,
        AnalyticsEventTypes.DetailView
    };

    private static readonly string[] SaveEventTypes =
    {
        AnalyticsEventTypes.FavoriteAdded,
        AnalyticsEventTypes.CollectionItemAdded
    };

    private static readonly string[] ExternalActionEventTypes =
    {
        AnalyticsEventTypes.OutboundLinkClicked,
        AnalyticsEventTypes.DirectionsStarted
    };

    private readonly AppDbContext _db;

    public TerritoryDashboardService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<List<TerritoryAssignedTerritoryDto>> GetAssignedTerritoriesAsync(int userId)
    {
        await EnsureTerritoryManagerRoleAsync(userId);

        return await _db.TerritoryManagerTerritories
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
            .ToListAsync();
    }

    public async Task<TerritorySidebarSummaryDto> GetSidebarSummaryAsync(int userId, int? territoryId)
    {
        var assignedTerritories = await GetAssignedTerritoriesAsync(userId);
        var activeTerritory = ResolveActiveTerritory(assignedTerritories, territoryId);

        return new TerritorySidebarSummaryDto
        {
            AssignedTerritories = assignedTerritories,
            ActiveTerritory = activeTerritory
        };
    }

    public async Task<TerritoryDashboardDto> GetDashboardAsync(int userId, int? territoryId)
    {
        var activeTerritory = await GetAuthorizedTerritoryAsync(userId, territoryId);
        if (activeTerritory is null)
        {
            return new TerritoryDashboardDto();
        }

        var now = DateTime.UtcNow;
        var currentPeriodStart = now.AddDays(-30);
        var previousPeriodStart = currentPeriodStart.AddDays(-30);

        var currentHits = await GetTerritoryAnalyticsHitsAsync(
            activeTerritory.Id,
            currentPeriodStart,
            now,
            visitOnly: true);
        var previousHits = await GetTerritoryAnalyticsHitsAsync(
            activeTerritory.Id,
            previousPeriodStart,
            currentPeriodStart,
            visitOnly: true);

        var currentRegionVisitCounts = currentHits
            .GroupBy(hit => hit.RegionId)
            .ToDictionary(group => group.Key, group => group.Count());
        var previousRegionVisitCounts = previousHits
            .GroupBy(hit => hit.RegionId)
            .ToDictionary(group => group.Key, group => group.Count());

        var currentActiveUsers = await CountActiveUsersAsync(
            activeTerritory.Id,
            currentPeriodStart,
            now);
        var previousActiveUsers = await CountActiveUsersAsync(
            activeTerritory.Id,
            previousPeriodStart,
            currentPeriodStart);

        return new TerritoryDashboardDto
        {
            ActiveTerritory = activeTerritory,
            Stats = await BuildStatsAsync(
                activeTerritory.Id,
                currentPeriodStart,
                currentActiveUsers,
                previousActiveUsers),
            RegionsHealth = await BuildRegionsHealthAsync(activeTerritory.Id, currentRegionVisitCounts),
            TopPerformingRegions = await BuildTopPerformingRegionsAsync(
                activeTerritory.Id,
                currentRegionVisitCounts,
                previousRegionVisitCounts),
            TopContent = await BuildTopContentAsync(activeTerritory.Id, currentPeriodStart, now)
        };
    }

    public async Task<TerritoryRegionDirectoryResponseDto> GetRegionsAsync(
        int userId,
        int? territoryId,
        string? search,
        string? sort,
        int skip,
        int take)
    {
        var activeTerritory = await GetAuthorizedTerritoryAsync(userId, territoryId);
        skip = Math.Max(0, skip);
        take = Math.Clamp(take, 1, DirectoryMaxTake);

        if (activeTerritory is null)
        {
            return new TerritoryRegionDirectoryResponseDto
            {
                Territory = null,
                Skip = skip,
                Take = take
            };
        }

        var query = _db.Regions
            .AsNoTracking()
            .Where(region => region.TerritoryId == activeTerritory.Id);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var normalizedSearch = search.Trim().ToLowerInvariant();
            query = query.Where(region =>
                region.Name.ToLower().Contains(normalizedSearch) ||
                region.Territory.Name.ToLower().Contains(normalizedSearch) ||
                region.RegionalManagerRegions.Any(assignment =>
                    assignment.User.Email.ToLower().Contains(normalizedSearch) ||
                    (assignment.User.DisplayName ?? string.Empty).ToLower().Contains(normalizedSearch) ||
                    assignment.User.FirstName.ToLower().Contains(normalizedSearch) ||
                    assignment.User.LastName.ToLower().Contains(normalizedSearch)));
        }

        var rows = await query
            .Select(region => new RegionBaseRow
            {
                Id = region.Id,
                Name = region.Name,
                TerritoryId = region.TerritoryId,
                TerritoryName = region.Territory.Name,
                CreatedAt = region.CreatedAt,
                UpdatedAt = region.UpdatedAt,
                ImageUrl = region.Images
                    .Where(image => image.TargetType == EntityImageTargetType.Region)
                    .OrderByDescending(image => image.IsMain)
                    .ThenBy(image => image.SortOrder)
                    .ThenBy(image => image.CreatedAt)
                    .ThenBy(image => image.Id)
                    .Select(image => image.Url)
                    .FirstOrDefault()
            })
            .ToListAsync();

        var regionIds = rows.Select(row => row.Id).ToList();
        var contentCounts = await GetRegionContentCountsAsync(regionIds);
        var pendingCounts = await GetRegionPendingRequestCountsAsync(regionIds);
        var managers = await GetRegionManagersAsync(regionIds);
        var quality = await GetRegionQualityAggregatesAsync(regionIds);

        var now = DateTime.UtcNow;
        var periodStart = now.AddDays(-30);
        var hits = await GetTerritoryAnalyticsHitsAsync(activeTerritory.Id, periodStart, now, visitOnly: false);
        var visitCounts = hits
            .Where(hit => VisitEventTypes.Contains(hit.EventType))
            .GroupBy(hit => hit.RegionId)
            .ToDictionary(group => group.Key, group => group.Count());
        var saveCounts = hits
            .Where(hit => SaveEventTypes.Contains(hit.EventType))
            .GroupBy(hit => hit.RegionId)
            .ToDictionary(group => group.Key, group => group.Count());
        var reviewCounts = await GetRegionReviewCountsAsync(regionIds);

        var maxVisits = Math.Max(1, visitCounts.Values.DefaultIfEmpty(0).Max());
        var maxContent = Math.Max(1, contentCounts.Values
            .Select(counts => counts.Destinations + counts.Locations + counts.Places + counts.Stays + counts.Events)
            .DefaultIfEmpty(0)
            .Max());

        var items = rows.Select(row =>
        {
            var counts = contentCounts.GetValueOrDefault(row.Id, RegionContentCounts.Empty);
            var managerRows = managers.GetValueOrDefault(row.Id, new List<ManagerRow>());
            var pendingRequests = pendingCounts.GetValueOrDefault(row.Id);
            var visits = visitCounts.GetValueOrDefault(row.Id);
            var regionQuality = quality.GetValueOrDefault(row.Id, RegionQualityAggregate.Empty);
            var regionReviews = reviewCounts.GetValueOrDefault(row.Id) ?? new RegionReviewAggregate();
            var health = CalculateRegionHealth(
                counts,
                regionQuality,
                visits,
                saveCounts.GetValueOrDefault(row.Id),
                regionReviews.Count,
                pendingRequests);

            var status = managerRows.Count > 0 ? "Active" : "Inactive";

            return new TerritoryRegionDirectoryItemDto
            {
                Id = row.Id,
                RegionId = row.Id,
                Name = row.Name,
                RegionName = row.Name,
                DisplayName = row.Name,
                TerritoryName = row.TerritoryName,
                TerritoryId = row.TerritoryId,
                TerritoryDisplayName = row.TerritoryName,
                Status = status,
                CreatedAt = row.CreatedAt,
                DestinationsCount = counts.Destinations,
                LocationsCount = counts.Locations,
                PlacesCount = counts.Places,
                StaysCount = counts.Stays,
                EventsCount = counts.Events,
                PendingRequestsCount = pendingRequests,
                AssignedPersonsCount = managerRows.Count,
                ActiveManagersCount = managerRows.Count,
                ManagerDisplayName = BuildManagerDisplayName(managerRows),
                ManagerNames = managerRows.Select(manager => manager.DisplayName).ToList(),
                AssignedManagers = managerRows.Select(BuildManagerDto).ToList(),
                HealthScore = health.Score,
                ActivityScore = CalculateActivityScore(
                    visits,
                    maxVisits,
                    counts.Destinations + counts.Locations + counts.Places + counts.Stays + counts.Events,
                    maxContent,
                    pendingRequests),
                ImageUrl = row.ImageUrl,
                DetailRoute = $"/territory/regions/{row.Id}"
            };
        });

        items = NormalizeRegionSort(sort) switch
        {
            "status-active-first" => items
                .OrderBy(item => item.Status == "Active" ? 0 : 1)
                .ThenBy(item => item.Name)
                .ThenBy(item => item.Id),
            "newest" => items.OrderByDescending(item => item.CreatedAt).ThenBy(item => item.Name),
            "oldest" => items.OrderBy(item => item.CreatedAt).ThenBy(item => item.Name),
            "content" => items
                .OrderByDescending(item =>
                    item.DestinationsCount + item.LocationsCount + item.PlacesCount + item.StaysCount + item.EventsCount)
                .ThenBy(item => item.Name),
            "most-destinations" => items.OrderByDescending(item => item.DestinationsCount).ThenBy(item => item.Name),
            "most-locations" => items.OrderByDescending(item => item.LocationsCount).ThenBy(item => item.Name),
            "most-places" => items.OrderByDescending(item => item.PlacesCount).ThenBy(item => item.Name),
            "most-events" => items.OrderByDescending(item => item.EventsCount).ThenBy(item => item.Name),
            "most-pending-requests" => items.OrderByDescending(item => item.PendingRequestsCount).ThenBy(item => item.Name),
            "health-high" => items.OrderByDescending(item => item.HealthScore ?? -1).ThenBy(item => item.Name),
            "health-low" => items.OrderBy(item => item.HealthScore ?? 101).ThenBy(item => item.Name),
            "activity-high" => items.OrderByDescending(item => item.ActivityScore).ThenBy(item => item.Name),
            "activity-low" => items.OrderBy(item => item.ActivityScore).ThenBy(item => item.Name),
            "name-desc" => items.OrderByDescending(item => item.Name).ThenByDescending(item => item.Id),
            _ => items.OrderBy(item => item.Name).ThenBy(item => item.Id)
        };

        var ordered = items.ToList();

        return new TerritoryRegionDirectoryResponseDto
        {
            Territory = activeTerritory,
            Items = ordered.Skip(skip).Take(take).ToList(),
            TotalCount = ordered.Count,
            Skip = skip,
            Take = take,
            HasMore = skip + take < ordered.Count
        };
    }

    public async Task<TerritoryRegionDetailDto> GetRegionDetailAsync(
        int userId,
        int regionId,
        int? territoryId,
        string? period)
    {
        var scope = await GetAuthorizedRegionAsync(userId, regionId, territoryId);
        var periodRange = ResolveRegionPeriod(period);
        var regionIds = new List<int> { scope.RegionId };

        var contentCounts = await GetRegionContentCountsAsync(regionIds);
        var counts = contentCounts.GetValueOrDefault(scope.RegionId, RegionContentCounts.Empty);
        var pendingCounts = await GetRegionPendingRequestCountsAsync(regionIds);
        var pendingRequests = pendingCounts.GetValueOrDefault(scope.RegionId);
        var quality = (await GetRegionQualityAggregatesAsync(regionIds))
            .GetValueOrDefault(scope.RegionId, RegionQualityAggregate.Empty);
        var managers = await GetDetailedRegionManagersAsync(scope.RegionId);
        var hits = await GetRegionAnalyticsHitsAsync(scope.RegionId, periodRange.Start, periodRange.End, visitOnly: false);
        var reviewAggregate = await GetRegionReviewAggregateAsync(scope.RegionId);
        var health = BuildRegionHealthDto(
            counts,
            quality,
            hits.Count(hit => VisitEventTypes.Contains(hit.EventType)),
            hits.Count(hit => SaveEventTypes.Contains(hit.EventType)),
            reviewAggregate.Count,
            pendingRequests);

        return new TerritoryRegionDetailDto
        {
            Region = new TerritoryRegionDetailRegionDto
            {
                Id = scope.RegionId,
                Name = scope.RegionName,
                DisplayName = scope.RegionName,
                Description = null,
                Status = managers.Count > 0 ? "Active" : "Inactive",
                TerritoryId = scope.TerritoryId,
                TerritoryDisplayName = scope.TerritoryName,
                ImageUrl = scope.ImageUrl,
                CenterLatitude = scope.CenterLatitude,
                CenterLongitude = scope.CenterLongitude
            },
            Managers = managers,
            Summary = new TerritoryRegionSummaryDto
            {
                DestinationsCount = counts.Destinations,
                LocationsCount = counts.Locations,
                PlacesCount = counts.Places,
                StaysCount = counts.Stays,
                EventsCount = counts.Events,
                PendingRequestsCount = pendingRequests,
                ActiveUsers = hits
                    .Where(hit => hit.UserId.HasValue)
                    .Select(hit => hit.UserId!.Value)
                    .Distinct()
                    .Count(),
                Visits = hits.Count(hit => VisitEventTypes.Contains(hit.EventType)),
                Saves = hits.Count(hit => SaveEventTypes.Contains(hit.EventType)),
                Reviews = reviewAggregate.Count,
                AverageRating = reviewAggregate.AverageRating
            },
            Health = health,
            Content = new TerritoryRegionContentPreviewDto
            {
                Destinations = await GetRegionContentPageItemsAsync(
                    scope.RegionId,
                    "destinations",
                    null,
                    "name",
                    0,
                    DetailPreviewTake,
                    periodRange),
                Locations = await GetRegionContentPageItemsAsync(
                    scope.RegionId,
                    "locations",
                    null,
                    "name",
                    0,
                    DetailPreviewTake,
                    periodRange),
                Places = await GetRegionContentPageItemsAsync(
                    scope.RegionId,
                    "places",
                    null,
                    "name",
                    0,
                    DetailPreviewTake,
                    periodRange),
                Stays = await GetRegionContentPageItemsAsync(
                    scope.RegionId,
                    "stays",
                    null,
                    "name",
                    0,
                    DetailPreviewTake,
                    periodRange),
                Events = await GetRegionContentPageItemsAsync(
                    scope.RegionId,
                    "events",
                    null,
                    "upcoming",
                    0,
                    DetailPreviewTake,
                    periodRange)
            },
            Analytics = new TerritoryRegionAnalyticsDto
            {
                TopEntities = await BuildRegionTopEntitiesAsync(scope.RegionId, periodRange),
                RecentActivity = await BuildRegionRecentActivityAsync(scope.RegionId, periodRange)
            }
        };
    }

    public async Task<TerritoryRegionContentResponseDto> GetRegionContentAsync(
        int userId,
        int regionId,
        int? territoryId,
        string? entityType,
        string? search,
        string? sort,
        int skip,
        int take)
    {
        var scope = await GetAuthorizedRegionAsync(userId, regionId, territoryId);
        skip = Math.Max(0, skip);
        take = Math.Clamp(take, 1, DirectoryMaxTake);

        var normalizedType = NormalizeRegionContentType(entityType);
        var periodRange = ResolveRegionPeriod(null);
        var items = await GetRegionContentItemsAsync(
            scope.RegionId,
            normalizedType,
            search,
            sort,
            periodRange);

        return new TerritoryRegionContentResponseDto
        {
            EntityType = normalizedType,
            Items = items.Skip(skip).Take(take).ToList(),
            TotalCount = items.Count,
            Skip = skip,
            Take = take,
            HasMore = skip + take < items.Count
        };
    }

    public async Task<TerritoryContentDirectoryResponseDto> GetContentAsync(
        int userId,
        int? territoryId,
        string? type,
        string? search,
        string? sort,
        int skip,
        int take)
    {
        var activeTerritory = await GetAuthorizedTerritoryAsync(userId, territoryId);
        skip = Math.Max(0, skip);
        take = Math.Clamp(take, 1, DirectoryMaxTake);

        var normalizedType = NormalizeContentType(type);
        if (activeTerritory is null)
        {
            return new TerritoryContentDirectoryResponseDto
            {
                EntityType = normalizedType,
                Skip = skip,
                Take = take
            };
        }

        var currentPeriodStart = DateTime.UtcNow.AddDays(-30);
        var currentPeriodEnd = DateTime.UtcNow;

        var items = normalizedType switch
        {
            "destinations" => await GetDestinationDirectoryItemsAsync(
                activeTerritory.Id,
                search,
                currentPeriodStart,
                currentPeriodEnd),
            "locations" => await GetLocationDirectoryItemsAsync(
                activeTerritory.Id,
                search,
                currentPeriodStart,
                currentPeriodEnd),
            "places" => await GetPlaceDirectoryItemsAsync(
                activeTerritory.Id,
                search,
                currentPeriodStart,
                currentPeriodEnd),
            "events" => await GetEventDirectoryItemsAsync(
                activeTerritory.Id,
                search,
                currentPeriodStart,
                currentPeriodEnd),
            _ => throw new InvalidOperationException("Unsupported content directory type.")
        };

        items = NormalizeSort(sort) switch
        {
            "newest" => items.OrderByDescending(item => item.CreatedAt).ThenBy(item => item.Name).ToList(),
            "oldest" => items.OrderBy(item => item.CreatedAt).ThenBy(item => item.Name).ToList(),
            "views" => items.OrderByDescending(item => item.ViewsLast30Days).ThenBy(item => item.Name).ToList(),
            "upcoming" when normalizedType == "events" => items
                .OrderBy(item => item.StartsAt ?? DateTime.MaxValue)
                .ThenBy(item => item.Name)
                .ToList(),
            "name-desc" => items.OrderByDescending(item => item.Name).ThenByDescending(item => item.Id).ToList(),
            _ => items.OrderBy(item => item.Name).ThenBy(item => item.Id).ToList()
        };

        return new TerritoryContentDirectoryResponseDto
        {
            EntityType = normalizedType,
            Items = items.Skip(skip).Take(take).ToList(),
            TotalCount = items.Count,
            Skip = skip,
            Take = take,
            HasMore = skip + take < items.Count
        };
    }

    private async Task<TerritoryDashboardStatsDto> BuildStatsAsync(
        int territoryId,
        DateTime lastMonthCutoff,
        int currentActiveUsers,
        int previousActiveUsers)
    {
        return new TerritoryDashboardStatsDto
        {
            TotalRegions = await _db.Regions
                .AsNoTracking()
                .CountAsync(region => region.TerritoryId == territoryId),
            RegionsAddedLastMonth = await _db.Regions
                .AsNoTracking()
                .CountAsync(region => region.TerritoryId == territoryId && region.CreatedAt >= lastMonthCutoff),
            ActiveUsers = currentActiveUsers,
            ActiveUsersChangePercent = CalculateChangePercent(currentActiveUsers, previousActiveUsers),
            TotalDestinations = await _db.Destinations
                .AsNoTracking()
                .CountAsync(destination => destination.Region.TerritoryId == territoryId),
            DestinationsAddedLastMonth = await _db.Destinations
                .AsNoTracking()
                .CountAsync(destination => destination.Region.TerritoryId == territoryId && destination.CreatedAt >= lastMonthCutoff),
            TotalLocations = await _db.Locations
                .AsNoTracking()
                .CountAsync(location => location.Destination.Region.TerritoryId == territoryId),
            LocationsAddedLastMonth = await _db.Locations
                .AsNoTracking()
                .CountAsync(location => location.Destination.Region.TerritoryId == territoryId && location.CreatedAt >= lastMonthCutoff),
            TotalPlaces = await _db.Places
                .AsNoTracking()
                .CountAsync(place => place.Location.Destination.Region.TerritoryId == territoryId),
            PlacesAddedLastMonth = await _db.Places
                .AsNoTracking()
                .CountAsync(place => place.Location.Destination.Region.TerritoryId == territoryId && place.CreatedAt >= lastMonthCutoff),
            TotalEvents = await _db.Events
                .AsNoTracking()
                .CountAsync(evt => evt.Destination.Region.TerritoryId == territoryId),
            EventsAddedLastMonth = await _db.Events
                .AsNoTracking()
                .CountAsync(evt => evt.Destination.Region.TerritoryId == territoryId && evt.CreatedAt >= lastMonthCutoff)
        };
    }

    private async Task<List<TerritoryRegionHealthDto>> BuildRegionsHealthAsync(
        int territoryId,
        IReadOnlyDictionary<int, int> regionVisitCounts)
    {
        var regions = await _db.Regions
            .AsNoTracking()
            .Where(region => region.TerritoryId == territoryId)
            .OrderBy(region => region.Name)
            .ThenBy(region => region.Id)
            .Select(region => new RegionBaseRow
            {
                Id = region.Id,
                Name = region.Name,
                TerritoryName = region.Territory.Name,
                CreatedAt = region.CreatedAt
            })
            .ToListAsync();

        var regionIds = regions.Select(region => region.Id).ToList();
        var contentCounts = await GetRegionContentCountsAsync(regionIds);
        var pendingCounts = await GetRegionPendingRequestCountsAsync(regionIds);
        var managers = await GetRegionManagersAsync(regionIds);

        var maxVisits = Math.Max(1, regionVisitCounts.Values.DefaultIfEmpty(0).Max());
        var maxContent = Math.Max(1, contentCounts.Values
            .Select(counts => counts.Destinations + counts.Locations + counts.Places + counts.Stays + counts.Events)
            .DefaultIfEmpty(0)
            .Max());

        return regions.Select(region =>
            {
                var counts = contentCounts.GetValueOrDefault(region.Id, RegionContentCounts.Empty);
                var managerRows = managers.GetValueOrDefault(region.Id, new List<ManagerRow>());
                var pendingRequests = pendingCounts.GetValueOrDefault(region.Id);
                var visits = regionVisitCounts.GetValueOrDefault(region.Id);

                return new TerritoryRegionHealthDto
                {
                    RegionId = region.Id,
                    RegionName = region.Name,
                    ManagerDisplayName = BuildManagerDisplayName(managerRows),
                    AssignedManagers = managerRows.Select(BuildManagerDto).ToList(),
                    AssignedManagersPreview = managerRows
                        .Select(manager => manager.Initials)
                        .Where(initials => !string.IsNullOrWhiteSpace(initials))
                        .Take(4)
                        .ToList(),
                    DestinationsCount = counts.Destinations,
                    LocationsCount = counts.Locations,
                    PlacesCount = counts.Places,
                    StaysCount = counts.Stays,
                    EventsCount = counts.Events,
                    PendingRequestsCount = pendingRequests,
                    ActivityScore = CalculateActivityScore(
                        visits,
                        maxVisits,
                        counts.Destinations + counts.Locations + counts.Places + counts.Stays + counts.Events,
                        maxContent,
                        pendingRequests)
                };
            })
            .ToList();
    }

    private async Task<List<TerritoryTopPerformingRegionDto>> BuildTopPerformingRegionsAsync(
        int territoryId,
        IReadOnlyDictionary<int, int> currentRegionVisitCounts,
        IReadOnlyDictionary<int, int> previousRegionVisitCounts)
    {
        if (currentRegionVisitCounts.Count == 0)
        {
            return new List<TerritoryTopPerformingRegionDto>();
        }

        var regionNames = await _db.Regions
            .AsNoTracking()
            .Where(region => region.TerritoryId == territoryId)
            .Select(region => new { region.Id, region.Name })
            .ToDictionaryAsync(region => region.Id, region => region.Name);

        var rank = 0;
        return currentRegionVisitCounts
            .Where(pair => pair.Value > 0 && regionNames.ContainsKey(pair.Key))
            .OrderByDescending(pair => pair.Value)
            .ThenBy(pair => regionNames[pair.Key])
            .Take(DefaultTopTake)
            .Select(pair =>
            {
                rank++;
                var previous = previousRegionVisitCounts.GetValueOrDefault(pair.Key);
                return new TerritoryTopPerformingRegionDto
                {
                    RegionId = pair.Key,
                    RegionName = regionNames[pair.Key],
                    VisitsThisMonth = pair.Value,
                    ChangePercent = CalculateChangePercent(pair.Value, previous),
                    Rank = rank
                };
            })
            .ToList();
    }

    private async Task<TerritoryTopContentDto> BuildTopContentAsync(int territoryId, DateTime periodStart, DateTime periodEnd)
    {
        return new TerritoryTopContentDto
        {
            TopDestinations = await GetTopDestinationsAsync(territoryId, periodStart, periodEnd),
            TopLocations = await GetTopLocationsAsync(territoryId, periodStart, periodEnd),
            TopPlaces = await GetTopPlacesAsync(territoryId, periodStart, periodEnd),
            UpcomingEvents = await GetUpcomingEventsAsync(territoryId, periodStart, periodEnd)
        };
    }

    private async Task<List<TerritoryTopContentItemDto>> GetTopDestinationsAsync(
        int territoryId,
        DateTime periodStart,
        DateTime periodEnd)
    {
        var viewCounts = await GetEntityViewCountsAsync(
            AnalyticsEntityTypes.Destination,
            periodStart,
            periodEnd);

        if (viewCounts.Count == 0)
        {
            return new List<TerritoryTopContentItemDto>();
        }

        var rows = await _db.Destinations
            .AsNoTracking()
            .Where(destination =>
                destination.Region.TerritoryId == territoryId &&
                viewCounts.Keys.Contains(destination.Id))
            .Select(destination => new
            {
                destination.Id,
                destination.Name,
                RegionName = destination.Region.Name,
                ImageUrl = destination.Images
                    .OrderByDescending(image => image.IsMain)
                    .ThenBy(image => image.SortOrder)
                    .ThenBy(image => image.CreatedAt)
                    .ThenBy(image => image.Id)
                    .Select(image => image.Url)
                    .FirstOrDefault()
            })
            .ToListAsync();

        return rows
            .OrderByDescending(row => viewCounts.GetValueOrDefault(row.Id))
            .ThenBy(row => row.Name)
            .Take(DefaultTopTake)
            .Select(row => new TerritoryTopContentItemDto
            {
                Id = row.Id,
                Name = row.Name,
                RegionName = row.RegionName,
                MetricValue = viewCounts.GetValueOrDefault(row.Id),
                MetricLabel = "views",
                ImageUrl = row.ImageUrl,
                DetailRoute = "/territory/content?type=destinations"
            })
            .ToList();
    }

    private async Task<List<TerritoryTopContentItemDto>> GetTopLocationsAsync(
        int territoryId,
        DateTime periodStart,
        DateTime periodEnd)
    {
        var viewCounts = await GetEntityViewCountsAsync(
            AnalyticsEntityTypes.Location,
            periodStart,
            periodEnd);

        if (viewCounts.Count == 0)
        {
            return new List<TerritoryTopContentItemDto>();
        }

        var rows = await _db.Locations
            .AsNoTracking()
            .Where(location =>
                location.Destination.Region.TerritoryId == territoryId &&
                viewCounts.Keys.Contains(location.Id))
            .Select(location => new
            {
                location.Id,
                location.Name,
                RegionName = location.Destination.Region.Name,
                ImageUrl = location.Images
                    .OrderByDescending(image => image.IsMain)
                    .ThenBy(image => image.SortOrder)
                    .ThenBy(image => image.CreatedAt)
                    .ThenBy(image => image.Id)
                    .Select(image => image.Url)
                    .FirstOrDefault()
            })
            .ToListAsync();

        return rows
            .OrderByDescending(row => viewCounts.GetValueOrDefault(row.Id))
            .ThenBy(row => row.Name)
            .Take(DefaultTopTake)
            .Select(row => new TerritoryTopContentItemDto
            {
                Id = row.Id,
                Name = row.Name,
                RegionName = row.RegionName,
                MetricValue = viewCounts.GetValueOrDefault(row.Id),
                MetricLabel = "views",
                ImageUrl = row.ImageUrl,
                DetailRoute = "/territory/content?type=locations"
            })
            .ToList();
    }

    private async Task<List<TerritoryTopContentItemDto>> GetTopPlacesAsync(
        int territoryId,
        DateTime periodStart,
        DateTime periodEnd)
    {
        var viewCounts = await GetEntityViewCountsAsync(
            AnalyticsEntityTypes.Place,
            periodStart,
            periodEnd,
            AnalyticsEntityTypes.Hotel);

        var rows = await _db.Places
            .AsNoTracking()
            .Where(place => place.Location.Destination.Region.TerritoryId == territoryId)
            .Select(place => new
            {
                place.Id,
                place.Name,
                RegionName = place.Location.Destination.Region.Name,
                place.AverageRating,
                place.ReviewCount,
                ImageUrl = place.Images
                    .OrderByDescending(image => image.IsMain)
                    .ThenBy(image => image.SortOrder)
                    .ThenBy(image => image.CreatedAt)
                    .ThenBy(image => image.Id)
                    .Select(image => image.Url)
                    .FirstOrDefault()
            })
            .ToListAsync();

        if (viewCounts.Count > 0)
        {
            return rows
                .Where(row => viewCounts.ContainsKey(row.Id))
                .OrderByDescending(row => viewCounts.GetValueOrDefault(row.Id))
                .ThenBy(row => row.Name)
                .Take(DefaultTopTake)
                .Select(row => new TerritoryTopContentItemDto
                {
                    Id = row.Id,
                    Name = row.Name,
                    RegionName = row.RegionName,
                    MetricValue = viewCounts.GetValueOrDefault(row.Id),
                    MetricLabel = "views",
                    ImageUrl = row.ImageUrl,
                    DetailRoute = "/territory/content?type=places"
                })
                .ToList();
        }

        return rows
            .Where(row => row.AverageRating.HasValue && row.ReviewCount > 0)
            .OrderByDescending(row => row.AverageRating)
            .ThenByDescending(row => row.ReviewCount)
            .ThenBy(row => row.Name)
            .Take(DefaultTopTake)
            .Select(row => new TerritoryTopContentItemDto
            {
                Id = row.Id,
                Name = row.Name,
                RegionName = row.RegionName,
                MetricValue = (int)Math.Round(row.AverageRating!.Value * 10, MidpointRounding.AwayFromZero),
                MetricLabel = "rating_x10",
                ImageUrl = row.ImageUrl,
                DetailRoute = "/territory/content?type=places"
            })
            .ToList();
    }

    private async Task<List<TerritoryTopContentItemDto>> GetUpcomingEventsAsync(
        int territoryId,
        DateTime periodStart,
        DateTime periodEnd)
    {
        var viewCounts = await GetEntityViewCountsAsync(
            AnalyticsEntityTypes.Event,
            periodStart,
            periodEnd);
        var now = DateTime.UtcNow;

        var rows = await _db.Events
            .AsNoTracking()
            .Where(evt =>
                evt.Destination.Region.TerritoryId == territoryId &&
                evt.StartDate >= now)
            .OrderBy(evt => evt.StartDate)
            .ThenBy(evt => evt.Name)
            .Take(DefaultTopTake)
            .Select(evt => new
            {
                evt.Id,
                evt.Name,
                RegionName = evt.Destination.Region.Name,
                evt.StartDate,
                ImageUrl = evt.Images
                    .OrderByDescending(image => image.IsMain)
                    .ThenBy(image => image.SortOrder)
                    .ThenBy(image => image.CreatedAt)
                    .ThenBy(image => image.Id)
                    .Select(image => image.Url)
                    .FirstOrDefault()
            })
            .ToListAsync();

        return rows.Select(row => new TerritoryTopContentItemDto
        {
            Id = row.Id,
            Name = row.Name,
            RegionName = row.RegionName,
            MetricValue = viewCounts.GetValueOrDefault(row.Id),
            MetricLabel = "views",
            ImageUrl = row.ImageUrl,
            DetailRoute = "/territory/content?type=events",
            StartsAt = row.StartDate
        }).ToList();
    }

    private async Task<Dictionary<int, int>> GetEntityViewCountsAsync(
        string entityType,
        DateTime periodStart,
        DateTime periodEnd,
        string? secondaryEntityType = null)
    {
        var allowedEntityTypes = secondaryEntityType is null
            ? new[] { entityType }
            : new[] { entityType, secondaryEntityType };

        return await _db.AnalyticsEvents
            .AsNoTracking()
            .Where(evt =>
                evt.OccurredAt >= periodStart &&
                evt.OccurredAt < periodEnd &&
                VisitEventTypes.Contains(evt.EventType) &&
                evt.EntityId.HasValue &&
                evt.EntityType != null &&
                allowedEntityTypes.Contains(evt.EntityType))
            .GroupBy(evt => evt.EntityId!.Value)
            .Select(group => new { Id = group.Key, Count = group.Count() })
            .ToDictionaryAsync(row => row.Id, row => row.Count);
    }

    private async Task<int> CountActiveUsersAsync(int territoryId, DateTime periodStart, DateTime periodEnd)
    {
        var hits = await GetTerritoryAnalyticsHitsAsync(
            territoryId,
            periodStart,
            periodEnd,
            visitOnly: false);

        return hits
            .Where(hit => hit.UserId.HasValue)
            .Select(hit => hit.UserId!.Value)
            .Distinct()
            .Count();
    }

    private async Task<List<AnalyticsRegionHit>> GetTerritoryAnalyticsHitsAsync(
        int territoryId,
        DateTime periodStart,
        DateTime periodEnd,
        bool visitOnly)
    {
        IQueryable<AnalyticsEventBaseRow> baseEvents = _db.AnalyticsEvents
            .AsNoTracking()
            .Where(evt => evt.OccurredAt >= periodStart && evt.OccurredAt < periodEnd)
            .Select(evt => new AnalyticsEventBaseRow
            {
                EventId = evt.Id,
                UserId = evt.UserId,
                EventType = evt.EventType,
                EntityType = evt.EntityType,
                EntityId = evt.EntityId,
                DestinationId = evt.DestinationId,
                OccurredAt = evt.OccurredAt
            });

        if (visitOnly)
        {
            baseEvents = baseEvents.Where(evt => VisitEventTypes.Contains(evt.EventType));
        }

        var hits = new List<AnalyticsRegionHit>();

        hits.AddRange(await (
            from evt in baseEvents
            where evt.DestinationId.HasValue
            join destination in _db.Destinations.AsNoTracking()
                on evt.DestinationId!.Value equals destination.Id
            where destination.Region.TerritoryId == territoryId
            select new AnalyticsRegionHit
            {
                EventId = evt.EventId,
                RegionId = destination.RegionId,
                UserId = evt.UserId,
                EventType = evt.EventType,
                EntityType = evt.EntityType,
                EntityId = evt.EntityId,
                OccurredAt = evt.OccurredAt
            }).ToListAsync());

        hits.AddRange(await (
            from evt in baseEvents
            where evt.EntityType == AnalyticsEntityTypes.Destination && evt.EntityId.HasValue
            join destination in _db.Destinations.AsNoTracking()
                on evt.EntityId!.Value equals destination.Id
            where destination.Region.TerritoryId == territoryId
            select new AnalyticsRegionHit
            {
                EventId = evt.EventId,
                RegionId = destination.RegionId,
                UserId = evt.UserId,
                EventType = evt.EventType,
                EntityType = evt.EntityType,
                EntityId = evt.EntityId,
                OccurredAt = evt.OccurredAt
            }).ToListAsync());

        hits.AddRange(await (
            from evt in baseEvents
            where evt.EntityType == AnalyticsEntityTypes.Location && evt.EntityId.HasValue
            join location in _db.Locations.AsNoTracking()
                on evt.EntityId!.Value equals location.Id
            where location.Destination.Region.TerritoryId == territoryId
            select new AnalyticsRegionHit
            {
                EventId = evt.EventId,
                RegionId = location.Destination.RegionId,
                UserId = evt.UserId,
                EventType = evt.EventType,
                EntityType = evt.EntityType,
                EntityId = evt.EntityId,
                OccurredAt = evt.OccurredAt
            }).ToListAsync());

        hits.AddRange(await (
            from evt in baseEvents
            where (evt.EntityType == AnalyticsEntityTypes.Place || evt.EntityType == AnalyticsEntityTypes.Hotel) &&
                evt.EntityId.HasValue
            join place in _db.Places.AsNoTracking()
                on evt.EntityId!.Value equals place.Id
            where place.Location.Destination.Region.TerritoryId == territoryId
            select new AnalyticsRegionHit
            {
                EventId = evt.EventId,
                RegionId = place.Location.Destination.RegionId,
                UserId = evt.UserId,
                EventType = evt.EventType,
                EntityType = evt.EntityType,
                EntityId = evt.EntityId,
                OccurredAt = evt.OccurredAt
            }).ToListAsync());

        hits.AddRange(await (
            from evt in baseEvents
            where evt.EntityType == AnalyticsEntityTypes.Event && evt.EntityId.HasValue
            join eventEntity in _db.Events.AsNoTracking()
                on evt.EntityId!.Value equals eventEntity.Id
            where eventEntity.Destination.Region.TerritoryId == territoryId
            select new AnalyticsRegionHit
            {
                EventId = evt.EventId,
                RegionId = eventEntity.Destination.RegionId,
                UserId = evt.UserId,
                EventType = evt.EventType,
                EntityType = evt.EntityType,
                EntityId = evt.EntityId,
                OccurredAt = evt.OccurredAt
            }).ToListAsync());

        hits.AddRange(await (
            from evt in baseEvents
            where evt.EntityType == AnalyticsEntityTypes.Region && evt.EntityId.HasValue
            join region in _db.Regions.AsNoTracking()
                on evt.EntityId!.Value equals region.Id
            where region.TerritoryId == territoryId
            select new AnalyticsRegionHit
            {
                EventId = evt.EventId,
                RegionId = region.Id,
                UserId = evt.UserId,
                EventType = evt.EventType,
                EntityType = evt.EntityType,
                EntityId = evt.EntityId,
                OccurredAt = evt.OccurredAt
            }).ToListAsync());

        return hits
            .GroupBy(hit => hit.EventId)
            .Select(group => group.First())
            .ToList();
    }

    private async Task<Dictionary<int, RegionContentCounts>> GetRegionContentCountsAsync(List<int> regionIds)
    {
        var destinationCounts = await _db.Destinations
            .AsNoTracking()
            .Where(destination => regionIds.Contains(destination.RegionId))
            .GroupBy(destination => destination.RegionId)
            .Select(group => new { RegionId = group.Key, Count = group.Count() })
            .ToDictionaryAsync(row => row.RegionId, row => row.Count);

        var locationCounts = await _db.Locations
            .AsNoTracking()
            .Where(location => regionIds.Contains(location.Destination.RegionId))
            .GroupBy(location => location.Destination.RegionId)
            .Select(group => new { RegionId = group.Key, Count = group.Count() })
            .ToDictionaryAsync(row => row.RegionId, row => row.Count);

        var placeCounts = await _db.Places
            .AsNoTracking()
            .Where(place =>
                place.Stay == null &&
                regionIds.Contains(place.Location.Destination.RegionId))
            .GroupBy(place => place.Location.Destination.RegionId)
            .Select(group => new { RegionId = group.Key, Count = group.Count() })
            .ToDictionaryAsync(row => row.RegionId, row => row.Count);

        var stayCounts = await _db.Places
            .AsNoTracking()
            .Where(place =>
                place.Stay != null &&
                regionIds.Contains(place.Location.Destination.RegionId))
            .GroupBy(place => place.Location.Destination.RegionId)
            .Select(group => new { RegionId = group.Key, Count = group.Count() })
            .ToDictionaryAsync(row => row.RegionId, row => row.Count);

        var eventCounts = await _db.Events
            .AsNoTracking()
            .Where(evt => regionIds.Contains(evt.Destination.RegionId))
            .GroupBy(evt => evt.Destination.RegionId)
            .Select(group => new { RegionId = group.Key, Count = group.Count() })
            .ToDictionaryAsync(row => row.RegionId, row => row.Count);

        return regionIds.ToDictionary(
            regionId => regionId,
            regionId => new RegionContentCounts(
                destinationCounts.GetValueOrDefault(regionId),
                locationCounts.GetValueOrDefault(regionId),
                placeCounts.GetValueOrDefault(regionId),
                stayCounts.GetValueOrDefault(regionId),
                eventCounts.GetValueOrDefault(regionId)));
    }

    private async Task<Dictionary<int, int>> GetRegionPendingRequestCountsAsync(List<int> regionIds)
    {
        var contributionCounts = await _db.PlaceContributions
            .AsNoTracking()
            .Where(contribution =>
                contribution.Status == PlaceContributionStatus.PendingReview &&
                ((contribution.LocationId.HasValue &&
                  contribution.Location != null &&
                  regionIds.Contains(contribution.Location.Destination.RegionId)) ||
                 (contribution.PlaceId.HasValue &&
                  contribution.Place != null &&
                  regionIds.Contains(contribution.Place.Location.Destination.RegionId))))
            .GroupBy(contribution => contribution.LocationId.HasValue
                ? contribution.Location!.Destination.RegionId
                : contribution.Place!.Location.Destination.RegionId)
            .Select(group => new { RegionId = group.Key, Count = group.Count() })
            .ToDictionaryAsync(row => row.RegionId, row => row.Count);

        var reviewReportCounts = await _db.ReviewReports
            .AsNoTracking()
            .Where(report =>
                report.Status == ReviewReportStatus.Pending &&
                regionIds.Contains(report.RegionId))
            .GroupBy(report => report.RegionId)
            .Select(group => new { RegionId = group.Key, Count = group.Count() })
            .ToDictionaryAsync(row => row.RegionId, row => row.Count);

        return regionIds.ToDictionary(
            regionId => regionId,
            regionId => contributionCounts.GetValueOrDefault(regionId) + reviewReportCounts.GetValueOrDefault(regionId));
    }

    private async Task<Dictionary<int, List<ManagerRow>>> GetRegionManagersAsync(List<int> regionIds)
    {
        var rows = await _db.RegionalManagerRegions
            .AsNoTracking()
            .Where(assignment => assignment.IsActive && regionIds.Contains(assignment.RegionId))
            .OrderBy(assignment => assignment.User.DisplayName)
            .ThenBy(assignment => assignment.User.Email)
            .Select(assignment => new RawManagerRow
            {
                RegionId = assignment.RegionId,
                UserId = assignment.UserId,
                DisplayName = assignment.User.DisplayName,
                FirstName = assignment.User.FirstName,
                LastName = assignment.User.LastName,
                Username = assignment.User.Username,
                Email = assignment.User.Email,
                AvatarUrl = assignment.User.AvatarUrl,
                AssignedAt = assignment.CreatedAt
            })
            .ToListAsync();

        return rows
            .Select(row =>
            {
                var displayName = BuildDisplayName(row.DisplayName, row.FirstName, row.LastName, row.Username);
                return new ManagerRow
                {
                    RegionId = row.RegionId,
                    UserId = row.UserId,
                    DisplayName = displayName,
                    Email = row.Email,
                    Initials = BuildManagerInitials(displayName, row.Email),
                    AvatarUrl = row.AvatarUrl,
                    AssignedAt = row.AssignedAt
                };
            })
            .GroupBy(row => row.RegionId)
            .ToDictionary(group => group.Key, group => group.ToList());
    }

    private async Task<List<TerritoryContentDirectoryItemDto>> GetDestinationDirectoryItemsAsync(
        int territoryId,
        string? search,
        DateTime periodStart,
        DateTime periodEnd)
    {
        var query = _db.Destinations
            .AsNoTracking()
            .Where(destination => destination.Region.TerritoryId == territoryId);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var normalizedSearch = search.Trim().ToLowerInvariant();
            query = query.Where(destination =>
                destination.Name.ToLower().Contains(normalizedSearch) ||
                destination.Region.Name.ToLower().Contains(normalizedSearch));
        }

        var rows = await query
            .Select(destination => new TerritoryContentDirectoryItemDto
            {
                Id = destination.Id,
                EntityType = "destination",
                Name = destination.Name,
                RegionName = destination.Region.Name,
                ParentName = destination.Region.Name,
                TypeName = destination.DestinationType.Name,
                Status = destination.Status,
                CreatedAt = destination.CreatedAt,
                UpdatedAt = destination.UpdatedAt,
                ImageUrl = destination.Images
                    .OrderByDescending(image => image.IsMain)
                    .ThenBy(image => image.SortOrder)
                    .ThenBy(image => image.CreatedAt)
                    .ThenBy(image => image.Id)
                    .Select(image => image.Url)
                    .FirstOrDefault(),
                DetailRoute = "/territory/content?type=destinations"
            })
            .ToListAsync();

        var viewCounts = await GetEntityViewCountsAsync(
            AnalyticsEntityTypes.Destination,
            periodStart,
            periodEnd);
        foreach (var row in rows)
        {
            row.ViewsLast30Days = viewCounts.GetValueOrDefault(row.Id);
        }

        return rows;
    }

    private async Task<List<TerritoryContentDirectoryItemDto>> GetLocationDirectoryItemsAsync(
        int territoryId,
        string? search,
        DateTime periodStart,
        DateTime periodEnd)
    {
        var query = _db.Locations
            .AsNoTracking()
            .Where(location => location.Destination.Region.TerritoryId == territoryId);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var normalizedSearch = search.Trim().ToLowerInvariant();
            query = query.Where(location =>
                location.Name.ToLower().Contains(normalizedSearch) ||
                location.Destination.Name.ToLower().Contains(normalizedSearch) ||
                location.Destination.Region.Name.ToLower().Contains(normalizedSearch));
        }

        var rows = await query
            .Select(location => new TerritoryContentDirectoryItemDto
            {
                Id = location.Id,
                EntityType = "location",
                Name = location.Name,
                RegionName = location.Destination.Region.Name,
                ParentName = location.Destination.Name,
                TypeName = location.LocationType.Name,
                Status = location.Status,
                CreatedAt = location.CreatedAt,
                UpdatedAt = location.UpdatedAt,
                ImageUrl = location.Images
                    .OrderByDescending(image => image.IsMain)
                    .ThenBy(image => image.SortOrder)
                    .ThenBy(image => image.CreatedAt)
                    .ThenBy(image => image.Id)
                    .Select(image => image.Url)
                    .FirstOrDefault(),
                DetailRoute = "/territory/content?type=locations"
            })
            .ToListAsync();

        var viewCounts = await GetEntityViewCountsAsync(
            AnalyticsEntityTypes.Location,
            periodStart,
            periodEnd);
        foreach (var row in rows)
        {
            row.ViewsLast30Days = viewCounts.GetValueOrDefault(row.Id);
        }

        return rows;
    }

    private async Task<List<TerritoryContentDirectoryItemDto>> GetPlaceDirectoryItemsAsync(
        int territoryId,
        string? search,
        DateTime periodStart,
        DateTime periodEnd)
    {
        var query = _db.Places
            .AsNoTracking()
            .Where(place => place.Location.Destination.Region.TerritoryId == territoryId);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var normalizedSearch = search.Trim().ToLowerInvariant();
            query = query.Where(place =>
                place.Name.ToLower().Contains(normalizedSearch) ||
                place.Location.Name.ToLower().Contains(normalizedSearch) ||
                place.Location.Destination.Region.Name.ToLower().Contains(normalizedSearch));
        }

        var rows = await query
            .Select(place => new TerritoryContentDirectoryItemDto
            {
                Id = place.Id,
                EntityType = "place",
                Name = place.Name,
                RegionName = place.Location.Destination.Region.Name,
                ParentName = place.Location.Name,
                TypeName = place.PlaceType.Name,
                Status = place.Status,
                CreatedAt = place.CreatedAt,
                UpdatedAt = place.UpdatedAt,
                ImageUrl = place.Images
                    .OrderByDescending(image => image.IsMain)
                    .ThenBy(image => image.SortOrder)
                    .ThenBy(image => image.CreatedAt)
                    .ThenBy(image => image.Id)
                    .Select(image => image.Url)
                    .FirstOrDefault(),
                DetailRoute = "/territory/content?type=places"
            })
            .ToListAsync();

        var viewCounts = await GetEntityViewCountsAsync(
            AnalyticsEntityTypes.Place,
            periodStart,
            periodEnd,
            AnalyticsEntityTypes.Hotel);
        foreach (var row in rows)
        {
            row.ViewsLast30Days = viewCounts.GetValueOrDefault(row.Id);
        }

        return rows;
    }

    private async Task<List<TerritoryContentDirectoryItemDto>> GetEventDirectoryItemsAsync(
        int territoryId,
        string? search,
        DateTime periodStart,
        DateTime periodEnd)
    {
        var query = _db.Events
            .AsNoTracking()
            .Where(evt => evt.Destination.Region.TerritoryId == territoryId);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var normalizedSearch = search.Trim().ToLowerInvariant();
            query = query.Where(evt =>
                evt.Name.ToLower().Contains(normalizedSearch) ||
                evt.Destination.Name.ToLower().Contains(normalizedSearch) ||
                evt.Destination.Region.Name.ToLower().Contains(normalizedSearch));
        }

        var rows = await query
            .Select(evt => new TerritoryContentDirectoryItemDto
            {
                Id = evt.Id,
                EntityType = "event",
                Name = evt.Name,
                RegionName = evt.Destination.Region.Name,
                ParentName = evt.Location != null ? evt.Location.Name : evt.Destination.Name,
                TypeName = evt.EventType.Name,
                Status = evt.Status,
                CreatedAt = evt.CreatedAt,
                UpdatedAt = evt.UpdatedAt,
                StartsAt = evt.StartDate,
                ImageUrl = evt.Images
                    .OrderByDescending(image => image.IsMain)
                    .ThenBy(image => image.SortOrder)
                    .ThenBy(image => image.CreatedAt)
                    .ThenBy(image => image.Id)
                    .Select(image => image.Url)
                    .FirstOrDefault(),
                DetailRoute = "/territory/content?type=events"
            })
            .ToListAsync();

        var viewCounts = await GetEntityViewCountsAsync(
            AnalyticsEntityTypes.Event,
            periodStart,
            periodEnd);
        foreach (var row in rows)
        {
            row.ViewsLast30Days = viewCounts.GetValueOrDefault(row.Id);
        }

        return rows;
    }

    private async Task<List<TerritoryRegionContentItemDto>> GetRegionContentPageItemsAsync(
        int regionId,
        string entityType,
        string? search,
        string? sort,
        int skip,
        int take,
        RegionPeriodRange periodRange)
    {
        var items = await GetRegionContentItemsAsync(regionId, entityType, search, sort, periodRange);
        return items.Skip(skip).Take(take).ToList();
    }

    private async Task<List<TerritoryRegionContentItemDto>> GetRegionContentItemsAsync(
        int regionId,
        string entityType,
        string? search,
        string? sort,
        RegionPeriodRange periodRange)
    {
        var normalizedType = NormalizeRegionContentType(entityType);
        List<TerritoryRegionContentItemDto> items;

        if (normalizedType == "all")
        {
            items = new List<TerritoryRegionContentItemDto>();
            items.AddRange(await LoadDestinationRegionContentItemsAsync(regionId, search));
            items.AddRange(await LoadLocationRegionContentItemsAsync(regionId, search));
            items.AddRange(await LoadPlaceRegionContentItemsAsync(regionId, search, staysOnly: false));
            items.AddRange(await LoadPlaceRegionContentItemsAsync(regionId, search, staysOnly: true));
            items.AddRange(await LoadEventRegionContentItemsAsync(regionId, search));
        }
        else
        {
            items = normalizedType switch
            {
                "destinations" => await LoadDestinationRegionContentItemsAsync(regionId, search),
                "locations" => await LoadLocationRegionContentItemsAsync(regionId, search),
                "places" => await LoadPlaceRegionContentItemsAsync(regionId, search, staysOnly: false),
                "stays" => await LoadPlaceRegionContentItemsAsync(regionId, search, staysOnly: true),
                "events" => await LoadEventRegionContentItemsAsync(regionId, search),
                _ => throw new InvalidOperationException("Unsupported region content type.")
            };
        }

        await ApplyContentMetricsAsync(items, periodRange);
        await ApplyContentReviewMetricsAsync(items);

        var destinationIds = items
            .Where(item => item.EntityType == "destination")
            .Select(item => item.Id)
            .ToList();

        if (destinationIds.Count > 0)
        {
            var health = await BuildDestinationHealthMapAsync(destinationIds, regionId, periodRange);
            foreach (var item in items.Where(item => item.EntityType == "destination"))
            {
                item.Health = health.GetValueOrDefault(item.Id);
            }
        }

        return SortRegionContentItems(items, normalizedType, sort);
    }

    private async Task<List<TerritoryRegionContentItemDto>> LoadDestinationRegionContentItemsAsync(
        int regionId,
        string? search)
    {
        var query = _db.Destinations
            .AsNoTracking()
            .Where(destination => destination.RegionId == regionId);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var normalizedSearch = search.Trim().ToLowerInvariant();
            query = query.Where(destination =>
                destination.Name.ToLower().Contains(normalizedSearch) ||
                (destination.Summary ?? string.Empty).ToLower().Contains(normalizedSearch) ||
                destination.Description.ToLower().Contains(normalizedSearch) ||
                destination.DestinationType.Name.ToLower().Contains(normalizedSearch));
        }

        return await query
            .Select(destination => new TerritoryRegionContentItemDto
            {
                Id = destination.Id,
                EntityType = "destination",
                PublicDetailRoute = destination.Status == PublishedStatus
                    ? "/explore?destinationId=" + destination.Id
                    : null,
                Name = destination.Name,
                Summary = destination.Summary ?? BuildSummary(destination.Description),
                Description = destination.Description,
                Status = destination.Status,
                RegionId = destination.RegionId,
                RegionName = destination.Region.Name,
                DestinationId = destination.Id,
                DestinationName = destination.Name,
                ParentName = destination.Region.Name,
                TypeName = destination.DestinationType.Name,
                CreatedAt = destination.CreatedAt,
                UpdatedAt = destination.UpdatedAt,
                LocationsCount = destination.Locations.Count,
                PlacesCount = destination.Locations
                    .SelectMany(location => location.Places)
                    .Count(place => place.Stay == null),
                StaysCount = destination.Locations
                    .SelectMany(location => location.Places)
                    .Count(place => place.Stay != null),
                EventsCount = destination.Events.Count,
                ImageUrl = destination.Images
                    .Where(image => image.TargetType == EntityImageTargetType.Destination)
                    .OrderByDescending(image => image.IsMain)
                    .ThenBy(image => image.SortOrder)
                    .ThenBy(image => image.CreatedAt)
                    .ThenBy(image => image.Id)
                    .Select(image => image.Url)
                    .FirstOrDefault()
            })
            .ToListAsync();
    }

    private async Task<List<TerritoryRegionContentItemDto>> LoadLocationRegionContentItemsAsync(
        int regionId,
        string? search)
    {
        var query = _db.Locations
            .AsNoTracking()
            .Where(location => location.Destination.RegionId == regionId);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var normalizedSearch = search.Trim().ToLowerInvariant();
            query = query.Where(location =>
                location.Name.ToLower().Contains(normalizedSearch) ||
                (location.Summary ?? string.Empty).ToLower().Contains(normalizedSearch) ||
                location.Description.ToLower().Contains(normalizedSearch) ||
                location.Destination.Name.ToLower().Contains(normalizedSearch) ||
                location.LocationType.Name.ToLower().Contains(normalizedSearch));
        }

        return await query
            .Select(location => new TerritoryRegionContentItemDto
            {
                Id = location.Id,
                EntityType = "location",
                PublicDetailRoute = null,
                Name = location.Name,
                Summary = location.Summary ?? BuildSummary(location.Description),
                Description = location.Description,
                Status = location.Status,
                RegionId = location.Destination.RegionId,
                RegionName = location.Destination.Region.Name,
                DestinationId = location.DestinationId,
                DestinationName = location.Destination.Name,
                LocationId = location.Id,
                LocationName = location.Name,
                ParentName = location.Destination.Name,
                TypeName = location.LocationType.Name,
                CreatedAt = location.CreatedAt,
                UpdatedAt = location.UpdatedAt,
                PlacesCount = location.Places.Count(place => place.Stay == null),
                StaysCount = location.Places.Count(place => place.Stay != null),
                EventsCount = location.Events.Count,
                ImageUrl = location.Images
                    .Where(image => image.TargetType == EntityImageTargetType.Location)
                    .OrderByDescending(image => image.IsMain)
                    .ThenBy(image => image.SortOrder)
                    .ThenBy(image => image.CreatedAt)
                    .ThenBy(image => image.Id)
                    .Select(image => image.Url)
                    .FirstOrDefault()
            })
            .ToListAsync();
    }

    private async Task<List<TerritoryRegionContentItemDto>> LoadPlaceRegionContentItemsAsync(
        int regionId,
        string? search,
        bool staysOnly)
    {
        var query = _db.Places
            .AsNoTracking()
            .Where(place => place.Location.Destination.RegionId == regionId);

        query = staysOnly
            ? query.Where(place => place.Stay != null)
            : query.Where(place => place.Stay == null);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var normalizedSearch = search.Trim().ToLowerInvariant();
            query = query.Where(place =>
                place.Name.ToLower().Contains(normalizedSearch) ||
                (place.Subtitle ?? string.Empty).ToLower().Contains(normalizedSearch) ||
                place.Description.ToLower().Contains(normalizedSearch) ||
                place.Location.Name.ToLower().Contains(normalizedSearch) ||
                place.Location.Destination.Name.ToLower().Contains(normalizedSearch) ||
                place.PlaceType.Name.ToLower().Contains(normalizedSearch) ||
                (place.Stay != null && place.Stay.StayType.Name.ToLower().Contains(normalizedSearch)));
        }

        var entityType = staysOnly ? "stay" : "place";
        return await query
            .Select(place => new TerritoryRegionContentItemDto
            {
                Id = place.Id,
                EntityType = entityType,
                PublicDetailRoute = place.Status == PublishedStatus
                    ? place.Stay != null
                        ? "/stays/" + place.Id
                        : "/places/" + place.Id
                    : null,
                Name = place.Name,
                Summary = place.Subtitle ?? BuildSummary(place.Description),
                Description = place.Description,
                Status = place.Status,
                RegionId = place.Location.Destination.RegionId,
                RegionName = place.Location.Destination.Region.Name,
                DestinationId = place.Location.DestinationId,
                DestinationName = place.Location.Destination.Name,
                LocationId = place.LocationId,
                LocationName = place.Location.Name,
                PlaceId = place.Id,
                PlaceName = place.Name,
                ParentName = place.Location.Name,
                TypeName = place.Stay != null ? place.Stay.StayType.Name : place.PlaceType.Name,
                CreatedAt = place.CreatedAt,
                UpdatedAt = place.UpdatedAt,
                EventsCount = place.Events.Count,
                ReviewsCount = place.ReviewCount,
                AverageRating = place.AverageRating,
                ImageUrl = place.Images
                    .Where(image => image.TargetType == EntityImageTargetType.Place)
                    .OrderByDescending(image => image.IsMain)
                    .ThenBy(image => image.SortOrder)
                    .ThenBy(image => image.CreatedAt)
                    .ThenBy(image => image.Id)
                    .Select(image => image.Url)
                    .FirstOrDefault()
            })
            .ToListAsync();
    }

    private async Task<List<TerritoryRegionContentItemDto>> LoadEventRegionContentItemsAsync(
        int regionId,
        string? search)
    {
        var query = _db.Events
            .AsNoTracking()
            .Where(evt => evt.Destination.RegionId == regionId);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var normalizedSearch = search.Trim().ToLowerInvariant();
            query = query.Where(evt =>
                evt.Name.ToLower().Contains(normalizedSearch) ||
                (evt.Summary ?? string.Empty).ToLower().Contains(normalizedSearch) ||
                evt.Description.ToLower().Contains(normalizedSearch) ||
                evt.Destination.Name.ToLower().Contains(normalizedSearch) ||
                (evt.Location != null && evt.Location.Name.ToLower().Contains(normalizedSearch)) ||
                (evt.Place != null && evt.Place.Name.ToLower().Contains(normalizedSearch)) ||
                evt.EventType.Name.ToLower().Contains(normalizedSearch));
        }

        return await query
            .Select(evt => new TerritoryRegionContentItemDto
            {
                Id = evt.Id,
                EntityType = "event",
                PublicDetailRoute = evt.Status == PublishedStatus
                    ? "/events/" + evt.Id
                    : null,
                Name = evt.Name,
                Summary = evt.Summary ?? BuildSummary(evt.Description),
                Description = evt.Description,
                Status = evt.Status,
                RegionId = evt.Destination.RegionId,
                RegionName = evt.Destination.Region.Name,
                DestinationId = evt.DestinationId,
                DestinationName = evt.Destination.Name,
                LocationId = evt.LocationId,
                LocationName = evt.Location != null ? evt.Location.Name : null,
                PlaceId = evt.PlaceId,
                PlaceName = evt.Place != null ? evt.Place.Name : null,
                ParentName = evt.Place != null
                    ? evt.Place.Name
                    : evt.Location != null
                        ? evt.Location.Name
                        : evt.Destination.Name,
                TypeName = evt.EventType.Name,
                CreatedAt = evt.CreatedAt,
                UpdatedAt = evt.UpdatedAt,
                StartsAt = evt.StartDate,
                ImageUrl = evt.Images
                    .Where(image => image.TargetType == EntityImageTargetType.Event)
                    .OrderByDescending(image => image.IsMain)
                    .ThenBy(image => image.SortOrder)
                    .ThenBy(image => image.CreatedAt)
                    .ThenBy(image => image.Id)
                    .Select(image => image.Url)
                    .FirstOrDefault()
            })
            .ToListAsync();
    }

    private async Task ApplyContentMetricsAsync(
        List<TerritoryRegionContentItemDto> items,
        RegionPeriodRange periodRange)
    {
        await ApplyEntityMetricCountsAsync(items, "destination", new[] { AnalyticsEntityTypes.Destination }, periodRange);
        await ApplyEntityMetricCountsAsync(items, "location", new[] { AnalyticsEntityTypes.Location }, periodRange);
        await ApplyEntityMetricCountsAsync(items, "place", new[] { AnalyticsEntityTypes.Place }, periodRange);
        await ApplyEntityMetricCountsAsync(items, "stay", new[] { AnalyticsEntityTypes.Hotel, AnalyticsEntityTypes.Place }, periodRange);
        await ApplyEntityMetricCountsAsync(items, "event", new[] { AnalyticsEntityTypes.Event }, periodRange);
    }

    private async Task ApplyEntityMetricCountsAsync(
        List<TerritoryRegionContentItemDto> items,
        string itemEntityType,
        IReadOnlyCollection<string> analyticsEntityTypes,
        RegionPeriodRange periodRange)
    {
        var scopedItems = items.Where(item => item.EntityType == itemEntityType).ToList();
        var ids = scopedItems.Select(item => item.Id).Distinct().ToList();
        if (ids.Count == 0)
            return;

        var rows = await _db.AnalyticsEvents
            .AsNoTracking()
            .Where(evt =>
                evt.OccurredAt >= periodRange.Start &&
                evt.OccurredAt < periodRange.End &&
                evt.EntityId.HasValue &&
                evt.EntityType != null &&
                analyticsEntityTypes.Contains(evt.EntityType) &&
                ids.Contains(evt.EntityId.Value))
            .GroupBy(evt => evt.EntityId!.Value)
            .Select(group => new
            {
                EntityId = group.Key,
                Views = group.Count(evt => VisitEventTypes.Contains(evt.EventType)),
                Saves = group.Count(evt => SaveEventTypes.Contains(evt.EventType)),
                ExternalClicks = group.Count(evt => ExternalActionEventTypes.Contains(evt.EventType))
            })
            .ToDictionaryAsync(row => row.EntityId);

        foreach (var item in scopedItems)
        {
            if (!rows.TryGetValue(item.Id, out var metrics))
                continue;

            item.Views = metrics.Views;
            item.Saves = metrics.Saves;
            item.ExternalClicks = metrics.ExternalClicks;
        }
    }

    private async Task ApplyContentReviewMetricsAsync(List<TerritoryRegionContentItemDto> items)
    {
        await ApplyDestinationReviewMetricsAsync(items);
        await ApplyLocationReviewMetricsAsync(items);
        await ApplyPlaceReviewMetricsAsync(items);
        await ApplyEventReviewMetricsAsync(items);
    }

    private async Task ApplyDestinationReviewMetricsAsync(List<TerritoryRegionContentItemDto> items)
    {
        var ids = items.Where(item => item.EntityType == "destination").Select(item => item.Id).Distinct().ToList();
        if (ids.Count == 0)
            return;

        var rows = await _db.DestinationReviews
            .AsNoTracking()
            .Where(review => ids.Contains(review.DestinationId))
            .GroupBy(review => review.DestinationId)
            .Select(group => new { Id = group.Key, Count = group.Count(), Average = group.Average(review => review.Rating) })
            .ToDictionaryAsync(row => row.Id);

        foreach (var item in items.Where(item => item.EntityType == "destination"))
        {
            if (!rows.TryGetValue(item.Id, out var row))
                continue;

            item.ReviewsCount = row.Count;
            item.AverageRating = Math.Round(row.Average, 1, MidpointRounding.AwayFromZero);
        }
    }

    private async Task ApplyLocationReviewMetricsAsync(List<TerritoryRegionContentItemDto> items)
    {
        var ids = items.Where(item => item.EntityType == "location").Select(item => item.Id).Distinct().ToList();
        if (ids.Count == 0)
            return;

        var rows = await _db.LocationReviews
            .AsNoTracking()
            .Where(review => ids.Contains(review.LocationId))
            .GroupBy(review => review.LocationId)
            .Select(group => new { Id = group.Key, Count = group.Count(), Average = group.Average(review => review.Rating) })
            .ToDictionaryAsync(row => row.Id);

        foreach (var item in items.Where(item => item.EntityType == "location"))
        {
            if (!rows.TryGetValue(item.Id, out var row))
                continue;

            item.ReviewsCount = row.Count;
            item.AverageRating = Math.Round(row.Average, 1, MidpointRounding.AwayFromZero);
        }
    }

    private async Task ApplyPlaceReviewMetricsAsync(List<TerritoryRegionContentItemDto> items)
    {
        var ids = items
            .Where(item => item.EntityType == "place" || item.EntityType == "stay")
            .Select(item => item.Id)
            .Distinct()
            .ToList();
        if (ids.Count == 0)
            return;

        var rows = await _db.PlaceReviews
            .AsNoTracking()
            .Where(review => ids.Contains(review.PlaceId))
            .GroupBy(review => review.PlaceId)
            .Select(group => new { Id = group.Key, Count = group.Count(), Average = group.Average(review => review.Rating) })
            .ToDictionaryAsync(row => row.Id);

        foreach (var item in items.Where(item => item.EntityType == "place" || item.EntityType == "stay"))
        {
            if (!rows.TryGetValue(item.Id, out var row))
                continue;

            item.ReviewsCount = row.Count;
            item.AverageRating = Math.Round(row.Average, 1, MidpointRounding.AwayFromZero);
        }
    }

    private async Task ApplyEventReviewMetricsAsync(List<TerritoryRegionContentItemDto> items)
    {
        var ids = items.Where(item => item.EntityType == "event").Select(item => item.Id).Distinct().ToList();
        if (ids.Count == 0)
            return;

        var rows = await _db.EventReviews
            .AsNoTracking()
            .Where(review => ids.Contains(review.EventId))
            .GroupBy(review => review.EventId)
            .Select(group => new { Id = group.Key, Count = group.Count(), Average = group.Average(review => review.Rating) })
            .ToDictionaryAsync(row => row.Id);

        foreach (var item in items.Where(item => item.EntityType == "event"))
        {
            if (!rows.TryGetValue(item.Id, out var row))
                continue;

            item.ReviewsCount = row.Count;
            item.AverageRating = Math.Round(row.Average, 1, MidpointRounding.AwayFromZero);
        }
    }

    private static List<TerritoryRegionContentItemDto> SortRegionContentItems(
        List<TerritoryRegionContentItemDto> items,
        string entityType,
        string? sort)
    {
        var normalized = NormalizeSort(sort);
        IEnumerable<TerritoryRegionContentItemDto> ordered = normalized switch
        {
            "newest" => items.OrderByDescending(item => item.CreatedAt).ThenBy(item => item.Name),
            "oldest" => items.OrderBy(item => item.CreatedAt).ThenBy(item => item.Name),
            "name-desc" => items.OrderByDescending(item => item.Name).ThenByDescending(item => item.Id),
            "views" => items.OrderByDescending(item => item.Views).ThenBy(item => item.Name),
            "saves" => items.OrderByDescending(item => item.Saves).ThenBy(item => item.Name),
            "rating" => items.OrderByDescending(item => item.AverageRating ?? -1).ThenBy(item => item.Name),
            "upcoming" when entityType == "events" || entityType == "all" => items
                .OrderBy(item => item.StartsAt ?? DateTime.MaxValue)
                .ThenBy(item => item.Name),
            "most-locations" => items.OrderByDescending(item => item.LocationsCount).ThenBy(item => item.Name),
            "most-places" => items.OrderByDescending(item => item.PlacesCount + item.StaysCount).ThenBy(item => item.Name),
            "most-events" => items.OrderByDescending(item => item.EventsCount).ThenBy(item => item.Name),
            "health-high" => items.OrderByDescending(item => item.Health?.Score ?? -1).ThenBy(item => item.Name),
            "health-low" => items.OrderBy(item => item.Health?.Score ?? 101).ThenBy(item => item.Name),
            _ => items.OrderBy(item => item.Name).ThenBy(item => item.Id)
        };

        return ordered.ToList();
    }

    private async Task<Dictionary<int, TerritoryHealthDto>> BuildDestinationHealthMapAsync(
        List<int> destinationIds,
        int regionId,
        RegionPeriodRange periodRange)
    {
        var rows = await _db.Destinations
            .AsNoTracking()
            .Where(destination => destination.RegionId == regionId && destinationIds.Contains(destination.Id))
            .Select(destination => new DestinationHealthSource
            {
                Id = destination.Id,
                Name = destination.Name,
                HasHeroImage = destination.Images.Any(image => image.TargetType == EntityImageTargetType.Destination),
                HasDescription = destination.Description != string.Empty || destination.Summary != null,
                HasTags = destination.DestinationTags.Any(),
                HasCoordinates = destination.Latitude != 0 || destination.Longitude != 0,
                HasBoundary = destination.BoundaryShapeType != null || destination.BoundaryNote != null,
                LocationsCount = destination.Locations.Count,
                PlacesCount = destination.Locations
                    .SelectMany(location => location.Places)
                    .Count(place => place.Stay == null),
                StaysCount = destination.Locations
                    .SelectMany(location => location.Places)
                    .Count(place => place.Stay != null),
                EventsCount = destination.Events.Count,
                CreatedAt = destination.CreatedAt,
                UpdatedAt = destination.UpdatedAt
            })
            .ToListAsync();

        var views = await GetEntityMetricCountsAsync(
            destinationIds,
            new[] { AnalyticsEntityTypes.Destination },
            VisitEventTypes,
            periodRange);
        var saves = await GetEntityMetricCountsAsync(
            destinationIds,
            new[] { AnalyticsEntityTypes.Destination },
            SaveEventTypes,
            periodRange);
        var reviews = await _db.DestinationReviews
            .AsNoTracking()
            .Where(review => destinationIds.Contains(review.DestinationId))
            .GroupBy(review => review.DestinationId)
            .Select(group => new { Id = group.Key, Count = group.Count(), Average = group.Average(review => review.Rating) })
            .ToDictionaryAsync(row => row.Id);
        var pending = await GetDestinationPendingRequestCountsAsync(destinationIds);

        return rows.ToDictionary(
            row => row.Id,
            row => BuildDestinationHealth(
                row,
                views.GetValueOrDefault(row.Id),
                saves.GetValueOrDefault(row.Id),
                reviews.TryGetValue(row.Id, out var review) ? review.Count : 0,
                reviews.TryGetValue(row.Id, out var rating) ? rating.Average : 0,
                pending.GetValueOrDefault(row.Id)));
    }

    private async Task<Dictionary<int, int>> GetEntityMetricCountsAsync(
        List<int> entityIds,
        IReadOnlyCollection<string> entityTypes,
        IReadOnlyCollection<string> eventTypes,
        RegionPeriodRange periodRange)
    {
        if (entityIds.Count == 0)
            return new Dictionary<int, int>();

        return await _db.AnalyticsEvents
            .AsNoTracking()
            .Where(evt =>
                evt.OccurredAt >= periodRange.Start &&
                evt.OccurredAt < periodRange.End &&
                evt.EntityId.HasValue &&
                evt.EntityType != null &&
                entityTypes.Contains(evt.EntityType) &&
                eventTypes.Contains(evt.EventType) &&
                entityIds.Contains(evt.EntityId.Value))
            .GroupBy(evt => evt.EntityId!.Value)
            .Select(group => new { Id = group.Key, Count = group.Count() })
            .ToDictionaryAsync(row => row.Id, row => row.Count);
    }

    private async Task<Dictionary<int, int>> GetDestinationPendingRequestCountsAsync(List<int> destinationIds)
    {
        if (destinationIds.Count == 0)
            return new Dictionary<int, int>();

        return await _db.PlaceContributions
            .AsNoTracking()
            .Where(contribution =>
                contribution.Status == PlaceContributionStatus.PendingReview &&
                ((contribution.LocationId.HasValue &&
                  contribution.Location != null &&
                  destinationIds.Contains(contribution.Location.DestinationId)) ||
                 (contribution.PlaceId.HasValue &&
                  contribution.Place != null &&
                  destinationIds.Contains(contribution.Place.Location.DestinationId))))
            .GroupBy(contribution => contribution.LocationId.HasValue
                ? contribution.Location!.DestinationId
                : contribution.Place!.Location.DestinationId)
            .Select(group => new { DestinationId = group.Key, Count = group.Count() })
            .ToDictionaryAsync(row => row.DestinationId, row => row.Count);
    }

    private static TerritoryHealthDto BuildDestinationHealth(
        DestinationHealthSource source,
        int views,
        int saves,
        int reviews,
        double averageRating,
        int pendingRequests)
    {
        var drivers = new List<TerritoryHealthInsightDto>();
        var recommendations = new List<TerritoryHealthInsightDto>();

        var completenessScore = 0;
        AddSignal(source.HasHeroImage, 8, "Hero image", "Destination has a hero image.", "Add a hero image to improve map and card visibility.");
        AddSignal(source.HasDescription, 8, "Description", "Destination has descriptive copy.", "Add a concise destination description for visitors and reviewers.");
        AddSignal(source.HasTags, 7, "Tags", "Destination has discovery tags.", "Add tags so this destination can appear in dynamic Explore sections.");
        AddSignal(source.HasCoordinates, 7, "Coordinates", "Destination has coordinates.", "Verify destination coordinates for better map filtering.");
        AddSignal(source.HasBoundary, 10, "Boundary", "Destination has boundary metadata.", "Add or verify boundary geometry for better map filtering.");

        var coverageScore = 0;
        if (source.LocationsCount > 0)
            coverageScore += 8;
        else
            recommendations.Add(HealthRecommendation("Add at least one location to improve regional structure."));

        if (source.PlacesCount + source.StaysCount >= 3)
            coverageScore += 10;
        else if (source.PlacesCount + source.StaysCount > 0)
            coverageScore += 5;
        else
            recommendations.Add(HealthRecommendation("Add more places or stays to improve content coverage."));

        if (source.EventsCount > 0)
            coverageScore += 7;
        else
            recommendations.Add(HealthRecommendation("Add upcoming events when this destination has seasonal or local activity."));

        var engagementScore =
            Math.Min(8, views) +
            Math.Min(5, saves * 2) +
            Math.Min(7, reviews * 2);

        if (views == 0)
        {
            drivers.Add(HealthDriver("No recent views", "No destination views were recorded in the selected period.", "warning"));
            recommendations.Add(HealthRecommendation("Improve imagery, tags, and content depth to increase destination discovery."));
        }

        if (reviews == 0)
        {
            drivers.Add(HealthDriver("No reviews", "No destination reviews are available yet.", "warning"));
            recommendations.Add(HealthRecommendation("Encourage reviews to improve confidence score."));
        }
        else if (averageRating < 3.5)
        {
            drivers.Add(HealthDriver("Low rating", $"Average rating is {Math.Round(averageRating, 1)}.", "warning"));
            recommendations.Add(HealthRecommendation("Review recent visitor feedback and prioritize quality improvements."));
        }

        var moderationScore = Math.Max(0, 10 - Math.Min(10, pendingRequests * 2));
        if (pendingRequests > 0)
        {
            drivers.Add(HealthDriver("Pending requests", $"{pendingRequests} pending request(s) need review.", "warning"));
            recommendations.Add(HealthRecommendation("Review pending contributions for this destination."));
        }

        var lastTouched = source.UpdatedAt ?? source.CreatedAt;
        var recencyScore = lastTouched >= DateTime.UtcNow.AddDays(-180) ? 5 : 0;
        if (recencyScore == 0)
        {
            recommendations.Add(HealthRecommendation("Refresh stale content so visitors see current destination information."));
        }

        var score = Math.Clamp(
            completenessScore + coverageScore + engagementScore + moderationScore + recencyScore,
            0,
            100);

        if (drivers.Count == 0)
        {
            drivers.Add(HealthDriver("Healthy baseline", "Core destination content and activity signals are present.", "positive"));
        }

        return new TerritoryHealthDto
        {
            Score = score,
            StatusLabel = BuildHealthStatusLabel(score),
            Drivers = drivers,
            Recommendations = recommendations.Take(6).ToList()
        };

        void AddSignal(bool present, int points, string label, string presentDetail, string recommendation)
        {
            if (present)
            {
                completenessScore += points;
                return;
            }

            drivers.Add(HealthDriver($"Missing {label.ToLowerInvariant()}", $"{label} is not present.", "warning"));
            recommendations.Add(HealthRecommendation(recommendation));
        }
    }

    private TerritoryHealthDto BuildRegionHealthDto(
        RegionContentCounts counts,
        RegionQualityAggregate quality,
        int visits,
        int saves,
        int reviews,
        int pendingRequests)
    {
        var health = CalculateRegionHealth(counts, quality, visits, saves, reviews, pendingRequests);

        if (!health.Score.HasValue)
        {
            health.Recommendations.Add(HealthRecommendation("Add at least one destination before calculating region health."));
            return health;
        }

        if (counts.Destinations == 0)
            health.Recommendations.Add(HealthRecommendation("Add destinations to establish region coverage."));
        if (counts.Locations == 0)
            health.Recommendations.Add(HealthRecommendation("Add locations under destinations to improve geographic coverage."));
        if (counts.Places + counts.Stays == 0)
            health.Recommendations.Add(HealthRecommendation("Add places or stays to make the region useful for visitors."));
        if (counts.Events == 0)
            health.Recommendations.Add(HealthRecommendation("Add events when the region has current local activity."));
        if (quality.QualityRatio < 0.65)
            health.Recommendations.Add(HealthRecommendation("Fill missing images, descriptions, tags, and coordinates across low-completeness content."));
        if (pendingRequests > 0)
            health.Recommendations.Add(HealthRecommendation("Review pending contributions and reports to reduce moderation backlog."));
        if (visits == 0)
            health.Recommendations.Add(HealthRecommendation("Improve content completeness and discovery tags to increase region engagement."));
        if (reviews == 0)
            health.Recommendations.Add(HealthRecommendation("Encourage reviews on destination, place, and event pages."));

        health.Recommendations = health.Recommendations.Take(6).ToList();
        return health;
    }

    private static TerritoryHealthDto CalculateRegionHealth(
        RegionContentCounts counts,
        RegionQualityAggregate quality,
        int visits,
        int saves,
        int reviews,
        int pendingRequests)
    {
        var totalContent = counts.Destinations + counts.Locations + counts.Places + counts.Stays + counts.Events;
        if (totalContent == 0)
        {
            return new TerritoryHealthDto
            {
                Score = null,
                StatusLabel = "Insufficient data",
                Drivers = new List<TerritoryHealthInsightDto>
                {
                    HealthDriver("No regional content", "Health is not calculated until content exists in the region.", "info")
                }
            };
        }

        // Region health uses only stored signals:
        // 25 pts content coverage, 25 pts content completeness,
        // 25 pts engagement, and 25 pts operational freshness/moderation.
        var coverageScore = 0;
        if (counts.Destinations > 0) coverageScore += 7;
        if (counts.Locations > 0) coverageScore += 6;
        if (counts.Places + counts.Stays > 0) coverageScore += 7;
        if (counts.Events > 0) coverageScore += 5;

        var completenessScore = quality.PossibleSignals <= 0
            ? 0
            : (int)Math.Round(25.0 * quality.CompletedSignals / quality.PossibleSignals);

        var engagementScore =
            Math.Min(10, visits) +
            Math.Min(7, saves * 2) +
            Math.Min(8, reviews * 2);

        var freshnessScore = quality.TotalItems <= 0
            ? 0
            : (int)Math.Round(10.0 * quality.RecentlyUpdatedItems / quality.TotalItems);
        var moderationScore = Math.Max(0, 15 - Math.Min(15, pendingRequests * 3));

        var score = Math.Clamp(
            coverageScore + completenessScore + engagementScore + freshnessScore + moderationScore,
            0,
            100);

        var drivers = new List<TerritoryHealthInsightDto>
        {
            HealthDriver("Content coverage", $"{totalContent} content item(s) are present in the region.", "info"),
            HealthDriver("Completeness", $"{Math.Round(quality.QualityRatio * 100)}% of tracked quality signals are complete.", "info")
        };

        if (pendingRequests > 0)
        {
            drivers.Add(HealthDriver("Moderation backlog", $"{pendingRequests} pending request(s) reduce operational health.", "warning"));
        }

        if (visits == 0)
        {
            drivers.Add(HealthDriver("Low engagement", "No visits were recorded in the selected period.", "warning"));
        }

        return new TerritoryHealthDto
        {
            Score = score,
            StatusLabel = BuildHealthStatusLabel(score),
            Drivers = drivers
        };
    }

    private static TerritoryHealthInsightDto HealthDriver(string label, string detail, string severity) =>
        new()
        {
            Label = label,
            Detail = detail,
            Severity = severity
        };

    private static TerritoryHealthInsightDto HealthRecommendation(string detail) =>
        new()
        {
            Label = "Recommendation",
            Detail = detail,
            Severity = "action"
        };

    private static string BuildHealthStatusLabel(int score)
    {
        if (score >= 85)
            return "Healthy";
        if (score >= 70)
            return "Stable";
        if (score >= 50)
            return "Needs attention";

        return "Critical";
    }

    private async Task<Dictionary<int, RegionQualityAggregate>> GetRegionQualityAggregatesAsync(List<int> regionIds)
    {
        if (regionIds.Count == 0)
            return new Dictionary<int, RegionQualityAggregate>();

        var recentCutoff = DateTime.UtcNow.AddDays(-180);
        var rows = new List<QualitySignalRow>();

        rows.AddRange(await _db.Destinations
            .AsNoTracking()
            .Where(destination => regionIds.Contains(destination.RegionId))
            .Select(destination => new QualitySignalRow
            {
                RegionId = destination.RegionId,
                PossibleSignals = 5,
                CompletedSignals =
                    (destination.Images.Any(image => image.TargetType == EntityImageTargetType.Destination) ? 1 : 0) +
                    ((destination.Description != string.Empty || destination.Summary != null) ? 1 : 0) +
                    (destination.DestinationTags.Any() ? 1 : 0) +
                    ((destination.Latitude != 0 || destination.Longitude != 0) ? 1 : 0) +
                    ((destination.BoundaryShapeType != null || destination.BoundaryNote != null) ? 1 : 0),
                RecentlyUpdated = (destination.UpdatedAt ?? destination.CreatedAt) >= recentCutoff
            })
            .ToListAsync());

        rows.AddRange(await _db.Locations
            .AsNoTracking()
            .Where(location => regionIds.Contains(location.Destination.RegionId))
            .Select(location => new QualitySignalRow
            {
                RegionId = location.Destination.RegionId,
                PossibleSignals = 5,
                CompletedSignals =
                    (location.Images.Any(image => image.TargetType == EntityImageTargetType.Location) ? 1 : 0) +
                    ((location.Description != string.Empty || location.Summary != null) ? 1 : 0) +
                    (location.LocationTags.Any() ? 1 : 0) +
                    ((location.Latitude != 0 || location.Longitude != 0) ? 1 : 0) +
                    ((location.BoundaryShapeType != null || location.BoundaryNote != null) ? 1 : 0),
                RecentlyUpdated = (location.UpdatedAt ?? location.CreatedAt) >= recentCutoff
            })
            .ToListAsync());

        rows.AddRange(await _db.Places
            .AsNoTracking()
            .Where(place => regionIds.Contains(place.Location.Destination.RegionId))
            .Select(place => new QualitySignalRow
            {
                RegionId = place.Location.Destination.RegionId,
                PossibleSignals = 5,
                CompletedSignals =
                    (place.Images.Any(image => image.TargetType == EntityImageTargetType.Place) ? 1 : 0) +
                    ((place.Description != string.Empty || place.Subtitle != null) ? 1 : 0) +
                    (place.PlaceTags.Any() ? 1 : 0) +
                    ((place.Latitude != 0 || place.Longitude != 0 || place.Address != null) ? 1 : 0) +
                    (place.PlaceTypeId > 0 ? 1 : 0),
                RecentlyUpdated = (place.UpdatedAt ?? place.CreatedAt) >= recentCutoff
            })
            .ToListAsync());

        rows.AddRange(await _db.Events
            .AsNoTracking()
            .Where(evt => regionIds.Contains(evt.Destination.RegionId))
            .Select(evt => new QualitySignalRow
            {
                RegionId = evt.Destination.RegionId,
                PossibleSignals = 5,
                CompletedSignals =
                    (evt.Images.Any(image => image.TargetType == EntityImageTargetType.Event) ? 1 : 0) +
                    ((evt.Description != string.Empty || evt.Summary != null) ? 1 : 0) +
                    (evt.EventTags.Any() ? 1 : 0) +
                    (evt.StartDate != default ? 1 : 0) +
                    ((evt.LocationId.HasValue || evt.PlaceId.HasValue || evt.DestinationId > 0) ? 1 : 0),
                RecentlyUpdated = (evt.UpdatedAt ?? evt.CreatedAt) >= recentCutoff
            })
            .ToListAsync());

        var grouped = rows
            .GroupBy(row => row.RegionId)
            .ToDictionary(
                group => group.Key,
                group => new RegionQualityAggregate(
                    group.Count(),
                    group.Sum(row => row.CompletedSignals),
                    group.Sum(row => row.PossibleSignals),
                    group.Count(row => row.RecentlyUpdated)));

        return regionIds.ToDictionary(
            regionId => regionId,
            regionId => grouped.GetValueOrDefault(regionId, RegionQualityAggregate.Empty));
    }

    private async Task<Dictionary<int, RegionReviewAggregate>> GetRegionReviewCountsAsync(List<int> regionIds)
    {
        if (regionIds.Count == 0)
            return new Dictionary<int, RegionReviewAggregate>();

        var aggregates = regionIds.ToDictionary(regionId => regionId, _ => new RegionReviewAggregate());

        foreach (var row in await _db.DestinationReviews
            .AsNoTracking()
            .Where(review => regionIds.Contains(review.Destination.RegionId))
            .GroupBy(review => review.Destination.RegionId)
            .Select(group => new { RegionId = group.Key, Count = group.Count(), Sum = group.Sum(review => review.Rating) })
            .ToListAsync())
        {
            aggregates[row.RegionId].Add(row.Count, row.Sum);
        }

        foreach (var row in await _db.LocationReviews
            .AsNoTracking()
            .Where(review => regionIds.Contains(review.Location.Destination.RegionId))
            .GroupBy(review => review.Location.Destination.RegionId)
            .Select(group => new { RegionId = group.Key, Count = group.Count(), Sum = group.Sum(review => review.Rating) })
            .ToListAsync())
        {
            aggregates[row.RegionId].Add(row.Count, row.Sum);
        }

        foreach (var row in await _db.PlaceReviews
            .AsNoTracking()
            .Where(review => regionIds.Contains(review.Place.Location.Destination.RegionId))
            .GroupBy(review => review.Place.Location.Destination.RegionId)
            .Select(group => new { RegionId = group.Key, Count = group.Count(), Sum = group.Sum(review => review.Rating) })
            .ToListAsync())
        {
            aggregates[row.RegionId].Add(row.Count, row.Sum);
        }

        foreach (var row in await _db.EventReviews
            .AsNoTracking()
            .Where(review => regionIds.Contains(review.Event.Destination.RegionId))
            .GroupBy(review => review.Event.Destination.RegionId)
            .Select(group => new { RegionId = group.Key, Count = group.Count(), Sum = group.Sum(review => review.Rating) })
            .ToListAsync())
        {
            aggregates[row.RegionId].Add(row.Count, row.Sum);
        }

        return aggregates;
    }

    private async Task<RegionReviewAggregate> GetRegionReviewAggregateAsync(int regionId)
    {
        var result = await GetRegionReviewCountsAsync(new List<int> { regionId });
        return result.GetValueOrDefault(regionId, new RegionReviewAggregate());
    }

    private async Task<List<TerritoryRegionManagerDto>> GetDetailedRegionManagersAsync(int regionId)
    {
        var rows = await _db.RegionalManagerRegions
            .AsNoTracking()
            .Where(assignment => assignment.RegionId == regionId && assignment.IsActive)
            .OrderBy(assignment => assignment.User.DisplayName)
            .ThenBy(assignment => assignment.User.Email)
            .Select(assignment => new
            {
                UserId = assignment.UserId,
                assignment.User.DisplayName,
                assignment.User.FirstName,
                assignment.User.LastName,
                assignment.User.Username,
                Email = assignment.User.Email,
                AvatarUrl = assignment.User.AvatarUrl,
                AssignedAt = assignment.CreatedAt
            })
            .ToListAsync();

        return rows.Select(row =>
        {
            var displayName = BuildDisplayName(row.DisplayName, row.FirstName, row.LastName, row.Username);
            return new TerritoryRegionManagerDto
            {
                UserId = row.UserId,
                DisplayName = displayName,
                Email = row.Email,
                Initials = BuildManagerInitials(displayName, row.Email),
                AvatarUrl = row.AvatarUrl,
                Role = RoleNames.RegionalManager,
                AssignedAt = row.AssignedAt
            };
        }).ToList();
    }

    private async Task<RegionScopeRow> GetAuthorizedRegionAsync(int userId, int regionId, int? territoryId)
    {
        await EnsureTerritoryManagerRoleAsync(userId);

        var region = await _db.Regions
            .AsNoTracking()
            .Where(item => item.Id == regionId)
            .Select(item => new RegionScopeRow
            {
                RegionId = item.Id,
                RegionName = item.Name,
                TerritoryId = item.TerritoryId,
                TerritoryName = item.Territory.Name,
                ImageUrl = item.Images
                    .Where(image => image.TargetType == EntityImageTargetType.Region)
                    .OrderByDescending(image => image.IsMain)
                    .ThenBy(image => image.SortOrder)
                    .ThenBy(image => image.CreatedAt)
                    .ThenBy(image => image.Id)
                    .Select(image => image.Url)
                    .FirstOrDefault(),
                CenterLatitude = item.Destinations
                    .OrderBy(destination => destination.Name)
                    .Select(destination => (double?)destination.Latitude)
                    .FirstOrDefault(),
                CenterLongitude = item.Destinations
                    .OrderBy(destination => destination.Name)
                    .Select(destination => (double?)destination.Longitude)
                    .FirstOrDefault()
            })
            .SingleOrDefaultAsync()
            ?? throw new KeyNotFoundException("Region not found.");

        if (territoryId.HasValue && region.TerritoryId != territoryId.Value)
            throw new UnauthorizedAccessException("Region is not in the requested territory.");

        var isAssigned = await _db.TerritoryManagerTerritories
            .AsNoTracking()
            .AnyAsync(assignment => assignment.UserId == userId && assignment.TerritoryId == region.TerritoryId);

        if (!isAssigned)
            throw new UnauthorizedAccessException("Territory is not assigned to the current territory manager.");

        return region;
    }

    private static RegionPeriodRange ResolveRegionPeriod(string? period)
    {
        var now = DateTime.UtcNow;
        var normalized = string.IsNullOrWhiteSpace(period)
            ? "30d"
            : period.Trim().ToLowerInvariant();

        return normalized switch
        {
            "7d" or "week" => new RegionPeriodRange(now.AddDays(-7), now),
            "90d" or "quarter" => new RegionPeriodRange(now.AddDays(-90), now),
            "all" or "all-time" => new RegionPeriodRange(DateTime.SpecifyKind(DateTime.MinValue, DateTimeKind.Utc), now),
            "30d" or "month" => new RegionPeriodRange(now.AddDays(-30), now),
            _ => throw new InvalidOperationException("Unsupported period.")
        };
    }

    private async Task<List<AnalyticsRegionHit>> GetRegionAnalyticsHitsAsync(
        int regionId,
        DateTime periodStart,
        DateTime periodEnd,
        bool visitOnly)
    {
        var territoryId = await _db.Regions
            .AsNoTracking()
            .Where(region => region.Id == regionId)
            .Select(region => region.TerritoryId)
            .SingleAsync();

        var hits = await GetTerritoryAnalyticsHitsAsync(territoryId, periodStart, periodEnd, visitOnly);
        return hits.Where(hit => hit.RegionId == regionId).ToList();
    }

    private async Task<List<TerritoryRegionTopEntityDto>> BuildRegionTopEntitiesAsync(
        int regionId,
        RegionPeriodRange periodRange)
    {
        var items = await GetRegionContentItemsAsync(regionId, "all", null, "views", periodRange);
        return items
            .Where(item => item.Views > 0)
            .OrderByDescending(item => item.Views)
            .ThenBy(item => item.Name)
            .Take(5)
            .Select(item => new TerritoryRegionTopEntityDto
            {
                EntityType = item.EntityType,
                EntityId = item.Id,
                Name = item.Name,
                Views = item.Views,
                ImageUrl = item.ImageUrl
            })
            .ToList();
    }

    private async Task<List<TerritoryRegionActivityDto>> BuildRegionRecentActivityAsync(
        int regionId,
        RegionPeriodRange periodRange)
    {
        var hits = await GetRegionAnalyticsHitsAsync(regionId, periodRange.Start, periodRange.End, visitOnly: false);
        var recent = hits
            .OrderByDescending(hit => hit.OccurredAt)
            .Take(10)
            .ToList();

        if (recent.Count == 0)
            return new List<TerritoryRegionActivityDto>();

        var names = await BuildEntityNameMapAsync(regionId);
        return recent.Select(hit => new TerritoryRegionActivityDto
        {
            EventType = hit.EventType,
            EntityType = hit.EntityType,
            EntityId = hit.EntityId,
            EntityName = hit.EntityType is not null && hit.EntityId.HasValue
                ? names.GetValueOrDefault(new EntityKey(hit.EntityType, hit.EntityId.Value))
                : null,
            OccurredAt = hit.OccurredAt
        }).ToList();
    }

    private async Task<Dictionary<EntityKey, string>> BuildEntityNameMapAsync(int regionId)
    {
        var map = new Dictionary<EntityKey, string>();

        foreach (var item in await _db.Destinations
            .AsNoTracking()
            .Where(destination => destination.RegionId == regionId)
            .Select(destination => new { destination.Id, destination.Name })
            .ToListAsync())
        {
            map[new EntityKey(AnalyticsEntityTypes.Destination, item.Id)] = item.Name;
        }

        foreach (var item in await _db.Locations
            .AsNoTracking()
            .Where(location => location.Destination.RegionId == regionId)
            .Select(location => new { location.Id, location.Name })
            .ToListAsync())
        {
            map[new EntityKey(AnalyticsEntityTypes.Location, item.Id)] = item.Name;
        }

        foreach (var item in await _db.Places
            .AsNoTracking()
            .Where(place => place.Location.Destination.RegionId == regionId)
            .Select(place => new { place.Id, place.Name, IsStay = place.Stay != null })
            .ToListAsync())
        {
            map[new EntityKey(AnalyticsEntityTypes.Place, item.Id)] = item.Name;
            if (item.IsStay)
            {
                map[new EntityKey(AnalyticsEntityTypes.Hotel, item.Id)] = item.Name;
            }
        }

        foreach (var item in await _db.Events
            .AsNoTracking()
            .Where(evt => evt.Destination.RegionId == regionId)
            .Select(evt => new { evt.Id, evt.Name })
            .ToListAsync())
        {
            map[new EntityKey(AnalyticsEntityTypes.Event, item.Id)] = item.Name;
        }

        map[new EntityKey(AnalyticsEntityTypes.Region, regionId)] = await _db.Regions
            .AsNoTracking()
            .Where(region => region.Id == regionId)
            .Select(region => region.Name)
            .SingleAsync();

        return map;
    }

    private async Task<TerritoryAssignedTerritoryDto?> GetAuthorizedTerritoryAsync(int userId, int? territoryId)
    {
        var assignedTerritories = await GetAssignedTerritoriesAsync(userId);
        return ResolveActiveTerritory(assignedTerritories, territoryId);
    }

    private static TerritoryAssignedTerritoryDto? ResolveActiveTerritory(
        List<TerritoryAssignedTerritoryDto> assignedTerritories,
        int? territoryId)
    {
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

    private async Task EnsureTerritoryManagerRoleAsync(int userId)
    {
        var roles = await _db.UserRoles
            .AsNoTracking()
            .Where(userRole => userRole.UserId == userId)
            .Select(userRole => userRole.Role.Name)
            .ToListAsync();

        if (!roles.Any(role => RoleNames.Normalize(role) == RoleNames.TerritoryManager))
            throw new UnauthorizedAccessException("Territory manager role is required.");
    }

    private static string? BuildManagerDisplayName(List<ManagerRow> managers)
    {
        if (managers.Count == 0)
            return null;

        return managers.Count == 1
            ? managers[0].DisplayName
            : $"{managers[0].DisplayName} +{managers.Count - 1}";
    }

    private static TerritoryRegionManagerDto BuildManagerDto(ManagerRow manager) =>
        new()
        {
            UserId = manager.UserId,
            DisplayName = manager.DisplayName,
            Email = manager.Email,
            Initials = manager.Initials,
            AvatarUrl = manager.AvatarUrl,
            Role = RoleNames.RegionalManager,
            AssignedAt = manager.AssignedAt
        };

    private static string BuildDisplayName(string? displayName, string? firstName, string? lastName, string? username)
    {
        if (!string.IsNullOrWhiteSpace(displayName))
            return displayName.Trim();

        var fullName = string.Join(" ", new[] { firstName, lastName }
            .Where(part => !string.IsNullOrWhiteSpace(part))
            .Select(part => part!.Trim()));

        if (!string.IsNullOrWhiteSpace(fullName))
            return fullName;

        return username?.Trim() ?? string.Empty;
    }

    private static string BuildSummary(string? description)
    {
        var value = description?.Trim() ?? string.Empty;
        if (value.Length <= 160)
            return value;

        return value[..157].TrimEnd() + "...";
    }

    private static string? BuildInitials(string displayName)
    {
        var parts = displayName.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
            return null;

        return string.Concat(parts.Take(2).Select(part => char.ToUpperInvariant(part[0])));
    }

    private static string BuildManagerInitials(string displayName, string email)
    {
        var initials = BuildInitials(displayName);
        if (!string.IsNullOrWhiteSpace(initials))
            return initials;

        var emailName = email.Split('@', StringSplitOptions.TrimEntries).FirstOrDefault() ?? string.Empty;
        initials = BuildInitials(emailName.Replace('.', ' ').Replace('_', ' '));
        return string.IsNullOrWhiteSpace(initials) ? "?" : initials;
    }

    private static int CalculateActivityScore(
        int visits,
        int maxVisits,
        int contentCount,
        int maxContent,
        int pendingRequests)
    {
        // Real-data score: baseline 50, up to 25 relative-visit points,
        // up to 15 relative-inventory points, minus up to 20 backlog points.
        var visitScore = maxVisits <= 0 ? 0 : (int)Math.Round(25.0 * visits / maxVisits);
        var contentScore = maxContent <= 0 ? 0 : (int)Math.Round(15.0 * contentCount / maxContent);
        var backlogPenalty = Math.Min(20, pendingRequests * 4);

        return Math.Clamp(50 + visitScore + contentScore - backlogPenalty, 0, 100);
    }

    private static double CalculateChangePercent(int current, int previous)
    {
        if (previous <= 0)
            return current <= 0 ? 0 : 100;

        return Math.Round(((double)(current - previous) / previous) * 100, 1, MidpointRounding.AwayFromZero);
    }

    private static string NormalizeContentType(string? type)
    {
        var normalized = (type ?? "destinations").Trim().ToLowerInvariant();
        return normalized switch
        {
            "destination" or "destinations" => "destinations",
            "location" or "locations" => "locations",
            "place" or "places" => "places",
            "event" or "events" => "events",
            _ => throw new InvalidOperationException("Unsupported content directory type.")
        };
    }

    private static string NormalizeRegionContentType(string? type)
    {
        var normalized = (type ?? "destinations").Trim().ToLowerInvariant();
        return normalized switch
        {
            "all" => "all",
            "destination" or "destinations" => "destinations",
            "location" or "locations" => "locations",
            "place" or "places" => "places",
            "stay" or "stays" or "accommodation" or "accommodations" => "stays",
            "event" or "events" => "events",
            _ => throw new InvalidOperationException("Unsupported region content type.")
        };
    }

    private static string NormalizeRegionSort(string? sort)
    {
        var normalized = NormalizeSort(sort);
        return normalized switch
        {
            "" or "status" or "status-active-first" or "active-first" => "status-active-first",
            "name" or "name-asc" => "name",
            "name-desc" => "name-desc",
            "content" => "content",
            "destinations" or "most-destinations" => "most-destinations",
            "locations" or "most-locations" => "most-locations",
            "places" or "most-places" => "most-places",
            "events" or "most-events" => "most-events",
            "pending" or "pending-requests" or "most-pending-requests" => "most-pending-requests",
            "health" or "health-high" or "activity-high" => "health-high",
            "health-low" or "activity-low" => "health-low",
            "newest" => "newest",
            "oldest" => "oldest",
            _ => "status-active-first"
        };
    }

    private static string NormalizeSort(string? sort) =>
        string.IsNullOrWhiteSpace(sort) ? string.Empty : sort.Trim().ToLowerInvariant();

    private sealed class RegionBaseRow
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public int TerritoryId { get; set; }
        public string TerritoryName { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
        public string? ImageUrl { get; set; }
    }

    private sealed class ManagerRow
    {
        public int RegionId { get; set; }
        public int UserId { get; set; }
        public string DisplayName { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string Initials { get; set; } = string.Empty;
        public string? AvatarUrl { get; set; }
        public DateTime AssignedAt { get; set; }
    }

    private sealed class RawManagerRow
    {
        public int RegionId { get; set; }
        public int UserId { get; set; }
        public string? DisplayName { get; set; }
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        public string? Username { get; set; }
        public string Email { get; set; } = string.Empty;
        public string? AvatarUrl { get; set; }
        public DateTime AssignedAt { get; set; }
    }

    private sealed class AnalyticsEventBaseRow
    {
        public int EventId { get; set; }
        public int? UserId { get; set; }
        public string EventType { get; set; } = string.Empty;
        public string? EntityType { get; set; }
        public int? EntityId { get; set; }
        public int? DestinationId { get; set; }
        public DateTime OccurredAt { get; set; }
    }

    private sealed class AnalyticsRegionHit
    {
        public int EventId { get; set; }
        public int RegionId { get; set; }
        public int? UserId { get; set; }
        public string EventType { get; set; } = string.Empty;
        public string? EntityType { get; set; }
        public int? EntityId { get; set; }
        public DateTime OccurredAt { get; set; }
    }

    private sealed record RegionContentCounts(int Destinations, int Locations, int Places, int Stays, int Events)
    {
        public static RegionContentCounts Empty { get; } = new(0, 0, 0, 0, 0);
    }

    private sealed record RegionPeriodRange(DateTime Start, DateTime End);

    private sealed record EntityKey(string EntityType, int EntityId);

    private sealed record RegionQualityAggregate(
        int TotalItems,
        int CompletedSignals,
        int PossibleSignals,
        int RecentlyUpdatedItems)
    {
        public static RegionQualityAggregate Empty { get; } = new(0, 0, 0, 0);
        public double QualityRatio => PossibleSignals <= 0 ? 0 : (double)CompletedSignals / PossibleSignals;
    }

    private sealed class QualitySignalRow
    {
        public int RegionId { get; set; }
        public int CompletedSignals { get; set; }
        public int PossibleSignals { get; set; }
        public bool RecentlyUpdated { get; set; }
    }

    private sealed class RegionReviewAggregate
    {
        private double RatingSum { get; set; }
        public int Count { get; private set; }
        public double AverageRating => Count == 0 ? 0 : Math.Round(RatingSum / Count, 1, MidpointRounding.AwayFromZero);

        public void Add(int count, double ratingSum)
        {
            Count += count;
            RatingSum += ratingSum;
        }
    }

    private sealed class RegionScopeRow
    {
        public int RegionId { get; set; }
        public string RegionName { get; set; } = string.Empty;
        public int TerritoryId { get; set; }
        public string TerritoryName { get; set; } = string.Empty;
        public string? ImageUrl { get; set; }
        public double? CenterLatitude { get; set; }
        public double? CenterLongitude { get; set; }
    }

    private sealed class DestinationHealthSource
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public bool HasHeroImage { get; set; }
        public bool HasDescription { get; set; }
        public bool HasTags { get; set; }
        public bool HasCoordinates { get; set; }
        public bool HasBoundary { get; set; }
        public int LocationsCount { get; set; }
        public int PlacesCount { get; set; }
        public int StaysCount { get; set; }
        public int EventsCount { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
    }
}
