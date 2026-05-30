using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Geotrack.Application.DTOs.Admin;
using Geotrack.Application.Interfaces.Service;
using Geotrack.Application.Security;
using Geotrack.Domain.Entities.Core;
using Geotrack.Domain.Entities.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using NetTopologySuite.Geometries;

namespace Geotrack.Infrastructure.Services;

public sealed class AdminTerritoryManagementService : IAdminTerritoryManagementService
{
    private const int DefaultPageSize = 100;
    private const int MaxPageSize = 250;
    private const string BoundaryGeometryPropertyName = "BoundaryGeometry";
    private const string BoundaryDisplayGeometryPropertyName =
        TerritoryBoundaryDisplayGeometry.BoundaryDisplayGeometryPropertyName;
    private const string GeoBoundariesBoundarySource = "geoBoundaries";
    private const string MapSerializationVersion = "adm0-display-v4-split-status";
    private const string MapBoundariesSerializationVersion = "adm0-display-boundaries-v1";
    private const string MapStatusSerializationVersion = "adm0-display-status-v1";
    private const int FullGeometryCoordinateDecimals = 7;
    private const int DisplayGeometryCoordinateDecimals = 5;
    private const double MinimumDisplayAreaRetentionForMap = 0.5d;
    private const double AntimeridianRawSpanThreshold = 180d;
    private const double LongitudeComparisonTolerance = 0.0000001d;
    private const string MissingBoundaryAssignmentMessage =
        "This country cannot be assigned until its boundary geometry is available.";
    private const string MissingDisplayBoundaryMessage =
        "This country boundary is available for validation, but its display geometry must be rebuilt before it can be shown on the map.";
    private const string DisputedViewOnlyMessage =
        "This disputed boundary is shown for map completeness and cannot be assigned as a country territory.";
    private const string DisputedAdmLevel = "DISP";
    private static readonly GeometryFactory Wgs84GeometryFactory = new(new PrecisionModel(), 4326);

    private readonly AppDbContext _db;
    private readonly IGeoBoundariesService _geoBoundaries;
    private readonly IMemoryCache? _cache;

    public AdminTerritoryManagementService(
        AppDbContext db,
        IGeoBoundariesService geoBoundaries,
        IMemoryCache? cache = null)
    {
        _db = db;
        _geoBoundaries = geoBoundaries;
        _cache = cache;
    }

    public async Task<AdminTerritoryPageResponseDto> GetTerritoriesAsync(
        AdminTerritoryPageRequest request,
        CancellationToken cancellationToken = default)
    {
        var page = Math.Max(1, request.Page);
        var pageSize = Math.Clamp(request.PageSize <= 0 ? DefaultPageSize : request.PageSize, 1, MaxPageSize);
        var countryFilter = request.CountryFilter
            .Select(NormalizeCountryCode)
            .Where(code => code is not null)
            .Cast<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var query = _db.Territories.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var term = request.Search.Trim().ToLowerInvariant();
            query = query.Where(territory =>
                territory.Name.ToLower().Contains(term) ||
                (territory.CountryCode != null && territory.CountryCode.ToLower().Contains(term)) ||
                (territory.CountryIso3 != null && territory.CountryIso3.ToLower().Contains(term)) ||
                territory.TerritoryManagerTerritories.Any(assignment =>
                    assignment.TerritoryManager.User.Email.ToLower().Contains(term) ||
                    (assignment.TerritoryManager.User.DisplayName != null &&
                     assignment.TerritoryManager.User.DisplayName.ToLower().Contains(term)) ||
                    assignment.TerritoryManager.User.FirstName.ToLower().Contains(term) ||
                    assignment.TerritoryManager.User.LastName.ToLower().Contains(term)));
        }

        if (countryFilter.Length > 0)
        {
            query = query.Where(territory => territory.CountryCode != null && countryFilter.Contains(territory.CountryCode));
        }

        var total = await query.CountAsync(cancellationToken);
        query = ApplySort(query, request.SortField, request.SortDir);

        var rows = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(territory => new TerritoryDirectoryRow
            {
                TerritoryId = territory.Id,
                Name = territory.Name,
                TerritoryKind = territory.TerritoryKind,
                CountryCode = territory.CountryCode,
                CountryIso3 = territory.CountryIso3,
                BoundarySource = territory.BoundarySource,
                BoundarySourceUrl = territory.BoundarySourceUrl,
                BoundaryShapeType = territory.BoundaryShapeType,
                BoundaryShapeGroup = territory.BoundaryShapeGroup,
                BoundaryShapeId = territory.BoundaryShapeId,
                BoundaryExternalKey = territory.BoundaryExternalKey,
                BoundaryAdmLevel = territory.BoundaryAdmLevel,
                BoundaryReleaseType = territory.BoundaryReleaseType,
                BoundaryLicense = territory.BoundaryLicense,
                BoundaryImportedAt = territory.BoundaryImportedAt,
                HasBoundary = EF.Property<Geometry?>(territory, BoundaryGeometryPropertyName) != null,
                CreatedAt = territory.CreatedAt,
                UpdatedAt = territory.UpdatedAt,
                Manager = territory.TerritoryManagerTerritories
                    .Where(assignment => assignment.TerritoryManager.User.UserRoles.Any(userRole =>
                        userRole.Role.Name == RoleNames.TerritoryManager))
                    .OrderBy(assignment => assignment.CreatedAt)
                    .Select(assignment => new TerritoryManagerRow
                    {
                        UserId = assignment.UserId,
                        DisplayName = assignment.TerritoryManager.User.DisplayName,
                        FirstName = assignment.TerritoryManager.User.FirstName,
                        LastName = assignment.TerritoryManager.User.LastName,
                        Email = assignment.TerritoryManager.User.Email
                    })
                    .FirstOrDefault()
            })
            .ToListAsync(cancellationToken);

        return new AdminTerritoryPageResponseDto
        {
            Items = rows.Select(MapTerritory).ToList(),
            Total = total
        };
    }

    public async Task<AdminTerritoryMapFeatureCollectionDto> GetTerritoryMapAsync(
        CancellationToken cancellationToken = default)
    {
        var rows = await LoadMapRowsWithDisplayAndStatusAsync(cancellationToken);
        var features = BuildLegacyMapFeatures(rows);

        var generatedAt = BuildMapLastModified(rows);
        return new AdminTerritoryMapFeatureCollectionDto
        {
            Features = features
                .OrderByDescending(feature => feature.Geometry is not null)
                .ThenBy(feature => feature.Properties.DisplayName)
                .ToList(),
            GeneratedAt = generatedAt,
            LastModified = generatedAt,
            ETag = BuildMapETag(rows, features.Count)
        };
    }

    public async Task<AdminTerritoryMapBoundaryFeatureCollectionDto> GetTerritoryMapBoundariesAsync(
        CancellationToken cancellationToken = default)
    {
        if (_cache is not null &&
            _cache.TryGetValue(
                TerritoryMapBoundaryCache.BoundariesKey,
                out AdminTerritoryMapBoundaryFeatureCollectionDto? cached) &&
            cached is not null)
        {
            return cached;
        }

        var rows = await LoadMapBoundaryRowsAsync(cancellationToken);
        var features = BuildBoundaryMapFeatures(rows);
        var generatedAt = BuildMapBoundaryLastModified(rows);
        var result = new AdminTerritoryMapBoundaryFeatureCollectionDto
        {
            Features = features
                .OrderByDescending(feature => feature.Geometry is not null)
                .ThenBy(feature => feature.Properties.DisplayName)
                .ToList(),
            GeneratedAt = generatedAt,
            LastModified = generatedAt,
            ETag = BuildMapBoundariesETag(rows, features.Count)
        };

        _cache?.Set(
            TerritoryMapBoundaryCache.BoundariesKey,
            result,
            new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(12),
                SlidingExpiration = TimeSpan.FromHours(2)
            });

        return result;
    }

    public async Task<AdminTerritoryMapStatusCollectionDto> GetTerritoryMapStatusAsync(
        CancellationToken cancellationToken = default)
    {
        var rows = await LoadMapStatusRowsAsync(cancellationToken);
        var items = BuildMapStatusItems(rows);
        var generatedAt = BuildMapLastModified(rows);

        return new AdminTerritoryMapStatusCollectionDto
        {
            Items = items
                .OrderBy(item => item.DisplayName)
                .ToList(),
            GeneratedAt = generatedAt,
            LastModified = generatedAt,
            ETag = BuildMapStatusETag(rows, items.Count)
        };
    }

    public async Task<AdminTerritoryDetailDto> GetTerritoryDetailAsync(
        int territoryId,
        CancellationToken cancellationToken = default)
    {
        if (territoryId <= 0)
            throw new InvalidOperationException("Territory id is required.");

        var territory = await _db.Territories
            .AsNoTracking()
            .Where(item => item.Id == territoryId)
            .Select(item => new TerritoryDetailRow
            {
                TerritoryId = item.Id,
                Name = item.Name,
                TerritoryKind = item.TerritoryKind,
                CountryCode = item.CountryCode,
                CountryIso3 = item.CountryIso3,
                BoundarySource = item.BoundarySource,
                BoundarySourceUrl = item.BoundarySourceUrl,
                BoundaryShapeType = item.BoundaryShapeType,
                BoundaryShapeGroup = item.BoundaryShapeGroup,
                BoundaryShapeId = item.BoundaryShapeId,
                BoundaryExternalKey = item.BoundaryExternalKey,
                BoundaryAdmLevel = item.BoundaryAdmLevel,
                BoundaryReleaseType = item.BoundaryReleaseType,
                BoundaryLicense = item.BoundaryLicense,
                BoundaryImportedAt = item.BoundaryImportedAt,
                BoundaryGeometry = EF.Property<Geometry?>(item, BoundaryGeometryPropertyName),
                CreatedAt = item.CreatedAt,
                UpdatedAt = item.UpdatedAt
            })
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new KeyNotFoundException("Territory not found.");

        territory.Manager = await _db.TerritoryManagerTerritories
            .AsNoTracking()
            .Where(assignment => assignment.TerritoryId == territoryId)
            .OrderBy(assignment => assignment.CreatedAt)
            .Select(assignment => new TerritoryManagerDetailRow
            {
                UserId = assignment.UserId,
                DisplayName = assignment.TerritoryManager.User.DisplayName,
                FirstName = assignment.TerritoryManager.User.FirstName,
                LastName = assignment.TerritoryManager.User.LastName,
                Email = assignment.TerritoryManager.User.Email,
                AvatarUrl = assignment.TerritoryManager.User.AvatarUrl,
                AssignedAt = assignment.CreatedAt,
                Roles = assignment.TerritoryManager.User.UserRoles
                    .Select(userRole => userRole.Role.Name)
                    .ToList()
            })
            .FirstOrDefaultAsync(cancellationToken);

        return MapTerritoryDetail(territory);
    }

    public async Task<AdminTerritoryManagerSearchResponseDto> SearchManagerCandidatesAsync(
        string? query,
        int take,
        int? countryTerritoryId = null,
        CancellationToken cancellationToken = default)
    {
        var normalizedTake = Math.Clamp(take <= 0 ? 20 : take, 1, 50);
        var normalizedQuery = query?.Trim().ToLowerInvariant();

        var users = _db.Users
            .AsNoTracking()
            .Where(user => !user.IsDeleted)
            .Include(user => user.UserRoles)
                .ThenInclude(userRole => userRole.Role)
            .Include(user => user.TerritoryManager!)
                .ThenInclude(manager => manager.TerritoryManagerTerritories)
                    .ThenInclude(assignment => assignment.Territory)
            .AsSplitQuery()
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(normalizedQuery))
        {
            users = users.Where(user =>
                user.Email.ToLower().Contains(normalizedQuery) ||
                user.Username.ToLower().Contains(normalizedQuery) ||
                user.FirstName.ToLower().Contains(normalizedQuery) ||
                user.LastName.ToLower().Contains(normalizedQuery) ||
                (user.DisplayName != null && user.DisplayName.ToLower().Contains(normalizedQuery)));
        }

        var candidates = await users
            .OrderBy(user => user.FirstName)
            .ThenBy(user => user.LastName)
            .ThenBy(user => user.Email)
            .Take(normalizedTake)
            .ToListAsync(cancellationToken);

        return new AdminTerritoryManagerSearchResponseDto
        {
            Items = candidates.Select(user => MapCandidate(user, countryTerritoryId)).ToList()
        };
    }

    public async Task<AdminTerritoryManagerSearchResponseDto> GetAssignableManagersAsync(
        int territoryId,
        string? query,
        int take,
        CancellationToken cancellationToken = default)
    {
        if (territoryId <= 0)
            throw new InvalidOperationException("Territory id is required.");

        var territoryKind = await _db.Territories
            .AsNoTracking()
            .Where(item => item.Id == territoryId)
            .Select(item => item.TerritoryKind)
            .SingleOrDefaultAsync(cancellationToken);

        if (territoryKind is null)
            throw new KeyNotFoundException("Territory not found.");

        if (!IsCountryKind(territoryKind))
            throw new InvalidOperationException(DisputedViewOnlyMessage);

        return await SearchManagerCandidatesAsync(query, take, territoryId, cancellationToken);
    }

    public async Task<AdminTerritoryDto> CreateTerritoryAsync(
        AdminCreateTerritoryRequestDto request,
        CancellationToken cancellationToken = default)
    {
        var countryCode = NormalizeCountryCode(request.CountryCode)
            ?? throw new InvalidOperationException("Country code is required.");
        var name = NormalizeName(request.Name, "Country name is required.");
        var countryIso3 = CountryRegistry.ToIso3(countryCode)
            ?? throw new InvalidOperationException("Country code is invalid.");
        var now = DateTime.UtcNow;

        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);

        var existing = await _db.Territories
            .Include(territory => territory.TerritoryManagerTerritories)
            .FirstOrDefaultAsync(
                territory =>
                    territory.TerritoryKind == TerritoryKinds.Country &&
                    (territory.CountryIso3 == countryIso3 ||
                     territory.CountryCode == countryCode ||
                     (territory.CountryCode == null &&
                      territory.Name.ToLower() == name.ToLower())),
                cancellationToken);

        if (existing is null)
        {
            throw new InvalidOperationException("Boundary data is missing. Import ADM0 boundary before assigning this territory.");
        }

        if (existing.TerritoryManagerTerritories.Count > 0)
        {
            throw new InvalidOperationException("This country is already assigned to a territory.");
        }

        var boundary = await _db.Territories
            .Where(item => item.Id == existing.Id)
            .Select(item => EF.Property<Geometry?>(item, BoundaryGeometryPropertyName))
            .SingleAsync(cancellationToken);
        if (!IsValidAdm0Boundary(boundary))
        {
            throw new InvalidOperationException(MissingBoundaryAssignmentMessage);
        }

        existing.Name = name;
        existing.TerritoryKind = TerritoryKinds.Country;
        existing.CountryCode = countryCode;
        existing.CountryIso3 = countryIso3;
        existing.UpdatedAt = now;

        if (request.AssignedManagerUserId.HasValue)
        {
            await AssignManagerToTerritoryAsync(
                existing,
                request.AssignedManagerUserId.Value,
                allowedExistingTerritoryId: null,
                now,
                cancellationToken);
        }

        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        TerritoryMapBoundaryCache.Invalidate(_cache);

        return await GetTerritoryDtoAsync(existing.Id, cancellationToken);
    }

    public async Task<AdminTerritoryDetailDto> AssignManagerAsync(
        int territoryId,
        AdminAssignTerritoryManagerRequestDto request,
        CancellationToken cancellationToken = default)
    {
        if (territoryId <= 0)
            throw new InvalidOperationException("Territory id is required.");

        var territory = await _db.Territories
            .Include(item => item.TerritoryManagerTerritories)
                .ThenInclude(assignment => assignment.TerritoryManager)
                    .ThenInclude(manager => manager.User)
                        .ThenInclude(user => user.UserRoles)
                            .ThenInclude(userRole => userRole.Role)
            .AsSplitQuery()
            .SingleOrDefaultAsync(item => item.Id == territoryId, cancellationToken)
            ?? throw new KeyNotFoundException("Territory not found.");

        var requestedManagerUserId = request.ManagerUserId ?? request.AssignedManagerUserId;

        if (requestedManagerUserId.HasValue)
        {
            if (!IsCountryKind(territory.TerritoryKind))
            {
                throw new InvalidOperationException(DisputedViewOnlyMessage);
            }

            var boundary = await _db.Territories
                .Where(item => item.Id == territoryId)
                .Select(item => EF.Property<Geometry?>(item, BoundaryGeometryPropertyName))
                .SingleAsync(cancellationToken);

            if (!IsValidAdm0Boundary(boundary))
            {
                throw new InvalidOperationException(MissingBoundaryAssignmentMessage);
            }
        }

        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);

        var alreadyAssignedManagerUserId = territory.TerritoryManagerTerritories
            .Select(assignment => assignment.UserId)
            .FirstOrDefault();
        if (requestedManagerUserId.HasValue &&
            requestedManagerUserId.Value == alreadyAssignedManagerUserId)
        {
            territory.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return await GetTerritoryDetailAsync(territoryId, cancellationToken);
        }

        var currentManagerAssignments = territory.TerritoryManagerTerritories
            .Where(assignment => !requestedManagerUserId.HasValue || assignment.UserId != requestedManagerUserId.Value)
            .ToList();
        var removedManagerUserIds = currentManagerAssignments
            .Select(assignment => assignment.UserId)
            .Distinct()
            .ToList();
        _db.TerritoryManagerTerritories.RemoveRange(currentManagerAssignments);

        foreach (var removedUserId in removedManagerUserIds)
        {
            await RemoveTerritoryManagerRoleIfUnassignedAsync(removedUserId, territoryId, cancellationToken);
        }

        if (requestedManagerUserId.HasValue)
        {
            await AssignManagerToTerritoryAsync(
                territory,
                requestedManagerUserId.Value,
                allowedExistingTerritoryId: territoryId,
                DateTime.UtcNow,
                cancellationToken);
        }

        territory.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return await GetTerritoryDetailAsync(territoryId, cancellationToken);
    }

    public async Task<AdminTerritoryDetailDto> ImportBoundaryAsync(
        int territoryId,
        CancellationToken cancellationToken = default)
    {
        if (territoryId <= 0)
            throw new InvalidOperationException("Territory id is required.");

        var territory = await _db.Territories
            .AsNoTracking()
            .Where(item => item.Id == territoryId)
            .Select(item => new
            {
                item.Id,
                item.TerritoryKind,
                item.CountryCode,
                item.CountryIso3
            })
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new KeyNotFoundException("Territory not found.");

        if (!IsCountryKind(territory.TerritoryKind))
            throw new InvalidOperationException("Disputed territories cannot be imported as ADM0 country boundaries.");

        var iso3 = CountryRegistry.NormalizeIso3ForBoundaryImport(territory.CountryIso3)
            ?? CountryRegistry.NormalizeIso3ForBoundaryImport(territory.CountryCode)
            ?? CountryRegistry.ToIso3(territory.CountryCode)
            ?? throw new InvalidOperationException("Territory must have an ISO3 country code before importing boundaries.");

        await _geoBoundaries.ImportAdm0BoundaryAsync(iso3, territory.Id, cancellationToken);
        return await GetTerritoryDetailAsync(territory.Id, cancellationToken);
    }

    public async Task<AdminTerritoryDetailDto> ImportBoundaryAsync(
        AdminImportTerritoryBoundaryRequestDto request,
        CancellationToken cancellationToken = default)
    {
        var iso3 = CountryRegistry.NormalizeIso3ForBoundaryImport(request.Iso3)
            ?? throw new InvalidOperationException("ISO3 country code is required.");

        var result = await _geoBoundaries.ImportAdm0BoundaryAsync(iso3, territoryId: null, cancellationToken);
        return await GetTerritoryDetailAsync(result.TerritoryId, cancellationToken);
    }

    public async Task<GeoBoundariesBatchImportResultDto> ImportMissingBoundariesAsync(
        AdminImportTerritoryBoundariesRequestDto request,
        CancellationToken cancellationToken = default)
    {
        return await _geoBoundaries.ImportMissingAdm0BoundariesAsync(
            cancellationToken,
            request.ForceRefresh);
    }

    public async Task<GeoBoundariesBatchImportResultDto> ImportAllBoundariesAsync(
        AdminImportTerritoryBoundariesRequestDto request,
        CancellationToken cancellationToken = default)
    {
        return await _geoBoundaries.ImportAllAdm0BoundariesAsync(
            cancellationToken,
            request.ForceRefresh);
    }

    public async Task<BoundaryDisplayGeometryRebuildResultDto> RebuildDisplayGeometryAsync(
        bool onlyMissing = true,
        string? iso3 = null,
        bool forceRefresh = false,
        CancellationToken cancellationToken = default)
    {
        var normalizedIso3 = string.IsNullOrWhiteSpace(iso3)
            ? null
            : CountryRegistry.NormalizeIso3ForBoundaryImport(iso3)
                ?? throw new InvalidOperationException("ISO3 country code is invalid.");
        var normalizedIso2 = CountryRegistry.ToIso2ForBoundaryImport(normalizedIso3);
        var startedAt = DateTime.UtcNow;
        var result = new BoundaryDisplayGeometryRebuildResultDto
        {
            ForceRefresh = forceRefresh,
            OnlyMissing = onlyMissing,
            Iso3 = normalizedIso3,
            StartedAt = startedAt
        };

        var query = _db.Territories
            .AsNoTracking()
            .Select(territory => new
            {
                territory.Id,
                territory.Name,
                territory.TerritoryKind,
                territory.CountryCode,
                territory.CountryIso3,
                territory.BoundaryShapeGroup,
                BoundaryGeometry = EF.Property<Geometry?>(territory, BoundaryGeometryPropertyName),
                BoundaryDisplayGeometry = EF.Property<Geometry?>(territory, BoundaryDisplayGeometryPropertyName)
            });

        if (normalizedIso3 is not null)
        {
            var isoAliases = CountryRegistry.BoundaryImportIsoAliases(normalizedIso3);
            query = query.Where(territory =>
                (territory.CountryIso3 != null && isoAliases.Contains(territory.CountryIso3)) ||
                (territory.BoundaryShapeGroup != null && isoAliases.Contains(territory.BoundaryShapeGroup)) ||
                (normalizedIso2 != null && territory.CountryCode == normalizedIso2));
        }

        var rows = await query
            .ToListAsync(cancellationToken);
        if (onlyMissing && !forceRefresh)
        {
            rows = rows
                .Where(row => !TerritoryBoundaryDisplayGeometry.IsValid(
                    row.BoundaryGeometry,
                    row.BoundaryDisplayGeometry))
                .ToList();
        }

        result.ScannedCount = rows.Count;
        result.TotalChecked = rows.Count;
        var now = DateTime.UtcNow;

        foreach (var row in rows)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var item = new BoundaryDisplayGeometryRebuildItemDto
            {
                TerritoryId = row.Id,
                Iso3 = row.CountryIso3 ?? row.BoundaryShapeGroup,
                Name = row.Name,
                TerritoryKind = row.TerritoryKind
            };

            if (row.BoundaryGeometry is null || row.BoundaryGeometry.IsEmpty)
            {
                result.SkippedCount++;
                item.Status = "Skipped";
                item.Message = "Full boundary geometry is missing.";
                result.Items.Add(item);
                continue;
            }

            if (!forceRefresh &&
                TerritoryBoundaryDisplayGeometry.IsValid(row.BoundaryGeometry, row.BoundaryDisplayGeometry))
            {
                result.SkippedCount++;
                item.Status = "Skipped";
                item.Message = "Display geometry is already valid.";
                result.Items.Add(item);
                continue;
            }

            var displayGeometry = TerritoryBoundaryDisplayGeometry.Create(row.BoundaryGeometry);
            if (displayGeometry is null)
            {
                result.FailedCount++;
                item.Status = "Failed";
                item.Message = "Display geometry could not be generated from the full boundary geometry.";
                result.Items.Add(item);
                continue;
            }

            var territory = new Territory { Id = row.Id };
            _db.Attach(territory);
            _db.Entry(territory)
                .Property<Geometry?>(BoundaryDisplayGeometryPropertyName)
                .CurrentValue = displayGeometry;
            _db.Entry(territory)
                .Property(BoundaryDisplayGeometryPropertyName)
                .IsModified = true;
            territory.UpdatedAt = now;
            _db.Entry(territory)
                .Property(item => item.UpdatedAt)
                .IsModified = true;

            result.RebuiltCount++;
            item.Status = "Rebuilt";
            item.Message = "Display geometry was rebuilt from the full boundary geometry.";
            result.Items.Add(item);
        }

        if (result.RebuiltCount > 0)
        {
            await _db.SaveChangesAsync(cancellationToken);
            _db.ChangeTracker.Clear();
            TerritoryMapBoundaryCache.Invalidate(_cache);
        }

        result.CompletedAt = DateTime.UtcNow;
        return result;
    }

    public async Task<BoundaryGeometryDebugDto> GetBoundaryDebugAsync(
        string iso3,
        CancellationToken cancellationToken = default)
    {
        var normalizedIso3 = CountryRegistry.NormalizeIso3ForBoundaryImport(iso3)
            ?? throw new InvalidOperationException("ISO3 country code is invalid.");
        var normalizedIso2 = CountryRegistry.ToIso2ForBoundaryImport(normalizedIso3);
        var isoAliases = CountryRegistry.BoundaryImportIsoAliases(normalizedIso3);

        var row = await _db.Territories
            .AsNoTracking()
            .Where(territory =>
                (territory.CountryIso3 != null && isoAliases.Contains(territory.CountryIso3)) ||
                (territory.BoundaryShapeGroup != null && isoAliases.Contains(territory.BoundaryShapeGroup)) ||
                (normalizedIso2 != null && territory.CountryCode == normalizedIso2))
            .Select(territory => new TerritoryMapRow
            {
                TerritoryId = territory.Id,
                Name = territory.Name,
                TerritoryKind = territory.TerritoryKind,
                CountryCode = territory.CountryCode,
                CountryIso3 = territory.CountryIso3,
                BoundaryShapeGroup = territory.BoundaryShapeGroup,
                BoundaryGeometry = EF.Property<Geometry?>(territory, BoundaryGeometryPropertyName),
                BoundaryDisplayGeometry = EF.Property<Geometry?>(territory, BoundaryDisplayGeometryPropertyName)
            })
            .OrderByDescending(territory => territory.CountryIso3 == normalizedIso3)
            .ThenByDescending(territory => territory.BoundaryShapeGroup == normalizedIso3)
            .FirstOrDefaultAsync(cancellationToken);

        if (row is null)
            throw new KeyNotFoundException($"Territory boundary was not found for ISO3 '{normalizedIso3}'.");

        var fullStats = BuildGeometryDebugStats(row.BoundaryGeometry);
        var displayStats = BuildGeometryDebugStats(row.BoundaryDisplayGeometry);
        var displayGeoJson = ToGeoJson(row.BoundaryDisplayGeometry, DisplayGeometryCoordinateDecimals);
        var displayAreaRatio = fullStats.Area is > 0d && displayStats.Area is not null
            ? displayStats.Area / fullStats.Area
            : null;
        var crossesAntimeridian =
            HasAntimeridianCrossing(row.BoundaryGeometry) ||
            HasAntimeridianCrossing(row.BoundaryDisplayGeometry);
        var isFullGeometryValid = IsValidAdm0Boundary(row.BoundaryGeometry);
        var isDisplayGeometryValid = TerritoryBoundaryDisplayGeometry.IsValid(
            row.BoundaryGeometry,
            row.BoundaryDisplayGeometry);

        return new BoundaryGeometryDebugDto
        {
            TerritoryId = row.TerritoryId,
            Iso3 = row.CountryIso3 ?? row.BoundaryShapeGroup ?? normalizedIso3,
            Name = row.Name,
            TerritoryKind = row.TerritoryKind,
            FullGeometryType = fullStats.GeometryType,
            FullGeometryNumGeometries = fullStats.NumGeometries,
            FullGeometryCoordinateCount = fullStats.CoordinateCount,
            CoordinateCountFull = fullStats.CoordinateCount,
            FullGeometryEnvelope = fullStats.Envelope,
            FullArea = fullStats.Area,
            FullGeometryArea = fullStats.Area,
            LargestFullPolygonArea = fullStats.LargestPolygonArea,
            LargestFullPolygonEnvelope = fullStats.LargestPolygonEnvelope,
            IsFullGeometryValid = isFullGeometryValid,
            DisplayGeometryType = displayStats.GeometryType,
            DisplayGeometryNumGeometries = displayStats.NumGeometries,
            DisplayGeometryCoordinateCount = displayStats.CoordinateCount,
            CoordinateCountDisplay = displayStats.CoordinateCount,
            DisplayGeometryEnvelope = displayStats.Envelope,
            DisplayArea = displayStats.Area,
            DisplayGeometryArea = displayStats.Area,
            DisplayAreaRatio = displayAreaRatio,
            FullToDisplayAreaRatio = displayAreaRatio,
            LargestDisplayPolygonArea = displayStats.LargestPolygonArea,
            LargestDisplayPolygonEnvelope = displayStats.LargestPolygonEnvelope,
            IsDisplayGeometryValid = isDisplayGeometryValid,
            HasAntimeridianCrossing = crossesAntimeridian,
            CrossesAntimeridian = crossesAntimeridian,
            Viewport = BuildMapViewport(displayGeoJson)
        };
    }

    public async Task DeleteTerritoryAsync(int territoryId, CancellationToken cancellationToken = default)
    {
        if (territoryId <= 0)
            throw new InvalidOperationException("Territory id is required.");

        var territory = await _db.Territories
            .Include(item => item.Regions)
            .Include(item => item.TerritoryManagerTerritories)
            .AsSplitQuery()
            .SingleOrDefaultAsync(item => item.Id == territoryId, cancellationToken)
            ?? throw new KeyNotFoundException("Territory not found.");

        if (territory.Regions.Count > 0)
            throw new InvalidOperationException("This territory cannot be deleted while it contains regions.");

        _db.TerritoryManagerTerritories.RemoveRange(territory.TerritoryManagerTerritories);
        _db.Territories.Remove(territory);
        await _db.SaveChangesAsync(cancellationToken);
        TerritoryMapBoundaryCache.Invalidate(_cache);
    }

    private async Task<AdminTerritoryDto> GetTerritoryDtoAsync(int territoryId, CancellationToken cancellationToken)
    {
        var row = await _db.Territories
            .AsNoTracking()
            .Where(item => item.Id == territoryId)
            .Select(item => new TerritoryDirectoryRow
            {
                TerritoryId = item.Id,
                Name = item.Name,
                TerritoryKind = item.TerritoryKind,
                CountryCode = item.CountryCode,
                CountryIso3 = item.CountryIso3,
                BoundarySource = item.BoundarySource,
                BoundarySourceUrl = item.BoundarySourceUrl,
                BoundaryShapeType = item.BoundaryShapeType,
                BoundaryShapeGroup = item.BoundaryShapeGroup,
                BoundaryShapeId = item.BoundaryShapeId,
                BoundaryExternalKey = item.BoundaryExternalKey,
                BoundaryAdmLevel = item.BoundaryAdmLevel,
                BoundaryReleaseType = item.BoundaryReleaseType,
                BoundaryLicense = item.BoundaryLicense,
                BoundaryImportedAt = item.BoundaryImportedAt,
                HasBoundary = EF.Property<Geometry?>(item, BoundaryGeometryPropertyName) != null,
                CreatedAt = item.CreatedAt,
                UpdatedAt = item.UpdatedAt,
                Manager = item.TerritoryManagerTerritories
                    .Where(assignment => assignment.TerritoryManager.User.UserRoles.Any(userRole =>
                        userRole.Role.Name == RoleNames.TerritoryManager))
                    .OrderBy(assignment => assignment.CreatedAt)
                    .Select(assignment => new TerritoryManagerRow
                    {
                        UserId = assignment.UserId,
                        DisplayName = assignment.TerritoryManager.User.DisplayName,
                        FirstName = assignment.TerritoryManager.User.FirstName,
                        LastName = assignment.TerritoryManager.User.LastName,
                        Email = assignment.TerritoryManager.User.Email
                    })
                    .FirstOrDefault()
            })
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new KeyNotFoundException("Territory not found.");

        return MapTerritory(row);
    }

    private async Task AssignManagerToTerritoryAsync(
        Territory territory,
        int userId,
        int? allowedExistingTerritoryId,
        DateTime now,
        CancellationToken cancellationToken)
    {
        if (userId <= 0)
            throw new InvalidOperationException("Territory Manager selection is invalid.");

        var user = await _db.Users
            .Include(item => item.UserRoles)
                .ThenInclude(userRole => userRole.Role)
            .Include(item => item.TerritoryManager!)
                .ThenInclude(manager => manager.TerritoryManagerTerritories)
                    .ThenInclude(assignment => assignment.Territory)
            .AsSplitQuery()
            .SingleOrDefaultAsync(item => item.Id == userId, cancellationToken)
            ?? throw new InvalidOperationException("Selected user was not found.");

        var roles = user.UserRoles
            .Select(userRole => RoleNames.Normalize(userRole.Role.Name))
            .ToList();

        if (roles.Contains(RoleNames.Admin, StringComparer.Ordinal))
            throw new InvalidOperationException("Administrators cannot be assigned as Territory Managers.");

        if (user.IsDeleted)
            throw new InvalidOperationException("Deleted users cannot be assigned as Territory Managers.");

        if (user.IsSuspended)
            throw new InvalidOperationException("Suspended users cannot be assigned as Territory Managers.");

        var conflictingAssignment = user.TerritoryManager?.TerritoryManagerTerritories
            .FirstOrDefault(assignment =>
                !allowedExistingTerritoryId.HasValue ||
                assignment.TerritoryId != allowedExistingTerritoryId.Value);

        if (conflictingAssignment is not null)
        {
            throw new InvalidOperationException(
                $"This user is already assigned as a Territory Manager for {conflictingAssignment.Territory.Name}.");
        }

        await EnsureRoleAssignmentAsync(user, RoleNames.TerritoryManager, now, cancellationToken);

        if (user.TerritoryManager is null)
        {
            user.TerritoryManager = new TerritoryManager
            {
                UserId = user.Id
            };
            _db.TerritoryManagers.Add(user.TerritoryManager);
        }

        if (territory.TerritoryManagerTerritories.Any(assignment => assignment.UserId == user.Id))
            return;

        territory.TerritoryManagerTerritories.Add(new TerritoryManagerTerritory
        {
            UserId = user.Id,
            TerritoryId = territory.Id,
            Territory = territory,
            CreatedAt = now
        });
    }

    private async Task RemoveTerritoryManagerRoleIfUnassignedAsync(
        int userId,
        int removedTerritoryId,
        CancellationToken cancellationToken)
    {
        var hasOtherAssignments = await _db.TerritoryManagerTerritories
            .AsNoTracking()
            .AnyAsync(assignment =>
                assignment.UserId == userId &&
                assignment.TerritoryId != removedTerritoryId,
                cancellationToken);

        if (hasOtherAssignments)
            return;

        var territoryManagerRole = await _db.UserRoles
            .Include(userRole => userRole.Role)
            .Where(userRole =>
                userRole.UserId == userId &&
                userRole.Role.Name == RoleNames.TerritoryManager)
            .SingleOrDefaultAsync(cancellationToken);

        if (territoryManagerRole is not null)
        {
            _db.UserRoles.Remove(territoryManagerRole);
        }
    }

    private async Task EnsureRoleAssignmentAsync(User user, string roleName, DateTime now, CancellationToken cancellationToken)
    {
        if (user.UserRoles.Any(userRole => RoleNames.Normalize(userRole.Role.Name) == roleName))
            return;

        var role = await _db.Roles
            .SingleOrDefaultAsync(item => item.Name == roleName, cancellationToken);

        if (role is null)
        {
            role = new Role
            {
                Name = roleName,
                Description = roleName,
                CreatedAt = now
            };
            _db.Roles.Add(role);
        }

        user.UserRoles.Add(new UserRole
        {
            UserId = user.Id,
            Role = role
        });
    }

    private async Task<List<TerritoryMapRow>> LoadMapRowsWithDisplayAndStatusAsync(
        CancellationToken cancellationToken)
    {
        return await _db.Territories
            .AsNoTracking()
            .Select(territory => new TerritoryMapRow
            {
                TerritoryId = territory.Id,
                Name = territory.Name,
                TerritoryKind = territory.TerritoryKind,
                CountryCode = territory.CountryCode,
                CountryIso3 = territory.CountryIso3,
                BoundarySource = territory.BoundarySource,
                BoundarySourceUrl = territory.BoundarySourceUrl,
                BoundaryShapeType = territory.BoundaryShapeType,
                BoundaryShapeGroup = territory.BoundaryShapeGroup,
                BoundaryShapeId = territory.BoundaryShapeId,
                BoundaryExternalKey = territory.BoundaryExternalKey,
                BoundaryAdmLevel = territory.BoundaryAdmLevel,
                BoundaryReleaseType = territory.BoundaryReleaseType,
                BoundaryLicense = territory.BoundaryLicense,
                BoundaryImportedAt = territory.BoundaryImportedAt,
                HasValidationBoundary = EF.Property<Geometry?>(territory, BoundaryGeometryPropertyName) != null,
                HasDisplayBoundary = EF.Property<Geometry?>(territory, BoundaryGeometryPropertyName) != null &&
                    EF.Property<Geometry?>(territory, BoundaryDisplayGeometryPropertyName) != null &&
                    EF.Property<Geometry?>(territory, BoundaryDisplayGeometryPropertyName)!.IsValid &&
                    (EF.Property<Geometry?>(territory, BoundaryGeometryPropertyName)!.Area <= 0d ||
                     EF.Property<Geometry?>(territory, BoundaryDisplayGeometryPropertyName)!.Area >=
                     EF.Property<Geometry?>(territory, BoundaryGeometryPropertyName)!.Area * MinimumDisplayAreaRetentionForMap),
                BoundaryDisplayGeometry = EF.Property<Geometry?>(territory, BoundaryDisplayGeometryPropertyName),
                CreatedAt = territory.CreatedAt,
                UpdatedAt = territory.UpdatedAt,
                Manager = territory.TerritoryManagerTerritories
                    .Where(assignment => assignment.TerritoryManager.User.UserRoles.Any(userRole =>
                        userRole.Role.Name == RoleNames.TerritoryManager))
                    .OrderBy(assignment => assignment.CreatedAt)
                    .Select(assignment => new TerritoryManagerDetailRow
                    {
                        UserId = assignment.UserId,
                        DisplayName = assignment.TerritoryManager.User.DisplayName,
                        FirstName = assignment.TerritoryManager.User.FirstName,
                        LastName = assignment.TerritoryManager.User.LastName,
                        Email = assignment.TerritoryManager.User.Email,
                        AssignedAt = assignment.CreatedAt
                    })
                    .FirstOrDefault()
            })
            .ToListAsync(cancellationToken);
    }

    private async Task<List<TerritoryMapRow>> LoadMapBoundaryRowsAsync(
        CancellationToken cancellationToken)
    {
        return await _db.Territories
            .AsNoTracking()
            .Select(territory => new TerritoryMapRow
            {
                TerritoryId = territory.Id,
                Name = territory.Name,
                TerritoryKind = territory.TerritoryKind,
                CountryCode = territory.CountryCode,
                CountryIso3 = territory.CountryIso3,
                BoundaryShapeType = territory.BoundaryShapeType,
                BoundaryShapeGroup = territory.BoundaryShapeGroup,
                BoundaryShapeId = territory.BoundaryShapeId,
                BoundaryExternalKey = territory.BoundaryExternalKey,
                BoundaryImportedAt = territory.BoundaryImportedAt,
                HasValidationBoundary = EF.Property<Geometry?>(territory, BoundaryGeometryPropertyName) != null,
                HasDisplayBoundary = EF.Property<Geometry?>(territory, BoundaryGeometryPropertyName) != null &&
                    EF.Property<Geometry?>(territory, BoundaryDisplayGeometryPropertyName) != null &&
                    EF.Property<Geometry?>(territory, BoundaryDisplayGeometryPropertyName)!.IsValid &&
                    (EF.Property<Geometry?>(territory, BoundaryGeometryPropertyName)!.Area <= 0d ||
                     EF.Property<Geometry?>(territory, BoundaryDisplayGeometryPropertyName)!.Area >=
                     EF.Property<Geometry?>(territory, BoundaryGeometryPropertyName)!.Area * MinimumDisplayAreaRetentionForMap),
                BoundaryDisplayGeometry = EF.Property<Geometry?>(territory, BoundaryDisplayGeometryPropertyName),
                CreatedAt = territory.CreatedAt,
                UpdatedAt = territory.UpdatedAt
            })
            .ToListAsync(cancellationToken);
    }

    private async Task<List<TerritoryMapRow>> LoadMapStatusRowsAsync(
        CancellationToken cancellationToken)
    {
        return await _db.Territories
            .AsNoTracking()
            .Select(territory => new TerritoryMapRow
            {
                TerritoryId = territory.Id,
                Name = territory.Name,
                TerritoryKind = territory.TerritoryKind,
                CountryCode = territory.CountryCode,
                CountryIso3 = territory.CountryIso3,
                BoundaryShapeType = territory.BoundaryShapeType,
                BoundaryShapeGroup = territory.BoundaryShapeGroup,
                BoundaryShapeId = territory.BoundaryShapeId,
                BoundaryExternalKey = territory.BoundaryExternalKey,
                BoundaryAdmLevel = territory.BoundaryAdmLevel,
                BoundaryImportedAt = territory.BoundaryImportedAt,
                HasValidationBoundary = EF.Property<Geometry?>(territory, BoundaryGeometryPropertyName) != null,
                HasDisplayBoundary = EF.Property<Geometry?>(territory, BoundaryGeometryPropertyName) != null &&
                    EF.Property<Geometry?>(territory, BoundaryDisplayGeometryPropertyName) != null &&
                    EF.Property<Geometry?>(territory, BoundaryDisplayGeometryPropertyName)!.IsValid &&
                    (EF.Property<Geometry?>(territory, BoundaryGeometryPropertyName)!.Area <= 0d ||
                     EF.Property<Geometry?>(territory, BoundaryDisplayGeometryPropertyName)!.Area >=
                     EF.Property<Geometry?>(territory, BoundaryGeometryPropertyName)!.Area * MinimumDisplayAreaRetentionForMap),
                CreatedAt = territory.CreatedAt,
                UpdatedAt = territory.UpdatedAt,
                Manager = territory.TerritoryManagerTerritories
                    .Where(assignment => assignment.TerritoryManager.User.UserRoles.Any(userRole =>
                        userRole.Role.Name == RoleNames.TerritoryManager))
                    .OrderBy(assignment => assignment.CreatedAt)
                    .Select(assignment => new TerritoryManagerDetailRow
                    {
                        UserId = assignment.UserId,
                        DisplayName = assignment.TerritoryManager.User.DisplayName,
                        FirstName = assignment.TerritoryManager.User.FirstName,
                        LastName = assignment.TerritoryManager.User.LastName,
                        Email = assignment.TerritoryManager.User.Email,
                        AssignedAt = assignment.CreatedAt
                    })
                    .FirstOrDefault()
            })
            .ToListAsync(cancellationToken);
    }

    private static IQueryable<Territory> ApplySort(IQueryable<Territory> query, string? sortField, string? sortDir)
    {
        var desc = string.Equals(sortDir, "desc", StringComparison.OrdinalIgnoreCase);
        var field = (sortField ?? "createdAt").Trim().ToLowerInvariant();

        return field switch
        {
            "name" => desc
                ? query.OrderByDescending(item => item.Name).ThenByDescending(item => item.Id)
                : query.OrderBy(item => item.Name).ThenBy(item => item.Id),
            "status" => desc
                ? query.OrderBy(item => item.TerritoryManagerTerritories.Any()).ThenBy(item => item.Name).ThenBy(item => item.Id)
                : query.OrderByDescending(item => item.TerritoryManagerTerritories.Any()).ThenBy(item => item.Name).ThenBy(item => item.Id),
            "countries" => desc
                ? query.OrderByDescending(item => item.CountryCode ?? item.Name).ThenByDescending(item => item.Id)
                : query.OrderBy(item => item.CountryCode ?? item.Name).ThenBy(item => item.Id),
            _ => desc
                ? query.OrderByDescending(item => item.CreatedAt).ThenByDescending(item => item.Id)
                : query.OrderBy(item => item.CreatedAt).ThenBy(item => item.Id)
        };
    }

    private static AdminTerritoryDto MapTerritory(TerritoryDirectoryRow row)
    {
        var manager = row.Manager;
        var territoryKind = NormalizeTerritoryKind(row.TerritoryKind);
        var (countryCode, iso3) = ResolveCountryCodes(row.CountryCode, row.CountryIso3, row.Name, territoryKind);
        var isDisputed = IsDisputedKind(territoryKind);

        return new AdminTerritoryDto
        {
            TerritoryId = row.TerritoryId,
            CountryId = row.TerritoryId,
            Id = BuildInternalReference(row.TerritoryId),
            Name = row.Name,
            DisplayName = row.Name,
            InternalReference = BuildInternalReference(row.TerritoryId),
            TerritoryKind = territoryKind,
            CountryCode = countryCode,
            Iso2 = countryCode,
            Iso3 = iso3,
            Countries = BuildCountryCodeList(countryCode, iso3),
            AssignedManagerUserId = manager?.UserId,
            AssignedManagerId = manager?.UserId.ToString(CultureInfo.InvariantCulture),
            AssignedManagerName = manager is null ? null : BuildDisplayName(manager.DisplayName, manager.FirstName, manager.LastName, manager.Email),
            AssignedManagerEmail = manager?.Email,
            Status = isDisputed
                ? manager is null ? "DisputedViewOnly" : "DisputedAssigned"
                : manager is null ? "Unassigned" : "Assigned",
            BoundarySource = row.BoundarySource,
            BoundarySourceUrl = row.BoundarySourceUrl,
            BoundaryShapeType = row.BoundaryShapeType,
            BoundaryShapeGroup = row.BoundaryShapeGroup,
            BoundaryShapeId = row.BoundaryShapeId,
            BoundaryExternalKey = row.BoundaryExternalKey,
            BoundaryAdmLevel = row.BoundaryAdmLevel,
            BoundaryReleaseType = row.BoundaryReleaseType,
            BoundaryLicense = row.BoundaryLicense,
            BoundaryImportedAt = row.BoundaryImportedAt,
            HasBoundary = row.HasBoundary,
            CreatedAt = row.CreatedAt,
            UpdatedAt = row.UpdatedAt ?? row.CreatedAt
        };
    }

    private static AdminTerritoryDetailDto MapTerritoryDetail(TerritoryDetailRow row)
    {
        var manager = row.Manager;
        var territoryKind = NormalizeTerritoryKind(row.TerritoryKind);
        var (countryCode, iso3) = ResolveCountryCodes(row.CountryCode, row.CountryIso3, row.Name, territoryKind);
        var isDisputed = IsDisputedKind(territoryKind);
        var boundaryGeoJson = ToGeoJson(row.BoundaryGeometry, FullGeometryCoordinateDecimals);
        var internalReference = BuildInternalReference(row.TerritoryId);

        return new AdminTerritoryDetailDto
        {
            TerritoryId = row.TerritoryId,
            CountryId = row.TerritoryId,
            Id = internalReference,
            Name = row.Name,
            DisplayName = row.Name,
            InternalReference = internalReference,
            TerritoryKind = territoryKind,
            CountryCode = countryCode,
            Iso2 = countryCode,
            Iso3 = iso3,
            Countries = BuildCountryCodeList(countryCode, iso3),
            AssignedManagerUserId = manager?.UserId,
            AssignedManagerId = manager?.UserId.ToString(CultureInfo.InvariantCulture),
            AssignedManagerName = manager is null ? null : BuildDisplayName(manager.DisplayName, manager.FirstName, manager.LastName, manager.Email),
            AssignedManagerEmail = manager?.Email,
            AssignedManager = manager is null ? null : MapAssignedManager(manager),
            Status = isDisputed
                ? manager is null ? "DisputedViewOnly" : "DisputedAssigned"
                : manager is null ? "Unassigned" : "Assigned",
            BoundarySource = row.BoundarySource,
            BoundarySourceUrl = row.BoundarySourceUrl,
            BoundaryShapeType = row.BoundaryShapeType,
            BoundaryShapeGroup = row.BoundaryShapeGroup,
            BoundaryShapeId = row.BoundaryShapeId,
            BoundaryExternalKey = row.BoundaryExternalKey,
            BoundaryAdmLevel = row.BoundaryAdmLevel,
            BoundaryReleaseType = row.BoundaryReleaseType,
            BoundaryLicense = row.BoundaryLicense,
            BoundaryImportedAt = row.BoundaryImportedAt,
            HasBoundary = boundaryGeoJson is not null,
            BoundaryGeoJson = boundaryGeoJson,
            Viewport = BuildMapViewport(boundaryGeoJson),
            CanAssign = !isDisputed && boundaryGeoJson is not null,
            BoundaryRequiredMessage = isDisputed
                ? DisputedViewOnlyMessage
                : boundaryGeoJson is null ? MissingBoundaryAssignmentMessage : null,
            CreatedAt = row.CreatedAt,
            UpdatedAt = row.UpdatedAt ?? row.CreatedAt
        };
    }

    private static List<AdminTerritoryMapFeatureDto> BuildLegacyMapFeatures(
        IReadOnlyCollection<TerritoryMapRow> rows)
    {
        var rowsByIso3 = rows
            .Where(row => IsCountryKind(row.TerritoryKind) && !string.IsNullOrWhiteSpace(row.CountryIso3))
            .GroupBy(row => row.CountryIso3!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        var rowsByIso2 = rows
            .Where(row => IsCountryKind(row.TerritoryKind) && !string.IsNullOrWhiteSpace(row.CountryCode))
            .GroupBy(row => row.CountryCode!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        var usedTerritoryIds = new HashSet<int>();
        var features = new List<AdminTerritoryMapFeatureDto>();

        foreach (var country in CountryRegistry.Countries)
        {
            rowsByIso3.TryGetValue(country.Iso3, out var row);
            row ??= rowsByIso2.GetValueOrDefault(country.Iso2);
            if (row is not null)
            {
                usedTerritoryIds.Add(row.TerritoryId);
            }

            features.Add(MapMapFeature(row, country));
        }

        features.AddRange(rows
            .Where(row => !usedTerritoryIds.Contains(row.TerritoryId))
            .Select(row => MapMapFeature(row, null)));

        return features;
    }

    private static List<AdminTerritoryMapBoundaryFeatureDto> BuildBoundaryMapFeatures(
        IReadOnlyCollection<TerritoryMapRow> rows)
    {
        var rowsByIso3 = rows
            .Where(row => IsCountryKind(row.TerritoryKind) && !string.IsNullOrWhiteSpace(row.CountryIso3))
            .GroupBy(row => row.CountryIso3!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        var rowsByIso2 = rows
            .Where(row => IsCountryKind(row.TerritoryKind) && !string.IsNullOrWhiteSpace(row.CountryCode))
            .GroupBy(row => row.CountryCode!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        var usedTerritoryIds = new HashSet<int>();
        var features = new List<AdminTerritoryMapBoundaryFeatureDto>();

        foreach (var country in CountryRegistry.Countries)
        {
            rowsByIso3.TryGetValue(country.Iso3, out var row);
            row ??= rowsByIso2.GetValueOrDefault(country.Iso2);
            if (row is not null)
            {
                usedTerritoryIds.Add(row.TerritoryId);
            }

            features.Add(MapBoundaryFeature(row, country));
        }

        features.AddRange(rows
            .Where(row => !usedTerritoryIds.Contains(row.TerritoryId))
            .Select(row => MapBoundaryFeature(row, null)));

        return features;
    }

    private static List<AdminTerritoryMapStatusDto> BuildMapStatusItems(
        IReadOnlyCollection<TerritoryMapRow> rows)
    {
        var rowsByIso3 = rows
            .Where(row => IsCountryKind(row.TerritoryKind) && !string.IsNullOrWhiteSpace(row.CountryIso3))
            .GroupBy(row => row.CountryIso3!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        var rowsByIso2 = rows
            .Where(row => IsCountryKind(row.TerritoryKind) && !string.IsNullOrWhiteSpace(row.CountryCode))
            .GroupBy(row => row.CountryCode!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        var usedTerritoryIds = new HashSet<int>();
        var items = new List<AdminTerritoryMapStatusDto>();

        foreach (var country in CountryRegistry.Countries)
        {
            rowsByIso3.TryGetValue(country.Iso3, out var row);
            row ??= rowsByIso2.GetValueOrDefault(country.Iso2);
            if (row is not null)
            {
                usedTerritoryIds.Add(row.TerritoryId);
            }

            items.Add(MapStatusItem(row, country));
        }

        items.AddRange(rows
            .Where(row => !usedTerritoryIds.Contains(row.TerritoryId))
            .Select(row => MapStatusItem(row, null)));

        return items;
    }

    private static AdminTerritoryMapBoundaryFeatureDto MapBoundaryFeature(
        TerritoryMapRow? row,
        CountryRegistryItem? registryCountry)
    {
        var territoryKind = NormalizeTerritoryKind(row?.TerritoryKind);
        var isDisputed = IsDisputedKind(territoryKind);
        (string? iso2, string? iso3) = isDisputed
            ? (null, null)
            : ResolveCountryCodes(
                row?.CountryCode ?? registryCountry?.Iso2,
                row?.CountryIso3 ?? registryCountry?.Iso3,
                row?.Name ?? registryCountry?.Name,
                territoryKind);
        var displayName = row?.Name ?? registryCountry?.Name ?? iso3 ?? iso2 ?? "Unknown country";
        var hasDisplayBoundary = row?.HasDisplayBoundary == true &&
            row.BoundaryDisplayGeometry is not null &&
            TerritoryBoundaryDisplayGeometry.IsValid(row.BoundaryDisplayGeometry);
        var geometry = hasDisplayBoundary
            ? ToGeoJson(row?.BoundaryDisplayGeometry, DisplayGeometryCoordinateDecimals)
            : null;

        return new AdminTerritoryMapBoundaryFeatureDto
        {
            Geometry = geometry,
            Properties = new AdminTerritoryMapBoundaryPropertiesDto
            {
                TerritoryId = row?.TerritoryId,
                CountryId = row?.TerritoryId,
                TerritoryKind = territoryKind,
                Iso2 = iso2,
                Iso3 = iso3,
                CountryCode = iso2,
                Name = displayName,
                DisplayName = displayName,
                ShapeGroup = row?.BoundaryShapeGroup,
                ShapeType = ResolveSourceShapeType(row, territoryKind),
                ShapeId = row?.BoundaryShapeId,
                HasDisplayBoundary = geometry is not null,
                Viewport = BuildMapViewport(geometry)
            }
        };
    }

    private static AdminTerritoryMapStatusDto MapStatusItem(
        TerritoryMapRow? row,
        CountryRegistryItem? registryCountry)
    {
        var territoryKind = NormalizeTerritoryKind(row?.TerritoryKind);
        var isDisputed = IsDisputedKind(territoryKind);
        (string? iso2, string? iso3) = isDisputed
            ? (null, null)
            : ResolveCountryCodes(
                row?.CountryCode ?? registryCountry?.Iso2,
                row?.CountryIso3 ?? registryCountry?.Iso3,
                row?.Name ?? registryCountry?.Name,
                territoryKind);
        var displayName = row?.Name ?? registryCountry?.Name ?? iso3 ?? iso2 ?? "Unknown country";
        var hasValidationBoundary = row?.HasValidationBoundary == true;
        var hasDisplayBoundary = row?.HasDisplayBoundary == true;
        var assignedManager = row?.Manager is null ? null : MapAssignedManagerSummary(row.Manager);
        var status = ResolveMapStatus(isDisputed, hasValidationBoundary, hasDisplayBoundary, assignedManager);
        var canAssign = !isDisputed && hasValidationBoundary && assignedManager is null && row is not null;
        var boundaryRequiredMessage = ResolveBoundaryRequiredMessage(
            row,
            isDisputed,
            hasValidationBoundary,
            hasDisplayBoundary);

        return new AdminTerritoryMapStatusDto
        {
            TerritoryId = row?.TerritoryId,
            CountryId = row?.TerritoryId,
            TerritoryKind = territoryKind,
            Iso2 = iso2,
            Iso3 = iso3,
            CountryCode = iso2,
            Name = displayName,
            DisplayName = displayName,
            Status = status,
            ShapeGroup = row?.BoundaryShapeGroup,
            ShapeType = ResolveSourceShapeType(row, territoryKind),
            ShapeId = row?.BoundaryShapeId,
            AssignedManager = assignedManager,
            HasBoundary = hasValidationBoundary,
            HasValidationBoundary = hasValidationBoundary,
            HasDisplayBoundary = hasDisplayBoundary,
            CanAssign = canAssign,
            CanEdit = row is not null && (isDisputed || assignedManager is not null),
            BoundaryRequiredMessage = boundaryRequiredMessage,
            BoundaryMissingReason = boundaryRequiredMessage
        };
    }

    private static AdminTerritoryMapFeatureDto MapMapFeature(TerritoryMapRow? row, CountryRegistryItem? registryCountry)
    {
        var territoryKind = NormalizeTerritoryKind(row?.TerritoryKind);
        var isDisputed = IsDisputedKind(territoryKind);
        (string? iso2, string? iso3) = isDisputed
            ? (null, null)
            : ResolveCountryCodes(
                row?.CountryCode ?? registryCountry?.Iso2,
                row?.CountryIso3 ?? registryCountry?.Iso3,
                row?.Name ?? registryCountry?.Name,
                territoryKind);
        var displayName = row?.Name ?? registryCountry?.Name ?? iso3 ?? iso2 ?? "Unknown country";
        var hasValidationBoundary = row?.HasValidationBoundary == true;
        var hasUsableDisplayGeometry = row?.HasDisplayBoundary == true &&
            row.BoundaryDisplayGeometry is not null &&
            TerritoryBoundaryDisplayGeometry.IsValid(row.BoundaryDisplayGeometry);
        var geometry = hasUsableDisplayGeometry
            ? ToGeoJson(row?.BoundaryDisplayGeometry, DisplayGeometryCoordinateDecimals)
            : null;
        var assignedManager = row?.Manager is null ? null : MapAssignedManagerSummary(row.Manager);
        var hasDisplayBoundary = geometry is not null;
        var status = ResolveMapStatus(isDisputed, hasValidationBoundary, hasDisplayBoundary, assignedManager);
        var canAssign = !isDisputed && hasValidationBoundary && assignedManager is null && row is not null;
        var boundaryRequiredMessage = ResolveBoundaryRequiredMessage(
            row,
            isDisputed,
            hasValidationBoundary,
            hasDisplayBoundary);

        return new AdminTerritoryMapFeatureDto
        {
            Geometry = geometry,
            Properties = new AdminTerritoryMapFeaturePropertiesDto
            {
                TerritoryId = row?.TerritoryId,
                CountryId = row?.TerritoryId,
                TerritoryKind = territoryKind,
                Iso2 = iso2,
                Iso3 = iso3,
                CountryCode = iso2,
                Name = displayName,
                DisplayName = displayName,
                Status = status,
                ShapeGroup = row?.BoundaryShapeGroup,
                ShapeType = ResolveSourceShapeType(row, territoryKind),
                ShapeId = row?.BoundaryShapeId,
                AssignedManager = assignedManager,
                HasBoundary = hasValidationBoundary,
                HasValidationBoundary = hasValidationBoundary,
                HasDisplayBoundary = hasDisplayBoundary,
                CanAssign = canAssign,
                CanEdit = row is not null && (isDisputed || assignedManager is not null),
                BoundaryRequiredMessage = boundaryRequiredMessage,
                BoundaryMissingReason = boundaryRequiredMessage,
                BoundarySource = row?.BoundarySource,
                BoundarySourceUrl = row?.BoundarySourceUrl,
                BoundaryShapeType = row?.BoundaryShapeType,
                BoundaryShapeGroup = row?.BoundaryShapeGroup,
                BoundaryShapeId = row?.BoundaryShapeId,
                BoundaryExternalKey = row?.BoundaryExternalKey,
                BoundaryAdmLevel = row?.BoundaryAdmLevel,
                BoundaryReleaseType = row?.BoundaryReleaseType,
                BoundaryLicense = row?.BoundaryLicense,
                BoundaryImportedAt = row?.BoundaryImportedAt,
                Viewport = BuildMapViewport(geometry),
                CreatedAt = row?.CreatedAt,
                UpdatedAt = row?.UpdatedAt
            }
        };
    }

    private static string ResolveMapStatus(
        bool isDisputed,
        bool hasValidationBoundary,
        bool hasDisplayBoundary,
        AdminTerritoryMapAssignedManagerDto? assignedManager)
    {
        if (isDisputed)
            return hasDisplayBoundary
                ? assignedManager is null ? "DisputedViewOnly" : "DisputedAssigned"
                : hasValidationBoundary ? "MissingDisplayGeometry" : "MissingBoundary";

        if (!hasValidationBoundary)
            return "MissingBoundary";

        if (!hasDisplayBoundary)
            return "MissingDisplayGeometry";

        return assignedManager is null ? "Available" : "Assigned";
    }

    private static string? ResolveBoundaryRequiredMessage(
        TerritoryMapRow? row,
        bool isDisputed,
        bool hasValidationBoundary,
        bool hasDisplayBoundary)
    {
        if (isDisputed)
            return hasDisplayBoundary
                ? DisputedViewOnlyMessage
                : hasValidationBoundary
                    ? MissingDisplayBoundaryMessage
                    : "This disputed boundary cannot be displayed because its geometry is missing.";

        if (hasDisplayBoundary)
            return null;

        return hasValidationBoundary || row?.HasValidationBoundary == true
            ? MissingDisplayBoundaryMessage
            : MissingBoundaryAssignmentMessage;
    }

    private static string? ResolveSourceShapeType(TerritoryMapRow? row, string territoryKind)
    {
        if (row is null)
            return null;

        if (IsDisputedKind(territoryKind))
            return row.BoundaryShapeType ?? row.BoundaryAdmLevel ?? DisputedAdmLevel;

        return row.BoundaryAdmLevel ?? row.BoundaryShapeType;
    }

    private static AdminTerritoryMapAssignedManagerDto MapAssignedManagerSummary(TerritoryManagerDetailRow manager)
    {
        var displayName = BuildDisplayName(manager.DisplayName, manager.FirstName, manager.LastName, manager.Email);

        return new AdminTerritoryMapAssignedManagerDto
        {
            UserId = manager.UserId,
            Id = manager.UserId.ToString(CultureInfo.InvariantCulture),
            DisplayName = displayName,
            Name = displayName,
            Email = manager.Email,
            Initials = BuildInitials(displayName, manager.Email)
        };
    }

    private static DateTime BuildMapLastModified(IEnumerable<TerritoryMapRow> rows)
    {
        var latest = rows
            .SelectMany(row => new[]
            {
                row.UpdatedAt,
                row.BoundaryImportedAt,
                row.Manager?.AssignedAt,
                row.CreatedAt
            })
            .Where(value => value.HasValue)
            .Select(value => value!.Value)
            .DefaultIfEmpty(DateTime.UnixEpoch)
            .Max();

        return latest.Kind == DateTimeKind.Utc
            ? latest
            : DateTime.SpecifyKind(latest, DateTimeKind.Utc);
    }

    private static DateTime BuildMapBoundaryLastModified(IEnumerable<TerritoryMapRow> rows)
    {
        var latest = rows
            .SelectMany(row => new[]
            {
                row.BoundaryImportedAt,
                row.CreatedAt
            })
            .Where(value => value.HasValue)
            .Select(value => value!.Value)
            .DefaultIfEmpty(DateTime.UnixEpoch)
            .Max();

        return latest.Kind == DateTimeKind.Utc
            ? latest
            : DateTime.SpecifyKind(latest, DateTimeKind.Utc);
    }

    private static string BuildMapETag(IEnumerable<TerritoryMapRow> rows, int featureCount)
    {
        var builder = new StringBuilder();
        builder
            .Append(MapSerializationVersion)
            .Append(':')
            .Append(featureCount.ToString(CultureInfo.InvariantCulture));

        foreach (var row in rows.OrderBy(item => item.TerritoryId))
        {
            builder
                .Append('|')
                .Append(row.TerritoryId.ToString(CultureInfo.InvariantCulture))
                .Append(':')
                .Append(row.TerritoryKind)
                .Append(':')
                .Append(row.BoundaryExternalKey ?? string.Empty)
                .Append(':')
                .Append(row.UpdatedAt?.Ticks.ToString(CultureInfo.InvariantCulture) ?? "0")
                .Append(':')
                .Append(row.BoundaryImportedAt?.Ticks.ToString(CultureInfo.InvariantCulture) ?? "0")
                .Append(':')
                .Append(row.Manager?.UserId.ToString(CultureInfo.InvariantCulture) ?? "0")
                .Append(':')
                .Append(row.Manager?.AssignedAt.Ticks.ToString(CultureInfo.InvariantCulture) ?? "0")
                .Append(':')
                .Append(TerritoryBoundaryDisplayGeometry.CountCoordinates(row.BoundaryDisplayGeometry)
                    .ToString(CultureInfo.InvariantCulture));
        }

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString()));
        return $"\"{Convert.ToHexString(hash).ToLowerInvariant()}\"";
    }

    private static string BuildMapBoundariesETag(IEnumerable<TerritoryMapRow> rows, int featureCount)
    {
        var builder = new StringBuilder();
        builder
            .Append(MapBoundariesSerializationVersion)
            .Append(':')
            .Append(featureCount.ToString(CultureInfo.InvariantCulture));

        foreach (var row in rows.OrderBy(item => item.TerritoryId))
        {
            builder
                .Append('|')
                .Append(row.TerritoryId.ToString(CultureInfo.InvariantCulture))
                .Append(':')
                .Append(row.TerritoryKind)
                .Append(':')
                .Append(row.CountryCode ?? string.Empty)
                .Append(':')
                .Append(row.CountryIso3 ?? string.Empty)
                .Append(':')
                .Append(row.BoundaryExternalKey ?? string.Empty)
                .Append(':')
                .Append(row.BoundaryShapeId ?? string.Empty)
                .Append(':')
                .Append(row.BoundaryImportedAt?.Ticks.ToString(CultureInfo.InvariantCulture) ?? "0")
                .Append(':')
                .Append(TerritoryBoundaryDisplayGeometry.CountCoordinates(row.BoundaryDisplayGeometry)
                    .ToString(CultureInfo.InvariantCulture));
        }

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString()));
        return $"\"{Convert.ToHexString(hash).ToLowerInvariant()}\"";
    }

    private static string BuildMapStatusETag(IEnumerable<TerritoryMapRow> rows, int itemCount)
    {
        var builder = new StringBuilder();
        builder
            .Append(MapStatusSerializationVersion)
            .Append(':')
            .Append(itemCount.ToString(CultureInfo.InvariantCulture));

        foreach (var row in rows.OrderBy(item => item.TerritoryId))
        {
            builder
                .Append('|')
                .Append(row.TerritoryId.ToString(CultureInfo.InvariantCulture))
                .Append(':')
                .Append(row.TerritoryKind)
                .Append(':')
                .Append(row.UpdatedAt?.Ticks.ToString(CultureInfo.InvariantCulture) ?? "0")
                .Append(':')
                .Append(row.BoundaryImportedAt?.Ticks.ToString(CultureInfo.InvariantCulture) ?? "0")
                .Append(':')
                .Append(row.HasValidationBoundary ? "1" : "0")
                .Append(':')
                .Append(row.HasDisplayBoundary ? "1" : "0")
                .Append(':')
                .Append(row.Manager?.UserId.ToString(CultureInfo.InvariantCulture) ?? "0")
                .Append(':')
                .Append(row.Manager?.AssignedAt.Ticks.ToString(CultureInfo.InvariantCulture) ?? "0");
        }

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString()));
        return $"\"{Convert.ToHexString(hash).ToLowerInvariant()}\"";
    }

    private static AdminTerritoryAssignedManagerDto MapAssignedManager(TerritoryManagerDetailRow manager)
    {
        var displayName = BuildDisplayName(manager.DisplayName, manager.FirstName, manager.LastName, manager.Email);

        return new AdminTerritoryAssignedManagerDto
        {
            UserId = manager.UserId,
            Id = manager.UserId.ToString(CultureInfo.InvariantCulture),
            DisplayName = displayName,
            Name = displayName,
            Email = manager.Email,
            AvatarUrl = manager.AvatarUrl,
            Initials = BuildInitials(displayName, manager.Email),
            Roles = manager.Roles
                .Select(RoleNames.Normalize)
                .Where(role => !string.IsNullOrWhiteSpace(role))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(role => role)
                .ToList(),
            AssignedAt = manager.AssignedAt
        };
    }

    private static AdminTerritoryManagerCandidateDto MapCandidate(User user, int? countryTerritoryId)
    {
        var assignedTerritories = user.TerritoryManager?.TerritoryManagerTerritories
            .OrderBy(assignment => assignment.Territory.Name)
            .Select(assignment => new AdminTerritoryManagerAssignedTerritoryDto
            {
                TerritoryId = assignment.TerritoryId,
                TerritoryName = assignment.Territory.Name,
                DisplayName = assignment.Territory.Name,
                CountryCode = NormalizeCountryCode(assignment.Territory.CountryCode) ?? TryResolveCountryCode(assignment.Territory.Name)
            })
            .ToList()
            ?? new List<AdminTerritoryManagerAssignedTerritoryDto>();

        var displayName = BuildDisplayName(user.DisplayName, user.FirstName, user.LastName, user.Email);
        var roles = user.UserRoles
            .Select(userRole => RoleNames.Normalize(userRole.Role.Name))
            .Where(role => !string.IsNullOrWhiteSpace(role))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(role => role)
            .ToList();

        var conflicting = assignedTerritories.FirstOrDefault(assignment =>
            !countryTerritoryId.HasValue || assignment.TerritoryId != countryTerritoryId.Value);
        var adminRoleConflict = roles.Contains(RoleNames.Admin, StringComparer.Ordinal);
        var isActive = !user.IsDeleted && !user.IsSuspended;

        return new AdminTerritoryManagerCandidateDto
        {
            UserId = user.Id,
            Id = user.Id.ToString(CultureInfo.InvariantCulture),
            DisplayName = displayName,
            Name = displayName,
            FirstName = user.FirstName,
            LastName = user.LastName,
            Email = user.Email,
            AvatarUrl = user.AvatarUrl,
            Initials = BuildInitials(displayName, user.Email),
            Roles = roles,
            AssignedTerritories = assignedTerritories,
            Status = isActive ? "Active" : "Inactive",
            IsActive = isActive,
            CanAssign = isActive && conflicting is null && !adminRoleConflict,
            IneligibilityReason = adminRoleConflict
                ? "Administrators cannot be assigned as Territory Managers."
                : !isActive
                    ? "Inactive users cannot be assigned as Territory Managers."
                    : conflicting is null
                        ? null
                        : $"Already assigned as Territory Manager for {conflicting.DisplayName}."
        };
    }

    private static Geometry BuildCountryBoundary(AdminCountryBoundaryDto? boundary)
    {
        if (boundary is null)
            throw new InvalidOperationException("Country boundary geometry is required for territory assignment.");

        if (boundary.Coordinates.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
            throw new InvalidOperationException("Country boundary geometry is required for territory assignment.");

        var type = boundary.Type?.Trim();
        Geometry geometry = type switch
        {
            "Polygon" => BuildPolygon(boundary.Coordinates),
            "MultiPolygon" => BuildMultiPolygon(boundary.Coordinates),
            _ => throw new InvalidOperationException("Country boundary geometry must be a Polygon or MultiPolygon.")
        };

        if (geometry.IsEmpty || !geometry.IsValid)
            throw new InvalidOperationException("Country boundary geometry is invalid.");

        geometry.SRID = 4326;
        return geometry;
    }

    private static AdminTerritoryGeometryDto? ToGeoJson(Geometry? geometry, int coordinateDecimals)
    {
        var polygonalGeometry = ToPolygonalGeometry(geometry);

        if (polygonalGeometry is null)
            return null;

        var geoJson = polygonalGeometry switch
        {
            Polygon polygon => new AdminTerritoryGeometryDto
            {
                Type = "Polygon",
                Coordinates = ToPolygonCoordinates(polygon, coordinateDecimals)
            },
            MultiPolygon multiPolygon => new AdminTerritoryGeometryDto
            {
                Type = "MultiPolygon",
                Coordinates = Enumerable.Range(0, multiPolygon.NumGeometries)
                    .Select(index => ToPolygonCoordinates(
                        (Polygon)multiPolygon.GetGeometryN(index),
                        coordinateDecimals))
                    .ToArray()
            },
            _ => null
        };

        return OrientGeoJsonRings(NormalizeLongitudesForDisplay(geoJson));
    }

    private static Geometry? ToPolygonalGeometry(Geometry? geometry)
    {
        if (geometry is null || geometry.IsEmpty)
            return null;

        var copy = geometry.Copy();
        if (copy.SRID != 4326 || !copy.IsValid)
            return null;

        return copy is Polygon or MultiPolygon ? copy : null;
    }

    private static bool IsValidAdm0Boundary(Geometry? geometry)
    {
        if (geometry is null || geometry.IsEmpty || !geometry.IsValid)
            return false;

        if (geometry is not Polygon and not MultiPolygon)
            return false;

        return geometry.SRID == 4326;
    }

    private static double[][][] ToPolygonCoordinates(Polygon polygon, int coordinateDecimals)
    {
        var rings = new List<double[][]>
        {
            ToRingCoordinates(polygon.ExteriorRing, coordinateDecimals)
        };

        for (var index = 0; index < polygon.NumInteriorRings; index++)
        {
            rings.Add(ToRingCoordinates(polygon.GetInteriorRingN(index), coordinateDecimals));
        }

        return rings.ToArray();
    }

    private static double[][] ToRingCoordinates(LineString ring, int coordinateDecimals) =>
        ring.Coordinates
            .Select(coordinate => new[]
            {
                RoundCoordinate(coordinate.X, coordinateDecimals),
                RoundCoordinate(coordinate.Y, coordinateDecimals)
            })
            .ToArray();

    private static AdminTerritoryGeometryDto? NormalizeLongitudesForDisplay(AdminTerritoryGeometryDto? geometry)
    {
        if (geometry?.Coordinates is null)
            return geometry;

        var longitudes = new List<double>();
        CollectLongitudes(geometry, longitudes);
        var longitudeFrame = DisplayLongitudeFrame.Create(longitudes);
        if (!longitudeFrame.ShouldUnwrap)
            return geometry;

        if (geometry.Type.Equals("Polygon", StringComparison.OrdinalIgnoreCase) &&
            geometry.Coordinates is double[][][] polygon)
        {
            return new AdminTerritoryGeometryDto
            {
                Type = geometry.Type,
                Coordinates = UnwrapPolygonLongitudes(polygon, longitudeFrame)
            };
        }

        if (geometry.Type.Equals("MultiPolygon", StringComparison.OrdinalIgnoreCase) &&
            geometry.Coordinates is double[][][][] multiPolygon)
        {
            return new AdminTerritoryGeometryDto
            {
                Type = geometry.Type,
                Coordinates = multiPolygon
                    .Select(polygonCoordinates => UnwrapPolygonLongitudes(polygonCoordinates, longitudeFrame))
                    .ToArray()
            };
        }

        return geometry;
    }

    private static AdminTerritoryGeometryDto? OrientGeoJsonRings(AdminTerritoryGeometryDto? geometry)
    {
        if (geometry?.Coordinates is null)
            return geometry;

        if (geometry.Type.Equals("Polygon", StringComparison.OrdinalIgnoreCase) &&
            geometry.Coordinates is double[][][] polygon)
        {
            return new AdminTerritoryGeometryDto
            {
                Type = geometry.Type,
                Coordinates = OrientPolygonCoordinates(polygon)
            };
        }

        if (geometry.Type.Equals("MultiPolygon", StringComparison.OrdinalIgnoreCase) &&
            geometry.Coordinates is double[][][][] multiPolygon)
        {
            return new AdminTerritoryGeometryDto
            {
                Type = geometry.Type,
                Coordinates = multiPolygon
                    .Select(OrientPolygonCoordinates)
                    .ToArray()
            };
        }

        return geometry;
    }

    private static double[][][] OrientPolygonCoordinates(double[][][] polygon)
    {
        if (polygon.Length == 0)
            return polygon;

        var oriented = new double[polygon.Length][][];
        oriented[0] = OrientRingCoordinates(polygon[0], clockwise: false);

        for (var index = 1; index < polygon.Length; index++)
        {
            oriented[index] = OrientRingCoordinates(polygon[index], clockwise: true);
        }

        return oriented;
    }

    private static double[][] OrientRingCoordinates(double[][] ring, bool clockwise)
    {
        if (ring.Length < 4)
            return ring;

        var signedArea = SignedRingArea(ring);
        if (Math.Abs(signedArea) <= LongitudeComparisonTolerance)
            return ring;

        var isClockwise = signedArea < 0d;
        return isClockwise == clockwise
            ? ring
            : ring.Reverse().Select(coordinate => coordinate.ToArray()).ToArray();
    }

    private static double SignedRingArea(double[][] ring)
    {
        var area = 0d;
        for (var index = 0; index < ring.Length - 1; index++)
        {
            var current = ring[index];
            var next = ring[index + 1];
            if (current.Length < 2 || next.Length < 2)
                continue;

            area += current[0] * next[1] - next[0] * current[1];
        }

        return area / 2d;
    }

    private static void CollectLongitudes(AdminTerritoryGeometryDto geometry, List<double> longitudes)
    {
        if (geometry.Type.Equals("Polygon", StringComparison.OrdinalIgnoreCase) &&
            geometry.Coordinates is double[][][] polygon)
        {
            CollectPolygonLongitudes(polygon, longitudes);
        }
        else if (geometry.Type.Equals("MultiPolygon", StringComparison.OrdinalIgnoreCase) &&
                 geometry.Coordinates is double[][][][] multiPolygon)
        {
            foreach (var polygonCoordinates in multiPolygon)
            {
                CollectPolygonLongitudes(polygonCoordinates, longitudes);
            }
        }
    }

    private static void CollectPolygonLongitudes(double[][][] polygon, List<double> longitudes)
    {
        foreach (var ring in polygon)
        {
            foreach (var coordinate in ring)
            {
                if (coordinate.Length >= 2 && double.IsFinite(coordinate[0]))
                    longitudes.Add(coordinate[0]);
            }
        }
    }

    private static double[][][] UnwrapPolygonLongitudes(
        double[][][] polygon,
        DisplayLongitudeFrame longitudeFrame) =>
        polygon
            .Select(ring => ring
                .Select(coordinate => coordinate.Length < 2
                    ? coordinate
                    : new[]
                    {
                        longitudeFrame.Unwrap(coordinate[0]),
                        coordinate[1]
                    })
                .ToArray())
            .ToArray();

    private static BoundaryGeometryStats BuildGeometryDebugStats(Geometry? geometry)
    {
        if (geometry is null || geometry.IsEmpty)
            return BoundaryGeometryStats.Empty;

        var largestPolygon = FlattenPolygons(geometry)
            .OrderByDescending(polygon => polygon.Area)
            .FirstOrDefault();

        return new BoundaryGeometryStats
        {
            GeometryType = geometry.GeometryType,
            NumGeometries = geometry is Polygon ? 1 : geometry.NumGeometries,
            CoordinateCount = geometry.Coordinates.Length,
            Envelope = ToBoundsDto(geometry.EnvelopeInternal),
            Area = geometry.Area,
            LargestPolygonArea = largestPolygon?.Area,
            LargestPolygonEnvelope = largestPolygon is null
                ? null
                : ToBoundsDto(largestPolygon.EnvelopeInternal)
        };
    }

    private static bool HasAntimeridianCrossing(Geometry? geometry)
    {
        if (geometry is null || geometry.IsEmpty)
            return false;

        var longitudes = geometry.Coordinates
            .Select(coordinate => coordinate.X)
            .Where(double.IsFinite)
            .ToArray();
        if (longitudes.Length < 2)
            return false;

        var rawSpan = longitudes.Max() - longitudes.Min();
        if (rawSpan > AntimeridianRawSpanThreshold)
            return true;

        return DisplayLongitudeFrame.Create(longitudes).ShouldUnwrap;
    }

    private static AdminTerritoryMapBoundsDto? ToBoundsDto(Envelope envelope)
    {
        if (envelope.IsNull)
            return null;

        return new AdminTerritoryMapBoundsDto
        {
            North = envelope.MaxY,
            South = envelope.MinY,
            East = envelope.MaxX,
            West = envelope.MinX
        };
    }

    private static IEnumerable<Polygon> FlattenPolygons(Geometry geometry)
    {
        if (geometry is Polygon polygon)
        {
            yield return polygon;
            yield break;
        }

        if (geometry is GeometryCollection collection)
        {
            for (var index = 0; index < collection.NumGeometries; index++)
            {
                foreach (var item in FlattenPolygons(collection.GetGeometryN(index)))
                    yield return item;
            }
        }
    }

    private static AdminTerritoryMapViewportDto BuildMapViewport(AdminTerritoryGeometryDto? geometry)
    {
        var accumulator = new BoundsAccumulator();
        IncludeGeometry(accumulator, geometry);

        if (!accumulator.HasValue)
        {
            return new AdminTerritoryMapViewportDto { IsEmpty = true };
        }

        var centerLatitude = (accumulator.North + accumulator.South) / 2d;
        var centerLongitude = (accumulator.East + accumulator.West) / 2d;
        var latitudeSpan = Math.Abs(accumulator.North - accumulator.South);
        var longitudeSpan = Math.Abs(accumulator.East - accumulator.West);
        var span = Math.Max(latitudeSpan, longitudeSpan);
        var isSinglePoint = span < 0.00001d;

        return new AdminTerritoryMapViewportDto
        {
            CenterLatitude = centerLatitude,
            CenterLongitude = centerLongitude,
            Zoom = isSinglePoint ? 13d : EstimateMapZoom(span),
            Bounds = isSinglePoint
                ? null
                : new AdminTerritoryMapBoundsDto
                {
                    North = accumulator.North,
                    South = accumulator.South,
                    East = accumulator.East,
                    West = accumulator.West
                },
            IsEmpty = false
        };
    }

    private static void IncludeGeometry(BoundsAccumulator accumulator, AdminTerritoryGeometryDto? geometry)
    {
        if (geometry?.Coordinates is null)
            return;

        if (geometry.Type.Equals("Polygon", StringComparison.OrdinalIgnoreCase) &&
            geometry.Coordinates is double[][][] polygon)
        {
            IncludePolygon(accumulator, polygon);
        }
        else if (geometry.Type.Equals("MultiPolygon", StringComparison.OrdinalIgnoreCase) &&
                 geometry.Coordinates is double[][][][] multiPolygon)
        {
            foreach (var polygonCoordinates in multiPolygon)
            {
                IncludePolygon(accumulator, polygonCoordinates);
            }
        }
    }

    private static void IncludePolygon(BoundsAccumulator accumulator, double[][][] polygon)
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

    private static double EstimateMapZoom(double span)
    {
        if (span >= 70d) return 2d;
        if (span >= 30d) return 3d;
        if (span >= 14d) return 4d;
        if (span >= 8d) return 5d;
        if (span >= 4d) return 6d;
        if (span >= 2d) return 7d;
        if (span >= 1d) return 8d;
        if (span >= 0.5d) return 9d;
        return 10d;
    }

    private static double RoundCoordinate(double coordinate, int decimals) =>
        Math.Round(coordinate, decimals, MidpointRounding.AwayFromZero);

    private static Polygon BuildPolygon(JsonElement coordinatesElement)
    {
        if (coordinatesElement.ValueKind != JsonValueKind.Array)
            throw new InvalidOperationException("Country boundary geometry is invalid.");

        var rings = coordinatesElement.EnumerateArray()
            .Select(ParseRing)
            .ToList();

        if (rings.Count == 0)
            throw new InvalidOperationException("Country boundary geometry must include an exterior ring.");

        var shell = Wgs84GeometryFactory.CreateLinearRing(rings[0]);
        var holes = rings
            .Skip(1)
            .Select(ring => Wgs84GeometryFactory.CreateLinearRing(ring))
            .ToArray();

        var polygon = Wgs84GeometryFactory.CreatePolygon(shell, holes);
        polygon.SRID = 4326;
        return polygon;
    }

    private static MultiPolygon BuildMultiPolygon(JsonElement coordinatesElement)
    {
        if (coordinatesElement.ValueKind != JsonValueKind.Array)
            throw new InvalidOperationException("Country boundary geometry is invalid.");

        var polygons = coordinatesElement.EnumerateArray()
            .Select(BuildPolygon)
            .ToArray();

        if (polygons.Length == 0)
            throw new InvalidOperationException("Country boundary geometry must include at least one polygon.");

        var multiPolygon = Wgs84GeometryFactory.CreateMultiPolygon(polygons);
        multiPolygon.SRID = 4326;
        return multiPolygon;
    }

    private static Coordinate[] ParseRing(JsonElement ringElement)
    {
        if (ringElement.ValueKind != JsonValueKind.Array)
            throw new InvalidOperationException("Country boundary geometry is invalid.");

        var coordinates = ringElement.EnumerateArray()
            .Select(ParseCoordinate)
            .ToList();

        if (coordinates.Count < 3)
            throw new InvalidOperationException("Country boundary geometry must contain at least three points.");

        var first = coordinates[0];
        var last = coordinates[^1];
        if (!CoordinatesEqual(first, last))
        {
            coordinates.Add(new Coordinate(first.X, first.Y));
        }

        var uniqueCount = coordinates
            .Take(coordinates.Count - 1)
            .Select(coordinate => $"{Math.Round(coordinate.X, 7)}:{Math.Round(coordinate.Y, 7)}")
            .Distinct(StringComparer.Ordinal)
            .Count();

        if (uniqueCount < 3 || coordinates.Count < 4)
            throw new InvalidOperationException("Country boundary geometry must contain at least three distinct points.");

        return coordinates.ToArray();
    }

    private static Coordinate ParseCoordinate(JsonElement coordinateElement)
    {
        if (coordinateElement.ValueKind != JsonValueKind.Array)
            throw new InvalidOperationException("Country boundary geometry is invalid.");

        var values = coordinateElement.EnumerateArray().ToArray();
        if (values.Length < 2 ||
            !values[0].TryGetDouble(out var longitude) ||
            !values[1].TryGetDouble(out var latitude) ||
            !double.IsFinite(latitude) ||
            !double.IsFinite(longitude) ||
            latitude is < -90 or > 90 ||
            longitude is < -180 or > 180)
        {
            throw new InvalidOperationException("Country boundary geometry contains invalid coordinates.");
        }

        return new Coordinate(longitude, latitude);
    }

    private static string NormalizeName(string? value, string requiredMessage)
    {
        var normalized = value?.Trim() ?? string.Empty;
        if (normalized.Length == 0)
            throw new InvalidOperationException(requiredMessage);
        if (normalized.Length > 200)
            throw new InvalidOperationException("Territory name is too long.");

        return normalized;
    }

    private static (string? Iso2, string? Iso3) ResolveCountryCodes(
        string? iso2,
        string? iso3,
        string? countryName,
        string? territoryKind = TerritoryKinds.Country)
    {
        if (!IsCountryKind(territoryKind))
            return (null, null);

        var resolvedIso2 = CountryRegistry.NormalizeIso2(iso2)
            ?? CountryRegistry.ToIso2ForBoundaryImport(iso3)
            ?? TryResolveCountryCode(countryName);
        var resolvedIso3 = CountryRegistry.NormalizeIso3ForBoundaryImport(iso3)
            ?? CountryRegistry.NormalizeIso3ForBoundaryImport(resolvedIso2)
            ?? CountryRegistry.ToIso3(resolvedIso2);

        return (resolvedIso2, resolvedIso3);
    }

    private static string NormalizeTerritoryKind(string? value) =>
        string.Equals(value, TerritoryKinds.DisputedTerritory, StringComparison.Ordinal)
            ? TerritoryKinds.DisputedTerritory
            : TerritoryKinds.Country;

    private static bool IsCountryKind(string? value) =>
        string.Equals(NormalizeTerritoryKind(value), TerritoryKinds.Country, StringComparison.Ordinal);

    private static bool IsDisputedKind(string? value) =>
        string.Equals(NormalizeTerritoryKind(value), TerritoryKinds.DisputedTerritory, StringComparison.Ordinal);

    private static List<string> BuildCountryCodeList(string? iso2, string? iso3)
    {
        if (!string.IsNullOrWhiteSpace(iso2))
            return new List<string> { iso2 };

        return string.IsNullOrWhiteSpace(iso3)
            ? new List<string>()
            : new List<string> { iso3 };
    }

    private static string? NormalizeCountryCode(string? value)
    {
        var normalized = value?.Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(normalized))
            return null;
        if (normalized.Length != 2 || normalized.Any(ch => ch is < 'A' or > 'Z'))
            throw new InvalidOperationException("Country code is invalid.");

        return normalized;
    }

    private static string NormalizeBoundarySource(string? value)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
            return GeoBoundariesBoundarySource;

        return normalized.Length > 32 ? normalized[..32] : normalized;
    }

    private static string BuildInternalReference(int territoryId) =>
        $"TERR-{territoryId:D3}";

    private static bool CoordinatesEqual(Coordinate first, Coordinate second) =>
        Math.Abs(first.X - second.X) < 0.0000001 &&
        Math.Abs(first.Y - second.Y) < 0.0000001;

    private static string BuildDisplayName(string? displayName, string? firstName, string? lastName, string email)
    {
        if (!string.IsNullOrWhiteSpace(displayName))
            return displayName.Trim();

        var name = string.Join(
            " ",
            new[] { firstName?.Trim(), lastName?.Trim() }
                .Where(part => !string.IsNullOrWhiteSpace(part)));

        return string.IsNullOrWhiteSpace(name) ? email : name;
    }

    private static string BuildInitials(string displayName, string email)
    {
        var source = string.IsNullOrWhiteSpace(displayName) ? email : displayName;
        var parts = source.Split(' ', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length >= 2)
            return string.Concat(parts[0][0], parts[1][0]).ToUpperInvariant();

        return source.Length >= 2
            ? source[..2].ToUpperInvariant()
            : "?";
    }

    private static string? TryResolveCountryCode(string? countryName)
    {
        if (string.IsNullOrWhiteSpace(countryName))
            return null;

        var normalizedName = countryName.Trim();

        try
        {
            return CultureInfo.GetCultures(CultureTypes.SpecificCultures)
                .Select(culture => new RegionInfo(culture.Name))
                .DistinctBy(region => region.TwoLetterISORegionName)
                .FirstOrDefault(region =>
                    string.Equals(region.EnglishName, normalizedName, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(region.DisplayName, normalizedName, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(region.NativeName, normalizedName, StringComparison.OrdinalIgnoreCase))
                ?.TwoLetterISORegionName;
        }
        catch (CultureNotFoundException)
        {
            return null;
        }
    }

    private sealed class TerritoryDirectoryRow
    {
        public int TerritoryId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string TerritoryKind { get; set; } = TerritoryKinds.Country;
        public string? CountryCode { get; set; }
        public string? CountryIso3 { get; set; }
        public string? BoundarySource { get; set; }
        public string? BoundarySourceUrl { get; set; }
        public string? BoundaryShapeType { get; set; }
        public string? BoundaryShapeGroup { get; set; }
        public string? BoundaryShapeId { get; set; }
        public string? BoundaryExternalKey { get; set; }
        public string? BoundaryAdmLevel { get; set; }
        public string? BoundaryReleaseType { get; set; }
        public string? BoundaryLicense { get; set; }
        public DateTime? BoundaryImportedAt { get; set; }
        public bool HasBoundary { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
        public TerritoryManagerRow? Manager { get; set; }
    }

    private class TerritoryDetailRow
    {
        public int TerritoryId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string TerritoryKind { get; set; } = TerritoryKinds.Country;
        public string? CountryCode { get; set; }
        public string? CountryIso3 { get; set; }
        public string? BoundarySource { get; set; }
        public string? BoundarySourceUrl { get; set; }
        public string? BoundaryShapeType { get; set; }
        public string? BoundaryShapeGroup { get; set; }
        public string? BoundaryShapeId { get; set; }
        public string? BoundaryExternalKey { get; set; }
        public string? BoundaryAdmLevel { get; set; }
        public string? BoundaryReleaseType { get; set; }
        public string? BoundaryLicense { get; set; }
        public DateTime? BoundaryImportedAt { get; set; }
        public Geometry? BoundaryGeometry { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
        public TerritoryManagerDetailRow? Manager { get; set; }
    }

    private sealed class TerritoryMapRow : TerritoryDetailRow
    {
        public bool HasValidationBoundary { get; set; }
        public bool HasDisplayBoundary { get; set; }
        public Geometry? BoundaryDisplayGeometry { get; set; }
    }

    private class TerritoryManagerRow
    {
        public int UserId { get; set; }
        public string? DisplayName { get; set; }
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
    }

    private sealed class TerritoryManagerDetailRow : TerritoryManagerRow
    {
        public string? AvatarUrl { get; set; }
        public List<string> Roles { get; set; } = new();
        public DateTime AssignedAt { get; set; }
    }

    private sealed class BoundaryGeometryStats
    {
        public static readonly BoundaryGeometryStats Empty = new();

        public string? GeometryType { get; init; }
        public int NumGeometries { get; init; }
        public int CoordinateCount { get; init; }
        public AdminTerritoryMapBoundsDto? Envelope { get; init; }
        public double? Area { get; init; }
        public double? LargestPolygonArea { get; init; }
        public AdminTerritoryMapBoundsDto? LargestPolygonEnvelope { get; init; }
    }

    private sealed class BoundsAccumulator
    {
        public double North { get; private set; } = double.NegativeInfinity;
        public double South { get; private set; } = double.PositiveInfinity;
        public double East { get; private set; } = double.NegativeInfinity;
        public double West { get; private set; } = double.PositiveInfinity;
        public bool HasValue { get; private set; }

        public void Include(double latitude, double longitude)
        {
            if (!double.IsFinite(latitude) || !double.IsFinite(longitude))
                return;

            North = Math.Max(North, latitude);
            South = Math.Min(South, latitude);
            East = Math.Max(East, longitude);
            West = Math.Min(West, longitude);
            HasValue = true;
        }
    }

    private readonly struct DisplayLongitudeFrame
    {
        private DisplayLongitudeFrame(bool shouldUnwrap, double start)
        {
            ShouldUnwrap = shouldUnwrap;
            Start = start;
        }

        public bool ShouldUnwrap { get; }
        private double Start { get; }

        public static DisplayLongitudeFrame Create(IReadOnlyCollection<double> longitudes)
        {
            var finite = longitudes
                .Where(double.IsFinite)
                .ToArray();
            if (finite.Length < 2)
                return new DisplayLongitudeFrame(false, 0d);

            var rawSpan = finite.Max() - finite.Min();
            if (rawSpan <= AntimeridianRawSpanThreshold)
                return new DisplayLongitudeFrame(false, 0d);

            var normalized = finite
                .Select(NormalizeLongitudeTo360)
                .OrderBy(value => value)
                .ToArray();
            if (normalized.Length < 2)
                return new DisplayLongitudeFrame(false, 0d);

            var largestGap = double.NegativeInfinity;
            var start = normalized[0];
            for (var index = 0; index < normalized.Length; index++)
            {
                var current = normalized[index];
                var next = index == normalized.Length - 1
                    ? normalized[0] + 360d
                    : normalized[index + 1];
                var gap = next - current;
                if (gap <= largestGap)
                    continue;

                largestGap = gap;
                start = index == normalized.Length - 1
                    ? normalized[0]
                    : normalized[index + 1];
            }

            var wrappedSpan = 360d - largestGap;
            var shouldUnwrap = wrappedSpan + LongitudeComparisonTolerance < rawSpan;
            if (shouldUnwrap && start + wrappedSpan / 2d > 180d + LongitudeComparisonTolerance)
            {
                start -= 360d;
            }

            return new DisplayLongitudeFrame(shouldUnwrap, start);
        }

        public double Unwrap(double longitude)
        {
            var normalized = NormalizeLongitudeTo360(longitude);
            var normalizedStart = NormalizeLongitudeTo360(Start);
            if (normalized + LongitudeComparisonTolerance < normalizedStart)
            {
                normalized += 360d;
            }

            if (Start < 0d)
            {
                normalized -= 360d;
            }

            return normalized;
        }

        private static double NormalizeLongitudeTo360(double longitude)
        {
            var normalized = longitude % 360d;
            if (normalized < 0d)
                normalized += 360d;

            return Math.Abs(normalized - 360d) < LongitudeComparisonTolerance
                ? 0d
                : normalized;
        }
    }
}
