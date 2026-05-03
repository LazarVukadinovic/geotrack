using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Geotrack.Application.DTOs.Locations;
using Geotrack.Application.DTOs.Map;
using Geotrack.Application.Interfaces.Service;
using Geotrack.Infrastructure.Services;
using Geotrack.IntegrationTests.Infrastructure;
using Geotrack.Server.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Geotrack.IntegrationTests;

public sealed class MapLocationAbuseProtectionTests
{
    [Fact]
    public async Task Map_search_rejects_overlong_query_before_service_call()
    {
        var mapService = new RecordingMapService();
        var controller = new MapController(mapService);
        ConfigureRequest(controller, HttpMethods.Get, "/api/map/search");

        var result = await controller.Search(new string('x', 101), null, CancellationToken.None);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        AssertMessage(badRequest, "Search query must be 100 characters or fewer.");
        Assert.Equal(0, mapService.SearchCalls);
    }

    [Fact]
    public async Task Map_search_returns_empty_for_too_short_query_without_service_call()
    {
        var mapService = new RecordingMapService();
        var controller = new MapController(mapService);
        ConfigureRequest(controller, HttpMethods.Get, "/api/map/search");

        var result = await controller.Search("a", null, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Empty(Assert.IsAssignableFrom<IEnumerable<object>>(ok.Value));
        Assert.Equal(0, mapService.SearchCalls);
    }

    [Fact]
    public async Task Map_reverse_rejects_invalid_coordinates_before_service_call()
    {
        var mapService = new RecordingMapService();
        var controller = new MapController(mapService);
        ConfigureRequest(controller, HttpMethods.Get, "/api/map/reverse");

        var result = await controller.ReverseGeocode(91, 20, CancellationToken.None);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        AssertMessage(badRequest, "Valid latitude and longitude are required.");
        Assert.Equal(0, mapService.ReverseCalls);
    }

    [Fact]
    public async Task Map_route_rejects_degenerate_route_before_service_call()
    {
        var mapService = new RecordingMapService();
        var controller = new MapController(mapService);
        ConfigureRequest(controller, HttpMethods.Get, "/api/map/route");

        var result = await controller.GetWalkingRoute(44.8125, 20.4612, 44.8125, 20.4612, CancellationToken.None);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        AssertMessage(badRequest, "Origin and destination must be different.");
        Assert.Equal(0, mapService.RouteCalls);
    }

    [Fact]
    public async Task Map_entities_rejects_excessive_radius_and_categories_before_service_call()
    {
        var mapService = new RecordingMapService();
        var controller = new MapController(mapService);
        ConfigureRequest(controller, HttpMethods.Get, "/api/map/entities");

        var badRadius = await controller.GetEntities(44.8125, 20.4612, "places", 26, CancellationToken.None);
        var badCategories = await controller.GetEntities(44.8125, 20.4612, "places,stays,events,unknown", 5, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(badRadius);
        Assert.IsType<BadRequestObjectResult>(badCategories);
        Assert.Equal(0, mapService.EntitiesCalls);
    }

    [Fact]
    public async Task Map_markers_rejects_oversized_entity_viewport_before_service_call()
    {
        var mapService = new RecordingMapService();
        var controller = new MapController(mapService);
        ConfigureRequest(controller, HttpMethods.Get, "/api/map/markers");

        var result = await controller.GetMarkers(60, 40, 40, 10, 13, null, CancellationToken.None);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        AssertMessage(badRequest, "Viewport bounds are too large for entity map results.");
        Assert.Equal(0, mapService.MarkersCalls);
    }

    [Fact]
    public void Public_map_and_approximate_location_actions_have_explicit_rate_limit_metadata()
    {
        AssertRateLimitPolicy<MapController>(nameof(MapController.Search), "mapSearch");
        AssertRateLimitPolicy<MapController>(nameof(MapController.ReverseGeocode), "mapExternalLookup");
        AssertRateLimitPolicy<MapController>(nameof(MapController.GetWalkingRoute), "mapExternalLookup");
        AssertRateLimitPolicy<MapController>(nameof(MapController.GetMarkers), "map");
        AssertRateLimitPolicy<MapController>(nameof(MapController.GetEntities), "map");
        AssertRateLimitPolicy<LocationController>(nameof(LocationController.GetApproximateLocation), "approximateLocation");
    }

    [Fact]
    public async Task Map_external_lookup_rate_limit_returns_429_after_policy_limit()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        using var client = app.CreateClient();

        HttpResponseMessage? response = null;
        for (var i = 0; i < 31; i++)
        {
            response?.Dispose();
            response = await client.GetAsync("/api/map/reverse?latitude=91&longitude=20");
        }

        var finalResponse = Assert.IsType<HttpResponseMessage>(response);
        using (finalResponse)
        {
            Assert.Equal(HttpStatusCode.TooManyRequests, finalResponse.StatusCode);
            var responseJson = await finalResponse.Content.ReadAsStringAsync();
            Assert.Contains("Too many requests. Please try again later.", responseJson);
        }
    }

    [Fact]
    public async Task Map_search_reuses_short_lived_cache_for_identical_normalized_query()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var handler = new StubHttpMessageHandler(_ => JsonResponse(
            """
            [{"display_name":"Belgrade, Serbia","lat":"44.8125","lon":"20.4612"}]
            """));
        using var client = new HttpClient(handler);
        var service = new MapService(client, null!, null!, cache, NullLogger<MapService>.Instance);

        var first = await service.SearchAsync(" Belgrade ");
        var second = await service.SearchAsync("belgrade");

        Assert.Equal(1, handler.RequestCount);
        Assert.Single(first);
        Assert.Single(second);
        Assert.Equal(first[0].Latitude, second[0].Latitude);
    }

    [Fact]
    public async Task Approximate_location_reuses_short_lived_cache_for_same_public_ip()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var handler = new StubHttpMessageHandler(_ => JsonResponse(
            """
            {"success":true,"latitude":44.8125,"longitude":20.4612,"city":"Belgrade","region":"Central Serbia","country":"Serbia"}
            """));
        using var client = new HttpClient(handler);
        var service = new ClientLocationResolverService(
            client,
            cache,
            NullLogger<ClientLocationResolverService>.Instance);

        var first = await service.ResolveApproximateLocationAsync("8.8.8.8");
        var second = await service.ResolveApproximateLocationAsync("8.8.8.8");

        Assert.Equal(1, handler.RequestCount);
        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.Equal(first!.DisplayLabel, second!.DisplayLabel);
    }

    private static void AssertRateLimitPolicy<TController>(string actionName, string expectedPolicy)
    {
        var method = typeof(TController).GetMethod(
            actionName,
            BindingFlags.Instance | BindingFlags.Public);

        Assert.NotNull(method);
        var attribute = Assert.Single(method!.GetCustomAttributes<EnableRateLimitingAttribute>());
        Assert.Equal(expectedPolicy, attribute.PolicyName);
    }

    private static void ConfigureRequest(ControllerBase controller, string method, string path)
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Method = method;
        httpContext.Request.Path = path;
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = httpContext
        };
    }

    private static string AssertMessage(ObjectResult result, string expectedMessage)
    {
        var responseJson = JsonSerializer.Serialize(result.Value);
        using var document = JsonDocument.Parse(responseJson);
        Assert.Equal(expectedMessage, document.RootElement.GetProperty("message").GetString());
        return responseJson;
    }

    private static HttpResponseMessage JsonResponse(string json) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _handler;

        public StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
        {
            _handler = handler;
        }

        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            return Task.FromResult(_handler(request));
        }
    }

    private sealed class RecordingMapService : IMapService
    {
        public int SearchCalls { get; private set; }
        public int ReverseCalls { get; private set; }
        public int RouteCalls { get; private set; }
        public int EntitiesCalls { get; private set; }
        public int MarkersCalls { get; private set; }

        public Task<List<MapSearchResultDto>> SearchAsync(
            string query,
            CancellationToken cancellationToken = default)
        {
            SearchCalls++;
            return Task.FromResult(new List<MapSearchResultDto>());
        }

        public Task<List<MapSearchResultDto>> SearchAsync(
            string query,
            IEnumerable<string>? types,
            CancellationToken cancellationToken = default)
        {
            SearchCalls++;
            return Task.FromResult(new List<MapSearchResultDto>());
        }

        public Task<MapReverseGeocodeDto?> ReverseGeocodeAsync(
            double latitude,
            double longitude,
            CancellationToken cancellationToken = default)
        {
            ReverseCalls++;
            return Task.FromResult<MapReverseGeocodeDto?>(new MapReverseGeocodeDto());
        }

        public Task<MapRouteResponseDto?> GetWalkingRouteAsync(
            double originLatitude,
            double originLongitude,
            double destinationLatitude,
            double destinationLongitude,
            CancellationToken cancellationToken = default)
        {
            RouteCalls++;
            return Task.FromResult<MapRouteResponseDto?>(new MapRouteResponseDto
            {
                Coordinates = new List<MapCoordinateDto>
                {
                    new() { Latitude = originLatitude, Longitude = originLongitude },
                    new() { Latitude = destinationLatitude, Longitude = destinationLongitude },
                }
            });
        }

        public Task<GetMapEntitiesResponseDto> GetEntitiesAsync(
            double latitude,
            double longitude,
            IEnumerable<string>? categories,
            double? radiusKm,
            int? userId = null,
            CancellationToken cancellationToken = default)
        {
            EntitiesCalls++;
            return Task.FromResult(new GetMapEntitiesResponseDto());
        }

        public Task<GetMapMarkersResponseDto> GetMarkersInViewportAsync(
            double north,
            double south,
            double east,
            double west,
            double zoom,
            IEnumerable<string>? categories,
            int? userId = null,
            CancellationToken cancellationToken = default)
        {
            MarkersCalls++;
            return Task.FromResult(new GetMapMarkersResponseDto());
        }
    }
}
