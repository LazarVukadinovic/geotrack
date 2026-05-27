using Geotrack.Application.Security;
using Geotrack.Domain.Entities.Users;
using Geotrack.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Geotrack.Server;

public static class DemoRegionalManagerSeeder
{
    public const string EnabledConfigKey = "Seed:DemoRegionalManager:Enabled";

    private const string ConfigSectionName = "Seed:DemoRegionalManager";
    private const string DefaultEmail = "pera@gmail.com";
    private const string DefaultPassword = "Proba123!";
    private const string DefaultUsername = "pera";
    private const int DefaultRegionId = 1;

    public static bool IsEnabled(IHostEnvironment environment, IConfiguration configuration)
    {
        var configured = configuration.GetValue<bool?>(EnabledConfigKey);
        return configured ?? environment.IsDevelopment();
    }

    public static async Task<DemoRegionalManagerSeedResult> EnsureAsync(
        AppDbContext db,
        IConfiguration configuration,
        ILogger? logger = null,
        CancellationToken cancellationToken = default)
    {
        var section = configuration.GetSection(ConfigSectionName);
        var email = NormalizeEmail(section["Email"] ?? DefaultEmail);
        var password = string.IsNullOrWhiteSpace(section["Password"])
            ? DefaultPassword
            : section["Password"]!.Trim();
        var regionId = section.GetValue<int?>("RegionId") ?? DefaultRegionId;

        var region = await db.Regions
            .AsNoTracking()
            .SingleOrDefaultAsync(existing => existing.Id == regionId, cancellationToken);

        if (region is null)
        {
            logger?.LogWarning(
                "Skipping demo Regional Manager seed because configured RegionId {RegionId} does not exist.",
                regionId);
            return DemoRegionalManagerSeedResult.Skipped(regionId, "Configured region was not found.");
        }

        if (!IsExpectedDemoRegionName(region.Name))
        {
            logger?.LogWarning(
                "Demo Regional Manager seed is using configured RegionId {RegionId}, but the region name is {RegionName}.",
                regionId,
                region.Name);
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        var now = DateTime.UtcNow;
        var hasher = new PasswordHasher<User>();
        var role = await EnsureRegionalManagerRoleAsync(db, logger, cancellationToken);
        var user = await FindUserByEmailAsync(db, email, cancellationToken);

        var createdUser = false;
        var passwordUpdated = false;

        if (user is null)
        {
            user = new User
            {
                Username = await ResolveNewUsernameAsync(db, email, cancellationToken),
                Email = email,
                FirstName = "Pera",
                LastName = "Regional Manager",
                DisplayName = "Pera",
                PhoneNumber = string.Empty,
                NeedsInterestsOnboarding = false,
                CreatedAt = now
            };
            user.PasswordHash = hasher.HashPassword(user, password);

            db.Users.Add(user);
            await db.SaveChangesAsync(cancellationToken);
            createdUser = true;
            passwordUpdated = true;
        }
        else if (PasswordNeedsReset(hasher, user, password))
        {
            user.PasswordHash = hasher.HashPassword(user, password);
            user.UpdatedAt = now;
            await db.SaveChangesAsync(cancellationToken);
            passwordUpdated = true;
        }

        var createdUserRole = false;
        var hasRole = await db.UserRoles.AnyAsync(
            existing => existing.UserId == user.Id && existing.RoleId == role.Id,
            cancellationToken);

        if (!hasRole)
        {
            db.UserRoles.Add(new UserRole
            {
                UserId = user.Id,
                RoleId = role.Id
            });
            createdUserRole = true;
        }

        var createdRegionAssignment = false;
        var reactivatedRegionAssignment = false;
        var assignment = await db.RegionalManagerRegions.FindAsync(
            new object?[] { user.Id, regionId },
            cancellationToken);

        if (assignment is null)
        {
            db.RegionalManagerRegions.Add(new RegionalManagerRegion
            {
                UserId = user.Id,
                RegionId = regionId,
                IsActive = true,
                CreatedAt = now
            });
            createdRegionAssignment = true;
        }
        else if (!assignment.IsActive)
        {
            assignment.IsActive = true;
            assignment.UpdatedAt = now;
            reactivatedRegionAssignment = true;
        }

        if (createdUserRole || createdRegionAssignment || reactivatedRegionAssignment)
        {
            await db.SaveChangesAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);

        logger?.LogInformation(
            "Ensured demo Regional Manager user {Email} for RegionId {RegionId}.",
            email,
            regionId);

        return new DemoRegionalManagerSeedResult(
            Seeded: true,
            UserId: user.Id,
            RoleId: role.Id,
            RegionId: regionId,
            CreatedUser: createdUser,
            PasswordUpdated: passwordUpdated,
            CreatedUserRole: createdUserRole,
            CreatedRegionAssignment: createdRegionAssignment,
            ReactivatedRegionAssignment: reactivatedRegionAssignment,
            SkippedReason: null);
    }

    private static async Task<Role> EnsureRegionalManagerRoleAsync(
        AppDbContext db,
        ILogger? logger,
        CancellationToken cancellationToken)
    {
        var roles = await db.Roles.ToListAsync(cancellationToken);
        var role = roles.FirstOrDefault(existing =>
            RoleNames.Normalize(existing.Name) == RoleNames.RegionalManager);

        if (role is not null)
        {
            return role;
        }

        role = new Role
        {
            Name = RoleNames.RegionalManager,
            Description = "Can review and approve regional user contributions.",
            CreatedAt = DateTime.UtcNow
        };

        db.Roles.Add(role);
        await db.SaveChangesAsync(cancellationToken);

        logger?.LogWarning("Regional Manager role was missing and was created by the demo seed.");
        return role;
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

        return matches.FirstOrDefault(existing => existing.Email == normalizedEmail)
            ?? matches.FirstOrDefault();
    }

    private static async Task<string> ResolveNewUsernameAsync(
        AppDbContext db,
        string email,
        CancellationToken cancellationToken)
    {
        if (!await UsernameExistsAsync(db, DefaultUsername, cancellationToken))
        {
            return DefaultUsername;
        }

        if (!await UsernameExistsAsync(db, email, cancellationToken))
        {
            return email;
        }

        for (var suffix = 2; suffix < 100; suffix++)
        {
            var candidate = $"{DefaultUsername}-{suffix}";
            if (!await UsernameExistsAsync(db, candidate, cancellationToken))
            {
                return candidate;
            }
        }

        return $"{DefaultUsername}-{Guid.NewGuid():N}";
    }

    private static Task<bool> UsernameExistsAsync(
        AppDbContext db,
        string username,
        CancellationToken cancellationToken)
    {
        var normalized = username.Trim().ToLowerInvariant();
        return db.Users.AnyAsync(existing => existing.Username.ToLower() == normalized, cancellationToken);
    }

    private static bool PasswordNeedsReset(PasswordHasher<User> hasher, User user, string password)
    {
        if (string.IsNullOrWhiteSpace(user.PasswordHash))
        {
            return true;
        }

        try
        {
            return hasher.VerifyHashedPassword(user, user.PasswordHash, password) != PasswordVerificationResult.Success;
        }
        catch (ArgumentException)
        {
            return true;
        }
        catch (FormatException)
        {
            return true;
        }
    }

    private static string NormalizeEmail(string email)
    {
        return string.IsNullOrWhiteSpace(email)
            ? DefaultEmail
            : email.Trim().ToLowerInvariant();
    }

    private static bool IsExpectedDemoRegionName(string name)
    {
        var normalized = name.Trim().ToLowerInvariant();
        return normalized is "dubrovnik-neretva" or "dubrovnik neretva" or "dalmatia" or "dalmacija";
    }
}

public sealed record DemoRegionalManagerSeedResult(
    bool Seeded,
    int? UserId,
    int? RoleId,
    int RegionId,
    bool CreatedUser,
    bool PasswordUpdated,
    bool CreatedUserRole,
    bool CreatedRegionAssignment,
    bool ReactivatedRegionAssignment,
    string? SkippedReason)
{
    public static DemoRegionalManagerSeedResult Skipped(int regionId, string reason) =>
        new(
            Seeded: false,
            UserId: null,
            RoleId: null,
            RegionId: regionId,
            CreatedUser: false,
            PasswordUpdated: false,
            CreatedUserRole: false,
            CreatedRegionAssignment: false,
            ReactivatedRegionAssignment: false,
            SkippedReason: reason);
}
