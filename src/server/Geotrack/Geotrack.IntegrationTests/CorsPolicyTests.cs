using Geotrack.IntegrationTests.Infrastructure;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Geotrack.IntegrationTests;

public sealed class CorsPolicyTests
{
    [Fact]
    public async Task Development_cors_allows_both_local_frontend_apps_with_credentials()
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

            Assert.True(
                response.Headers.TryGetValues("Access-Control-Allow-Origin", out var allowedOriginValues),
                $"Missing Access-Control-Allow-Origin for {origin}.");
            Assert.Contains(origin, allowedOriginValues);

            Assert.True(
                response.Headers.TryGetValues("Access-Control-Allow-Credentials", out var credentialValues),
                $"Missing Access-Control-Allow-Credentials for {origin}.");
            Assert.Contains("true", credentialValues);
        }
    }

    [Fact]
    public async Task Development_cors_does_not_allow_unconfigured_origins()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        using var client = app.CreateClient();

        using var request = BuildPreflightRequest("https://example.invalid");
        using var response = await client.SendAsync(request);

        Assert.False(response.Headers.TryGetValues("Access-Control-Allow-Origin", out _));
        Assert.False(response.Headers.TryGetValues("Access-Control-Allow-Credentials", out _));
    }

    [Fact]
    public async Task Production_cors_does_not_emit_development_credentials_policy()
    {
        await using var app = await GeotrackApiFactory.CreateAsync(Environments.Production);
        using var client = app.CreateClient();

        using var request = BuildPreflightRequest("http://localhost:4300");
        using var response = await client.SendAsync(request);

        Assert.False(response.Headers.TryGetValues("Access-Control-Allow-Origin", out _));
        Assert.False(response.Headers.TryGetValues("Access-Control-Allow-Credentials", out _));
    }

    private static HttpRequestMessage BuildPreflightRequest(string origin)
    {
        var request = new HttpRequestMessage(HttpMethod.Options, "/api/admin/auth/login");
        request.Headers.TryAddWithoutValidation("Origin", origin);
        request.Headers.TryAddWithoutValidation("Access-Control-Request-Method", "POST");
        request.Headers.TryAddWithoutValidation("Access-Control-Request-Headers", "content-type, authorization");
        return request;
    }
}
