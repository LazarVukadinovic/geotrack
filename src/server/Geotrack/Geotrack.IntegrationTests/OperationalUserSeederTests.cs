using System.Net.Http.Json;
using Geotrack.Application.DTOs.Admin;
using Geotrack.Application.DTOs.Auth;
using Geotrack.Application.Security;
using Geotrack.Domain.Entities.Users;
using Geotrack.Infrastructure;
using Geotrack.IntegrationTests.Infrastructure;
using Geotrack.Server;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Geotrack.IntegrationTests;

public sealed class OperationalUserSeederTests
{
    private const string AdminEmail = "zika@gmail.com";
    private const string ContentModeratorEmail = "moderator@gmail.com";
    private const string CroatiaTerritoryManagerEmail = "mika@gmail.com";
    private const string CroatiaRegionalManagerEmail = "pera@gmail.com";
    private const string SerbiaTerritoryManagerEmail = "nikola@gmail.com";
    private const string SerbiaRegionalManagerEmail = "serbia.regional@gmail.com";
    private const string SeedPassword = "Proba123!";

    [Fact]
    public async Task Development_startup_operational_seed_creates_required_users_roles_and_assignments()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();

        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        await AssertRequiredOperationalStateAsync(db);
        Assert.True(await db.Destinations.AnyAsync());
        Assert.True(await db.Users.AnyAsync(user => user.Email == "marko.p@example.com"));
        Assert.True(await db.Users.AnyAsync(user => user.Email == "elena@example.com"));
    }

    [Fact]
    public async Task Production_startup_operational_seed_creates_required_users_without_demo_content()
    {
        await using var app = await GeotrackApiFactory.CreateAsync(Environments.Production);

        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            await AssertRequiredOperationalStateAsync(db);
            Assert.False(await db.Destinations.AnyAsync());
            Assert.False(await db.Places.AnyAsync());
            Assert.False(await db.Events.AnyAsync());
            Assert.False(await db.Users.AnyAsync(user => user.Email == "marko.p@example.com"));
            Assert.False(await db.Users.AnyAsync(user => user.Email == "elena@example.com"));
        }

        await AssertAdminLoginAsync(app, AdminEmail, RoleNames.Admin);
        await AssertAdminLoginAsync(app, ContentModeratorEmail, RoleNames.ContentModerator);
        await AssertAdminLoginAsync(app, CroatiaTerritoryManagerEmail, RoleNames.TerritoryManager);
        await AssertAdminLoginAsync(app, CroatiaRegionalManagerEmail, RoleNames.RegionalManager);
        await AssertAdminLoginAsync(app, SerbiaTerritoryManagerEmail, RoleNames.TerritoryManager);
        await AssertAdminLoginAsync(app, SerbiaRegionalManagerEmail, RoleNames.RegionalManager);
    }

    [Fact]
    public async Task Production_operational_seed_is_idempotent_and_preserves_existing_profile_and_password()
    {
        await using var app = await GeotrackApiFactory.CreateAsync(Environments.Production);

        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var configuration = scope.ServiceProvider.GetRequiredService<IConfiguration>();
        var logger = scope.ServiceProvider
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger(nameof(OperationalUserSeederTests));

        var hasher = new PasswordHasher<User>();
        var admin = await db.Users.SingleAsync(user => user.Email == AdminEmail);
        admin.FirstName = "Existing";
        admin.LastName = "Administrator";
        admin.DisplayName = "Existing Administrator";
        admin.PasswordHash = hasher.HashPassword(admin, "Existing123!");
        admin.IsSuspended = true;
        admin.SuspendedAt = DateTime.UtcNow;
        admin.IsDeleted = true;
        admin.DeletedAt = DateTime.UtcNow;
        admin.NeedsInterestsOnboarding = true;

        var adminRole = await db.Roles.SingleAsync(role => role.Name == RoleNames.Admin);
        var adminRoleAssignment = await db.UserRoles.SingleAsync(userRole =>
            userRole.UserId == admin.Id &&
            userRole.RoleId == adminRole.Id);
        db.UserRoles.Remove(adminRoleAssignment);

        var croatia = await db.Territories.SingleAsync(territory => territory.CountryIso3 == "HRV");
        var serbia = await db.Territories.SingleAsync(territory => territory.CountryIso3 == "SRB");
        var croatiaManager = await db.Users.SingleAsync(user => user.Email == CroatiaTerritoryManagerEmail);
        var croatiaAssignment = await db.TerritoryManagerTerritories.SingleAsync(assignment =>
            assignment.UserId == croatiaManager.Id &&
            assignment.TerritoryId == croatia.Id);
        db.TerritoryManagerTerritories.Remove(croatiaAssignment);

        if (!await db.TerritoryManagerTerritories.AnyAsync(assignment =>
                assignment.UserId == croatiaManager.Id &&
                assignment.TerritoryId == serbia.Id))
        {
            db.TerritoryManagerTerritories.Add(new TerritoryManagerTerritory
            {
                UserId = croatiaManager.Id,
                TerritoryId = serbia.Id,
                CreatedAt = DateTime.UtcNow
            });
        }

        var dubrovnik = await db.Regions.SingleAsync(region =>
            region.TerritoryId == croatia.Id &&
            region.Name == "Dubrovnik-Neretva");
        var regionalManager = await db.Users.SingleAsync(user => user.Email == CroatiaRegionalManagerEmail);
        var regionAssignment = await db.RegionalManagerRegions.SingleAsync(assignment =>
            assignment.UserId == regionalManager.Id &&
            assignment.RegionId == dubrovnik.Id);
        regionAssignment.IsActive = false;
        regionAssignment.UpdatedAt = DateTime.UtcNow;

        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        await OperationalUserSeeder.EnsureAsync(db, configuration, logger);
        await OperationalUserSeeder.EnsureAsync(db, configuration, logger);

        db.ChangeTracker.Clear();

        admin = await db.Users.SingleAsync(user => user.Email == AdminEmail);
        Assert.Equal("Existing", admin.FirstName);
        Assert.Equal("Administrator", admin.LastName);
        Assert.Equal("Existing Administrator", admin.DisplayName);
        Assert.False(admin.IsSuspended);
        Assert.False(admin.IsDeleted);
        Assert.False(admin.NeedsInterestsOnboarding);
        Assert.NotEqual(PasswordVerificationResult.Failed,
            hasher.VerifyHashedPassword(admin, admin.PasswordHash, "Existing123!"));
        Assert.Equal(PasswordVerificationResult.Failed,
            hasher.VerifyHashedPassword(admin, admin.PasswordHash, SeedPassword));

        await AssertRequiredOperationalStateAsync(db);
        Assert.Equal(1, await db.UserRoles.CountAsync(userRole =>
            userRole.UserId == admin.Id &&
            userRole.RoleId == adminRole.Id));
        Assert.Equal(1, await db.TerritoryManagerTerritories.CountAsync(assignment =>
            assignment.UserId == croatiaManager.Id &&
            assignment.TerritoryId == croatia.Id));
        Assert.Equal(1, await db.TerritoryManagerTerritories.CountAsync(assignment =>
            assignment.UserId == croatiaManager.Id &&
            assignment.TerritoryId == serbia.Id));
        Assert.Equal(1, await db.RegionalManagerRegions.CountAsync(assignment =>
            assignment.UserId == regionalManager.Id &&
            assignment.RegionId == dubrovnik.Id &&
            assignment.IsActive));
    }

    private static async Task AssertRequiredOperationalStateAsync(AppDbContext db)
    {
        var roleNames = await db.Roles
            .Select(role => role.Name)
            .ToListAsync();

        Assert.Contains(roleNames, role => RoleNames.Normalize(role) == RoleNames.Admin);
        Assert.Contains(roleNames, role => RoleNames.Normalize(role) == RoleNames.ContentModerator);
        Assert.Contains(roleNames, role => RoleNames.Normalize(role) == RoleNames.TerritoryManager);
        Assert.Contains(roleNames, role => RoleNames.Normalize(role) == RoleNames.RegionalManager);
        Assert.Contains(roleNames, role => RoleNames.Normalize(role) == RoleNames.TerritoryAssociate);
        Assert.Contains(roleNames, role => RoleNames.Normalize(role) == RoleNames.RegisteredUser);

        var admin = await AssertSingleActiveUserAsync(db, AdminEmail);
        var moderator = await AssertSingleActiveUserAsync(db, ContentModeratorEmail);
        var croatiaManager = await AssertSingleActiveUserAsync(db, CroatiaTerritoryManagerEmail);
        var regionalManager = await AssertSingleActiveUserAsync(db, CroatiaRegionalManagerEmail);
        var serbiaManager = await AssertSingleActiveUserAsync(db, SerbiaTerritoryManagerEmail);
        var serbiaRegionalManager = await AssertSingleActiveUserAsync(db, SerbiaRegionalManagerEmail);

        await AssertHasSingleRoleAsync(db, admin.Id, RoleNames.Admin);
        await AssertHasSingleRoleAsync(db, moderator.Id, RoleNames.ContentModerator);
        await AssertHasSingleRoleAsync(db, croatiaManager.Id, RoleNames.TerritoryManager);
        await AssertHasSingleRoleAsync(db, regionalManager.Id, RoleNames.RegionalManager);
        await AssertHasSingleRoleAsync(db, serbiaManager.Id, RoleNames.TerritoryManager);
        await AssertHasSingleRoleAsync(db, serbiaRegionalManager.Id, RoleNames.RegionalManager);

        var croatia = await db.Territories.SingleAsync(territory => territory.CountryIso3 == "HRV");
        var serbia = await db.Territories.SingleAsync(territory => territory.CountryIso3 == "SRB");
        var dubrovnik = await db.Regions.SingleAsync(region =>
            region.TerritoryId == croatia.Id &&
            region.Name == "Dubrovnik-Neretva");
        var sumadija = await db.Regions.SingleAsync(region =>
            region.TerritoryId == serbia.Id &&
            region.Name == "Sumadija District");

        Assert.Equal(1, await db.TerritoryManagers.CountAsync(manager =>
            manager.UserId == croatiaManager.Id));
        Assert.Equal(1, await db.TerritoryManagers.CountAsync(manager =>
            manager.UserId == serbiaManager.Id));
        Assert.Equal(1, await db.TerritoryManagerTerritories.CountAsync(assignment =>
            assignment.UserId == croatiaManager.Id &&
            assignment.TerritoryId == croatia.Id));
        Assert.Equal(1, await db.TerritoryManagerTerritories.CountAsync(assignment =>
            assignment.UserId == serbiaManager.Id &&
            assignment.TerritoryId == serbia.Id));
        Assert.Equal(1, await db.RegionalManagerRegions.CountAsync(assignment =>
            assignment.UserId == regionalManager.Id &&
            assignment.RegionId == dubrovnik.Id &&
            assignment.IsActive));
        Assert.Equal(1, await db.RegionalManagerRegions.CountAsync(assignment =>
            assignment.UserId == serbiaRegionalManager.Id &&
            assignment.RegionId == sumadija.Id &&
            assignment.IsActive));
    }

    private static async Task<User> AssertSingleActiveUserAsync(AppDbContext db, string email)
    {
        var users = await db.Users
            .Where(user => user.Email.ToLower() == email)
            .ToListAsync();

        var user = Assert.Single(users);
        Assert.False(user.IsSuspended);
        Assert.False(user.IsDeleted);
        Assert.False(user.NeedsInterestsOnboarding);
        Assert.False(string.IsNullOrWhiteSpace(user.PasswordHash));
        return user;
    }

    private static async Task AssertHasSingleRoleAsync(AppDbContext db, int userId, string roleName)
    {
        var role = await db.Roles.SingleAsync(role => role.Name == roleName);
        Assert.Equal(1, await db.UserRoles.CountAsync(userRole =>
            userRole.UserId == userId &&
            userRole.RoleId == role.Id));
    }

    private static async Task AssertAdminLoginAsync(
        GeotrackApiFactory app,
        string email,
        string expectedRole)
    {
        using var client = app.CreateClient();
        var response = await client.PostAsJsonAsync(
            "/api/admin/auth/login",
            new LoginRequestDto
            {
                Email = email,
                Password = SeedPassword,
                RememberMe = false
            });

        response.EnsureSuccessStatusCode();

        var session = await response.Content.ReadFromJsonAsync<AdminLoginResponseDto>();
        Assert.NotNull(session);
        Assert.Equal(email, session!.User.Email);
        Assert.Contains(expectedRole, session.User.Roles);
    }
}
