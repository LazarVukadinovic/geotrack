using Geotrack.Application.DTOs.Admin;
using Geotrack.Application.Interfaces.Service;
using Geotrack.Application.Security;
using Geotrack.Application.Services;
using Geotrack.Domain.Entities.Users;
using Microsoft.EntityFrameworkCore;

namespace Geotrack.Infrastructure.Services;

public class AdminProfileService : IAdminProfileService
{
    private const int MaxFirstNameLength = 100;
    private const int MaxLastNameLength = 100;
    private const int MaxShortBioLength = 300;

    private static readonly HashSet<string> AllowedLanguages = new(StringComparer.Ordinal)
    {
        "en-US",
        "sr",
        "de",
        "fr",
    };

    private static readonly HashSet<string> AllowedTimezones = new(StringComparer.Ordinal)
    {
        "UTC",
        "Europe/Belgrade",
        "Europe/Berlin",
        "Europe/Paris",
        "America/New_York",
        "America/Los_Angeles",
    };

    private readonly AppDbContext _db;

    public AdminProfileService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<AdminProfileResponseDto> GetProfileAsync(int userId)
    {
        var user = await _db.Users
            .AsNoTracking()
            .Include(existingUser => existingUser.UserRoles)
                .ThenInclude(userRole => userRole.Role)
            .Include(existingUser => existingUser.RegionalManagerRegions)
                .ThenInclude(assignment => assignment.Region)
                    .ThenInclude(region => region.Territory)
            .Include(existingUser => existingUser.TerritoryManager)
                .ThenInclude(territoryManager => territoryManager!.TerritoryManagerTerritories)
                    .ThenInclude(assignment => assignment.Territory)
            .SingleOrDefaultAsync(existingUser => existingUser.Id == userId)
            ?? throw new KeyNotFoundException("User not found.");

        var roles = NormalizeRoles(user.UserRoles.Select(userRole => userRole.Role.Name));
        EnsureAdminRole(roles);
        var roleCode = ResolvePrimaryAdminRole(roles);

        var activeAssignments = user.RegionalManagerRegions
            .Where(assignment => assignment.IsActive)
            .OrderBy(assignment => assignment.Region.Name)
            .ThenBy(assignment => assignment.RegionId)
            .ToList();

        var activeTerritoryAssignments = (user.TerritoryManager?.TerritoryManagerTerritories
                ?? Enumerable.Empty<TerritoryManagerTerritory>())
            .OrderBy(assignment => assignment.Territory.Name)
            .ThenBy(assignment => assignment.TerritoryId)
            .ToList();

        var regionalManager = new AdminProfileRegionalManagerDto
        {
            AssignedRegions = activeAssignments
                .Select(assignment => new AdminProfileRegionDto
                {
                    Id = assignment.RegionId,
                    Name = assignment.Region.Name,
                    TerritoryName = assignment.Region.Territory.Name,
                    DisplayName = BuildRegionDisplayName(
                        assignment.Region.Name,
                        assignment.Region.Territory.Name),
                    AssignedAt = assignment.CreatedAt,
                })
                .ToList(),
            MemberSince = ResolveRegionalMemberSince(activeAssignments, user.CreatedAt),
        };

        return new AdminProfileResponseDto
        {
            User = new AdminProfileUserDto
            {
                Id = user.Id,
                FirstName = user.FirstName,
                LastName = user.LastName,
                DisplayName = user.DisplayName,
                Email = user.Email,
                ShortBio = user.ShortBio,
                AvatarUrl = user.AvatarUrl,
                AvatarVersion = AvatarVersionBuilder.From(user.AvatarUrl, user.UpdatedAt, user.CreatedAt),
                CreatedAt = user.CreatedAt,
                UpdatedAt = user.UpdatedAt,
            },
            Roles = roles,
            Role = new AdminProfileRoleDto
            {
                RoleCode = roleCode,
                RoleDisplayName = FormatRoleDisplayName(roleCode),
            },
            Assignment = BuildAssignment(
                roleCode,
                activeAssignments,
                activeTerritoryAssignments,
                user.CreatedAt),
            RegionalManager = regionalManager,
            Preferences = new AdminProfilePreferencesDto
            {
                InterfaceLanguage = NormalizeLanguageOrDefault(user.InterfaceLanguage),
                Timezone = NormalizeTimezoneOrDefault(user.Timezone),
                EmailRequestsEnabled = user.EmailRequestsEnabled,
                EmailNewReviewEnabled = user.EmailNewReviewEnabled,
            },
        };
    }

    public async Task<AdminProfileResponseDto> UpdateProfileAsync(
        int userId,
        UpdateAdminProfileRequestDto request)
    {
        ValidateUpdateRequest(request);

        var user = await _db.Users
            .Include(existingUser => existingUser.UserRoles)
                .ThenInclude(userRole => userRole.Role)
            .SingleOrDefaultAsync(existingUser => existingUser.Id == userId)
            ?? throw new KeyNotFoundException("User not found.");

        var roles = NormalizeRoles(user.UserRoles.Select(userRole => userRole.Role.Name));
        EnsureAdminRole(roles);

        user.FirstName = request.FirstName?.Trim() ?? string.Empty;
        user.LastName = request.LastName?.Trim() ?? string.Empty;
        user.ShortBio = NormalizeOptionalString(request.ShortBio);
        user.InterfaceLanguage = NormalizeLanguageOrDefault(request.InterfaceLanguage);
        user.Timezone = NormalizeTimezoneOrDefault(request.Timezone);
        user.EmailRequestsEnabled = request.EmailRequestsEnabled;
        user.EmailNewReviewEnabled = request.EmailNewReviewEnabled;
        user.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();

        return await GetProfileAsync(userId);
    }

    private static void ValidateUpdateRequest(UpdateAdminProfileRequestDto request)
    {
        if ((request.FirstName ?? string.Empty).Length > MaxFirstNameLength)
            throw new InvalidOperationException("First name is too long.");

        if ((request.LastName ?? string.Empty).Length > MaxLastNameLength)
            throw new InvalidOperationException("Last name is too long.");

        if ((request.ShortBio ?? string.Empty).Length > MaxShortBioLength)
            throw new InvalidOperationException("Short bio is too long.");

        if (!AllowedLanguages.Contains((request.InterfaceLanguage ?? string.Empty).Trim()))
            throw new InvalidOperationException("Interface language is not supported.");

        if (!AllowedTimezones.Contains((request.Timezone ?? string.Empty).Trim()))
            throw new InvalidOperationException("Timezone is not supported.");
    }

    private static List<string> NormalizeRoles(IEnumerable<string?> roles)
    {
        return roles
            .Select(RoleNames.Normalize)
            .Where(role => !string.IsNullOrWhiteSpace(role))
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    private static void EnsureAdminRole(IReadOnlyCollection<string> roles)
    {
        if (!roles.Contains(RoleNames.Admin, StringComparer.Ordinal) &&
            !roles.Contains(RoleNames.ContentModerator, StringComparer.Ordinal) &&
            !roles.Contains(RoleNames.RegionalManager, StringComparer.Ordinal) &&
            !roles.Contains(RoleNames.TerritoryManager, StringComparer.Ordinal) &&
            !roles.Contains(RoleNames.TerritoryAssociate, StringComparer.Ordinal))
            throw new UnauthorizedAccessException("Admin role is required.");
    }

    private static string ResolvePrimaryAdminRole(IReadOnlyCollection<string> roles)
    {
        if (roles.Contains(RoleNames.Admin, StringComparer.Ordinal))
            return RoleNames.Admin;

        if (roles.Contains(RoleNames.ContentModerator, StringComparer.Ordinal))
            return RoleNames.ContentModerator;

        if (roles.Contains(RoleNames.TerritoryManager, StringComparer.Ordinal))
            return RoleNames.TerritoryManager;

        if (roles.Contains(RoleNames.TerritoryAssociate, StringComparer.Ordinal))
            return RoleNames.TerritoryAssociate;

        return RoleNames.RegionalManager;
    }

    private static string FormatRoleDisplayName(string roleCode)
    {
        return roleCode switch
        {
            RoleNames.TerritoryManager => "Territory Manager",
            RoleNames.TerritoryAssociate => "Territory Associate",
            RoleNames.RegionalManager => "Regional Manager",
            RoleNames.ContentModerator => "Content Moderator",
            RoleNames.Admin => "Administrator",
            _ => roleCode
        };
    }

    private static string NormalizeLanguageOrDefault(string? value)
    {
        var normalized = (value ?? string.Empty).Trim();
        return AllowedLanguages.Contains(normalized) ? normalized : "en-US";
    }

    private static string NormalizeTimezoneOrDefault(string? value)
    {
        var normalized = (value ?? string.Empty).Trim();
        return AllowedTimezones.Contains(normalized) ? normalized : "UTC";
    }

    private static string? NormalizeOptionalString(string? value)
    {
        var normalized = value?.Trim();
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }

    private static string BuildRegionDisplayName(string regionName, string? territoryName)
    {
        return string.IsNullOrWhiteSpace(territoryName)
            ? regionName
            : regionName + ", " + territoryName.Trim();
    }

    private static AdminProfileAssignmentDto BuildAssignment(
        string roleCode,
        IReadOnlyCollection<RegionalManagerRegion> regionalAssignments,
        IReadOnlyCollection<TerritoryManagerTerritory> territoryAssignments,
        DateTime userCreatedAt)
    {
        if (roleCode == RoleNames.Admin)
        {
            return new AdminProfileAssignmentDto
            {
                AssignmentType = "System",
                SectionTitle = "System Access",
                Label = "Scope",
                DisplayValue = "Platform-wide",
                AccessLevel = "Full access",
                AssignedItems = new List<AdminProfileAssignmentItemDto>(),
                MemberSince = userCreatedAt == default ? null : userCreatedAt,
            };
        }

        if (roleCode == RoleNames.ContentModerator)
        {
            return new AdminProfileAssignmentDto
            {
                AssignmentType = "ContentModeration",
                SectionTitle = "Content Moderation Access",
                Label = "Scope",
                DisplayValue = "Content moderation",
                AccessLevel = "Review moderation",
                AssignedItems = new List<AdminProfileAssignmentItemDto>(),
                MemberSince = userCreatedAt == default ? null : userCreatedAt,
            };
        }

        if (roleCode is RoleNames.TerritoryManager or RoleNames.TerritoryAssociate)
        {
            var items = territoryAssignments
                .Select(assignment => new AdminProfileAssignmentItemDto
                {
                    Id = assignment.TerritoryId,
                    Name = assignment.Territory.Name,
                    DisplayName = assignment.Territory.Name,
                    AssignedAt = assignment.CreatedAt,
                })
                .ToList();

            return new AdminProfileAssignmentDto
            {
                AssignmentType = "Territory",
                SectionTitle = roleCode == RoleNames.TerritoryAssociate
                    ? "Territory Associate Assignment"
                    : "Territory Manager Assignment",
                Label = "Assigned Territory",
                DisplayValue = ResolveAssignmentDisplayValue(items, "No assigned territories"),
                AssignedItems = items,
                MemberSince = ResolveTerritoryMemberSince(territoryAssignments, userCreatedAt),
            };
        }

        var regionalItems = regionalAssignments
            .Select(assignment => new AdminProfileAssignmentItemDto
            {
                Id = assignment.RegionId,
                Name = assignment.Region.Name,
                DisplayName = BuildRegionDisplayName(
                    assignment.Region.Name,
                    assignment.Region.Territory.Name),
                AssignedAt = assignment.CreatedAt,
            })
            .ToList();

        return new AdminProfileAssignmentDto
        {
            AssignmentType = "Region",
            SectionTitle = "Regional Manager Assignment",
            Label = "Assigned Region",
            DisplayValue = ResolveAssignmentDisplayValue(regionalItems, "No assigned regions"),
            AssignedItems = regionalItems,
            MemberSince = ResolveRegionalMemberSince(regionalAssignments, userCreatedAt),
        };
    }

    private static string ResolveAssignmentDisplayValue(
        IReadOnlyCollection<AdminProfileAssignmentItemDto> items,
        string emptyValue)
    {
        return items.Count == 0
            ? emptyValue
            : string.Join(", ", items.Select(item => item.DisplayName));
    }

    private static DateTime? ResolveRegionalMemberSince(
        IReadOnlyCollection<RegionalManagerRegion> activeAssignments,
        DateTime userCreatedAt)
    {
        if (activeAssignments.Count > 0)
            return activeAssignments.Min(assignment => assignment.CreatedAt);

        return userCreatedAt == default ? null : userCreatedAt;
    }

    private static DateTime? ResolveTerritoryMemberSince(
        IReadOnlyCollection<TerritoryManagerTerritory> territoryAssignments,
        DateTime userCreatedAt)
    {
        if (territoryAssignments.Count > 0)
            return territoryAssignments.Min(assignment => assignment.CreatedAt);

        return userCreatedAt == default ? null : userCreatedAt;
    }
}
