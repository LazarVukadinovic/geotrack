using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Geotrack.Application.DTOs.Admin;
using Geotrack.Application.DTOs.Auth;
using Geotrack.Application.Security;
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

public sealed class TerritoryRegionCreationTests
{
    private static readonly GeometryFactory GeometryFactory = new(new PrecisionModel(), 4326);

    [Fact]
    public async Task Territory_manager_can_create_region_inside_assigned_territory_boundary()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        var fixture = await CreateRegionCreationFixtureAsync(app);

        using var manager = await LoginAdminAsync(app, "mika@gmail.com", "Proba123!");
        var response = await manager.PostAsJsonAsync(
            "/api/admin/territory/regions",
            BuildCreateRequest(fixture.AssignedTerritoryId));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<TerritoryRegionCreateResponseDto>();
        Assert.NotNull(created);
        Assert.Equal(fixture.AssignedTerritoryId, created!.TerritoryId);
        Assert.Equal("New Territory Region", created.Name);
        Assert.Equal("Inactive", created.Status);
        Assert.Equal("ManualDraw", created.BoundarySource);
        Assert.Equal("Polygon", created.BoundaryShapeType);
        Assert.NotNull(created.BoundaryGeometryGeoJson);

        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var stored = await db.Regions.SingleAsync(region => region.Id == created.RegionId);
        Assert.Equal(fixture.ManagerUserId, stored.CreatedByUserId);
        Assert.Equal("ManualDraw", stored.BoundarySource);

        var boundary = db.Entry(stored)
            .Property<Geometry?>(BoundaryGeometryPropertyName)
            .CurrentValue;
        Assert.NotNull(boundary);
        Assert.Equal(4326, boundary!.SRID);
        Assert.False(boundary.IsEmpty);

        var list = await manager.GetFromJsonAsync<TerritoryRegionDirectoryResponseDto>(
            $"/api/admin/territory/regions?territoryId={fixture.AssignedTerritoryId}&search=New%20Territory%20Region");
        Assert.NotNull(list);
        Assert.Contains(list!.Items, item => item.RegionId == created.RegionId);
    }

    [Fact]
    public async Task Create_region_rejects_outside_partial_invalid_missing_and_duplicate_geometry()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        var fixture = await CreateRegionCreationFixtureAsync(app);
        using var manager = await LoginAdminAsync(app, "mika@gmail.com", "Proba123!");

        var outside = BuildCreateRequest(fixture.AssignedTerritoryId, "Outside Region");
        outside.Boundary!.Coordinates =
        [
            new() { Latitude = 45, Longitude = 21 },
            new() { Latitude = 45, Longitude = 21.2 },
            new() { Latitude = 45.2, Longitude = 21.2 },
            new() { Latitude = 45.2, Longitude = 21 }
        ];
        Assert.Equal(HttpStatusCode.BadRequest, (await manager.PostAsJsonAsync("/api/admin/territory/regions", outside)).StatusCode);

        var partiallyOutside = BuildCreateRequest(fixture.AssignedTerritoryId, "Partial Region");
        partiallyOutside.Boundary!.Coordinates =
        [
            new() { Latitude = 43.8, Longitude = 19.2 },
            new() { Latitude = 43.8, Longitude = 20.4 },
            new() { Latitude = 44.4, Longitude = 20.4 },
            new() { Latitude = 44.4, Longitude = 19.2 }
        ];
        Assert.Equal(HttpStatusCode.BadRequest, (await manager.PostAsJsonAsync("/api/admin/territory/regions", partiallyOutside)).StatusCode);

        var invalidGeometry = BuildCreateRequest(fixture.AssignedTerritoryId, "Invalid Geometry");
        invalidGeometry.Boundary!.Coordinates =
        [
            new() { Latitude = 95, Longitude = 18.4 },
            new() { Latitude = 42.4, Longitude = 18.6 },
            new() { Latitude = 42.6, Longitude = 18.4 }
        ];
        Assert.Equal(HttpStatusCode.BadRequest, (await manager.PostAsJsonAsync("/api/admin/territory/regions", invalidGeometry)).StatusCode);

        var missingBoundary = BuildCreateRequest(fixture.AssignedTerritoryId, "Missing Boundary");
        missingBoundary.Boundary = null;
        Assert.Equal(HttpStatusCode.BadRequest, (await manager.PostAsJsonAsync("/api/admin/territory/regions", missingBoundary)).StatusCode);

        var missingName = BuildCreateRequest(fixture.AssignedTerritoryId, "");
        Assert.Equal(HttpStatusCode.BadRequest, (await manager.PostAsJsonAsync("/api/admin/territory/regions", missingName)).StatusCode);

        var first = await manager.PostAsJsonAsync(
            "/api/admin/territory/regions",
            BuildCreateRequest(fixture.AssignedTerritoryId, "Duplicate Region"));
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        var duplicate = await manager.PostAsJsonAsync(
            "/api/admin/territory/regions",
            BuildCreateRequest(fixture.AssignedTerritoryId, "duplicate region"));
        Assert.Equal(HttpStatusCode.BadRequest, duplicate.StatusCode);
    }

    [Fact]
    public async Task Create_region_allows_boundary_touching_territory_boundary()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        var fixture = await CreateRegionCreationFixtureAsync(app);

        using var manager = await LoginAdminAsync(app, "mika@gmail.com", "Proba123!");
        var request = BuildCreateRequest(fixture.AssignedTerritoryId, "Touching Boundary");
        request.Boundary!.Coordinates =
        [
            new() { Latitude = 42.0, Longitude = 18.0 },
            new() { Latitude = 42.0, Longitude = 18.4 },
            new() { Latitude = 42.4, Longitude = 18.4 },
            new() { Latitude = 42.4, Longitude = 18.0 }
        ];

        var response = await manager.PostAsJsonAsync("/api/admin/territory/regions", request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task Create_region_requires_assigned_territory_and_territory_manager_role()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        var fixture = await CreateRegionCreationFixtureAsync(app);
        await GrantUserRoleAsync(app, "elena@example.com", RoleNames.RegionalManager);
        await GrantTerritoryAssociateAsync(app, fixture.AssignedTerritoryId);

        using var anonymous = app.CreateClient();
        var anonymousResponse = await anonymous.PostAsJsonAsync(
            "/api/admin/territory/regions",
            BuildCreateRequest(fixture.AssignedTerritoryId));
        Assert.Equal(HttpStatusCode.Unauthorized, anonymousResponse.StatusCode);

        using var regionalManager = await LoginAdminAsync(app, "elena@example.com", "password123");
        var regionalResponse = await regionalManager.PostAsJsonAsync(
            "/api/admin/territory/regions",
            BuildCreateRequest(fixture.AssignedTerritoryId));
        Assert.Equal(HttpStatusCode.Forbidden, regionalResponse.StatusCode);

        using var associate = await LoginAdminAsync(app, "marko.p@example.com", "password123");
        var associateResponse = await associate.PostAsJsonAsync(
            "/api/admin/territory/regions",
            BuildCreateRequest(fixture.AssignedTerritoryId));
        Assert.Equal(HttpStatusCode.Forbidden, associateResponse.StatusCode);

        using var manager = await LoginAdminAsync(app, "mika@gmail.com", "Proba123!");
        var unassignedResponse = await manager.PostAsJsonAsync(
            "/api/admin/territory/regions",
            BuildCreateRequest(fixture.UnassignedTerritoryId));
        Assert.Equal(HttpStatusCode.Forbidden, unassignedResponse.StatusCode);
    }

    [Fact]
    public async Task Create_region_requires_assigned_territory_boundary()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        var territoryId = await CreateAssignedTerritoryWithoutBoundaryAsync(app);

        using var manager = await LoginAdminAsync(app, "mika@gmail.com", "Proba123!");
        var response = await manager.PostAsJsonAsync(
            "/api/admin/territory/regions",
            BuildCreateRequest(territoryId));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<Dictionary<string, string>>();
        Assert.Equal(
            "Assigned country/territory must have a boundary before adding regions.",
            body?["message"]);
    }

    [Fact]
    public async Task Create_context_returns_assigned_boundary_existing_region_boundaries_and_blocks_missing_boundary()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        var fixture = await CreateRegionCreationFixtureAsync(app, createExistingRegionBoundary: true);

        using var manager = await LoginAdminAsync(app, "mika@gmail.com", "Proba123!");
        var context = await manager.GetFromJsonAsync<TerritoryRegionCreateContextDto>(
            $"/api/admin/territory/regions/create-context?territoryId={fixture.AssignedTerritoryId}");

        Assert.NotNull(context);
        Assert.True(context!.CanCreate);
        Assert.Equal(fixture.AssignedTerritoryId, context.Territory!.Id);
        Assert.NotNull(context.TerritoryBoundaryGeoJson);
        Assert.False(context.Viewport.IsEmpty);
        Assert.Contains(context.ExistingRegions, item =>
            item.RegionId == fixture.ExistingRegionId &&
            item.GeometryGeoJson is not null);

        var noBoundaryTerritoryId = await CreateAssignedTerritoryWithoutBoundaryAsync(app);
        var noBoundaryContext = await manager.GetFromJsonAsync<TerritoryRegionCreateContextDto>(
            $"/api/admin/territory/regions/create-context?territoryId={noBoundaryTerritoryId}");

        Assert.NotNull(noBoundaryContext);
        Assert.False(noBoundaryContext!.CanCreate);
        Assert.Equal(
            "Assigned country/territory must have a boundary before adding regions.",
            noBoundaryContext.BoundaryRequiredMessage);
    }

    private const string BoundaryGeometryPropertyName = "BoundaryGeometry";

    private static TerritoryRegionCreateRequestDto BuildCreateRequest(
        int territoryId,
        string name = "New Territory Region")
    {
        return new TerritoryRegionCreateRequestDto
        {
            TerritoryId = territoryId,
            Name = name,
            BoundarySource = "ManualDraw",
            Boundary = new TerritoryRegionBoundaryDto
            {
                ShapeType = "Polygon",
                Coordinates =
                [
                    new() { Latitude = 42.4, Longitude = 18.4 },
                    new() { Latitude = 42.4, Longitude = 18.9 },
                    new() { Latitude = 42.9, Longitude = 18.9 },
                    new() { Latitude = 42.9, Longitude = 18.4 }
                ]
            }
        };
    }

    private static async Task<HttpClient> LoginAdminAsync(GeotrackApiFactory app, string email, string password)
    {
        var client = app.CreateClient();

        var loginResponse = await client.PostAsJsonAsync(
            "/api/admin/auth/login",
            new LoginRequestDto
            {
                Email = email,
                Password = password,
                RememberMe = false
            });
        loginResponse.EnsureSuccessStatusCode();

        var session = await loginResponse.Content.ReadFromJsonAsync<AdminLoginResponseDto>();
        Assert.NotNull(session);
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", session!.AccessToken);

        return client;
    }

    private static async Task<RegionCreationFixture> CreateRegionCreationFixtureAsync(
        GeotrackApiFactory app,
        bool createExistingRegionBoundary = false)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var now = DateTime.UtcNow;
        var manager = await db.Users.SingleAsync(user => user.Email == "mika@gmail.com");

        var assignedTerritory = new Territory
        {
            Name = $"Region Create Assigned {Guid.NewGuid():N}",
            CreatedAt = now,
            BoundaryShapeType = "Polygon",
            BoundarySource = "ManualDraw"
        };
        var unassignedTerritory = new Territory
        {
            Name = $"Region Create Unassigned {Guid.NewGuid():N}",
            CreatedAt = now,
            BoundaryShapeType = "Polygon",
            BoundarySource = "ManualDraw"
        };

        db.Territories.AddRange(assignedTerritory, unassignedTerritory);
        await db.SaveChangesAsync();

        SetBoundary(db, assignedTerritory, Polygon(
            (42.0, 18.0),
            (42.0, 20.0),
            (44.0, 20.0),
            (44.0, 18.0)));

        SetBoundary(db, unassignedTerritory, Polygon(
            (42.0, 21.0),
            (42.0, 23.0),
            (44.0, 23.0),
            (44.0, 21.0)));

        db.TerritoryManagerTerritories.Add(new TerritoryManagerTerritory
        {
            UserId = manager.Id,
            TerritoryId = assignedTerritory.Id,
            CreatedAt = now
        });

        Region? existingRegion = null;
        if (createExistingRegionBoundary)
        {
            existingRegion = new Region
            {
                Name = "Existing Boundary Region",
                TerritoryId = assignedTerritory.Id,
                CreatedAt = now,
                BoundaryShapeType = "Polygon",
                BoundarySource = "ManualDraw",
                CreatedByUserId = manager.Id
            };
            db.Regions.Add(existingRegion);
        }

        await db.SaveChangesAsync();

        if (existingRegion is not null)
        {
            db.Entry(existingRegion)
                .Property<Geometry?>(BoundaryGeometryPropertyName)
                .CurrentValue = Polygon(
                    (43.0, 19.0),
                    (43.0, 19.4),
                    (43.4, 19.4),
                    (43.4, 19.0));
            await db.SaveChangesAsync();
        }

        return new RegionCreationFixture(
            assignedTerritory.Id,
            unassignedTerritory.Id,
            manager.Id,
            existingRegion?.Id);
    }

    private static async Task<int> CreateAssignedTerritoryWithoutBoundaryAsync(GeotrackApiFactory app)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var manager = await db.Users.SingleAsync(user => user.Email == "mika@gmail.com");
        var now = DateTime.UtcNow;

        var territory = new Territory
        {
            Name = $"Region Create No Boundary {Guid.NewGuid():N}",
            CreatedAt = now
        };
        db.Territories.Add(territory);
        await db.SaveChangesAsync();

        db.TerritoryManagerTerritories.Add(new TerritoryManagerTerritory
        {
            UserId = manager.Id,
            TerritoryId = territory.Id,
            CreatedAt = now
        });
        await db.SaveChangesAsync();

        return territory.Id;
    }

    private static async Task GrantTerritoryAssociateAsync(GeotrackApiFactory app, int territoryId)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = await db.Users.SingleAsync(existing => existing.Email == "marko.p@example.com");
        var role = await db.Roles.SingleAsync(existing => existing.Name == RoleNames.TerritoryAssociate);

        if (!await db.UserRoles.AnyAsync(existing => existing.UserId == user.Id && existing.RoleId == role.Id))
        {
            db.UserRoles.Add(new UserRole
            {
                UserId = user.Id,
                RoleId = role.Id
            });
        }

        if (!await db.TerritoryManagers.AnyAsync(existing => existing.UserId == user.Id))
        {
            db.TerritoryManagers.Add(new TerritoryManager
            {
                UserId = user.Id
            });
        }

        if (!await db.TerritoryManagerTerritories.AnyAsync(existing =>
                existing.UserId == user.Id &&
                existing.TerritoryId == territoryId))
        {
            db.TerritoryManagerTerritories.Add(new TerritoryManagerTerritory
            {
                UserId = user.Id,
                TerritoryId = territoryId,
                CreatedAt = DateTime.UtcNow
            });
        }

        await db.SaveChangesAsync();
    }

    private static async Task GrantUserRoleAsync(GeotrackApiFactory app, string email, string roleName)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = await db.Users.SingleAsync(existing => existing.Email == email);
        var role = await db.Roles.SingleAsync(existing => existing.Name == roleName);

        if (!await db.UserRoles.AnyAsync(existing => existing.UserId == user.Id && existing.RoleId == role.Id))
        {
            db.UserRoles.Add(new UserRole
            {
                UserId = user.Id,
                RoleId = role.Id
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

    private sealed record RegionCreationFixture(
        int AssignedTerritoryId,
        int UnassignedTerritoryId,
        int ManagerUserId,
        int? ExistingRegionId);
}
