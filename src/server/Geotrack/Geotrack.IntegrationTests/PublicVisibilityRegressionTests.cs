using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Geotrack.Application.DTOs.Agent;
using Geotrack.Application.DTOs.GeoTrackTools;
using Geotrack.Application.DTOs.Places;
using Geotrack.Application.Interfaces.Service;
using Geotrack.Domain.Entities.Core;
using Geotrack.Infrastructure;
using Geotrack.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Geotrack.IntegrationTests;

public class PublicVisibilityRegressionTests
{
    private const string PublishedStatus = "Published";
    private const string DraftStatus = "Draft";
    private const string ArchivedStatus = "Archived";

    [Fact]
    public async Task GeoTrackToolService_filters_non_public_places_and_keeps_published_places()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var tools = scope.ServiceProvider.GetRequiredService<IGeoTrackToolService>();
        var fixture = await SeedVisibilityFixtureAsync(db);

        var results = await tools.SearchPlacesAsync(new GeoTrackToolSearchRequest
        {
            Query = fixture.PlaceSearchTerm,
            DestinationId = fixture.DestinationId,
            Limit = 10
        });
        var titles = results.Select(item => item.Title).ToList();

        Assert.Contains(fixture.PublishedPlaceTitle, titles);
        Assert.DoesNotContain(fixture.DraftPlaceTitle, titles);
        Assert.DoesNotContain(fixture.ArchivedPlaceTitle, titles);
        Assert.DoesNotContain(fixture.ParentHiddenPlaceTitle, titles);

        Assert.NotNull(await tools.GetPlaceDetailsAsync(fixture.PublishedPlaceId));
        Assert.Null(await tools.GetPlaceDetailsAsync(fixture.DraftPlaceId));
        Assert.Null(await tools.GetPlaceDetailsAsync(fixture.ArchivedPlaceId));
        Assert.Null(await tools.GetPlaceDetailsAsync(fixture.ParentHiddenPlaceId));
    }

    [Fact]
    public async Task GeoTrackToolService_filters_non_public_events_and_keeps_published_events()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var tools = scope.ServiceProvider.GetRequiredService<IGeoTrackToolService>();
        var fixture = await SeedVisibilityFixtureAsync(db);

        var results = await tools.SearchEventsAsync(new GeoTrackToolSearchRequest
        {
            Query = fixture.EventSearchTerm,
            DestinationId = fixture.DestinationId,
            Limit = 10,
            UpcomingOnly = true
        });
        var titles = results.Select(item => item.Title).ToList();

        Assert.Contains(fixture.PublishedEventTitle, titles);
        Assert.DoesNotContain(fixture.DraftEventTitle, titles);
        Assert.DoesNotContain(fixture.ArchivedEventTitle, titles);
        Assert.DoesNotContain(fixture.ParentHiddenEventTitle, titles);

        Assert.NotNull(await tools.GetEventDetailsAsync(fixture.PublishedEventId));
        Assert.Null(await tools.GetEventDetailsAsync(fixture.DraftEventId));
        Assert.Null(await tools.GetEventDetailsAsync(fixture.ArchivedEventId));
        Assert.Null(await tools.GetEventDetailsAsync(fixture.ParentHiddenEventId));
    }

    [Fact]
    public async Task New_public_catalog_tools_filter_non_public_content()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var tools = scope.ServiceProvider.GetRequiredService<IGeoTrackToolService>();
        var fixture = await SeedVisibilityFixtureAsync(db);

        var catalog = await tools.SearchCatalogAsync(new GeoTrackCatalogSearchRequest
        {
            Query = fixture.PlaceSearchTerm,
            AreaType = "destination",
            AreaId = fixture.DestinationId,
            EntityTypes = ["places"],
            Limit = 20
        });
        var titles = catalog.Results.Select(item => item.Title).ToList();

        Assert.Contains(fixture.PublishedPlaceTitle, titles);
        Assert.DoesNotContain(fixture.DraftPlaceTitle, titles);
        Assert.DoesNotContain(fixture.ArchivedPlaceTitle, titles);
        Assert.DoesNotContain(fixture.ParentHiddenPlaceTitle, titles);

        var area = await tools.SearchByAreaAsync(new GeoTrackAreaContentRequest
        {
            AreaType = "destination",
            AreaId = fixture.DestinationId,
            EntityTypes = ["places"],
            Limit = 20
        });
        var areaTitles = area.Places.Select(item => item.Title).ToList();

        Assert.DoesNotContain(fixture.DraftPlaceTitle, areaTitles);
        Assert.DoesNotContain(fixture.ArchivedPlaceTitle, areaTitles);
        Assert.DoesNotContain(fixture.ParentHiddenPlaceTitle, areaTitles);

        var nearby = await tools.FindNearbyPublicContentAsync(new GeoTrackNearbySearchRequest
        {
            Latitude = 42.6402,
            Longitude = 18.1083,
            RadiusKm = 5,
            EntityTypes = ["places"],
            Limit = 20
        });
        var nearbyTitles = nearby.Results.Select(item => item.Title).ToList();

        Assert.Contains(fixture.PublishedPlaceTitle, nearbyTitles);
        Assert.DoesNotContain(fixture.DraftPlaceTitle, nearbyTitles);
        Assert.DoesNotContain(fixture.ArchivedPlaceTitle, nearbyTitles);
        Assert.DoesNotContain(fixture.ParentHiddenPlaceTitle, nearbyTitles);
    }

    [Fact]
    public async Task Mcp_tool_calls_filter_non_public_content_and_keep_published_content()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        using var client = app.CreateClient();
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var fixture = await SeedVisibilityFixtureAsync(db);

        var placeSearchText = await ReadMcpTextPayloadAsync(await PostMcpJsonAsync(
            client,
            new
            {
                jsonrpc = "2.0",
                id = 10,
                method = "tools/call",
                @params = new
                {
                    name = "geotrack_search_places",
                    arguments = new
                    {
                        query = fixture.PlaceSearchTerm,
                        destinationId = fixture.DestinationId,
                        limit = 10
                    }
                }
            }));
        using (var placePayload = JsonDocument.Parse(placeSearchText))
        {
            var titles = placePayload.RootElement
                .EnumerateArray()
                .Select(item => item.GetProperty("title").GetString())
                .ToList();

            Assert.Contains(fixture.PublishedPlaceTitle, titles);
            Assert.DoesNotContain(fixture.DraftPlaceTitle, titles);
            Assert.DoesNotContain(fixture.ArchivedPlaceTitle, titles);
            Assert.DoesNotContain(fixture.ParentHiddenPlaceTitle, titles);
        }

        var eventSearchText = await ReadMcpTextPayloadAsync(await PostMcpJsonAsync(
            client,
            new
            {
                jsonrpc = "2.0",
                id = 11,
                method = "tools/call",
                @params = new
                {
                    name = "geotrack_search_events",
                    arguments = new
                    {
                        query = fixture.EventSearchTerm,
                        destinationId = fixture.DestinationId,
                        limit = 10,
                        upcomingOnly = true
                    }
                }
            }));
        using (var eventPayload = JsonDocument.Parse(eventSearchText))
        {
            var titles = eventPayload.RootElement
                .EnumerateArray()
                .Select(item => item.GetProperty("title").GetString())
                .ToList();

            Assert.Contains(fixture.PublishedEventTitle, titles);
            Assert.DoesNotContain(fixture.DraftEventTitle, titles);
            Assert.DoesNotContain(fixture.ArchivedEventTitle, titles);
            Assert.DoesNotContain(fixture.ParentHiddenEventTitle, titles);
        }

        var draftPlaceDetailText = await ReadMcpTextPayloadAsync(await PostMcpJsonAsync(
            client,
            new
            {
                jsonrpc = "2.0",
                id = 12,
                method = "tools/call",
                @params = new
                {
                    name = "geotrack_get_place_details",
                    arguments = new { id = fixture.DraftPlaceId }
                }
            }));
        using (var draftPlaceDetail = JsonDocument.Parse(draftPlaceDetailText))
        {
            Assert.Equal(JsonValueKind.Null, draftPlaceDetail.RootElement.ValueKind);
        }

        var archivedEventDetailText = await ReadMcpTextPayloadAsync(await PostMcpJsonAsync(
            client,
            new
            {
                jsonrpc = "2.0",
                id = 13,
                method = "tools/call",
                @params = new
                {
                    name = "geotrack_get_event_details",
                    arguments = new { id = fixture.ArchivedEventId }
                }
            }));
        using (var archivedEventDetail = JsonDocument.Parse(archivedEventDetailText))
        {
            Assert.Equal(JsonValueKind.Null, archivedEventDetail.RootElement.ValueKind);
        }
    }

    [Fact]
    public async Task Public_place_detail_related_events_filter_non_public_events_and_keep_published_events()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        using var client = app.CreateClient();
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var fixture = await SeedVisibilityFixtureAsync(db);

        var detail = await client.GetFromJsonAsync<PlaceDetailDto>($"/api/places/{fixture.PublishedPlaceId}");

        Assert.NotNull(detail);
        var titles = detail!.RelatedEvents.Select(item => item.Title).ToList();
        Assert.Contains(fixture.PublishedEventTitle, titles);
        Assert.DoesNotContain(fixture.DraftEventTitle, titles);
        Assert.DoesNotContain(fixture.ArchivedEventTitle, titles);
        Assert.DoesNotContain(fixture.ParentHiddenEventTitle, titles);
    }

    private static async Task<VisibilityFixture> SeedVisibilityFixtureAsync(AppDbContext db)
    {
        var now = DateTime.UtcNow;
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var placeSearchTerm = $"Batch1 Visibility {suffix} Place";
        var eventSearchTerm = $"Batch1 Visibility {suffix} Event";

        var destination = await db.Destinations
            .Where(destination => destination.Status == PublishedStatus)
            .OrderBy(destination => destination.Id)
            .FirstAsync();
        var location = await db.Locations
            .Where(location =>
                location.DestinationId == destination.Id &&
                location.Status == PublishedStatus)
            .OrderBy(location => location.Id)
            .FirstAsync();
        var placeType = await db.PlaceTypes.OrderBy(placeType => placeType.Id).FirstAsync();
        var eventType = await db.EventTypes.OrderBy(eventType => eventType.Id).FirstAsync();

        var hiddenLocation = new Location
        {
            Name = $"{placeSearchTerm} Draft Location",
            SearchText = $"{placeSearchTerm.ToLowerInvariant()} draft location",
            Status = DraftStatus,
            DestinationId = destination.Id,
            LocationTypeId = location.LocationTypeId,
            Description = "Draft parent location for public visibility regression tests.",
            Latitude = location.Latitude + 0.01,
            Longitude = location.Longitude + 0.01,
            CreatedAt = now
        };

        var publishedPlace = BuildPlace(
            $"{placeSearchTerm} Published",
            PublishedStatus,
            location.Id,
            placeType.Id,
            now,
            averageRating: 9999);
        var draftPlace = BuildPlace(
            $"{placeSearchTerm} Draft",
            DraftStatus,
            location.Id,
            placeType.Id,
            now,
            averageRating: 10000);
        var archivedPlace = BuildPlace(
            $"{placeSearchTerm} Archived",
            ArchivedStatus,
            location.Id,
            placeType.Id,
            now,
            averageRating: 10001);
        var parentHiddenPlace = BuildPlace(
            $"{placeSearchTerm} Hidden Parent",
            PublishedStatus,
            0,
            placeType.Id,
            now,
            averageRating: 10002);
        parentHiddenPlace.Location = hiddenLocation;

        var publishedEvent = BuildEvent(
            $"{eventSearchTerm} Published",
            PublishedStatus,
            destination.Id,
            location.Id,
            publishedPlace,
            eventType.Id,
            now.AddMinutes(1),
            now);
        var draftEvent = BuildEvent(
            $"{eventSearchTerm} Draft",
            DraftStatus,
            destination.Id,
            location.Id,
            publishedPlace,
            eventType.Id,
            now.AddMinutes(2),
            now);
        var archivedEvent = BuildEvent(
            $"{eventSearchTerm} Archived",
            ArchivedStatus,
            destination.Id,
            location.Id,
            publishedPlace,
            eventType.Id,
            now.AddMinutes(3),
            now);
        var parentHiddenEvent = BuildEvent(
            $"{eventSearchTerm} Hidden Parent",
            PublishedStatus,
            destination.Id,
            location.Id,
            parentHiddenPlace,
            eventType.Id,
            now.AddMinutes(4),
            now);

        db.Locations.Add(hiddenLocation);
        db.Places.AddRange(publishedPlace, draftPlace, archivedPlace, parentHiddenPlace);
        db.Events.AddRange(publishedEvent, draftEvent, archivedEvent, parentHiddenEvent);
        await db.SaveChangesAsync();

        return new VisibilityFixture(
            destination.Id,
            placeSearchTerm,
            eventSearchTerm,
            publishedPlace.Id,
            draftPlace.Id,
            archivedPlace.Id,
            parentHiddenPlace.Id,
            publishedEvent.Id,
            draftEvent.Id,
            archivedEvent.Id,
            parentHiddenEvent.Id,
            publishedPlace.Name,
            draftPlace.Name,
            archivedPlace.Name,
            parentHiddenPlace.Name,
            publishedEvent.Name,
            draftEvent.Name,
            archivedEvent.Name,
            parentHiddenEvent.Name);
    }

    private static Place BuildPlace(
        string name,
        string status,
        int locationId,
        int placeTypeId,
        DateTime now,
        double averageRating) =>
        new()
        {
            Name = name,
            SearchText = name.ToLowerInvariant(),
            Status = status,
            PlaceTypeId = placeTypeId,
            LocationId = locationId,
            Subtitle = "Visibility regression fixture",
            Description = $"{name} description",
            Latitude = 42.6402,
            Longitude = 18.1083,
            AverageRating = averageRating,
            ReviewCount = 1,
            CreatedAt = now,
            PublishedAt = status == PublishedStatus ? now : null
        };

    private static Event BuildEvent(
        string name,
        string status,
        int destinationId,
        int locationId,
        Place place,
        int eventTypeId,
        DateTime startDate,
        DateTime now) =>
        new()
        {
            Name = name,
            SearchText = name.ToLowerInvariant(),
            Status = status,
            EventTypeId = eventTypeId,
            DestinationId = destinationId,
            LocationId = locationId,
            Place = place,
            ParentEntityType = "Place",
            VenueName = place.Name,
            Summary = $"{name} summary",
            Description = $"{name} description",
            StartDate = startDate,
            EndDate = startDate.AddHours(2),
            CreatedAt = now,
            PublishedAt = status == PublishedStatus ? now : null
        };

    private static async Task<HttpResponseMessage> PostMcpJsonAsync(HttpClient client, object payload)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/mcp")
        {
            Content = JsonContent.Create(payload)
        };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));

        return await client.SendAsync(request);
    }

    private static async Task<string> ReadMcpTextPayloadAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(body);
        var result = document.RootElement.GetProperty("result");
        Assert.False(result.GetProperty("isError").GetBoolean());
        var content = Assert.Single(result.GetProperty("content").EnumerateArray());
        Assert.Equal("text", content.GetProperty("type").GetString());
        return content.GetProperty("text").GetString() ?? string.Empty;
    }

    private sealed record VisibilityFixture(
        int DestinationId,
        string PlaceSearchTerm,
        string EventSearchTerm,
        int PublishedPlaceId,
        int DraftPlaceId,
        int ArchivedPlaceId,
        int ParentHiddenPlaceId,
        int PublishedEventId,
        int DraftEventId,
        int ArchivedEventId,
        int ParentHiddenEventId,
        string PublishedPlaceTitle,
        string DraftPlaceTitle,
        string ArchivedPlaceTitle,
        string ParentHiddenPlaceTitle,
        string PublishedEventTitle,
        string DraftEventTitle,
        string ArchivedEventTitle,
        string ParentHiddenEventTitle);
}
