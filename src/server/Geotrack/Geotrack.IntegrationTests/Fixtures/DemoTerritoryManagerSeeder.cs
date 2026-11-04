// Integration-test fixture only. This file is not compiled or published with the server.
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Geotrack.Application.Security;
using Geotrack.Domain.Entities.Core;
using Geotrack.Domain.Entities.Users;
using Geotrack.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Geotrack.Server;

public static class DemoTerritoryManagerSeeder
{
    public const string EnabledConfigKey = "Seed:DemoTerritoryManager:Enabled";

    private const string ConfigSectionName = "Seed:DemoTerritoryManager";
    private const string DefaultEmail = "mika@gmail.com";
    private const string DefaultPassword = "TEST-only-Fixture-9284!";
    private const string DefaultUsername = "mika";
    private const string DefaultTerritoryName = "Croatia";

    public static bool IsEnabled(IHostEnvironment environment, IConfiguration configuration)
    {
        var configured = configuration.GetValue<bool?>(EnabledConfigKey);
        return configured ?? environment.IsDevelopment();
    }

    public static async Task<DemoTerritoryManagerSeedResult> EnsureAsync(
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
        var territoryName = string.IsNullOrWhiteSpace(section["TerritoryName"])
            ? DefaultTerritoryName
            : section["TerritoryName"]!.Trim();

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        var now = DateTime.UtcNow;
        var hasher = new PasswordHasher<User>();
        var role = await EnsureTerritoryManagerRoleAsync(db, logger, cancellationToken);
        var user = await FindUserByEmailAsync(db, email, cancellationToken);

        var createdUser = false;
        var passwordUpdated = false;

        if (user is null)
        {
            user = new User
            {
                Username = await ResolveNewUsernameAsync(db, email, cancellationToken),
                Email = email,
                FirstName = "Mika",
                LastName = "Territory Manager",
                DisplayName = "Mika",
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

        var createdTerritoryManager = false;
        var territoryManager = await db.TerritoryManagers.FindAsync(
            new object?[] { user.Id },
            cancellationToken);

        if (territoryManager is null)
        {
            db.TerritoryManagers.Add(new TerritoryManager
            {
                UserId = user.Id
            });
            createdTerritoryManager = true;
        }

        var territory = await FindTerritoryAsync(db, territoryName, cancellationToken);
        var createdTerritoryAssignment = false;
        string? skippedTerritoryAssignmentReason = null;

        if (territory is null)
        {
            skippedTerritoryAssignmentReason = $"Territory '{territoryName}' was not found.";
            logger?.LogWarning(
                "Demo Territory Manager user {Email} was seeded without a territory assignment because {Reason}",
                email,
                skippedTerritoryAssignmentReason);
        }
        else
        {
            var staleAssignments = await db.TerritoryManagerTerritories
                .Where(existing => existing.UserId == user.Id && existing.TerritoryId != territory.Id)
                .ToListAsync(cancellationToken);

            if (staleAssignments.Count > 0)
            {
                db.TerritoryManagerTerritories.RemoveRange(staleAssignments);
            }

            var hasAssignment = await db.TerritoryManagerTerritories.AnyAsync(
                existing => existing.UserId == user.Id && existing.TerritoryId == territory.Id,
                cancellationToken);

            if (!hasAssignment)
            {
                db.TerritoryManagerTerritories.Add(new TerritoryManagerTerritory
                {
                    UserId = user.Id,
                    TerritoryId = territory.Id,
                    CreatedAt = now
                });
                createdTerritoryAssignment = true;
            }
        }

        if (createdUserRole || createdTerritoryManager || createdTerritoryAssignment || db.ChangeTracker.HasChanges())
        {
            await db.SaveChangesAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);

        logger?.LogInformation(
            "Ensured demo Territory Manager user {Email}.",
            email);

        return new DemoTerritoryManagerSeedResult(
            Seeded: true,
            UserId: user.Id,
            RoleId: role.Id,
            TerritoryId: territory?.Id,
            CreatedUser: createdUser,
            PasswordUpdated: passwordUpdated,
            CreatedUserRole: createdUserRole,
            CreatedTerritoryManager: createdTerritoryManager,
            CreatedTerritoryAssignment: createdTerritoryAssignment,
            SkippedTerritoryAssignmentReason: skippedTerritoryAssignmentReason);
    }

    private static async Task<Role> EnsureTerritoryManagerRoleAsync(
        AppDbContext db,
        ILogger? logger,
        CancellationToken cancellationToken)
    {
        var role = await db.Roles.FirstOrDefaultAsync(
            existing => existing.Name == RoleNames.TerritoryManager,
            cancellationToken);

        if (role is not null)
        {
            return role;
        }

        role = new Role
        {
            Name = RoleNames.TerritoryManager,
            Description = "Can manage territories and assigned regions.",
            CreatedAt = DateTime.UtcNow
        };

        db.Roles.Add(role);
        await db.SaveChangesAsync(cancellationToken);

        logger?.LogWarning("Territory Manager role was missing and was created by the demo seed.");
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

    private static async Task<Territory?> FindTerritoryAsync(
        AppDbContext db,
        string territoryName,
        CancellationToken cancellationToken)
    {
        var candidates = new[]
            {
                territoryName,
                DefaultTerritoryName,
                "Hrvatska",
                "Republika Hrvatska",
                "Republic of Croatia"
            }
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name.Trim().ToLowerInvariant())
            .Distinct()
            .ToList();

        foreach (var candidate in candidates)
        {
            var territory = await db.Territories.FirstOrDefaultAsync(
                existing => existing.Name.ToLower() == candidate,
                cancellationToken);

            if (territory is not null)
            {
                return territory;
            }
        }

        return null;
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
}

public sealed record DemoTerritoryManagerSeedResult(
    bool Seeded,
    int? UserId,
    int? RoleId,
    int? TerritoryId,
    bool CreatedUser,
    bool PasswordUpdated,
    bool CreatedUserRole,
    bool CreatedTerritoryManager,
    bool CreatedTerritoryAssignment,
    string? SkippedTerritoryAssignmentReason);
