using Geotrack.Application.DTOs.Admin;
using Geotrack.Application.Interfaces.Service;
using Geotrack.Application.Security;
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
            .SingleOrDefaultAsync(existingUser => existingUser.Id == userId)
            ?? throw new KeyNotFoundException("User not found.");

        var roles = NormalizeRoles(user.UserRoles.Select(userRole => userRole.Role.Name));
        EnsureRegionalManagerRole(roles);

        var activeAssignments = user.RegionalManagerRegions
            .Where(assignment => assignment.IsActive)
            .OrderBy(assignment => assignment.Region.Name)
            .ThenBy(assignment => assignment.RegionId)
            .ToList();

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
                CreatedAt = user.CreatedAt,
                UpdatedAt = user.UpdatedAt,
            },
            Roles = roles,
            RegionalManager = new AdminProfileRegionalManagerDto
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
                MemberSince = ResolveMemberSince(activeAssignments, user.CreatedAt),
            },
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
        EnsureRegionalManagerRole(roles);

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

    private static void EnsureRegionalManagerRole(IReadOnlyCollection<string> roles)
    {
        if (!roles.Contains(RoleNames.RegionalManager, StringComparer.Ordinal))
            throw new UnauthorizedAccessException("Regional manager role is required.");
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

    private static DateTime? ResolveMemberSince(
        IReadOnlyCollection<Geotrack.Domain.Entities.Users.RegionalManagerRegion> activeAssignments,
        DateTime userCreatedAt)
    {
        if (activeAssignments.Count > 0)
            return activeAssignments.Min(assignment => assignment.CreatedAt);

        return userCreatedAt == default ? null : userCreatedAt;
    }
}
