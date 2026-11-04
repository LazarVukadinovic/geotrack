// Integration-test fixture only. This file is not compiled or published with the server.
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Geotrack.Application.Security;
using Geotrack.Domain.Entities.Users;
using Geotrack.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Geotrack.Server;

public static class DemoContentModeratorSeeder
{
    public const string EnabledConfigKey = "Seed:DemoContentModerator:Enabled";

    private const string ConfigSectionName = "Seed:DemoContentModerator";
    private const string DefaultEmail = "moderator@gmail.com";
    private const string DefaultPassword = "TEST-only-Fixture-9284!";
    private const string DefaultUsername = "moderator";

    public static bool IsEnabled(IHostEnvironment environment, IConfiguration configuration)
    {
        var configured = configuration.GetValue<bool?>(EnabledConfigKey);
        return configured ?? environment.IsDevelopment();
    }

    public static async Task<DemoContentModeratorSeedResult> EnsureAsync(
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

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        var now = DateTime.UtcNow;
        var hasher = new PasswordHasher<User>();
        var role = await EnsureContentModeratorRoleAsync(db, logger, cancellationToken);
        var user = await FindUserByEmailAsync(db, email, cancellationToken);

        var createdUser = false;
        var passwordUpdated = false;

        if (user is null)
        {
            user = new User
            {
                Username = await ResolveNewUsernameAsync(db, email, cancellationToken),
                Email = email,
                FirstName = "Content",
                LastName = "Moderator",
                DisplayName = "Content Moderator",
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
        else
        {
            var changed = false;
            if (PasswordNeedsReset(hasher, user, password))
            {
                user.PasswordHash = hasher.HashPassword(user, password);
                passwordUpdated = true;
                changed = true;
            }

            if (user.IsSuspended || user.IsDeleted)
            {
                user.IsSuspended = false;
                user.SuspendedAt = null;
                user.SuspendedByUserId = null;
                user.IsDeleted = false;
                user.DeletedAt = null;
                user.DeletedByUserId = null;
                changed = true;
            }

            if (changed)
            {
                user.UpdatedAt = now;
                await db.SaveChangesAsync(cancellationToken);
            }
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

        if (createdUserRole)
        {
            await db.SaveChangesAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);

        logger?.LogInformation("Ensured demo Content Moderator user {Email}.", email);

        return new DemoContentModeratorSeedResult(
            Seeded: true,
            UserId: user.Id,
            RoleId: role.Id,
            CreatedUser: createdUser,
            PasswordUpdated: passwordUpdated,
            CreatedUserRole: createdUserRole);
    }

    private static async Task<Role> EnsureContentModeratorRoleAsync(
        AppDbContext db,
        ILogger? logger,
        CancellationToken cancellationToken)
    {
        var roles = await db.Roles.ToListAsync(cancellationToken);
        var role = roles.FirstOrDefault(existing =>
            RoleNames.Normalize(existing.Name) == RoleNames.ContentModerator);

        if (role is not null)
        {
            return role;
        }

        role = new Role
        {
            Name = RoleNames.ContentModerator,
            Description = "Can moderate reported reviews and content.",
            CreatedAt = DateTime.UtcNow
        };

        db.Roles.Add(role);
        await db.SaveChangesAsync(cancellationToken);

        logger?.LogWarning("Content Moderator role was missing and was created by the demo seed.");
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
}

public sealed record DemoContentModeratorSeedResult(
    bool Seeded,
    int? UserId,
    int? RoleId,
    bool CreatedUser,
    bool PasswordUpdated,
    bool CreatedUserRole);
