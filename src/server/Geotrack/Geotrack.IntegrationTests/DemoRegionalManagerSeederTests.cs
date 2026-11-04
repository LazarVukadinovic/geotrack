using System.Net.Http.Headers;
using System.Net.Http.Json;
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

public sealed class DemoRegionalManagerSeederTests
{
    private const string SeedRegionName = "Dubrovnik-Neretva";

    [Fact]
    public async Task Development_startup_seed_creates_login_ready_demo_regional_manager()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();

        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var users = await db.Users
                .Include(user => user.UserRoles)
                    .ThenInclude(userRole => userRole.Role)
                .Include(user => user.RegionalManagerRegions)
                .Where(user => user.Email.ToLower() == "pera@gmail.com")
                .ToListAsync();

            var user = Assert.Single(users);
            Assert.NotEqual("TEST-only-Fixture-9284!", user.PasswordHash);

            var passwordResult = new PasswordHasher<User>()
                .VerifyHashedPassword(user, user.PasswordHash, "TEST-only-Fixture-9284!");
            Assert.NotEqual(PasswordVerificationResult.Failed, passwordResult);

            Assert.Contains(user.UserRoles, userRole =>
                RoleNames.Normalize(userRole.Role.Name) == RoleNames.RegionalManager);
            Assert.Contains(user.RegionalManagerRegions, assignment =>
                assignment.RegionId == 1 && assignment.IsActive);

            var region = await db.Regions.SingleAsync(existing => existing.Id == 1);
            Assert.Equal(SeedRegionName, region.Name);

            var role = await GetRegionalManagerRoleAsync(db);
            Assert.Equal(1, await db.UserRoles.CountAsync(existing =>
                existing.UserId == user.Id && existing.RoleId == role.Id));
            Assert.Equal(1, await db.RegionalManagerRegions.CountAsync(existing =>
                existing.UserId == user.Id && existing.RegionId == 1));
        }

        using var client = app.CreateClient();
        var response = await client.PostAsJsonAsync(
            "/api/admin/auth/login",
            new LoginRequestDto
            {
                Email = "pera@gmail.com",
                Password = "TEST-only-Fixture-9284!",
                RememberMe = false
            });

        response.EnsureSuccessStatusCode();

        var session = await response.Content.ReadFromJsonAsync<AdminLoginResponseDto>();
        Assert.NotNull(session);
        Assert.Equal("pera@gmail.com", session!.User.Email);
        Assert.Contains(RoleNames.RegionalManager, session.User.Roles);

        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", session.AccessToken);

        var regions = await client.GetFromJsonAsync<List<RegionalAssignedRegionDto>>(
            "/api/admin/regional/regions");
        Assert.NotNull(regions);
        Assert.Contains(regions!, region => region.Id == 1 && region.Name == SeedRegionName);
    }

    [Fact]
    public async Task Demo_regional_manager_seed_is_idempotent_and_repairs_seeded_user()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var configuration = BuildDemoSeedConfiguration();

        var user = await db.Users.SingleAsync(existing => existing.Email == "pera@gmail.com");
        var role = await GetRegionalManagerRoleAsync(db);
        var assignment = await db.RegionalManagerRegions.SingleAsync(existing =>
            existing.UserId == user.Id && existing.RegionId == 1);

        user.PasswordHash = "not-a-compatible-password-hash";
        user.UpdatedAt = DateTime.UtcNow;
        assignment.IsActive = false;
        assignment.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var repaired = await DemoRegionalManagerSeeder.EnsureAsync(db, configuration);
        var repeated = await DemoRegionalManagerSeeder.EnsureAsync(db, configuration);

        Assert.True(repaired.Seeded);
        Assert.True(repaired.PasswordUpdated);
        Assert.True(repaired.ReactivatedRegionAssignment);
        Assert.True(repeated.Seeded);
        Assert.False(repeated.CreatedUser);
        Assert.False(repeated.CreatedUserRole);
        Assert.False(repeated.CreatedRegionAssignment);
        Assert.False(repeated.ReactivatedRegionAssignment);

        db.ChangeTracker.Clear();
        var users = await db.Users
            .Where(existing => existing.Email.ToLower() == "pera@gmail.com")
            .ToListAsync();
        var seededUser = Assert.Single(users);
        Assert.Equal(user.Id, seededUser.Id);

        var passwordResult = new PasswordHasher<User>()
            .VerifyHashedPassword(seededUser, seededUser.PasswordHash, "TEST-only-Fixture-9284!");
        Assert.NotEqual(PasswordVerificationResult.Failed, passwordResult);

        Assert.Equal(1, await db.UserRoles.CountAsync(existing =>
            existing.UserId == seededUser.Id && existing.RoleId == role.Id));

        var assignments = await db.RegionalManagerRegions
            .Where(existing => existing.UserId == seededUser.Id && existing.RegionId == 1)
            .ToListAsync();
        var seededAssignment = Assert.Single(assignments);
        Assert.True(seededAssignment.IsActive);
    }

    [Fact]
    public void Demo_regional_manager_seed_gate_defaults_to_development_and_requires_explicit_production_enable()
    {
        Assert.True(DemoRegionalManagerSeeder.IsEnabled(
            new TestHostEnvironment(Environments.Development),
            new ConfigurationBuilder().Build()));

        Assert.False(DemoRegionalManagerSeeder.IsEnabled(
            new TestHostEnvironment(Environments.Development),
            BuildConfiguration(("Seed:DemoRegionalManager:Enabled", "false"))));

        Assert.False(DemoRegionalManagerSeeder.IsEnabled(
            new TestHostEnvironment(Environments.Production),
            new ConfigurationBuilder().Build()));

        Assert.True(DemoRegionalManagerSeeder.IsEnabled(
            new TestHostEnvironment(Environments.Production),
            BuildConfiguration(("Seed:DemoRegionalManager:Enabled", "true"))));
    }

    private static IConfiguration BuildDemoSeedConfiguration() =>
        BuildConfiguration(
            ("Seed:DemoRegionalManager:Enabled", "true"),
            ("Seed:DemoRegionalManager:Email", "pera@gmail.com"),
            ("Seed:DemoRegionalManager:RegionId", "1"));

    private static async Task<Role> GetRegionalManagerRoleAsync(AppDbContext db)
    {
        var roles = await db.Roles.ToListAsync();
        return Assert.Single(roles, role =>
            RoleNames.Normalize(role.Name) == RoleNames.RegionalManager);
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
