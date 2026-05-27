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
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Geotrack.IntegrationTests;

public sealed class DemoContentModeratorSeederTests
{
    [Fact]
    public async Task Development_startup_seed_creates_login_ready_demo_content_moderator()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();

        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var users = await db.Users
                .Include(user => user.UserRoles)
                    .ThenInclude(userRole => userRole.Role)
                .Where(user => user.Email.ToLower() == "moderator@gmail.com")
                .ToListAsync();

            var user = Assert.Single(users);
            Assert.Equal("Content", user.FirstName);
            Assert.Equal("Moderator", user.LastName);
            Assert.Equal("Content Moderator", user.DisplayName);
            Assert.False(user.IsSuspended);
            Assert.False(user.IsDeleted);
            Assert.False(user.NeedsInterestsOnboarding);
            Assert.NotEqual("Proba123!", user.PasswordHash);

            var passwordResult = new PasswordHasher<User>()
                .VerifyHashedPassword(user, user.PasswordHash, "Proba123!");
            Assert.NotEqual(PasswordVerificationResult.Failed, passwordResult);

            Assert.Contains(user.UserRoles, userRole =>
                RoleNames.Normalize(userRole.Role.Name) == RoleNames.ContentModerator);

            var role = await GetContentModeratorRoleAsync(db);
            Assert.Equal(1, await db.UserRoles.CountAsync(existing =>
                existing.UserId == user.Id && existing.RoleId == role.Id));
        }

        using var client = app.CreateClient();
        var response = await client.PostAsJsonAsync(
            "/api/admin/auth/login",
            new LoginRequestDto
            {
                Email = "moderator@gmail.com",
                Password = "Proba123!",
                RememberMe = false
            });

        response.EnsureSuccessStatusCode();

        var session = await response.Content.ReadFromJsonAsync<AdminLoginResponseDto>();
        Assert.NotNull(session);
        Assert.Equal("moderator@gmail.com", session!.User.Email);
        Assert.Contains(RoleNames.ContentModerator, session.User.Roles);
        Assert.True(session.User.CanAccessContentModeration);
        Assert.False(session.User.CanAccessRegionalAdmin);
        Assert.False(session.User.CanAccessTerritoryAdmin);

        var token = new JwtSecurityTokenHandler().ReadJwtToken(session.AccessToken);
        var roleClaims = token.Claims
            .Where(claim => claim.Type is "role" or ClaimTypes.Role)
            .Select(claim => claim.Value)
            .ToList();

        Assert.Contains(RoleNames.ContentModerator, roleClaims);

        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", session.AccessToken);

        var me = await client.GetFromJsonAsync<AdminAuthUserDto>("/api/admin/auth/me");
        Assert.NotNull(me);
        Assert.Equal("moderator@gmail.com", me!.Email);
        Assert.Contains(RoleNames.ContentModerator, me.Roles);

        var moderatorDashboard = await client.GetAsync("/api/admin/content-moderator/dashboard");
        Assert.Equal(HttpStatusCode.OK, moderatorDashboard.StatusCode);

        var reviewManagement = await client.GetAsync("/api/admin/reviews");
        Assert.Equal(HttpStatusCode.OK, reviewManagement.StatusCode);

        var adminDashboard = await client.GetAsync("/api/admin/dashboard");
        Assert.Equal(HttpStatusCode.Forbidden, adminDashboard.StatusCode);

        var territories = await client.GetAsync("/api/admin/territories");
        Assert.Equal(HttpStatusCode.Forbidden, territories.StatusCode);

        var usersEndpoint = await client.GetAsync("/api/admin/users");
        Assert.Equal(HttpStatusCode.Forbidden, usersEndpoint.StatusCode);

        var supportTickets = await client.GetAsync("/api/admin/support-tickets");
        Assert.Equal(HttpStatusCode.Forbidden, supportTickets.StatusCode);
    }

    [Fact]
    public async Task Demo_content_moderator_seed_is_idempotent_and_repairs_password_role_and_active_state()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var configuration = BuildDemoSeedConfiguration();

        var user = await db.Users.SingleAsync(existing => existing.Email == "moderator@gmail.com");
        var role = await GetContentModeratorRoleAsync(db);

        user.FirstName = "Existing";
        user.LastName = "Moderator";
        user.DisplayName = "Existing Moderator";
        user.PasswordHash = "not-a-compatible-password-hash";
        user.IsSuspended = true;
        user.SuspendedAt = DateTime.UtcNow;
        user.IsDeleted = true;
        user.DeletedAt = DateTime.UtcNow;
        user.UpdatedAt = DateTime.UtcNow;

        var userRole = await db.UserRoles.SingleAsync(existing =>
            existing.UserId == user.Id && existing.RoleId == role.Id);
        db.UserRoles.Remove(userRole);

        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var repaired = await DemoContentModeratorSeeder.EnsureAsync(db, configuration);
        var repeated = await DemoContentModeratorSeeder.EnsureAsync(db, configuration);

        Assert.True(repaired.Seeded);
        Assert.True(repaired.PasswordUpdated);
        Assert.True(repaired.CreatedUserRole);
        Assert.True(repeated.Seeded);
        Assert.False(repeated.CreatedUser);
        Assert.False(repeated.CreatedUserRole);

        db.ChangeTracker.Clear();
        var users = await db.Users
            .Where(existing => existing.Email.ToLower() == "moderator@gmail.com")
            .ToListAsync();
        var seededUser = Assert.Single(users);
        Assert.Equal(user.Id, seededUser.Id);
        Assert.Equal("Existing", seededUser.FirstName);
        Assert.Equal("Moderator", seededUser.LastName);
        Assert.Equal("Existing Moderator", seededUser.DisplayName);
        Assert.False(seededUser.IsSuspended);
        Assert.False(seededUser.IsDeleted);

        var passwordResult = new PasswordHasher<User>()
            .VerifyHashedPassword(seededUser, seededUser.PasswordHash, "Proba123!");
        Assert.NotEqual(PasswordVerificationResult.Failed, passwordResult);

        Assert.Equal(1, await db.UserRoles.CountAsync(existing =>
            existing.UserId == seededUser.Id && existing.RoleId == role.Id));
    }

    [Fact]
    public void Demo_content_moderator_seed_gate_defaults_to_development_and_requires_explicit_production_enable()
    {
        Assert.True(DemoContentModeratorSeeder.IsEnabled(
            new TestHostEnvironment(Environments.Development),
            new ConfigurationBuilder().Build()));

        Assert.False(DemoContentModeratorSeeder.IsEnabled(
            new TestHostEnvironment(Environments.Development),
            BuildConfiguration(("Seed:DemoContentModerator:Enabled", "false"))));

        Assert.False(DemoContentModeratorSeeder.IsEnabled(
            new TestHostEnvironment(Environments.Production),
            new ConfigurationBuilder().Build()));

        Assert.True(DemoContentModeratorSeeder.IsEnabled(
            new TestHostEnvironment(Environments.Production),
            BuildConfiguration(("Seed:DemoContentModerator:Enabled", "true"))));
    }

    private static IConfiguration BuildDemoSeedConfiguration() =>
        BuildConfiguration(
            ("Seed:DemoContentModerator:Enabled", "true"),
            ("Seed:DemoContentModerator:Email", "moderator@gmail.com"));

    private static async Task<Role> GetContentModeratorRoleAsync(AppDbContext db)
    {
        var roles = await db.Roles.ToListAsync();
        return Assert.Single(roles, role =>
            RoleNames.Normalize(role.Name) == RoleNames.ContentModerator);
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
