using System.Globalization;
using System.Text.Json;
using Geotrack.Application.DTOs.Admin;
using Geotrack.Application.Interfaces.Service;
using Geotrack.Application.Security;
using Geotrack.Domain.Entities.Core;
using Geotrack.Domain.Entities.Users;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;

namespace Geotrack.Infrastructure.Services;

public sealed class AdminTerritoryManagementService : IAdminTerritoryManagementService
{
    private const int DefaultPageSize = 100;
    private const int MaxPageSize = 250;
    private const string BoundaryGeometryPropertyName = "BoundaryGeometry";
    private const string WorldMapBoundarySource = "WorldMapGeoJson";
    private const string MissingBoundaryAssignmentMessage =
        "This country cannot be assigned until its boundary geometry is available.";
    private static readonly GeometryFactory Wgs84GeometryFactory = new(new PrecisionModel(), 4326);

    private readonly AppDbContext _db;

    public AdminTerritoryManagementService(AppDbContext db)
    {
        _db = db;
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
                CountryCode = territory.CountryCode,
                BoundarySource = territory.BoundarySource,
                BoundaryShapeType = territory.BoundaryShapeType,
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
                CountryCode = item.CountryCode,
                BoundarySource = item.BoundarySource,
                BoundaryShapeType = item.BoundaryShapeType,
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
            .Include(user => user.UserRoles)
                .ThenInclude(userRole => userRole.Role)
            .Include(user => user.TerritoryManager!)
                .ThenInclude(manager => manager.TerritoryManagerTerritories)
                    .ThenInclude(assignment => assignment.Territory)
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

        var exists = await _db.Territories
            .AsNoTracking()
            .AnyAsync(item => item.Id == territoryId, cancellationToken);

        if (!exists)
            throw new KeyNotFoundException("Territory not found.");

        return await SearchManagerCandidatesAsync(query, take, territoryId, cancellationToken);
    }

    public async Task<AdminTerritoryDto> CreateTerritoryAsync(
        AdminCreateTerritoryRequestDto request,
        CancellationToken cancellationToken = default)
    {
        var countryCode = NormalizeCountryCode(request.CountryCode)
            ?? throw new InvalidOperationException("Country code is required.");
        var name = NormalizeName(request.Name, "Country name is required.");
        var boundary = BuildCountryBoundary(request.Boundary);
        var now = DateTime.UtcNow;

        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);

        var existing = await _db.Territories
            .Include(territory => territory.TerritoryManagerTerritories)
            .FirstOrDefaultAsync(
                territory =>
                    territory.CountryCode == countryCode ||
                    (territory.CountryCode == null && territory.Name.ToLower() == name.ToLower()),
                cancellationToken);

        if (existing is not null && !string.IsNullOrWhiteSpace(existing.CountryCode))
        {
            throw new InvalidOperationException("This country is already assigned to a territory.");
        }

        var territory = existing ?? new Territory
        {
            CreatedAt = now
        };

        territory.Name = name;
        territory.CountryCode = countryCode;
        territory.BoundarySource = NormalizeBoundarySource(request.Boundary?.Source);
        territory.BoundaryShapeType = boundary.GeometryType;
        territory.UpdatedAt = existing is null ? null : now;

        if (existing is null)
        {
            _db.Territories.Add(territory);
        }

        _db.Entry(territory).Property<Geometry?>(BoundaryGeometryPropertyName).CurrentValue = boundary;

        if (request.AssignedManagerUserId.HasValue)
        {
            await AssignManagerToTerritoryAsync(
                territory,
                request.AssignedManagerUserId.Value,
                allowedExistingTerritoryId: null,
                now,
                cancellationToken);
        }

        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return await GetTerritoryDtoAsync(territory.Id, cancellationToken);
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
            .SingleOrDefaultAsync(item => item.Id == territoryId, cancellationToken)
            ?? throw new KeyNotFoundException("Territory not found.");

        var requestedManagerUserId = request.ManagerUserId ?? request.AssignedManagerUserId;

        if (requestedManagerUserId.HasValue)
        {
            var boundary = await _db.Territories
                .Where(item => item.Id == territoryId)
                .Select(item => EF.Property<Geometry?>(item, BoundaryGeometryPropertyName))
                .SingleAsync(cancellationToken);

            if (boundary is null || boundary.IsEmpty || !boundary.IsValid)
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

    public async Task DeleteTerritoryAsync(int territoryId, CancellationToken cancellationToken = default)
    {
        if (territoryId <= 0)
            throw new InvalidOperationException("Territory id is required.");

        var territory = await _db.Territories
            .Include(item => item.Regions)
            .Include(item => item.TerritoryManagerTerritories)
            .SingleOrDefaultAsync(item => item.Id == territoryId, cancellationToken)
            ?? throw new KeyNotFoundException("Territory not found.");

        if (territory.Regions.Count > 0)
            throw new InvalidOperationException("This territory cannot be deleted while it contains regions.");

        _db.TerritoryManagerTerritories.RemoveRange(territory.TerritoryManagerTerritories);
        _db.Territories.Remove(territory);
        await _db.SaveChangesAsync(cancellationToken);
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
                CountryCode = item.CountryCode,
                BoundarySource = item.BoundarySource,
                BoundaryShapeType = item.BoundaryShapeType,
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
            .SingleOrDefaultAsync(item => item.Id == userId, cancellationToken)
            ?? throw new InvalidOperationException("Selected user was not found.");

        var roles = user.UserRoles
            .Select(userRole => RoleNames.Normalize(userRole.Role.Name))
            .ToList();

        if (roles.Contains(RoleNames.Admin, StringComparer.Ordinal))
            throw new InvalidOperationException("Administrators cannot be assigned as Territory Managers.");

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

    private static IQueryable<Territory> ApplySort(IQueryable<Territory> query, string? sortField, string? sortDir)
    {
        var desc = string.Equals(sortDir, "desc", StringComparison.OrdinalIgnoreCase);
        var field = (sortField ?? "createdAt").Trim().ToLowerInvariant();

        return field switch
        {
            "name" => desc ? query.OrderByDescending(item => item.Name) : query.OrderBy(item => item.Name),
            "status" => desc
                ? query.OrderBy(item => item.TerritoryManagerTerritories.Any())
                : query.OrderByDescending(item => item.TerritoryManagerTerritories.Any()),
            "countries" => desc
                ? query.OrderByDescending(item => item.CountryCode ?? item.Name)
                : query.OrderBy(item => item.CountryCode ?? item.Name),
            _ => desc ? query.OrderByDescending(item => item.CreatedAt) : query.OrderBy(item => item.CreatedAt)
        };
    }

    private static AdminTerritoryDto MapTerritory(TerritoryDirectoryRow row)
    {
        var manager = row.Manager;
        var countryCode = NormalizeCountryCode(row.CountryCode) ?? TryResolveCountryCode(row.Name);

        return new AdminTerritoryDto
        {
            TerritoryId = row.TerritoryId,
            CountryId = row.TerritoryId,
            Id = BuildInternalReference(row.TerritoryId),
            Name = row.Name,
            DisplayName = row.Name,
            InternalReference = BuildInternalReference(row.TerritoryId),
            CountryCode = countryCode,
            Countries = countryCode is null ? new List<string>() : new List<string> { countryCode },
            AssignedManagerUserId = manager?.UserId,
            AssignedManagerId = manager?.UserId.ToString(CultureInfo.InvariantCulture),
            AssignedManagerName = manager is null ? null : BuildDisplayName(manager.DisplayName, manager.FirstName, manager.LastName, manager.Email),
            AssignedManagerEmail = manager?.Email,
            Status = manager is null ? "Unassigned" : "Assigned",
            BoundarySource = row.BoundarySource,
            BoundaryShapeType = row.BoundaryShapeType,
            HasBoundary = row.HasBoundary,
            CreatedAt = row.CreatedAt,
            UpdatedAt = row.UpdatedAt ?? row.CreatedAt
        };
    }

    private static AdminTerritoryDetailDto MapTerritoryDetail(TerritoryDetailRow row)
    {
        var manager = row.Manager;
        var countryCode = NormalizeCountryCode(row.CountryCode) ?? TryResolveCountryCode(row.Name);
        var boundaryGeoJson = ToGeoJson(row.BoundaryGeometry);
        var internalReference = BuildInternalReference(row.TerritoryId);

        return new AdminTerritoryDetailDto
        {
            TerritoryId = row.TerritoryId,
            CountryId = row.TerritoryId,
            Id = internalReference,
            Name = row.Name,
            DisplayName = row.Name,
            InternalReference = internalReference,
            CountryCode = countryCode,
            Countries = countryCode is null ? new List<string>() : new List<string> { countryCode },
            AssignedManagerUserId = manager?.UserId,
            AssignedManagerId = manager?.UserId.ToString(CultureInfo.InvariantCulture),
            AssignedManagerName = manager is null ? null : BuildDisplayName(manager.DisplayName, manager.FirstName, manager.LastName, manager.Email),
            AssignedManagerEmail = manager?.Email,
            AssignedManager = manager is null ? null : MapAssignedManager(manager),
            Status = manager is null ? "Unassigned" : "Assigned",
            BoundarySource = row.BoundarySource,
            BoundaryShapeType = row.BoundaryShapeType,
            HasBoundary = boundaryGeoJson is not null,
            BoundaryGeoJson = boundaryGeoJson,
            Viewport = BuildMapViewport(boundaryGeoJson),
            CanAssign = boundaryGeoJson is not null,
            BoundaryRequiredMessage = boundaryGeoJson is null ? MissingBoundaryAssignmentMessage : null,
            CreatedAt = row.CreatedAt,
            UpdatedAt = row.UpdatedAt ?? row.CreatedAt
        };
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
            Status = "Active",
            IsActive = true,
            CanAssign = conflicting is null && !adminRoleConflict,
            IneligibilityReason = adminRoleConflict
                ? "Administrators cannot be assigned as Territory Managers."
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

    private static AdminTerritoryGeometryDto? ToGeoJson(Geometry? geometry)
    {
        var polygonalGeometry = ToPolygonalGeometry(geometry);
        if (polygonalGeometry is null)
            return null;

        return polygonalGeometry switch
        {
            Polygon polygon => new AdminTerritoryGeometryDto
            {
                Type = "Polygon",
                Coordinates = ToPolygonCoordinates(polygon)
            },
            MultiPolygon multiPolygon => new AdminTerritoryGeometryDto
            {
                Type = "MultiPolygon",
                Coordinates = Enumerable.Range(0, multiPolygon.NumGeometries)
                    .Select(index => ToPolygonCoordinates((Polygon)multiPolygon.GetGeometryN(index)))
                    .ToArray()
            },
            _ => null
        };
    }

    private static Geometry? ToPolygonalGeometry(Geometry? geometry)
    {
        if (geometry is null || geometry.IsEmpty)
            return null;

        var copy = geometry.Copy();
        copy.SRID = copy.SRID > 0 ? copy.SRID : 4326;

        if (!copy.IsValid)
            return null;

        return copy is Polygon or MultiPolygon ? copy : null;
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

    private static double RoundCoordinate(double coordinate) =>
        Math.Round(coordinate, 7, MidpointRounding.AwayFromZero);

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
            return WorldMapBoundarySource;

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
        public string? CountryCode { get; set; }
        public string? BoundarySource { get; set; }
        public string? BoundaryShapeType { get; set; }
        public bool HasBoundary { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
        public TerritoryManagerRow? Manager { get; set; }
    }

    private sealed class TerritoryDetailRow
    {
        public int TerritoryId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? CountryCode { get; set; }
        public string? BoundarySource { get; set; }
        public string? BoundaryShapeType { get; set; }
        public Geometry? BoundaryGeometry { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
        public TerritoryManagerDetailRow? Manager { get; set; }
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
}
