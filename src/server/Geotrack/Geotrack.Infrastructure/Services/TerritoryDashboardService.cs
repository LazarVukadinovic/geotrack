using Geotrack.Application.Analytics;
using Geotrack.Application.DTOs.Admin;
using Geotrack.Application.Interfaces.Service;
using Geotrack.Application.Security;
using Geotrack.Domain.Entities.Core;
using Geotrack.Domain.Entities.Users;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;
using NetTopologySuite.Operation.Union;
using Coordinate = NetTopologySuite.Geometries.Coordinate;

namespace Geotrack.Infrastructure.Services;

public sealed class TerritoryDashboardService : ITerritoryDashboardService
{
    private const int DefaultTopTake = 3;
    private const int DashboardRegionsHealthTake = 4;
    private const int DetailPreviewTake = 5;
    private const int DirectoryMaxTake = 100;
    private const string PublishedStatus = "Published";
    private const string BoundaryGeometryPropertyName = "BoundaryGeometry";
    private const string BoundaryDisplayGeometryPropertyName = "BoundaryDisplayGeometry";
    private const string TerritoryBoundaryGeometrySource = "Territory.BoundaryGeometry";
    private const string RegionBoundaryGeometrySource = "Region.BoundaryGeometry";
    private const string ManualDrawBoundarySource = "ManualDraw";
    private const string OsmBoundarySource = "OsmBoundary";
    private const string GeoBoundariesBoundarySource = "geoBoundaries";
    private const string GeoBoundariesReleaseType = "gbOpen";
    private const string GeoBoundariesLicense = "CC BY 4.0";
    private const string Adm1Level = "ADM1";
    private const string DuplicateAdministrativeRegionMessage =
        "This administrative region has already been created.";
    private const string MissingTerritoryBoundaryMessage =
        "Assigned country/territory must have a boundary before adding regions.";

    private static readonly GeometryFactory Wgs84GeometryFactory = new(new PrecisionModel(), 4326);

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
    private readonly IHealthScoreService _healthScores;
    private readonly LocalBoundaryImportService _localBoundaryImport;
    private readonly IAdminNotificationService _adminNotificationService;

    public TerritoryDashboardService(
        AppDbContext db,
        IHealthScoreService healthScores,
        LocalBoundaryImportService localBoundaryImport,
        IAdminNotificationService adminNotificationService)
    {
        _db = db;
        _healthScores = healthScores;
        _localBoundaryImport = localBoundaryImport;
        _adminNotificationService = adminNotificationService;
    }

    public async Task<List<TerritoryAssignedTerritoryDto>> GetAssignedTerritoriesAsync(int userId)
    {
        await EnsureTerritoryManagerRoleAsync(userId);

        return await _db.TerritoryManagerTerritories
            .AsNoTracking()
            .Where(assignment => assignment.UserId == userId)
            .Where(assignment => assignment.Territory.TerritoryKind == TerritoryKinds.Country)
            .OrderBy(assignment => assignment.Territory.Name)
            .ThenBy(assignment => assignment.TerritoryId)
            .Select(assignment => new TerritoryAssignedTerritoryDto
            {
                Id = assignment.TerritoryId,
                Name = assignment.Territory.Name,
                DisplayName = assignment.Territory.Name,
                CountryCode = assignment.Territory.CountryCode,
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

        var healthPeriod = _healthScores.ResolvePeriod("1m");
        var now = healthPeriod.End;
        var currentPeriodStart = healthPeriod.Start;
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
            RegionsHealth = await BuildRegionsHealthAsync(activeTerritory.Id, healthPeriod),
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
                UpdatedAt = region.UpdatedAt
            })
            .ToListAsync();

        var regionIds = rows.Select(row => row.Id).ToList();
        var contentCounts = await GetRegionContentCountsAsync(regionIds);
        var pendingCounts = await GetRegionPendingRequestCountsAsync(regionIds);
        var managers = await GetRegionManagersAsync(regionIds);
        var healthPeriod = _healthScores.ResolvePeriod("1m");
        var healthScores = await _healthScores.GetRegionHealthScoresAsync(regionIds, healthPeriod);

        var items = rows.Select(row =>
        {
            var counts = contentCounts.GetValueOrDefault(row.Id, RegionContentCounts.Empty);
            var managerRows = managers.GetValueOrDefault(row.Id, new List<ManagerRow>());
            var pendingRequests = pendingCounts.GetValueOrDefault(row.Id);
            var health = healthScores.GetValueOrDefault(row.Id);
            var healthScore = health?.Score ?? 0;

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
                HealthScore = health?.Score,
                Health = health,
                ActivityScore = healthScore,
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
        var pageItems = ordered.Skip(skip).Take(take).ToList();
        var representativeImages = await GetRepresentativeImagesForRegionsAsync(pageItems);

        foreach (var item in pageItems)
        {
            var representativeImage = representativeImages.GetValueOrDefault(item.RegionId);
            item.RepresentativeImage = representativeImage;
            item.ImageUrl = representativeImage?.Url;
        }

        return new TerritoryRegionDirectoryResponseDto
        {
            Territory = activeTerritory,
            Items = pageItems,
            TotalCount = ordered.Count,
            Skip = skip,
            Take = take,
            HasMore = skip + take < ordered.Count
        };
    }

    public async Task<TerritoryRegionMapDto> GetRegionMapAsync(
        int userId,
        int? territoryId,
        CancellationToken cancellationToken = default)
    {
        await EnsureTerritoryManagerRoleAsync(userId);

        var activeTerritory = await GetAuthorizedTerritoryBoundaryAsync(userId, territoryId, cancellationToken);
        if (activeTerritory is null)
        {
            return new TerritoryRegionMapDto
            {
                Viewport = new TerritoryRegionMapViewportDto { IsEmpty = true },
                EmptyStateMessage = "No territory is assigned to this account.",
                BoundaryWarningMessage = "No territory is assigned to this account."
            };
        }

        var regionRows = await _db.Regions
            .AsNoTracking()
            .Where(region => region.TerritoryId == activeTerritory.Id)
            .OrderBy(region => region.Name)
            .ThenBy(region => region.Id)
            .Select(region => new TerritoryRegionMapRow
            {
                Id = region.Id,
                Name = region.Name,
                TerritoryId = region.TerritoryId,
                TerritoryName = region.Territory.Name,
                BoundaryGeometry = EF.Property<Geometry?>(region, BoundaryGeometryPropertyName),
                BoundaryDisplayGeometry = EF.Property<Geometry?>(region, BoundaryDisplayGeometryPropertyName)
            })
            .ToListAsync(cancellationToken);

        var regionIds = regionRows.Select(region => region.Id).ToList();
        var contentCounts = await GetRegionContentCountsAsync(regionIds);
        var pendingCounts = await GetRegionPendingRequestCountsAsync(regionIds);
        var managers = await GetRegionManagersAsync(regionIds);
        var healthPeriod = _healthScores.ResolvePeriod("1m");
        var healthScores = await _healthScores.GetRegionHealthScoresAsync(regionIds, healthPeriod);

        var territoryDto = BuildMapTerritory(activeTerritory);
        var regionDtos = regionRows
            .Select(row => BuildMapRegion(
                row,
                contentCounts.GetValueOrDefault(row.Id, RegionContentCounts.Empty),
                pendingCounts.GetValueOrDefault(row.Id),
                managers.GetValueOrDefault(row.Id, new List<ManagerRow>()),
                healthScores.GetValueOrDefault(row.Id)))
            .ToList();

        var viewportGeometries = new List<Geometry>();
        var territoryGeometry = ToPolygonalGeometry(activeTerritory.BoundaryGeometry);
        if (territoryGeometry is not null)
        {
            viewportGeometries.Add(territoryGeometry);
        }

        viewportGeometries.AddRange(regionRows
            .Select(row => ToPolygonalGeometry(row.BoundaryGeometry))
            .Where(geometry => geometry is not null)
            .Select(geometry => geometry!));

        return new TerritoryRegionMapDto
        {
            Territory = territoryDto,
            Regions = regionDtos,
            Viewport = BuildRegionMapViewport(viewportGeometries),
            EmptyStateMessage = regionDtos.Count == 0
                ? "No regions are available for this territory. Sync ADM1 regions first."
                : null,
            BoundaryWarningMessage = BuildRegionMapBoundaryWarning(territoryDto, regionDtos)
        };
    }

    public async Task<TerritoryRegionCreateContextDto> GetRegionCreateContextAsync(int userId, int? territoryId)
    {
        await EnsureTerritoryManagerOnlyRoleAsync(userId);

        var activeTerritory = await GetAuthorizedTerritoryBoundaryAsync(userId, territoryId);
        if (activeTerritory is null)
        {
            return new TerritoryRegionCreateContextDto
            {
                CanCreate = false,
                BoundaryRequiredMessage = "No assigned country/territory is available for this account.",
                Viewport = new TerritoryRegionMapViewportDto { IsEmpty = true }
            };
        }

        var territoryGeometry = ToPolygonalGeometry(activeTerritory.BoundaryGeometry);
        var regionRows = await _db.Regions
            .AsNoTracking()
            .Where(region => region.TerritoryId == activeTerritory.Id)
            .OrderBy(region => region.Name)
            .ThenBy(region => region.Id)
            .Select(region => new RegionBoundaryRow
            {
                Id = region.Id,
                Name = region.Name,
                BoundaryGeometry = EF.Property<Geometry?>(region, BoundaryGeometryPropertyName)
            })
            .ToListAsync();

        var existingRegions = regionRows
            .Select(BuildRegionMapItem)
            .ToList();

        var viewportGeometries = new List<Geometry>();
        if (territoryGeometry is not null)
        {
            viewportGeometries.Add(territoryGeometry);
        }

        viewportGeometries.AddRange(regionRows
            .Select(row => ToPolygonalGeometry(row.BoundaryGeometry))
            .Where(geometry => geometry is not null)
            .Select(geometry => geometry!));

        return new TerritoryRegionCreateContextDto
        {
            Territory = new TerritoryAssignedTerritoryDto
            {
                Id = activeTerritory.Id,
                Name = activeTerritory.Name,
                DisplayName = activeTerritory.Name,
                CountryCode = activeTerritory.CountryCode,
                StatusLabel = "Assigned"
            },
            TerritoryBoundaryGeoJson = ToGeoJson(territoryGeometry),
            ExistingRegions = existingRegions,
            Viewport = BuildRegionMapViewport(viewportGeometries),
            CanCreate = territoryGeometry is not null,
            BoundaryRequiredMessage = territoryGeometry is null
                ? MissingTerritoryBoundaryMessage
                : null
        };
    }

    public async Task<BoundaryRegionCandidateResponseDto> GetRegionBoundaryCandidatesAsync(
        int userId,
        int? territoryId,
        string? admLevel,
        string? search,
        int skip,
        int? take,
        string? geometryMode = null)
    {
        await EnsureTerritoryManagerOnlyRoleAsync(userId);

        var activeTerritory = await GetAuthorizedTerritoryBoundaryAsync(userId, territoryId)
            ?? throw new UnauthorizedAccessException("Territory is not assigned to the current territory manager.");
        var territoryGeometry = ToPolygonalGeometry(activeTerritory.BoundaryGeometry);
        if (territoryGeometry is null)
            throw new InvalidOperationException(MissingTerritoryBoundaryMessage);

        var iso3 = CountryRegistry.NormalizeIso3ForBoundaryImport(activeTerritory.CountryIso3)
            ?? CountryRegistry.NormalizeIso3ForBoundaryImport(activeTerritory.CountryCode)
            ?? CountryRegistry.ToIso3(activeTerritory.CountryCode)
            ?? throw new InvalidOperationException("Assigned country/territory must have an ISO3 country code before loading boundary candidates.");
        var requestedAdmLevel = NormalizeAdm1CandidateLevel(admLevel);

        var response = await _localBoundaryImport.GetRegionCandidatesAsync(
            requestedAdmLevel,
            iso3,
            territoryGeometry,
            search,
            skip,
            take,
            geometryMode);

        var existingRows = await _db.Regions
            .AsNoTracking()
            .Where(region => region.TerritoryId == activeTerritory.Id)
            .Select(region => new RegionBoundaryMetadataRow
            {
                Id = region.Id,
                Name = region.Name,
                BoundaryCandidateId = region.BoundaryCandidateId,
                BoundaryExternalKey = region.BoundaryExternalKey,
                BoundaryShapeGroup = region.BoundaryShapeGroup,
                BoundaryShapeId = region.BoundaryShapeId,
                BoundaryAdmLevel = region.BoundaryAdmLevel
            })
            .ToListAsync();

        MarkExistingRegionCandidates(response.Items, existingRows);
        return response;
    }

    public async Task<TerritoryRegionCreateResponseDto> CreateRegionAsync(
        int userId,
        TerritoryRegionCreateRequestDto request)
    {
        await EnsureTerritoryManagerOnlyRoleAsync(userId);

        if (request.TerritoryId <= 0 && !HasBoundaryCandidateRequest(request))
            throw new InvalidOperationException("territoryId is required.");

        var requestedTerritoryId = request.TerritoryId > 0 ? request.TerritoryId : (int?)null;
        var activeTerritory = await GetAuthorizedTerritoryBoundaryAsync(userId, requestedTerritoryId)
            ?? throw new UnauthorizedAccessException("Territory is not assigned to the current territory manager.");

        var territoryGeometry = ToPolygonalGeometry(activeTerritory.BoundaryGeometry);
        if (territoryGeometry is null)
            throw new InvalidOperationException(MissingTerritoryBoundaryMessage);

        if (HasBoundaryCandidateRequest(request))
        {
            return await CreateRegionFromBoundaryCandidateAsync(
                userId,
                activeTerritory,
                territoryGeometry,
                request);
        }

        var name = NormalizeRequiredRegionName(request.Name);
        var normalizedName = name.ToLowerInvariant();
        var duplicateExists = await _db.Regions
            .AsNoTracking()
            .AnyAsync(region =>
                region.TerritoryId == activeTerritory.Id &&
                region.Name.ToLower() == normalizedName);

        if (duplicateExists)
            throw new InvalidOperationException($"Region with name '{name}' already exists in this territory.");

        var boundary = BuildRegionBoundary(request.Boundary);
        if (!territoryGeometry.Covers(boundary.Geometry))
            throw new InvalidOperationException("Region boundary must be inside the assigned country/territory.");

        var now = DateTime.UtcNow;
        var source = NormalizeBoundarySource(request.BoundarySource);
        var region = new Region
        {
            Name = name,
            TerritoryId = activeTerritory.Id,
            CreatedAt = now,
            CreatedByUserId = userId,
            BoundaryShapeType = boundary.ShapeType,
            BoundarySource = source,
            OsmRelationId = request.OsmRelationId,
            OsmAdminLevel = request.OsmAdminLevel
        };

        _db.Regions.Add(region);
        _db.Entry(region)
            .Property<Geometry?>(BoundaryGeometryPropertyName)
            .CurrentValue = boundary.Geometry;

        await _db.SaveChangesAsync();
        await _adminNotificationService.CreateTerritoryRegionCreatedNotificationAsync(
            activeTerritory.Id,
            region.Id,
            region.Name,
            region.CreatedAt);

        return new TerritoryRegionCreateResponseDto
        {
            Id = region.Id,
            RegionId = region.Id,
            Name = region.Name,
            DisplayName = region.Name,
            TerritoryId = region.TerritoryId,
            TerritoryDisplayName = activeTerritory.Name,
            Status = "Inactive",
            BoundarySource = source,
            BoundaryShapeType = region.BoundaryShapeType,
            BoundaryGeometryGeoJson = ToGeoJson(boundary.Geometry),
            CreatedAt = region.CreatedAt,
            DetailRoute = $"/territory/regions/{region.Id}"
        };
    }

    private async Task<TerritoryRegionCreateResponseDto> CreateRegionFromBoundaryCandidateAsync(
        int userId,
        TerritoryBoundaryRow activeTerritory,
        Geometry territoryGeometry,
        TerritoryRegionCreateRequestDto request)
    {
        var admLevel = NormalizeAdm1CandidateLevel(request.AdmLevel);
        var iso3 = CountryRegistry.NormalizeIso3ForBoundaryImport(activeTerritory.CountryIso3)
            ?? CountryRegistry.NormalizeIso3ForBoundaryImport(activeTerritory.CountryCode)
            ?? CountryRegistry.ToIso3(activeTerritory.CountryCode)
            ?? throw new InvalidOperationException("Assigned country/territory must have an ISO3 country code before creating regions from boundary candidates.");
        var shapeId = NormalizeOptional(request.ShapeId);
        var requestedBoundaryCandidateId = NormalizeOptional(request.BoundaryCandidateId);
        var requestedCandidateRecordId = TryParseBoundaryCandidateId(requestedBoundaryCandidateId);
        var requestedExternalKey = FirstNonEmptyNullable(
            request.CandidateExternalKey,
            request.BoundaryExternalKey,
            requestedCandidateRecordId.HasValue ? null : requestedBoundaryCandidateId);
        if (!requestedCandidateRecordId.HasValue &&
            string.IsNullOrWhiteSpace(shapeId) &&
            string.IsNullOrWhiteSpace(requestedExternalKey))
        {
            throw new InvalidOperationException("Boundary candidate id is required.");
        }

        var requestedShapeGroup = NormalizeOptional(request.ShapeGroup);
        if (!string.IsNullOrWhiteSpace(requestedShapeGroup) &&
            !BoundaryIsoCodesMatch(requestedShapeGroup, iso3))
        {
            throw new InvalidOperationException("Boundary candidate is not available for the assigned country/territory.");
        }

        var candidateResult = await _localBoundaryImport.FindRegionCandidateGeometryAsync(
            admLevel,
            iso3,
            requestedCandidateRecordId,
            shapeId,
            requestedExternalKey,
            territoryGeometry);
        if (candidateResult is null)
            throw new InvalidOperationException("Boundary candidate is not available for the assigned country/territory.");

        var candidate = candidateResult.Candidate;
        if (requestedCandidateRecordId.HasValue &&
            requestedCandidateRecordId.Value != candidate.BoundaryCandidateId)
        {
            throw new InvalidOperationException("Boundary candidate is not available for the assigned country/territory.");
        }

        if (!string.IsNullOrWhiteSpace(requestedExternalKey) &&
            !string.Equals(requestedExternalKey, candidate.BoundaryExternalKey, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(requestedExternalKey, candidate.ExternalKey, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Boundary candidate is not available for the assigned country/territory.");
        }

        var boundaryGeometry = ToPolygonalGeometry(candidateResult.Geometry)
            ?? throw new InvalidOperationException("Boundary candidate geometry is invalid.");
        if (!candidate.IsInsideAssignedTerritory)
            throw new InvalidOperationException("Region boundary must be inside the assigned country/territory.");

        var duplicateCandidateExists = await _db.Regions
            .AsNoTracking()
            .AnyAsync(region =>
                region.TerritoryId == activeTerritory.Id &&
                ((region.BoundaryCandidateId != null &&
                  region.BoundaryCandidateId == candidate.BoundaryCandidateId) ||
                 (region.BoundaryExternalKey != null &&
                  region.BoundaryExternalKey.ToLower() == candidate.BoundaryExternalKey.ToLower()) ||
                 (region.BoundaryAdmLevel == Adm1Level &&
                  region.BoundaryShapeGroup != null &&
                  region.BoundaryShapeId != null &&
                  candidate.ShapeGroup != null &&
                  candidate.ShapeId != null &&
                  region.BoundaryShapeGroup.ToLower() == candidate.ShapeGroup.ToLower() &&
                  region.BoundaryShapeId.ToLower() == candidate.ShapeId.ToLower()) ||
                 (region.BoundaryCandidateId == null &&
                  region.BoundaryExternalKey == null &&
                  region.BoundaryShapeId == null &&
                  region.Name.ToLower() == candidate.DisplayName.ToLower())));

        if (duplicateCandidateExists)
            throw new InvalidOperationException(DuplicateAdministrativeRegionMessage);

        var name = NormalizeCandidateRegionName(request.Name, candidate.DisplayName);
        var normalizedName = name.ToLowerInvariant();
        var duplicateNameExists = await _db.Regions
            .AsNoTracking()
            .AnyAsync(region =>
                region.TerritoryId == activeTerritory.Id &&
                region.Name.ToLower() == normalizedName);

        if (duplicateNameExists)
            throw new InvalidOperationException($"Region with name '{name}' already exists in this territory.");

        var now = DateTime.UtcNow;
        var region = new Region
        {
            Name = name,
            TerritoryId = activeTerritory.Id,
            CreatedAt = now,
            CreatedByUserId = userId,
            BoundaryShapeType = boundaryGeometry.GeometryType,
            BoundarySource = GeoBoundariesBoundarySource,
            BoundaryAdmLevel = Adm1Level,
            BoundaryShapeGroup = candidate.ShapeGroup,
            BoundaryShapeId = candidate.ShapeId,
            BoundaryExternalKey = candidate.BoundaryExternalKey,
            BoundaryReleaseType = GeoBoundariesReleaseType,
            BoundaryLicense = GeoBoundariesLicense,
            BoundaryImportedAt = now,
            BoundaryCandidateId = candidate.BoundaryCandidateId
        };

        _db.Regions.Add(region);
        _db.Entry(region)
            .Property<Geometry?>(BoundaryGeometryPropertyName)
            .CurrentValue = boundaryGeometry;

        await _db.SaveChangesAsync();
        await _adminNotificationService.CreateTerritoryRegionCreatedNotificationAsync(
            activeTerritory.Id,
            region.Id,
            region.Name,
            region.CreatedAt);

        return new TerritoryRegionCreateResponseDto
        {
            Id = region.Id,
            RegionId = region.Id,
            Name = region.Name,
            DisplayName = region.Name,
            TerritoryId = region.TerritoryId,
            TerritoryDisplayName = activeTerritory.Name,
            Status = "Inactive",
            BoundarySource = region.BoundarySource,
            BoundaryShapeType = region.BoundaryShapeType,
            BoundaryShapeGroup = region.BoundaryShapeGroup,
            BoundaryShapeId = region.BoundaryShapeId,
            BoundaryExternalKey = region.BoundaryExternalKey,
            BoundaryAdmLevel = region.BoundaryAdmLevel,
            BoundaryReleaseType = region.BoundaryReleaseType,
            BoundaryCandidateId = region.BoundaryCandidateId,
            BoundaryGeometryGeoJson = ToGeoJson(boundaryGeometry),
            CreatedAt = region.CreatedAt,
            DetailRoute = $"/territory/regions/{region.Id}"
        };
    }

    public async Task<TerritoryRegionDetailDto> GetRegionDetailAsync(
        int userId,
        int regionId,
        int? territoryId,
        string? period)
    {
        var scope = await GetAuthorizedRegionAsync(userId, regionId, territoryId);
        var healthPeriod = _healthScores.ResolvePeriod(period);
        var periodRange = new RegionPeriodRange(healthPeriod.Start, healthPeriod.End, healthPeriod.Key, healthPeriod.Label);
        var regionIds = new List<int> { scope.RegionId };

        var contentCounts = await GetRegionContentCountsAsync(regionIds);
        var counts = contentCounts.GetValueOrDefault(scope.RegionId, RegionContentCounts.Empty);
        var pendingCounts = await GetRegionPendingRequestCountsAsync(regionIds);
        var pendingRequests = pendingCounts.GetValueOrDefault(scope.RegionId);
        var managers = await GetDetailedRegionManagersAsync(scope.RegionId);
        var hits = await GetRegionAnalyticsHitsAsync(scope.RegionId, periodRange.Start, periodRange.End, visitOnly: false);
        var reviewAggregate = await GetRegionReviewAggregateAsync(scope.RegionId);
        var regionHealth = await _healthScores.GetRegionHealthScoresAsync(regionIds, healthPeriod);
        var health = regionHealth.GetValueOrDefault(scope.RegionId) ?? new TerritoryHealthDto
        {
            Score = 0,
            Status = "critical",
            StatusLabel = "Critical",
            PeriodLabel = healthPeriod.Label,
            CalculatedAt = healthPeriod.End
        };

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
        var healthPeriod = _healthScores.ResolvePeriod(null);
        var periodRange = new RegionPeriodRange(healthPeriod.Start, healthPeriod.End, healthPeriod.Key, healthPeriod.Label);
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
        HealthScorePeriod healthPeriod)
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
        var healthScores = await _healthScores.GetRegionHealthScoresAsync(regionIds, healthPeriod);

        return regions.Select(region =>
            {
                var counts = contentCounts.GetValueOrDefault(region.Id, RegionContentCounts.Empty);
                var managerRows = managers.GetValueOrDefault(region.Id, new List<ManagerRow>());
                var pendingRequests = pendingCounts.GetValueOrDefault(region.Id);
                var health = healthScores.GetValueOrDefault(region.Id);
                var healthScore = health?.Score ?? 0;

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
                    HealthScore = healthScore,
                    Health = health ?? new TerritoryHealthDto
                    {
                        Score = healthScore,
                        Status = "critical",
                        StatusLabel = "Critical",
                        PeriodLabel = healthPeriod.Label,
                        CalculatedAt = healthPeriod.End
                    },
                    ActivityScore = healthScore
                };
            })
            .OrderByDescending(region => region.HealthScore)
            .ThenByDescending(region => region.AssignedManagers.Count > 0)
            .ThenByDescending(TotalContentCount)
            .ThenBy(region => region.PendingRequestsCount)
            .ThenBy(region => region.RegionName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(region => region.RegionName, StringComparer.Ordinal)
            .ThenBy(region => region.RegionId)
            .Take(DashboardRegionsHealthTake)
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

    private async Task<Dictionary<int, TerritoryRegionRepresentativeImageDto>> GetRepresentativeImagesForRegionsAsync(
        IReadOnlyList<TerritoryRegionDirectoryItemDto> regionItems)
    {
        var regionIds = regionItems
            .Select(item => item.RegionId)
            .Where(regionId => regionId > 0)
            .Distinct()
            .ToList();

        if (regionIds.Count == 0)
            return new Dictionary<int, TerritoryRegionRepresentativeImageDto>();

        // Maps each region on the current page to its owning territory so a
        // territory-level profile image can act as a fallback for its regions.
        var regionTerritoryMap = regionItems
            .Where(item => item.RegionId > 0 && item.TerritoryId > 0)
            .GroupBy(item => item.RegionId)
            .ToDictionary(group => group.Key, group => group.First().TerritoryId);
        var territoryIds = regionTerritoryMap.Values.Distinct().ToList();

        var candidates = new List<RegionRepresentativeImageCandidate>();

        candidates.AddRange(await _db.EntityImages
            .AsNoTracking()
            .Where(image =>
                image.TargetType == EntityImageTargetType.Destination &&
                image.DestinationId.HasValue &&
                image.Url != string.Empty &&
                regionIds.Contains(image.Destination!.RegionId))
            .Select(image => new RegionRepresentativeImageCandidate
            {
                RegionId = image.Destination!.RegionId,
                SourcePriority = 0,
                SourceType = "Destination",
                SourceId = image.DestinationId!.Value,
                SourceName = image.Destination.Name,
                SourceStatus = image.Destination.Status,
                Url = image.Url,
                AltText = image.AltText,
                IsMain = image.IsMain,
                ImageRole = image.ImageRole,
                SortOrder = image.SortOrder,
                CreatedAt = image.CreatedAt,
                ImageId = image.Id
            })
            .ToListAsync());

        candidates.AddRange(await _db.EntityImages
            .AsNoTracking()
            .Where(image =>
                image.TargetType == EntityImageTargetType.Location &&
                image.LocationId.HasValue &&
                image.Url != string.Empty &&
                regionIds.Contains(image.Location!.Destination.RegionId))
            .Select(image => new RegionRepresentativeImageCandidate
            {
                RegionId = image.Location!.Destination.RegionId,
                SourcePriority = 1,
                SourceType = "Location",
                SourceId = image.LocationId!.Value,
                SourceName = image.Location.Name,
                SourceStatus = image.Location.Status,
                Url = image.Url,
                AltText = image.AltText,
                IsMain = image.IsMain,
                ImageRole = image.ImageRole,
                SortOrder = image.SortOrder,
                CreatedAt = image.CreatedAt,
                ImageId = image.Id
            })
            .ToListAsync());

        candidates.AddRange(await _db.EntityImages
            .AsNoTracking()
            .Where(image =>
                image.TargetType == EntityImageTargetType.Place &&
                image.PlaceId.HasValue &&
                image.Url != string.Empty &&
                regionIds.Contains(image.Place!.Location.Destination.RegionId))
            .Select(image => new RegionRepresentativeImageCandidate
            {
                RegionId = image.Place!.Location.Destination.RegionId,
                SourcePriority = 2,
                SourceType = image.Place.Stay != null ? "Stay" : "Place",
                SourceId = image.PlaceId!.Value,
                SourceName = image.Place.Name,
                SourceStatus = image.Place.Status,
                Url = image.Url,
                AltText = image.AltText,
                IsMain = image.IsMain,
                ImageRole = image.ImageRole,
                SortOrder = image.SortOrder,
                CreatedAt = image.CreatedAt,
                ImageId = image.Id
            })
            .ToListAsync());

        candidates.AddRange(await _db.EntityImages
            .AsNoTracking()
            .Where(image =>
                image.TargetType == EntityImageTargetType.Event &&
                image.EventId.HasValue &&
                image.Url != string.Empty &&
                regionIds.Contains(image.Event!.Destination.RegionId))
            .Select(image => new RegionRepresentativeImageCandidate
            {
                RegionId = image.Event!.Destination.RegionId,
                SourcePriority = 3,
                SourceType = "Event",
                SourceId = image.EventId!.Value,
                SourceName = image.Event.Name,
                SourceStatus = image.Event.Status,
                Url = image.Url,
                AltText = image.AltText,
                IsMain = image.IsMain,
                ImageRole = image.ImageRole,
                SortOrder = image.SortOrder,
                CreatedAt = image.CreatedAt,
                ImageId = image.Id
            })
            .ToListAsync());

        // Region-level profile image (set via "Edit Region Details"). This is an
        // explicitly chosen card image for the region, so it takes precedence over
        // images pulled from the region's child content.
        candidates.AddRange(await _db.EntityImages
            .AsNoTracking()
            .Where(image =>
                image.TargetType == EntityImageTargetType.Region &&
                image.RegionId.HasValue &&
                image.Url != string.Empty &&
                regionIds.Contains(image.RegionId.Value))
            .Select(image => new RegionRepresentativeImageCandidate
            {
                RegionId = image.RegionId!.Value,
                SourcePriority = -2,
                SourceType = "Region",
                SourceId = image.RegionId!.Value,
                SourceName = image.Region!.Name,
                SourceStatus = "Active",
                Url = image.Url,
                AltText = image.AltText,
                IsMain = image.IsMain,
                ImageRole = image.ImageRole,
                SortOrder = image.SortOrder,
                CreatedAt = image.CreatedAt,
                ImageId = image.Id
            })
            .ToListAsync());

        // Territory-level profile image (set via "Edit Territory Details"). Shared
        // across every region in the territory, so it is only used as a last-resort
        // fallback when a region has no image of its own or from its child content.
        if (territoryIds.Count > 0)
        {
            var territoryImages = await _db.EntityImages
                .AsNoTracking()
                .Where(image =>
                    image.TargetType == EntityImageTargetType.Territory &&
                    image.TerritoryId.HasValue &&
                    image.Url != string.Empty &&
                    territoryIds.Contains(image.TerritoryId.Value))
                .Select(image => new
                {
                    TerritoryId = image.TerritoryId!.Value,
                    TerritoryName = image.Territory!.Name,
                    image.Url,
                    image.AltText,
                    image.SortOrder,
                    image.CreatedAt,
                    ImageId = image.Id
                })
                .ToListAsync();

            var territoryImagesByTerritory = territoryImages
                .GroupBy(image => image.TerritoryId)
                .ToDictionary(group => group.Key, group => group.ToList());

            foreach (var entry in regionTerritoryMap)
            {
                if (!territoryImagesByTerritory.TryGetValue(entry.Value, out var images))
                    continue;

                foreach (var image in images)
                {
                    candidates.Add(new RegionRepresentativeImageCandidate
                    {
                        RegionId = entry.Key,
                        SourcePriority = 100,
                        SourceType = "Territory",
                        SourceId = entry.Value,
                        SourceName = image.TerritoryName,
                        SourceStatus = "Active",
                        Url = image.Url,
                        AltText = image.AltText,
                        // Force into the non-preferred group so any region-specific
                        // image always wins over the shared territory fallback.
                        IsMain = false,
                        ImageRole = EntityImageRole.Gallery,
                        SortOrder = image.SortOrder,
                        CreatedAt = image.CreatedAt,
                        ImageId = image.ImageId
                    });
                }
            }
        }

        return candidates
            .Where(IsDisplayableRepresentativeImageCandidate)
            .GroupBy(candidate => candidate.RegionId)
            .ToDictionary(
                group => group.Key,
                group =>
                {
                    var selected = group
                        .OrderBy(RepresentativeImagePreferenceGroup)
                        .ThenBy(candidate => candidate.SourcePriority)
                        .ThenByDescending(candidate => candidate.IsMain)
                        .ThenBy(candidate => RepresentativeImageRoleRank(candidate.ImageRole))
                        .ThenBy(candidate => candidate.SortOrder)
                        .ThenBy(candidate => candidate.CreatedAt)
                        .ThenBy(candidate => candidate.ImageId)
                        .ThenBy(candidate => candidate.SourceName)
                        .ThenBy(candidate => candidate.SourceId)
                        .First();

                    return new TerritoryRegionRepresentativeImageDto
                    {
                        Url = selected.Url.Trim(),
                        AltText = NullIfWhiteSpace(selected.AltText) ?? selected.SourceName,
                        SourceType = selected.SourceType,
                        SourceId = selected.SourceId,
                        SourceName = selected.SourceName
                    };
                });
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

        return regionIds.ToDictionary(
            regionId => regionId,
            regionId => contributionCounts.GetValueOrDefault(regionId));
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
                DetailRoute = "/territory/content?type=destinations",
                PublicDetailRoute = destination.Status == PublishedStatus
                    ? $"/explore?destinationId={destination.Id}"
                    : null
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
                DetailRoute = "/territory/content?type=locations",
                PublicDetailRoute = null
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
                DetailRoute = "/territory/content?type=places",
                PublicDetailRoute = place.Status == PublishedStatus
                    ? place.Stay != null
                        ? $"/stays/{place.Id}"
                        : $"/places/{place.Id}"
                    : null
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
                DetailRoute = "/territory/content?type=events",
                PublicDetailRoute = evt.Status == PublishedStatus
                    ? $"/events/{evt.Id}"
                    : null
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
            var healthPeriod = new HealthScorePeriod(
                periodRange.Key,
                periodRange.Label,
                periodRange.Start,
                periodRange.End);
            var health = await _healthScores.GetDestinationHealthScoresAsync(destinationIds, regionId, healthPeriod);
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
                ReviewsCount = 0,
                AverageRating = null,
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

    private async Task<TerritoryBoundaryRow?> GetAuthorizedTerritoryBoundaryAsync(
        int userId,
        int? territoryId,
        CancellationToken cancellationToken = default)
    {
        if (territoryId.HasValue && territoryId.Value <= 0)
            throw new InvalidOperationException("territoryId must be positive.");

        var query = _db.TerritoryManagerTerritories
            .AsNoTracking()
            .Where(assignment =>
                assignment.UserId == userId &&
                assignment.Territory.TerritoryKind == TerritoryKinds.Country);

        if (territoryId.HasValue)
        {
            query = query.Where(assignment => assignment.TerritoryId == territoryId.Value);
        }

        var territory = await query
            .OrderBy(assignment => assignment.Territory.Name)
            .ThenBy(assignment => assignment.TerritoryId)
            .Select(assignment => new TerritoryBoundaryRow
            {
                Id = assignment.TerritoryId,
                Name = assignment.Territory.Name,
                CountryCode = assignment.Territory.CountryCode,
                CountryIso3 = assignment.Territory.CountryIso3,
                BoundaryShapeType = assignment.Territory.BoundaryShapeType,
                BoundarySource = assignment.Territory.BoundarySource,
                BoundaryGeometry = EF.Property<Geometry?>(
                    assignment.Territory,
                    BoundaryGeometryPropertyName),
                BoundaryDisplayGeometry = EF.Property<Geometry?>(
                    assignment.Territory,
                    BoundaryDisplayGeometryPropertyName)
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (territoryId.HasValue && territory is null)
            throw new UnauthorizedAccessException("Territory is not assigned to the current territory manager.");

        return territory;
    }

    private static TerritoryRegionMapItemDto BuildRegionMapItem(RegionBoundaryRow row)
    {
        var geometry = ToPolygonalGeometry(row.BoundaryGeometry);
        var centroid = geometry?.Centroid;

        return new TerritoryRegionMapItemDto
        {
            Id = row.Id,
            RegionId = row.Id,
            Name = row.Name,
            DisplayName = row.Name,
            GeometryGeoJson = ToGeoJson(geometry),
            CenterLatitude = centroid?.Y,
            CenterLongitude = centroid?.X
        };
    }

    private static TerritoryRegionMapTerritoryDto BuildMapTerritory(TerritoryBoundaryRow row)
    {
        var geometry = ToPolygonalGeometry(row.BoundaryGeometry);
        var geoJson = ToGeoJson(geometry);

        return new TerritoryRegionMapTerritoryDto
        {
            TerritoryId = row.Id,
            Name = row.Name,
            DisplayName = row.Name,
            CountryCode = row.CountryCode,
            CountryIso3 = row.CountryIso3,
            HasBoundary = geoJson is not null,
            GeometrySource = geoJson is not null ? TerritoryBoundaryGeometrySource : null,
            BoundaryMissingReason = geoJson is null
                ? BuildBoundaryMissingReason("Full territory boundary", row.BoundaryDisplayGeometry)
                : null,
            GeometryGeoJson = geoJson,
            Viewport = BuildSingleGeometryViewport(geometry)
        };
    }

    private static TerritoryRegionMapRegionDto BuildMapRegion(
        TerritoryRegionMapRow row,
        RegionContentCounts counts,
        int pendingRequests,
        List<ManagerRow> managers,
        TerritoryHealthDto? health)
    {
        var geometry = ToPolygonalGeometry(row.BoundaryGeometry);
        var geoJson = ToGeoJson(geometry);
        var centroid = geometry?.Centroid;
        var healthScore = health?.Score;

        return new TerritoryRegionMapRegionDto
        {
            RegionId = row.Id,
            Name = row.Name,
            DisplayName = row.Name,
            TerritoryId = row.TerritoryId,
            TerritoryDisplayName = row.TerritoryName,
            Status = managers.Count > 0 ? "Active" : "Inactive",
            HealthScore = healthScore,
            Health = health,
            AssignedManagers = managers.Select(BuildManagerDto).ToList(),
            ManagerDisplayName = BuildManagerDisplayName(managers),
            ContentCounts = new TerritoryRegionMapContentCountsDto
            {
                Destinations = counts.Destinations,
                Locations = counts.Locations,
                Places = counts.Places,
                Stays = counts.Stays,
                Events = counts.Events,
                Pending = pendingRequests
            },
            HasBoundary = geoJson is not null,
            GeometrySource = geoJson is not null ? RegionBoundaryGeometrySource : null,
            BoundaryMissingReason = geoJson is null
                ? BuildBoundaryMissingReason("Full region boundary", row.BoundaryDisplayGeometry)
                : null,
            GeometryGeoJson = geoJson,
            Viewport = BuildSingleGeometryViewport(geometry),
            CenterLatitude = centroid?.Y,
            CenterLongitude = centroid?.X,
            DetailRoute = $"/territory/regions/{row.Id}"
        };
    }

    private static string? BuildRegionMapBoundaryWarning(
        TerritoryRegionMapTerritoryDto territory,
        List<TerritoryRegionMapRegionDto> regions)
    {
        var territoryMissing = !territory.HasBoundary;
        var regionsMissing = regions.Any(region => !region.HasBoundary);

        return territoryMissing || regionsMissing
            ? "Boundary data is missing for this territory or some regions."
            : null;
    }

    private static TerritoryRegionMapViewportDto BuildSingleGeometryViewport(Geometry? geometry)
    {
        var polygonal = ToPolygonalGeometry(geometry);
        return polygonal is null
            ? new TerritoryRegionMapViewportDto { IsEmpty = true }
            : BuildRegionMapViewport(new[] { polygonal });
    }

    private static string BuildBoundaryMissingReason(string boundaryLabel, Geometry? displayGeometry)
    {
        var hasDisplayGeometry = ToPolygonalGeometry(displayGeometry) is not null;
        return hasDisplayGeometry
            ? $"{boundaryLabel} is missing. Display geometry exists but is not used on this accuracy map."
            : $"{boundaryLabel} is missing.";
    }

    private static void MarkExistingRegionCandidates(
        List<BoundaryRegionCandidateDto> candidates,
        List<RegionBoundaryMetadataRow> existingRows)
    {
        var byExternalKey = existingRows
            .Where(row => !string.IsNullOrWhiteSpace(row.BoundaryExternalKey))
            .GroupBy(row => row.BoundaryExternalKey!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        var byCandidateId = existingRows
            .Where(row => row.BoundaryCandidateId.HasValue)
            .GroupBy(row => row.BoundaryCandidateId!.Value)
            .ToDictionary(group => group.Key, group => group.First());
        var byShapeKey = existingRows
            .Where(row =>
                string.Equals(row.BoundaryAdmLevel, Adm1Level, StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(row.BoundaryShapeGroup) &&
                !string.IsNullOrWhiteSpace(row.BoundaryShapeId))
            .GroupBy(row => BuildShapeCandidateKey(row.BoundaryShapeGroup, row.BoundaryShapeId), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        var byPendingExactName = existingRows
            .Where(row =>
                row.BoundaryCandidateId is null &&
                string.IsNullOrWhiteSpace(row.BoundaryExternalKey) &&
                string.IsNullOrWhiteSpace(row.BoundaryShapeId))
            .GroupBy(row => row.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        foreach (var candidate in candidates)
        {
            RegionBoundaryMetadataRow? existing = null;
            if (candidate.BoundaryCandidateId > 0)
            {
                byCandidateId.TryGetValue(candidate.BoundaryCandidateId, out existing);
            }

            if (existing is null &&
                !string.IsNullOrWhiteSpace(candidate.BoundaryExternalKey))
            {
                byExternalKey.TryGetValue(candidate.BoundaryExternalKey, out existing);
            }

            if (existing is null &&
                !string.IsNullOrWhiteSpace(candidate.ShapeGroup) &&
                !string.IsNullOrWhiteSpace(candidate.ShapeId))
            {
                byShapeKey.TryGetValue(
                    BuildShapeCandidateKey(candidate.ShapeGroup, candidate.ShapeId),
                    out existing);
            }

            if (existing is null &&
                !string.IsNullOrWhiteSpace(candidate.DisplayName))
            {
                byPendingExactName.TryGetValue(candidate.DisplayName, out existing);
            }

            if (existing is null)
            {
                candidate.Status = candidate.IsInsideAssignedTerritory ? "Available" : "InvalidOutsideTerritory";
                candidate.CanSelect = candidate.HasBoundary &&
                    candidate.HasDisplayBoundary &&
                    candidate.IsInsideAssignedTerritory &&
                    candidate.Status == "Available";
                continue;
            }

            candidate.Status = "ExistingRegion";
            candidate.CanSelect = false;
            candidate.ExistingRegionId = existing.Id;
            candidate.ExistingRegionName = existing.Name;
            candidate.ValidationMessage = DuplicateAdministrativeRegionMessage;
        }
    }

    private static string BuildShapeCandidateKey(string? shapeGroup, string? shapeId) =>
        $"{shapeGroup?.Trim().ToUpperInvariant()}::{shapeId?.Trim().ToUpperInvariant()}";

    private static bool HasBoundaryCandidateRequest(TerritoryRegionCreateRequestDto request) =>
        !string.IsNullOrWhiteSpace(request.ShapeId) ||
        !string.IsNullOrWhiteSpace(request.CandidateExternalKey) ||
        !string.IsNullOrWhiteSpace(request.BoundaryCandidateId) ||
        !string.IsNullOrWhiteSpace(request.BoundaryExternalKey);

    private static string NormalizeAdm1CandidateLevel(string? value)
    {
        var normalized = string.IsNullOrWhiteSpace(value)
            ? Adm1Level
            : value.Trim().Replace(" ", string.Empty).Replace("-", string.Empty).ToUpperInvariant();

        return normalized == Adm1Level
            ? normalized
            : throw new InvalidOperationException("Boundary candidate must be an ADM1 administrative region.");
    }

    private static string NormalizeRequiredBoundaryCandidateShapeId(string? value)
    {
        var shapeId = value?.Trim() ?? string.Empty;
        if (shapeId.Length == 0)
            throw new InvalidOperationException("Boundary candidate shape id is required.");

        return shapeId;
    }

    private static int? TryParseBoundaryCandidateId(string? value)
    {
        return int.TryParse(value, out var candidateId) && candidateId > 0
            ? candidateId
            : null;
    }

    private static string NormalizeCandidateRegionName(string? requestedName, string candidateName)
    {
        var name = string.IsNullOrWhiteSpace(requestedName)
            ? candidateName.Trim()
            : requestedName.Trim();

        if (name.Length == 0)
            throw new InvalidOperationException("Region name is required.");

        if (name.Length > 200)
            throw new InvalidOperationException("Region name is too long.");

        return name;
    }

    private static string? NormalizeOptional(string? value)
    {
        var normalized = value?.Trim();
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }

    private static string? FirstNonEmptyNullable(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
                return value.Trim();
        }

        return null;
    }

    private static bool BoundaryIsoCodesMatch(string first, string second) =>
        string.Equals(
            CountryRegistry.NormalizeIso3ForBoundaryImport(first),
            CountryRegistry.NormalizeIso3ForBoundaryImport(second),
            StringComparison.OrdinalIgnoreCase) ||
        CountryRegistry.AreBoundaryImportIsoAliases(first, second);

    private static BoundaryResult BuildRegionBoundary(TerritoryRegionBoundaryDto? boundary)
    {
        if (boundary is null)
            throw new InvalidOperationException("Region boundary is required.");

        var shapeType = NormalizeRegionShapeType(boundary.ShapeType);
        var coordinates = boundary.Coordinates ?? new List<TerritoryRegionBoundaryCoordinateDto>();

        if (coordinates.Count < 3)
            throw new InvalidOperationException("Boundary geometry must contain at least three points.");

        var ringCoordinates = coordinates
            .Select(point =>
            {
                ValidateLatitude(point.Latitude);
                ValidateLongitude(point.Longitude);
                return new Coordinate(point.Longitude, point.Latitude);
            })
            .ToList();

        var first = ringCoordinates[0];
        var last = ringCoordinates[^1];
        if (!CoordinatesEqual(first, last))
        {
            ringCoordinates.Add(new Coordinate(first.X, first.Y));
        }

        var uniquePointCount = ringCoordinates
            .Take(ringCoordinates.Count - 1)
            .Select(coordinate => $"{Math.Round(coordinate.X, 7)}:{Math.Round(coordinate.Y, 7)}")
            .Distinct(StringComparer.Ordinal)
            .Count();

        if (uniquePointCount < 3 || ringCoordinates.Count < 4)
            throw new InvalidOperationException("Boundary geometry must contain at least three distinct points.");

        var polygon = Wgs84GeometryFactory.CreatePolygon(ringCoordinates.ToArray());
        polygon.SRID = 4326;

        EnsureWgs84(polygon, "Boundary geometry is invalid.");
        if (polygon.Area <= 0)
            throw new InvalidOperationException("Boundary geometry is invalid.");

        return new BoundaryResult(shapeType, polygon);
    }

    private static string NormalizeRequiredRegionName(string? value)
    {
        var name = value?.Trim() ?? string.Empty;
        if (name.Length == 0)
            throw new InvalidOperationException("Region name is required.");

        if (name.Length > 200)
            throw new InvalidOperationException("Region name is too long.");

        return name;
    }

    private static string NormalizeBoundarySource(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return ManualDrawBoundarySource;

        var normalized = value.Trim().Replace(" ", string.Empty).Replace("-", string.Empty).ToLowerInvariant();
        return normalized switch
        {
            "manual" or "manualdraw" => ManualDrawBoundarySource,
            "osm" or "osmboundary" => OsmBoundarySource,
            _ => throw new InvalidOperationException("Boundary source is invalid.")
        };
    }

    private static string? ExtractCountryCode(string? boundarySource)
    {
        if (string.IsNullOrWhiteSpace(boundarySource) ||
            !boundarySource.StartsWith("CountryGeoJson:", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var code = boundarySource["CountryGeoJson:".Length..].Trim().ToUpperInvariant();
        return code.Length == 2 ? code : null;
    }

    private static string NormalizeRegionShapeType(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "Polygon";

        var normalized = value.Trim().Replace(" ", string.Empty).Replace("-", string.Empty).ToLowerInvariant();
        return normalized switch
        {
            "polygon" => "Polygon",
            "rectangle" => "Rectangle",
            _ => throw new InvalidOperationException("Region boundary shape type is invalid.")
        };
    }

    private static void ValidateLatitude(double latitude)
    {
        if (double.IsNaN(latitude) || double.IsInfinity(latitude) || latitude < -90d || latitude > 90d)
            throw new InvalidOperationException("Boundary geometry is invalid.");
    }

    private static void ValidateLongitude(double longitude)
    {
        if (double.IsNaN(longitude) || double.IsInfinity(longitude) || longitude < -180d || longitude > 180d)
            throw new InvalidOperationException("Boundary geometry is invalid.");
    }

    private static bool CoordinatesEqual(Coordinate left, Coordinate right) =>
        Math.Abs(left.X - right.X) < 1e-10 && Math.Abs(left.Y - right.Y) < 1e-10;

    private static void EnsureWgs84(Geometry geometry, string errorMessage)
    {
        if (geometry.IsEmpty || !geometry.IsValid)
            throw new InvalidOperationException(errorMessage);

        if (geometry.SRID == 0)
        {
            geometry.SRID = 4326;
            return;
        }

        if (geometry.SRID != 4326)
            throw new InvalidOperationException(errorMessage);
    }

    private static Geometry? ToPolygonalGeometry(Geometry? geometry)
    {
        if (geometry is null || geometry.IsEmpty)
            return null;

        var copy = geometry.Copy();
        copy.SRID = copy.SRID > 0 ? copy.SRID : 4326;

        if (!copy.IsValid)
            return null;

        if (copy is Polygon or MultiPolygon)
            return copy;

        if (copy is GeometryCollection collection)
        {
            var polygons = Enumerable.Range(0, collection.NumGeometries)
                .Select(collection.GetGeometryN)
                .Where(item => item is Polygon or MultiPolygon)
                .ToList();

            if (polygons.Count == 0)
                return null;

            return polygons.Count == 1
                ? polygons[0]
                : ToPolygonalGeometry(UnaryUnionOp.Union(polygons));
        }

        return null;
    }

    private static TerritoryRegionGeometryDto? ToGeoJson(Geometry? geometry)
    {
        var polygonalGeometry = ToPolygonalGeometry(geometry);
        if (polygonalGeometry is null)
            return null;

        return polygonalGeometry switch
        {
            Polygon polygon => new TerritoryRegionGeometryDto
            {
                Type = "Polygon",
                Coordinates = ToPolygonCoordinates(polygon)
            },
            MultiPolygon multiPolygon => new TerritoryRegionGeometryDto
            {
                Type = "MultiPolygon",
                Coordinates = Enumerable.Range(0, multiPolygon.NumGeometries)
                    .Select(index => ToPolygonCoordinates((Polygon)multiPolygon.GetGeometryN(index)))
                    .ToArray()
            },
            _ => null
        };
    }

    private static double[][][] ToPolygonCoordinates(Polygon polygon)
    {
        var rings = new List<double[][]>
        {
            ToRingCoordinates(polygon.ExteriorRing)
        };

        for (var index = 0; index < polygon.NumInteriorRings; index++)
        {
            rings.Add(ToRingCoordinates(polygon.GetInteriorRingN(index)));
        }

        return rings.ToArray();
    }

    private static double[][] ToRingCoordinates(LineString ring) =>
        ring.Coordinates
            .Select(coordinate => new[]
            {
                RoundCoordinate(coordinate.X),
                RoundCoordinate(coordinate.Y)
            })
            .ToArray();

    private static TerritoryRegionMapViewportDto BuildRegionMapViewport(IEnumerable<Geometry> geometries)
    {
        var accumulator = new BoundsAccumulator();

        foreach (var geometry in geometries)
        {
            IncludeGeometry(accumulator, ToGeoJson(geometry));
        }

        if (!accumulator.HasValue)
        {
            return new TerritoryRegionMapViewportDto { IsEmpty = true };
        }

        var centerLatitude = (accumulator.North + accumulator.South) / 2d;
        var centerLongitude = (accumulator.East + accumulator.West) / 2d;
        var latitudeSpan = Math.Abs(accumulator.North - accumulator.South);
        var longitudeSpan = Math.Abs(accumulator.East - accumulator.West);
        var isSinglePoint = latitudeSpan < 0.00001d && longitudeSpan < 0.00001d;

        return new TerritoryRegionMapViewportDto
        {
            CenterLatitude = centerLatitude,
            CenterLongitude = centerLongitude,
            Zoom = isSinglePoint ? 13d : EstimateMapZoom(Math.Max(latitudeSpan, longitudeSpan)),
            Bounds = isSinglePoint
                ? null
                : new TerritoryRegionMapBoundsDto
                {
                    North = accumulator.North,
                    South = accumulator.South,
                    East = accumulator.East,
                    West = accumulator.West
                },
            IsEmpty = false
        };
    }

    private static void IncludeGeometry(BoundsAccumulator accumulator, TerritoryRegionGeometryDto? geometry)
    {
        if (geometry?.Coordinates is null)
            return;

        if (geometry.Type.Equals("Polygon", StringComparison.OrdinalIgnoreCase) &&
            geometry.Coordinates is double[][][] polygon)
        {
            foreach (var ring in polygon)
            {
                foreach (var coordinate in ring)
                {
                    if (coordinate.Length >= 2)
                        accumulator.Include(coordinate[1], coordinate[0]);
                }
            }
        }
        else if (geometry.Type.Equals("MultiPolygon", StringComparison.OrdinalIgnoreCase) &&
                 geometry.Coordinates is double[][][][] multiPolygon)
        {
            foreach (var polygonCoordinates in multiPolygon)
            {
                foreach (var ring in polygonCoordinates)
                {
                    foreach (var coordinate in ring)
                    {
                        if (coordinate.Length >= 2)
                            accumulator.Include(coordinate[1], coordinate[0]);
                    }
                }
            }
        }
    }

    private static double EstimateMapZoom(double span)
    {
        if (span >= 8d) return 6d;
        if (span >= 4d) return 7d;
        if (span >= 2d) return 8d;
        if (span >= 1d) return 9d;
        if (span >= 0.5d) return 10d;
        if (span >= 0.2d) return 11d;
        if (span >= 0.08d) return 12d;
        return 13d;
    }

    private static double RoundCoordinate(double coordinate) =>
        Math.Round(coordinate, 7, MidpointRounding.AwayFromZero);

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

        var normalizedRoles = roles.Select(RoleNames.Normalize).ToList();
        if (!normalizedRoles.Contains(RoleNames.TerritoryManager) &&
            !normalizedRoles.Contains(RoleNames.TerritoryAssociate))
        {
            throw new UnauthorizedAccessException("Territory administrator role is required.");
        }
    }

    private async Task EnsureTerritoryManagerOnlyRoleAsync(int userId)
    {
        var roles = await _db.UserRoles
            .AsNoTracking()
            .Where(userRole => userRole.UserId == userId)
            .Select(userRole => userRole.Role.Name)
            .ToListAsync();

        var normalizedRoles = roles.Select(RoleNames.Normalize).ToList();
        if (!normalizedRoles.Contains(RoleNames.TerritoryManager))
        {
            throw new UnauthorizedAccessException("Territory Manager role is required.");
        }
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

    private static int TotalContentCount(TerritoryRegionHealthDto region) =>
        region.DestinationsCount +
        region.LocationsCount +
        region.PlacesCount +
        region.StaysCount +
        region.EventsCount;

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

    private static bool IsDisplayableRepresentativeImageCandidate(RegionRepresentativeImageCandidate candidate) =>
        !string.IsNullOrWhiteSpace(candidate.Url) &&
        IsDisplayableContentStatus(candidate.SourceStatus);

    private static bool IsDisplayableContentStatus(string? status)
    {
        var normalized = status?.Trim();
        return !string.Equals(normalized, "Deleted", StringComparison.OrdinalIgnoreCase) &&
               !string.Equals(normalized, "Suspended", StringComparison.OrdinalIgnoreCase) &&
               !string.Equals(normalized, "Disabled", StringComparison.OrdinalIgnoreCase);
    }

    private static int RepresentativeImagePreferenceGroup(RegionRepresentativeImageCandidate candidate)
    {
        var normalizedRole = EntityImageRole.Normalize(candidate.ImageRole);
        return candidate.IsMain ||
               string.Equals(normalizedRole, EntityImageRole.Cover, StringComparison.Ordinal) ||
               string.Equals(normalizedRole, EntityImageRole.Card, StringComparison.Ordinal)
            ? 0
            : 1;
    }

    private static int RepresentativeImageRoleRank(string? imageRole)
    {
        var normalizedRole = EntityImageRole.Normalize(imageRole);
        if (string.Equals(normalizedRole, EntityImageRole.Cover, StringComparison.Ordinal))
            return 0;

        if (string.Equals(normalizedRole, EntityImageRole.Card, StringComparison.Ordinal))
            return 1;

        return 2;
    }

    private static string? NullIfWhiteSpace(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

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
    }

    private sealed class RegionRepresentativeImageCandidate
    {
        public int RegionId { get; set; }
        public int SourcePriority { get; set; }
        public string SourceType { get; set; } = string.Empty;
        public int SourceId { get; set; }
        public string SourceName { get; set; } = string.Empty;
        public string SourceStatus { get; set; } = string.Empty;
        public string Url { get; set; } = string.Empty;
        public string? AltText { get; set; }
        public bool IsMain { get; set; }
        public string ImageRole { get; set; } = EntityImageRole.Gallery;
        public int SortOrder { get; set; }
        public DateTime CreatedAt { get; set; }
        public int ImageId { get; set; }
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

    private sealed record RegionPeriodRange(DateTime Start, DateTime End, string Key, string Label);

    private sealed record EntityKey(string EntityType, int EntityId);

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

    private sealed record BoundaryResult(string ShapeType, Geometry Geometry);

    private sealed class TerritoryBoundaryRow
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? CountryCode { get; set; }
        public string? CountryIso3 { get; set; }
        public string? BoundaryShapeType { get; set; }
        public string? BoundarySource { get; set; }
        public Geometry? BoundaryGeometry { get; set; }
        public Geometry? BoundaryDisplayGeometry { get; set; }
    }

    private sealed class RegionBoundaryRow
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public Geometry? BoundaryGeometry { get; set; }
    }

    private sealed class TerritoryRegionMapRow
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public int TerritoryId { get; set; }
        public string TerritoryName { get; set; } = string.Empty;
        public Geometry? BoundaryGeometry { get; set; }
        public Geometry? BoundaryDisplayGeometry { get; set; }
    }

    private sealed class RegionBoundaryMetadataRow
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public int? BoundaryCandidateId { get; set; }
        public string? BoundaryExternalKey { get; set; }
        public string? BoundaryShapeGroup { get; set; }
        public string? BoundaryShapeId { get; set; }
        public string? BoundaryAdmLevel { get; set; }
    }

    private sealed class BoundsAccumulator
    {
        public bool HasValue { get; private set; }
        public double North { get; private set; }
        public double South { get; private set; }
        public double East { get; private set; }
        public double West { get; private set; }

        public void Include(double latitude, double longitude)
        {
            if (!IsValidCoordinate(latitude, longitude))
                return;

            if (!HasValue)
            {
                North = latitude;
                South = latitude;
                East = longitude;
                West = longitude;
                HasValue = true;
                return;
            }

            North = Math.Max(North, latitude);
            South = Math.Min(South, latitude);
            East = Math.Max(East, longitude);
            West = Math.Min(West, longitude);
        }

        private static bool IsValidCoordinate(double latitude, double longitude) =>
            !double.IsNaN(latitude) &&
            !double.IsNaN(longitude) &&
            !double.IsInfinity(latitude) &&
            !double.IsInfinity(longitude) &&
            latitude >= -90d &&
            latitude <= 90d &&
            longitude >= -180d &&
            longitude <= 180d;
    }
}
