using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Geotrack.Application.DTOs.Admin;
using Geotrack.Application.DTOs.Auth;
using Geotrack.Application.Interfaces.Service;
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

public sealed class RegionalScopeValidationTests
{
    private const string BoundaryGeometryPropertyName = "BoundaryGeometry";
    private const string BoundaryDisplayGeometryPropertyName = "BoundaryDisplayGeometry";
    private static readonly GeometryFactory GeometryFactory = new(new PrecisionModel(), 4326);

    [Fact]
    public async Task Validate_entity_location_applies_country_boundary_tolerance_other_region_blocking_and_full_boundary_geometry()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        var fixture = await CreateScopeFixtureAsync(app);
        using var manager = await LoginAsync(app, "elena@example.com", "password123");

        var inside = await ValidateAsync(manager, fixture.AssignedRegionId, 42.05, 18.05);
        Assert.True(inside.IsAllowed);
        Assert.Equal(RegionalScopeValidationModes.InsideAssignedRegion, inside.ValidationMode);
        Assert.Equal(fixture.AssignedTerritoryId, inside.AssignedCountryOrTerritoryId);
        Assert.Equal(fixture.AssignedTerritoryName, inside.AssignedCountryOrTerritoryName);

        var onBoundary = await ValidateAsync(manager, fixture.AssignedRegionId, 42.0, 18.05);
        Assert.True(onBoundary.IsAllowed);
        Assert.Equal(RegionalScopeValidationModes.InsideAssignedRegion, onBoundary.ValidationMode);

        var tolerated = await ValidateAsync(manager, fixture.AssignedRegionId, 42.105, 18.05);
        Assert.True(tolerated.IsAllowed);
        Assert.Equal(RegionalScopeValidationModes.AllowedWithinToleranceInsideAssignedCountry, tolerated.ValidationMode);
        Assert.InRange(tolerated.DistanceFromAssignedRegionMeters ?? -1, 0, 5000);
        Assert.Contains("assigned country/territory", tolerated.Message, StringComparison.OrdinalIgnoreCase);

        var neumLikeOutsideCountry = await ValidateAsync(manager, fixture.AssignedRegionId, 42.05, 17.99);
        Assert.False(neumLikeOutsideCountry.IsAllowed);
        Assert.Equal(RegionalScopeValidationModes.RejectedOutsideAssignedCountry, neumLikeOutsideCountry.ValidationMode);
        Assert.Equal("This location is outside your assigned country/territory.", neumLikeOutsideCountry.Message);

        var insideOtherRegion = await ValidateAsync(manager, fixture.AssignedRegionId, 42.125, 18.05);
        Assert.False(insideOtherRegion.IsAllowed);
        Assert.Equal(RegionalScopeValidationModes.RejectedInsideAnotherRegion, insideOtherRegion.ValidationMode);
        Assert.Equal(fixture.OtherRegionId, insideOtherRegion.MatchedOtherRegionId);

        var insideDisplayButOutsideFullBoundary = await ValidateAsync(manager, fixture.AssignedRegionId, 42.15, 18.05);
        Assert.False(insideDisplayButOutsideFullBoundary.IsAllowed);
        Assert.Equal(
            RegionalScopeValidationModes.RejectedTooFarOutsideRegion,
            insideDisplayButOutsideFullBoundary.ValidationMode);
        Assert.True(insideDisplayButOutsideFullBoundary.DistanceFromAssignedRegionMeters > 5000);

        var missingBoundary = await ValidateAsync(manager, fixture.MissingBoundaryRegionId, 42.05, 18.05);
        Assert.False(missingBoundary.IsAllowed);
        Assert.Equal(RegionalScopeValidationModes.RejectedMissingRegionBoundary, missingBoundary.ValidationMode);

        var missingCountryBoundary = await ValidateAsync(manager, fixture.MissingCountryBoundaryRegionId, 42.05, 18.05);
        Assert.False(missingCountryBoundary.IsAllowed);
        Assert.Equal(
            RegionalScopeValidationModes.RejectedMissingAssignedCountryBoundary,
            missingCountryBoundary.ValidationMode);
        Assert.Equal(
            "Assigned country/territory boundary is missing. Please contact an administrator.",
            missingCountryBoundary.Message);

        var missingAssignedRegion = await ValidateMissingAssignedRegionAsync(app, fixture.ManagerUserId);
        Assert.False(missingAssignedRegion.IsAllowed);
        Assert.Equal(RegionalScopeValidationModes.RejectedMissingAssignedRegion, missingAssignedRegion.ValidationMode);
    }

    [Fact]
    public async Task Destination_create_allows_nearby_unassigned_tolerance_area()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        var fixture = await CreateScopeFixtureAsync(app);
        using var manager = await LoginAsync(app, "elena@example.com", "password123");

        var response = await manager.PostAsJsonAsync(
            "/api/admin/regional/destinations",
            new RegionalDestinationCreateRequestDto
            {
                RegionId = fixture.AssignedRegionId,
                DestinationTypeId = fixture.DestinationTypeId,
                Name = "Tolerated Coastal Destination",
                Summary = "Nearby unassigned edge.",
                Description = "Allowed by regional tolerance because no other region owns the point.",
                Status = "Published",
                Latitude = 42.105,
                Longitude = 18.05,
                CardImageUrl = "/media/uploads/regional/tolerated-card.png",
                CoverImageUrl = "/media/uploads/regional/tolerated-cover.png"
            });

        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.Created, body);
    }

    [Fact]
    public async Task Destination_update_cannot_move_entity_into_another_region()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        var fixture = await CreateScopeFixtureAsync(app);
        using var manager = await LoginAsync(app, "elena@example.com", "password123");

        var response = await manager.PutAsJsonAsync(
            $"/api/admin/regional/destinations/{fixture.DestinationId}?regionId={fixture.AssignedRegionId}",
            new RegionalDestinationUpdateRequestDto
            {
                Latitude = 42.125,
                Longitude = 18.05
            });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<Dictionary<string, string>>();
        Assert.Contains("another region", body?["message"], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Entity_create_rejects_outside_assigned_country_for_all_regional_content_types()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        var fixture = await CreateScopeFixtureAsync(app);
        using var manager = await LoginAsync(app, "elena@example.com", "password123");

        var destinationResponse = await manager.PostAsJsonAsync(
            "/api/admin/regional/destinations",
            new RegionalDestinationCreateRequestDto
            {
                RegionId = fixture.AssignedRegionId,
                DestinationTypeId = fixture.DestinationTypeId,
                Name = "Outside Country Destination",
                Summary = "Outside country edge.",
                Description = "Should be rejected before tolerance.",
                Status = "Published",
                Latitude = 42.05,
                Longitude = 17.99,
                CardImageUrl = "/media/uploads/regional/outside-country-destination-card.png",
                CoverImageUrl = "/media/uploads/regional/outside-country-destination-cover.png"
            });
        await AssertOutsideCountryBadRequestAsync(destinationResponse);

        var locationResponse = await manager.PostAsJsonAsync(
            "/api/admin/regional/locations",
            new RegionalLocationCreateRequestDto
            {
                RegionId = fixture.AssignedRegionId,
                DestinationId = fixture.DestinationId,
                LocationTypeId = fixture.LocationTypeId,
                Name = "Outside Country Location",
                Summary = "Outside country edge.",
                Description = "Should be rejected before tolerance.",
                Status = "Published",
                Latitude = 42.05,
                Longitude = 17.99,
                CardImageUrl = "/media/uploads/regional/outside-country-location-card.png",
                CoverImageUrl = "/media/uploads/regional/outside-country-location-cover.png"
            });
        await AssertOutsideCountryBadRequestAsync(locationResponse);

        var placeResponse = await manager.PostAsJsonAsync(
            "/api/admin/regional/places",
            new RegionalPlaceCreateRequestDto
            {
                RegionId = fixture.AssignedRegionId,
                LocationId = fixture.LocationId,
                PlaceTypeId = fixture.PlaceTypeId,
                Name = "Outside Country Place",
                Summary = "Outside country place.",
                Description = "Should be rejected before tolerance.",
                Status = "Published",
                Latitude = 42.05,
                Longitude = 17.99,
                CardImageUrl = "/media/uploads/regional/outside-country-place-card.png",
                CoverImageUrl = "/media/uploads/regional/outside-country-place-cover.png"
            });
        await AssertOutsideCountryBadRequestAsync(placeResponse);

        var startsAt = DateTime.UtcNow.AddDays(7);
        var eventResponse = await manager.PostAsJsonAsync(
            "/api/admin/regional/events",
            new RegionalEventCreateRequestDto
            {
                RegionId = fixture.AssignedRegionId,
                ParentEntityType = "Destination",
                DestinationId = fixture.OutsideCountryDestinationId,
                EventTypeId = fixture.EventTypeId,
                Name = "Outside Country Event",
                Summary = "Outside country event.",
                Description = "Should be rejected before tolerance.",
                Status = "Published",
                StartsAt = startsAt,
                EndsAt = startsAt.AddHours(2),
                IsFree = true,
                CardImageUrl = "/media/uploads/regional/outside-country-event-card.png",
                CoverImageUrl = "/media/uploads/regional/outside-country-event-cover.png"
            });
        await AssertOutsideCountryBadRequestAsync(eventResponse);
    }

    [Fact]
    public async Task Entity_update_rejects_outside_assigned_country_for_all_regional_content_types()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        var fixture = await CreateScopeFixtureAsync(app);
        using var manager = await LoginAsync(app, "elena@example.com", "password123");

        var destinationResponse = await manager.PutAsJsonAsync(
            $"/api/admin/regional/destinations/{fixture.DestinationId}?regionId={fixture.AssignedRegionId}",
            new RegionalDestinationUpdateRequestDto
            {
                Latitude = 42.05,
                Longitude = 17.99
            });
        await AssertOutsideCountryBadRequestAsync(destinationResponse);

        var locationResponse = await manager.PutAsJsonAsync(
            $"/api/admin/regional/locations/{fixture.LocationId}?regionId={fixture.AssignedRegionId}",
            new RegionalLocationUpdateRequestDto
            {
                Latitude = 42.05,
                Longitude = 17.99
            });
        await AssertOutsideCountryBadRequestAsync(locationResponse);

        var placeResponse = await manager.PutAsJsonAsync(
            $"/api/admin/regional/places/{fixture.PlaceId}?regionId={fixture.AssignedRegionId}",
            new RegionalPlaceUpdateRequestDto
            {
                Latitude = 42.05,
                Longitude = 17.99
            });
        await AssertOutsideCountryBadRequestAsync(placeResponse);

        var eventResponse = await manager.PutAsJsonAsync(
            $"/api/admin/regional/events/{fixture.EventId}?regionId={fixture.AssignedRegionId}",
            new RegionalEventUpdateRequestDto
            {
                ParentEntityType = "Destination",
                DestinationId = fixture.OutsideCountryDestinationId
            });
        await AssertOutsideCountryBadRequestAsync(eventResponse);
    }

    [Fact]
    public async Task Location_place_and_event_create_use_shared_tolerance_validation()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        var fixture = await CreateScopeFixtureAsync(app);
        using var manager = await LoginAsync(app, "elena@example.com", "password123");

        var locationResponse = await manager.PostAsJsonAsync(
            "/api/admin/regional/locations",
            new RegionalLocationCreateRequestDto
            {
                RegionId = fixture.AssignedRegionId,
                DestinationId = fixture.DestinationId,
                LocationTypeId = fixture.LocationTypeId,
                Name = "Tolerated Coastal Location",
                Summary = "Nearby unassigned edge.",
                Description = "Allowed by regional tolerance.",
                Status = "Published",
                Latitude = 42.105,
                Longitude = 18.05,
                CardImageUrl = "/media/uploads/regional/tolerated-location-card.png",
                CoverImageUrl = "/media/uploads/regional/tolerated-location-cover.png"
            });
        var locationBody = await locationResponse.Content.ReadAsStringAsync();
        Assert.True(locationResponse.StatusCode == HttpStatusCode.Created, locationBody);

        var placeResponse = await manager.PostAsJsonAsync(
            "/api/admin/regional/places",
            new RegionalPlaceCreateRequestDto
            {
                RegionId = fixture.AssignedRegionId,
                LocationId = fixture.LocationId,
                PlaceTypeId = fixture.PlaceTypeId,
                Name = "Tolerated Coastal Place",
                Summary = "Nearby place.",
                Description = "Allowed by regional tolerance.",
                Status = "Published",
                Latitude = 42.105,
                Longitude = 18.05,
                CardImageUrl = "/media/uploads/regional/tolerated-place-card.png",
                CoverImageUrl = "/media/uploads/regional/tolerated-place-cover.png"
            });
        var placeBody = await placeResponse.Content.ReadAsStringAsync();
        Assert.True(placeResponse.StatusCode == HttpStatusCode.Created, placeBody);

        var startsAt = DateTime.UtcNow.AddDays(7);
        var eventResponse = await manager.PostAsJsonAsync(
            "/api/admin/regional/events",
            new RegionalEventCreateRequestDto
            {
                RegionId = fixture.AssignedRegionId,
                ParentEntityType = "Destination",
                DestinationId = fixture.ToleratedDestinationId,
                EventTypeId = fixture.EventTypeId,
                Name = "Tolerated Coastal Event",
                Summary = "Nearby event.",
                Description = "Allowed by regional tolerance.",
                Status = "Published",
                StartsAt = startsAt,
                EndsAt = startsAt.AddHours(2),
                IsFree = true,
                CardImageUrl = "/media/uploads/regional/tolerated-event-card.png",
                CoverImageUrl = "/media/uploads/regional/tolerated-event-cover.png"
            });
        var eventBody = await eventResponse.Content.ReadAsStringAsync();
        Assert.True(eventResponse.StatusCode == HttpStatusCode.Created, eventBody);
    }

    private static async Task<RegionalScopeValidationResultDto> ValidateAsync(
        HttpClient client,
        int regionId,
        double latitude,
        double longitude)
    {
        var response = await client.PostAsJsonAsync(
            "/api/admin/regional/validate-entity-location",
            new RegionalScopeValidationRequestDto
            {
                RegionId = regionId,
                EntityType = "Location",
                OperationType = "Create",
                Latitude = latitude,
                Longitude = longitude
            });

        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<RegionalScopeValidationResultDto>();
        Assert.NotNull(result);
        return result!;
    }

    private static async Task AssertOutsideCountryBadRequestAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<Dictionary<string, string>>();
        Assert.Equal("This location is outside your assigned country/territory.", body?["message"]);
    }

    private static async Task<RegionalScopeValidationResultDto> ValidateMissingAssignedRegionAsync(
        GeotrackApiFactory app,
        int managerUserId)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var validator = scope.ServiceProvider.GetRequiredService<IRegionalScopeValidationService>();
        return await validator.ValidatePointAsync(
            managerUserId,
            0,
            "Location",
            42.05,
            18.05,
            "Create");
    }

    private static async Task<ScopeFixture> CreateScopeFixtureAsync(GeotrackApiFactory app)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var now = DateTime.UtcNow;
        var territory = await db.Territories.OrderBy(item => item.Id).FirstAsync();
        territory.BoundaryShapeType = "Polygon";
        territory.BoundarySource = "IntegrationTest";
        SetBoundary(db, territory, Polygon(
            (41.5, 18.0),
            (41.5, 18.8),
            (42.16, 18.8),
            (42.16, 18.0)));

        var missingCountryBoundaryTerritory = new Territory
        {
            Name = $"Scope Missing Boundary Territory {Guid.NewGuid():N}",
            TerritoryKind = TerritoryKinds.Country,
            CreatedAt = now
        };
        db.Territories.Add(missingCountryBoundaryTerritory);
        await db.SaveChangesAsync();

        var destinationType = await db.DestinationTypes.OrderBy(item => item.Id).FirstAsync();
        var locationType = await db.LocationTypes.OrderBy(item => item.Id).FirstAsync();
        var placeType = await db.PlaceTypes.OrderBy(item => item.Id).FirstAsync();
        var eventType = await db.EventTypes.OrderBy(item => item.Id).FirstAsync();
        var manager = await db.Users.SingleAsync(item => item.Email == "elena@example.com");

        var assignedRegion = new Region
        {
            Name = $"Scope Assigned {Guid.NewGuid():N}",
            TerritoryId = territory.Id,
            CreatedAt = now
        };
        var otherRegion = new Region
        {
            Name = $"Scope Other {Guid.NewGuid():N}",
            TerritoryId = territory.Id,
            CreatedAt = now
        };
        var missingBoundaryRegion = new Region
        {
            Name = $"Scope Missing Boundary {Guid.NewGuid():N}",
            TerritoryId = territory.Id,
            CreatedAt = now
        };
        var missingCountryBoundaryRegion = new Region
        {
            Name = $"Scope Missing Country Boundary {Guid.NewGuid():N}",
            TerritoryId = missingCountryBoundaryTerritory.Id,
            CreatedAt = now
        };

        db.Regions.AddRange(assignedRegion, otherRegion, missingBoundaryRegion, missingCountryBoundaryRegion);
        await db.SaveChangesAsync();

        SetBoundary(db, assignedRegion, Polygon(
            (42.0, 18.0),
            (42.0, 18.1),
            (42.1, 18.1),
            (42.1, 18.0)));
        SetDisplayBoundary(db, assignedRegion, Polygon(
            (41.8, 17.8),
            (41.8, 18.4),
            (42.4, 18.4),
            (42.4, 17.8)));
        SetBoundary(db, otherRegion, Polygon(
            (42.12, 18.04),
            (42.12, 18.06),
            (42.14, 18.06),
            (42.14, 18.04)));
        SetBoundary(db, missingCountryBoundaryRegion, Polygon(
            (42.0, 18.0),
            (42.0, 18.1),
            (42.1, 18.1),
            (42.1, 18.0)));
        await db.SaveChangesAsync();

        await GrantRegionalManagerCoverageAsync(db, manager, assignedRegion.Id);
        await GrantRegionalManagerCoverageAsync(db, manager, missingBoundaryRegion.Id);
        await GrantRegionalManagerCoverageAsync(db, manager, missingCountryBoundaryRegion.Id);

        var destination = new Destination
        {
            Name = "Scoped Destination",
            DestinationTypeId = destinationType.Id,
            RegionId = assignedRegion.Id,
            Description = "Destination for scope update validation.",
            Latitude = 42.05,
            Longitude = 18.05,
            CreatedAt = now
        };
        var toleratedDestination = new Destination
        {
            Name = "Scoped Tolerated Destination",
            DestinationTypeId = destinationType.Id,
            RegionId = assignedRegion.Id,
            Description = "Destination centered just outside the assigned region.",
            Latitude = 42.105,
            Longitude = 18.05,
            CreatedAt = now
        };
        var outsideCountryDestination = new Destination
        {
            Name = "Scoped Legacy Outside Country Destination",
            DestinationTypeId = destinationType.Id,
            RegionId = assignedRegion.Id,
            Description = "Legacy destination centered outside the assigned country.",
            Latitude = 42.05,
            Longitude = 17.99,
            CreatedAt = now
        };
        db.Destinations.AddRange(destination, toleratedDestination, outsideCountryDestination);
        await db.SaveChangesAsync();

        var location = new Location
        {
            Name = "Scoped Location",
            LocationTypeId = locationType.Id,
            DestinationId = destination.Id,
            Description = "Location for scope validation.",
            Latitude = 42.05,
            Longitude = 18.05,
            CreatedAt = now
        };
        db.Locations.Add(location);
        await db.SaveChangesAsync();

        var place = new Place
        {
            Name = "Scoped Place",
            PlaceTypeId = placeType.Id,
            LocationId = location.Id,
            Subtitle = "Place for scope update validation.",
            Description = "Place for scope update validation.",
            Latitude = 42.05,
            Longitude = 18.05,
            CreatedAt = now
        };
        db.Places.Add(place);
        await db.SaveChangesAsync();

        var @event = new Event
        {
            Name = "Scoped Event",
            EventTypeId = eventType.Id,
            DestinationId = destination.Id,
            ParentEntityType = "Destination",
            Summary = "Event for scope update validation.",
            Description = "Event for scope update validation.",
            StartDate = now.AddDays(14),
            EndDate = now.AddDays(14).AddHours(2),
            IsFree = true,
            CreatedAt = now
        };
        db.Events.Add(@event);
        await db.SaveChangesAsync();

        return new ScopeFixture(
            manager.Id,
            territory.Id,
            territory.Name,
            assignedRegion.Id,
            otherRegion.Id,
            missingBoundaryRegion.Id,
            missingCountryBoundaryRegion.Id,
            destination.Id,
            toleratedDestination.Id,
            outsideCountryDestination.Id,
            location.Id,
            place.Id,
            @event.Id,
            destinationType.Id,
            locationType.Id,
            placeType.Id,
            eventType.Id);
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

    private static void SetDisplayBoundary(AppDbContext db, Region region, Geometry geometry)
    {
        db.Entry(region)
            .Property<Geometry?>(BoundaryDisplayGeometryPropertyName)
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

    private sealed record ScopeFixture(
        int ManagerUserId,
        int AssignedTerritoryId,
        string AssignedTerritoryName,
        int AssignedRegionId,
        int OtherRegionId,
        int MissingBoundaryRegionId,
        int MissingCountryBoundaryRegionId,
        int DestinationId,
        int ToleratedDestinationId,
        int OutsideCountryDestinationId,
        int LocationId,
        int PlaceId,
        int EventId,
        int DestinationTypeId,
        int LocationTypeId,
        int PlaceTypeId,
        int EventTypeId);
}
