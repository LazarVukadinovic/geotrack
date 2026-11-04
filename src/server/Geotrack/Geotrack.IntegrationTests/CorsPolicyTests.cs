using Geotrack.IntegrationTests.Infrastructure;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Geotrack.IntegrationTests;

public sealed class CorsPolicyTests
{
    [Fact]
    public async Task Development_cors_allows_configured_origins_with_credentials()
    {
        await using var app = await CreateCorsAppAsync();
        using var client = app.CreateClient();

        var allowedOrigins = new[]
        {
            "http://localhost:4200",
            "http://127.0.0.1:4200",
            "http://localhost:4300",
            "http://127.0.0.1:4300"
        };

        foreach (var origin in allowedOrigins)
        {
            using var request = BuildPreflightRequest(origin);
            using var response = await client.SendAsync(request);

            AssertCorsAllowsOrigin(response, origin);
        }
    }

    [Fact]
    public async Task Production_cors_rejects_unconfigured_origins()
    {
        await using var app = await CreateCorsAppAsync(Environments.Production);
        using var client = app.CreateClient();

        const string origin = "https://app.example.invalid";
        using var request = BuildPreflightRequest(origin);
        using var response = await client.SendAsync(request);

        AssertCorsOmitsOrigin(response, origin);
    }

    [Fact]
    public async Task Production_cors_allows_configured_production_origin_with_credentials()
    {
        const string origin = "https://app.example.invalid";
        await using var app = await CreateCorsAppAsync(Environments.Production, origin);
        using var client = app.CreateClient();

        using var request = BuildPreflightRequest(origin);
        using var response = await client.SendAsync(request);

        AssertCorsAllowsOrigin(response, origin);
    }

    [Fact]
    public async Task Security_headers_include_csp_without_inline_scripts()
    {
        await using var app = await CreateCorsAppAsync();
        using var client = app.CreateClient();

        using var response = await client.GetAsync("/");

        Assert.True(
            response.Headers.TryGetValues("Content-Security-Policy", out var cspValues),
            "Missing Content-Security-Policy header.");

        var csp = Assert.Single(cspValues);
        Assert.Contains("object-src 'none'", csp);
        Assert.Contains("base-uri 'self'", csp);
        Assert.Contains("frame-ancestors 'none'", csp);
        Assert.Contains("form-action 'self'", csp);
        Assert.Contains("script-src 'self'", csp);
        Assert.DoesNotContain("script-src 'self' 'unsafe-inline'", csp);
    }

    private static Task<GeotrackApiFactory> CreateCorsAppAsync(
        string environment = "Development",
        string? allowedOrigin = null)
    {
        // CORS and header checks do not require demo users or catalogue content.
        var configuration = new Dictionary<string, string?>
        {
            ["Seed:EnableOperationalSeed"] = "false",
            ["Seed:EnableDemoContentSeed"] = "false",
            ["Seed:EnablePublicContentSeed"] = "false",
            ["Seed:SyncAdm1Regions:Enabled"] = "false"
        };
        if (allowedOrigin is not null)
            configuration["Cors:AllowedOrigins:0"] = allowedOrigin;
        return GeotrackApiFactory.CreateAsync(environment, configuration);
    }

    private static HttpRequestMessage BuildPreflightRequest(string origin)
    {
        var request = new HttpRequestMessage(HttpMethod.Options, "/api/admin/auth/login");
        request.Headers.TryAddWithoutValidation("Origin", origin);
        request.Headers.TryAddWithoutValidation("Access-Control-Request-Method", "POST");
        request.Headers.TryAddWithoutValidation("Access-Control-Request-Headers", "content-type, authorization");
        return request;
    }

    private static void AssertCorsAllowsOrigin(HttpResponseMessage response, string origin)
    {
        Assert.True(
            response.Headers.TryGetValues("Access-Control-Allow-Origin", out var allowedOriginValues),
            $"Missing Access-Control-Allow-Origin for {origin}.");
        Assert.Contains(origin, allowedOriginValues);

        Assert.True(
            response.Headers.TryGetValues("Access-Control-Allow-Credentials", out var credentialValues),
            $"Missing Access-Control-Allow-Credentials for {origin}.");
        Assert.Contains("true", credentialValues);
    }

    private static void AssertCorsOmitsOrigin(HttpResponseMessage response, string origin)
    {
        Assert.False(
            response.Headers.TryGetValues("Access-Control-Allow-Origin", out _),
            $"Unexpected Access-Control-Allow-Origin for {origin}.");
    }
}
