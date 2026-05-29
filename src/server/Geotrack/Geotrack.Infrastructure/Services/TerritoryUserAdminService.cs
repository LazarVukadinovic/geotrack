using Geotrack.Application.DTOs.Admin;
using Geotrack.Application.Interfaces.Service;
using Geotrack.Application.Security;
using Geotrack.Domain.Entities.Users;
using Microsoft.EntityFrameworkCore;

namespace Geotrack.Infrastructure.Services;

public sealed class TerritoryUserAdminService : ITerritoryUserAdminService
{
    private const int DirectoryMaxTake = 100;
    private const int AvailableRegionsMaxTake = 100;
    private const int AssignableUsersMaxTake = 50;
    private const int MaxRegionIdsToAdd = 100;

    private readonly AppDbContext _db;
    private readonly IAdminNotificationService _adminNotificationService;

    public TerritoryUserAdminService(
        AppDbContext db,
        IAdminNotificationService adminNotificationService)
    {
        _db = db;
        _adminNotificationService = adminNotificationService;
    }

    public async Task<TerritoryUserListResponseDto> GetUsersAsync(
        int currentUserId,
        int? territoryId,
        string? search,
        string? sort,
        int skip,
        int take)
    {
        var context = await GetAuthorizedTerritoryContextAsync(currentUserId, territoryId);
        skip = Math.Max(0, skip);
        take = Math.Clamp(take, 1, DirectoryMaxTake);

        if (context.ActiveTerritory is null)
        {
            return new TerritoryUserListResponseDto
            {
                Skip = skip,
                Take = take
            };
        }

        var regionalAssignments = _db.RegionalManagerRegions
            .AsNoTracking()
            .Where(assignment => assignment.Region.TerritoryId == context.ActiveTerritory.Id);

        var territoryAssociateAssignments = _db.TerritoryManagerTerritories
            .AsNoTracking()
            .Where(assignment =>
                assignment.TerritoryId == context.ActiveTerritory.Id &&
                assignment.TerritoryManager.User.UserRoles.Any(userRole =>
                    userRole.Role.Name == RoleNames.TerritoryAssociate));

        if (!string.IsNullOrWhiteSpace(search))
        {
            var normalizedSearch = search.Trim().ToLowerInvariant();
            regionalAssignments = regionalAssignments.Where(assignment =>
                assignment.User.Email.ToLower().Contains(normalizedSearch) ||
                (assignment.User.DisplayName ?? string.Empty).ToLower().Contains(normalizedSearch) ||
                assignment.User.FirstName.ToLower().Contains(normalizedSearch) ||
                assignment.User.LastName.ToLower().Contains(normalizedSearch) ||
                assignment.Region.Name.ToLower().Contains(normalizedSearch));

            territoryAssociateAssignments = territoryAssociateAssignments.Where(assignment =>
                assignment.TerritoryManager.User.Email.ToLower().Contains(normalizedSearch) ||
                (assignment.TerritoryManager.User.DisplayName ?? string.Empty).ToLower().Contains(normalizedSearch) ||
                assignment.TerritoryManager.User.FirstName.ToLower().Contains(normalizedSearch) ||
                assignment.TerritoryManager.User.LastName.ToLower().Contains(normalizedSearch));
        }

        var regionalUserIds = await regionalAssignments
            .Select(assignment => assignment.UserId)
            .Distinct()
            .ToListAsync();

        var associateUserIds = await territoryAssociateAssignments
            .Select(assignment => assignment.UserId)
            .Distinct()
            .ToListAsync();

        var userIds = regionalUserIds
            .Concat(associateUserIds)
            .Distinct()
            .ToList();

        var rows = await BuildRowsAsync(context, userIds);
        var ordered = ApplySort(rows, sort).ToList();

        return new TerritoryUserListResponseDto
        {
            Territory = context.ActiveTerritory,
            Items = ordered.Skip(skip).Take(take).ToList(),
            TotalCount = ordered.Count,
            Skip = skip,
            Take = take,
            HasMore = skip + take < ordered.Count
        };
    }

    public async Task<TerritoryUserListItemDto> GetUserAsync(
        int currentUserId,
        int targetUserId,
        int? territoryId)
    {
        if (targetUserId <= 0)
            throw new InvalidOperationException("userId must be positive.");

        var context = await GetAuthorizedTerritoryContextAsync(currentUserId, territoryId);
        var row = await BuildSingleRowAsync(context, targetUserId);
        if (!row.IsInTerritoryScope)
            throw new UnauthorizedAccessException("User is not assigned to this territory.");

        return row;
    }

    public async Task<TerritoryUserListItemDto> UpdateUserAsync(
        int currentUserId,
        int targetUserId,
        TerritoryUserUpdateRequestDto request)
    {
        if (targetUserId <= 0)
            throw new InvalidOperationException("userId must be positive.");

        ValidateUpdateRequest(request);

        var context = await GetAuthorizedTerritoryContextAsync(currentUserId, request.TerritoryId);
        var activeTerritory = context.ActiveTerritory
            ?? throw new UnauthorizedAccessException("Territory is not assigned to the current territory administrator.");

        var user = await GetTargetUserForMutationAsync(targetUserId);
        EnsureTargetIsInTerritory(user, activeTerritory.Id);
        EnsureTargetCanBeManaged(user);

        var now = DateTime.UtcNow;
        var regionIdsToAdd = NormalizeRegionIdsToAdd(request.RegionIdsToAdd);

        user.FirstName = request.FirstName?.Trim() ?? string.Empty;
        user.LastName = request.LastName?.Trim() ?? string.Empty;

        if (regionIdsToAdd.Count > 0)
        {
            EnsureNotSelf(currentUserId, targetUserId, "You cannot change your own region assignments.");
            var regionalRole = await EnsureRoleAsync(RoleNames.RegionalManager);
            await AddRegionsToUserAsync(user, activeTerritory.Id, regionIdsToAdd, now, ignoreActiveDuplicates: false);
            EnsureRoleOnUser(user, regionalRole, RoleNames.RegionalManager);
        }

        user.UpdatedAt = now;

        await _db.SaveChangesAsync();
        return await BuildSingleRowAsync(context, targetUserId);
    }

    public async Task<TerritoryUserAvailableRegionsResponseDto> GetAvailableRegionsAsync(
        int currentUserId,
        int targetUserId,
        int? territoryId,
        string? search,
        int take)
    {
        if (targetUserId <= 0)
            throw new InvalidOperationException("userId must be positive.");

        var context = await GetAuthorizedTerritoryContextAsync(currentUserId, territoryId);
        var activeTerritory = context.ActiveTerritory
            ?? throw new UnauthorizedAccessException("Territory is not assigned to the current territory administrator.");

        var user = await GetTargetUserForMutationAsync(targetUserId);
        EnsureTargetCanBeManaged(user);
        EnsureNotSelf(currentUserId, targetUserId, "You cannot change your own region assignments.");

        var activeRegionIds = user.RegionalManagerRegions
            .Where(assignment => assignment.IsActive)
            .Select(assignment => assignment.RegionId)
            .ToHashSet();

        take = Math.Clamp(take, 1, AvailableRegionsMaxTake);

        var query = _db.Regions
            .AsNoTracking()
            .Where(region =>
                region.TerritoryId == activeTerritory.Id &&
                !activeRegionIds.Contains(region.Id));

        if (!string.IsNullOrWhiteSpace(search))
        {
            var normalizedSearch = search.Trim().ToLowerInvariant();
            query = query.Where(region => region.Name.ToLower().Contains(normalizedSearch));
        }

        var items = await query
            .OrderBy(region => region.Name)
            .ThenBy(region => region.Id)
            .Take(take)
            .Select(region => new TerritoryUserAvailableRegionDto
            {
                RegionId = region.Id,
                RegionName = region.Name,
                DisplayName = region.Name
            })
            .ToListAsync();

        return new TerritoryUserAvailableRegionsResponseDto
        {
            Items = items
        };
    }

    public async Task<TerritoryAssignableUserSearchResponseDto> SearchAssignableUsersAsync(
        int currentUserId,
        int? territoryId,
        string? query,
        int take,
        bool excludeAlreadyRegionalManager,
        bool excludeAlreadyTerritoryAssociate)
    {
        var context = await GetAuthorizedTerritoryContextAsync(currentUserId, territoryId);
        var activeTerritory = context.ActiveTerritory
            ?? throw new UnauthorizedAccessException("Territory is not assigned to the current territory administrator.");

        take = Math.Clamp(take, 1, AssignableUsersMaxTake);

        var users = _db.Users
            .AsNoTracking()
            .Where(user =>
                user.Id != currentUserId &&
                !user.UserRoles.Any(userRole =>
                    userRole.Role.Name == RoleNames.Admin ||
                    userRole.Role.Name == RoleNames.TerritoryManager));

        if (!string.IsNullOrWhiteSpace(query))
        {
            var normalizedQuery = query.Trim().ToLowerInvariant();
            users = users.Where(user =>
                user.Email.ToLower().Contains(normalizedQuery) ||
                (user.DisplayName ?? string.Empty).ToLower().Contains(normalizedQuery) ||
                user.FirstName.ToLower().Contains(normalizedQuery) ||
                user.LastName.ToLower().Contains(normalizedQuery));
        }

        if (excludeAlreadyRegionalManager)
        {
            users = users.Where(user =>
                !user.RegionalManagerRegions.Any(assignment =>
                    assignment.IsActive &&
                    assignment.Region.TerritoryId == activeTerritory.Id));
        }

        if (excludeAlreadyTerritoryAssociate)
        {
            users = users.Where(user =>
                !user.UserRoles.Any(userRole => userRole.Role.Name == RoleNames.TerritoryAssociate) ||
                !_db.TerritoryManagerTerritories.Any(assignment =>
                    assignment.UserId == user.Id &&
                    assignment.TerritoryId == activeTerritory.Id));
        }

        var userIds = await users
            .OrderBy(user => user.FirstName)
            .ThenBy(user => user.LastName)
            .ThenBy(user => user.DisplayName)
            .ThenBy(user => user.Email)
            .Take(take)
            .Select(user => user.Id)
            .ToListAsync();

        return new TerritoryAssignableUserSearchResponseDto
        {
            Items = await BuildAssignableRowsAsync(activeTerritory, userIds)
        };
    }

    public async Task<TerritoryUserListItemDto> AddRegionAsync(
        int currentUserId,
        int targetUserId,
        TerritoryUserAssignRegionRequestDto request)
    {
        if (targetUserId <= 0)
            throw new InvalidOperationException("userId must be positive.");

        if (request.RegionId <= 0)
            throw new InvalidOperationException("regionId must be positive.");

        var context = await GetAuthorizedTerritoryContextAsync(currentUserId, request.TerritoryId);
        var activeTerritory = context.ActiveTerritory
            ?? throw new UnauthorizedAccessException("Territory is not assigned to the current territory administrator.");

        var region = await _db.Regions
            .AsNoTracking()
            .SingleOrDefaultAsync(existing => existing.Id == request.RegionId)
            ?? throw new KeyNotFoundException("Region not found.");

        if (region.TerritoryId != activeTerritory.Id)
            throw new UnauthorizedAccessException("Region is not in the current territory.");

        var user = await GetTargetUserForMutationAsync(targetUserId);
        EnsureTargetIsInTerritory(user, activeTerritory.Id);
        EnsureTargetCanBeManaged(user);
        EnsureNotSelf(currentUserId, targetUserId, "You cannot change your own region assignments.");

        var now = DateTime.UtcNow;
        var regionalRole = await EnsureRoleAsync(RoleNames.RegionalManager);
        await AddRegionsToUserAsync(user, activeTerritory.Id, [request.RegionId], now, ignoreActiveDuplicates: false);
        EnsureRoleOnUser(user, regionalRole, RoleNames.RegionalManager);
        user.UpdatedAt = now;

        await _db.SaveChangesAsync();
        await _adminNotificationService.CreateTerritoryRegionalManagerAssignedNotificationAsync(
            activeTerritory.Id,
            request.RegionId,
            targetUserId,
            now);
        return await BuildSingleRowAsync(context, targetUserId);
    }

    public async Task<TerritoryUserListItemDto> RemoveRegionAsync(
        int currentUserId,
        int targetUserId,
        int regionId,
        int? territoryId)
    {
        if (targetUserId <= 0)
            throw new InvalidOperationException("userId must be positive.");

        if (regionId <= 0)
            throw new InvalidOperationException("regionId must be positive.");

        var context = await GetAuthorizedTerritoryContextAsync(currentUserId, territoryId);
        var activeTerritory = context.ActiveTerritory
            ?? throw new UnauthorizedAccessException("Territory is not assigned to the current territory administrator.");

        EnsureCurrentUserIsTerritoryManager(context);
        EnsureNotSelf(currentUserId, targetUserId, "You cannot change your own region assignments.");

        var user = await GetTargetUserForMutationAsync(targetUserId);
        EnsureTargetIsInTerritory(user, activeTerritory.Id);
        EnsureTargetCanBeManaged(user);

        var assignment = user.RegionalManagerRegions
            .SingleOrDefault(existing => existing.RegionId == regionId && existing.IsActive)
            ?? throw new KeyNotFoundException("Region assignment not found.");

        if (assignment.Region.TerritoryId != activeTerritory.Id)
            throw new UnauthorizedAccessException("Region is not in the current territory.");

        var regionalRole = user.UserRoles
            .SingleOrDefault(userRole => RoleNames.Normalize(userRole.Role.Name) == RoleNames.RegionalManager);

        if (regionalRole is null)
            throw new InvalidOperationException("User does not have the RegionalManager role.");

        var now = DateTime.UtcNow;
        assignment.IsActive = false;
        assignment.UpdatedAt = now;

        if (!user.RegionalManagerRegions.Any(existing =>
                existing.IsActive &&
                existing.RegionId != regionId))
        {
            _db.UserRoles.Remove(regionalRole);
        }

        user.UpdatedAt = now;
        await _db.SaveChangesAsync();
        await _adminNotificationService.CreateTerritoryRegionalManagerRemovedNotificationAsync(
            activeTerritory.Id,
            regionId,
            targetUserId,
            now);

        return await BuildSingleRowAsync(context, targetUserId);
    }

    public async Task<TerritoryUserListItemDto> AddRegionalManagerAsync(
        int currentUserId,
        int targetUserId,
        TerritoryUserAddRegionalManagerRequestDto request)
    {
        if (targetUserId <= 0)
            throw new InvalidOperationException("userId must be positive.");

        var regionIds = NormalizeRegionIdsToAdd(request.RegionIds);
        if (regionIds.Count == 0)
            throw new InvalidOperationException("At least one region is required.");

        var context = await GetAuthorizedTerritoryContextAsync(currentUserId, request.TerritoryId);
        var activeTerritory = context.ActiveTerritory
            ?? throw new UnauthorizedAccessException("Territory is not assigned to the current territory administrator.");

        EnsureNotSelf(currentUserId, targetUserId, "You cannot change your own regional manager access.");

        var user = await GetTargetUserForMutationAsync(targetUserId);
        EnsureTargetCanBeManaged(user);

        var now = DateTime.UtcNow;
        var existingActiveRegionIds = user.RegionalManagerRegions
            .Where(assignment => assignment.IsActive && assignment.Region.TerritoryId == activeTerritory.Id)
            .Select(assignment => assignment.RegionId)
            .ToHashSet();
        var regionalRole = await EnsureRoleAsync(RoleNames.RegionalManager);
        await AddRegionsToUserAsync(user, activeTerritory.Id, regionIds, now, ignoreActiveDuplicates: true);
        EnsureRoleOnUser(user, regionalRole, RoleNames.RegionalManager);

        if (!user.RegionalManagerRegions.Any(assignment =>
                assignment.IsActive &&
                (regionIds.Contains(assignment.RegionId) ||
                 assignment.Region.TerritoryId == activeTerritory.Id)))
        {
            throw new InvalidOperationException("Regional Manager must have at least one assigned region.");
        }

        user.UpdatedAt = now;
        await _db.SaveChangesAsync();
        foreach (var regionId in regionIds.Where(regionId => !existingActiveRegionIds.Contains(regionId)))
        {
            await _adminNotificationService.CreateTerritoryRegionalManagerAssignedNotificationAsync(
                activeTerritory.Id,
                regionId,
                targetUserId,
                now);
        }

        return await BuildSingleRowAsync(context, targetUserId);
    }

    public async Task<TerritoryUserListItemDto> AddTerritoryAssociateAsync(
        int currentUserId,
        int targetUserId,
        TerritoryUserAddTerritoryAssociateRequestDto request)
    {
        if (targetUserId <= 0)
            throw new InvalidOperationException("userId must be positive.");

        var context = await GetAuthorizedTerritoryContextAsync(currentUserId, request.TerritoryId);
        var activeTerritory = context.ActiveTerritory
            ?? throw new UnauthorizedAccessException("Territory is not assigned to the current territory administrator.");

        EnsureCurrentUserIsTerritoryManager(context);
        EnsureNotSelf(currentUserId, targetUserId, "You cannot assign yourself as a Territory Associate.");

        var user = await GetTargetUserForMutationAsync(targetUserId);
        EnsureTargetCanBeManaged(user);

        var now = DateTime.UtcNow;
        var associateRole = await EnsureRoleAsync(RoleNames.TerritoryAssociate);
        EnsureRoleOnUser(user, associateRole, RoleNames.TerritoryAssociate);
        EnsureTerritoryAssignment(user, activeTerritory.Id, now);

        user.UpdatedAt = now;
        await _db.SaveChangesAsync();
        return await BuildSingleRowAsync(context, targetUserId);
    }

    public async Task<TerritoryUserListItemDto> RemoveManagerRoleAsync(
        int currentUserId,
        int targetUserId,
        TerritoryUserRemoveManagerRoleRequestDto request)
    {
        if (targetUserId <= 0)
            throw new InvalidOperationException("userId must be positive.");

        var context = await GetAuthorizedTerritoryContextAsync(currentUserId, request.TerritoryId);
        var activeTerritory = context.ActiveTerritory
            ?? throw new UnauthorizedAccessException("Territory is not assigned to the current territory administrator.");

        EnsureCurrentUserIsTerritoryManager(context);

        var user = await GetTargetUserForMutationAsync(targetUserId);
        EnsureTargetIsInTerritory(user, activeTerritory.Id);
        EnsureTargetCanBeManaged(user);
        EnsureNotSelf(currentUserId, targetUserId, "You cannot remove your own manager access.");

        var regionalRole = user.UserRoles
            .SingleOrDefault(userRole => RoleNames.Normalize(userRole.Role.Name) == RoleNames.RegionalManager);

        if (regionalRole is null)
            throw new InvalidOperationException("User does not have the RegionalManager role.");

        var activeAssignmentsInTerritory = user.RegionalManagerRegions
            .Where(assignment =>
                assignment.IsActive &&
                assignment.Region.TerritoryId == activeTerritory.Id)
            .ToList();

        if (activeAssignmentsInTerritory.Count == 0)
            throw new InvalidOperationException("User has no active manager assignments in this territory.");

        var now = DateTime.UtcNow;
        var removedRegionIds = activeAssignmentsInTerritory
            .Select(assignment => assignment.RegionId)
            .ToList();
        foreach (var assignment in activeAssignmentsInTerritory)
        {
            assignment.IsActive = false;
            assignment.UpdatedAt = now;
        }

        if (!user.RegionalManagerRegions.Any(assignment => assignment.IsActive))
        {
            _db.UserRoles.Remove(regionalRole);
        }

        user.UpdatedAt = now;
        await _db.SaveChangesAsync();
        foreach (var regionId in removedRegionIds)
        {
            await _adminNotificationService.CreateTerritoryRegionalManagerRemovedNotificationAsync(
                activeTerritory.Id,
                regionId,
                targetUserId,
                now);
        }

        return await BuildSingleRowAsync(context, targetUserId);
    }

    public async Task<TerritoryUserListItemDto> RemoveTerritoryAssociateAsync(
        int currentUserId,
        int targetUserId,
        TerritoryUserRemoveTerritoryAssociateRequestDto request)
    {
        if (targetUserId <= 0)
            throw new InvalidOperationException("userId must be positive.");

        var context = await GetAuthorizedTerritoryContextAsync(currentUserId, request.TerritoryId);
        var activeTerritory = context.ActiveTerritory
            ?? throw new UnauthorizedAccessException("Territory is not assigned to the current territory administrator.");

        EnsureCurrentUserIsTerritoryManager(context);
        EnsureNotSelf(currentUserId, targetUserId, "You cannot remove your own territory access.");

        var user = await GetTargetUserForMutationAsync(targetUserId);
        EnsureTargetCanBeManaged(user);

        var associateRole = user.UserRoles
            .SingleOrDefault(userRole => RoleNames.Normalize(userRole.Role.Name) == RoleNames.TerritoryAssociate);

        if (associateRole is null)
            throw new InvalidOperationException("User does not have the TerritoryAssociate role.");

        var territoryManager = user.TerritoryManager
            ?? throw new InvalidOperationException("User is not assigned as a Territory Associate in this territory.");

        var assignment = territoryManager.TerritoryManagerTerritories
            .SingleOrDefault(existing => existing.TerritoryId == activeTerritory.Id)
            ?? throw new InvalidOperationException("User is not assigned as a Territory Associate in this territory.");

        var remainingAssociateAssignments = territoryManager.TerritoryManagerTerritories
            .Where(existing => existing.TerritoryId != activeTerritory.Id)
            .ToList();

        _db.TerritoryManagerTerritories.Remove(assignment);

        if (remainingAssociateAssignments.Count == 0)
        {
            _db.UserRoles.Remove(associateRole);
            _db.TerritoryManagers.Remove(territoryManager);
        }

        user.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        return await BuildSingleRowAsync(context, targetUserId, allowOutOfScope: true);
    }

    private async Task<TerritoryUserAdminContext> GetAuthorizedTerritoryContextAsync(int currentUserId, int? territoryId)
    {
        var roles = await GetUserRolesAsync(currentUserId);
        var isTerritoryManager = roles.Contains(RoleNames.TerritoryManager, StringComparer.Ordinal);
        var isTerritoryAssociate = roles.Contains(RoleNames.TerritoryAssociate, StringComparer.Ordinal);

        if (!isTerritoryManager && !isTerritoryAssociate)
            throw new UnauthorizedAccessException("Territory administrator role is required.");

        var assignedTerritories = await _db.TerritoryManagerTerritories
            .AsNoTracking()
            .Where(assignment => assignment.UserId == currentUserId)
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

        if (assignedTerritories.Count == 0)
        {
            if (territoryId.HasValue)
                throw new UnauthorizedAccessException("Territory is not assigned to the current territory administrator.");

            return new TerritoryUserAdminContext(
                currentUserId,
                null,
                roles,
                isTerritoryManager,
                isTerritoryAssociate);
        }

        var selected = territoryId.HasValue
            ? assignedTerritories.SingleOrDefault(territory => territory.Id == territoryId.Value)
            : assignedTerritories[0];

        return new TerritoryUserAdminContext(
            currentUserId,
            selected ?? throw new UnauthorizedAccessException("Territory is not assigned to the current territory administrator."),
            roles,
            isTerritoryManager,
            isTerritoryAssociate);
    }

    private async Task<List<string>> GetUserRolesAsync(int userId)
    {
        var roles = await _db.UserRoles
            .AsNoTracking()
            .Where(userRole => userRole.UserId == userId)
            .Select(userRole => userRole.Role.Name)
            .ToListAsync();

        return NormalizeRoles(roles);
    }

    private static void EnsureCurrentUserIsTerritoryManager(TerritoryUserAdminContext context)
    {
        if (!context.IsTerritoryManager)
            throw new UnauthorizedAccessException("Territory Manager role is required.");
    }

    private async Task<User> GetTargetUserForMutationAsync(int targetUserId)
    {
        return await _db.Users
            .Include(user => user.UserRoles)
                .ThenInclude(userRole => userRole.Role)
            .Include(user => user.RegionalManagerRegions)
                .ThenInclude(assignment => assignment.Region)
            .Include(user => user.TerritoryManager)
                .ThenInclude(manager => manager!.TerritoryManagerTerritories)
            .AsSplitQuery()
            .SingleOrDefaultAsync(user => user.Id == targetUserId)
            ?? throw new KeyNotFoundException("User not found.");
    }

    private static void EnsureTargetIsInTerritory(User user, int territoryId)
    {
        var hasRegionalScope = user.RegionalManagerRegions
            .Any(assignment => assignment.Region.TerritoryId == territoryId);
        var hasTerritoryAssociateScope = user.TerritoryManager?.TerritoryManagerTerritories
            .Any(assignment => assignment.TerritoryId == territoryId) == true;

        if (!hasRegionalScope && !hasTerritoryAssociateScope)
            throw new UnauthorizedAccessException("User is not assigned to this territory.");
    }

    private static void EnsureTargetCanBeManaged(User user)
    {
        var roles = NormalizeRoles(user.UserRoles.Select(userRole => userRole.Role.Name));

        if (roles.Contains(RoleNames.Admin, StringComparer.Ordinal) ||
            roles.Contains(RoleNames.TerritoryManager, StringComparer.Ordinal))
        {
            throw new InvalidOperationException("This user's role cannot be managed from territory administration.");
        }
    }

    private static void EnsureNotSelf(int currentUserId, int targetUserId, string message)
    {
        if (currentUserId == targetUserId)
            throw new InvalidOperationException(message);
    }

    private async Task<int> AddRegionsToUserAsync(
        User user,
        int activeTerritoryId,
        IReadOnlyCollection<int> regionIdsToAdd,
        DateTime now,
        bool ignoreActiveDuplicates)
    {
        if (regionIdsToAdd.Count == 0)
            return 0;

        var regions = await _db.Regions
            .AsNoTracking()
            .Where(region => regionIdsToAdd.Contains(region.Id))
            .Select(region => new
            {
                region.Id,
                region.TerritoryId
            })
            .ToListAsync();

        if (regions.Count != regionIdsToAdd.Count)
            throw new KeyNotFoundException("Region not found.");

        if (regions.Any(region => region.TerritoryId != activeTerritoryId))
            throw new UnauthorizedAccessException("Region is not in the current territory.");

        var changedCount = 0;
        foreach (var regionId in regionIdsToAdd)
        {
            var existingAssignment = user.RegionalManagerRegions
                .SingleOrDefault(assignment => assignment.RegionId == regionId);

            if (existingAssignment is { IsActive: true })
            {
                if (ignoreActiveDuplicates)
                    continue;

                throw new InvalidOperationException("Region is already assigned to this user.");
            }

            if (existingAssignment is not null)
            {
                existingAssignment.IsActive = true;
                existingAssignment.UpdatedAt = now;
                changedCount++;
                continue;
            }

            user.RegionalManagerRegions.Add(new RegionalManagerRegion
            {
                UserId = user.Id,
                RegionId = regionId,
                IsActive = true,
                CreatedAt = now
            });
            changedCount++;
        }

        return changedCount;
    }

    private async Task<Role> EnsureRoleAsync(string roleName)
    {
        var normalized = RoleNames.Normalize(roleName);
        var role = await _db.Roles.SingleOrDefaultAsync(existing => existing.Name == normalized);
        if (role is not null)
            return role;

        role = new Role
        {
            Name = normalized,
            Description = normalized switch
            {
                RoleNames.RegionalManager => "Can review and approve regional user contributions.",
                RoleNames.TerritoryManager => "Can manage territories and assigned regions.",
                RoleNames.TerritoryAssociate => "Can manage assigned territories except associate assignment and protected role removal.",
                RoleNames.RegisteredUser => "Regular authenticated user.",
                _ => normalized
            },
            CreatedAt = DateTime.UtcNow
        };

        _db.Roles.Add(role);
        await _db.SaveChangesAsync();
        return role;
    }

    private static void EnsureRoleOnUser(User user, Role role, string roleName)
    {
        var normalized = RoleNames.Normalize(roleName);
        if (user.UserRoles.Any(userRole => RoleNames.Normalize(userRole.Role.Name) == normalized))
            return;

        user.UserRoles.Add(new UserRole
        {
            UserId = user.Id,
            RoleId = role.Id,
            Role = role
        });
    }

    private void EnsureTerritoryAssignment(User user, int territoryId, DateTime now)
    {
        if (user.TerritoryManager is null)
        {
            user.TerritoryManager = new TerritoryManager
            {
                UserId = user.Id
            };
            _db.TerritoryManagers.Add(user.TerritoryManager);
        }

        if (user.TerritoryManager.TerritoryManagerTerritories.Any(assignment => assignment.TerritoryId == territoryId))
            return;

        user.TerritoryManager.TerritoryManagerTerritories.Add(new TerritoryManagerTerritory
        {
            UserId = user.Id,
            TerritoryId = territoryId,
            CreatedAt = now
        });
    }

    private async Task<TerritoryUserListItemDto> BuildSingleRowAsync(
        TerritoryUserAdminContext context,
        int targetUserId,
        bool allowOutOfScope = false)
    {
        var row = (await BuildRowsAsync(context, [targetUserId])).SingleOrDefault();
        if (row is not null)
            return row;

        if (allowOutOfScope)
            return await BuildOutOfScopeRowAsync(context, targetUserId);

        throw new UnauthorizedAccessException("User is not assigned to this territory.");
    }

    private async Task<TerritoryUserListItemDto> BuildOutOfScopeRowAsync(
        TerritoryUserAdminContext context,
        int targetUserId)
    {
        var row = (await BuildRowsAsync(context, [targetUserId], includeOutOfScope: true)).SingleOrDefault()
            ?? throw new KeyNotFoundException("User not found.");

        row.IsInTerritoryScope = false;
        row.IsActive = false;
        row.Status = "Inactive";
        row.CanEdit = false;
        row.CanEditUser = false;
        row.CanAddRegionalManagerRole = false;
        row.CanAddRegion = false;
        row.CanRemoveRegion = false;
        row.CanRemoveManagerRole = false;
        row.CanRemoveRegionalManagerRole = false;
        row.CanAddTerritoryAssociate = false;
        row.CanAddTerritoryAssociateRole = false;
        row.CanRemoveTerritoryAssociateRole = false;
        return row;
    }

    private async Task<List<TerritoryUserListItemDto>> BuildRowsAsync(
        TerritoryUserAdminContext context,
        IReadOnlyCollection<int> userIds,
        bool includeOutOfScope = false)
    {
        var activeTerritory = context.ActiveTerritory
            ?? throw new UnauthorizedAccessException("Territory is not assigned to the current territory administrator.");

        var ids = userIds.Distinct().ToList();
        if (ids.Count == 0)
            return new List<TerritoryUserListItemDto>();

        var users = await _db.Users
            .AsNoTracking()
            .Where(user => ids.Contains(user.Id))
            .Select(user => new UserRow
            {
                UserId = user.Id,
                FirstName = user.FirstName,
                LastName = user.LastName,
                DisplayName = user.DisplayName,
                Username = user.Username,
                Email = user.Email,
                AvatarUrl = user.AvatarUrl,
                CreatedAt = user.CreatedAt,
                LastLoginAt = user.LastLoginAt
            })
            .ToListAsync();

        var assignments = await _db.RegionalManagerRegions
            .AsNoTracking()
            .Where(assignment =>
                ids.Contains(assignment.UserId) &&
                (includeOutOfScope || assignment.Region.TerritoryId == activeTerritory.Id))
            .Select(assignment => new AssignmentRow
            {
                UserId = assignment.UserId,
                RegionId = assignment.RegionId,
                RegionName = assignment.Region.Name,
                IsActive = assignment.IsActive,
                TerritoryId = assignment.Region.TerritoryId,
                TerritoryName = assignment.Region.Territory.Name,
                CreatedAt = assignment.CreatedAt
            })
            .ToListAsync();

        var territoryAssignments = await _db.TerritoryManagerTerritories
            .AsNoTracking()
            .Where(assignment =>
                ids.Contains(assignment.UserId) &&
                (includeOutOfScope || assignment.TerritoryId == activeTerritory.Id))
            .Select(assignment => new TerritoryAssignmentRow
            {
                UserId = assignment.UserId,
                TerritoryId = assignment.TerritoryId,
                TerritoryName = assignment.Territory.Name
            })
            .ToListAsync();

        var roleRows = await _db.UserRoles
            .AsNoTracking()
            .Where(userRole => ids.Contains(userRole.UserId))
            .Select(userRole => new RoleRow
            {
                UserId = userRole.UserId,
                RoleName = userRole.Role.Name
            })
            .ToListAsync();

        var rolesByUser = roleRows
            .GroupBy(role => role.UserId)
            .ToDictionary(
                group => group.Key,
                group => NormalizeRoles(group.Select(role => role.RoleName)));

        var assignmentsByUser = assignments
            .GroupBy(assignment => assignment.UserId)
            .ToDictionary(group => group.Key, group => group.ToList());

        var territoryAssignmentsByUser = territoryAssignments
            .GroupBy(assignment => assignment.UserId)
            .ToDictionary(group => group.Key, group => group.ToList());

        var territoryRegionCount = await _db.Regions
            .AsNoTracking()
            .CountAsync(region => region.TerritoryId == activeTerritory.Id);

        return users.Select(user =>
        {
            var userAssignments = assignmentsByUser.GetValueOrDefault(user.UserId, new List<AssignmentRow>());
            var currentTerritoryAssignments = userAssignments
                .Where(assignment => assignment.TerritoryId == activeTerritory.Id)
                .ToList();
            var activeAssignments = currentTerritoryAssignments
                .Where(assignment => assignment.IsActive)
                .OrderBy(assignment => assignment.RegionName)
                .ThenBy(assignment => assignment.RegionId)
                .ToList();
            var userTerritoryAssignments = territoryAssignmentsByUser
                .GetValueOrDefault(user.UserId, new List<TerritoryAssignmentRow>())
                .Where(assignment => assignment.TerritoryId == activeTerritory.Id)
                .OrderBy(assignment => assignment.TerritoryName)
                .ThenBy(assignment => assignment.TerritoryId)
                .ToList();
            var roles = rolesByUser.GetValueOrDefault(user.UserId, new List<string>());
            var (roleCode, roleDisplayName) = ResolvePrimaryRole(roles);
            var displayName = BuildDisplayName(user.DisplayName, user.FirstName, user.LastName, user.Username, user.Email);
            var hasPrivilegedRole =
                roles.Contains(RoleNames.Admin, StringComparer.Ordinal) ||
                roles.Contains(RoleNames.TerritoryManager, StringComparer.Ordinal);
            var canManage = !hasPrivilegedRole;
            var isSelf = user.UserId == context.CurrentUserId;
            var hasTerritoryAssociateRole = roles.Contains(RoleNames.TerritoryAssociate, StringComparer.Ordinal);
            var hasTerritoryAssociateAssignment = userTerritoryAssignments.Count > 0;
            var hasRegionalRole = roles.Contains(RoleNames.RegionalManager, StringComparer.Ordinal);
            var hasAnyRegionalScope = currentTerritoryAssignments.Count > 0;
            var isInTerritoryScope = hasAnyRegionalScope || hasTerritoryAssociateAssignment;
            var isActive = activeAssignments.Count > 0 || hasTerritoryAssociateAssignment;
            var canEditUser = canManage && !isSelf && isInTerritoryScope;
            var canAddRegionalManagerRole = canManage &&
                !isSelf &&
                isInTerritoryScope &&
                !hasRegionalRole;
            var canAddRegion = canManage &&
                !isSelf &&
                isInTerritoryScope &&
                hasRegionalRole &&
                territoryRegionCount > activeAssignments.Select(assignment => assignment.RegionId).Distinct().Count();
            var canRemoveRegionalManagerRole = canManage &&
                context.IsTerritoryManager &&
                !isSelf &&
                isActive &&
                hasRegionalRole &&
                activeAssignments.Count > 0;
            var canRemoveRegion = canRemoveRegionalManagerRole;
            var canAddTerritoryAssociateRole = canManage &&
                !isSelf &&
                context.IsTerritoryManager &&
                !hasTerritoryAssociateAssignment;

            return new TerritoryUserListItemDto
            {
                UserId = user.UserId,
                DisplayName = displayName,
                FirstName = user.FirstName,
                LastName = user.LastName,
                Email = user.Email,
                AvatarUrl = user.AvatarUrl,
                Initials = BuildInitials(displayName, user.Email),
                RoleCode = roleCode,
                RoleDisplayName = roleDisplayName,
                Roles = roles,
                AssignedRegions = activeAssignments
                    .Select(assignment => new TerritoryUserAssignedRegionDto
                    {
                        RegionId = assignment.RegionId,
                        RegionName = assignment.RegionName,
                        DisplayName = assignment.RegionName,
                        TerritoryId = assignment.TerritoryId,
                        TerritoryName = assignment.TerritoryName,
                        AssignedAt = assignment.CreatedAt,
                        IsActive = assignment.IsActive,
                        CanRemove = canRemoveRegion
                    })
                    .ToList(),
                AssignedTerritories = userTerritoryAssignments
                    .Select(assignment => new TerritoryUserAssignedTerritoryDto
                    {
                        TerritoryId = assignment.TerritoryId,
                        TerritoryName = assignment.TerritoryName,
                        DisplayName = assignment.TerritoryName
                    })
                    .ToList(),
                Status = isActive ? "Active" : "Inactive",
                IsActive = isActive,
                IsInTerritoryScope = isInTerritoryScope,
                LastLoginAt = user.LastLoginAt,
                LastLoginDisplay = FormatLastLogin(user.LastLoginAt),
                CreatedAt = user.CreatedAt,
                CanEdit = canEditUser,
                CanEditUser = canEditUser,
                CanAddRegionalManagerRole = canAddRegionalManagerRole,
                CanAddRegion = canAddRegion,
                CanRemoveRegion = canRemoveRegion,
                CanRemoveManagerRole = canRemoveRegionalManagerRole,
                CanRemoveRegionalManagerRole = canRemoveRegionalManagerRole,
                CanAddTerritoryAssociate = canAddTerritoryAssociateRole,
                CanAddTerritoryAssociateRole = canAddTerritoryAssociateRole,
                CanRemoveTerritoryAssociateRole = canManage &&
                    !isSelf &&
                    context.IsTerritoryManager &&
                    hasTerritoryAssociateRole &&
                    hasTerritoryAssociateAssignment
            };
        }).ToList();
    }

    private async Task<List<TerritoryAssignableUserDto>> BuildAssignableRowsAsync(
        TerritoryAssignedTerritoryDto activeTerritory,
        IReadOnlyCollection<int> userIds)
    {
        var ids = userIds.Distinct().ToList();
        if (ids.Count == 0)
            return new List<TerritoryAssignableUserDto>();

        var rows = await _db.Users
            .AsNoTracking()
            .Where(user => ids.Contains(user.Id))
            .Select(user => new UserRow
            {
                UserId = user.Id,
                FirstName = user.FirstName,
                LastName = user.LastName,
                DisplayName = user.DisplayName,
                Username = user.Username,
                Email = user.Email,
                AvatarUrl = user.AvatarUrl,
                CreatedAt = user.CreatedAt,
                LastLoginAt = user.LastLoginAt
            })
            .ToListAsync();

        var roleRows = await _db.UserRoles
            .AsNoTracking()
            .Where(userRole => ids.Contains(userRole.UserId))
            .Select(userRole => new RoleRow
            {
                UserId = userRole.UserId,
                RoleName = userRole.Role.Name
            })
            .ToListAsync();

        var assignments = await _db.RegionalManagerRegions
            .AsNoTracking()
            .Where(assignment =>
                ids.Contains(assignment.UserId) &&
                assignment.IsActive &&
                assignment.Region.TerritoryId == activeTerritory.Id)
            .Select(assignment => new AssignmentRow
            {
                UserId = assignment.UserId,
                RegionId = assignment.RegionId,
                RegionName = assignment.Region.Name,
                IsActive = assignment.IsActive,
                TerritoryId = assignment.Region.TerritoryId,
                TerritoryName = assignment.Region.Territory.Name,
                CreatedAt = assignment.CreatedAt
            })
            .ToListAsync();

        var territoryAssignments = await _db.TerritoryManagerTerritories
            .AsNoTracking()
            .Where(assignment =>
                ids.Contains(assignment.UserId) &&
                assignment.TerritoryId == activeTerritory.Id)
            .Select(assignment => new TerritoryAssignmentRow
            {
                UserId = assignment.UserId,
                TerritoryId = assignment.TerritoryId,
                TerritoryName = assignment.Territory.Name
            })
            .ToListAsync();

        var rolesByUser = roleRows
            .GroupBy(role => role.UserId)
            .ToDictionary(
                group => group.Key,
                group => NormalizeRoles(group.Select(role => role.RoleName)));

        var assignmentsByUser = assignments
            .GroupBy(assignment => assignment.UserId)
            .ToDictionary(group => group.Key, group => group.ToList());

        var territoryAssignmentsByUser = territoryAssignments
            .GroupBy(assignment => assignment.UserId)
            .ToDictionary(group => group.Key, group => group.ToList());

        return rows.Select(row =>
        {
            var displayName = BuildDisplayName(row.DisplayName, row.FirstName, row.LastName, row.Username, row.Email);
            var assignedRegions = assignmentsByUser
                .GetValueOrDefault(row.UserId, new List<AssignmentRow>())
                .OrderBy(assignment => assignment.RegionName)
                .ThenBy(assignment => assignment.RegionId)
                .Select(assignment => new TerritoryUserAssignedRegionDto
                {
                    RegionId = assignment.RegionId,
                    RegionName = assignment.RegionName,
                    DisplayName = assignment.RegionName,
                    TerritoryId = assignment.TerritoryId,
                    TerritoryName = assignment.TerritoryName,
                    AssignedAt = assignment.CreatedAt,
                    IsActive = assignment.IsActive,
                    CanRemove = false
                })
                .ToList();
            var assignedTerritories = territoryAssignmentsByUser
                .GetValueOrDefault(row.UserId, new List<TerritoryAssignmentRow>())
                .OrderBy(assignment => assignment.TerritoryName)
                .ThenBy(assignment => assignment.TerritoryId)
                .Select(assignment => new TerritoryUserAssignedTerritoryDto
                {
                    TerritoryId = assignment.TerritoryId,
                    TerritoryName = assignment.TerritoryName,
                    DisplayName = assignment.TerritoryName
                })
                .ToList();

            return new TerritoryAssignableUserDto
            {
                UserId = row.UserId,
                DisplayName = displayName,
                FirstName = row.FirstName,
                LastName = row.LastName,
                Email = row.Email,
                AvatarUrl = row.AvatarUrl,
                Initials = BuildInitials(displayName, row.Email),
                Roles = rolesByUser.GetValueOrDefault(row.UserId, new List<string>()),
                AssignedRegions = assignedRegions,
                AssignedTerritories = assignedTerritories,
                IsActive = true
            };
        }).ToList();
    }

    private static IEnumerable<TerritoryUserListItemDto> ApplySort(
        IEnumerable<TerritoryUserListItemDto> rows,
        string? sort)
    {
        return NormalizeSort(sort) switch
        {
            "name-asc" => rows.OrderBy(row => row.DisplayName).ThenBy(row => row.UserId),
            "name-desc" => rows.OrderByDescending(row => row.DisplayName).ThenByDescending(row => row.UserId),
            "last-login-desc" => rows
                .OrderBy(row => row.LastLoginAt.HasValue ? 0 : 1)
                .ThenByDescending(row => row.LastLoginAt)
                .ThenBy(row => row.DisplayName),
            "last-login-asc" => rows
                .OrderBy(row => row.LastLoginAt.HasValue ? 0 : 1)
                .ThenBy(row => row.LastLoginAt)
                .ThenBy(row => row.DisplayName),
            "role" => rows.OrderBy(row => row.RoleDisplayName).ThenBy(row => row.DisplayName),
            "regions-count-desc" => rows
                .OrderByDescending(row => row.AssignedRegions.Count)
                .ThenBy(row => row.DisplayName),
            "status-active-first" => rows
                .OrderBy(row => row.IsActive ? 0 : 1)
                .ThenBy(row => row.DisplayName)
                .ThenBy(row => row.UserId),
            _ => rows
                .OrderBy(row => row.IsActive ? 0 : 1)
                .ThenBy(row => row.DisplayName)
                .ThenBy(row => row.UserId)
        };
    }

    private static string NormalizeSort(string? sort)
    {
        var normalized = (sort ?? string.Empty).Trim().ToLowerInvariant().Replace('_', '-');
        return normalized switch
        {
            "" or "status" or "active-first" or "status-active-first" => "status-active-first",
            "name" or "name-asc" => "name-asc",
            "name-desc" => "name-desc",
            "last-login" or "last-login-desc" => "last-login-desc",
            "last-login-asc" => "last-login-asc",
            "role" => "role",
            "regions-count" or "regions-count-desc" => "regions-count-desc",
            _ => "status-active-first"
        };
    }

    private static (string RoleCode, string RoleDisplayName) ResolvePrimaryRole(IReadOnlyCollection<string> roles)
    {
        if (roles.Contains(RoleNames.Admin, StringComparer.Ordinal))
            return (RoleNames.Admin, "Admin");

        if (roles.Contains(RoleNames.TerritoryManager, StringComparer.Ordinal))
            return (RoleNames.TerritoryManager, "Territory Manager");

        if (roles.Contains(RoleNames.TerritoryAssociate, StringComparer.Ordinal))
            return (RoleNames.TerritoryAssociate, "Territory Associate");

        if (roles.Contains(RoleNames.RegionalManager, StringComparer.Ordinal))
            return (RoleNames.RegionalManager, "Regional Manager");

        if (roles.Contains(RoleNames.RegisteredUser, StringComparer.Ordinal))
            return (RoleNames.RegisteredUser, "Viewer");

        return ("Viewer", "Viewer");
    }

    private static List<string> NormalizeRoles(IEnumerable<string?> roles)
    {
        return roles
            .Select(RoleNames.Normalize)
            .Where(role => !string.IsNullOrWhiteSpace(role))
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    private static string BuildDisplayName(
        string? displayName,
        string? firstName,
        string? lastName,
        string? username,
        string email)
    {
        var fullName = string.Join(" ", new[] { firstName, lastName }
            .Where(part => !string.IsNullOrWhiteSpace(part))
            .Select(part => part!.Trim()));

        if (!string.IsNullOrWhiteSpace(fullName))
            return fullName;

        if (!string.IsNullOrWhiteSpace(displayName))
            return displayName.Trim();

        return string.IsNullOrWhiteSpace(username)
            ? email
            : username.Trim();
    }

    private static string BuildInitials(string displayName, string email)
    {
        var initials = InitialsFrom(displayName);
        if (!string.IsNullOrWhiteSpace(initials))
            return initials;

        var emailPrefix = email.Split('@', StringSplitOptions.TrimEntries).FirstOrDefault() ?? string.Empty;
        initials = InitialsFrom(emailPrefix.Replace('.', ' ').Replace('_', ' ').Replace('-', ' '));
        return string.IsNullOrWhiteSpace(initials) ? "?" : initials;
    }

    private static string? InitialsFrom(string value)
    {
        var parts = value
            .Trim()
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (parts.Length == 0)
            return null;

        return string.Concat(parts.Take(2).Select(part => char.ToUpperInvariant(part[0])));
    }

    private static string FormatLastLogin(DateTime? lastLoginAt)
    {
        if (!lastLoginAt.HasValue)
            return "Never";

        var elapsed = DateTime.UtcNow - lastLoginAt.Value;
        if (elapsed.TotalMinutes < 1)
            return "Just now";

        if (elapsed.TotalHours < 1)
            return $"{Math.Max(1, (int)Math.Floor(elapsed.TotalMinutes))} min ago";

        if (elapsed.TotalDays < 1)
        {
            var hours = Math.Max(1, (int)Math.Floor(elapsed.TotalHours));
            return hours == 1 ? "1 hour ago" : $"{hours} hours ago";
        }

        if (elapsed.TotalDays < 30)
        {
            var days = Math.Max(1, (int)Math.Floor(elapsed.TotalDays));
            return days == 1 ? "1 day ago" : $"{days} days ago";
        }

        var months = Math.Max(1, (int)Math.Floor(elapsed.TotalDays / 30));
        return months == 1 ? "1 month ago" : $"{months} months ago";
    }

    private static void ValidateUpdateRequest(TerritoryUserUpdateRequestDto request)
    {
        if ((request.FirstName ?? string.Empty).Length > 100)
            throw new InvalidOperationException("First name is too long.");

        if ((request.LastName ?? string.Empty).Length > 100)
            throw new InvalidOperationException("Last name is too long.");
    }

    private static List<int> NormalizeRegionIdsToAdd(IEnumerable<int>? regionIds)
    {
        var normalized = (regionIds ?? [])
            .Distinct()
            .ToList();

        if (normalized.Count > MaxRegionIdsToAdd)
            throw new InvalidOperationException("Too many regions were selected.");

        if (normalized.Any(regionId => regionId <= 0))
            throw new InvalidOperationException("regionIds must contain positive region ids.");

        return normalized;
    }

    private sealed record TerritoryUserAdminContext(
        int CurrentUserId,
        TerritoryAssignedTerritoryDto? ActiveTerritory,
        IReadOnlyCollection<string> Roles,
        bool IsTerritoryManager,
        bool IsTerritoryAssociate);

    private sealed class UserRow
    {
        public int UserId { get; set; }
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public string? DisplayName { get; set; }
        public string Username { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string? AvatarUrl { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? LastLoginAt { get; set; }
    }

    private sealed class AssignmentRow
    {
        public int UserId { get; set; }
        public int RegionId { get; set; }
        public string RegionName { get; set; } = string.Empty;
        public bool IsActive { get; set; }
        public int TerritoryId { get; set; }
        public string TerritoryName { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
    }

    private sealed class TerritoryAssignmentRow
    {
        public int UserId { get; set; }
        public int TerritoryId { get; set; }
        public string TerritoryName { get; set; } = string.Empty;
    }

    private sealed class RoleRow
    {
        public int UserId { get; set; }
        public string RoleName { get; set; } = string.Empty;
    }
}
