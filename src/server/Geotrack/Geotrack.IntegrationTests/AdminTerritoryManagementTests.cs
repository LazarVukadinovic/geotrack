using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Geotrack.Application.DTOs.Admin;
using Geotrack.Application.DTOs.Auth;
using Geotrack.Application.Options;
using Geotrack.Application.Interfaces.Service;
using Geotrack.Application.Security;
using Geotrack.Domain.Entities.Core;
using Geotrack.Domain.Entities.Users;
using Geotrack.Infrastructure;
using Geotrack.Infrastructure.Services;
using Geotrack.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;
using Coordinate = NetTopologySuite.Geometries.Coordinate;
using Geometry = NetTopologySuite.Geometries.Geometry;
using GeometryCollection = NetTopologySuite.Geometries.GeometryCollection;
using GeometryFactory = NetTopologySuite.Geometries.GeometryFactory;
using Polygon = NetTopologySuite.Geometries.Polygon;
using PrecisionModel = NetTopologySuite.Geometries.PrecisionModel;

namespace Geotrack.IntegrationTests;

public sealed class AdminTerritoryManagementTests
{
    private const string BoundaryGeometryPropertyName = "BoundaryGeometry";
    private const string BoundaryDisplayGeometryPropertyName = "BoundaryDisplayGeometry";
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
    public async Task Assignment_validation_uses_full_boundary_even_if_display_geometry_is_missing()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        var fixture = await CreateFixtureAsync(app);
        var managerId = await GetUserIdAsync(app, "marko.p@example.com");

        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var territory = await db.Territories.SingleAsync(item => item.Id == fixture.TerritoryId);
            db.Entry(territory)
                .Property<Geometry?>(BoundaryDisplayGeometryPropertyName)
                .CurrentValue = null;
            await db.SaveChangesAsync();
        }

        using var admin = await LoginAdminAsync(app, "zika@gmail.com", "Proba123!");
        var response = await admin.PutAsJsonAsync(
            $"/api/admin/territories/{fixture.TerritoryId}/manager",
            new AdminAssignTerritoryManagerRequestDto { ManagerUserId = managerId });

        response.EnsureSuccessStatusCode();
        var assigned = await response.Content.ReadFromJsonAsync<AdminTerritoryDetailDto>();
        Assert.Equal(managerId, assigned!.AssignedManager!.UserId);
    }

    [Fact]
    public async Task GeoBoundaries_import_uses_iso3_adm0_and_stores_boundary_with_srid_4326()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var requests = new List<Uri>();
        using var httpClient = new HttpClient(new StubHttpMessageHandler(request =>
        {
            requests.Add(request.RequestUri!);
            return request.RequestUri!.ToString() switch
            {
                "https://www.geoboundaries.org/api/current/gbOpen/SRB/ADM0/" => JsonResponse(
                    """
                    {
                      "boundaryName": "Serbia",
                      "boundaryLicense": "CC BY 4.0",
                      "gjDownloadURL": "https://example.test/SRB_ADM0.geojson"
                    }
                    """),
                "https://example.test/SRB_ADM0.geojson" => JsonResponse(
                    """
                    {
                      "type": "FeatureCollection",
                      "features": [
                        {
                          "type": "Feature",
                          "properties": { "shapeName": "Serbia" },
                          "geometry": {
                            "type": "Polygon",
                            "coordinates": [[[20.0,44.0],[21.0,44.0],[21.0,45.0],[20.0,45.0],[20.0,44.0]]]
                          }
                        }
                      ]
                    }
                    """),
                _ => new HttpResponseMessage(HttpStatusCode.NotFound)
            };
        }));
        var service = new GeoBoundariesService(
            db,
            httpClient,
            NullLogger<GeoBoundariesService>.Instance);

        var result = await service.ImportAdm0BoundaryAsync("srb");

        Assert.Contains(requests, uri => uri.ToString() == "https://www.geoboundaries.org/api/current/gbOpen/SRB/ADM0/");
        Assert.Contains(requests, uri => uri.ToString() == "https://example.test/SRB_ADM0.geojson");
        Assert.Equal("SRB", result.Iso3);
        Assert.Equal("RS", result.Iso2);
        Assert.Equal("ADM0", result.AdmLevel);
        Assert.Equal("gbOpen", result.ReleaseType);
        Assert.Equal("CC BY 4.0", result.License);
        Assert.Equal("Polygon", result.BoundaryGeoJson.Type);

        var territory = await db.Territories.SingleAsync(item => item.CountryIso3 == "SRB");
        var geometry = await db.Territories
            .Where(item => item.Id == territory.Id)
            .Select(item => EF.Property<Geometry?>(item, BoundaryGeometryPropertyName))
            .SingleAsync();
        var displayGeometry = await db.Territories
            .Where(item => item.Id == territory.Id)
            .Select(item => EF.Property<Geometry?>(item, BoundaryDisplayGeometryPropertyName))
            .SingleAsync();

        Assert.NotNull(geometry);
        Assert.Equal(4326, geometry!.SRID);
        Assert.True(geometry.IsValid);
        Assert.NotNull(displayGeometry);
        Assert.Equal(4326, displayGeometry!.SRID);
        Assert.True(displayGeometry.IsValid);
        Assert.True(displayGeometry.Coordinates.Length <= geometry.Coordinates.Length);
        Assert.Equal("geoBoundaries", territory.BoundarySource);
        Assert.Equal("https://example.test/SRB_ADM0.geojson", territory.BoundarySourceUrl);
        Assert.Equal("ADM0", territory.BoundaryAdmLevel);
        Assert.Equal("gbOpen", territory.BoundaryReleaseType);
        Assert.Equal("CC BY 4.0", territory.BoundaryLicense);
        Assert.NotNull(territory.BoundaryImportedAt);
    }

    [Fact]
    public async Task GeoBoundaries_import_skips_existing_boundary_unless_force_refresh()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var territory = await EnsureCountryTerritoryAsync(db, "Serbia", "RS", "SRB");
        SetBoundary(db, territory, Polygon(
            (44.0, 20.0),
            (44.0, 21.0),
            (45.0, 21.0),
            (45.0, 20.0)));
        territory.BoundarySource = "geoBoundaries";
        territory.BoundarySourceUrl = "https://example.test/existing-srb.geojson";
        territory.BoundaryAdmLevel = "ADM0";
        territory.BoundaryReleaseType = "gbOpen";
        territory.BoundaryLicense = "CC BY 4.0";
        territory.BoundaryImportedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        var requests = new List<Uri>();
        using var httpClient = new HttpClient(new StubHttpMessageHandler(request =>
        {
            requests.Add(request.RequestUri!);
            return new HttpResponseMessage(HttpStatusCode.InternalServerError);
        }));
        var service = new GeoBoundariesService(
            db,
            httpClient,
            NullLogger<GeoBoundariesService>.Instance);

        var result = await service.ImportAdm0BoundaryAsync("SRB");

        Assert.Empty(requests);
        Assert.True(result.Skipped);
        Assert.Equal("SRB", result.Iso3);
        Assert.Equal("Polygon", result.BoundaryGeoJson.Type);
    }

    [Fact]
    public async Task GeoBoundaries_missing_import_continues_when_one_country_fails()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var serbia = await EnsureCountryTerritoryAsync(db, "Serbia", "RS", "SRB");
        var croatia = await EnsureCountryTerritoryAsync(db, "Croatia", "HR", "HRV");
        SetBoundary(db, serbia, null);
        SetBoundary(db, croatia, null);
        serbia.BoundarySource = null;
        croatia.BoundarySource = null;
        await db.SaveChangesAsync();

        using var httpClient = new HttpClient(new StubHttpMessageHandler(request =>
        {
            return request.RequestUri!.ToString() switch
            {
                "https://www.geoboundaries.org/api/current/gbOpen/SRB/ADM0/" => JsonResponse(
                    """
                    {
                      "boundaryName": "Serbia",
                      "boundaryLicense": "CC BY 4.0",
                      "gjDownloadURL": "https://example.test/SRB_ADM0.geojson"
                    }
                    """),
                "https://example.test/SRB_ADM0.geojson" => JsonResponse(
                    """
                    {
                      "type": "Feature",
                      "geometry": {
                        "type": "Polygon",
                        "coordinates": [[[20.0,44.0],[21.0,44.0],[21.0,45.0],[20.0,45.0],[20.0,44.0]]]
                      }
                    }
                    """),
                "https://www.geoboundaries.org/api/current/gbOpen/HRV/ADM0/" =>
                    new HttpResponseMessage(HttpStatusCode.BadGateway),
                _ => new HttpResponseMessage(HttpStatusCode.NotFound)
            };
        }));
        var service = new GeoBoundariesService(
            db,
            httpClient,
            NullLogger<GeoBoundariesService>.Instance);

        var result = await service.ImportMissingAdm0BoundariesAsync();

        Assert.True(result.RequestedCount >= 2);
        Assert.Equal(1, result.ImportedCount);
        Assert.True(result.FailedCount >= 1);
        Assert.Contains(result.Results, item => item.Iso3 == "SRB" && !item.Skipped);
        Assert.Contains(result.Failures, item => item.Iso3 == "HRV");

        var serbiaGeometry = await db.Territories
            .Where(item => item.Id == serbia.Id)
            .Select(item => EF.Property<Geometry?>(item, BoundaryGeometryPropertyName))
            .SingleAsync();
        var serbiaDisplayGeometry = await db.Territories
            .Where(item => item.Id == serbia.Id)
            .Select(item => EF.Property<Geometry?>(item, BoundaryDisplayGeometryPropertyName))
            .SingleAsync();
        var croatiaGeometry = await db.Territories
            .Where(item => item.Id == croatia.Id)
            .Select(item => EF.Property<Geometry?>(item, BoundaryGeometryPropertyName))
            .SingleAsync();

        Assert.NotNull(serbiaGeometry);
        Assert.NotNull(serbiaDisplayGeometry);
        Assert.True(serbiaDisplayGeometry!.Coordinates.Length <= serbiaGeometry!.Coordinates.Length);
        Assert.Null(croatiaGeometry);
    }

    [Fact]
    public async Task GeoBoundaries_missing_import_seeds_country_rows_and_map_returns_geometry()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var existingSpain = await db.Territories.SingleOrDefaultAsync(item => item.CountryIso3 == "ESP");
        Assert.Null(existingSpain);

        using var httpClient = new HttpClient(new StubHttpMessageHandler(request =>
        {
            return request.RequestUri!.ToString() switch
            {
                "https://www.geoboundaries.org/api/current/gbOpen/ESP/ADM0/" => JsonResponse(
                    """
                    {
                      "boundaryName": "Spain",
                      "boundaryLicense": "Creative Commons Attribution 4.0 International (CC BY 4.0)",
                      "gjDownloadURL": "https://example.test/ESP_ADM0.geojson"
                    }
                    """),
                "https://example.test/ESP_ADM0.geojson" => JsonResponse(
                    """
                    {
                      "type": "Feature",
                      "geometry": {
                        "type": "MultiPolygon",
                        "coordinates": [
                          [[[-9.3,36.0],[3.4,36.0],[3.4,43.8],[-9.3,43.8],[-9.3,36.0]]],
                          [[[-18.2,27.5],[-13.2,27.5],[-13.2,29.5],[-18.2,29.5],[-18.2,27.5]]]
                        ]
                      }
                    }
                    """),
                _ => new HttpResponseMessage(HttpStatusCode.NotFound)
            };
        }));
        var geoBoundaries = new GeoBoundariesService(
            db,
            httpClient,
            NullLogger<GeoBoundariesService>.Instance);

        var import = await geoBoundaries.ImportMissingAdm0BoundariesAsync();

        Assert.Contains(import.Results, item => item.Iso3 == "ESP" && !item.Skipped);
        var spain = await db.Territories.SingleAsync(item => item.CountryIso3 == "ESP");
        var geometry = await db.Territories
            .Where(item => item.Id == spain.Id)
            .Select(item => EF.Property<Geometry?>(item, BoundaryGeometryPropertyName))
            .SingleAsync();
        var displayGeometry = await db.Territories
            .Where(item => item.Id == spain.Id)
            .Select(item => EF.Property<Geometry?>(item, BoundaryDisplayGeometryPropertyName))
            .SingleAsync();

        Assert.NotNull(geometry);
        Assert.Equal(4326, geometry!.SRID);
        Assert.NotNull(displayGeometry);
        Assert.Equal(4326, displayGeometry!.SRID);
        Assert.True(displayGeometry.Coordinates.Length <= geometry.Coordinates.Length);
        Assert.Equal("CC BY 4.0", spain.BoundaryLicense);

        var management = new AdminTerritoryManagementService(db, geoBoundaries);
        var map = await management.GetTerritoryMapAsync();
        var feature = Assert.Single(map.Features, item => item.Properties.Iso3 == "ESP");

        Assert.NotNull(feature.Geometry);
        Assert.Equal("MultiPolygon", feature.Geometry!.Type);
        Assert.True(feature.Properties.HasBoundary);
        Assert.Equal("Available", feature.Properties.Status);
        Assert.True(feature.Properties.CanAssign);
        Assert.False(feature.Properties.CanEdit);
        Assert.False(feature.Properties.Viewport.IsEmpty);
        Assert.NotNull(feature.Properties.Viewport.Bounds);
    }

    [Fact]
    public async Task Local_adm0_import_reads_configured_file_updates_creates_skips_and_reports_failures()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var tempRoot = CreateTempBoundaryDirectory();

        try
        {
            var adm0Path = Path.Combine(tempRoot, "local-adm0.geojson");
            await File.WriteAllTextAsync(adm0Path, """
                {
                  "type": "FeatureCollection",
                  "features": [
                    {
                      "type": "Feature",
                      "properties": { "shapeGroup": "TAA", "shapeType": "ADM0", "shapeName": "Test Alpha" },
                      "geometry": {
                        "type": "Polygon",
                        "coordinates": [[[10.0,10.0],[11.0,10.0],[11.0,11.0],[10.0,11.0],[10.0,10.0]]]
                      }
                    },
                    {
                      "type": "Feature",
                      "properties": { "shapeGroup": "TBB", "shapeType": "ADM0", "shapeName": "Test Beta" },
                      "geometry": {
                        "type": "MultiPolygon",
                        "coordinates": [
                          [[[20.0,20.0],[21.0,20.0],[21.0,21.0],[20.0,21.0],[20.0,20.0]]],
                          [[[22.0,20.0],[23.0,20.0],[23.0,21.0],[22.0,21.0],[22.0,20.0]]]
                        ]
                      }
                    },
                    {
                      "type": "Feature",
                      "properties": { "shapeGroup": "TCC", "shapeType": "ADM0", "shapeName": "Broken" },
                      "geometry": {
                        "type": "Polygon",
                        "coordinates": [[[30.0,30.0],[30.0,30.0],[30.0,30.0]]]
                      }
                    }
                  ]
                }
                """);

            var existing = new Territory
            {
                Name = "Existing Alpha",
                CountryIso3 = "TAA",
                CreatedAt = DateTime.UtcNow
            };
            db.Territories.Add(existing);
            await db.SaveChangesAsync();

            var service = CreateLocalBoundaryImportService(db, tempRoot, adm0Path: adm0Path);
            var summary = await service.ImportLocalBoundariesAsync("ADM0");

            Assert.Equal("ADM0", summary.AdmLevel);
            Assert.Equal(3, summary.TotalFeatures);
            Assert.Equal(1, summary.ImportedCount);
            Assert.Equal(1, summary.UpdatedCount);
            Assert.Equal(1, summary.FailedCount);
            Assert.Contains(summary.Items, item => item.Iso3 == "TAA" && item.Status == "Updated");
            Assert.Contains(summary.Items, item => item.Iso3 == "TBB" && item.Status == "Imported");
            var failed = Assert.Single(summary.Items, item => item.Iso3 == "TCC" && item.Status == "Failed");
            Assert.Equal("TCC", failed.ShapeGroup);
            Assert.Equal("Broken", failed.Name);
            Assert.Equal("ADM0", failed.ShapeType);
            Assert.Contains("Boundary", failed.Message, StringComparison.OrdinalIgnoreCase);

            var alpha = await db.Territories.SingleAsync(item => item.CountryIso3 == "TAA");
            var beta = await db.Territories.SingleAsync(item => item.CountryIso3 == "TBB");
            var alphaGeometry = await db.Territories
                .Where(item => item.Id == alpha.Id)
                .Select(item => EF.Property<Geometry?>(item, BoundaryGeometryPropertyName))
                .SingleAsync();
            var betaGeometry = await db.Territories
                .Where(item => item.Id == beta.Id)
                .Select(item => EF.Property<Geometry?>(item, BoundaryGeometryPropertyName))
                .SingleAsync();
            var alphaDisplayGeometry = await db.Territories
                .Where(item => item.Id == alpha.Id)
                .Select(item => EF.Property<Geometry?>(item, BoundaryDisplayGeometryPropertyName))
                .SingleAsync();
            var betaDisplayGeometry = await db.Territories
                .Where(item => item.Id == beta.Id)
                .Select(item => EF.Property<Geometry?>(item, BoundaryDisplayGeometryPropertyName))
                .SingleAsync();

            Assert.NotNull(alphaGeometry);
            Assert.Equal(4326, alphaGeometry!.SRID);
            Assert.Equal("Polygon", alphaGeometry.GeometryType);
            Assert.NotNull(alphaDisplayGeometry);
            Assert.Equal(4326, alphaDisplayGeometry!.SRID);
            Assert.True(alphaDisplayGeometry.Coordinates.Length <= alphaGeometry.Coordinates.Length);
            Assert.Equal("geoBoundaries", alpha.BoundarySource);
            Assert.Equal("ADM0", alpha.BoundaryAdmLevel);
            Assert.Equal("gbOpen", alpha.BoundaryReleaseType);
            Assert.Equal("CC BY 4.0", alpha.BoundaryLicense);
            Assert.StartsWith("local://", alpha.BoundarySourceUrl);

            Assert.NotNull(betaGeometry);
            Assert.Equal(4326, betaGeometry!.SRID);
            Assert.Equal("MultiPolygon", betaGeometry.GeometryType);
            Assert.NotNull(betaDisplayGeometry);
            Assert.Equal(4326, betaDisplayGeometry!.SRID);
            Assert.True(betaDisplayGeometry.Coordinates.Length <= betaGeometry.Coordinates.Length);

            var secondRun = await service.ImportLocalBoundariesAsync("ADM0");

            Assert.Equal(3, secondRun.TotalFeatures);
            Assert.Equal(0, secondRun.ImportedCount);
            Assert.Equal(0, secondRun.UpdatedCount);
            Assert.Equal(2, secondRun.SkippedCount);
            Assert.Equal(1, secondRun.FailedCount);
        }
        finally
        {
            TryDeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public async Task Local_adm0_import_accepts_normalized_missing_and_non_adm_country_candidates()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var tempRoot = CreateTempBoundaryDirectory();

        try
        {
            var adm0Path = Path.Combine(tempRoot, "local-adm0-variants.geojson");
            await File.WriteAllTextAsync(adm0Path, """
                {
                  "type": "FeatureCollection",
                  "features": [
                    {
                      "type": "Feature",
                      "properties": { "shapeGroup": "TDD", "shapeType": " adm0 ", "shapeName": "Whitespace ADM0", "shapeID": "TDD-ADM0" },
                      "geometry": {
                        "type": "Polygon",
                        "coordinates": [[[10.0,10.0],[11.0,10.0],[11.0,11.0],[10.0,11.0],[10.0,10.0]]]
                      }
                    },
                    {
                      "type": "Feature",
                      "properties": { "shapeGroup": "TEE", "shapeName": "Missing Shape Type", "shapeID": "TEE-MISSING" },
                      "geometry": {
                        "type": "Polygon",
                        "coordinates": [[[20.0,20.0],[21.0,20.0],[21.0,21.0],[20.0,21.0],[20.0,20.0]]]
                      }
                    },
                    {
                      "type": "Feature",
                      "properties": { "shapeGroup": "TFF", "shapeType": "DISP", "shapeName": "Disputed Country Candidate", "shapeID": "TFF-DISP" },
                      "geometry": {
                        "type": "Polygon",
                        "coordinates": [[[30.0,30.0],[31.0,30.0],[31.0,31.0],[30.0,31.0],[30.0,30.0]]]
                      }
                    }
                  ]
                }
                """);

            var service = CreateLocalBoundaryImportService(db, tempRoot, adm0Path: adm0Path);
            var summary = await service.ImportLocalBoundariesAsync("ADM0");

            Assert.Equal(3, summary.TotalFeatures);
            Assert.Equal(3, summary.ImportedCount);
            Assert.Equal(2, summary.ImportedCountriesCount);
            Assert.Equal(1, summary.ImportedDisputedCount);
            Assert.Equal(0, summary.FailedCount);

            var whitespaceItem = Assert.Single(summary.Items, item => item.Iso3 == "TDD");
            Assert.Equal(TerritoryKinds.Country, whitespaceItem.Kind);
            Assert.Equal(" adm0 ", whitespaceItem.ShapeType);
            Assert.Equal("TDD-ADM0", whitespaceItem.ShapeId);
            var missingItem = Assert.Single(summary.Items, item => item.Iso3 == "TEE");
            Assert.Equal(TerritoryKinds.Country, missingItem.Kind);
            Assert.Null(missingItem.ShapeType);
            Assert.Equal("TEE-MISSING", missingItem.ShapeId);
            var disputedItem = Assert.Single(summary.Items, item => item.ShapeGroup == "TFF");
            Assert.Equal(TerritoryKinds.DisputedTerritory, disputedItem.Kind);
            Assert.Null(disputedItem.Iso3);
            Assert.Equal("DISP", disputedItem.ShapeType);
            Assert.Equal("TFF-DISP", disputedItem.ShapeId);

            foreach (var iso3 in new[] { "TDD", "TEE" })
            {
                var territory = await db.Territories.SingleAsync(item => item.CountryIso3 == iso3);
                var geometry = await db.Territories
                    .Where(item => item.Id == territory.Id)
                    .Select(item => EF.Property<Geometry?>(item, BoundaryGeometryPropertyName))
                    .SingleAsync();
                var displayGeometry = await db.Territories
                    .Where(item => item.Id == territory.Id)
                    .Select(item => EF.Property<Geometry?>(item, BoundaryDisplayGeometryPropertyName))
                    .SingleAsync();

                Assert.NotNull(geometry);
                Assert.Equal(4326, geometry!.SRID);
                Assert.True(geometry.IsValid);
                Assert.NotNull(displayGeometry);
                Assert.Equal(4326, displayGeometry!.SRID);
                Assert.True(displayGeometry.Coordinates.Length <= geometry.Coordinates.Length);
                Assert.Equal("ADM0", territory.BoundaryAdmLevel);
            }

            var disputed = await db.Territories.SingleAsync(item =>
                item.TerritoryKind == TerritoryKinds.DisputedTerritory &&
                item.BoundaryShapeGroup == "TFF");
            var disputedGeometry = await db.Territories
                .Where(item => item.Id == disputed.Id)
                .Select(item => EF.Property<Geometry?>(item, BoundaryGeometryPropertyName))
                .SingleAsync();
            var disputedDisplayGeometry = await db.Territories
                .Where(item => item.Id == disputed.Id)
                .Select(item => EF.Property<Geometry?>(item, BoundaryDisplayGeometryPropertyName))
                .SingleAsync();

            Assert.Equal("Disputed Country Candidate", disputed.Name);
            Assert.Null(disputed.CountryIso3);
            Assert.Null(disputed.CountryCode);
            Assert.Equal("DISP", disputed.BoundaryAdmLevel);
            Assert.Equal("DISP", disputed.BoundaryShapeType);
            Assert.Equal("TFF-DISP", disputed.BoundaryShapeId);
            Assert.Equal("geoBoundaries:DISP:TFF", disputed.BoundaryExternalKey);
            Assert.NotNull(disputedGeometry);
            Assert.Equal(4326, disputedGeometry!.SRID);
            Assert.NotNull(disputedDisplayGeometry);
            Assert.Equal(4326, disputedDisplayGeometry!.SRID);
        }
        finally
        {
            TryDeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public async Task Local_adm0_imports_disputed_features_as_view_only_map_boundaries()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var tempRoot = CreateTempBoundaryDirectory();

        try
        {
            var adm0Path = Path.Combine(tempRoot, "local-adm0-disputed.geojson");
            await File.WriteAllTextAsync(adm0Path, """
                {
                  "type": "FeatureCollection",
                  "features": [
                    {
                      "type": "Feature",
                      "properties": { "shapeGroup": "TAA", "shapeType": "ADM0", "shapeName": "Test Alpha", "shapeID": "TAA-ADM0" },
                      "geometry": {
                        "type": "Polygon",
                        "coordinates": [[[10.0,10.0],[11.0,10.0],[11.0,11.0],[10.0,11.0],[10.0,10.0]]]
                      }
                    },
                    {
                      "type": "Feature",
                      "properties": { "shapeGroup": "111", "shapeType": "DISP", "shapeName": "Abyei", "shapeID": "ABYEI-DISP" },
                      "geometry": {
                        "type": "Polygon",
                        "coordinates": [[[28.0,9.0],[29.0,9.0],[29.0,10.0],[28.0,10.0],[28.0,9.0]]]
                      }
                    }
                  ]
                }
                """);

            var importService = CreateLocalBoundaryImportService(db, tempRoot, adm0Path: adm0Path);
            var summary = await importService.ImportLocalBoundariesAsync("ADM0");

            Assert.Equal(2, summary.ImportedCount);
            Assert.Equal(1, summary.ImportedCountriesCount);
            Assert.Equal(1, summary.ImportedDisputedCount);
            Assert.Equal(0, summary.FailedCount);
            Assert.Contains(summary.Items, item =>
                item.Kind == TerritoryKinds.DisputedTerritory &&
                item.Iso3 is null &&
                item.ShapeGroup == "111" &&
                item.ShapeType == "DISP" &&
                item.Status == "Imported");

            var disputed = await db.Territories.SingleAsync(item =>
                item.TerritoryKind == TerritoryKinds.DisputedTerritory &&
                item.BoundaryShapeGroup == "111");
            var disputedGeometry = await db.Territories
                .Where(item => item.Id == disputed.Id)
                .Select(item => EF.Property<Geometry?>(item, BoundaryGeometryPropertyName))
                .SingleAsync();

            Assert.Equal("Abyei", disputed.Name);
            Assert.Null(disputed.CountryIso3);
            Assert.Null(disputed.CountryCode);
            Assert.Equal("DISP", disputed.BoundaryAdmLevel);
            Assert.Equal("DISP", disputed.BoundaryShapeType);
            Assert.Equal("ABYEI-DISP", disputed.BoundaryShapeId);
            Assert.Equal("geoBoundaries:DISP:111", disputed.BoundaryExternalKey);
            Assert.NotNull(disputedGeometry);
            Assert.Equal(4326, disputedGeometry!.SRID);

            var management = scope.ServiceProvider.GetRequiredService<IAdminTerritoryManagementService>();
            var map = await management.GetTerritoryMapAsync();
            var feature = Assert.Single(map.Features, item => item.Properties.TerritoryId == disputed.Id);

            Assert.NotNull(feature.Geometry);
            Assert.Equal(TerritoryKinds.DisputedTerritory, feature.Properties.TerritoryKind);
            Assert.Null(feature.Properties.Iso2);
            Assert.Null(feature.Properties.Iso3);
            Assert.Equal("DISP", feature.Properties.ShapeType);
            Assert.Equal("111", feature.Properties.ShapeGroup);
            Assert.Equal("ABYEI-DISP", feature.Properties.ShapeId);
            Assert.Equal("DisputedViewOnly", feature.Properties.Status);
            Assert.True(feature.Properties.HasBoundary);
            Assert.False(feature.Properties.CanAssign);
            Assert.True(feature.Properties.CanEdit);
            Assert.Contains("cannot be assigned", feature.Properties.BoundaryRequiredMessage);

            var managerId = await GetUserIdAsync(app, "marko.p@example.com");
            using var admin = await LoginAdminAsync(app, "zika@gmail.com", "Proba123!");
            var assignResponse = await admin.PutAsJsonAsync(
                $"/api/admin/territories/{disputed.Id}/manager",
                new AdminAssignTerritoryManagerRequestDto { ManagerUserId = managerId });

            Assert.Equal(HttpStatusCode.BadRequest, assignResponse.StatusCode);
            var error = await assignResponse.Content.ReadFromJsonAsync<Dictionary<string, string>>();
            Assert.Contains("disputed boundary", error?["message"], StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            TryDeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public async Task Local_adm0_import_reuses_existing_name_for_western_sahara_disputed_boundary()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var tempRoot = CreateTempBoundaryDirectory();

        try
        {
            var existing = new Territory
            {
                Name = "Western Sahara",
                TerritoryKind = TerritoryKinds.Country,
                CountryCode = "EH",
                CountryIso3 = "ESH",
                CreatedAt = DateTime.UtcNow
            };
            db.Territories.Add(existing);
            await db.SaveChangesAsync();

            var adm0Path = Path.Combine(tempRoot, "western-sahara.geojson");
            await File.WriteAllTextAsync(adm0Path, """
                {
                  "type": "FeatureCollection",
                  "features": [
                    {
                      "type": "Feature",
                      "properties": { "shapeGroup": "ESH", "shapeType": "DISP", "shapeName": "Western Sahara", "shapeID": "ESH-DISP" },
                      "geometry": {
                        "type": "Polygon",
                        "coordinates": [[[-9.0,27.0],[-8.0,27.0],[-8.0,28.0],[-9.0,28.0],[-9.0,27.0]]]
                      }
                    }
                  ]
                }
                """);

            var service = CreateLocalBoundaryImportService(db, tempRoot, adm0Path: adm0Path);
            var summary = await service.ImportLocalBoundariesAsync("ADM0");

            Assert.Equal(1, summary.TotalFeatures);
            Assert.Equal(0, summary.FailedCount);
            Assert.Equal(1, summary.UpdatedDisputedCount);
            var item = Assert.Single(summary.Items);
            Assert.Equal("Updated", item.Status);
            Assert.Equal(TerritoryKinds.DisputedTerritory, item.Kind);

            var westernSaharaRows = await db.Territories
                .Where(territory => territory.Name == "Western Sahara")
                .ToListAsync();
            var westernSahara = Assert.Single(westernSaharaRows);
            Assert.Equal(existing.Id, westernSahara.Id);
            Assert.Equal(TerritoryKinds.DisputedTerritory, westernSahara.TerritoryKind);
            Assert.Null(westernSahara.CountryCode);
            Assert.Null(westernSahara.CountryIso3);
            Assert.Equal("ESH", westernSahara.BoundaryShapeGroup);
            Assert.Equal("DISP", westernSahara.BoundaryAdmLevel);
            Assert.Equal("geoBoundaries:DISP:ESH", westernSahara.BoundaryExternalKey);

            var geometry = await db.Territories
                .Where(territory => territory.Id == westernSahara.Id)
                .Select(territory => EF.Property<Geometry?>(territory, BoundaryGeometryPropertyName))
                .SingleAsync();
            var displayGeometry = await db.Territories
                .Where(territory => territory.Id == westernSahara.Id)
                .Select(territory => EF.Property<Geometry?>(territory, BoundaryDisplayGeometryPropertyName))
                .SingleAsync();

            Assert.NotNull(geometry);
            Assert.NotNull(displayGeometry);
        }
        finally
        {
            TryDeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public async Task Local_adm0_import_updates_existing_country_by_name_when_iso3_is_missing_on_row()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var tempRoot = CreateTempBoundaryDirectory();

        try
        {
            var existing = new Territory
            {
                Name = "Test Name Match",
                TerritoryKind = TerritoryKinds.Country,
                CreatedAt = DateTime.UtcNow
            };
            db.Territories.Add(existing);
            await db.SaveChangesAsync();

            var adm0Path = Path.Combine(tempRoot, "name-match.geojson");
            await File.WriteAllTextAsync(adm0Path, """
                {
                  "type": "FeatureCollection",
                  "features": [
                    {
                      "type": "Feature",
                      "properties": { "shapeGroup": "TNM", "shapeType": "ADM0", "shapeName": "Test Name Match", "shapeID": "TNM-ADM0" },
                      "geometry": {
                        "type": "Polygon",
                        "coordinates": [[[12.0,12.0],[13.0,12.0],[13.0,13.0],[12.0,13.0],[12.0,12.0]]]
                      }
                    }
                  ]
                }
                """);

            var service = CreateLocalBoundaryImportService(db, tempRoot, adm0Path: adm0Path);
            var summary = await service.ImportLocalBoundariesAsync("ADM0");

            Assert.Equal(0, summary.FailedCount);
            Assert.Equal(1, summary.UpdatedCountriesCount);
            var territory = await db.Territories.SingleAsync(item => item.Name == "Test Name Match");
            Assert.Equal(existing.Id, territory.Id);
            Assert.Equal("TNM", territory.CountryIso3);
            Assert.Equal("geoBoundaries:ADM0:TNM", territory.BoundaryExternalKey);
        }
        finally
        {
            TryDeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public async Task Local_adm0_import_maps_kosovo_xkx_to_existing_canonical_kosovo_row()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var tempRoot = CreateTempBoundaryDirectory();

        try
        {
            var existing = new Territory
            {
                Name = "Kosovo",
                TerritoryKind = TerritoryKinds.Country,
                CountryCode = "XK",
                CountryIso3 = "XKK",
                CreatedAt = DateTime.UtcNow
            };
            db.Territories.Add(existing);
            await db.SaveChangesAsync();

            var adm0Path = Path.Combine(tempRoot, "kosovo-xkx.geojson");
            await File.WriteAllTextAsync(adm0Path, """
                {
                  "type": "FeatureCollection",
                  "features": [
                    {
                      "type": "Feature",
                      "properties": { "shapeGroup": "XKX", "shapeType": "ADM0", "shapeName": "Kosovo", "shapeID": "XKX-ADM0" },
                      "geometry": {
                        "type": "Polygon",
                        "coordinates": [[[20.0,42.0],[21.0,42.0],[21.0,43.0],[20.0,43.0],[20.0,42.0]]]
                      }
                    }
                  ]
                }
                """);

            var service = CreateLocalBoundaryImportService(db, tempRoot, adm0Path: adm0Path);
            var summary = await service.ImportLocalBoundariesAsync("ADM0");

            Assert.Equal(1, summary.TotalFeatures);
            Assert.Equal(0, summary.FailedCount);
            Assert.Equal(1, summary.UpdatedCountriesCount);
            Assert.Contains(summary.Items, item => item.Iso3 == "XKX" && item.Status == "Updated");

            var kosovoRows = await db.Territories
                .Where(item => item.Name == "Kosovo" || item.CountryIso3 == "XKK" || item.CountryIso3 == "XKX")
                .ToListAsync();
            var kosovo = Assert.Single(kosovoRows);
            var geometry = await db.Territories
                .Where(item => item.Id == kosovo.Id)
                .Select(item => EF.Property<Geometry?>(item, BoundaryGeometryPropertyName))
                .SingleAsync();
            var displayGeometry = await db.Territories
                .Where(item => item.Id == kosovo.Id)
                .Select(item => EF.Property<Geometry?>(item, BoundaryDisplayGeometryPropertyName))
                .SingleAsync();

            Assert.Equal(existing.Id, kosovo.Id);
            Assert.Equal("XKK", kosovo.CountryIso3);
            Assert.Equal("XK", kosovo.CountryCode);
            Assert.Equal("XKX", kosovo.BoundaryShapeGroup);
            Assert.Equal("XKX-ADM0", kosovo.BoundaryShapeId);
            Assert.Equal("geoBoundaries:ADM0:XKX", kosovo.BoundaryExternalKey);
            Assert.NotNull(geometry);
            Assert.NotNull(displayGeometry);

            var management = scope.ServiceProvider.GetRequiredService<IAdminTerritoryManagementService>();
            var map = await management.GetTerritoryMapAsync();
            var feature = Assert.Single(map.Features, item => item.Properties.TerritoryId == kosovo.Id);
            Assert.NotNull(feature.Geometry);
            Assert.Equal("XKK", feature.Properties.Iso3);
            Assert.Equal("XKX", feature.Properties.ShapeGroup);
            Assert.Equal("Available", feature.Properties.Status);
        }
        finally
        {
            TryDeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public async Task Local_adm0_import_keeps_unknown_iso_mismatch_failed_safely()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var tempRoot = CreateTempBoundaryDirectory();

        try
        {
            var existing = new Territory
            {
                Name = "Mismatchland",
                TerritoryKind = TerritoryKinds.Country,
                CountryCode = "OD",
                CountryIso3 = "OLD",
                CreatedAt = DateTime.UtcNow
            };
            db.Territories.Add(existing);
            await db.SaveChangesAsync();

            var adm0Path = Path.Combine(tempRoot, "unknown-mismatch.geojson");
            await File.WriteAllTextAsync(adm0Path, """
                {
                  "type": "FeatureCollection",
                  "features": [
                    {
                      "type": "Feature",
                      "properties": { "shapeGroup": "NEW", "shapeType": "ADM0", "shapeName": "Mismatchland", "shapeID": "NEW-ADM0" },
                      "geometry": {
                        "type": "Polygon",
                        "coordinates": [[[14.0,14.0],[15.0,14.0],[15.0,15.0],[14.0,15.0],[14.0,14.0]]]
                      }
                    }
                  ]
                }
                """);

            var service = CreateLocalBoundaryImportService(db, tempRoot, adm0Path: adm0Path);
            var summary = await service.ImportLocalBoundariesAsync("ADM0");

            Assert.Equal(1, summary.TotalFeatures);
            Assert.Equal(1, summary.FailedCount);
            Assert.Equal(0, summary.UpdatedCount);
            var failed = Assert.Single(summary.Items);
            Assert.Equal("NEW", failed.Iso3);
            Assert.Equal("Failed", failed.Status);
            Assert.Contains("ISO3 code does not match", failed.Message);

            var territory = await db.Territories.SingleAsync(item => item.Id == existing.Id);
            var geometry = await db.Territories
                .Where(item => item.Id == existing.Id)
                .Select(item => EF.Property<Geometry?>(item, BoundaryGeometryPropertyName))
                .SingleAsync();

            Assert.Equal("OLD", territory.CountryIso3);
            Assert.Equal("OD", territory.CountryCode);
            Assert.Null(geometry);
            Assert.False(await db.Territories.AnyAsync(item => item.CountryIso3 == "NEW"));
        }
        finally
        {
            TryDeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public async Task Local_adm0_import_keeps_valid_rus_multipolygon_members_when_source_contains_degenerate_member()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var tempRoot = CreateTempBoundaryDirectory();

        try
        {
            var adm0Path = Path.Combine(tempRoot, "rus-multipolygon.geojson");
            await File.WriteAllTextAsync(adm0Path, """
                {
                  "type": "FeatureCollection",
                  "features": [
                    {
                      "type": "Feature",
                      "properties": { "shapeGroup": "RUS", "shapeType": "ADM0", "shapeName": "Russia", "shapeID": "RUS-ADM0" },
                      "geometry": {
                        "type": "MultiPolygon",
                        "coordinates": [
                          [[[30.0,55.0],[31.0,55.0],[31.0,56.0],[30.0,56.0],[30.0,55.0]]],
                          [[[50.0,55.0],[51.0,55.0],[51.0,56.0],[50.0,56.0],[50.0,55.0]]],
                          [[[40.0,55.0],[40.0,55.0],[40.0,55.0]]]
                        ]
                      }
                    }
                  ]
                }
                """);

            var service = CreateLocalBoundaryImportService(db, tempRoot, adm0Path: adm0Path);
            var summary = await service.ImportLocalBoundariesAsync("ADM0");

            Assert.Equal(1, summary.TotalFeatures);
            Assert.Equal(1, summary.ImportedCountriesCount);
            Assert.Equal(0, summary.FailedCount);
            Assert.Contains(summary.Items, item => item.Iso3 == "RUS" && item.Status == "Imported");

            var russia = await db.Territories.SingleAsync(item => item.CountryIso3 == "RUS");
            var geometry = await db.Territories
                .Where(item => item.Id == russia.Id)
                .Select(item => EF.Property<Geometry?>(item, BoundaryGeometryPropertyName))
                .SingleAsync();

            Assert.Equal("Russia", russia.Name);
            Assert.NotNull(geometry);
            Assert.Equal("MultiPolygon", geometry!.GeometryType);
            Assert.True(geometry!.IsValid);

            var management = scope.ServiceProvider.GetRequiredService<IAdminTerritoryManagementService>();
            var map = await management.GetTerritoryMapAsync();
            var feature = Assert.Single(map.Features, item => item.Properties.Iso3 == "RUS");

            Assert.NotNull(feature.Geometry);
            Assert.Equal("MultiPolygon", feature.Geometry!.Type);
            Assert.Equal("Available", feature.Properties.Status);
            Assert.True(feature.Properties.HasDisplayBoundary);
            Assert.False(feature.Properties.Viewport.IsEmpty);
        }
        finally
        {
            TryDeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public async Task Local_adm0_import_rejects_clear_region_shape_type_and_reports_properties()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var tempRoot = CreateTempBoundaryDirectory();

        try
        {
            var adm0Path = Path.Combine(tempRoot, "local-adm0-wrong-level.geojson");
            await File.WriteAllTextAsync(adm0Path, """
                {
                  "type": "FeatureCollection",
                  "features": [
                    {
                      "type": "Feature",
                      "properties": { "shapeGroup": "TRJ", "shapeType": "ADM1", "shapeName": "Wrong Region", "shapeID": "REGION-IN-ADM0" },
                      "geometry": {
                        "type": "Polygon",
                        "coordinates": [[[10.0,10.0],[11.0,10.0],[11.0,11.0],[10.0,11.0],[10.0,10.0]]]
                      }
                    }
                  ]
                }
                """);

            var service = CreateLocalBoundaryImportService(db, tempRoot, adm0Path: adm0Path);
            var summary = await service.ImportLocalBoundariesAsync("ADM0");

            Assert.Equal(1, summary.TotalFeatures);
            Assert.Equal(0, summary.ImportedCount);
            Assert.Equal(1, summary.FailedCount);
            var failed = Assert.Single(summary.Items);
            Assert.Equal("TRJ", failed.Iso3);
            Assert.Equal("TRJ", failed.ShapeGroup);
            Assert.Equal("Wrong Region", failed.Name);
            Assert.Equal("REGION-IN-ADM0", failed.ShapeId);
            Assert.Equal("ADM1", failed.ShapeType);
            Assert.Equal("Failed", failed.Status);
            Assert.Contains("expected ADM0", failed.Message);
            Assert.False(await db.Territories.AnyAsync(item => item.CountryIso3 == "TRJ"));
        }
        finally
        {
            TryDeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public async Task Local_adm1_and_adm2_imports_still_reject_wrong_adm_levels()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var tempRoot = CreateTempBoundaryDirectory();

        try
        {
            var adm1Path = Path.Combine(tempRoot, "local-adm1-wrong-level.geojson");
            var adm2Path = Path.Combine(tempRoot, "local-adm2-wrong-level.geojson");
            await File.WriteAllTextAsync(adm1Path, """
                {
                  "type": "FeatureCollection",
                  "features": [
                    {
                      "type": "Feature",
                      "properties": { "shapeGroup": "TAA", "shapeType": "ADM0", "shapeName": "Country In ADM1", "shapeID": "ADM1-WRONG" },
                      "geometry": {
                        "type": "Polygon",
                        "coordinates": [[[10.0,10.0],[11.0,10.0],[11.0,11.0],[10.0,11.0],[10.0,10.0]]]
                      }
                    }
                  ]
                }
                """);
            await File.WriteAllTextAsync(adm2Path, """
                {
                  "type": "FeatureCollection",
                  "features": [
                    {
                      "type": "Feature",
                      "properties": { "shapeGroup": "TAA", "shapeType": "ADM0", "shapeName": "Country In ADM2", "shapeID": "ADM2-ADM0" },
                      "geometry": {
                        "type": "Polygon",
                        "coordinates": [[[20.0,20.0],[21.0,20.0],[21.0,21.0],[20.0,21.0],[20.0,20.0]]]
                      }
                    },
                    {
                      "type": "Feature",
                      "properties": { "shapeGroup": "TAA", "shapeType": "ADM1", "shapeName": "Region In ADM2", "shapeID": "ADM2-ADM1" },
                      "geometry": {
                        "type": "Polygon",
                        "coordinates": [[[30.0,30.0],[31.0,30.0],[31.0,31.0],[30.0,31.0],[30.0,30.0]]]
                      }
                    }
                  ]
                }
                """);

            var service = CreateLocalBoundaryImportService(
                db,
                tempRoot,
                adm1Path: adm1Path,
                adm2Path: adm2Path);

            var adm1Summary = await service.ImportLocalBoundariesAsync("ADM1");
            var adm2Summary = await service.ImportLocalBoundariesAsync("ADM2");

            Assert.Equal(1, adm1Summary.TotalFeatures);
            Assert.Equal(1, adm1Summary.FailedCount);
            var adm1Failure = Assert.Single(adm1Summary.Items);
            Assert.Equal("TAA", adm1Failure.ShapeGroup);
            Assert.Equal("ADM0", adm1Failure.ShapeType);
            Assert.Contains("expected ADM1", adm1Failure.Message);

            Assert.Equal(2, adm2Summary.TotalFeatures);
            Assert.Equal(2, adm2Summary.FailedCount);
            Assert.Contains(adm2Summary.Items, item =>
                item.ShapeId == "ADM2-ADM0" &&
                item.ShapeType == "ADM0" &&
                item.Message != null &&
                item.Message.Contains("expected ADM2", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(adm2Summary.Items, item =>
                item.ShapeId == "ADM2-ADM1" &&
                item.ShapeType == "ADM1" &&
                item.Message != null &&
                item.Message.Contains("expected ADM2", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            TryDeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public async Task Boundary_reconciliation_reports_pse_missing_country_source_and_related_disputed_features_separately()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var tempRoot = CreateTempBoundaryDirectory();

        try
        {
            var pse = new Territory
            {
                Name = "Palestinian Authority",
                TerritoryKind = TerritoryKinds.Country,
                CountryCode = "PS",
                CountryIso3 = "PSE",
                CreatedAt = DateTime.UtcNow
            };
            db.Territories.Add(pse);
            await db.SaveChangesAsync();

            var adm0Path = Path.Combine(tempRoot, "pse-reconciliation.geojson");
            await File.WriteAllTextAsync(adm0Path, """
                {
                  "type": "FeatureCollection",
                  "features": [
                    {
                      "type": "Feature",
                      "properties": { "shapeGroup": "118", "shapeType": "DISP", "shapeName": "Gaza Strip", "shapeID": "GAZA-DISP" },
                      "geometry": {
                        "type": "Polygon",
                        "coordinates": [[[34.0,31.0],[35.0,31.0],[35.0,32.0],[34.0,32.0],[34.0,31.0]]]
                      }
                    },
                    {
                      "type": "Feature",
                      "properties": { "shapeGroup": "129", "shapeType": "DISP", "shapeName": "West Bank", "shapeID": "WEST-BANK-DISP" },
                      "geometry": {
                        "type": "Polygon",
                        "coordinates": [[[35.0,31.0],[36.0,31.0],[36.0,32.0],[35.0,32.0],[35.0,31.0]]]
                      }
                    }
                  ]
                }
                """);

            var service = CreateLocalBoundaryImportService(db, tempRoot, adm0Path: adm0Path);
            var report = await service.GetAdm0ReconciliationAsync();

            Assert.Equal(0, report.SourceCountryFeatureCount);
            Assert.Equal(2, report.SourceDisputedFeatureCount);
            Assert.Contains(report.MissingFullBoundary, item =>
                item.Iso3 == "PSE" &&
                item.Status == "MissingBoundary" &&
                item.BoundaryMissingReason == "No ADM0 country feature found in local source. Related disputed features may exist separately.");
            Assert.Contains(report.DbTerritoriesNotFoundInSource, item =>
                item.Iso3 == "PSE" &&
                item.BoundaryMissingReason == "No ADM0 country feature found in local source. Related disputed features may exist separately.");
            Assert.Contains(report.SourceFeaturesMissingInDb, item =>
                item.Kind == TerritoryKinds.DisputedTerritory &&
                item.Name == "Gaza Strip" &&
                item.Status == "MissingInDb");
            Assert.Contains(report.SourceFeaturesMissingInDb, item =>
                item.Kind == TerritoryKinds.DisputedTerritory &&
                item.Name == "West Bank" &&
                item.Status == "MissingInDb");
        }
        finally
        {
            TryDeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public async Task Local_adm0_endpoint_imports_configured_file_and_map_returns_available_geometry()
    {
        var tempRoot = CreateTempBoundaryDirectory();

        try
        {
            var adm0Path = Path.Combine(tempRoot, "endpoint-adm0.geojson");
            await File.WriteAllTextAsync(adm0Path, """
                {
                  "type": "FeatureCollection",
                  "features": [
                    {
                      "type": "Feature",
                      "properties": { "shapeGroup": "QZZ", "shapeName": "Endpoint Country" },
                      "geometry": {
                        "type": "Polygon",
                        "coordinates": [[[40.0,40.0],[41.0,40.0],[41.0,41.0],[40.0,41.0],[40.0,40.0]]]
                      }
                    }
                  ]
                }
                """);

            await using var app = await GeotrackApiFactory.CreateAsync(
                configurationOverrides: new Dictionary<string, string?>
                {
                    ["BoundaryImport:Adm0GeoJsonPath"] = adm0Path
                });

            using var admin = await LoginAdminAsync(app, "zika@gmail.com", "Proba123!");
            var importResponse = await admin.PostAsync(
                "/api/admin/territories/boundaries/import-local-adm0?force=false",
                content: null);
            importResponse.EnsureSuccessStatusCode();
            var summary = await importResponse.Content.ReadFromJsonAsync<BoundaryImportSummaryDto>();

            Assert.NotNull(summary);
            Assert.Equal(1, summary!.ImportedCount);
            Assert.Equal(0, summary.FailedCount);

            var map = await admin.GetFromJsonAsync<AdminTerritoryMapFeatureCollectionDto>("/api/admin/territories/map");
            var feature = Assert.Single(map!.Features, item => item.Properties.Iso3 == "QZZ");

            Assert.NotNull(feature.Geometry);
            Assert.Equal("Available", feature.Properties.Status);
            Assert.True(feature.Properties.HasBoundary);
            Assert.True(feature.Properties.CanAssign);
            Assert.False(feature.Properties.Viewport.IsEmpty);
        }
        finally
        {
            TryDeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public async Task Local_adm0_import_skips_existing_valid_boundary_before_parsing_invalid_source_geometry()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var tempRoot = CreateTempBoundaryDirectory();

        try
        {
            var existing = new Territory
            {
                Name = "Already Valid Skipland",
                TerritoryKind = TerritoryKinds.Country,
                CountryIso3 = "TSK",
                CreatedAt = DateTime.UtcNow
            };
            db.Territories.Add(existing);
            await db.SaveChangesAsync();
            SetBoundary(db, existing, Polygon(
                (10.0, 10.0),
                (10.0, 11.0),
                (11.0, 11.0),
                (11.0, 10.0)));
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();

            var adm0Path = Path.Combine(tempRoot, "skip-invalid-source.geojson");
            await File.WriteAllTextAsync(adm0Path, """
                {
                  "type": "FeatureCollection",
                  "features": [
                    {
                      "type": "Feature",
                      "properties": { "shapeGroup": "TSK", "shapeType": "ADM0", "shapeName": "Already Valid Skipland", "shapeID": "TSK-ADM0" },
                      "geometry": {
                        "type": "Polygon",
                        "coordinates": [[[10.0,10.0],[10.0,10.0],[10.0,10.0]]]
                      }
                    }
                  ]
                }
                """);

            var service = CreateLocalBoundaryImportService(db, tempRoot, adm0Path: adm0Path);
            var summary = await service.ImportLocalBoundariesAsync("ADM0", forceRefresh: false);

            Assert.Equal(1, summary.TotalFeatures);
            Assert.Equal(1, summary.ProcessedCount);
            Assert.Equal(1, summary.SkippedCount);
            Assert.Equal(0, summary.FailedCount);
            var item = Assert.Single(summary.Items);
            Assert.Equal("Skipped", item.Status);
            Assert.Equal("TSK", item.Iso3);
            Assert.Empty(db.ChangeTracker.Entries());
        }
        finally
        {
            TryDeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public async Task Local_adm0_import_batches_saves_clears_tracker_and_bounds_summary_items()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var tempRoot = CreateTempBoundaryDirectory();

        try
        {
            var adm0Path = Path.Combine(tempRoot, "bounded-summary.geojson");
            await File.WriteAllTextAsync(adm0Path, """
                {
                  "type": "FeatureCollection",
                  "features": [
                    {
                      "type": "Feature",
                      "properties": { "shapeGroup": "TBA", "shapeType": "ADM0", "shapeName": "Batch Alpha", "shapeID": "TBA-ADM0" },
                      "geometry": { "type": "Polygon", "coordinates": [[[1.0,1.0],[2.0,1.0],[2.0,2.0],[1.0,2.0],[1.0,1.0]]] }
                    },
                    {
                      "type": "Feature",
                      "properties": { "shapeGroup": "TBB", "shapeType": "ADM0", "shapeName": "Batch Beta", "shapeID": "TBB-ADM0" },
                      "geometry": { "type": "Polygon", "coordinates": [[[3.0,3.0],[4.0,3.0],[4.0,4.0],[3.0,4.0],[3.0,3.0]]] }
                    },
                    {
                      "type": "Feature",
                      "properties": { "shapeGroup": "TBC", "shapeType": "ADM0", "shapeName": "Batch Gamma", "shapeID": "TBC-ADM0" },
                      "geometry": { "type": "Polygon", "coordinates": [[[5.0,5.0],[6.0,5.0],[6.0,6.0],[5.0,6.0],[5.0,5.0]]] }
                    },
                    {
                      "type": "Feature",
                      "properties": { "shapeGroup": "TBD", "shapeType": "ADM0", "shapeName": "Batch Broken", "shapeID": "TBD-ADM0" },
                      "geometry": { "type": "Polygon", "coordinates": [[[7.0,7.0],[7.0,7.0],[7.0,7.0]]] }
                    }
                  ]
                }
                """);

            var service = CreateLocalBoundaryImportService(
                db,
                tempRoot,
                adm0Path: adm0Path,
                maxSummaryItems: 2,
                saveBatchSize: 2,
                progressLogInterval: 1);
            var summary = await service.ImportLocalBoundariesAsync("ADM0");

            Assert.Equal(4, summary.TotalFeatures);
            Assert.Equal(4, summary.ProcessedCount);
            Assert.Equal(3, summary.ImportedCount);
            Assert.Equal(1, summary.FailedCount);
            Assert.True(summary.ItemsTruncated);
            Assert.Equal(2, summary.Items.Count);
            Assert.Contains(summary.Items, item => item.Status == "Failed" && item.Iso3 == "TBD");
            Assert.True(summary.DurationMs >= 0);
            Assert.Empty(db.ChangeTracker.Entries());
            Assert.Equal(3, await db.Territories.CountAsync(item =>
                item.CountryIso3 == "TBA" ||
                item.CountryIso3 == "TBB" ||
                item.CountryIso3 == "TBC"));
        }
        finally
        {
            TryDeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public async Task Production_logging_config_keeps_ef_command_sql_at_warning()
    {
        await using var app = await GeotrackApiFactory.CreateAsync(Environments.Production);
        var configuration = app.Services.GetRequiredService<Microsoft.Extensions.Configuration.IConfiguration>();

        Assert.Equal(
            "Warning",
            configuration["Logging:LogLevel:Microsoft.EntityFrameworkCore.Database.Command"]);
    }

    [Fact]
    public async Task Local_adm1_candidates_are_filtered_by_assigned_country_and_validated_inside_adm0()
    {
        var tempRoot = CreateTempBoundaryDirectory();

        try
        {
            var adm1Path = Path.Combine(tempRoot, "local-adm1.geojson");
            await File.WriteAllTextAsync(adm1Path, """
                {
                  "type": "FeatureCollection",
                  "features": [
                    {
                      "type": "Feature",
                      "properties": { "shapeName": "Inside Region", "shapeID": "INSIDE-1", "shapeGroup": "TAA", "shapeType": "ADM1" },
                      "geometry": {
                        "type": "Polygon",
                        "coordinates": [[[0.2,0.2],[0.8,0.2],[0.8,0.8],[0.2,0.8],[0.2,0.2]]]
                      }
                    },
                    {
                      "type": "Feature",
                      "properties": { "shapeName": "Border Mismatch Region", "shapeID": "BORDER-MISMATCH-1", "shapeGroup": "TAA", "shapeType": "ADM1" },
                      "geometry": {
                        "type": "Polygon",
                        "coordinates": [[[-0.1,0.2],[0.6,0.2],[0.6,0.8],[-0.1,0.8],[-0.1,0.2]]]
                      }
                    },
                    {
                      "type": "Feature",
                      "properties": { "shapeName": "Outside Region", "shapeID": "OUTSIDE-1", "shapeGroup": "TAA", "shapeType": "ADM1" },
                      "geometry": {
                        "type": "Polygon",
                        "coordinates": [[[5.0,5.0],[6.0,5.0],[6.0,6.0],[5.0,6.0],[5.0,5.0]]]
                      }
                    },
                    {
                      "type": "Feature",
                      "properties": { "shapeName": "Other Country", "shapeID": "OTHER-1", "shapeGroup": "TBB", "shapeType": "ADM1" },
                      "geometry": {
                        "type": "Polygon",
                        "coordinates": [[[0.2,0.2],[0.8,0.2],[0.8,0.8],[0.2,0.8],[0.2,0.2]]]
                      }
                    }
                  ]
                }
                """);

            await using var app = await GeotrackApiFactory.CreateAsync(
                configurationOverrides: new Dictionary<string, string?>
                {
                    ["BoundaryImport:Adm1GeoJsonPath"] = adm1Path
            });

            var territoryId = await CreateAssignedTerritoryForCandidateTestAsync(app, "TAA");
            await using (var scope = app.Services.CreateAsyncScope())
            {
                var import = scope.ServiceProvider.GetRequiredService<ILocalBoundaryImportService>();
                var importSummary = await import.ImportLocalBoundariesAsync("ADM1");
                Assert.Equal(4, importSummary.ImportedCount);
                Assert.Equal(0, importSummary.FailedCount);
            }

            using var manager = await LoginAdminAsync(app, "mika@gmail.com", "Proba123!");
            var response = await manager.GetFromJsonAsync<BoundaryRegionCandidateResponseDto>(
                $"/api/admin/territory/regions/boundary-candidates?territoryId={territoryId}&level=ADM1&take=10");

            Assert.NotNull(response);
            Assert.Equal("ADM1", response!.AdmLevel);
            Assert.Equal("TAA", response.Iso3);
            Assert.Equal(3, response.Items.Count);
            var candidate = Assert.Single(response.Items, item => item.ShapeId == "INSIDE-1");
            Assert.Equal("INSIDE-1", candidate.ShapeId);
            Assert.Equal("Inside Region", candidate.Name);
            Assert.True(candidate.IsInsideAssignedTerritory);
            Assert.NotNull(candidate.GeometryGeoJson);
            Assert.Equal("Polygon", candidate.GeometryGeoJson!.Type);

            var borderMismatch = Assert.Single(response.Items, item => item.ShapeId == "BORDER-MISMATCH-1");
            Assert.Equal("Available", borderMismatch.Status);
            Assert.True(borderMismatch.CanSelect);
            Assert.True(borderMismatch.IsInsideAssignedTerritory);

            var outside = Assert.Single(response.Items, item => item.ShapeId == "OUTSIDE-1");
            Assert.Equal("InvalidOutsideTerritory", outside.Status);
            Assert.False(outside.CanSelect);
        }
        finally
        {
            TryDeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public async Task Territory_map_split_endpoints_return_cached_boundaries_and_lightweight_status()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        var fixture = await CreateFixtureAsync(app);

        using var admin = await LoginAdminAsync(app, "zika@gmail.com", "Proba123!");
        var boundaryResponse = await admin.GetAsync("/api/admin/territories/map/boundaries");
        boundaryResponse.EnsureSuccessStatusCode();
        Assert.True(boundaryResponse.Headers.ETag is not null);
        Assert.Equal(TimeSpan.FromSeconds(86400), boundaryResponse.Headers.CacheControl?.MaxAge);

        var boundaryJson = await boundaryResponse.Content.ReadAsStringAsync();
        Assert.DoesNotContain("\"assignedManager\"", boundaryJson);
        Assert.DoesNotContain("\"status\"", boundaryJson);
        Assert.DoesNotContain("\"boundarySourceUrl\"", boundaryJson);
        Assert.DoesNotContain("\"roles\"", boundaryJson);

        var boundaries = JsonSerializer.Deserialize<AdminTerritoryMapBoundaryFeatureCollectionDto>(
            boundaryJson,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.NotNull(boundaries);
        Assert.Equal("FeatureCollection", boundaries!.Type);
        Assert.Contains("geoBoundaries", boundaries.Attribution, StringComparison.OrdinalIgnoreCase);
        var feature = Assert.Single(boundaries.Features, item => item.Properties.TerritoryId == fixture.TerritoryId);
        Assert.NotNull(feature.Geometry);
        Assert.Equal("Polygon", feature.Geometry!.Type);
        Assert.Equal(fixture.CountryCode, feature.Properties.CountryCode);
        Assert.True(feature.Properties.HasDisplayBoundary);
        Assert.False(feature.Properties.Viewport.IsEmpty);

        var statusResponse = await admin.GetAsync("/api/admin/territories/map/status");
        statusResponse.EnsureSuccessStatusCode();
        Assert.True(statusResponse.Headers.ETag is not null);
        Assert.True(statusResponse.Headers.CacheControl?.NoStore);

        var statusJson = await statusResponse.Content.ReadAsStringAsync();
        Assert.DoesNotContain("\"coordinates\"", statusJson);
        Assert.DoesNotContain("\"geometry\"", statusJson);
        Assert.DoesNotContain("\"roles\"", statusJson);

        var status = JsonSerializer.Deserialize<AdminTerritoryMapStatusCollectionDto>(
            statusJson,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.NotNull(status);
        var statusItem = Assert.Single(status!.Items, item => item.TerritoryId == fixture.TerritoryId);
        Assert.Equal("Available", statusItem.Status);
        Assert.True(statusItem.HasBoundary);
        Assert.True(statusItem.HasValidationBoundary);
        Assert.True(statusItem.HasDisplayBoundary);
        Assert.True(statusItem.CanAssign);
        Assert.False(statusItem.CanEdit);
    }

    [Fact]
    public async Task Territory_map_reports_full_boundary_with_missing_display_geometry_distinctly()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        var fixture = await CreateFixtureAsync(app);

        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var territory = await db.Territories.SingleAsync(item => item.Id == fixture.TerritoryId);
            db.Entry(territory)
                .Property<Geometry?>(BoundaryDisplayGeometryPropertyName)
                .CurrentValue = null;
            await db.SaveChangesAsync();
        }

        using var admin = await LoginAdminAsync(app, "zika@gmail.com", "Proba123!");
        var map = await admin.GetFromJsonAsync<AdminTerritoryMapFeatureCollectionDto>("/api/admin/territories/map");
        var feature = Assert.Single(map!.Features, item => item.Properties.TerritoryId == fixture.TerritoryId);

        Assert.Null(feature.Geometry);
        Assert.Equal("MissingDisplayGeometry", feature.Properties.Status);
        Assert.True(feature.Properties.HasBoundary);
        Assert.True(feature.Properties.HasValidationBoundary);
        Assert.False(feature.Properties.HasDisplayBoundary);
        Assert.True(feature.Properties.CanAssign);
        Assert.Equal(
            "This country boundary is available for validation, but its display geometry must be rebuilt before it can be shown on the map.",
            feature.Properties.BoundaryMissingReason);
    }

    [Fact]
    public async Task Territory_map_reports_area_collapsed_display_geometry_as_missing_display_geometry()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        var fixture = await CreateFixtureAsync(app);

        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var territory = await db.Territories.SingleAsync(item => item.Id == fixture.TerritoryId);
            db.Entry(territory)
                .Property<Geometry?>(BoundaryDisplayGeometryPropertyName)
                .CurrentValue = Polygon(
                    (10.0, 20.0),
                    (10.0, 20.01),
                    (10.01, 20.01),
                    (10.01, 20.0));
            await db.SaveChangesAsync();
        }

        using var admin = await LoginAdminAsync(app, "zika@gmail.com", "Proba123!");
        var map = await admin.GetFromJsonAsync<AdminTerritoryMapFeatureCollectionDto>("/api/admin/territories/map");
        var feature = Assert.Single(map!.Features, item => item.Properties.TerritoryId == fixture.TerritoryId);

        Assert.Null(feature.Geometry);
        Assert.Equal("MissingDisplayGeometry", feature.Properties.Status);
        Assert.True(feature.Properties.HasBoundary);
        Assert.True(feature.Properties.HasValidationBoundary);
        Assert.False(feature.Properties.HasDisplayBoundary);
        Assert.True(feature.Properties.CanAssign);
    }

    [Fact]
    public async Task Rebuild_display_geometry_endpoint_backfills_existing_full_boundaries()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        var fixture = await CreateFixtureAsync(app);

        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var territory = await db.Territories.SingleAsync(item => item.Id == fixture.TerritoryId);
            db.Entry(territory)
                .Property<Geometry?>(BoundaryDisplayGeometryPropertyName)
                .CurrentValue = null;
            await db.SaveChangesAsync();
        }

        using var admin = await LoginAdminAsync(app, "zika@gmail.com", "Proba123!");
        var response = await admin.PostAsync(
            "/api/admin/territories/boundaries/rebuild-display-geometry",
            content: null);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<BoundaryDisplayGeometryRebuildResultDto>();

        Assert.NotNull(result);
        Assert.True(result!.ScannedCount >= 1);
        Assert.Equal(result.ScannedCount, result.TotalChecked);
        Assert.True(result.RebuiltCount >= 1);
        Assert.Contains(result.Items, item =>
            item.TerritoryId == fixture.TerritoryId &&
            item.Status == "Rebuilt");

        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var displayGeometry = await db.Territories
                .Where(item => item.Id == fixture.TerritoryId)
                .Select(item => EF.Property<Geometry?>(item, BoundaryDisplayGeometryPropertyName))
                .SingleAsync();

            Assert.NotNull(displayGeometry);
            Assert.Equal(4326, displayGeometry!.SRID);
            Assert.True(displayGeometry.IsValid);
        }

        var map = await admin.GetFromJsonAsync<AdminTerritoryMapFeatureCollectionDto>("/api/admin/territories/map");
        var feature = Assert.Single(map!.Features, item => item.Properties.TerritoryId == fixture.TerritoryId);

        Assert.NotNull(feature.Geometry);
        Assert.True(feature.Properties.HasBoundary);
        Assert.True(feature.Properties.CanAssign);
    }

    [Fact]
    public async Task Rebuild_display_geometry_endpoint_filters_by_iso3_and_restores_tkm_map_geometry()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();

        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var territory = new Territory
            {
                Name = "Turkmenistan",
                TerritoryKind = TerritoryKinds.Country,
                CountryCode = "TM",
                CountryIso3 = "TKM",
                BoundarySource = "IntegrationTest",
                BoundaryAdmLevel = "ADM0",
                CreatedAt = DateTime.UtcNow
            };
            db.Territories.Add(territory);
            await db.SaveChangesAsync();
            db.Entry(territory)
                .Property<Geometry?>(BoundaryGeometryPropertyName)
                .CurrentValue = Polygon(
                    (35.0, 52.0),
                    (35.0, 54.0),
                    (37.0, 54.0),
                    (37.0, 52.0));
            db.Entry(territory)
                .Property<Geometry?>(BoundaryDisplayGeometryPropertyName)
                .CurrentValue = null;
            await db.SaveChangesAsync();
        }

        using var admin = await LoginAdminAsync(app, "zika@gmail.com", "Proba123!");
        var before = await admin.GetFromJsonAsync<AdminTerritoryMapFeatureCollectionDto>("/api/admin/territories/map");
        var beforeFeature = Assert.Single(before!.Features, item => item.Properties.Iso3 == "TKM");
        Assert.Null(beforeFeature.Geometry);
        Assert.Equal("MissingDisplayGeometry", beforeFeature.Properties.Status);
        Assert.True(beforeFeature.Properties.HasBoundary);
        Assert.False(beforeFeature.Properties.HasDisplayBoundary);

        var response = await admin.PostAsync(
            "/api/admin/territories/boundaries/rebuild-display-geometry?onlyMissing=true&iso3=TKM&force=false",
            content: null);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<BoundaryDisplayGeometryRebuildResultDto>();

        Assert.NotNull(result);
        Assert.Equal("TKM", result!.Iso3);
        Assert.Equal(1, result.TotalChecked);
        Assert.Equal(1, result.RebuiltCount);
        var item = Assert.Single(result.Items);
        Assert.Equal("TKM", item.Iso3);
        Assert.Equal("Rebuilt", item.Status);

        var after = await admin.GetFromJsonAsync<AdminTerritoryMapFeatureCollectionDto>("/api/admin/territories/map");
        var afterFeature = Assert.Single(after!.Features, item => item.Properties.Iso3 == "TKM");
        Assert.NotNull(afterFeature.Geometry);
        Assert.Equal("Available", afterFeature.Properties.Status);
        Assert.True(afterFeature.Properties.HasBoundary);
        Assert.True(afterFeature.Properties.HasDisplayBoundary);
        Assert.True(afterFeature.Properties.CanAssign);
    }

    [Fact]
    public async Task Rebuild_display_geometry_repairs_display_copy_without_modifying_full_boundary()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();

        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var territory = new Territory
            {
                Name = "Repairable Display Geometry",
                TerritoryKind = TerritoryKinds.Country,
                CountryCode = "ZZ",
                CountryIso3 = "ZZZ",
                BoundarySource = "IntegrationTest",
                BoundaryAdmLevel = "ADM0",
                CreatedAt = DateTime.UtcNow
            };
            db.Territories.Add(territory);
            await db.SaveChangesAsync();

            var repairableFullGeometry = Polygon(
                (0.0, 0.0),
                (1.0, 1.0),
                (1.0, 0.0),
                (0.0, 1.0));
            Assert.False(repairableFullGeometry.IsValid);

            db.Entry(territory)
                .Property<Geometry?>(BoundaryGeometryPropertyName)
                .CurrentValue = repairableFullGeometry;
            db.Entry(territory)
                .Property<Geometry?>(BoundaryDisplayGeometryPropertyName)
                .CurrentValue = null;
            await db.SaveChangesAsync();
        }

        using var admin = await LoginAdminAsync(app, "zika@gmail.com", "Proba123!");
        var response = await admin.PostAsync(
            "/api/admin/territories/boundaries/rebuild-display-geometry?onlyMissing=true&iso3=ZZZ&force=false",
            content: null);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<BoundaryDisplayGeometryRebuildResultDto>();

        Assert.NotNull(result);
        Assert.Equal(1, result!.TotalChecked);
        Assert.Equal(1, result.RebuiltCount);
        var item = Assert.Single(result.Items);
        Assert.Equal("Rebuilt", item.Status);

        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var stored = await db.Territories
                .Where(territory => territory.CountryIso3 == "ZZZ")
                .Select(territory => new
                {
                    Full = EF.Property<Geometry?>(territory, BoundaryGeometryPropertyName),
                    Display = EF.Property<Geometry?>(territory, BoundaryDisplayGeometryPropertyName)
                })
                .SingleAsync();

            Assert.NotNull(stored.Full);
            Assert.False(stored.Full!.IsValid);
            Assert.NotNull(stored.Display);
            Assert.Equal(4326, stored.Display!.SRID);
            Assert.True(stored.Display.IsValid);
        }
    }

    [Fact]
    public async Task Rebuild_display_geometry_for_rus_retains_mainland_and_full_validation_boundary()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        int territoryId;
        double fullArea;
        double largestFullPolygonArea;

        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var territory = await db.Territories
                .FirstOrDefaultAsync(item => item.CountryIso3 == "RUS");
            if (territory is null)
            {
                territory = new Territory
                {
                    Name = "Russia",
                    TerritoryKind = TerritoryKinds.Country,
                    CountryCode = "RU",
                    CountryIso3 = "RUS",
                    BoundarySource = "IntegrationTest",
                    BoundaryAdmLevel = "ADM0",
                    CreatedAt = DateTime.UtcNow
                };
                db.Territories.Add(territory);
                await db.SaveChangesAsync();
            }

            var geometry = RussiaLikeAntimeridianGeometry();
            SetBoundary(db, territory, geometry);
            db.Entry(territory)
                .Property<Geometry?>(BoundaryDisplayGeometryPropertyName)
                .CurrentValue = Polygon(
                    (70.0, -179.0),
                    (70.0, -178.0),
                    (71.0, -178.0),
                    (71.0, -179.0));
            await db.SaveChangesAsync();

            territoryId = territory.Id;
            fullArea = geometry.Area;
            largestFullPolygonArea = LargestPolygonArea(geometry);
        }

        using var admin = await LoginAdminAsync(app, "zika@gmail.com", "Proba123!");
        var response = await admin.PostAsync(
            "/api/admin/territories/boundaries/rebuild-display-geometry?onlyMissing=true&iso3=RUS&force=false",
            content: null);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<BoundaryDisplayGeometryRebuildResultDto>();

        Assert.NotNull(result);
        Assert.Equal("RUS", result!.Iso3);
        Assert.Equal(1, result.TotalChecked);
        Assert.Equal(1, result.RebuiltCount);

        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var stored = await db.Territories
                .Where(territory => territory.Id == territoryId)
                .Select(territory => new
                {
                    Full = EF.Property<Geometry?>(territory, BoundaryGeometryPropertyName),
                    Display = EF.Property<Geometry?>(territory, BoundaryDisplayGeometryPropertyName)
                })
                .SingleAsync();

            Assert.NotNull(stored.Full);
            Assert.Equal("MultiPolygon", stored.Full!.GeometryType);
            Assert.Equal(3, stored.Full.NumGeometries);
            Assert.Equal(fullArea, stored.Full.Area, precision: 6);

            Assert.NotNull(stored.Display);
            Assert.Equal(4326, stored.Display!.SRID);
            Assert.True(stored.Display.IsValid);
            Assert.True(stored.Display.Area >= fullArea * 0.5d);
            Assert.True(LargestPolygonArea(stored.Display) >= largestFullPolygonArea * 0.5d);
            Assert.True(ContainsPolygonCoveringEnvelope(
                stored.Display,
                west: 27d,
                east: 180d,
                south: 41d,
                north: 77d));
        }
    }

    [Fact]
    public async Task Boundary_debug_endpoint_reports_rus_antimeridian_display_diagnostics()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();

        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var territory = await db.Territories
                .FirstOrDefaultAsync(item => item.CountryIso3 == "RUS");
            if (territory is null)
            {
                territory = new Territory
                {
                    Name = "Russia",
                    TerritoryKind = TerritoryKinds.Country,
                    CountryCode = "RU",
                    CountryIso3 = "RUS",
                    BoundarySource = "IntegrationTest",
                    BoundaryAdmLevel = "ADM0",
                    CreatedAt = DateTime.UtcNow
                };
                db.Territories.Add(territory);
                await db.SaveChangesAsync();
            }

            SetBoundary(db, territory, RussiaLikeAntimeridianGeometry());
            await db.SaveChangesAsync();
        }

        using var admin = await LoginAdminAsync(app, "zika@gmail.com", "Proba123!");
        var debug = await admin.GetFromJsonAsync<BoundaryGeometryDebugDto>(
            "/api/admin/territories/boundaries/debug/RUS");

        Assert.NotNull(debug);
        Assert.Equal("RUS", debug!.Iso3);
        Assert.Equal("MultiPolygon", debug.FullGeometryType);
        Assert.Equal(3, debug.FullGeometryNumGeometries);
        Assert.True(debug.IsFullGeometryValid);
        Assert.True(debug.CoordinateCountFull > 0);
        Assert.Equal("MultiPolygon", debug.DisplayGeometryType);
        Assert.Equal(3, debug.DisplayGeometryNumGeometries);
        Assert.True(debug.IsDisplayGeometryValid);
        Assert.True(debug.CoordinateCountDisplay > 0);
        Assert.NotNull(debug.FullGeometryEnvelope);
        Assert.True(debug.FullGeometryEnvelope!.West <= -180d);
        Assert.True(debug.FullGeometryEnvelope.East >= 180d);
        Assert.Equal(debug.FullArea, debug.FullGeometryArea);
        Assert.Equal(debug.DisplayArea, debug.DisplayGeometryArea);
        Assert.NotNull(debug.DisplayAreaRatio);
        Assert.True(debug.DisplayAreaRatio!.Value >= 0.99d);
        Assert.Equal(debug.DisplayAreaRatio, debug.FullToDisplayAreaRatio);
        Assert.NotNull(debug.LargestFullPolygonArea);
        Assert.NotNull(debug.LargestDisplayPolygonArea);
        Assert.True(debug.LargestDisplayPolygonArea!.Value >= debug.LargestFullPolygonArea!.Value * 0.99d);
        Assert.True(debug.HasAntimeridianCrossing);
        Assert.True(debug.CrossesAntimeridian);
        Assert.False(debug.Viewport.IsEmpty);
        Assert.True(debug.Viewport.CenterLongitude is > 80d and < 130d);
        Assert.NotNull(debug.Viewport.Bounds);
        Assert.True(debug.Viewport.Bounds!.East > 180d);
        Assert.True(debug.Viewport.Bounds.East - debug.Viewport.Bounds.West < 180d);
    }

    [Fact]
    public async Task Territory_map_viewport_and_coordinates_are_antimeridian_safe()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();

        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var territory = await db.Territories
                .FirstOrDefaultAsync(item => item.CountryIso3 == "RUS");
            if (territory is null)
            {
                territory = new Territory
                {
                    Name = "Russia",
                    TerritoryKind = TerritoryKinds.Country,
                    CountryCode = "RU",
                    CountryIso3 = "RUS",
                    BoundarySource = "IntegrationTest",
                    BoundaryAdmLevel = "ADM0",
                    CreatedAt = DateTime.UtcNow
                };
                db.Territories.Add(territory);
                await db.SaveChangesAsync();
            }

            var geometry = RussiaLikeAntimeridianGeometry();
            Assert.True(geometry.IsValid);

            SetBoundary(db, territory, geometry);
            await db.SaveChangesAsync();
        }

        await using (var scope = app.Services.CreateAsyncScope())
        {
            var management = scope.ServiceProvider.GetRequiredService<IAdminTerritoryManagementService>();
            var map = await management.GetTerritoryMapAsync();
            var feature = Assert.Single(map.Features, item => item.Properties.Iso3 == "RUS");
            var viewport = feature.Properties.Viewport;
            var bounds = viewport.Bounds;

            Assert.NotNull(feature.Geometry);
            Assert.NotNull(bounds);
            Assert.False(viewport.IsEmpty);
            Assert.True(viewport.CenterLongitude is > 80d and < 130d);
            Assert.True(bounds!.East > 180d);
            Assert.True(bounds.East - bounds.West < 180d);

            var coordinates = Assert.IsType<double[][][][]>(feature.Geometry!.Coordinates);
            var maxLongitude = coordinates
                .SelectMany(polygon => polygon)
                .SelectMany(ring => ring)
                .Max(coordinate => coordinate[0]);
            Assert.True(maxLongitude > 180d);
        }
    }

    [Fact]
    public async Task Territory_map_uses_display_geometry_by_default_and_keeps_full_geometry_for_detail()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        var fixture = await CreateFixtureAsync(app);

        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var territory = await db.Territories.SingleAsync(item => item.Id == fixture.TerritoryId);
            var fullGeometry = Polygon(
                (10.0, 20.0),
                (10.0, 20.25),
                (10.0, 20.5),
                (10.0, 20.75),
                (10.0, 21.0),
                (11.0, 21.0),
                (11.0, 20.0));
            var displayGeometry = Polygon(
                (10.0, 20.0),
                (10.0, 21.0),
                (11.0, 21.0),
                (11.0, 20.0));
            db.Entry(territory)
                .Property<Geometry?>(BoundaryGeometryPropertyName)
                .CurrentValue = fullGeometry;
            db.Entry(territory)
                .Property<Geometry?>(BoundaryDisplayGeometryPropertyName)
                .CurrentValue = displayGeometry;
            await db.SaveChangesAsync();
        }

        await using (var scope = app.Services.CreateAsyncScope())
        {
            var service = scope.ServiceProvider.GetRequiredService<IAdminTerritoryManagementService>();
            var map = await service.GetTerritoryMapAsync();
            var feature = Assert.Single(map.Features, item => item.Properties.TerritoryId == fixture.TerritoryId);
            var mapCoordinates = Assert.IsType<double[][][]>(feature.Geometry!.Coordinates);

            Assert.Equal(5, mapCoordinates[0].Length);

            var detail = await service.GetTerritoryDetailAsync(fixture.TerritoryId);
            var detailCoordinates = Assert.IsType<double[][][]>(detail.BoundaryGeoJson!.Coordinates);
            Assert.True(detailCoordinates[0].Length > mapCoordinates[0].Length);
        }
    }

    [Fact]
    public async Task Territory_map_endpoint_returns_assigned_status_and_edit_permission_when_boundary_exists()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        var fixture = await CreateFixtureAsync(app, assignManagerEmail: "marko.p@example.com");

        using var admin = await LoginAdminAsync(app, "zika@gmail.com", "Proba123!");
        var response = await admin.GetAsync("/api/admin/territories/map");
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("\"roles\"", json);
        var map = JsonSerializer.Deserialize<AdminTerritoryMapFeatureCollectionDto>(
            json,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.NotNull(map);
        var feature = Assert.Single(map!.Features, item => item.Properties.TerritoryId == fixture.TerritoryId);
        Assert.NotNull(feature.Geometry);
        Assert.Equal("Assigned", feature.Properties.Status);
        Assert.True(feature.Properties.HasBoundary);
        Assert.False(feature.Properties.CanAssign);
        Assert.True(feature.Properties.CanEdit);
        Assert.NotNull(feature.Properties.AssignedManager);
        Assert.False(feature.Properties.Viewport.IsEmpty);
    }

    [Fact]
    public async Task Territory_map_endpoint_marks_boundary_less_countries_as_missing_boundary()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        var fixture = await CreateFixtureAsync(app, withBoundary: false);

        using var admin = await LoginAdminAsync(app, "zika@gmail.com", "Proba123!");
        var map = await admin.GetFromJsonAsync<AdminTerritoryMapFeatureCollectionDto>("/api/admin/territories/map");

        Assert.NotNull(map);
        var feature = Assert.Single(map!.Features, item => item.Properties.TerritoryId == fixture.TerritoryId);
        Assert.Null(feature.Geometry);
        Assert.Equal("MissingBoundary", feature.Properties.Status);
        Assert.False(feature.Properties.HasBoundary);
        Assert.False(feature.Properties.CanAssign);
        Assert.Equal("This country cannot be assigned until its boundary geometry is available.", feature.Properties.BoundaryRequiredMessage);
    }

    [Fact]
    public async Task Non_admin_cannot_access_map_or_import_boundaries()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        await GrantUserRoleAsync(app, "elena@example.com", RoleNames.RegionalManager);

        using var regionalManager = await LoginAdminAsync(app, "elena@example.com", "password123");
        var mapResponse = await regionalManager.GetAsync("/api/admin/territories/map");
        var mapBoundariesResponse = await regionalManager.GetAsync("/api/admin/territories/map/boundaries");
        var mapStatusResponse = await regionalManager.GetAsync("/api/admin/territories/map/status");
        var importResponse = await regionalManager.PostAsJsonAsync(
            "/api/admin/territories/import-boundary",
            new AdminImportTerritoryBoundaryRequestDto { Iso3 = "SRB" });
        var importMissingResponse = await regionalManager.PostAsJsonAsync(
            "/api/admin/territories/import-boundaries/missing",
            new AdminImportTerritoryBoundariesRequestDto());
        var importMissingAliasResponse = await regionalManager.PostAsJsonAsync(
            "/api/admin/territories/boundaries/import-missing",
            new AdminImportTerritoryBoundariesRequestDto());
        var localImportResponse = await regionalManager.PostAsync(
            "/api/admin/territories/boundaries/import-local-adm0",
            content: null);
        var rebuildResponse = await regionalManager.PostAsync(
            "/api/admin/territories/boundaries/rebuild-display-geometry",
            content: null);
        var debugResponse = await regionalManager.GetAsync(
            "/api/admin/territories/boundaries/debug/RUS");
        var reconciliationResponse = await regionalManager.GetAsync(
            "/api/admin/territories/boundaries/reconciliation");

        Assert.Equal(HttpStatusCode.Forbidden, mapResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, mapBoundariesResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, mapStatusResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, importResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, importMissingResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, importMissingAliasResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, localImportResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, rebuildResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, debugResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, reconciliationResponse.StatusCode);
    }

    [Fact]
    public async Task Assignable_managers_endpoint_returns_real_user_roles()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        var fixture = await CreateFixtureAsync(app);
        await GrantUserRoleAsync(app, "marko.p@example.com", RoleNames.RegionalManager);

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

    private static async Task<Territory> EnsureCountryTerritoryAsync(
        AppDbContext db,
        string name,
        string iso2,
        string iso3)
    {
        var territory = await db.Territories
            .FirstOrDefaultAsync(item =>
                item.CountryIso3 == iso3 ||
                item.CountryCode == iso2 ||
                item.Name == name);

        if (territory is null)
        {
            territory = new Territory
            {
                Name = name,
                CreatedAt = DateTime.UtcNow
            };
            db.Territories.Add(territory);
        }

        territory.CountryCode = iso2;
        territory.CountryIso3 = iso3;
        territory.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return territory;
    }

    private static LocalBoundaryImportService CreateLocalBoundaryImportService(
        AppDbContext db,
        string contentRoot,
        string? adm0Path = null,
        string? adm1Path = null,
        string? adm2Path = null,
        int maxSummaryItems = 100,
        int saveBatchSize = 10,
        int progressLogInterval = 25) =>
        new(
            db,
            Options.Create(new BoundaryImportOptions
            {
                Adm0GeoJsonPath = adm0Path ?? Path.Combine(contentRoot, "adm0.geojson"),
                Adm1GeoJsonPath = adm1Path ?? Path.Combine(contentRoot, "adm1.geojson"),
                Adm2GeoJsonPath = adm2Path ?? Path.Combine(contentRoot, "adm2.geojson"),
                MaxSummaryItems = maxSummaryItems,
                SaveBatchSize = saveBatchSize,
                ProgressLogInterval = progressLogInterval
            }),
            new TestHostEnvironment(contentRoot),
            NullLogger<LocalBoundaryImportService>.Instance);

    private static async Task<int> CreateAssignedTerritoryForCandidateTestAsync(
        GeotrackApiFactory app,
        string iso3)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var manager = await db.Users.SingleAsync(user => user.Email == "mika@gmail.com");
        await EnsureTerritoryManagerRoleAndProfileAsync(db, manager.Id);

        var territory = new Territory
        {
            Name = $"Candidate Territory {Guid.NewGuid():N}",
            CountryIso3 = iso3,
            CreatedAt = DateTime.UtcNow,
            BoundaryShapeType = "Polygon",
            BoundarySource = "IntegrationTest"
        };
        db.Territories.Add(territory);
        await db.SaveChangesAsync();

        SetBoundary(db, territory, Polygon(
            (0.0, 0.0),
            (0.0, 2.0),
            (2.0, 2.0),
            (2.0, 0.0)));

        db.TerritoryManagerTerritories.Add(new TerritoryManagerTerritory
        {
            UserId = manager.Id,
            TerritoryId = territory.Id,
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        return territory.Id;
    }

    private static string CreateTempBoundaryDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "geotrack-boundary-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
            // Best-effort cleanup for test temp files.
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

    private static void SetBoundary(AppDbContext db, Territory territory, Geometry? geometry)
    {
        db.Entry(territory)
            .Property<Geometry?>(BoundaryGeometryPropertyName)
            .CurrentValue = geometry;
        db.Entry(territory)
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

    private static Geometry RussiaLikeAntimeridianGeometry()
    {
        var geometry = GeometryFactory.CreateMultiPolygon(new[]
        {
            Polygon(
                (41.0, 27.0),
                (41.0, 180.0),
                (77.0, 180.0),
                (77.0, 27.0)),
            Polygon(
                (64.0, -180.0),
                (64.0, -169.0),
                (69.0, -169.0),
                (69.0, -180.0)),
            Polygon(
                (54.0, 19.0),
                (54.0, 22.0),
                (55.0, 22.0),
                (55.0, 19.0))
        });
        geometry.SRID = 4326;
        return geometry;
    }

    private static double LargestPolygonArea(Geometry geometry) =>
        Polygons(geometry)
            .Select(polygon => polygon.Area)
            .DefaultIfEmpty(0d)
            .Max();

    private static bool ContainsPolygonCoveringEnvelope(
        Geometry geometry,
        double west,
        double east,
        double south,
        double north) =>
        Polygons(geometry)
            .Any(polygon =>
                polygon.EnvelopeInternal.MinX <= west &&
                polygon.EnvelopeInternal.MaxX >= east &&
                polygon.EnvelopeInternal.MinY <= south &&
                polygon.EnvelopeInternal.MaxY >= north);

    private static IEnumerable<Polygon> Polygons(Geometry geometry)
    {
        if (geometry is Polygon polygon)
        {
            yield return polygon;
            yield break;
        }

        if (geometry is GeometryCollection collection)
        {
            for (var index = 0; index < collection.NumGeometries; index++)
            {
                foreach (var item in Polygons(collection.GetGeometryN(index)))
                    yield return item;
            }
        }
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

    private static HttpResponseMessage JsonResponse(string json) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _send;

        public StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> send)
        {
            _send = send;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(_send(request));
    }

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public TestHostEnvironment(string contentRootPath)
        {
            ContentRootPath = contentRootPath;
        }

        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = "Geotrack.IntegrationTests";
        public string ContentRootPath { get; set; }
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private sealed record AdminTerritoryFixture(
        int TerritoryId,
        string TerritoryName,
        string CountryCode,
        int? AssignedManagerUserId);
}
