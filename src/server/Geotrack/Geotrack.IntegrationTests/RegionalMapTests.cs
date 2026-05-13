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
using NetTopologySuite.Geometries;
using Xunit;

namespace Geotrack.IntegrationTests;

using DomainLocation = Geotrack.Domain.Entities.Core.Location;

public sealed class RegionalMapTests
{
    private static readonly GeometryFactory GeometryFactory = new(new PrecisionModel(), 4326);

    [Fact]
    public async Task Regional_manager_loads_scoped_map_markers_zones_images_and_viewport()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        var fixture = await CreateMapFixtureAsync(app);

        using var client = await LoginAsync(app, "elena@example.com", "password123");
        var map = await client.GetFromJsonAsync<RegionalMapResponseDto>(
            $"/api/admin/regional/map/entities?regionId={fixture.AssignedRegionId}");

        Assert.NotNull(map);
        Assert.False(map!.Viewport.IsEmpty);
        Assert.NotNull(map.Viewport.Bounds);
        Assert.Equal(3, map.Markers.Count);
        Assert.All(map.Markers, marker => Assert.Equal(fixture.AssignedRegionId, marker.RegionId));

        Assert.Contains(map.Markers, marker =>
            marker.EntityType == "Destination" &&
            marker.Id == fixture.AssignedDestinationId);
        Assert.Contains(map.Markers, marker =>
            marker.EntityType == "Location" &&
            marker.Id == fixture.AssignedLocationId &&
            marker.DestinationId == fixture.AssignedDestinationId);

        var placeMarker = Assert.Single(map.Markers, marker => marker.EntityType == "Place");
        Assert.Equal(fixture.AssignedPlaceId, placeMarker.Id);
        Assert.Equal(fixture.AssignedLocationId, placeMarker.LocationId);
        Assert.Equal("/media/test/map-main.svg", placeMarker.ImageUrl);
        Assert.DoesNotContain(map.Markers, marker => marker.Id == fixture.UnassignedPlaceId);

        Assert.Contains(map.Zones, zone =>
            zone.EntityType == "Region" &&
            zone.Id == fixture.AssignedRegionId &&
            zone.GeometryGeoJson?.Type is "Polygon" or "MultiPolygon");
        Assert.Contains(map.Zones, zone =>
            zone.EntityType == "Destination" &&
            zone.Id == fixture.AssignedDestinationId &&
            zone.GeometryGeoJson?.Type == "Polygon");
        Assert.Contains(map.Zones, zone =>
            zone.EntityType == "Location" &&
            zone.Id == fixture.AssignedLocationId &&
            zone.GeometryGeoJson?.Type == "Polygon");
        Assert.DoesNotContain(map.Zones, zone => zone.Id == fixture.UnassignedLocationId);
    }

    [Fact]
    public async Task Regional_map_rejects_anonymous_non_regional_and_unassigned_region_requests()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        var fixture = await CreateMapFixtureAsync(app);

        using var anonymous = app.CreateClient();
        var anonymousResponse = await anonymous.GetAsync(
            $"/api/admin/regional/map/entities?regionId={fixture.AssignedRegionId}");
        Assert.Equal(HttpStatusCode.Unauthorized, anonymousResponse.StatusCode);

        using var regularUser = await LoginAsync(app, "marko.p@example.com", "password123");
        var regularUserResponse = await regularUser.GetAsync(
            $"/api/admin/regional/map/entities?regionId={fixture.AssignedRegionId}");
        Assert.Equal(HttpStatusCode.Forbidden, regularUserResponse.StatusCode);

        using var manager = await LoginAsync(app, "elena@example.com", "password123");
        var unassignedResponse = await manager.GetAsync(
            $"/api/admin/regional/map/entities?regionId={fixture.UnassignedRegionId}");
        Assert.Equal(HttpStatusCode.Forbidden, unassignedResponse.StatusCode);
    }

    [Fact]
    public async Task Regional_map_search_is_scoped_to_assigned_region()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        var fixture = await CreateMapFixtureAsync(app);

        using var manager = await LoginAsync(app, "elena@example.com", "password123");
        var assignedSearch = await manager.GetFromJsonAsync<RegionalMapSearchResponseDto>(
            $"/api/admin/regional/map/search?regionId={fixture.AssignedRegionId}&query=Assigned%20Map");

        Assert.NotNull(assignedSearch);
        Assert.Contains(assignedSearch!.Items, item => item.Id == fixture.AssignedPlaceId && item.EntityType == "Place");
        Assert.DoesNotContain(assignedSearch.Items, item => item.Id == fixture.UnassignedPlaceId);

        var outOfRegionSearch = await manager.GetFromJsonAsync<RegionalMapSearchResponseDto>(
            $"/api/admin/regional/map/search?regionId={fixture.AssignedRegionId}&query=Unassigned%20Map");
        Assert.NotNull(outOfRegionSearch);
        Assert.Empty(outOfRegionSearch!.Items);

        var tamperedResponse = await manager.GetAsync(
            $"/api/admin/regional/map/search?regionId={fixture.UnassignedRegionId}&query=Unassigned");
        Assert.Equal(HttpStatusCode.Forbidden, tamperedResponse.StatusCode);
    }

    [Fact]
    public async Task Regional_map_handles_missing_geometry_without_fake_zones()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        var regionId = await CreatePointOnlyMapFixtureAsync(app);

        using var manager = await LoginAsync(app, "elena@example.com", "password123");
        var map = await manager.GetFromJsonAsync<RegionalMapResponseDto>(
            $"/api/admin/regional/map/entities?regionId={regionId}");

        Assert.NotNull(map);
        Assert.False(map!.Viewport.IsEmpty);
        Assert.Single(map.Markers);
        Assert.Empty(map.Zones);
    }

    [Fact]
    public async Task Create_place_rejects_coordinates_outside_parent_location_boundary()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        var fixture = await CreateMapFixtureAsync(app);

        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var placeTypeId = await db.PlaceTypes.OrderBy(type => type.Id).Select(type => type.Id).FirstAsync();

        using var manager = await LoginAsync(app, "elena@example.com", "password123");
        var response = await manager.PostAsJsonAsync(
            "/api/admin/regional/places",
            new RegionalPlaceCreateRequestDto
            {
                RegionId = fixture.AssignedRegionId,
                LocationId = fixture.AssignedLocationId,
                PlaceTypeId = placeTypeId,
                Name = "Outside Boundary Place",
                Summary = "Outside parent location.",
                Status = "Published",
                Latitude = 12.5,
                Longitude = 22.5
            });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private static async Task<MapFixture> CreateMapFixtureAsync(GeotrackApiFactory app)
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
            Name = $"Map Assigned {Guid.NewGuid():N}",
            TerritoryId = territory.Id,
            CreatedAt = now
        };
        var unassignedRegion = new Region
        {
            Name = $"Map Unassigned {Guid.NewGuid():N}",
            TerritoryId = territory.Id,
            CreatedAt = now
        };
        db.Regions.AddRange(assignedRegion, unassignedRegion);
        await db.SaveChangesAsync();

        await GrantRegionalManagerCoverageAsync(db, manager, assignedRegion.Id);

        var assignedDestination = new Destination
        {
            Name = "Assigned Map Destination",
            DestinationTypeId = destinationType.Id,
            RegionId = assignedRegion.Id,
            Description = "Assigned map destination.",
            Latitude = 10.5,
            Longitude = 20.5,
            CreatedAt = now
        };
        var unassignedDestination = new Destination
        {
            Name = "Unassigned Map Destination",
            DestinationTypeId = destinationType.Id,
            RegionId = unassignedRegion.Id,
            Description = "Unassigned map destination.",
            Latitude = 30.5,
            Longitude = 40.5,
            CreatedAt = now
        };
        db.Destinations.AddRange(assignedDestination, unassignedDestination);
        db.Entry(assignedDestination).Property<Geometry?>("BoundaryGeometry").CurrentValue = Polygon(
            (10.0, 20.0),
            (11.0, 20.0),
            (11.0, 21.0),
            (10.0, 21.0));
        db.Entry(unassignedDestination).Property<Geometry?>("BoundaryGeometry").CurrentValue = Polygon(
            (30.0, 40.0),
            (31.0, 40.0),
            (31.0, 41.0),
            (30.0, 41.0));
        await db.SaveChangesAsync();

        var assignedLocation = new DomainLocation
        {
            Name = "Assigned Map Location",
            DestinationId = assignedDestination.Id,
            LocationTypeId = locationType.Id,
            Latitude = 10.5,
            Longitude = 20.5,
            CreatedAt = now
        };
        var unassignedLocation = new DomainLocation
        {
            Name = "Unassigned Map Location",
            DestinationId = unassignedDestination.Id,
            LocationTypeId = locationType.Id,
            Latitude = 30.5,
            Longitude = 40.5,
            CreatedAt = now
        };
        db.Locations.AddRange(assignedLocation, unassignedLocation);
        db.Entry(assignedLocation).Property<Geometry?>("BoundaryGeometry").CurrentValue = Polygon(
            (10.2, 20.2),
            (10.8, 20.2),
            (10.8, 20.8),
            (10.2, 20.8));
        db.Entry(unassignedLocation).Property<Geometry?>("BoundaryGeometry").CurrentValue = Polygon(
            (30.2, 40.2),
            (30.8, 40.2),
            (30.8, 40.8),
            (30.2, 40.8));
        await db.SaveChangesAsync();

        var assignedPlace = new Place
        {
            Name = "Assigned Map Place",
            PlaceTypeId = placeType.Id,
            LocationId = assignedLocation.Id,
            Description = "Assigned map place.",
            Latitude = 10.4,
            Longitude = 20.4,
            CreatedAt = now
        };
        var unassignedPlace = new Place
        {
            Name = "Unassigned Map Place",
            PlaceTypeId = placeType.Id,
            LocationId = unassignedLocation.Id,
            Description = "Unassigned map place.",
            Latitude = 30.4,
            Longitude = 40.4,
            CreatedAt = now
        };
        db.Places.AddRange(assignedPlace, unassignedPlace);
        await db.SaveChangesAsync();

        db.EntityImages.AddRange(
            new EntityImage
            {
                TargetType = EntityImageTargetType.Place,
                PlaceId = assignedPlace.Id,
                Url = "/media/test/map-secondary.svg",
                IsMain = false,
                SortOrder = 0,
                CreatedAt = now
            },
            new EntityImage
            {
                TargetType = EntityImageTargetType.Place,
                PlaceId = assignedPlace.Id,
                Url = "/media/test/map-main.svg",
                IsMain = true,
                SortOrder = 10,
                CreatedAt = now.AddSeconds(1)
            });
        await db.SaveChangesAsync();

        return new MapFixture(
            assignedRegion.Id,
            unassignedRegion.Id,
            assignedDestination.Id,
            assignedLocation.Id,
            assignedPlace.Id,
            unassignedLocation.Id,
            unassignedPlace.Id);
    }

    private static async Task<int> CreatePointOnlyMapFixtureAsync(GeotrackApiFactory app)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var now = DateTime.UtcNow;
        var territory = await db.Territories.OrderBy(item => item.Id).FirstAsync();
        var destinationType = await db.DestinationTypes.OrderBy(item => item.Id).FirstAsync();
        var manager = await db.Users.SingleAsync(item => item.Email == "elena@example.com");

        var region = new Region
        {
            Name = $"Point Map Assigned {Guid.NewGuid():N}",
            TerritoryId = territory.Id,
            CreatedAt = now
        };
        db.Regions.Add(region);
        await db.SaveChangesAsync();

        await GrantRegionalManagerCoverageAsync(db, manager, region.Id);

        db.Destinations.Add(new Destination
        {
            Name = "Point Only Destination",
            DestinationTypeId = destinationType.Id,
            RegionId = region.Id,
            Description = "Point-only destination.",
            Latitude = 15,
            Longitude = 25,
            CreatedAt = now
        });
        await db.SaveChangesAsync();

        return region.Id;
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

    private sealed record MapFixture(
        int AssignedRegionId,
        int UnassignedRegionId,
        int AssignedDestinationId,
        int AssignedLocationId,
        int AssignedPlaceId,
        int UnassignedLocationId,
        int UnassignedPlaceId);
}
