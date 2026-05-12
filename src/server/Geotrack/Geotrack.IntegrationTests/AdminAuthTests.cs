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
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Geotrack.IntegrationTests;

public sealed class AdminAuthTests
{
    [Fact]
    public async Task Regional_manager_login_succeeds_and_issues_role_claims_from_user_roles()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        await GrantRegionalManagerRoleAsync(app, "elena@example.com");

        using var client = app.CreateClient();
        var response = await client.PostAsJsonAsync(
            "/api/admin/auth/login",
            new LoginRequestDto
            {
                Email = "elena@example.com",
                Password = "password123",
                RememberMe = false
            });

        response.EnsureSuccessStatusCode();
        Assert.True(response.Headers.TryGetValues("Set-Cookie", out var cookies));
        Assert.Contains(cookies, value => value.Contains("geotrack_admin_refresh=", StringComparison.Ordinal));

        var session = await response.Content.ReadFromJsonAsync<AdminLoginResponseDto>();
        Assert.NotNull(session);
        Assert.False(string.IsNullOrWhiteSpace(session.AccessToken));
        Assert.True(session.ExpiresAt > DateTime.UtcNow);
        Assert.Equal("elena@example.com", session.User.Email);
        Assert.True(session.User.CanAccessRegionalAdmin);
        Assert.Contains(RoleNames.RegionalManager, session.User.Roles);

        var token = new JwtSecurityTokenHandler().ReadJwtToken(session.AccessToken);
        var roleClaims = token.Claims
            .Where(claim => claim.Type is "role" or ClaimTypes.Role)
            .Select(claim => claim.Value)
            .ToList();

        Assert.Contains(RoleNames.RegionalManager, roleClaims);
    }

    [Fact]
    public async Task Admin_login_rejects_invalid_credentials_without_revealing_account_existence()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        using var client = app.CreateClient();

        var wrongPassword = await client.PostAsJsonAsync(
            "/api/admin/auth/login",
            new LoginRequestDto
            {
                Email = "marko.p@example.com",
                Password = "wrong-password"
            });

        var missingAccount = await client.PostAsJsonAsync(
            "/api/admin/auth/login",
            new LoginRequestDto
            {
                Email = "missing@example.com",
                Password = "wrong-password"
            });

        Assert.Equal(HttpStatusCode.Unauthorized, wrongPassword.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, missingAccount.StatusCode);

        var wrongPasswordBody = await wrongPassword.Content.ReadFromJsonAsync<Dictionary<string, string>>();
        var missingAccountBody = await missingAccount.Content.ReadFromJsonAsync<Dictionary<string, string>>();

        Assert.NotNull(wrongPasswordBody);
        Assert.NotNull(missingAccountBody);
        Assert.Equal("Invalid email or password.", wrongPasswordBody["message"]);
        Assert.Equal(wrongPasswordBody["message"], missingAccountBody["message"]);
    }

    [Fact]
    public async Task Admin_login_rejects_valid_credentials_without_regional_manager_role()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        using var client = app.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/admin/auth/login",
            new LoginRequestDto
            {
                Email = "marko.p@example.com",
                Password = "password123"
            });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<Dictionary<string, string>>();
        Assert.NotNull(body);
        Assert.Equal("You do not have access to the admin app.", body["message"]);
    }

    [Fact]
    public async Task Admin_me_requires_authentication_and_returns_user_metadata_and_roles()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        await GrantRegionalManagerRoleAsync(app, "elena@example.com");

        using var anonymousClient = app.CreateClient();
        var anonymous = await anonymousClient.GetAsync("/api/admin/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);

        using var client = app.CreateClient();
        var session = await LoginRegionalManagerAsync(client);

        var me = await client.GetFromJsonAsync<AdminAuthUserDto>("/api/admin/auth/me");
        Assert.NotNull(me);
        Assert.Equal(session.User.Id, me!.Id);
        Assert.Equal("elena@example.com", me.Email);
        Assert.Contains(RoleNames.RegionalManager, me.Roles);
        Assert.True(me.CanAccessRegionalAdmin);
    }

    [Fact]
    public async Task Admin_refresh_and_logout_use_the_admin_refresh_cookie()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        await GrantRegionalManagerRoleAsync(app, "elena@example.com");

        using var client = app.CreateClient();
        await LoginRegionalManagerAsync(client);

        var refresh = await client.PostAsJsonAsync("/api/admin/auth/refresh", new { });
        refresh.EnsureSuccessStatusCode();

        var refreshedSession = await refresh.Content.ReadFromJsonAsync<AdminLoginResponseDto>();
        Assert.NotNull(refreshedSession);
        Assert.False(string.IsNullOrWhiteSpace(refreshedSession.AccessToken));
        Assert.Contains(RoleNames.RegionalManager, refreshedSession.User.Roles);

        var logout = await client.PostAsJsonAsync("/api/admin/auth/logout", new { });
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);

        var refreshAfterLogout = await client.PostAsJsonAsync("/api/admin/auth/refresh", new { });
        Assert.Equal(HttpStatusCode.Unauthorized, refreshAfterLogout.StatusCode);
    }

    [Fact]
    public async Task Regional_admin_endpoints_reject_anonymous_and_non_regional_users()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();

        using var anonymousClient = app.CreateClient();
        var anonymous = await anonymousClient.GetAsync("/api/admin/regional/regions");
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);

        using var nonRegionalClient = app.CreateClient();
        var loginResponse = await nonRegionalClient.PostAsJsonAsync(
            "/api/auth/login",
            new LoginRequestDto
            {
                Email = "marko.p@example.com",
                Password = "password123"
            });
        loginResponse.EnsureSuccessStatusCode();

        var nonRegionalSession = await loginResponse.Content.ReadFromJsonAsync<LoginResponseDto>();
        Assert.NotNull(nonRegionalSession);
        nonRegionalClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", nonRegionalSession.AccessToken);

        var forbidden = await nonRegionalClient.GetAsync("/api/admin/regional/regions");
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
    }

    private static async Task<AdminLoginResponseDto> LoginRegionalManagerAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync(
            "/api/admin/auth/login",
            new LoginRequestDto
            {
                Email = "elena@example.com",
                Password = "password123"
            });
        response.EnsureSuccessStatusCode();

        var session = await response.Content.ReadFromJsonAsync<AdminLoginResponseDto>();
        Assert.NotNull(session);
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", session.AccessToken);

        return session;
    }

    private static async Task GrantRegionalManagerRoleAsync(GeotrackApiFactory app, string email)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = await db.Users.SingleAsync(existing => existing.Email == email);
        var role = await db.Roles.SingleAsync(existing => existing.Name == RoleNames.RegionalManager);

        var hasRole = await db.UserRoles.AnyAsync(userRole =>
            userRole.UserId == user.Id &&
            userRole.RoleId == role.Id);

        if (!hasRole)
        {
            db.UserRoles.Add(new UserRole
            {
                UserId = user.Id,
                RoleId = role.Id
            });
        }

        await db.SaveChangesAsync();
    }
}
