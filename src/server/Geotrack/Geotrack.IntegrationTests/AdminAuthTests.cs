using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
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
        var sharedCookie = Assert.Single(cookies, IsSharedRefreshCookie);
        AssertSharedRefreshCookieFlags(sharedCookie);

        var session = await ReadAdminSessionWithoutRefreshTokenAsync(response);
        Assert.False(string.IsNullOrWhiteSpace(session.AccessToken));
        Assert.Equal(3600, session.ExpiresIn);
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
    public async Task Content_moderator_login_succeeds_and_issues_role_claims()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();

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
    public async Task Admin_refresh_and_logout_use_the_shared_refresh_cookie()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        await GrantRegionalManagerRoleAsync(app, "elena@example.com");

        using var client = app.CreateClient();
        var login = await client.PostAsJsonAsync(
            "/api/admin/auth/login",
            new LoginRequestDto
            {
                Email = "elena@example.com",
                Password = "password123",
                RememberMe = false
            });
        login.EnsureSuccessStatusCode();

        Assert.True(login.Headers.TryGetValues("Set-Cookie", out var loginCookies));
        var loginCookie = Assert.Single(loginCookies, IsSharedRefreshCookie);
        AssertSharedRefreshCookieFlags(loginCookie);

        var loginSession = await ReadAdminSessionWithoutRefreshTokenAsync(login);
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", loginSession.AccessToken);

        var refresh = await client.PostAsJsonAsync("/api/admin/auth/refresh", new { });
        refresh.EnsureSuccessStatusCode();

        Assert.True(refresh.Headers.TryGetValues("Set-Cookie", out var refreshCookies));
        var refreshCookie = Assert.Single(refreshCookies, IsSharedRefreshCookie);
        AssertSharedRefreshCookieFlags(refreshCookie);
        Assert.NotEqual(ExtractCookieValue(loginCookie), ExtractCookieValue(refreshCookie));

        var refreshedSession = await ReadAdminSessionWithoutRefreshTokenAsync(refresh);
        Assert.False(string.IsNullOrWhiteSpace(refreshedSession.AccessToken));
        Assert.Equal(3600, refreshedSession.ExpiresIn);
        Assert.Contains(RoleNames.RegionalManager, refreshedSession.User.Roles);

        var logout = await client.PostAsJsonAsync("/api/admin/auth/logout", new { });
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        Assert.True(logout.Headers.TryGetValues("Set-Cookie", out var logoutCookies));
        Assert.Contains(logoutCookies, IsSharedRefreshCookieDeletion);

        var refreshAfterLogout = await client.PostAsJsonAsync("/api/admin/auth/refresh", new { });
        Assert.Equal(HttpStatusCode.Unauthorized, refreshAfterLogout.StatusCode);
    }

    [Fact]
    public async Task Refresh_cookie_flags_are_secure_when_request_is_https()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();

        using var client = app.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/admin/auth/login")
        {
            Content = JsonContent.Create(new LoginRequestDto
            {
                Email = "zika@gmail.com",
                Password = "Proba123!",
                RememberMe = true
            })
        };

        using var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();

        Assert.True(response.Headers.TryGetValues("Set-Cookie", out var cookies));
        var sharedCookie = Assert.Single(cookies, IsSharedRefreshCookie);
        AssertSharedRefreshCookieFlags(sharedCookie);
        Assert.Contains("secure", sharedCookie, StringComparison.OrdinalIgnoreCase);

        var session = await ReadAdminSessionWithoutRefreshTokenAsync(response);
        Assert.Equal("zika@gmail.com", session.User.Email);
    }

    [Fact]
    public async Task User_app_login_with_admin_role_bootstraps_admin_app_from_shared_cookie()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        using var client = app.CreateClient();

        var login = await client.PostAsJsonAsync(
            "/api/auth/login",
            new LoginRequestDto
            {
                Email = "mika@gmail.com",
                Password = "Proba123!",
                RememberMe = true
            });
        login.EnsureSuccessStatusCode();

        Assert.True(login.Headers.TryGetValues("Set-Cookie", out var cookies));
        var sharedCookie = Assert.Single(cookies, IsSharedRefreshCookie);
        Assert.Contains("max-age=", sharedCookie, StringComparison.OrdinalIgnoreCase);

        var userSession = await login.Content.ReadFromJsonAsync<LoginResponseDto>();
        Assert.NotNull(userSession);
        Assert.NotNull(userSession!.Session);
        Assert.True(userSession.Session!.CanAccessAdminApp);
        Assert.Contains(RoleNames.TerritoryManager, userSession.Session.Roles);
        Assert.Equal("/territory/overview", userSession.Session.AdminLandingRoute);

        var adminRefresh = await client.PostAsJsonAsync("/api/admin/auth/refresh", new { });
        adminRefresh.EnsureSuccessStatusCode();

        var adminSession = await adminRefresh.Content.ReadFromJsonAsync<AdminLoginResponseDto>();
        Assert.NotNull(adminSession);
        Assert.Equal("mika@gmail.com", adminSession!.User.Email);
        Assert.True(adminSession.User.CanAccessAdminApp);
        Assert.True(adminSession.User.CanAccessTerritoryAdmin);
        Assert.Contains(RoleNames.TerritoryManager, adminSession.User.Roles);
    }

    [Fact]
    public async Task Admin_app_login_bootstraps_user_app_from_shared_cookie()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        using var client = app.CreateClient();

        var adminLogin = await client.PostAsJsonAsync(
            "/api/admin/auth/login",
            new LoginRequestDto
            {
                Email = "zika@gmail.com",
                Password = "Proba123!",
                RememberMe = true
            });
        adminLogin.EnsureSuccessStatusCode();

        Assert.True(adminLogin.Headers.TryGetValues("Set-Cookie", out var cookies));
        Assert.Contains(cookies, IsSharedRefreshCookie);

        var userRefresh = await client.PostAsJsonAsync("/api/auth/refresh", new { });
        userRefresh.EnsureSuccessStatusCode();

        var userSession = await userRefresh.Content.ReadFromJsonAsync<RefreshResponseDto>();
        Assert.NotNull(userSession);
        Assert.False(string.IsNullOrWhiteSpace(userSession!.AccessToken));
        Assert.True(userSession.SessionPersistent);
        Assert.NotNull(userSession.Session);
        Assert.True(userSession.Session!.CanAccessAdminApp);
        Assert.Contains(RoleNames.Admin, userSession.Session.Roles);
    }

    [Fact]
    public async Task Admin_refresh_denies_normal_user_without_revoking_shared_user_session()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        using var client = app.CreateClient();

        var login = await client.PostAsJsonAsync(
            "/api/auth/login",
            new LoginRequestDto
            {
                Email = "marko.p@example.com",
                Password = "password123",
                RememberMe = true
            });
        login.EnsureSuccessStatusCode();

        var denied = await client.PostAsJsonAsync("/api/admin/auth/refresh", new { });
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);

        var body = await denied.Content.ReadFromJsonAsync<Dictionary<string, string>>();
        Assert.NotNull(body);
        Assert.Equal("You do not have access to the admin app.", body["message"]);

        var userRefresh = await client.PostAsJsonAsync("/api/auth/refresh", new { });
        userRefresh.EnsureSuccessStatusCode();

        var refreshed = await userRefresh.Content.ReadFromJsonAsync<RefreshResponseDto>();
        Assert.NotNull(refreshed);
        Assert.False(string.IsNullOrWhiteSpace(refreshed!.AccessToken));
        Assert.False(refreshed.Session!.CanAccessAdminApp);
    }

    [Fact]
    public async Task Auth_session_endpoint_returns_safe_roles_and_admin_access_flags()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();

        using (var anonymousClient = app.CreateClient())
        {
            var anonymous = await anonymousClient.GetAsync("/api/auth/session");
            Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        }

        using var client = app.CreateClient();
        var login = await client.PostAsJsonAsync(
            "/api/auth/login",
            new LoginRequestDto
            {
                Email = "moderator@gmail.com",
                Password = "Proba123!",
                RememberMe = false
            });
        login.EnsureSuccessStatusCode();

        var sessionResponse = await client.GetAsync("/api/auth/session");
        sessionResponse.EnsureSuccessStatusCode();

        var session = await sessionResponse.Content.ReadFromJsonAsync<AuthSessionDto>();
        Assert.NotNull(session);
        Assert.True(session!.IsAuthenticated);
        Assert.Equal("moderator@gmail.com", session.Email);
        Assert.True(session.CanAccessAdminApp);
        Assert.Equal(RoleNames.ContentModerator, session.AdminRoleType);
        Assert.Equal("/content-moderator/dashboard", session.AdminLandingRoute);
        Assert.Contains(RoleNames.ContentModerator, session.Roles);
    }

    [Fact]
    public async Task Remember_me_controls_shared_cookie_persistence()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();

        using (var persistentClient = app.CreateClient())
        {
            var persistentLogin = await persistentClient.PostAsJsonAsync(
                "/api/auth/login",
                new LoginRequestDto
                {
                    Email = "marko.p@example.com",
                    Password = "password123",
                    RememberMe = true
                });
            persistentLogin.EnsureSuccessStatusCode();

            Assert.True(persistentLogin.Headers.TryGetValues("Set-Cookie", out var persistentCookies));
            var sharedCookie = Assert.Single(persistentCookies, IsSharedRefreshCookie);
            Assert.Contains("max-age=", sharedCookie, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("expires=", sharedCookie, StringComparison.OrdinalIgnoreCase);
        }

        using (var sessionClient = app.CreateClient())
        {
            var sessionLogin = await sessionClient.PostAsJsonAsync(
                "/api/auth/login",
                new LoginRequestDto
                {
                    Email = "marko.p@example.com",
                    Password = "password123",
                    RememberMe = false
                });
            sessionLogin.EnsureSuccessStatusCode();

            Assert.True(sessionLogin.Headers.TryGetValues("Set-Cookie", out var sessionCookies));
            var sharedCookie = Assert.Single(sessionCookies, IsSharedRefreshCookie);
            Assert.DoesNotContain("max-age=", sharedCookie, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("expires=", sharedCookie, StringComparison.OrdinalIgnoreCase);
        }
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

    private static async Task<AdminLoginResponseDto> ReadAdminSessionWithoutRefreshTokenAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        AssertNoRefreshTokenJson(body);

        var session = JsonSerializer.Deserialize<AdminLoginResponseDto>(body, JsonOptions);
        Assert.NotNull(session);
        Assert.Equal(string.Empty, session!.RefreshToken);

        return session;
    }

    private static void AssertNoRefreshTokenJson(string body)
    {
        using var document = JsonDocument.Parse(body);
        Assert.False(
            document.RootElement.TryGetProperty("refreshToken", out _),
            $"Admin auth response must not include refreshToken JSON. Body: {body}");
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

    private static bool IsSharedRefreshCookie(string value)
    {
        var lower = value.ToLowerInvariant();

        return value.Contains("geotrack_refresh=", StringComparison.Ordinal)
            && (lower.Contains("path=/api;") || lower.EndsWith("path=/api", StringComparison.Ordinal))
            && !value.Contains("expires=Thu, 01 Jan 1970", StringComparison.OrdinalIgnoreCase)
            && !value.Contains("expires=Thu, 01 Jan 1970 00:00:00 GMT", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSharedRefreshCookieDeletion(string value)
    {
        var lower = value.ToLowerInvariant();

        return value.Contains("geotrack_refresh=", StringComparison.Ordinal)
            && (lower.Contains("path=/api;") || lower.EndsWith("path=/api"))
            && value.Contains("expires=Thu, 01 Jan 1970", StringComparison.OrdinalIgnoreCase);
    }

    private static void AssertSharedRefreshCookieFlags(string value)
    {
        Assert.Contains("httponly", value, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", value, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("path=/api", value, StringComparison.OrdinalIgnoreCase);
    }

    private static string ExtractCookieValue(string setCookie)
    {
        const string prefix = "geotrack_refresh=";
        var start = setCookie.IndexOf(prefix, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Missing {prefix} in Set-Cookie header.");

        start += prefix.Length;
        var end = setCookie.IndexOf(';', start);
        return end >= 0
            ? setCookie[start..end]
            : setCookie[start..];
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
}
