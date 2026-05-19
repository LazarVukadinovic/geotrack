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

    private readonly AppDbContext _db;

    public TerritoryUserAdminService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<TerritoryUserListResponseDto> GetUsersAsync(
        int currentUserId,
        int? territoryId,
        string? search,
        string? sort,
        int skip,
        int take)
    {
        var activeTerritory = await GetAuthorizedTerritoryAsync(currentUserId, territoryId);
        skip = Math.Max(0, skip);
        take = Math.Clamp(take, 1, DirectoryMaxTake);

        if (activeTerritory is null)
        {
            return new TerritoryUserListResponseDto
            {
                Skip = skip,
                Take = take
            };
        }

        var assignments = _db.RegionalManagerRegions
            .AsNoTracking()
            .Where(assignment => assignment.Region.TerritoryId == activeTerritory.Id);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var normalizedSearch = search.Trim().ToLowerInvariant();
            assignments = assignments.Where(assignment =>
                assignment.User.Email.ToLower().Contains(normalizedSearch) ||
                (assignment.User.DisplayName ?? string.Empty).ToLower().Contains(normalizedSearch) ||
                assignment.User.FirstName.ToLower().Contains(normalizedSearch) ||
                assignment.User.LastName.ToLower().Contains(normalizedSearch) ||
                assignment.Region.Name.ToLower().Contains(normalizedSearch));
        }

        var userIds = await assignments
            .Select(assignment => assignment.UserId)
            .Distinct()
            .ToListAsync();

        var rows = await BuildRowsAsync(activeTerritory, userIds, currentUserId);
        var ordered = ApplySort(rows, sort).ToList();

        return new TerritoryUserListResponseDto
        {
            Territory = activeTerritory,
            Items = ordered.Skip(skip).Take(take).ToList(),
            TotalCount = ordered.Count,
            Skip = skip,
            Take = take,
            HasMore = skip + take < ordered.Count
        };
    }

    public async Task<TerritoryUserListItemDto> UpdateUserAsync(
        int currentUserId,
        int targetUserId,
        TerritoryUserUpdateRequestDto request)
    {
        if (targetUserId <= 0)
            throw new InvalidOperationException("userId must be positive.");

        ValidateUpdateRequest(request);

        var activeTerritory = await GetAuthorizedTerritoryAsync(currentUserId, request.TerritoryId)
            ?? throw new UnauthorizedAccessException("Territory is not assigned to the current territory manager.");

        var user = await GetTargetUserForMutationAsync(targetUserId);
        EnsureTargetIsInTerritory(user, activeTerritory.Id);
        EnsureTargetCanBeManaged(user);

        user.FirstName = request.FirstName?.Trim() ?? string.Empty;
        user.LastName = request.LastName?.Trim() ?? string.Empty;
        user.DisplayName = NormalizeOptionalString(request.DisplayName);
        user.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();
        return await BuildSingleRowAsync(activeTerritory, targetUserId, currentUserId);
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

        var activeTerritory = await GetAuthorizedTerritoryAsync(currentUserId, territoryId)
            ?? throw new UnauthorizedAccessException("Territory is not assigned to the current territory manager.");

        var user = await GetTargetUserForMutationAsync(targetUserId);
        EnsureTargetIsInTerritory(user, activeTerritory.Id);
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

    public async Task<TerritoryUserListItemDto> AddRegionAsync(
        int currentUserId,
        int targetUserId,
        TerritoryUserAssignRegionRequestDto request)
    {
        if (targetUserId <= 0)
            throw new InvalidOperationException("userId must be positive.");

        if (request.RegionId <= 0)
            throw new InvalidOperationException("regionId must be positive.");

        var activeTerritory = await GetAuthorizedTerritoryAsync(currentUserId, request.TerritoryId)
            ?? throw new UnauthorizedAccessException("Territory is not assigned to the current territory manager.");

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
        var existingAssignment = user.RegionalManagerRegions
            .SingleOrDefault(assignment => assignment.RegionId == request.RegionId);

        if (existingAssignment is { IsActive: true })
            throw new InvalidOperationException("Region is already assigned to this user.");

        if (existingAssignment is not null)
        {
            existingAssignment.IsActive = true;
            existingAssignment.UpdatedAt = now;
        }
        else
        {
            user.RegionalManagerRegions.Add(new RegionalManagerRegion
            {
                UserId = targetUserId,
                RegionId = request.RegionId,
                IsActive = true,
                CreatedAt = now
            });
        }

        await EnsureRegionalManagerRoleAsync(user);
        user.UpdatedAt = now;

        await _db.SaveChangesAsync();
        return await BuildSingleRowAsync(activeTerritory, targetUserId, currentUserId);
    }

    public async Task<TerritoryUserListItemDto> RemoveManagerRoleAsync(
        int currentUserId,
        int targetUserId,
        TerritoryUserRemoveManagerRoleRequestDto request)
    {
        if (targetUserId <= 0)
            throw new InvalidOperationException("userId must be positive.");

        var activeTerritory = await GetAuthorizedTerritoryAsync(currentUserId, request.TerritoryId)
            ?? throw new UnauthorizedAccessException("Territory is not assigned to the current territory manager.");

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

        return await BuildSingleRowAsync(activeTerritory, targetUserId, currentUserId);
    }

    private async Task<TerritoryAssignedTerritoryDto?> GetAuthorizedTerritoryAsync(int currentUserId, int? territoryId)
    {
        await EnsureTerritoryManagerRoleAsync(currentUserId);

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
                throw new UnauthorizedAccessException("Territory is not assigned to the current territory manager.");

            return null;
        }

        var selected = territoryId.HasValue
            ? assignedTerritories.SingleOrDefault(territory => territory.Id == territoryId.Value)
            : assignedTerritories[0];

        return selected
            ?? throw new UnauthorizedAccessException("Territory is not assigned to the current territory manager.");
    }

    private async Task EnsureTerritoryManagerRoleAsync(int currentUserId)
    {
        var roles = await _db.UserRoles
            .AsNoTracking()
            .Where(userRole => userRole.UserId == currentUserId)
            .Select(userRole => userRole.Role.Name)
            .ToListAsync();

        if (!roles.Any(role => RoleNames.Normalize(role) == RoleNames.TerritoryManager))
            throw new UnauthorizedAccessException("Territory manager role is required.");
    }

    private async Task<User> GetTargetUserForMutationAsync(int targetUserId)
    {
        return await _db.Users
            .Include(user => user.UserRoles)
                .ThenInclude(userRole => userRole.Role)
            .Include(user => user.RegionalManagerRegions)
                .ThenInclude(assignment => assignment.Region)
            .SingleOrDefaultAsync(user => user.Id == targetUserId)
            ?? throw new KeyNotFoundException("User not found.");
    }

    private static void EnsureTargetIsInTerritory(User user, int territoryId)
    {
        if (!user.RegionalManagerRegions.Any(assignment => assignment.Region.TerritoryId == territoryId))
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

    private async Task EnsureRegionalManagerRoleAsync(User user)
    {
        if (user.UserRoles.Any(userRole => RoleNames.Normalize(userRole.Role.Name) == RoleNames.RegionalManager))
            return;

        var role = await _db.Roles.SingleOrDefaultAsync(existing => existing.Name == RoleNames.RegionalManager)
            ?? throw new InvalidOperationException("RegionalManager role does not exist.");

        user.UserRoles.Add(new UserRole
        {
            UserId = user.Id,
            RoleId = role.Id,
            Role = role
        });
    }

    private async Task<TerritoryUserListItemDto> BuildSingleRowAsync(
        TerritoryAssignedTerritoryDto activeTerritory,
        int targetUserId,
        int currentUserId)
    {
        var row = (await BuildRowsAsync(activeTerritory, [targetUserId], currentUserId)).SingleOrDefault();
        return row ?? throw new UnauthorizedAccessException("User is not assigned to this territory.");
    }

    private async Task<List<TerritoryUserListItemDto>> BuildRowsAsync(
        TerritoryAssignedTerritoryDto activeTerritory,
        IReadOnlyCollection<int> userIds,
        int currentUserId)
    {
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
                assignment.Region.TerritoryId == activeTerritory.Id)
            .Select(assignment => new AssignmentRow
            {
                UserId = assignment.UserId,
                RegionId = assignment.RegionId,
                RegionName = assignment.Region.Name,
                IsActive = assignment.IsActive
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

        var territoryRegionCount = await _db.Regions
            .AsNoTracking()
            .CountAsync(region => region.TerritoryId == activeTerritory.Id);

        return users.Select(user =>
        {
            var userAssignments = assignmentsByUser.GetValueOrDefault(user.UserId, new List<AssignmentRow>());
            var activeAssignments = userAssignments
                .Where(assignment => assignment.IsActive)
                .OrderBy(assignment => assignment.RegionName)
                .ThenBy(assignment => assignment.RegionId)
                .ToList();
            var roles = rolesByUser.GetValueOrDefault(user.UserId, new List<string>());
            var (roleCode, roleDisplayName) = ResolvePrimaryRole(roles);
            var displayName = BuildDisplayName(user.DisplayName, user.FirstName, user.LastName, user.Username, user.Email);
            var hasPrivilegedRole =
                roles.Contains(RoleNames.Admin, StringComparer.Ordinal) ||
                roles.Contains(RoleNames.TerritoryManager, StringComparer.Ordinal);
            var canManage = !hasPrivilegedRole;
            var isSelf = user.UserId == currentUserId;
            var isActive = activeAssignments.Count > 0;

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
                AssignedRegions = activeAssignments
                    .Select(assignment => new TerritoryUserAssignedRegionDto
                    {
                        RegionId = assignment.RegionId,
                        RegionName = assignment.RegionName,
                        DisplayName = assignment.RegionName
                    })
                    .ToList(),
                Status = isActive ? "Active" : "Inactive",
                IsActive = isActive,
                LastLoginAt = user.LastLoginAt,
                LastLoginDisplay = FormatLastLogin(user.LastLoginAt),
                CreatedAt = user.CreatedAt,
                CanEdit = canManage,
                CanAddRegion = canManage &&
                    !isSelf &&
                    territoryRegionCount > activeAssignments.Select(assignment => assignment.RegionId).Distinct().Count(),
                CanRemoveManagerRole = canManage &&
                    !isSelf &&
                    isActive &&
                    roles.Contains(RoleNames.RegionalManager, StringComparer.Ordinal)
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
        if (!string.IsNullOrWhiteSpace(displayName))
            return displayName.Trim();

        var fullName = string.Join(" ", new[] { firstName, lastName }
            .Where(part => !string.IsNullOrWhiteSpace(part))
            .Select(part => part!.Trim()));

        if (!string.IsNullOrWhiteSpace(fullName))
            return fullName;

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

        if ((request.DisplayName ?? string.Empty).Length > 100)
            throw new InvalidOperationException("Display name is too long.");
    }

    private static string? NormalizeOptionalString(string? value)
    {
        var normalized = value?.Trim();
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }

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
    }

    private sealed class RoleRow
    {
        public int UserId { get; set; }
        public string RoleName { get; set; } = string.Empty;
    }
}
