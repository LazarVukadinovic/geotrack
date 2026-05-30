using Geotrack.Application.Security;
using Geotrack.Domain.Entities.Core;
using Geotrack.Domain.Entities.Users;
using Geotrack.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Geotrack.Server;

public static class OperationalUserSeeder
{
    public const string EnabledConfigKey = "Seed:EnableOperationalSeed";
    public const string OperationalUsersEnabledConfigKey = "Seed:OperationalUsers:Enabled";

    private const string DefaultPassword = "Proba123!";

    private const string AdminEmail = "zika@gmail.com";
    private const string AdminUsername = "zika";

    private const string ContentModeratorEmail = "moderator@gmail.com";
    private const string ContentModeratorUsername = "moderator";

    private const string CroatiaTerritoryManagerEmail = "mika@gmail.com";
    private const string CroatiaTerritoryManagerUsername = "mika";

    private const string CroatiaRegionalManagerEmail = "pera@gmail.com";
    private const string CroatiaRegionalManagerUsername = "pera";

    private const string SerbiaTerritoryManagerEmail = "nikola@gmail.com";
    private const string SerbiaTerritoryManagerUsername = "nikola";
    private const string SerbiaRegionalManagerEmail = "serbia.regional@gmail.com";
    private const string SerbiaRegionalManagerUsername = "serbia-regional";

    private const string CroatiaName = "Croatia";
    private const string CroatiaIso2 = "HR";
    private const string CroatiaIso3 = "HRV";
    private const string CroatiaSeedRegionName = "Dubrovnik-Neretva";

    private const string SerbiaName = "Serbia";
    private const string SerbiaIso2 = "RS";
    private const string SerbiaIso3 = "SRB";
    private const string SerbiaSeedRegionName = "Sumadija District";

    private static readonly OperationalRoleDefinition[] RoleDefinitions =
    [
        new(RoleNames.Admin, "Can manage users and administrative configuration."),
        new(RoleNames.ContentModerator, "Can moderate reported reviews and content."),
        new(RoleNames.RegionalManager, "Can review and approve regional user contributions."),
        new(RoleNames.TerritoryManager, "Can manage territories and assigned regions."),
        new(RoleNames.TerritoryAssociate, "Can manage assigned territories except associate assignment and protected role removal."),
        new("Publisher", "Can publish and manage assigned places."),
        new(RoleNames.RegisteredUser, "Regular authenticated user.")
    ];

    public static bool IsEnabled(IHostEnvironment environment, IConfiguration configuration)
    {
        var configured =
            configuration.GetValue<bool?>(EnabledConfigKey)
            ?? configuration.GetValue<bool?>(OperationalUsersEnabledConfigKey);

        return configured ?? environment.IsDevelopment();
    }

    public static async Task<OperationalUserSeedResult> EnsureAsync(
        AppDbContext db,
        IConfiguration configuration,
        ILogger? logger = null,
        CancellationToken cancellationToken = default)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        var now = DateTime.UtcNow;
        var roles = await EnsureRolesAsync(db, now, logger, cancellationToken);

        var croatia = await EnsureTerritoryAsync(
            db,
            CroatiaName,
            CroatiaIso2,
            CroatiaIso3,
            ["Croatia", "Hrvatska", "Republika Hrvatska", "Republic of Croatia"],
            now,
            logger,
            cancellationToken);

        var dubrovnikNeretva = await EnsureRegionAsync(
            db,
            croatia,
            CroatiaSeedRegionName,
            ["Dalmatia", "Dalmacija", "Dubrovnik Neretva", "Dubrovnik-Neretva"],
            now,
            logger,
            cancellationToken);

        var serbia = await EnsureTerritoryAsync(
            db,
            SerbiaName,
            SerbiaIso2,
            SerbiaIso3,
            ["Serbia", "Srbija", "Republic of Serbia", "Republika Srbija"],
            now,
            logger,
            cancellationToken);

        var sumadija = await EnsureRegionAsync(
            db,
            serbia,
            SerbiaSeedRegionName,
            ["Sumadija District", "Šumadija District", "Sumadija", "Šumadija"],
            now,
            logger,
            cancellationToken);

        var adminUser = await EnsureUserAsync(
            db,
            ReadConfiguredValue(configuration, "Seed:OperationalUsers:Admin", "Seed:DemoAdmin", "Email", AdminEmail),
            ReadConfiguredValue(configuration, "Seed:OperationalUsers:Admin", "Seed:DemoAdmin", "Username", AdminUsername),
            "Zika",
            "Admin",
            "Zika",
            ReadConfiguredValue(configuration, "Seed:OperationalUsers:Admin", "Seed:DemoAdmin", "Password", DefaultPassword),
            location: null,
            now,
            cancellationToken);
        await EnsureUserRoleAsync(db, adminUser.User, roles[RoleNames.Admin], cancellationToken);
        logger?.LogInformation("Ensured operational Admin user {Email}.", adminUser.User.Email);

        var contentModeratorUser = await EnsureUserAsync(
            db,
            ReadConfiguredValue(configuration, "Seed:OperationalUsers:ContentModerator", "Seed:DemoContentModerator", "Email", ContentModeratorEmail),
            ReadConfiguredValue(configuration, "Seed:OperationalUsers:ContentModerator", "Seed:DemoContentModerator", "Username", ContentModeratorUsername),
            "Content",
            "Moderator",
            "Content Moderator",
            ReadConfiguredValue(configuration, "Seed:OperationalUsers:ContentModerator", "Seed:DemoContentModerator", "Password", DefaultPassword),
            location: null,
            now,
            cancellationToken);
        await EnsureUserRoleAsync(db, contentModeratorUser.User, roles[RoleNames.ContentModerator], cancellationToken);
        logger?.LogInformation(
            "Ensured operational Content Moderator user {Email}.",
            contentModeratorUser.User.Email);

        var croatiaTerritoryManagerUser = await EnsureUserAsync(
            db,
            ReadConfiguredValue(configuration, "Seed:OperationalUsers:CroatiaTerritoryManager", "Seed:DemoTerritoryManager", "Email", CroatiaTerritoryManagerEmail),
            ReadConfiguredValue(configuration, "Seed:OperationalUsers:CroatiaTerritoryManager", "Seed:DemoTerritoryManager", "Username", CroatiaTerritoryManagerUsername),
            "Mika",
            "Territory Manager",
            "Mika",
            ReadConfiguredValue(configuration, "Seed:OperationalUsers:CroatiaTerritoryManager", "Seed:DemoTerritoryManager", "Password", DefaultPassword),
            CroatiaName,
            now,
            cancellationToken);
        await EnsureTerritoryManagerAsync(
            db,
            croatiaTerritoryManagerUser.User,
            roles[RoleNames.TerritoryManager],
            croatia,
            now,
            cancellationToken);
        logger?.LogInformation(
            "Ensured operational Territory Manager {Email} assigned to {TerritoryName}.",
            croatiaTerritoryManagerUser.User.Email,
            croatia.Name);

        var croatiaRegionalManagerUser = await EnsureUserAsync(
            db,
            ReadConfiguredValue(configuration, "Seed:OperationalUsers:CroatiaRegionalManager", "Seed:DemoRegionalManager", "Email", CroatiaRegionalManagerEmail),
            ReadConfiguredValue(configuration, "Seed:OperationalUsers:CroatiaRegionalManager", "Seed:DemoRegionalManager", "Username", CroatiaRegionalManagerUsername),
            "Pera",
            "Regional Manager",
            "Pera",
            ReadConfiguredValue(configuration, "Seed:OperationalUsers:CroatiaRegionalManager", "Seed:DemoRegionalManager", "Password", DefaultPassword),
            dubrovnikNeretva.Name,
            now,
            cancellationToken);
        await EnsureRegionalManagerAsync(
            db,
            croatiaRegionalManagerUser.User,
            roles[RoleNames.RegionalManager],
            dubrovnikNeretva,
            now,
            cancellationToken);
        logger?.LogInformation(
            "Ensured operational Regional Manager {Email} assigned to {RegionName}.",
            croatiaRegionalManagerUser.User.Email,
            dubrovnikNeretva.Name);

        var serbiaTerritoryManagerUser = await EnsureUserAsync(
            db,
            ReadConfiguredValue(configuration, "Seed:OperationalUsers:SerbiaTerritoryManager", "Seed:SerbiaTerritoryManager", "Email", SerbiaTerritoryManagerEmail),
            ReadConfiguredValue(configuration, "Seed:OperationalUsers:SerbiaTerritoryManager", "Seed:SerbiaTerritoryManager", "Username", SerbiaTerritoryManagerUsername),
            "Nikola",
            "Territory Manager",
            "Nikola Territory Manager",
            ReadConfiguredValue(configuration, "Seed:OperationalUsers:SerbiaTerritoryManager", "Seed:SerbiaTerritoryManager", "Password", DefaultPassword),
            SerbiaName,
            now,
            cancellationToken);
        await EnsureTerritoryManagerAsync(
            db,
            serbiaTerritoryManagerUser.User,
            roles[RoleNames.TerritoryManager],
            serbia,
            now,
            cancellationToken);
        logger?.LogInformation(
            "Ensured operational Territory Manager {Email} assigned to {TerritoryName}.",
            serbiaTerritoryManagerUser.User.Email,
            serbia.Name);

        var serbiaRegionalManagerUser = await EnsureUserAsync(
            db,
            ReadConfiguredValue(configuration, "Seed:OperationalUsers:SerbiaRegionalManager", "Seed:SerbiaRegionalManager", "Email", SerbiaRegionalManagerEmail),
            ReadConfiguredValue(configuration, "Seed:OperationalUsers:SerbiaRegionalManager", "Seed:SerbiaRegionalManager", "Username", SerbiaRegionalManagerUsername),
            "Serbia",
            "Regional Manager",
            "Serbia Regional Manager",
            ReadConfiguredValue(configuration, "Seed:OperationalUsers:SerbiaRegionalManager", "Seed:SerbiaRegionalManager", "Password", DefaultPassword),
            sumadija.Name,
            now,
            cancellationToken);
        await EnsureRegionalManagerAsync(
            db,
            serbiaRegionalManagerUser.User,
            roles[RoleNames.RegionalManager],
            sumadija,
            now,
            cancellationToken);
        logger?.LogInformation(
            "Ensured operational Regional Manager {Email} assigned to {RegionName}.",
            serbiaRegionalManagerUser.User.Email,
            sumadija.Name);

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return new OperationalUserSeedResult(
            AdminUserId: adminUser.User.Id,
            ContentModeratorUserId: contentModeratorUser.User.Id,
            CroatiaTerritoryManagerUserId: croatiaTerritoryManagerUser.User.Id,
            CroatiaRegionalManagerUserId: croatiaRegionalManagerUser.User.Id,
            SerbiaTerritoryManagerUserId: serbiaTerritoryManagerUser.User.Id,
            SerbiaRegionalManagerUserId: serbiaRegionalManagerUser.User.Id,
            CroatiaTerritoryId: croatia.Id,
            CroatiaRegionId: dubrovnikNeretva.Id,
            SerbiaTerritoryId: serbia.Id,
            SerbiaRegionId: sumadija.Id);
    }

    private static async Task<Dictionary<string, Role>> EnsureRolesAsync(
        AppDbContext db,
        DateTime now,
        ILogger? logger,
        CancellationToken cancellationToken)
    {
        var result = new Dictionary<string, Role>(StringComparer.Ordinal);

        foreach (var definition in RoleDefinitions)
        {
            var role = await EnsureRoleAsync(db, definition, now, logger, cancellationToken);
            result[definition.Name] = role;
        }

        return result;
    }

    private static async Task<Role> EnsureRoleAsync(
        AppDbContext db,
        OperationalRoleDefinition definition,
        DateTime now,
        ILogger? logger,
        CancellationToken cancellationToken)
    {
        var normalizedWanted = RoleNames.Normalize(definition.Name);
        var roles = await db.Roles.ToListAsync(cancellationToken);
        var role = roles.FirstOrDefault(existing =>
            string.Equals(
                RoleNames.Normalize(existing.Name),
                normalizedWanted,
                StringComparison.OrdinalIgnoreCase));

        if (role is not null)
        {
            return role;
        }

        role = new Role
        {
            Name = definition.Name,
            Description = definition.Description,
            CreatedAt = now
        };

        db.Roles.Add(role);
        await db.SaveChangesAsync(cancellationToken);

        logger?.LogInformation("Created missing operational role {RoleName}.", definition.Name);
        return role;
    }

    private static async Task<Territory> EnsureTerritoryAsync(
        AppDbContext db,
        string name,
        string countryCode,
        string countryIso3,
        IReadOnlyCollection<string> aliases,
        DateTime now,
        ILogger? logger,
        CancellationToken cancellationToken)
    {
        var territory = await db.Territories
            .FirstOrDefaultAsync(existing => existing.CountryIso3 == countryIso3, cancellationToken)
            ?? await db.Territories
                .FirstOrDefaultAsync(existing => existing.CountryCode == countryCode, cancellationToken)
            ?? await FindTerritoryByNameAsync(db, aliases, cancellationToken);

        if (territory is null)
        {
            territory = new Territory
            {
                Name = name,
                CountryCode = countryCode,
                CountryIso3 = countryIso3,
                TerritoryKind = TerritoryKinds.Country,
                CreatedAt = now
            };

            db.Territories.Add(territory);
            await db.SaveChangesAsync(cancellationToken);
            logger?.LogInformation("Created missing operational territory {TerritoryName}.", name);
            return territory;
        }

        var changed = false;

        if (string.IsNullOrWhiteSpace(territory.Name))
        {
            territory.Name = name;
            changed = true;
        }

        if (string.IsNullOrWhiteSpace(territory.CountryCode))
        {
            territory.CountryCode = countryCode;
            changed = true;
        }
        else if (!string.Equals(territory.CountryCode, countryCode, StringComparison.OrdinalIgnoreCase))
        {
            logger?.LogWarning(
                "Operational territory {TerritoryName} matched {ExpectedIso3}, but CountryCode is {ActualCountryCode}.",
                territory.Name,
                countryIso3,
                territory.CountryCode);
        }

        if (string.IsNullOrWhiteSpace(territory.CountryIso3))
        {
            territory.CountryIso3 = countryIso3;
            changed = true;
        }
        else if (!string.Equals(territory.CountryIso3, countryIso3, StringComparison.OrdinalIgnoreCase))
        {
            logger?.LogWarning(
                "Operational territory {TerritoryName} matched {ExpectedIso3}, but CountryIso3 is {ActualIso3}.",
                territory.Name,
                countryIso3,
                territory.CountryIso3);
        }

        if (string.IsNullOrWhiteSpace(territory.TerritoryKind))
        {
            territory.TerritoryKind = TerritoryKinds.Country;
            changed = true;
        }

        if (changed)
        {
            territory.UpdatedAt = now;
            await db.SaveChangesAsync(cancellationToken);
        }

        return territory;
    }

    private static async Task<Territory?> FindTerritoryByNameAsync(
        AppDbContext db,
        IReadOnlyCollection<string> aliases,
        CancellationToken cancellationToken)
    {
        foreach (var alias in aliases.Select(NormalizeLookupValue).Where(value => value.Length > 0).Distinct())
        {
            var territory = await db.Territories.FirstOrDefaultAsync(
                existing => existing.Name.ToLower() == alias,
                cancellationToken);

            if (territory is not null)
            {
                return territory;
            }
        }

        return null;
    }

    private static async Task<Region> EnsureRegionAsync(
        AppDbContext db,
        Territory territory,
        string name,
        IReadOnlyCollection<string> aliases,
        DateTime now,
        ILogger? logger,
        CancellationToken cancellationToken)
    {
        var normalizedNames = aliases
            .Append(name)
            .Select(NormalizeLookupValue)
            .Where(value => value.Length > 0)
            .Distinct()
            .ToList();

        Region? region = null;
        foreach (var normalizedName in normalizedNames)
        {
            region = await db.Regions.FirstOrDefaultAsync(
                existing =>
                    existing.TerritoryId == territory.Id &&
                    existing.Name.ToLower() == normalizedName,
                cancellationToken);

            if (region is not null)
            {
                break;
            }
        }

        if (region is null)
        {
            region = new Region
            {
                Name = name,
                TerritoryId = territory.Id,
                CreatedAt = now
            };

            db.Regions.Add(region);
            await db.SaveChangesAsync(cancellationToken);
            logger?.LogInformation(
                "Created missing operational region {RegionName} in {TerritoryName}.",
                name,
                territory.Name);
            return region;
        }

        if (!string.Equals(region.Name, name, StringComparison.Ordinal) &&
            !await db.Regions.AnyAsync(
                existing =>
                    existing.TerritoryId == territory.Id &&
                    existing.Name == name,
                cancellationToken))
        {
            region.Name = name;
            region.UpdatedAt = now;
            await db.SaveChangesAsync(cancellationToken);
        }

        return region;
    }

    private static async Task<OperationalUserEnsureResult> EnsureUserAsync(
        AppDbContext db,
        string email,
        string username,
        string firstName,
        string lastName,
        string displayName,
        string password,
        string? location,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var normalizedEmail = NormalizeEmail(email);
        var hasher = new PasswordHasher<User>();
        var user = await FindUserByEmailAsync(db, normalizedEmail, cancellationToken);
        var created = false;
        var passwordInitialized = false;

        if (user is null)
        {
            user = new User
            {
                Username = await ResolveUsernameAsync(db, username, normalizedEmail, cancellationToken),
                Email = normalizedEmail,
                FirstName = firstName,
                LastName = lastName,
                DisplayName = displayName,
                PhoneNumber = string.Empty,
                Location = location,
                NeedsInterestsOnboarding = false,
                CreatedAt = now
            };

            user.PasswordHash = hasher.HashPassword(user, password);
            db.Users.Add(user);
            await db.SaveChangesAsync(cancellationToken);
            created = true;
            passwordInitialized = true;

            return new OperationalUserEnsureResult(user, created, passwordInitialized);
        }

        var changed = false;

        if (string.IsNullOrWhiteSpace(user.PasswordHash))
        {
            user.PasswordHash = hasher.HashPassword(user, password);
            passwordInitialized = true;
            changed = true;
        }

        if (user.IsSuspended)
        {
            user.IsSuspended = false;
            user.SuspendedAt = null;
            user.SuspendedByUserId = null;
            changed = true;
        }

        if (user.IsDeleted)
        {
            user.IsDeleted = false;
            user.DeletedAt = null;
            user.DeletedByUserId = null;
            changed = true;
        }

        if (user.NeedsInterestsOnboarding)
        {
            user.NeedsInterestsOnboarding = false;
            changed = true;
        }

        if (string.IsNullOrWhiteSpace(user.FirstName))
        {
            user.FirstName = firstName;
            changed = true;
        }

        if (string.IsNullOrWhiteSpace(user.LastName))
        {
            user.LastName = lastName;
            changed = true;
        }

        if (string.IsNullOrWhiteSpace(user.DisplayName))
        {
            user.DisplayName = displayName;
            changed = true;
        }

        if (string.IsNullOrWhiteSpace(user.Location) && !string.IsNullOrWhiteSpace(location))
        {
            user.Location = location;
            changed = true;
        }

        if (changed)
        {
            user.UpdatedAt = now;
            await db.SaveChangesAsync(cancellationToken);
        }

        return new OperationalUserEnsureResult(user, created, passwordInitialized);
    }

    private static async Task<User?> FindUserByEmailAsync(
        AppDbContext db,
        string normalizedEmail,
        CancellationToken cancellationToken)
    {
        var matches = await db.Users
            .Where(existing => existing.Email.ToLower() == normalizedEmail)
            .OrderBy(existing => existing.Id)
            .ToListAsync(cancellationToken);

        return matches.FirstOrDefault(existing =>
                string.Equals(existing.Email, normalizedEmail, StringComparison.Ordinal))
            ?? matches.FirstOrDefault();
    }

    private static async Task<string> ResolveUsernameAsync(
        AppDbContext db,
        string preferredUsername,
        string normalizedEmail,
        CancellationToken cancellationToken)
    {
        var normalizedUsername = string.IsNullOrWhiteSpace(preferredUsername)
            ? normalizedEmail
            : preferredUsername.Trim();

        if (!await UsernameExistsAsync(db, normalizedUsername, cancellationToken))
        {
            return normalizedUsername;
        }

        if (!await UsernameExistsAsync(db, normalizedEmail, cancellationToken))
        {
            return normalizedEmail;
        }

        for (var suffix = 2; suffix < 100; suffix++)
        {
            var candidate = $"{normalizedUsername}-{suffix}";
            if (!await UsernameExistsAsync(db, candidate, cancellationToken))
            {
                return candidate;
            }
        }

        return $"{normalizedUsername}-{Guid.NewGuid():N}";
    }

    private static Task<bool> UsernameExistsAsync(
        AppDbContext db,
        string username,
        CancellationToken cancellationToken)
    {
        var normalized = NormalizeLookupValue(username);
        return db.Users.AnyAsync(existing => existing.Username.ToLower() == normalized, cancellationToken);
    }

    private static async Task<bool> EnsureUserRoleAsync(
        AppDbContext db,
        User user,
        Role role,
        CancellationToken cancellationToken)
    {
        if (await db.UserRoles.AnyAsync(
                existing => existing.UserId == user.Id && existing.RoleId == role.Id,
                cancellationToken))
        {
            return false;
        }

        db.UserRoles.Add(new UserRole
        {
            UserId = user.Id,
            RoleId = role.Id
        });

        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    private static async Task EnsureTerritoryManagerAsync(
        AppDbContext db,
        User user,
        Role territoryManagerRole,
        Territory territory,
        DateTime now,
        CancellationToken cancellationToken)
    {
        await EnsureUserRoleAsync(db, user, territoryManagerRole, cancellationToken);

        if (!await db.TerritoryManagers.AnyAsync(
                existing => existing.UserId == user.Id,
                cancellationToken))
        {
            db.TerritoryManagers.Add(new TerritoryManager
            {
                UserId = user.Id
            });
            await db.SaveChangesAsync(cancellationToken);
        }

        if (!await db.TerritoryManagerTerritories.AnyAsync(
                existing =>
                    existing.UserId == user.Id &&
                    existing.TerritoryId == territory.Id,
                cancellationToken))
        {
            db.TerritoryManagerTerritories.Add(new TerritoryManagerTerritory
            {
                UserId = user.Id,
                TerritoryId = territory.Id,
                CreatedAt = now
            });
            await db.SaveChangesAsync(cancellationToken);
        }
    }

    private static async Task EnsureRegionalManagerAsync(
        AppDbContext db,
        User user,
        Role regionalManagerRole,
        Region region,
        DateTime now,
        CancellationToken cancellationToken)
    {
        await EnsureUserRoleAsync(db, user, regionalManagerRole, cancellationToken);

        var assignment = await db.RegionalManagerRegions.FindAsync(
            new object?[] { user.Id, region.Id },
            cancellationToken);

        if (assignment is null)
        {
            db.RegionalManagerRegions.Add(new RegionalManagerRegion
            {
                UserId = user.Id,
                RegionId = region.Id,
                IsActive = true,
                CreatedAt = now
            });
            await db.SaveChangesAsync(cancellationToken);
            return;
        }

        if (!assignment.IsActive)
        {
            assignment.IsActive = true;
            assignment.UpdatedAt = now;
            await db.SaveChangesAsync(cancellationToken);
        }
    }

    private static string ReadConfiguredValue(
        IConfiguration configuration,
        string operationalSectionName,
        string legacySectionName,
        string key,
        string fallback)
    {
        var operationalValue = configuration.GetSection(operationalSectionName)[key];
        if (!string.IsNullOrWhiteSpace(operationalValue))
        {
            return operationalValue.Trim();
        }

        var legacyValue = configuration.GetSection(legacySectionName)[key];
        if (!string.IsNullOrWhiteSpace(legacyValue))
        {
            return legacyValue.Trim();
        }

        return fallback;
    }

    private static string NormalizeEmail(string email)
    {
        return string.IsNullOrWhiteSpace(email)
            ? throw new InvalidOperationException("Operational seed user email is required.")
            : email.Trim().ToLowerInvariant();
    }

    private static string NormalizeLookupValue(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Trim().ToLowerInvariant();

    private sealed record OperationalRoleDefinition(string Name, string Description);

    private sealed record OperationalUserEnsureResult(User User, bool Created, bool PasswordInitialized);
}

public sealed record OperationalUserSeedResult(
    int AdminUserId,
    int ContentModeratorUserId,
    int CroatiaTerritoryManagerUserId,
    int CroatiaRegionalManagerUserId,
    int SerbiaTerritoryManagerUserId,
    int SerbiaRegionalManagerUserId,
    int CroatiaTerritoryId,
    int CroatiaRegionId,
    int SerbiaTerritoryId,
    int SerbiaRegionId);
