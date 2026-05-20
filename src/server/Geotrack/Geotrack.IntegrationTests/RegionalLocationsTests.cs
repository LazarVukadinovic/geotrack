using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Geotrack.Application.DTOs.Admin;
using Geotrack.Application.DTOs.Auth;
using Geotrack.Domain.Entities.Core;
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

public sealed class RegionalLocationsTests
{
    private static readonly GeometryFactory GeometryFactory = new(new PrecisionModel(), 4326);

    [Fact]
    public async Task Regional_manager_creates_location_in_assigned_region_with_tags_reference_and_boundary()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        var fixture = await CreateLocationsFixtureAsync(app);

        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var locationTags = await db.Tags
            .Where(tag => tag.ForLocation)
            .OrderBy(tag => tag.Id)
            .Take(2)
            .ToListAsync();
        Assert.Equal(2, locationTags.Count);
        var locationTypeId = await db.LocationTypes
            .OrderBy(type => type.Id)
            .Select(type => type.Id)
            .FirstAsync();

        using var manager = await LoginAsync(app, "elena@example.com", "password123");
        var response = await manager.PostAsJsonAsync(
            "/api/admin/regional/locations",
            BuildCreateRequest(
                fixture.AssignedRegionId,
                fixture.AssignedDestinationId,
                locationTypeId,
                locationTags.Select(tag => tag.Id).ToArray()));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<RegionalLocationDetailDto>();
        Assert.NotNull(created);
        Assert.Equal(fixture.AssignedRegionId, created!.RegionId);
        Assert.Equal(fixture.AssignedDestinationId, created.DestinationId);
        Assert.Equal("Published", created.Status);
        Assert.Equal("New Regional Location", created.Name);
        Assert.Equal("Short location summary.", created.Summary);
        Assert.StartsWith("LOC-", created.InternalReferenceId);
        Assert.Equal(locationTypeId, created.LocationTypeId);
        Assert.Equal(locationTags.Select(tag => tag.Id).OrderBy(id => id), created.Tags.Select(tag => tag.Id).OrderBy(id => id));
        Assert.NotNull(created.PublishedAt);

        var stored = await db.Locations
            .Include(location => location.LocationTags)
            .SingleAsync(location => location.Id == created.Id);

        Assert.Equal(fixture.AssignedDestinationId, stored.DestinationId);
        Assert.Equal(fixture.ManagerUserId, stored.CreatedByUserId);
        Assert.Equal(created.InternalReferenceId, stored.InternalReferenceId);
        Assert.Equal("North edge boundary note.", stored.BoundaryNote);
        Assert.Equal("Polygon", stored.BoundaryShapeType);
        Assert.Equal(2, stored.LocationTags.Count);

        var boundary = db.Entry(stored)
            .Property<Geometry?>("BoundaryGeometry")
            .CurrentValue;
        Assert.NotNull(boundary);
        Assert.Equal(4326, boundary!.SRID);
        Assert.False(boundary.IsEmpty);

        var list = await GetLocationsAsync(manager, fixture.AssignedRegionId, search: created.InternalReferenceId);
        Assert.Contains(list.Items, item => item.Id == created.Id);
    }

    [Fact]
    public async Task Create_location_requires_regional_manager_assigned_region_and_in_region_parent_destination()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        var fixture = await CreateLocationsFixtureAsync(app);

        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var locationTypeId = await db.LocationTypes.OrderBy(type => type.Id).Select(type => type.Id).FirstAsync();

        using var anonymous = app.CreateClient();
        var anonymousResponse = await anonymous.PostAsJsonAsync(
            "/api/admin/regional/locations",
            BuildCreateRequest(fixture.AssignedRegionId, fixture.AssignedDestinationId, locationTypeId));
        Assert.Equal(HttpStatusCode.Unauthorized, anonymousResponse.StatusCode);

        using var regularUser = await LoginAsync(app, "marko.p@example.com", "password123");
        var regularUserResponse = await regularUser.PostAsJsonAsync(
            "/api/admin/regional/locations",
            BuildCreateRequest(fixture.AssignedRegionId, fixture.AssignedDestinationId, locationTypeId));
        Assert.Equal(HttpStatusCode.Forbidden, regularUserResponse.StatusCode);

        using var manager = await LoginAsync(app, "elena@example.com", "password123");
        var unassignedRegionResponse = await manager.PostAsJsonAsync(
            "/api/admin/regional/locations",
            BuildCreateRequest(fixture.UnassignedRegionId, fixture.UnassignedDestinationId, locationTypeId));
        Assert.Equal(HttpStatusCode.Forbidden, unassignedRegionResponse.StatusCode);

        var outOfRegionDestinationResponse = await manager.PostAsJsonAsync(
            "/api/admin/regional/locations",
            BuildCreateRequest(fixture.AssignedRegionId, fixture.UnassignedDestinationId, locationTypeId));
        Assert.Equal(HttpStatusCode.BadRequest, outOfRegionDestinationResponse.StatusCode);
    }

    [Fact]
    public async Task Create_location_validates_reference_required_fields_summary_tags_and_geometry()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        var fixture = await CreateLocationsFixtureAsync(app);

        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var locationTypeId = await db.LocationTypes.OrderBy(type => type.Id).Select(type => type.Id).FirstAsync();
        var nonLocationTag = await db.Tags.FirstAsync(tag => !tag.ForLocation);

        using var manager = await LoginAsync(app, "elena@example.com", "password123");

        var tamperedReference = BuildCreateRequest(fixture.AssignedRegionId, fixture.AssignedDestinationId, locationTypeId);
        tamperedReference.InternalReferenceId = "CLIENT-123";
        var tamperedResponse = await manager.PostAsJsonAsync("/api/admin/regional/locations", tamperedReference);
        Assert.Equal(HttpStatusCode.BadRequest, tamperedResponse.StatusCode);

        var missingParent = BuildCreateRequest(fixture.AssignedRegionId, fixture.AssignedDestinationId, locationTypeId);
        missingParent.DestinationId = 0;
        var missingParentResponse = await manager.PostAsJsonAsync("/api/admin/regional/locations", missingParent);
        Assert.Equal(HttpStatusCode.BadRequest, missingParentResponse.StatusCode);

        var missingName = BuildCreateRequest(fixture.AssignedRegionId, fixture.AssignedDestinationId, locationTypeId);
        missingName.Name = "";
        var missingNameResponse = await manager.PostAsJsonAsync("/api/admin/regional/locations", missingName);
        Assert.Equal(HttpStatusCode.BadRequest, missingNameResponse.StatusCode);

        var longSummary = BuildCreateRequest(fixture.AssignedRegionId, fixture.AssignedDestinationId, locationTypeId);
        longSummary.Summary = new string('x', 161);
        var longSummaryResponse = await manager.PostAsJsonAsync("/api/admin/regional/locations", longSummary);
        Assert.Equal(HttpStatusCode.BadRequest, longSummaryResponse.StatusCode);

        var invalidTag = BuildCreateRequest(fixture.AssignedRegionId, fixture.AssignedDestinationId, locationTypeId, [nonLocationTag.Id]);
        var invalidTagResponse = await manager.PostAsJsonAsync("/api/admin/regional/locations", invalidTag);
        Assert.Equal(HttpStatusCode.BadRequest, invalidTagResponse.StatusCode);

        var invalidGeometry = BuildCreateRequest(fixture.AssignedRegionId, fixture.AssignedDestinationId, locationTypeId);
        invalidGeometry.Boundary = new RegionalLocationBoundaryDto
        {
            ShapeType = "Polygon",
            Coordinates =
            [
                new() { Latitude = 95, Longitude = 19 },
                new() { Latitude = 43, Longitude = 19.2 },
                new() { Latitude = 43.2, Longitude = 19.1 }
            ]
        };
        var invalidGeometryResponse = await manager.PostAsJsonAsync("/api/admin/regional/locations", invalidGeometry);
        Assert.Equal(HttpStatusCode.BadRequest, invalidGeometryResponse.StatusCode);
    }

    [Fact]
    public async Task Create_location_rejects_boundary_outside_or_partially_outside_parent_destination()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        var fixture = await CreateLocationsFixtureAsync(app);

        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var locationTypeId = await db.LocationTypes.OrderBy(type => type.Id).Select(type => type.Id).FirstAsync();

        using var manager = await LoginAsync(app, "elena@example.com", "password123");

        var outside = BuildCreateRequest(fixture.AssignedRegionId, fixture.AssignedDestinationId, locationTypeId);
        outside.Boundary!.Coordinates =
        [
            new() { Latitude = 43.4, Longitude = 19.4 },
            new() { Latitude = 43.4, Longitude = 19.5 },
            new() { Latitude = 43.5, Longitude = 19.5 },
            new() { Latitude = 43.5, Longitude = 19.4 }
        ];
        var outsideResponse = await manager.PostAsJsonAsync("/api/admin/regional/locations", outside);
        Assert.Equal(HttpStatusCode.BadRequest, outsideResponse.StatusCode);

        var partiallyOutside = BuildCreateRequest(fixture.AssignedRegionId, fixture.AssignedDestinationId, locationTypeId);
        partiallyOutside.Boundary!.Coordinates =
        [
            new() { Latitude = 43.1, Longitude = 19.1 },
            new() { Latitude = 43.1, Longitude = 19.4 },
            new() { Latitude = 43.2, Longitude = 19.4 },
            new() { Latitude = 43.2, Longitude = 19.1 }
        ];
        var partiallyOutsideResponse = await manager.PostAsJsonAsync("/api/admin/regional/locations", partiallyOutside);
        Assert.Equal(HttpStatusCode.BadRequest, partiallyOutsideResponse.StatusCode);
    }

    [Fact]
    public async Task Create_location_allows_boundary_touching_parent_destination_boundary()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        var fixture = await CreateLocationsFixtureAsync(app);

        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var locationTypeId = await db.LocationTypes.OrderBy(type => type.Id).Select(type => type.Id).FirstAsync();

        using var manager = await LoginAsync(app, "elena@example.com", "password123");

        var touching = BuildCreateRequest(fixture.AssignedRegionId, fixture.AssignedDestinationId, locationTypeId);
        touching.Boundary!.Coordinates =
        [
            new() { Latitude = 42.9, Longitude = 18.9 },
            new() { Latitude = 42.9, Longitude = 19.1 },
            new() { Latitude = 43.1, Longitude = 19.1 },
            new() { Latitude = 43.1, Longitude = 18.9 }
        ];

        var response = await manager.PostAsJsonAsync("/api/admin/regional/locations", touching);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task Create_location_with_zone_requires_parent_destination_boundary()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        var fixture = await CreateLocationsFixtureAsync(app);

        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var destinationTypeId = await db.DestinationTypes.OrderBy(type => type.Id).Select(type => type.Id).FirstAsync();
        var locationTypeId = await db.LocationTypes.OrderBy(type => type.Id).Select(type => type.Id).FirstAsync();

        var noBoundaryDestination = new Destination
        {
            Name = "Destination Without Boundary",
            DestinationTypeId = destinationTypeId,
            RegionId = fixture.AssignedRegionId,
            Description = "No boundary destination.",
            Latitude = 43,
            Longitude = 19,
            CreatedAt = DateTime.UtcNow
        };
        db.Destinations.Add(noBoundaryDestination);
        await db.SaveChangesAsync();

        using var manager = await LoginAsync(app, "elena@example.com", "password123");
        var request = BuildCreateRequest(fixture.AssignedRegionId, noBoundaryDestination.Id, locationTypeId);
        var response = await manager.PostAsJsonAsync("/api/admin/regional/locations", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<Dictionary<string, string>>();
        Assert.Equal("Parent destination must have a boundary before adding location zones.", body?["message"]);
    }

    [Fact]
    public async Task Location_image_upload_and_create_persist_entity_images()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        var fixture = await CreateLocationsFixtureAsync(app);

        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var locationTypeId = await db.LocationTypes.OrderBy(type => type.Id).Select(type => type.Id).FirstAsync();

        using var manager = await LoginAsync(app, "elena@example.com", "password123");
        var upload = await UploadOneImageAsync(manager, fixture.AssignedRegionId);

        var request = BuildCreateRequest(fixture.AssignedRegionId, fixture.AssignedDestinationId, locationTypeId);
        request.CardImageUrl = upload.Url;
        request.CoverImageUrl = upload.Url;

        var response = await manager.PostAsJsonAsync("/api/admin/regional/locations", request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var created = await response.Content.ReadFromJsonAsync<RegionalLocationDetailDto>();
        Assert.NotNull(created);
        Assert.Equal(upload.Url, created!.ImageUrl);
        Assert.Equal(upload.Url, created.CardImageUrl);
        Assert.Equal(upload.Url, created.CoverImageUrl);
        Assert.Empty(created.GalleryImageUrls);

        var storedImages = await db.EntityImages
            .Where(image => image.LocationId == created.Id)
            .OrderBy(image => image.Id)
            .ToListAsync();
        Assert.Equal(2, storedImages.Count);
        Assert.Contains(storedImages, image =>
            image.TargetType == EntityImageTargetType.Location &&
            image.ImageRole == EntityImageRole.Card &&
            image.Url == upload.Url &&
            image.IsMain);
        Assert.Contains(storedImages, image =>
            image.TargetType == EntityImageTargetType.Location &&
            image.ImageRole == EntityImageRole.Cover &&
            image.Url == upload.Url &&
            !image.IsMain);
    }

    [Fact]
    public async Task Location_selector_tag_and_type_endpoints_are_backend_scoped()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        var fixture = await CreateLocationsFixtureAsync(app);

        using var manager = await LoginAsync(app, "elena@example.com", "password123");

        var parents = await manager.GetFromJsonAsync<List<RegionalLocationParentDestinationDto>>(
            $"/api/admin/regional/location-parent-destinations?regionId={fixture.AssignedRegionId}");
        Assert.NotNull(parents);
        Assert.Contains(parents!, item => item.Id == fixture.AssignedDestinationId);
        Assert.DoesNotContain(parents!, item => item.Id == fixture.UnassignedDestinationId);

        var tags = await manager.GetFromJsonAsync<List<RegionalLocationTagDto>>(
            "/api/admin/regional/location-tags");
        Assert.NotNull(tags);
        Assert.NotEmpty(tags!);

        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var returnedTagIds = tags!.Select(tag => tag.Id).ToList();
        var returnedTagRows = await db.Tags
            .Where(tag => returnedTagIds.Contains(tag.Id))
            .Select(tag => new { tag.Id, tag.ForLocation })
            .ToListAsync();
        Assert.Equal(returnedTagIds.Count, returnedTagRows.Count);
        Assert.All(returnedTagRows, tag => Assert.True(tag.ForLocation));

        var nonLocationTag = await db.Tags.FirstAsync(tag => !tag.ForLocation);
        Assert.DoesNotContain(tags, tag => tag.Id == nonLocationTag.Id);

        var searchableTag = tags.First();
        var searchResponse = await manager.GetFromJsonAsync<List<RegionalLocationTagDto>>(
            "/api/admin/regional/location-tags?search=" + Uri.EscapeDataString(searchableTag.Name));
        Assert.NotNull(searchResponse);
        Assert.Contains(searchResponse!, tag => tag.Id == searchableTag.Id);

        var types = await manager.GetFromJsonAsync<List<RegionalLocationTypeDto>>(
            "/api/admin/regional/location-types");
        Assert.NotNull(types);
        Assert.NotEmpty(types!);
    }

    [Fact]
    public async Task Regional_manager_lists_only_assigned_region_locations_with_default_25_item_page()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        var fixture = await CreateLocationsFixtureAsync(app);

        using var anonymous = app.CreateClient();
        var anonymousResponse = await anonymous.GetAsync(
            $"/api/admin/regional/locations?regionId={fixture.AssignedRegionId}");
        Assert.Equal(HttpStatusCode.Unauthorized, anonymousResponse.StatusCode);

        using var regularUser = await LoginAsync(app, "marko.p@example.com", "password123");
        var nonRegionalResponse = await regularUser.GetAsync(
            $"/api/admin/regional/locations?regionId={fixture.AssignedRegionId}");
        Assert.Equal(HttpStatusCode.Forbidden, nonRegionalResponse.StatusCode);

        using var manager = await LoginAsync(app, "elena@example.com", "password123");
        var unassignedRegionResponse = await manager.GetAsync(
            $"/api/admin/regional/locations?regionId={fixture.UnassignedRegionId}");
        Assert.Equal(HttpStatusCode.Forbidden, unassignedRegionResponse.StatusCode);

        var page = await GetLocationsAsync(manager, fixture.AssignedRegionId);

        Assert.Equal(30, page.TotalCount);
        Assert.Equal(0, page.Skip);
        Assert.Equal(25, page.Take);
        Assert.Equal(25, page.Items.Count);
        Assert.All(page.Items, item => Assert.Equal(fixture.AssignedRegionId, item.RegionId));
        Assert.DoesNotContain(page.Items, item => item.Id == fixture.UnassignedLocationId);
    }

    [Fact]
    public async Task Pagination_search_and_sort_use_backend_query_parameters()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        var fixture = await CreateLocationsFixtureAsync(app);

        using var manager = await LoginAsync(app, "elena@example.com", "password123");

        var nextPage = await GetLocationsAsync(manager, fixture.AssignedRegionId, skip: 25, take: 10);
        Assert.Equal(30, nextPage.TotalCount);
        Assert.Equal(25, nextPage.Skip);
        Assert.Equal(10, nextPage.Take);
        Assert.Equal(5, nextPage.Items.Count);

        var searchedByName = await GetLocationsAsync(
            manager,
            fixture.AssignedRegionId,
            search: "Regional Location 07");
        Assert.Single(searchedByName.Items);
        Assert.Equal(fixture.SearchLocationId, searchedByName.Items[0].Id);

        var searchedByRelatedSummary = await GetLocationsAsync(
            manager,
            fixture.AssignedRegionId,
            search: "sunlit promenades");
        Assert.Single(searchedByRelatedSummary.Items);
        Assert.Equal(fixture.SearchLocationId, searchedByRelatedSummary.Items[0].Id);

        var newest = await GetLocationsAsync(manager, fixture.AssignedRegionId, sort: "newest", take: 3);
        Assert.Equal("Regional Location 00", newest.Items[0].Name);

        var oldest = await GetLocationsAsync(manager, fixture.AssignedRegionId, sort: "oldest", take: 3);
        Assert.Equal("Regional Location 29", oldest.Items[0].Name);

        var nameAsc = await GetLocationsAsync(manager, fixture.AssignedRegionId, sort: "nameAsc", take: 3);
        Assert.Equal("Regional Location 00", nameAsc.Items[0].Name);

        var nameDesc = await GetLocationsAsync(manager, fixture.AssignedRegionId, sort: "nameDesc", take: 3);
        Assert.Equal("Regional Location 29", nameDesc.Items[0].Name);
    }

    [Fact]
    public async Task Delete_location_is_region_scoped_and_blocks_related_content()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        var fixture = await CreateLocationsFixtureAsync(app);

        using var manager = await LoginAsync(app, "elena@example.com", "password123");

        var outOfRegionResponse = await manager.DeleteAsync(
            $"/api/admin/regional/locations/{fixture.UnassignedLocationId}?regionId={fixture.AssignedRegionId}");
        Assert.Equal(HttpStatusCode.NotFound, outOfRegionResponse.StatusCode);

        var blockedResponse = await manager.DeleteAsync(
            $"/api/admin/regional/locations/{fixture.LocationWithContentId}?regionId={fixture.AssignedRegionId}");
        Assert.Equal(HttpStatusCode.Conflict, blockedResponse.StatusCode);
        var blockedBody = await blockedResponse.Content.ReadFromJsonAsync<Dictionary<string, string>>();
        Assert.Equal("This location cannot be deleted while it has related content.", blockedBody?["message"]);

        var deleteResponse = await manager.DeleteAsync(
            $"/api/admin/regional/locations/{fixture.BareLocationId}?regionId={fixture.AssignedRegionId}");
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.False(await db.Locations.AnyAsync(item => item.Id == fixture.BareLocationId));
    }

    [Fact]
    public async Task Detail_and_update_are_restricted_to_assigned_region()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        var fixture = await CreateLocationsFixtureAsync(app);

        using var manager = await LoginAsync(app, "elena@example.com", "password123");

        var outDetail = await manager.GetAsync(
            $"/api/admin/regional/locations/{fixture.UnassignedLocationId}?regionId={fixture.AssignedRegionId}");
        Assert.Equal(HttpStatusCode.NotFound, outDetail.StatusCode);

        var updateResponse = await manager.PutAsJsonAsync(
            $"/api/admin/regional/locations/{fixture.SearchLocationId}?regionId={fixture.AssignedRegionId}",
            new RegionalLocationUpdateRequestDto
            {
                Name = "Regional Location 07 Updated",
                Latitude = 42.77,
                Longitude = 18.77
            });
        updateResponse.EnsureSuccessStatusCode();

        var updated = await updateResponse.Content.ReadFromJsonAsync<RegionalLocationDetailDto>();
        Assert.NotNull(updated);
        Assert.Equal(fixture.AssignedRegionId, updated!.RegionId);
        Assert.Equal("Regional Location 07 Updated", updated.Name);
        Assert.Equal(42.77, updated.Latitude);
        Assert.Equal(18.77, updated.Longitude);
    }

    private static async Task<RegionalLocationListResponseDto> GetLocationsAsync(
        HttpClient client,
        int regionId,
        string? search = null,
        string sort = "newest",
        int skip = 0,
        int? take = null)
    {
        var url = $"/api/admin/regional/locations?regionId={regionId}&sort={sort}&skip={skip}";
        if (take.HasValue)
        {
            url += $"&take={take.Value}";
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            url += "&search=" + Uri.EscapeDataString(search);
        }

        var page = await client.GetFromJsonAsync<RegionalLocationListResponseDto>(url);
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

    private static RegionalLocationCreateRequestDto BuildCreateRequest(
        int regionId,
        int destinationId,
        int locationTypeId,
        int[]? tagIds = null,
        string name = "New Regional Location")
    {
        return new RegionalLocationCreateRequestDto
        {
            RegionId = regionId,
            DestinationId = destinationId,
            LocationTypeId = locationTypeId,
            Name = name,
            Summary = "Short location summary.",
            Description = "Full location description.",
            Status = "Published",
            BoundaryNote = "North edge boundary note.",
            CardImageUrl = "/media/uploads/regional/default-location-card.png",
            CoverImageUrl = "/media/uploads/regional/default-location-cover.png",
            Boundary = new RegionalLocationBoundaryDto
            {
                ShapeType = "Polygon",
                Coordinates =
                [
                    new() { Latitude = 43.0, Longitude = 19.0 },
                    new() { Latitude = 43.0, Longitude = 19.2 },
                    new() { Latitude = 43.2, Longitude = 19.2 },
                    new() { Latitude = 43.2, Longitude = 19.0 }
                ]
            },
            TagIds = tagIds?.ToList() ?? []
        };
    }

    private static async Task<LocationsFixture> CreateLocationsFixtureAsync(GeotrackApiFactory app)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var now = DateTime.UtcNow;
        var territory = await db.Territories.OrderBy(item => item.Id).FirstAsync();
        var destinationType = await db.DestinationTypes.OrderBy(item => item.Id).FirstAsync();
        var locationType = await db.LocationTypes.OrderBy(item => item.Id).FirstAsync();
        var placeType = await db.PlaceTypes.OrderBy(item => item.Id).FirstAsync();
        var manager = await db.Users.SingleAsync(item => item.Email == "elena@example.com");

        var assignedRegion = new Region
        {
            Name = $"Locations Assigned {Guid.NewGuid():N}",
            TerritoryId = territory.Id,
            CreatedAt = now
        };
        var unassignedRegion = new Region
        {
            Name = $"Locations Unassigned {Guid.NewGuid():N}",
            TerritoryId = territory.Id,
            CreatedAt = now
        };
        db.Regions.AddRange(assignedRegion, unassignedRegion);
        await db.SaveChangesAsync();

        await GrantRegionalManagerCoverageAsync(db, manager, assignedRegion.Id);

        var assignedDestinations = Enumerable.Range(0, 30)
            .Select(index => new Destination
            {
                Name = $"Location Parent Destination {index:D2}",
                DestinationTypeId = destinationType.Id,
                RegionId = assignedRegion.Id,
                Description = index == 7
                    ? "Searchable destination summary with sunlit promenades."
                    : $"Location parent destination description {index:D2}.",
                Latitude = 42 + index / 100d,
                Longitude = 18 + index / 100d,
                CreatedAt = now.AddMinutes(-index)
            })
            .ToList();

        var unassignedDestination = new Destination
        {
            Name = "Unassigned Location Parent Destination",
            DestinationTypeId = destinationType.Id,
            RegionId = unassignedRegion.Id,
            Description = "This destination must never appear in assigned-region location results.",
            Latitude = 45,
            Longitude = 19,
            CreatedAt = now
        };

        db.Destinations.AddRange(assignedDestinations);
        db.Destinations.Add(unassignedDestination);
        await db.SaveChangesAsync();

        db.Entry(assignedDestinations[0])
            .Property<Geometry?>("BoundaryGeometry")
            .CurrentValue = Polygon(
                (42.9, 18.9),
                (43.3, 18.9),
                (43.3, 19.3),
                (42.9, 19.3));
        await db.SaveChangesAsync();

        var locations = Enumerable.Range(0, 30)
            .Select(index => new Location
            {
                Name = $"Regional Location {index:D2}",
                DestinationId = assignedDestinations[index].Id,
                LocationTypeId = locationType.Id,
                Latitude = 42 + index / 100d,
                Longitude = 18 + index / 100d,
                CreatedAt = now.AddMinutes(-index)
            })
            .ToList();

        var unassignedLocation = new Location
        {
            Name = "Unassigned Regional Location",
            DestinationId = unassignedDestination.Id,
            LocationTypeId = locationType.Id,
            Latitude = 45,
            Longitude = 19,
            CreatedAt = now
        };

        db.Locations.AddRange(locations);
        db.Locations.Add(unassignedLocation);
        await db.SaveChangesAsync();

        db.Places.Add(new Place
        {
            Name = "Location Related Place",
            PlaceTypeId = placeType.Id,
            LocationId = locations[29].Id,
            Description = "Related content that protects the location from deletion.",
            Latitude = 42.9,
            Longitude = 18.9,
            CreatedAt = now
        });
        await db.SaveChangesAsync();

        return new LocationsFixture(
            assignedRegion.Id,
            unassignedRegion.Id,
            manager.Id,
            assignedDestinations[0].Id,
            unassignedDestination.Id,
            locations[7].Id,
            locations[28].Id,
            locations[29].Id,
            unassignedLocation.Id);
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

    private static async Task<RegionalImageUploadResultDto> UploadOneImageAsync(HttpClient client, int regionId)
    {
        using var form = new MultipartFormDataContent();
        using var content = new ByteArrayContent(TestPngBytes());
        content.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        form.Add(content, "files", "cover.png");

        var response = await client.PostAsync($"/api/admin/regional/media/images?regionId={regionId}", form);
        response.EnsureSuccessStatusCode();

        var uploaded = await response.Content.ReadFromJsonAsync<RegionalImageUploadResponseDto>();
        Assert.NotNull(uploaded);
        var image = Assert.Single(uploaded!.Images);
        Assert.StartsWith("/media/uploads/regional/", image.Url);
        return image;
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

    private static byte[] TestPngBytes() =>
        Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+/p9sAAAAASUVORK5CYII=");

    private sealed record LocationsFixture(
        int AssignedRegionId,
        int UnassignedRegionId,
        int ManagerUserId,
        int AssignedDestinationId,
        int UnassignedDestinationId,
        int SearchLocationId,
        int BareLocationId,
        int LocationWithContentId,
        int UnassignedLocationId);
}
