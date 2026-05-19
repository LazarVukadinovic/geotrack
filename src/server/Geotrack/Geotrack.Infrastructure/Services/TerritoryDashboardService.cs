using Geotrack.Application.Analytics;
using Geotrack.Application.DTOs.Admin;
using Geotrack.Application.Interfaces.Service;
using Geotrack.Application.Security;
using Geotrack.Domain.Entities.Core;
using Geotrack.Domain.Entities.Users;
using Microsoft.EntityFrameworkCore;

namespace Geotrack.Infrastructure.Services;

public sealed class TerritoryDashboardService : ITerritoryDashboardService
{
    private const int DefaultTopTake = 3;
    private const int DirectoryMaxTake = 100;

    private static readonly string[] VisitEventTypes =
    {
        AnalyticsEventTypes.PageView,
        AnalyticsEventTypes.DetailView
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
            query = query.Where(region => region.Name.ToLower().Contains(normalizedSearch));
        }

        var rows = await query
            .Select(region => new RegionBaseRow
            {
                Id = region.Id,
                Name = region.Name,
                TerritoryName = region.Territory.Name,
                CreatedAt = region.CreatedAt
            })
            .ToListAsync();

        var regionIds = rows.Select(row => row.Id).ToList();
        var contentCounts = await GetRegionContentCountsAsync(regionIds);
        var pendingCounts = await GetRegionPendingRequestCountsAsync(regionIds);
        var managers = await GetRegionManagersAsync(regionIds);

        var items = rows.Select(row =>
        {
            var counts = contentCounts.GetValueOrDefault(row.Id, RegionContentCounts.Empty);
            var managerRows = managers.GetValueOrDefault(row.Id, new List<ManagerRow>());
            return new TerritoryRegionDirectoryItemDto
            {
                Id = row.Id,
                Name = row.Name,
                TerritoryName = row.TerritoryName,
                CreatedAt = row.CreatedAt,
                DestinationsCount = counts.Destinations,
                LocationsCount = counts.Locations,
                PlacesCount = counts.Places,
                EventsCount = counts.Events,
                PendingRequestsCount = pendingCounts.GetValueOrDefault(row.Id),
                ActiveManagersCount = managerRows.Count,
                ManagerDisplayName = BuildManagerDisplayName(managerRows)
            };
        });

        items = NormalizeSort(sort) switch
        {
            "newest" => items.OrderByDescending(item => item.CreatedAt).ThenBy(item => item.Name),
            "oldest" => items.OrderBy(item => item.CreatedAt).ThenBy(item => item.Name),
            "content" => items
                .OrderByDescending(item => item.DestinationsCount + item.LocationsCount + item.PlacesCount + item.EventsCount)
                .ThenBy(item => item.Name),
            "name-desc" => items.OrderByDescending(item => item.Name).ThenByDescending(item => item.Id),
            _ => items.OrderBy(item => item.Name).ThenBy(item => item.Id)
        };

        var ordered = items.ToList();

        return new TerritoryRegionDirectoryResponseDto
        {
            Items = ordered.Skip(skip).Take(take).ToList(),
            TotalCount = ordered.Count,
            Skip = skip,
            Take = take,
            HasMore = skip + take < ordered.Count
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
            .Select(counts => counts.Destinations + counts.Locations + counts.Places + counts.Events)
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
                    AssignedManagersPreview = managerRows
                        .Select(manager => BuildInitials(manager.DisplayName))
                        .Where(initials => !string.IsNullOrWhiteSpace(initials))
                        .Take(4)
                        .Cast<string>()
                        .ToList(),
                    DestinationsCount = counts.Destinations,
                    LocationsCount = counts.Locations,
                    PlacesCount = counts.Places,
                    EventsCount = counts.Events,
                    PendingRequestsCount = pendingRequests,
                    ActivityScore = CalculateActivityScore(
                        visits,
                        maxVisits,
                        counts.Destinations + counts.Locations + counts.Places + counts.Events,
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
                DestinationId = evt.DestinationId
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
                UserId = evt.UserId
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
                UserId = evt.UserId
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
                UserId = evt.UserId
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
                UserId = evt.UserId
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
                UserId = evt.UserId
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
                UserId = evt.UserId
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
            .Where(place => regionIds.Contains(place.Location.Destination.RegionId))
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
                eventCounts.GetValueOrDefault(regionId)));
    }

    private async Task<Dictionary<int, int>> GetRegionPendingRequestCountsAsync(List<int> regionIds)
    {
        return await _db.PlaceContributions
            .AsNoTracking()
            .Where(contribution =>
                contribution.Status == PlaceContributionStatus.PendingReview &&
                contribution.LocationId.HasValue &&
                contribution.Location != null &&
                regionIds.Contains(contribution.Location.Destination.RegionId))
            .GroupBy(contribution => contribution.Location!.Destination.RegionId)
            .Select(group => new { RegionId = group.Key, Count = group.Count() })
            .ToDictionaryAsync(row => row.RegionId, row => row.Count);
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
                DisplayName = assignment.User.DisplayName,
                FirstName = assignment.User.FirstName,
                LastName = assignment.User.LastName,
                Username = assignment.User.Username
            })
            .ToListAsync();

        return rows
            .Select(row => new ManagerRow
            {
                RegionId = row.RegionId,
                DisplayName = BuildDisplayName(row.DisplayName, row.FirstName, row.LastName, row.Username)
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

    private static string? BuildInitials(string displayName)
    {
        var parts = displayName.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
            return null;

        return string.Concat(parts.Take(2).Select(part => char.ToUpperInvariant(part[0])));
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

    private static string NormalizeSort(string? sort) =>
        string.IsNullOrWhiteSpace(sort) ? string.Empty : sort.Trim().ToLowerInvariant();

    private sealed class RegionBaseRow
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string TerritoryName { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
    }

    private sealed class ManagerRow
    {
        public int RegionId { get; set; }
        public string DisplayName { get; set; } = string.Empty;
    }

    private sealed class RawManagerRow
    {
        public int RegionId { get; set; }
        public string? DisplayName { get; set; }
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        public string? Username { get; set; }
    }

    private sealed class AnalyticsEventBaseRow
    {
        public int EventId { get; set; }
        public int? UserId { get; set; }
        public string EventType { get; set; } = string.Empty;
        public string? EntityType { get; set; }
        public int? EntityId { get; set; }
        public int? DestinationId { get; set; }
    }

    private sealed class AnalyticsRegionHit
    {
        public int EventId { get; set; }
        public int RegionId { get; set; }
        public int? UserId { get; set; }
    }

    private sealed record RegionContentCounts(int Destinations, int Locations, int Places, int Events)
    {
        public static RegionContentCounts Empty { get; } = new(0, 0, 0, 0);
    }
}
