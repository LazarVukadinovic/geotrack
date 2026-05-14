using Geotrack.IntegrationTests.Infrastructure;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Geotrack.IntegrationTests;

public sealed class CorsPolicyTests
{
    [Fact]
    public async Task Development_cors_allows_configured_local_frontend_origins_with_credentials()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
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
    public async Task Development_cors_rejects_unconfigured_origins()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        using var client = app.CreateClient();

        using var request = BuildPreflightRequest("https://example.invalid");
        using var response = await client.SendAsync(request);

        AssertCorsRejectsOrigin(response);
    }

    [Fact]
    public async Task Production_cors_allows_only_configured_https_origin_with_credentials()
    {
        await using var app = await GeotrackApiFactory.CreateAsync(Environments.Production);
        using var client = app.CreateClient();

        const string origin = "https://softeng.pmf.kg.ac.rs:10131";
        using var request = BuildPreflightRequest(origin);
        using var response = await client.SendAsync(request);

        AssertCorsAllowsOrigin(response, origin);
    }

    [Fact]
    public async Task Production_cors_rejects_unconfigured_origins()
    {
        await using var app = await GeotrackApiFactory.CreateAsync(Environments.Production);
        using var client = app.CreateClient();

        using var request = BuildPreflightRequest("http://softeng.pmf.kg.ac.rs:10131");
        using var response = await client.SendAsync(request);

        AssertCorsRejectsOrigin(response);
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

    private static void AssertCorsRejectsOrigin(HttpResponseMessage response)
    {
        Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"));
        Assert.False(response.Headers.Contains("Access-Control-Allow-Credentials"));
    }
}
