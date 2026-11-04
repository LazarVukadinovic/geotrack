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

public sealed class DemoAdminSeederTests
{
    [Fact]
    public async Task Development_startup_seed_creates_login_ready_demo_admin()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();

        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var users = await db.Users
                .Include(user => user.UserRoles)
                    .ThenInclude(userRole => userRole.Role)
                .Where(user => user.Email.ToLower() == "zika@gmail.com")
                .ToListAsync();

            var user = Assert.Single(users);
            Assert.Equal("Zika", user.FirstName);
            Assert.Equal("Admin", user.LastName);
            Assert.Equal("Zika", user.DisplayName);
            Assert.False(user.NeedsInterestsOnboarding);
            Assert.NotEqual("TEST-only-Fixture-9284!", user.PasswordHash);

            var passwordResult = new PasswordHasher<User>()
                .VerifyHashedPassword(user, user.PasswordHash, "TEST-only-Fixture-9284!");
            Assert.NotEqual(PasswordVerificationResult.Failed, passwordResult);

            Assert.Contains(user.UserRoles, userRole =>
                RoleNames.Normalize(userRole.Role.Name) == RoleNames.Admin);

            var role = await GetAdminRoleAsync(db);
            Assert.Equal(1, await db.UserRoles.CountAsync(existing =>
                existing.UserId == user.Id && existing.RoleId == role.Id));
        }

        using var client = app.CreateClient();
        var response = await client.PostAsJsonAsync(
            "/api/admin/auth/login",
            new LoginRequestDto
            {
                Email = "zika@gmail.com",
                Password = "TEST-only-Fixture-9284!",
                RememberMe = false
            });

        response.EnsureSuccessStatusCode();

        var session = await response.Content.ReadFromJsonAsync<AdminLoginResponseDto>();
        Assert.NotNull(session);
        Assert.Equal("zika@gmail.com", session!.User.Email);
        Assert.Contains(RoleNames.Admin, session.User.Roles);
        Assert.False(session.User.CanAccessRegionalAdmin);
        Assert.False(session.User.CanAccessTerritoryAdmin);

        var token = new JwtSecurityTokenHandler().ReadJwtToken(session.AccessToken);
        var roleClaims = token.Claims
            .Where(claim => claim.Type is "role" or ClaimTypes.Role)
            .Select(claim => claim.Value)
            .ToList();

        Assert.Contains(RoleNames.Admin, roleClaims);

        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", session.AccessToken);

        var me = await client.GetFromJsonAsync<AdminAuthUserDto>("/api/admin/auth/me");
        Assert.NotNull(me);
        Assert.Equal("zika@gmail.com", me!.Email);
        Assert.Contains(RoleNames.Admin, me.Roles);

        var regionalEndpoint = await client.GetAsync("/api/admin/regional/regions");
        Assert.Equal(HttpStatusCode.Forbidden, regionalEndpoint.StatusCode);
    }

    [Fact]
    public async Task Demo_admin_seed_is_idempotent_and_repairs_password_and_role_only()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var configuration = BuildDemoSeedConfiguration();

        var user = await db.Users.SingleAsync(existing => existing.Email == "zika@gmail.com");
        var role = await GetAdminRoleAsync(db);

        user.FirstName = "Existing";
        user.LastName = "Person";
        user.DisplayName = "Existing Person";
        user.PasswordHash = "not-a-compatible-password-hash";
        user.UpdatedAt = DateTime.UtcNow;

        var userRole = await db.UserRoles.SingleAsync(existing =>
            existing.UserId == user.Id && existing.RoleId == role.Id);
        db.UserRoles.Remove(userRole);

        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var repaired = await DemoAdminSeeder.EnsureAsync(db, configuration);
        var repeated = await DemoAdminSeeder.EnsureAsync(db, configuration);

        Assert.True(repaired.Seeded);
        Assert.True(repaired.PasswordUpdated);
        Assert.True(repaired.CreatedUserRole);
        Assert.True(repeated.Seeded);
        Assert.False(repeated.CreatedUser);
        Assert.False(repeated.CreatedUserRole);

        db.ChangeTracker.Clear();
        var users = await db.Users
            .Where(existing => existing.Email.ToLower() == "zika@gmail.com")
            .ToListAsync();
        var seededUser = Assert.Single(users);
        Assert.Equal(user.Id, seededUser.Id);
        Assert.Equal("Existing", seededUser.FirstName);
        Assert.Equal("Person", seededUser.LastName);
        Assert.Equal("Existing Person", seededUser.DisplayName);

        var passwordResult = new PasswordHasher<User>()
            .VerifyHashedPassword(seededUser, seededUser.PasswordHash, "TEST-only-Fixture-9284!");
        Assert.NotEqual(PasswordVerificationResult.Failed, passwordResult);

        Assert.Equal(1, await db.UserRoles.CountAsync(existing =>
            existing.UserId == seededUser.Id && existing.RoleId == role.Id));
    }

    [Fact]
    public void Demo_admin_seed_gate_defaults_to_development_and_requires_explicit_production_enable()
    {
        Assert.True(DemoAdminSeeder.IsEnabled(
            new TestHostEnvironment(Environments.Development),
            new ConfigurationBuilder().Build()));

        Assert.False(DemoAdminSeeder.IsEnabled(
            new TestHostEnvironment(Environments.Development),
            BuildConfiguration(("Seed:DemoAdmin:Enabled", "false"))));

        Assert.False(DemoAdminSeeder.IsEnabled(
            new TestHostEnvironment(Environments.Production),
            new ConfigurationBuilder().Build()));

        Assert.True(DemoAdminSeeder.IsEnabled(
            new TestHostEnvironment(Environments.Production),
            BuildConfiguration(("Seed:DemoAdmin:Enabled", "true"))));
    }

    private static IConfiguration BuildDemoSeedConfiguration() =>
        BuildConfiguration(
            ("Seed:DemoAdmin:Enabled", "true"),
            ("Seed:DemoAdmin:Email", "zika@gmail.com"));

    private static async Task<Role> GetAdminRoleAsync(AppDbContext db)
    {
        var roles = await db.Roles.ToListAsync();
        return Assert.Single(roles, role =>
            RoleNames.Normalize(role.Name) == RoleNames.Admin);
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
