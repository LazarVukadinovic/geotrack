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

public static class DemoAdminSeeder
{
    public const string EnabledConfigKey = "Seed:DemoAdmin:Enabled";

    private const string ConfigSectionName = "Seed:DemoAdmin";
    private const string DefaultEmail = "zika@gmail.com";
    private const string DefaultPassword = "TEST-only-Fixture-9284!";
    private const string DefaultUsername = "zika";

    public static bool IsEnabled(IHostEnvironment environment, IConfiguration configuration)
    {
        var configured = configuration.GetValue<bool?>(EnabledConfigKey);
        return configured ?? environment.IsDevelopment();
    }

    public static async Task<DemoAdminSeedResult> EnsureAsync(
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
        var role = await EnsureAdminRoleAsync(db, logger, cancellationToken);
        var user = await FindUserByEmailAsync(db, email, cancellationToken);

        var createdUser = false;
        var passwordUpdated = false;

        if (user is null)
        {
            user = new User
            {
                Username = await ResolveNewUsernameAsync(db, email, cancellationToken),
                Email = email,
                FirstName = "Zika",
                LastName = "Admin",
                DisplayName = "Zika",
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

        if (createdUserRole)
        {
            await db.SaveChangesAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);

        logger?.LogInformation("Ensured demo Admin user {Email}.", email);

        return new DemoAdminSeedResult(
            Seeded: true,
            UserId: user.Id,
            RoleId: role.Id,
            CreatedUser: createdUser,
            PasswordUpdated: passwordUpdated,
            CreatedUserRole: createdUserRole);
    }

    private static async Task<Role> EnsureAdminRoleAsync(
        AppDbContext db,
        ILogger? logger,
        CancellationToken cancellationToken)
    {
        var roles = await db.Roles.ToListAsync(cancellationToken);
        var role = roles.FirstOrDefault(existing =>
            RoleNames.Normalize(existing.Name) == RoleNames.Admin);

        if (role is not null)
        {
            return role;
        }

        role = new Role
        {
            Name = RoleNames.Admin,
            Description = "Can manage users and administrative configuration.",
            CreatedAt = DateTime.UtcNow
        };

        db.Roles.Add(role);
        await db.SaveChangesAsync(cancellationToken);

        logger?.LogWarning("Admin role was missing and was created by the demo seed.");
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

public sealed record DemoAdminSeedResult(
    bool Seeded,
    int? UserId,
    int? RoleId,
    bool CreatedUser,
    bool PasswordUpdated,
    bool CreatedUserRole);
