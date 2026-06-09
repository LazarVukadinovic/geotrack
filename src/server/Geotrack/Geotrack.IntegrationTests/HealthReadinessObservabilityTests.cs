using System.Net;
using Geotrack.IntegrationTests.Infrastructure;
using Geotrack.Server.Middleware;
using Xunit;

namespace Geotrack.IntegrationTests;

public sealed class HealthReadinessObservabilityTests
{
    [Fact]
    public async Task Health_live_returns_safe_success_and_correlation_header()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        using var client = app.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/health/live");
        request.Headers.TryAddWithoutValidation(CorrelationIdMiddleware.HeaderName, "batch7-live-123");

        var response = await client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("batch7-live-123", response.Headers.GetValues(CorrelationIdMiddleware.HeaderName).Single());
        Assert.Contains("\"status\":\"Healthy\"", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ConnectionStrings", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Jwt", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ApiKey", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Health_ready_checks_database_without_leaking_secrets()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        using var client = app.CreateClient();

        var response = await client.GetAsync("/health/ready");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.Contains(CorrelationIdMiddleware.HeaderName));
        Assert.Contains("\"database\"", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Host=", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Password=", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Exception", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Invalid_correlation_id_is_replaced()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        using var client = app.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/health/live");
        request.Headers.TryAddWithoutValidation(
            CorrelationIdMiddleware.HeaderName,
            new string('x', 200));

        var response = await client.SendAsync(request);

        response.EnsureSuccessStatusCode();
        var returned = response.Headers.GetValues(CorrelationIdMiddleware.HeaderName).Single();
        Assert.NotEqual(new string('x', 200), returned);
        Assert.True(returned.Length <= 128);
    }
}
