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

public sealed class AdminTerritoryManagementTests
{
    private const string BoundaryGeometryPropertyName = "BoundaryGeometry";
    private static readonly GeometryFactory GeometryFactory = new(new PrecisionModel(), 4326);

    [Fact]
    public async Task Admin_can_load_territory_detail_with_real_boundary_and_assigned_manager()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        var fixture = await CreateFixtureAsync(app, assignManagerEmail: "marko.p@example.com");

        using var admin = await LoginAdminAsync(app, "zika@gmail.com", "Proba123!");
        var detail = await admin.GetFromJsonAsync<AdminTerritoryDetailDto>(
            $"/api/admin/territories/{fixture.TerritoryId}");

        Assert.NotNull(detail);
        Assert.Equal(fixture.TerritoryId, detail!.TerritoryId);
        Assert.Equal(fixture.CountryCode, detail.CountryCode);
        Assert.Equal(fixture.TerritoryName, detail.DisplayName);
        Assert.Equal($"TERR-{fixture.TerritoryId:D3}", detail.InternalReference);
        Assert.Equal("Assigned", detail.Status);
        Assert.True(detail.HasBoundary);
        Assert.NotNull(detail.BoundaryGeoJson);
        Assert.Equal("Polygon", detail.BoundaryGeoJson!.Type);
        Assert.False(detail.Viewport.IsEmpty);
        Assert.NotNull(detail.AssignedManager);
        Assert.Equal(fixture.AssignedManagerUserId, detail.AssignedManager!.UserId);
        Assert.Equal("marko.p@example.com", detail.AssignedManager.Email);
        Assert.Contains(RoleNames.TerritoryManager, detail.AssignedManager.Roles);
    }

    [Fact]
    public async Task Territory_detail_returns_no_manager_safely_when_unassigned()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        var fixture = await CreateFixtureAsync(app);

        using var admin = await LoginAdminAsync(app, "zika@gmail.com", "Proba123!");
        var detail = await admin.GetFromJsonAsync<AdminTerritoryDetailDto>(
            $"/api/admin/territories/{fixture.TerritoryId}");

        Assert.NotNull(detail);
        Assert.Equal("Unassigned", detail!.Status);
        Assert.Null(detail.AssignedManager);
        Assert.Null(detail.AssignedManagerUserId);
        Assert.True(detail.HasBoundary);
        Assert.NotNull(detail.BoundaryGeoJson);
    }

    [Fact]
    public async Task Admin_can_assign_change_and_unassign_manager_without_duplicate_rows()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        var fixture = await CreateFixtureAsync(app);
        var firstManagerId = await GetUserIdAsync(app, "marko.p@example.com");
        var secondManagerId = await GetUserIdAsync(app, "elena@example.com");

        using var admin = await LoginAdminAsync(app, "zika@gmail.com", "Proba123!");

        var assignedResponse = await admin.PutAsJsonAsync(
            $"/api/admin/territories/{fixture.TerritoryId}/manager",
            new AdminAssignTerritoryManagerRequestDto { ManagerUserId = firstManagerId });
        assignedResponse.EnsureSuccessStatusCode();
        var assigned = await assignedResponse.Content.ReadFromJsonAsync<AdminTerritoryDetailDto>();
        Assert.Equal(firstManagerId, assigned!.AssignedManager!.UserId);
        Assert.Equal("Assigned", assigned.Status);

        var changedResponse = await admin.PutAsJsonAsync(
            $"/api/admin/territories/{fixture.TerritoryId}/manager",
            new AdminAssignTerritoryManagerRequestDto { ManagerUserId = secondManagerId });
        changedResponse.EnsureSuccessStatusCode();
        var changed = await changedResponse.Content.ReadFromJsonAsync<AdminTerritoryDetailDto>();
        Assert.Equal(secondManagerId, changed!.AssignedManager!.UserId);

        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.Equal(1, await db.TerritoryManagerTerritories.CountAsync(item => item.TerritoryId == fixture.TerritoryId));
            Assert.False(await HasRoleAsync(db, firstManagerId, RoleNames.TerritoryManager));
            Assert.True(await HasRoleAsync(db, secondManagerId, RoleNames.TerritoryManager));
        }

        var unassignResponse = await admin.PutAsJsonAsync(
            $"/api/admin/territories/{fixture.TerritoryId}/manager",
            new AdminAssignTerritoryManagerRequestDto { ManagerUserId = null });
        unassignResponse.EnsureSuccessStatusCode();
        var unassigned = await unassignResponse.Content.ReadFromJsonAsync<AdminTerritoryDetailDto>();
        Assert.Equal("Unassigned", unassigned!.Status);
        Assert.Null(unassigned.AssignedManager);

        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.Equal(0, await db.TerritoryManagerTerritories.CountAsync(item => item.TerritoryId == fixture.TerritoryId));
            Assert.False(await HasRoleAsync(db, secondManagerId, RoleNames.TerritoryManager));
        }
    }

    [Fact]
    public async Task Admin_cannot_assign_manager_to_country_without_valid_boundary_but_can_unassign()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        var fixture = await CreateFixtureAsync(app, withBoundary: false);
        var managerId = await GetUserIdAsync(app, "marko.p@example.com");

        using var admin = await LoginAdminAsync(app, "zika@gmail.com", "Proba123!");
        var assignResponse = await admin.PutAsJsonAsync(
            $"/api/admin/territories/{fixture.TerritoryId}/manager",
            new AdminAssignTerritoryManagerRequestDto { ManagerUserId = managerId });

        Assert.Equal(HttpStatusCode.BadRequest, assignResponse.StatusCode);
        var error = await assignResponse.Content.ReadFromJsonAsync<Dictionary<string, string>>();
        Assert.Equal(
            "This country cannot be assigned until its boundary geometry is available.",
            error?["message"]);

        await AssignDirectlyAsync(app, fixture.TerritoryId, managerId);
        var unassignResponse = await admin.PutAsJsonAsync(
            $"/api/admin/territories/{fixture.TerritoryId}/manager",
            new AdminAssignTerritoryManagerRequestDto { ManagerUserId = null });

        unassignResponse.EnsureSuccessStatusCode();
        var detail = await unassignResponse.Content.ReadFromJsonAsync<AdminTerritoryDetailDto>();
        Assert.Equal("Unassigned", detail!.Status);
    }

    [Fact]
    public async Task Assignable_managers_endpoint_returns_real_user_roles()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        var fixture = await CreateFixtureAsync(app);

        using var admin = await LoginAdminAsync(app, "zika@gmail.com", "Proba123!");
        var result = await admin.GetFromJsonAsync<AdminTerritoryManagerSearchResponseDto>(
            $"/api/admin/territories/{fixture.TerritoryId}/assignable-managers?query=marko&take=10");

        Assert.NotNull(result);
        var marko = Assert.Single(result!.Items, item => item.Email == "marko.p@example.com");
        Assert.NotEmpty(marko.Roles);
        Assert.True(marko.CanAssign);
        Assert.Equal("Active", marko.Status);
    }

    [Fact]
    public async Task Non_admin_cannot_assign_territory_manager()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        var fixture = await CreateFixtureAsync(app);
        await GrantUserRoleAsync(app, "elena@example.com", RoleNames.RegionalManager);
        var managerId = await GetUserIdAsync(app, "marko.p@example.com");

        using var regionalManager = await LoginAdminAsync(app, "elena@example.com", "password123");
        var response = await regionalManager.PutAsJsonAsync(
            $"/api/admin/territories/{fixture.TerritoryId}/manager",
            new AdminAssignTerritoryManagerRequestDto { ManagerUserId = managerId });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private static async Task<AdminTerritoryFixture> CreateFixtureAsync(
        GeotrackApiFactory app,
        bool withBoundary = true,
        string? assignManagerEmail = null)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = DateTime.UtcNow;

        var territory = new Territory
        {
            Name = $"Admin Territory {Guid.NewGuid():N}",
            CountryCode = UniqueCountryCode(),
            CreatedAt = now,
            BoundaryShapeType = withBoundary ? "Polygon" : null,
            BoundarySource = withBoundary ? "IntegrationTest" : null
        };

        db.Territories.Add(territory);
        await db.SaveChangesAsync();

        if (withBoundary)
        {
            SetBoundary(db, territory, Polygon(
                (10.0, 20.0),
                (10.0, 21.0),
                (11.0, 21.0),
                (11.0, 20.0)));
            await db.SaveChangesAsync();
        }

        int? assignedManagerUserId = null;
        if (!string.IsNullOrWhiteSpace(assignManagerEmail))
        {
            assignedManagerUserId = await db.Users
                .Where(user => user.Email == assignManagerEmail)
                .Select(user => user.Id)
                .SingleAsync();
            await EnsureTerritoryManagerRoleAndProfileAsync(db, assignedManagerUserId.Value);
            db.TerritoryManagerTerritories.Add(new TerritoryManagerTerritory
            {
                UserId = assignedManagerUserId.Value,
                TerritoryId = territory.Id,
                CreatedAt = now
            });
            await db.SaveChangesAsync();
        }

        return new AdminTerritoryFixture(
            territory.Id,
            territory.Name,
            territory.CountryCode!,
            assignedManagerUserId);
    }

    private static async Task AssignDirectlyAsync(GeotrackApiFactory app, int territoryId, int userId)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await EnsureTerritoryManagerRoleAndProfileAsync(db, userId);
        if (!await db.TerritoryManagerTerritories.AnyAsync(item => item.UserId == userId && item.TerritoryId == territoryId))
        {
            db.TerritoryManagerTerritories.Add(new TerritoryManagerTerritory
            {
                UserId = userId,
                TerritoryId = territoryId,
                CreatedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }
    }

    private static async Task EnsureTerritoryManagerRoleAndProfileAsync(AppDbContext db, int userId)
    {
        var role = await db.Roles.SingleAsync(item => item.Name == RoleNames.TerritoryManager);
        if (!await db.UserRoles.AnyAsync(item => item.UserId == userId && item.RoleId == role.Id))
        {
            db.UserRoles.Add(new UserRole { UserId = userId, RoleId = role.Id });
        }

        if (!await db.TerritoryManagers.AnyAsync(item => item.UserId == userId))
        {
            db.TerritoryManagers.Add(new TerritoryManager { UserId = userId });
        }
    }

    private static async Task<bool> HasRoleAsync(AppDbContext db, int userId, string roleName) =>
        await db.UserRoles.AnyAsync(item => item.UserId == userId && item.Role.Name == roleName);

    private static async Task<int> GetUserIdAsync(GeotrackApiFactory app, string email)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Users
            .Where(user => user.Email == email)
            .Select(user => user.Id)
            .SingleAsync();
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
            await db.SaveChangesAsync();
        }
    }

    private static async Task<HttpClient> LoginAdminAsync(GeotrackApiFactory app, string email, string password)
    {
        var client = app.CreateClient();
        var response = await client.PostAsJsonAsync(
            "/api/admin/auth/login",
            new LoginRequestDto
            {
                Email = email,
                Password = password,
                RememberMe = false
            });
        response.EnsureSuccessStatusCode();

        var session = await response.Content.ReadFromJsonAsync<AdminLoginResponseDto>();
        Assert.NotNull(session);
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", session!.AccessToken);

        return client;
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

    private static string UniqueCountryCode()
    {
        var bytes = Guid.NewGuid().ToByteArray();
        return string.Create(2, bytes, static (span, state) =>
        {
            span[0] = (char)('A' + state[0] % 26);
            span[1] = (char)('A' + state[1] % 26);
        });
    }

    private sealed record AdminTerritoryFixture(
        int TerritoryId,
        string TerritoryName,
        string CountryCode,
        int? AssignedManagerUserId);
}
