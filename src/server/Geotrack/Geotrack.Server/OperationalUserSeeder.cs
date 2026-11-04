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

    public static bool IsEnabled(IHostEnvironment environment, IConfiguration configuration) =>
        configuration.GetValue<bool?>(EnabledConfigKey)
        ?? configuration.GetValue<bool?>(OperationalUsersEnabledConfigKey)
        ?? false;

    public static async Task<OperationalUserSeedResult> EnsureAsync(
        AppDbContext db,
        IConfiguration configuration,
        ILogger? logger = null,
        CancellationToken cancellationToken = default)
    {
        var accounts = BootstrapAccountConfiguration.Read(configuration);
        if (accounts.Count == 0)
            return new(0, 0, 0, 0, 0, 0, 0, 0, 0, 0);

        return await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            return await EnsureCoreAsync(db, accounts, logger, cancellationToken);
        });
    }

    private static async Task<OperationalUserSeedResult> EnsureCoreAsync(
        AppDbContext db,
        IReadOnlyList<BootstrapAccountConfiguration> accounts,
        ILogger? logger,
        CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        // Serialize bootstrap across application instances, using the existing PostgreSQL database.
        await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(741926305)", cancellationToken);

        var now = DateTime.UtcNow;
        var ids = new Dictionary<string, int>();
        var croatiaId = 0;
        var croatiaRegionId = 0;
        var serbiaId = 0;
        var serbiaRegionId = 0;

        foreach (var account in accounts)
        {
            var matches = await db.Users.Include(user => user.UserRoles).ThenInclude(ur => ur.Role)
                .Where(user => user.Email.ToLower() == account.Email)
                .ToListAsync(cancellationToken);
            if (matches.Count > 0)
            {
                if (matches.Count != 1 || !matches[0].UserRoles.Any(ur => RoleNames.Normalize(ur.Role.Name) == account.Role))
                    throw new InvalidOperationException("Bootstrap account conflicts with an existing account. Resolve through authorized account management.");

                // Never reset a hash, restore a revoked assignment, confirm email, or reactivate an account.
                ids[account.Name] = matches[0].Id;
                logger?.LogInformation("Privileged bootstrap {AccountType}: {Status}.", account.Name, "ExistingAccountPreserved");
                continue;
            }

            if (await UsernameExistsAsync(db, account.Username, cancellationToken))
                throw new InvalidOperationException("Bootstrap username conflicts with an existing account.");

            var roles = await EnsureRolesAsync(db, now, logger, cancellationToken);
            var user = new User
            {
                Email = account.Email, Username = account.Username,
                FirstName = account.FirstName, LastName = account.LastName, DisplayName = account.DisplayName,
                IsEmailConfirmed = true, EmailConfirmedAt = now,
                NeedsInterestsOnboarding = false, CreatedAt = now
            };
            user.PasswordHash = new PasswordHasher<User>().HashPassword(user, account.Password);
            db.Users.Add(user);
            await db.SaveChangesAsync(cancellationToken);
            await EnsureUserRoleAsync(db, user, roles[account.Role], cancellationToken);

            if (account.Role is RoleNames.TerritoryManager or RoleNames.RegionalManager)
            {
                var isCroatia = account.Name.StartsWith("Croatia", StringComparison.Ordinal);
                var territory = await EnsureTerritoryAsync(db,
                    isCroatia ? CroatiaName : SerbiaName, isCroatia ? CroatiaIso2 : SerbiaIso2,
                    isCroatia ? CroatiaIso3 : SerbiaIso3,
                    isCroatia ? ["Croatia", "Hrvatska", "Republika Hrvatska", "Republic of Croatia"]
                        : ["Serbia", "Srbija", "Republic of Serbia", "Republika Srbija"],
                    now, logger, cancellationToken);
                if (isCroatia) croatiaId = territory.Id; else serbiaId = territory.Id;

                if (account.Role == RoleNames.TerritoryManager)
                    await EnsureTerritoryManagerAsync(db, user, roles[account.Role], territory, now, cancellationToken);
                else
                {
                    var region = await EnsureRegionAsync(db, territory,
                        isCroatia ? CroatiaSeedRegionName : SerbiaSeedRegionName,
                        isCroatia ? ["Dalmatia", "Dalmacija", "Dubrovnik Neretva", "Dubrovnik-Neretva"]
                            : ["Sumadija District", "Šumadija District", "Sumadija", "Šumadija"],
                        now, logger, cancellationToken);
                    if (isCroatia) croatiaRegionId = region.Id; else serbiaRegionId = region.Id;
                    await EnsureRegionalManagerAsync(db, user, roles[account.Role], region, now, cancellationToken);
                }
            }
            ids[account.Name] = user.Id;
            logger?.LogInformation("Privileged bootstrap {AccountType}: {Status}.", account.Name, "Created");
        }

        await transaction.CommitAsync(cancellationToken);
        return new(ids.GetValueOrDefault("Admin"), ids.GetValueOrDefault("ContentModerator"),
            ids.GetValueOrDefault("CroatiaTerritoryManager"), ids.GetValueOrDefault("CroatiaRegionalManager"),
            ids.GetValueOrDefault("SerbiaTerritoryManager"), ids.GetValueOrDefault("SerbiaRegionalManager"),
            croatiaId, croatiaRegionId, serbiaId, serbiaRegionId);
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

    private static string NormalizeLookupValue(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Trim().ToLowerInvariant();

    private sealed record OperationalRoleDefinition(string Name, string Description);

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
