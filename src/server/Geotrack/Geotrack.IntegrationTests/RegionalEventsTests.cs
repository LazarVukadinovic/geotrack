using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Geotrack.Application.DTOs.Admin;
using Geotrack.Application.DTOs.Auth;
using Geotrack.Domain.Entities.Core;
using Geotrack.Domain.Entities.Reviews;
using Geotrack.Domain.Entities.Tags;
using Geotrack.Domain.Entities.Users;
using Geotrack.Infrastructure;
using Geotrack.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Coordinate = NetTopologySuite.Geometries.Coordinate;
using Geometry = NetTopologySuite.Geometries.Geometry;
using GeometryFactory = NetTopologySuite.Geometries.GeometryFactory;
using Polygon = NetTopologySuite.Geometries.Polygon;
using PrecisionModel = NetTopologySuite.Geometries.PrecisionModel;

namespace Geotrack.IntegrationTests;

public sealed class RegionalEventsTests
{
    private const string BoundaryGeometryPropertyName = "BoundaryGeometry";
    private static readonly GeometryFactory GeometryFactory = new(new PrecisionModel(), 4326);

    [Fact]
    public async Task Regional_manager_lists_only_assigned_region_events_with_default_25_item_page()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        var fixture = await CreateEventsFixtureAsync(app);

        using var anonymous = app.CreateClient();
        var anonymousResponse = await anonymous.GetAsync(
            $"/api/admin/regional/events?regionId={fixture.AssignedRegionId}");
        Assert.Equal(HttpStatusCode.Unauthorized, anonymousResponse.StatusCode);

        using var regularUser = await LoginAsync(app, "marko.p@example.com", "password123");
        var nonRegionalResponse = await regularUser.GetAsync(
            $"/api/admin/regional/events?regionId={fixture.AssignedRegionId}");
        Assert.Equal(HttpStatusCode.Forbidden, nonRegionalResponse.StatusCode);

        using var manager = await LoginAsync(app, "elena@example.com", "password123");
        var unassignedRegionResponse = await manager.GetAsync(
            $"/api/admin/regional/events?regionId={fixture.UnassignedRegionId}");
        Assert.Equal(HttpStatusCode.Forbidden, unassignedRegionResponse.StatusCode);

        var page = await GetEventsAsync(manager, fixture.AssignedRegionId);

        Assert.Equal(30, page.TotalCount);
        Assert.Equal(0, page.Skip);
        Assert.Equal(25, page.Take);
        Assert.Equal(25, page.Items.Count);
        Assert.All(page.Items, item => Assert.Equal(fixture.AssignedRegionId, item.RegionId));
        Assert.DoesNotContain(page.Items, item => item.Id == fixture.UnassignedEventId);
        var searchEvent = Assert.Single(page.Items, item => item.Id == fixture.SearchEventId);
        AssertDateClose(fixture.SearchEventStartsAt, searchEvent.StartsAt);
        AssertDateClose(fixture.SearchEventEndsAt, searchEvent.EndsAt);
        Assert.Equal(fixture.SearchEventTypeName, searchEvent.Category);
        Assert.Equal(fixture.SearchLocationName, searchEvent.LocationName);
    }

    [Fact]
    public async Task Pagination_search_and_sort_use_backend_query_parameters()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        var fixture = await CreateEventsFixtureAsync(app);

        using var manager = await LoginAsync(app, "elena@example.com", "password123");

        var nextPage = await GetEventsAsync(manager, fixture.AssignedRegionId, skip: 25, take: 10);
        Assert.Equal(30, nextPage.TotalCount);
        Assert.Equal(25, nextPage.Skip);
        Assert.Equal(10, nextPage.Take);
        Assert.Equal(5, nextPage.Items.Count);

        var searchedByName = await GetEventsAsync(
            manager,
            fixture.AssignedRegionId,
            search: "Regional Event 07");
        Assert.Single(searchedByName.Items);
        Assert.Equal(fixture.SearchEventId, searchedByName.Items[0].Id);

        var searchedBySummary = await GetEventsAsync(
            manager,
            fixture.AssignedRegionId,
            search: "moonlit strings");
        Assert.Single(searchedBySummary.Items);
        Assert.Equal(fixture.SearchEventId, searchedBySummary.Items[0].Id);

        var searchedByCategory = await GetEventsAsync(
            manager,
            fixture.AssignedRegionId,
            search: fixture.SearchEventTypeName);
        Assert.Single(searchedByCategory.Items);
        Assert.Equal(fixture.SearchEventId, searchedByCategory.Items[0].Id);

        var newest = await GetEventsAsync(manager, fixture.AssignedRegionId, sort: "newest", take: 3);
        Assert.Equal("Regional Event 00", newest.Items[0].Name);

        var oldest = await GetEventsAsync(manager, fixture.AssignedRegionId, sort: "oldest", take: 3);
        Assert.Equal("Regional Event 29", oldest.Items[0].Name);

        var nameAsc = await GetEventsAsync(manager, fixture.AssignedRegionId, sort: "nameAsc", take: 3);
        Assert.Equal("Regional Event 00", nameAsc.Items[0].Name);

        var nameDesc = await GetEventsAsync(manager, fixture.AssignedRegionId, sort: "nameDesc", take: 3);
        Assert.Equal("Regional Event 29", nameDesc.Items[0].Name);

        var eventDateAsc = await GetEventsAsync(manager, fixture.AssignedRegionId, sort: "eventDateAsc", take: 3);
        Assert.Equal("Regional Event 00", eventDateAsc.Items[0].Name);

        var eventDateDesc = await GetEventsAsync(manager, fixture.AssignedRegionId, sort: "eventDateDesc", take: 3);
        Assert.Equal("Regional Event 29", eventDateDesc.Items[0].Name);
    }

    [Fact]
    public async Task Delete_event_is_region_scoped_and_blocks_related_content()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        var fixture = await CreateEventsFixtureAsync(app);

        using var manager = await LoginAsync(app, "elena@example.com", "password123");

        var outOfRegionResponse = await manager.DeleteAsync(
            $"/api/admin/regional/events/{fixture.UnassignedEventId}?regionId={fixture.AssignedRegionId}");
        Assert.Equal(HttpStatusCode.NotFound, outOfRegionResponse.StatusCode);

        var blockedResponse = await manager.DeleteAsync(
            $"/api/admin/regional/events/{fixture.EventWithContentId}?regionId={fixture.AssignedRegionId}");
        Assert.Equal(HttpStatusCode.Conflict, blockedResponse.StatusCode);
        var blockedBody = await blockedResponse.Content.ReadFromJsonAsync<Dictionary<string, string>>();
        Assert.Equal("This event cannot be deleted while it has related content.", blockedBody?["message"]);

        var deleteResponse = await manager.DeleteAsync(
            $"/api/admin/regional/events/{fixture.BareEventId}?regionId={fixture.AssignedRegionId}");
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.False(await db.Events.AnyAsync(item => item.Id == fixture.BareEventId));
    }

    [Fact]
    public async Task Detail_and_update_are_restricted_to_assigned_region()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        var fixture = await CreateEventsFixtureAsync(app);

        using var manager = await LoginAsync(app, "elena@example.com", "password123");

        var outDetail = await manager.GetAsync(
            $"/api/admin/regional/events/{fixture.UnassignedEventId}?regionId={fixture.AssignedRegionId}");
        Assert.Equal(HttpStatusCode.NotFound, outDetail.StatusCode);

        var updatedStart = fixture.SearchEventStartsAt.AddDays(10);
        var updatedEnd = fixture.SearchEventEndsAt.AddDays(10);
        var updateResponse = await manager.PutAsJsonAsync(
            $"/api/admin/regional/events/{fixture.SearchEventId}?regionId={fixture.AssignedRegionId}",
            new RegionalEventUpdateRequestDto
            {
                Name = "Regional Event 07 Updated",
                Summary = "Updated event summary.",
                Description = "Updated event description.",
                VenueName = "Updated venue hall",
                IsAllDay = false,
                StartsAt = updatedStart,
                EndsAt = updatedEnd,
                IsRecurring = true,
                IsFree = false,
                TicketPrice = "35",
                ExternalTicketLink = "https://tickets.example.com/updated-event",
                OrganizerName = "Updated organizer",
                ContactEmail = "updated-events@example.com",
                AttendanceLimit = 321,
                AgeRestriction = "16+",
                Images =
                [
                    new RegionalEntityImageInputDto
                    {
                        Url = "/media/seed/events/search-card.webp",
                        IsMain = true,
                        SortOrder = 0,
                        AltText = "Existing search event card"
                    },
                    new RegionalEntityImageInputDto
                    {
                        Url = "/media/uploads/regional/updated-gallery.webp",
                        IsMain = false,
                        SortOrder = 2,
                        AltText = "Updated gallery image"
                    }
                ]
            });
        updateResponse.EnsureSuccessStatusCode();

        var updated = await updateResponse.Content.ReadFromJsonAsync<RegionalEventDetailDto>();
        Assert.NotNull(updated);
        Assert.Equal(fixture.AssignedRegionId, updated!.RegionId);
        Assert.Equal("Regional Event 07 Updated", updated.Name);
        Assert.Equal("Updated event summary.", updated.Summary);
        Assert.Equal("Updated event description.", updated.Description);
        Assert.Equal("Updated venue hall", updated.VenueName);
        AssertDateClose(updatedStart, updated.StartsAt);
        AssertDateClose(updatedEnd, updated.EndsAt);
        Assert.False(updated.IsAllDay);
        Assert.True(updated.IsRecurring);
        Assert.False(updated.IsFree);
        Assert.Equal("35", updated.TicketPrice);
        Assert.Equal("https://tickets.example.com/updated-event", updated.ExternalTicketLink);
        Assert.Equal("Updated organizer", updated.HostedBy);
        Assert.Equal("updated-events@example.com", updated.ContactEmail);
        Assert.Equal(321, updated.AttendanceLimit);
        Assert.Equal("16+", updated.AgeRestriction);
        Assert.Contains(updated.Images, image => image.Url == "/media/seed/events/search-card.webp" && image.IsMain);
        Assert.Contains(updated.Images, image => image.Url == "/media/uploads/regional/updated-gallery.webp" && !image.IsMain);
    }

    [Fact]
    public async Task Create_event_enforces_auth_region_parent_scope_and_persists_real_data()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        var fixture = await CreateEventsFixtureAsync(app);
        var validRequest = BuildValidCreateRequest(fixture);

        using var anonymous = app.CreateClient();
        var anonymousResponse = await anonymous.PostAsJsonAsync("/api/admin/regional/events", validRequest);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymousResponse.StatusCode);

        using var regularUser = await LoginAsync(app, "marko.p@example.com", "password123");
        var nonRegionalResponse = await regularUser.PostAsJsonAsync("/api/admin/regional/events", validRequest);
        Assert.Equal(HttpStatusCode.Forbidden, nonRegionalResponse.StatusCode);

        using var manager = await LoginAsync(app, "elena@example.com", "password123");

        var unassignedRegionResponse = await manager.PostAsJsonAsync(
            "/api/admin/regional/events",
            validRequest with { RegionId = fixture.UnassignedRegionId });
        Assert.Equal(HttpStatusCode.Forbidden, unassignedRegionResponse.StatusCode);

        var outOfRegionParentResponse = await manager.PostAsJsonAsync(
            "/api/admin/regional/events",
            validRequest with
            {
                LocationId = fixture.UnassignedLocationId,
                DestinationId = fixture.UnassignedDestinationId
            });
        Assert.Equal(HttpStatusCode.BadRequest, outOfRegionParentResponse.StatusCode);

        var tamperedReferenceResponse = await manager.PostAsJsonAsync(
            "/api/admin/regional/events",
            validRequest with { InternalReferenceId = "EVT-CLIENT-001" });
        Assert.Equal(HttpStatusCode.BadRequest, tamperedReferenceResponse.StatusCode);

        var createResponse = await manager.PostAsJsonAsync("/api/admin/regional/events", validRequest);
        createResponse.EnsureSuccessStatusCode();

        var created = await createResponse.Content.ReadFromJsonAsync<RegionalEventDetailDto>();
        Assert.NotNull(created);
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        Assert.Equal(fixture.AssignedRegionId, created!.RegionId);
        Assert.Equal(fixture.AssignedDestinationId, created.DestinationId);
        Assert.Equal(fixture.AssignedLocationId, created.LocationId);
        Assert.Null(created.PlaceId);
        Assert.Equal("Location", created.ParentEntityType);
        Assert.Equal("Backend Integrated Event", created.Name);
        Assert.Equal("A real event created through the regional admin API.", created.Summary);
        Assert.StartsWith("EVT-", created.InternalReferenceId);
        Assert.True(created.IsFree);
        Assert.Null(created.TicketPrice);
        Assert.True(created.IsRecurring);
        Assert.Single(created.Tags, tag => tag.Id == fixture.EventTagId);

        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var stored = await db.Events
            .Include(item => item.EventTags)
            .SingleAsync(item => item.Id == created.Id);

        Assert.Equal($"EVT-{created.Id:D6}", stored.InternalReferenceId);
        Assert.Equal(fixture.ManagerUserId, stored.CreatedByUserId);
        Assert.Equal(fixture.AssignedDestinationId, stored.DestinationId);
        Assert.Equal(fixture.AssignedLocationId, stored.LocationId);
        Assert.Null(stored.PlaceId);
        Assert.Contains(stored.EventTags, eventTag => eventTag.TagId == fixture.EventTagId);

        var page = await GetEventsAsync(manager, fixture.AssignedRegionId, search: "Backend Integrated Event");
        Assert.Single(page.Items);
        Assert.Equal(created.Id, page.Items[0].Id);
    }

    [Fact]
    public async Task Create_event_derives_parent_destination_for_place_and_destination_parents()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        var fixture = await CreateEventsFixtureAsync(app);

        using var manager = await LoginAsync(app, "elena@example.com", "password123");

        var placeResponse = await manager.PostAsJsonAsync(
            "/api/admin/regional/events",
            BuildValidCreateRequest(fixture) with
            {
                ParentEntityType = "Place",
                Name = "Place Parent Event",
                DestinationId = null,
                LocationId = null,
                PlaceId = fixture.PlaceId
            });
        placeResponse.EnsureSuccessStatusCode();
        var placeCreated = await placeResponse.Content.ReadFromJsonAsync<RegionalEventDetailDto>();
        Assert.NotNull(placeCreated);
        Assert.Equal("Place", placeCreated!.ParentEntityType);
        Assert.Equal(fixture.AssignedDestinationId, placeCreated.DestinationId);
        Assert.Equal(fixture.AssignedLocationId, placeCreated.LocationId);
        Assert.Equal(fixture.PlaceId, placeCreated.PlaceId);

        var destinationResponse = await manager.PostAsJsonAsync(
            "/api/admin/regional/events",
            BuildValidCreateRequest(fixture) with
            {
                ParentEntityType = "Destination",
                Name = "Destination Parent Event",
                DestinationId = fixture.AssignedDestinationId,
                LocationId = null,
                PlaceId = null,
                IsAllDay = true,
                StartsAt = DateTime.UtcNow.Date.AddDays(3),
                EndsAt = DateTime.UtcNow.Date.AddDays(3)
            });
        destinationResponse.EnsureSuccessStatusCode();
        var destinationCreated = await destinationResponse.Content.ReadFromJsonAsync<RegionalEventDetailDto>();
        Assert.NotNull(destinationCreated);
        Assert.Equal("Destination", destinationCreated!.ParentEntityType);
        Assert.Equal(fixture.AssignedDestinationId, destinationCreated.DestinationId);
        Assert.Null(destinationCreated.LocationId);
        Assert.Null(destinationCreated.PlaceId);
        Assert.True(destinationCreated.IsAllDay);
    }

    [Fact]
    public async Task Create_event_validates_type_tags_schedule_ticket_and_contact_fields()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        var fixture = await CreateEventsFixtureAsync(app);

        using var manager = await LoginAsync(app, "elena@example.com", "password123");
        var valid = BuildValidCreateRequest(fixture);

        await AssertBadRequestAsync(manager, valid with { EventTypeId = -1 }, "Event category/type is required.");
        await AssertBadRequestAsync(manager, valid with { TagIds = [fixture.NonEventTagId] }, "not compatible with events");
        await AssertBadRequestAsync(manager, valid with { Summary = new string('x', 161) }, "Summary is too long.");
        await AssertBadRequestAsync(manager, valid with { EndsAt = valid.StartsAt!.Value.AddMinutes(-1) }, "end time");
        await AssertBadRequestAsync(manager, valid with { ExternalTicketLink = "javascript:alert(1)" }, "External ticket link is invalid.");
        await AssertBadRequestAsync(manager, valid with { ContactEmail = "not-an-email" }, "Contact email is invalid.");
        await AssertBadRequestAsync(manager, valid with { AttendanceLimit = 0 }, "Attendance limit");
        await AssertBadRequestAsync(manager, valid with { IsFree = false, TicketPrice = "-1" }, "Ticket price");

        var allDayResponse = await manager.PostAsJsonAsync(
            "/api/admin/regional/events",
            valid with
            {
                Name = "All Day Validation Event",
                IsAllDay = true,
                StartsAt = DateTime.UtcNow.Date.AddDays(10),
                EndsAt = DateTime.UtcNow.Date.AddDays(10)
            });
        allDayResponse.EnsureSuccessStatusCode();
        var allDay = await allDayResponse.Content.ReadFromJsonAsync<RegionalEventDetailDto>();
        Assert.NotNull(allDay);
        Assert.True(allDay!.IsAllDay);
    }

    [Fact]
    public async Task Event_selectors_types_and_tags_are_backend_scoped()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        var fixture = await CreateEventsFixtureAsync(app);

        using var manager = await LoginAsync(app, "elena@example.com", "password123");

        var destinations = await manager.GetFromJsonAsync<List<RegionalEventParentDestinationDto>>(
            $"/api/admin/regional/event-parent-destinations?regionId={fixture.AssignedRegionId}");
        Assert.NotNull(destinations);
        Assert.Contains(destinations!, item => item.Id == fixture.AssignedDestinationId);
        Assert.DoesNotContain(destinations!, item => item.Id == fixture.UnassignedDestinationId);

        var locations = await manager.GetFromJsonAsync<List<RegionalEventParentLocationDto>>(
            $"/api/admin/regional/event-parent-locations?regionId={fixture.AssignedRegionId}");
        Assert.NotNull(locations);
        Assert.Contains(locations!, item => item.Id == fixture.AssignedLocationId && item.DestinationId == fixture.AssignedDestinationId);
        Assert.DoesNotContain(locations!, item => item.Id == fixture.UnassignedLocationId);

        var places = await manager.GetFromJsonAsync<List<RegionalEventParentPlaceDto>>(
            $"/api/admin/regional/event-parent-places?regionId={fixture.AssignedRegionId}");
        Assert.NotNull(places);
        Assert.Contains(places!, item =>
            item.Id == fixture.PlaceId &&
            item.LocationId == fixture.AssignedLocationId &&
            item.DestinationId == fixture.AssignedDestinationId);

        var tags = await manager.GetFromJsonAsync<List<RegionalEventTagDto>>(
            $"/api/admin/regional/event-tags?search={Uri.EscapeDataString(fixture.EventTagName)}");
        Assert.NotNull(tags);
        Assert.Single(tags!, item => item.Id == fixture.EventTagId);
        Assert.DoesNotContain(tags!, item => item.Id == fixture.NonEventTagId);

        var types = await manager.GetFromJsonAsync<List<RegionalEventTypeDto>>("/api/admin/regional/event-types");
        Assert.NotNull(types);
        Assert.Contains(types!, item => item.Id == fixture.BaseEventTypeId);
    }

    private static async Task<RegionalEventListResponseDto> GetEventsAsync(
        HttpClient client,
        int regionId,
        string? search = null,
        string sort = "newest",
        int skip = 0,
        int? take = null)
    {
        var url = $"/api/admin/regional/events?regionId={regionId}&sort={sort}&skip={skip}";
        if (take.HasValue)
        {
            url += $"&take={take.Value}";
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            url += "&search=" + Uri.EscapeDataString(search);
        }

        var page = await client.GetFromJsonAsync<RegionalEventListResponseDto>(url);
        Assert.NotNull(page);
        return page!;
    }

    private static async Task<HttpClient> LoginAsync(GeotrackApiFactory app, string email, string password)
    {
        var client = app.CreateClient();

        var loginResponse = await client.PostAsJsonAsync(
            "/api/auth/login",
            new LoginRequestDto
            {
                Email = email,
                Password = password,
                RememberMe = false,
            });
        loginResponse.EnsureSuccessStatusCode();

        var session = await loginResponse.Content.ReadFromJsonAsync<LoginResponseDto>();
        Assert.NotNull(session);
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", session.AccessToken);

        return client;
    }

    private static void AssertDateClose(DateTime expected, DateTime? actual)
    {
        Assert.True(actual.HasValue, "Expected the event date to be present.");

        var difference = (expected - actual.Value).Duration();
        Assert.True(
            difference < TimeSpan.FromMilliseconds(1),
            $"Expected {expected:o}, got {actual.Value:o}.");
    }

    private static RegionalEventCreateRequestDto BuildValidCreateRequest(EventsFixture fixture)
    {
        var startsAt = DateTime.UtcNow.AddDays(7).AddHours(2);
        return new RegionalEventCreateRequestDto
        {
            RegionId = fixture.AssignedRegionId,
            ParentEntityType = "Location",
            DestinationId = fixture.AssignedDestinationId,
            LocationId = fixture.AssignedLocationId,
            EventTypeId = fixture.BaseEventTypeId,
            Name = "Backend Integrated Event",
            Summary = "A real event created through the regional admin API.",
            Description = "This event is persisted by the backend create endpoint.",
            Status = "Published",
            VenueName = "Regional venue hall",
            IsAllDay = false,
            StartsAt = startsAt,
            EndsAt = startsAt.AddHours(3),
            IsRecurring = true,
            IsFree = true,
            TicketPrice = "99",
            ExternalTicketLink = "https://tickets.example.com/regional-event",
            OrganizerName = "Regional Events Office",
            ContactEmail = "events@example.com",
            AttendanceLimit = 250,
            AgeRestriction = "All ages",
            TagIds = [fixture.EventTagId],
            CardImageUrl = "/media/uploads/regional/create-card.webp",
            CoverImageUrl = "/media/uploads/regional/create-cover.webp",
            GalleryImageUrls = ["/media/uploads/regional/create-gallery.webp"]
        };
    }

    private static async Task AssertBadRequestAsync(
        HttpClient client,
        RegionalEventCreateRequestDto request,
        string expectedMessagePart)
    {
        var response = await client.PostAsJsonAsync("/api/admin/regional/events", request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<Dictionary<string, string>>();
        Assert.Contains(expectedMessagePart, body?["message"], StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<EventsFixture> CreateEventsFixtureAsync(GeotrackApiFactory app)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var now = DateTime.UtcNow;
        var territory = await db.Territories.OrderBy(item => item.Id).FirstAsync();
        territory.BoundaryShapeType = "Polygon";
        territory.BoundarySource = "IntegrationTest";
        SetBoundary(db, territory, Polygon(
            (40.0, 15.0),
            (40.0, 22.0),
            (47.0, 22.0),
            (47.0, 15.0)));
        var destinationType = await db.DestinationTypes.OrderBy(item => item.Id).FirstAsync();
        var locationType = await db.LocationTypes.OrderBy(item => item.Id).FirstAsync();
        var placeType = await db.PlaceTypes.OrderBy(item => item.Id).FirstAsync();
        var manager = await db.Users.SingleAsync(item => item.Email == "elena@example.com");
        var regularUser = await db.Users.SingleAsync(item => item.Email == "marko.p@example.com");

        var baseEventType = new EventType
        {
            Name = $"Regional Base Event Type {Guid.NewGuid():N}",
            Description = "Base event type for regional event tests.",
            CreatedAt = now
        };
        var searchEventType = new EventType
        {
            Name = $"Regional Chamber Concert {Guid.NewGuid():N}",
            Description = "Search-specific event type for regional event tests.",
            CreatedAt = now
        };
        db.EventTypes.AddRange(baseEventType, searchEventType);
        await db.SaveChangesAsync();

        var eventTag = new Tag
        {
            Name = $"Regional Event Compatible Tag {Guid.NewGuid():N}",
            Slug = $"regional-event-compatible-{Guid.NewGuid():N}",
            ForEvent = true,
            CreatedAt = now
        };
        var nonEventTag = new Tag
        {
            Name = $"Regional Non Event Tag {Guid.NewGuid():N}",
            Slug = $"regional-non-event-{Guid.NewGuid():N}",
            ForPlace = true,
            CreatedAt = now
        };
        db.Tags.AddRange(eventTag, nonEventTag);
        await db.SaveChangesAsync();

        var assignedRegion = new Region
        {
            Name = $"Events Assigned {Guid.NewGuid():N}",
            TerritoryId = territory.Id,
            CreatedAt = now
        };
        var unassignedRegion = new Region
        {
            Name = $"Events Unassigned {Guid.NewGuid():N}",
            TerritoryId = territory.Id,
            CreatedAt = now
        };
        db.Regions.AddRange(assignedRegion, unassignedRegion);
        await db.SaveChangesAsync();

        SetBoundary(db, assignedRegion, Polygon(
            (41.5, 17.5),
            (41.5, 20.5),
            (44.0, 20.5),
            (44.0, 17.5)));
        SetBoundary(db, unassignedRegion, Polygon(
            (44.5, 18.5),
            (44.5, 19.5),
            (45.5, 19.5),
            (45.5, 18.5)));
        await db.SaveChangesAsync();

        await GrantRegionalManagerCoverageAsync(db, manager, assignedRegion.Id);

        var assignedDestination = new Destination
        {
            Name = "Regional Event Parent Destination",
            DestinationTypeId = destinationType.Id,
            RegionId = assignedRegion.Id,
            Description = "Destination for regional event tests.",
            Latitude = 42,
            Longitude = 18,
            CreatedAt = now
        };
        var unassignedDestination = new Destination
        {
            Name = "Unassigned Regional Event Parent Destination",
            DestinationTypeId = destinationType.Id,
            RegionId = unassignedRegion.Id,
            Description = "This destination must never appear in assigned-region event results.",
            Latitude = 45,
            Longitude = 19,
            CreatedAt = now
        };
        db.Destinations.AddRange(assignedDestination, unassignedDestination);
        await db.SaveChangesAsync();

        var assignedLocation = new Location
        {
            Name = "Regional Event Parent Location",
            DestinationId = assignedDestination.Id,
            LocationTypeId = locationType.Id,
            Latitude = 42,
            Longitude = 18,
            CreatedAt = now
        };
        var unassignedLocation = new Location
        {
            Name = "Unassigned Regional Event Parent Location",
            DestinationId = unassignedDestination.Id,
            LocationTypeId = locationType.Id,
            Latitude = 45,
            Longitude = 19,
            CreatedAt = now
        };
        db.Locations.AddRange(assignedLocation, unassignedLocation);
        await db.SaveChangesAsync();

        var place = new Place
        {
            Name = "Regional Event Venue Place",
            PlaceTypeId = placeType.Id,
            LocationId = assignedLocation.Id,
            Description = "Place venue for regional event tests.",
            Address = "Regional venue address",
            Latitude = 42.1,
            Longitude = 18.1,
            CreatedAt = now
        };
        db.Places.Add(place);
        await db.SaveChangesAsync();

        var events = Enumerable.Range(0, 30)
            .Select(index => new Event
            {
                Name = $"Regional Event {index:D2}",
                EventTypeId = index == 7 ? searchEventType.Id : baseEventType.Id,
                DestinationId = assignedDestination.Id,
                LocationId = assignedLocation.Id,
                PlaceId = index == 7 ? place.Id : null,
                Description = index == 7
                    ? "Searchable event summary with moonlit strings."
                    : $"Event description {index:D2}.",
                StartDate = now.AddDays(index + 1),
                EndDate = now.AddDays(index + 1).AddHours(2),
                TicketPrice = index == 7 ? "Free" : null,
                HostedBy = index == 7 ? "Regional Test Orchestra" : null,
                CreatedAt = now.AddMinutes(-index)
            })
            .ToList();

        var unassignedEvent = new Event
        {
            Name = "Unassigned Regional Event",
            EventTypeId = baseEventType.Id,
            DestinationId = unassignedDestination.Id,
            LocationId = unassignedLocation.Id,
            Description = "This event must never appear in assigned-region results.",
            StartDate = now.AddDays(1),
            EndDate = now.AddDays(1).AddHours(2),
            CreatedAt = now
        };

        db.Events.AddRange(events);
        db.Events.Add(unassignedEvent);
        await db.SaveChangesAsync();

        db.EntityImages.AddRange(
            new EntityImage
            {
                TargetType = EntityImageTargetType.Event,
                EventId = events[7].Id,
                Url = "/media/seed/events/search-card.webp",
                AltText = "Existing search event card",
                IsMain = true,
                SortOrder = 0,
                CreatedAt = now
            },
            new EntityImage
            {
                TargetType = EntityImageTargetType.Event,
                EventId = events[7].Id,
                Url = "/media/seed/events/search-cover.webp",
                AltText = "Existing search event cover",
                IsMain = false,
                SortOrder = 1,
                CreatedAt = now
            });
        await db.SaveChangesAsync();

        db.EventReviews.Add(new EventReview
        {
            UserId = regularUser.Id,
            EventId = events[29].Id,
            Rating = 5,
            Comment = "Related content that protects the event from deletion.",
            CreatedAt = now,
            UpdatedAt = now
        });
        await db.SaveChangesAsync();

        return new EventsFixture(
            assignedRegion.Id,
            unassignedRegion.Id,
            manager.Id,
            assignedDestination.Id,
            unassignedDestination.Id,
            assignedLocation.Id,
            unassignedLocation.Id,
            place.Id,
            baseEventType.Id,
            eventTag.Id,
            eventTag.Name,
            nonEventTag.Id,
            events[7].Id,
            events[28].Id,
            events[29].Id,
            unassignedEvent.Id,
            searchEventType.Name,
            assignedLocation.Name,
            events[7].StartDate,
            events[7].EndDate);
    }

    private static async Task GrantRegionalManagerCoverageAsync(AppDbContext db, User manager, int regionId)
    {
        var role = await db.Roles.SingleAsync(item => item.Name == "RegionalManager");

        if (!await db.UserRoles.AnyAsync(item => item.UserId == manager.Id && item.RoleId == role.Id))
        {
            db.UserRoles.Add(new UserRole
            {
                UserId = manager.Id,
                RoleId = role.Id
            });
        }

        if (!await db.RegionalManagerRegions.AnyAsync(item => item.UserId == manager.Id && item.RegionId == regionId))
        {
            db.RegionalManagerRegions.Add(new RegionalManagerRegion
            {
                UserId = manager.Id,
                RegionId = regionId,
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            });
        }

        await db.SaveChangesAsync();
    }

    private static void SetBoundary(AppDbContext db, Territory territory, Geometry geometry)
    {
        db.Entry(territory)
            .Property<Geometry?>(BoundaryGeometryPropertyName)
            .CurrentValue = geometry;
    }

    private static void SetBoundary(AppDbContext db, Region region, Geometry geometry)
    {
        db.Entry(region)
            .Property<Geometry?>(BoundaryGeometryPropertyName)
            .CurrentValue = geometry;
    }

    private static Polygon Polygon(params (double Latitude, double Longitude)[] points)
    {
        var coordinates = points
            .Select(point => new Coordinate(point.Longitude, point.Latitude))
            .ToList();

        if (!coordinates[0].Equals2D(coordinates[^1]))
        {
            coordinates.Add(new Coordinate(coordinates[0].X, coordinates[0].Y));
        }

        var polygon = GeometryFactory.CreatePolygon(coordinates.ToArray());
        polygon.SRID = 4326;
        return polygon;
    }

    private sealed record EventsFixture(
        int AssignedRegionId,
        int UnassignedRegionId,
        int ManagerUserId,
        int AssignedDestinationId,
        int UnassignedDestinationId,
        int AssignedLocationId,
        int UnassignedLocationId,
        int PlaceId,
        int BaseEventTypeId,
        int EventTagId,
        string EventTagName,
        int NonEventTagId,
        int SearchEventId,
        int BareEventId,
        int EventWithContentId,
        int UnassignedEventId,
        string SearchEventTypeName,
        string SearchLocationName,
        DateTime SearchEventStartsAt,
        DateTime SearchEventEndsAt);
}
