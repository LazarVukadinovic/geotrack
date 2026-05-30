using HeaderNames = Microsoft.Net.Http.Headers.HeaderNames;

namespace Geotrack.Server;

internal static class AuthRefreshCookie
{
    public const string SharedCookieName = "geotrack_refresh";
    public const string SharedCookiePath = "/api";
    private const string LegacyUserCookiePath = "/api/auth";
    private const string LegacyAdminCookieName = "geotrack_admin_refresh";
    private const string LegacyAdminCookiePath = "/api/admin/auth";
    private static readonly TimeSpan PersistentLifetime = TimeSpan.FromDays(7);

    public static string Resolve(HttpRequest request, string? requestRefreshToken)
    {
        var cookieToken = request.Cookies[SharedCookieName];
        if (!string.IsNullOrWhiteSpace(cookieToken))
            return cookieToken;

        var legacyAdminCookieToken = request.Cookies[LegacyAdminCookieName];
        if (!string.IsNullOrWhiteSpace(legacyAdminCookieToken))
            return legacyAdminCookieToken;

        return requestRefreshToken?.Trim() ?? string.Empty;
    }

    public static void Append(HttpContext context, string refreshToken, bool isPersistent)
    {
        DeleteLegacyCookies(context);

        var options = BuildOptions(context, SharedCookiePath);
        if (isPersistent)
        {
            options.Expires = DateTimeOffset.UtcNow.Add(PersistentLifetime);
            options.MaxAge = PersistentLifetime;
        }

        context.Response.Cookies.Append(SharedCookieName, refreshToken, options);
        context.Response.Headers.Append(HeaderNames.CacheControl, "no-store");
    }

    public static void DeleteAll(HttpContext context)
    {
        context.Response.Cookies.Delete(SharedCookieName, BuildOptions(context, SharedCookiePath));
        DeleteLegacyCookies(context);
        context.Response.Headers.Append(HeaderNames.CacheControl, "no-store");
    }

    private static void DeleteLegacyCookies(HttpContext context)
    {
        context.Response.Cookies.Delete(SharedCookieName, BuildOptions(context, LegacyUserCookiePath));
        context.Response.Cookies.Delete(LegacyAdminCookieName, BuildOptions(context, LegacyAdminCookiePath));
    }

    private static CookieOptions BuildOptions(HttpContext context, string path)
    {
        var options = new CookieOptions
        {
            HttpOnly = true,
            IsEssential = true,
            Path = path,
            SameSite = SameSiteMode.Lax,
            Secure = ShouldUseSecureCookie(context.Request)
        };

        var configuredDomain = context.RequestServices
            .GetRequiredService<IConfiguration>()["Auth:RefreshCookie:Domain"];
        if (!string.IsNullOrWhiteSpace(configuredDomain))
        {
            options.Domain = configuredDomain.Trim();
        }

        return options;
    }

    private static bool ShouldUseSecureCookie(HttpRequest request)
    {
        if (request.IsHttps)
            return true;

        var host = request.Host.Host;
        return !IsLocalDevelopmentHost(host);
    }

    private static bool IsLocalDevelopmentHost(string? host)
    {
        return string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase)
            || string.Equals(host, "127.0.0.1", StringComparison.OrdinalIgnoreCase)
            || string.Equals(host, "::1", StringComparison.OrdinalIgnoreCase)
            || string.Equals(host, "[::1]", StringComparison.OrdinalIgnoreCase);
    }
}
