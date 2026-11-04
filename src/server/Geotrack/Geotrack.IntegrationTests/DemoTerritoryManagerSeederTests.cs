using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using Geotrack.Application.DTOs.Admin;
using Geotrack.Application.DTOs.Auth;
using Geotrack.Application.Security;
using Geotrack.Domain.Entities.Users;
using Geotrack.Infrastructure;
using Geotrack.IntegrationTests.Infrastructure;
using Geotrack.Server;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Geotrack.IntegrationTests;

public sealed class DemoTerritoryManagerSeederTests
{
    [Fact]
    public async Task Development_startup_seed_creates_login_ready_demo_territory_manager()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();

        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var users = await db.Users
                .Include(user => user.UserRoles)
                    .ThenInclude(userRole => userRole.Role)
                .Include(user => user.TerritoryManager!)
                    .ThenInclude(manager => manager.TerritoryManagerTerritories)
                .Where(user => user.Email.ToLower() == "mika@gmail.com")
                .ToListAsync();

            var user = Assert.Single(users);
            Assert.NotEqual("TEST-only-Fixture-9284!", user.PasswordHash);

            var passwordResult = new PasswordHasher<User>()
                .VerifyHashedPassword(user, user.PasswordHash, "TEST-only-Fixture-9284!");
            Assert.NotEqual(PasswordVerificationResult.Failed, passwordResult);

            Assert.Contains(user.UserRoles, userRole =>
                RoleNames.Normalize(userRole.Role.Name) == RoleNames.TerritoryManager);

            var territory = await db.Territories.SingleAsync(existing => existing.Name == "Croatia");
            var serbia = await db.Territories.SingleOrDefaultAsync(existing => existing.Name == "Serbia");
            Assert.NotNull(user.TerritoryManager);
            Assert.Contains(user.TerritoryManager!.TerritoryManagerTerritories, assignment =>
                assignment.TerritoryId == territory.Id);
            if (serbia is not null)
            {
                Assert.DoesNotContain(user.TerritoryManager!.TerritoryManagerTerritories, assignment =>
                    assignment.TerritoryId == serbia.Id);
            }

            var role = await GetTerritoryManagerRoleAsync(db);
            Assert.Equal(1, await db.UserRoles.CountAsync(existing =>
                existing.UserId == user.Id && existing.RoleId == role.Id));
            Assert.Equal(1, await db.TerritoryManagers.CountAsync(existing =>
                existing.UserId == user.Id));
            Assert.Equal(1, await db.TerritoryManagerTerritories.CountAsync(existing =>
                existing.UserId == user.Id && existing.TerritoryId == territory.Id));
        }

        using var client = app.CreateClient();
        var response = await client.PostAsJsonAsync(
            "/api/admin/auth/login",
            new LoginRequestDto
            {
                Email = "mika@gmail.com",
                Password = "TEST-only-Fixture-9284!",
                RememberMe = false
            });

        response.EnsureSuccessStatusCode();

        var session = await response.Content.ReadFromJsonAsync<AdminLoginResponseDto>();
        Assert.NotNull(session);
        Assert.Equal("mika@gmail.com", session!.User.Email);
        Assert.Contains(RoleNames.TerritoryManager, session.User.Roles);
        Assert.True(session.User.CanAccessTerritoryAdmin);
        Assert.False(session.User.CanAccessRegionalAdmin);

        var token = new JwtSecurityTokenHandler().ReadJwtToken(session.AccessToken);
        var roleClaims = token.Claims
            .Where(claim => claim.Type is "role" or ClaimTypes.Role)
            .Select(claim => claim.Value)
            .ToList();

        Assert.Contains(RoleNames.TerritoryManager, roleClaims);

        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", session.AccessToken);

        var me = await client.GetFromJsonAsync<AdminAuthUserDto>("/api/admin/auth/me");
        Assert.NotNull(me);
        Assert.Equal("mika@gmail.com", me!.Email);
        Assert.Contains(RoleNames.TerritoryManager, me.Roles);
        Assert.True(me.CanAccessTerritoryAdmin);
        Assert.False(me.CanAccessRegionalAdmin);

        var regionalEndpoint = await client.GetAsync("/api/admin/regional/regions");
        Assert.Equal(HttpStatusCode.Forbidden, regionalEndpoint.StatusCode);
    }

    [Fact]
    public async Task Demo_territory_manager_seed_is_idempotent_and_repairs_seeded_user()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var configuration = BuildDemoSeedConfiguration();

        var user = await db.Users.SingleAsync(existing => existing.Email == "mika@gmail.com");
        var role = await GetTerritoryManagerRoleAsync(db);
        var territory = await db.Territories.SingleAsync(existing => existing.Name == "Croatia");
        var serbia = await db.Territories.SingleOrDefaultAsync(existing => existing.Name == "Serbia");

        user.PasswordHash = "not-a-compatible-password-hash";
        user.UpdatedAt = DateTime.UtcNow;

        var userRole = await db.UserRoles.SingleAsync(existing =>
            existing.UserId == user.Id && existing.RoleId == role.Id);
        db.UserRoles.Remove(userRole);

        var assignment = await db.TerritoryManagerTerritories.SingleAsync(existing =>
            existing.UserId == user.Id && existing.TerritoryId == territory.Id);
        db.TerritoryManagerTerritories.Remove(assignment);

        if (serbia is not null &&
            !await db.TerritoryManagerTerritories.AnyAsync(existing =>
                existing.UserId == user.Id && existing.TerritoryId == serbia.Id))
        {
            db.TerritoryManagerTerritories.Add(new TerritoryManagerTerritory
            {
                UserId = user.Id,
                TerritoryId = serbia.Id,
                CreatedAt = DateTime.UtcNow
            });
        }

        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var repaired = await DemoTerritoryManagerSeeder.EnsureAsync(db, configuration);
        var repeated = await DemoTerritoryManagerSeeder.EnsureAsync(db, configuration);

        Assert.True(repaired.Seeded);
        Assert.True(repaired.PasswordUpdated);
        Assert.True(repaired.CreatedUserRole);
        Assert.True(repaired.CreatedTerritoryAssignment);
        Assert.True(repeated.Seeded);
        Assert.False(repeated.CreatedUser);
        Assert.False(repeated.CreatedUserRole);
        Assert.False(repeated.CreatedTerritoryManager);
        Assert.False(repeated.CreatedTerritoryAssignment);

        db.ChangeTracker.Clear();
        var users = await db.Users
            .Where(existing => existing.Email.ToLower() == "mika@gmail.com")
            .ToListAsync();
        var seededUser = Assert.Single(users);
        Assert.Equal(user.Id, seededUser.Id);

        var passwordResult = new PasswordHasher<User>()
            .VerifyHashedPassword(seededUser, seededUser.PasswordHash, "TEST-only-Fixture-9284!");
        Assert.NotEqual(PasswordVerificationResult.Failed, passwordResult);

        Assert.Equal(1, await db.UserRoles.CountAsync(existing =>
            existing.UserId == seededUser.Id && existing.RoleId == role.Id));
        Assert.Equal(1, await db.TerritoryManagers.CountAsync(existing =>
            existing.UserId == seededUser.Id));
        Assert.Equal(1, await db.TerritoryManagerTerritories.CountAsync(existing =>
            existing.UserId == seededUser.Id && existing.TerritoryId == territory.Id));
        if (serbia is not null)
        {
            Assert.Equal(0, await db.TerritoryManagerTerritories.CountAsync(existing =>
                existing.UserId == seededUser.Id && existing.TerritoryId == serbia.Id));
        }
    }

    [Fact]
    public void Demo_territory_manager_seed_gate_defaults_to_development_and_requires_explicit_production_enable()
    {
        Assert.True(DemoTerritoryManagerSeeder.IsEnabled(
            new TestHostEnvironment(Environments.Development),
            new ConfigurationBuilder().Build()));

        Assert.False(DemoTerritoryManagerSeeder.IsEnabled(
            new TestHostEnvironment(Environments.Development),
            BuildConfiguration(("Seed:DemoTerritoryManager:Enabled", "false"))));

        Assert.False(DemoTerritoryManagerSeeder.IsEnabled(
            new TestHostEnvironment(Environments.Production),
            new ConfigurationBuilder().Build()));

        Assert.True(DemoTerritoryManagerSeeder.IsEnabled(
            new TestHostEnvironment(Environments.Production),
            BuildConfiguration(("Seed:DemoTerritoryManager:Enabled", "true"))));
    }

    private static IConfiguration BuildDemoSeedConfiguration() =>
        BuildConfiguration(
            ("Seed:DemoTerritoryManager:Enabled", "true"),
            ("Seed:DemoTerritoryManager:Email", "mika@gmail.com"),
            ("Seed:DemoTerritoryManager:TerritoryName", "Croatia"));

    private static async Task<Role> GetTerritoryManagerRoleAsync(AppDbContext db)
    {
        var roles = await db.Roles.ToListAsync();
        return Assert.Single(roles, role => role.Name == RoleNames.TerritoryManager);
    }

    private static IConfiguration BuildConfiguration(params (string Key, string Value)[] values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(values.ToDictionary(item => item.Key, item => (string?)item.Value))
            .Build();

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public TestHostEnvironment(string environmentName)
        {
            EnvironmentName = environmentName;
        }

        public string EnvironmentName { get; set; }
        public string ApplicationName { get; set; } = "Geotrack.IntegrationTests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
