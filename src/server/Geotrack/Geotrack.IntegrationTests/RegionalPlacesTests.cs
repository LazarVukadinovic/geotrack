using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Geotrack.Application.DTOs.Admin;
using Geotrack.Application.DTOs.Auth;
using Geotrack.Domain.Entities.Core;
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

public sealed class RegionalPlacesTests
{
    private const string BoundaryGeometryPropertyName = "BoundaryGeometry";
    private static readonly GeometryFactory GeometryFactory = new(new PrecisionModel(), 4326);

    [Fact]
    public async Task Regional_manager_creates_place_in_assigned_region_with_tags_reference_point_and_hours()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        var fixture = await CreatePlacesFixtureAsync(app);

        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var placeTags = await db.Tags
            .Where(tag => tag.ForPlace)
            .OrderBy(tag => tag.Id)
            .Take(2)
            .ToListAsync();
        Assert.Equal(2, placeTags.Count);
        var placeTypeId = await db.PlaceTypes
            .OrderBy(type => type.Id)
            .Select(type => type.Id)
            .FirstAsync();

        using var manager = await LoginAsync(app, "elena@example.com", "password123");
        var response = await manager.PostAsJsonAsync(
            "/api/admin/regional/places",
            BuildCreateRequest(
                fixture.AssignedRegionId,
                fixture.AssignedLocationId,
                placeTypeId,
                placeTags.Select(tag => tag.Id).ToArray()));

        var responseBody = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.Created, responseBody);
        var created = await response.Content.ReadFromJsonAsync<RegionalPlaceDetailDto>();
        Assert.NotNull(created);
        Assert.Equal(fixture.AssignedRegionId, created!.RegionId);
        Assert.Equal(fixture.AssignedLocationId, created.LocationId);
        Assert.Equal(fixture.AssignedDestinationId, created.DestinationId);
        Assert.Equal("Published", created.Status);
        Assert.Equal("New Regional Place", created.Name);
        Assert.Equal("Short place summary.", created.Summary);
        Assert.StartsWith("PLC-", created.InternalReferenceId);
        Assert.Equal(placeTypeId, created.PlaceTypeId);
        Assert.Equal(placeTags.Select(tag => tag.Id).OrderBy(id => id), created.Tags.Select(tag => tag.Id).OrderBy(id => id));
        Assert.True(created.OperatingHoursEnabled);
        Assert.Equal(2, created.OperatingHours.Count);
        Assert.NotNull(created.PublishedAt);

        var stored = await db.Places
            .Include(place => place.PlaceTags)
            .SingleAsync(place => place.Id == created.Id);

        Assert.Equal(fixture.AssignedLocationId, stored.LocationId);
        Assert.Equal(fixture.ManagerUserId, stored.CreatedByUserId);
        Assert.Equal(created.InternalReferenceId, stored.InternalReferenceId);
        Assert.Equal("Museum lane 1", stored.Address);
        Assert.Equal("https://example.com/place", stored.Website);
        Assert.Equal("+1 555 000 1111", stored.Number);
        Assert.Equal("EUR 10 - EUR 20", stored.AdmissionPriceRange);
        Assert.Equal("https://example.com/book", stored.BookingExternalLink);
        Assert.Equal("Spring mornings", stored.BestTimeToVisit);
        Assert.Equal("Book ahead.", stored.VisitorTips);
        Assert.Equal("North entrance.", stored.AccessNotes);
        Assert.Equal("Step-free route available.", stored.AccessibilityNotes);
        Assert.Equal(2, stored.PlaceTags.Count);

        var coordinates = db.Entry(stored)
            .Property<NetTopologySuite.Geometries.Point?>("Coordinates")
            .CurrentValue;
        Assert.NotNull(coordinates);
        Assert.Equal(4326, coordinates!.SRID);
        Assert.Equal(stored.Longitude, coordinates.X);
        Assert.Equal(stored.Latitude, coordinates.Y);

        var list = await GetPlacesAsync(manager, fixture.AssignedRegionId, search: created.InternalReferenceId);
        Assert.Contains(list.Items, item => item.Id == created.Id);
    }

    [Fact]
    public async Task Create_place_requires_regional_manager_assigned_region_and_in_region_parent_location()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        var fixture = await CreatePlacesFixtureAsync(app);

        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var placeTypeId = await db.PlaceTypes.OrderBy(type => type.Id).Select(type => type.Id).FirstAsync();

        using var anonymous = app.CreateClient();
        var anonymousResponse = await anonymous.PostAsJsonAsync(
            "/api/admin/regional/places",
            BuildCreateRequest(fixture.AssignedRegionId, fixture.AssignedLocationId, placeTypeId));
        Assert.Equal(HttpStatusCode.Unauthorized, anonymousResponse.StatusCode);

        using var regularUser = await LoginAsync(app, "marko.p@example.com", "password123");
        var regularUserResponse = await regularUser.PostAsJsonAsync(
            "/api/admin/regional/places",
            BuildCreateRequest(fixture.AssignedRegionId, fixture.AssignedLocationId, placeTypeId));
        Assert.Equal(HttpStatusCode.Forbidden, regularUserResponse.StatusCode);

        using var manager = await LoginAsync(app, "elena@example.com", "password123");
        var unassignedRegionResponse = await manager.PostAsJsonAsync(
            "/api/admin/regional/places",
            BuildCreateRequest(fixture.UnassignedRegionId, fixture.UnassignedLocationId, placeTypeId));
        Assert.Equal(HttpStatusCode.Forbidden, unassignedRegionResponse.StatusCode);

        var outOfRegionLocationResponse = await manager.PostAsJsonAsync(
            "/api/admin/regional/places",
            BuildCreateRequest(fixture.AssignedRegionId, fixture.UnassignedLocationId, placeTypeId));
        Assert.Equal(HttpStatusCode.BadRequest, outOfRegionLocationResponse.StatusCode);
    }

    [Fact]
    public async Task Create_place_validates_reference_required_fields_type_tags_coordinates_urls_and_hours()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        var fixture = await CreatePlacesFixtureAsync(app);

        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var placeTypeId = await db.PlaceTypes.OrderBy(type => type.Id).Select(type => type.Id).FirstAsync();
        var nonPlaceTag = new Tag
        {
            Name = $"Not Place {Guid.NewGuid():N}",
            Slug = $"not-place-{Guid.NewGuid():N}",
            ForDestination = true,
            ForPlace = false,
            CreatedAt = DateTime.UtcNow
        };
        db.Tags.Add(nonPlaceTag);
        await db.SaveChangesAsync();

        using var manager = await LoginAsync(app, "elena@example.com", "password123");

        var tamperedReference = BuildCreateRequest(fixture.AssignedRegionId, fixture.AssignedLocationId, placeTypeId);
        tamperedReference.InternalReferenceId = "CLIENT-123";
        var tamperedResponse = await manager.PostAsJsonAsync("/api/admin/regional/places", tamperedReference);
        Assert.Equal(HttpStatusCode.BadRequest, tamperedResponse.StatusCode);

        var missingParent = BuildCreateRequest(fixture.AssignedRegionId, fixture.AssignedLocationId, placeTypeId);
        missingParent.LocationId = 0;
        var missingParentResponse = await manager.PostAsJsonAsync("/api/admin/regional/places", missingParent);
        Assert.Equal(HttpStatusCode.BadRequest, missingParentResponse.StatusCode);

        var missingName = BuildCreateRequest(fixture.AssignedRegionId, fixture.AssignedLocationId, placeTypeId);
        missingName.Name = "";
        var missingNameResponse = await manager.PostAsJsonAsync("/api/admin/regional/places", missingName);
        Assert.Equal(HttpStatusCode.BadRequest, missingNameResponse.StatusCode);

        var longSummary = BuildCreateRequest(fixture.AssignedRegionId, fixture.AssignedLocationId, placeTypeId);
        longSummary.Summary = new string('x', 161);
        var longSummaryResponse = await manager.PostAsJsonAsync("/api/admin/regional/places", longSummary);
        Assert.Equal(HttpStatusCode.BadRequest, longSummaryResponse.StatusCode);

        var missingType = BuildCreateRequest(fixture.AssignedRegionId, fixture.AssignedLocationId, placeTypeId);
        missingType.PlaceTypeId = null;
        var missingTypeResponse = await manager.PostAsJsonAsync("/api/admin/regional/places", missingType);
        Assert.Equal(HttpStatusCode.BadRequest, missingTypeResponse.StatusCode);

        var invalidTag = BuildCreateRequest(fixture.AssignedRegionId, fixture.AssignedLocationId, placeTypeId, [nonPlaceTag.Id]);
        var invalidTagResponse = await manager.PostAsJsonAsync("/api/admin/regional/places", invalidTag);
        Assert.Equal(HttpStatusCode.BadRequest, invalidTagResponse.StatusCode);

        var invalidCoordinates = BuildCreateRequest(fixture.AssignedRegionId, fixture.AssignedLocationId, placeTypeId);
        invalidCoordinates.Latitude = 95;
        var invalidCoordinatesResponse = await manager.PostAsJsonAsync("/api/admin/regional/places", invalidCoordinates);
        Assert.Equal(HttpStatusCode.BadRequest, invalidCoordinatesResponse.StatusCode);

        var invalidUrl = BuildCreateRequest(fixture.AssignedRegionId, fixture.AssignedLocationId, placeTypeId);
        invalidUrl.Website = "javascript:alert(1)";
        var invalidUrlResponse = await manager.PostAsJsonAsync("/api/admin/regional/places", invalidUrl);
        Assert.Equal(HttpStatusCode.BadRequest, invalidUrlResponse.StatusCode);

        var invalidHours = BuildCreateRequest(fixture.AssignedRegionId, fixture.AssignedLocationId, placeTypeId);
        invalidHours.OperatingHours[0].OpenTime = "18:00";
        invalidHours.OperatingHours[0].CloseTime = "09:00";
        var invalidHoursResponse = await manager.PostAsJsonAsync("/api/admin/regional/places", invalidHours);
        Assert.Equal(HttpStatusCode.BadRequest, invalidHoursResponse.StatusCode);
    }

    [Fact]
    public async Task Place_selector_tag_and_type_endpoints_are_backend_scoped()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        var fixture = await CreatePlacesFixtureAsync(app);

        using var anonymous = app.CreateClient();
        var anonymousParents = await anonymous.GetAsync(
            $"/api/admin/regional/place-parent-locations?regionId={fixture.AssignedRegionId}");
        Assert.Equal(HttpStatusCode.Unauthorized, anonymousParents.StatusCode);

        using var regularUser = await LoginAsync(app, "marko.p@example.com", "password123");
        var regularTags = await regularUser.GetAsync("/api/admin/regional/place-tags");
        Assert.Equal(HttpStatusCode.Forbidden, regularTags.StatusCode);

        using var manager = await LoginAsync(app, "elena@example.com", "password123");
        var parents = await manager.GetFromJsonAsync<List<RegionalPlaceParentLocationDto>>(
            $"/api/admin/regional/place-parent-locations?regionId={fixture.AssignedRegionId}");
        Assert.NotNull(parents);
        Assert.Contains(parents!, item => item.Id == fixture.AssignedLocationId && item.DestinationId == fixture.AssignedDestinationId);
        Assert.DoesNotContain(parents!, item => item.Id == fixture.UnassignedLocationId);

        var tags = await manager.GetFromJsonAsync<List<RegionalPlaceTagDto>>(
            "/api/admin/regional/place-tags");
        Assert.NotNull(tags);
        Assert.NotEmpty(tags!);

        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var returnedTagIds = tags!.Select(tag => tag.Id).ToList();
        var returnedTagRows = await db.Tags
            .Where(tag => returnedTagIds.Contains(tag.Id))
            .Select(tag => new { tag.Id, tag.ForPlace })
            .ToListAsync();
        Assert.Equal(returnedTagIds.Count, returnedTagRows.Count);
        Assert.All(returnedTagRows, tag => Assert.True(tag.ForPlace));

        var searchableTag = tags.First();
        var searchResponse = await manager.GetFromJsonAsync<List<RegionalPlaceTagDto>>(
            "/api/admin/regional/place-tags?search=" + Uri.EscapeDataString(searchableTag.Name));
        Assert.NotNull(searchResponse);
        Assert.Contains(searchResponse!, tag => tag.Id == searchableTag.Id);

        var types = await manager.GetFromJsonAsync<List<RegionalPlaceTypeDto>>(
            "/api/admin/regional/place-types");
        Assert.NotNull(types);
        Assert.NotEmpty(types!);
    }

    [Fact]
    public async Task Generated_place_internal_reference_ids_are_unique()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        var fixture = await CreatePlacesFixtureAsync(app);

        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var placeTypeId = await db.PlaceTypes.OrderBy(type => type.Id).Select(type => type.Id).FirstAsync();

        using var manager = await LoginAsync(app, "elena@example.com", "password123");

        var firstResponse = await manager.PostAsJsonAsync(
            "/api/admin/regional/places",
            BuildCreateRequest(fixture.AssignedRegionId, fixture.AssignedLocationId, placeTypeId, name: "Unique Place One"));
        firstResponse.EnsureSuccessStatusCode();

        var secondResponse = await manager.PostAsJsonAsync(
            "/api/admin/regional/places",
            BuildCreateRequest(fixture.AssignedRegionId, fixture.AssignedLocationId, placeTypeId, name: "Unique Place Two"));
        secondResponse.EnsureSuccessStatusCode();

        var first = await firstResponse.Content.ReadFromJsonAsync<RegionalPlaceDetailDto>();
        var second = await secondResponse.Content.ReadFromJsonAsync<RegionalPlaceDetailDto>();
        Assert.NotNull(first?.InternalReferenceId);
        Assert.NotNull(second?.InternalReferenceId);
        Assert.NotEqual(first!.InternalReferenceId, second!.InternalReferenceId);
    }

    [Fact]
    public async Task Regional_manager_lists_only_assigned_region_places_with_default_25_item_page()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        var fixture = await CreatePlacesFixtureAsync(app);

        using var anonymous = app.CreateClient();
        var anonymousResponse = await anonymous.GetAsync(
            $"/api/admin/regional/places?regionId={fixture.AssignedRegionId}");
        Assert.Equal(HttpStatusCode.Unauthorized, anonymousResponse.StatusCode);

        using var regularUser = await LoginAsync(app, "marko.p@example.com", "password123");
        var nonRegionalResponse = await regularUser.GetAsync(
            $"/api/admin/regional/places?regionId={fixture.AssignedRegionId}");
        Assert.Equal(HttpStatusCode.Forbidden, nonRegionalResponse.StatusCode);

        using var manager = await LoginAsync(app, "elena@example.com", "password123");
        var unassignedRegionResponse = await manager.GetAsync(
            $"/api/admin/regional/places?regionId={fixture.UnassignedRegionId}");
        Assert.Equal(HttpStatusCode.Forbidden, unassignedRegionResponse.StatusCode);

        var page = await GetPlacesAsync(manager, fixture.AssignedRegionId);

        Assert.Equal(30, page.TotalCount);
        Assert.Equal(0, page.Skip);
        Assert.Equal(25, page.Take);
        Assert.Equal(25, page.Items.Count);
        Assert.All(page.Items, item => Assert.Equal(fixture.AssignedRegionId, item.RegionId));
        Assert.DoesNotContain(page.Items, item => item.Id == fixture.UnassignedPlaceId);
        Assert.Contains(page.Items, item => item.Id == fixture.StayPlaceId && item.StayType == fixture.StayTypeName);
    }

    [Fact]
    public async Task Pagination_search_and_sort_use_backend_query_parameters()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        var fixture = await CreatePlacesFixtureAsync(app);

        using var manager = await LoginAsync(app, "elena@example.com", "password123");

        var nextPage = await GetPlacesAsync(manager, fixture.AssignedRegionId, skip: 25, take: 10);
        Assert.Equal(30, nextPage.TotalCount);
        Assert.Equal(25, nextPage.Skip);
        Assert.Equal(10, nextPage.Take);
        Assert.Equal(5, nextPage.Items.Count);

        var searchedByName = await GetPlacesAsync(
            manager,
            fixture.AssignedRegionId,
            search: "Regional Place 07");
        Assert.Single(searchedByName.Items);
        Assert.Equal(fixture.SearchPlaceId, searchedByName.Items[0].Id);

        var searchedBySummary = await GetPlacesAsync(
            manager,
            fixture.AssignedRegionId,
            search: "vaulted galleries");
        Assert.Single(searchedBySummary.Items);
        Assert.Equal(fixture.SearchPlaceId, searchedBySummary.Items[0].Id);

        var searchedByType = await GetPlacesAsync(
            manager,
            fixture.AssignedRegionId,
            search: fixture.SearchPlaceTypeName);
        Assert.Single(searchedByType.Items);
        Assert.Equal(fixture.SearchPlaceId, searchedByType.Items[0].Id);

        var searchedByStayType = await GetPlacesAsync(
            manager,
            fixture.AssignedRegionId,
            search: fixture.StayTypeName);
        Assert.Single(searchedByStayType.Items);
        Assert.Equal(fixture.StayPlaceId, searchedByStayType.Items[0].Id);

        var newest = await GetPlacesAsync(manager, fixture.AssignedRegionId, sort: "newest", take: 3);
        Assert.Equal("Regional Place 00", newest.Items[0].Name);

        var oldest = await GetPlacesAsync(manager, fixture.AssignedRegionId, sort: "oldest", take: 3);
        Assert.Equal("Regional Place 29", oldest.Items[0].Name);

        var nameAsc = await GetPlacesAsync(manager, fixture.AssignedRegionId, sort: "nameAsc", take: 3);
        Assert.Equal("Regional Place 00", nameAsc.Items[0].Name);

        var nameDesc = await GetPlacesAsync(manager, fixture.AssignedRegionId, sort: "nameDesc", take: 3);
        Assert.Equal("Regional Place 29", nameDesc.Items[0].Name);
    }

    [Fact]
    public async Task Delete_place_is_region_scoped_and_blocks_related_content()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        var fixture = await CreatePlacesFixtureAsync(app);

        using var manager = await LoginAsync(app, "elena@example.com", "password123");

        var outOfRegionResponse = await manager.DeleteAsync(
            $"/api/admin/regional/places/{fixture.UnassignedPlaceId}?regionId={fixture.AssignedRegionId}");
        Assert.Equal(HttpStatusCode.NotFound, outOfRegionResponse.StatusCode);

        var blockedResponse = await manager.DeleteAsync(
            $"/api/admin/regional/places/{fixture.PlaceWithContentId}?regionId={fixture.AssignedRegionId}");
        Assert.Equal(HttpStatusCode.Conflict, blockedResponse.StatusCode);
        var blockedBody = await blockedResponse.Content.ReadFromJsonAsync<Dictionary<string, string>>();
        Assert.Equal("This place cannot be deleted while it has related content.", blockedBody?["message"]);

        var deleteResponse = await manager.DeleteAsync(
            $"/api/admin/regional/places/{fixture.BarePlaceId}?regionId={fixture.AssignedRegionId}");
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.False(await db.Places.AnyAsync(item => item.Id == fixture.BarePlaceId));
    }

    [Fact]
    public async Task Detail_and_update_are_restricted_to_assigned_region()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        var fixture = await CreatePlacesFixtureAsync(app);

        using var manager = await LoginAsync(app, "elena@example.com", "password123");

        var outDetail = await manager.GetAsync(
            $"/api/admin/regional/places/{fixture.UnassignedPlaceId}?regionId={fixture.AssignedRegionId}");
        Assert.Equal(HttpStatusCode.NotFound, outDetail.StatusCode);

        var updateResponse = await manager.PutAsJsonAsync(
            $"/api/admin/regional/places/{fixture.SearchPlaceId}?regionId={fixture.AssignedRegionId}",
            new RegionalPlaceUpdateRequestDto
            {
                Name = "Regional Place 07 Updated",
                Description = "Updated place description.",
                Latitude = 42.77,
                Longitude = 18.77
            });
        updateResponse.EnsureSuccessStatusCode();

        var updated = await updateResponse.Content.ReadFromJsonAsync<RegionalPlaceDetailDto>();
        Assert.NotNull(updated);
        Assert.Equal(fixture.AssignedRegionId, updated!.RegionId);
        Assert.Equal("Regional Place 07 Updated", updated.Name);
        Assert.Equal("Updated place description.", updated.Description);
        Assert.Equal(42.77, updated.Latitude);
        Assert.Equal(18.77, updated.Longitude);
    }

    private static async Task<RegionalPlaceListResponseDto> GetPlacesAsync(
        HttpClient client,
        int regionId,
        string? search = null,
        string sort = "newest",
        int skip = 0,
        int? take = null)
    {
        var url = $"/api/admin/regional/places?regionId={regionId}&sort={sort}&skip={skip}";
        if (take.HasValue)
        {
            url += $"&take={take.Value}";
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            url += "&search=" + Uri.EscapeDataString(search);
        }

        var page = await client.GetFromJsonAsync<RegionalPlaceListResponseDto>(url);
        Assert.NotNull(page);
        return page!;
    }

    private static RegionalPlaceCreateRequestDto BuildCreateRequest(
        int regionId,
        int locationId,
        int placeTypeId,
        int[]? tagIds = null,
        string name = "New Regional Place")
    {
        return new RegionalPlaceCreateRequestDto
        {
            RegionId = regionId,
            LocationId = locationId,
            PlaceTypeId = placeTypeId,
            Name = name,
            Summary = "Short place summary.",
            Description = "Longer place description.",
            Status = "Published",
            Latitude = 42.1234,
            Longitude = 18.5678,
            StreetAddress = "Museum lane 1",
            BestTimeToVisit = "Spring mornings",
            VisitorTips = "Book ahead.",
            AccessNotes = "North entrance.",
            AccessibilityNotes = "Step-free route available.",
            Website = "https://example.com/place",
            ContactPhone = "+1 555 000 1111",
            AdmissionPriceRange = "EUR 10 - EUR 20",
            BookingExternalLink = "https://example.com/book",
            OperatingHoursEnabled = true,
            OperatingHours =
            [
                new() { DayOfWeek = "Monday", IsOpen = true, OpenTime = "09:00", CloseTime = "17:00" },
                new() { DayOfWeek = "Tuesday", IsOpen = false }
            ],
            TagIds = tagIds?.ToList() ?? []
        };
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

    private static async Task<PlacesFixture> CreatePlacesFixtureAsync(GeotrackApiFactory app)
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
        var eventType = await db.EventTypes.OrderBy(item => item.Id).FirstAsync();
        var manager = await db.Users.SingleAsync(item => item.Email == "elena@example.com");

        var basePlaceType = new PlaceType
        {
            Name = $"Regional Base Place Type {Guid.NewGuid():N}",
            Description = "Base place type for regional place tests.",
            CreatedAt = now
        };
        var searchPlaceType = new PlaceType
        {
            Name = $"Regional Gallery Place Type {Guid.NewGuid():N}",
            Description = "Search-specific place type for regional place tests.",
            CreatedAt = now
        };
        var accommodationPlaceType = new PlaceType
        {
            Name = $"Regional Accommodation Place Type {Guid.NewGuid():N}",
            Description = "Accommodation place type for regional place tests.",
            CreatedAt = now
        };
        var stayType = new StayType
        {
            Name = $"Boutique Lodge {Guid.NewGuid():N}",
            Slug = $"boutique-lodge-{Guid.NewGuid():N}",
            Description = "Stay type for accommodation inclusion tests.",
            CreatedAt = now
        };
        db.PlaceTypes.AddRange(basePlaceType, searchPlaceType, accommodationPlaceType);
        db.StayTypes.Add(stayType);
        await db.SaveChangesAsync();

        var assignedRegion = new Region
        {
            Name = $"Places Assigned {Guid.NewGuid():N}",
            TerritoryId = territory.Id,
            CreatedAt = now
        };
        var unassignedRegion = new Region
        {
            Name = $"Places Unassigned {Guid.NewGuid():N}",
            TerritoryId = territory.Id,
            CreatedAt = now
        };
        db.Regions.AddRange(assignedRegion, unassignedRegion);
        await db.SaveChangesAsync();

        await GrantRegionalManagerCoverageAsync(db, manager, assignedRegion.Id);

        var assignedDestination = new Destination
        {
            Name = "Regional Place Parent Destination",
            DestinationTypeId = destinationType.Id,
            RegionId = assignedRegion.Id,
            Description = "Destination for regional place tests.",
            Latitude = 42,
            Longitude = 18,
            CreatedAt = now
        };
        var unassignedDestination = new Destination
        {
            Name = "Unassigned Regional Place Parent Destination",
            DestinationTypeId = destinationType.Id,
            RegionId = unassignedRegion.Id,
            Description = "This destination must never appear in assigned-region place results.",
            Latitude = 45,
            Longitude = 19,
            CreatedAt = now
        };
        db.Destinations.AddRange(assignedDestination, unassignedDestination);
        await db.SaveChangesAsync();

        var assignedLocation = new Location
        {
            Name = "Regional Place Parent Location",
            DestinationId = assignedDestination.Id,
            LocationTypeId = locationType.Id,
            Latitude = 42,
            Longitude = 18,
            CreatedAt = now
        };
        var unassignedLocation = new Location
        {
            Name = "Unassigned Regional Place Parent Location",
            DestinationId = unassignedDestination.Id,
            LocationTypeId = locationType.Id,
            Latitude = 45,
            Longitude = 19,
            CreatedAt = now
        };
        db.Locations.AddRange(assignedLocation, unassignedLocation);
        await db.SaveChangesAsync();

        var places = Enumerable.Range(0, 30)
            .Select(index => new Place
            {
                Name = $"Regional Place {index:D2}",
                PlaceTypeId = index == 7
                    ? searchPlaceType.Id
                    : index == 5 ? accommodationPlaceType.Id : basePlaceType.Id,
                LocationId = assignedLocation.Id,
                Description = index == 7
                    ? "Searchable place summary with vaulted galleries."
                    : $"Place description {index:D2}.",
                Latitude = 42 + index / 100d,
                Longitude = 18 + index / 100d,
                CreatedAt = now.AddMinutes(-index)
            })
            .ToList();

        var unassignedPlace = new Place
        {
            Name = "Unassigned Regional Place",
            PlaceTypeId = basePlaceType.Id,
            LocationId = unassignedLocation.Id,
            Description = "This place must never appear in assigned-region results.",
            Latitude = 45,
            Longitude = 19,
            CreatedAt = now
        };

        db.Places.AddRange(places);
        db.Places.Add(unassignedPlace);
        await db.SaveChangesAsync();

        db.Stays.Add(new Stay
        {
            PlaceId = places[5].Id,
            StayTypeId = stayType.Id
        });

        db.Events.Add(new Event
        {
            Name = "Place Related Event",
            EventTypeId = eventType.Id,
            DestinationId = assignedDestination.Id,
            LocationId = assignedLocation.Id,
            PlaceId = places[29].Id,
            Description = "Related content that protects the place from deletion.",
            StartDate = now.AddDays(1),
            EndDate = now.AddDays(1).AddHours(2),
            CreatedAt = now
        });
        await db.SaveChangesAsync();

        return new PlacesFixture(
            assignedRegion.Id,
            unassignedRegion.Id,
            manager.Id,
            assignedDestination.Id,
            assignedLocation.Id,
            unassignedLocation.Id,
            places[7].Id,
            places[28].Id,
            places[29].Id,
            unassignedPlace.Id,
            places[5].Id,
            searchPlaceType.Name,
            stayType.Name);
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

    private sealed record PlacesFixture(
        int AssignedRegionId,
        int UnassignedRegionId,
        int ManagerUserId,
        int AssignedDestinationId,
        int AssignedLocationId,
        int UnassignedLocationId,
        int SearchPlaceId,
        int BarePlaceId,
        int PlaceWithContentId,
        int UnassignedPlaceId,
        int StayPlaceId,
        string SearchPlaceTypeName,
        string StayTypeName);
}
