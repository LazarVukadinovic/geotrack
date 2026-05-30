using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Geotrack.Application.DTOs.Admin;
using Geotrack.Application.DTOs.Auth;
using Geotrack.Application.Interfaces.Service;
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
            item.FeatureKind == "ExistingRegion" &&
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

    [Fact]
    public async Task Import_local_adm1_stores_boundary_candidates_and_rerun_skips_valid_existing()
    {
        var tempRoot = CreateTempBoundaryDirectory();

        try
        {
            var adm1Path = await WriteAdm1CandidateFileAsync(tempRoot);
            await using var app = await GeotrackApiFactory.CreateAsync(
                configurationOverrides: new Dictionary<string, string?>
                {
                    ["BoundaryImport:Adm1GeoJsonPath"] = adm1Path
                });
            await CreateAdm1CandidateFixtureAsync(app);

            var firstRun = await ImportAdmBoundaryCandidatesAsync(app, "ADM1");
            Assert.Equal(5, firstRun.TotalFeatures);
            Assert.Equal(5, firstRun.ImportedCount);
            Assert.Equal(0, firstRun.SkippedCount);
            Assert.Equal(0, firstRun.FailedCount);

            await using (var scope = app.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var stored = await db.AdministrativeBoundaryCandidates
                    .AsNoTracking()
                    .Where(candidate => candidate.AdmLevel == "ADM1")
                    .Select(candidate => new
                    {
                        candidate.ShapeId,
                        candidate.ShapeName,
                        candidate.ShapeGroup,
                        candidate.CountryIso3,
                        candidate.CountryTerritoryId,
                        candidate.ExternalKey,
                        BoundaryGeometry = EF.Property<Geometry?>(candidate, BoundaryGeometryPropertyName),
                        BoundaryDisplayGeometry = EF.Property<Geometry?>(candidate, BoundaryDisplayGeometryPropertyName)
                    })
                    .ToListAsync();

                Assert.Equal(5, stored.Count);
                var inside = Assert.Single(stored, item => item.ShapeId == "INSIDE-1");
                Assert.Equal("Inside Region", inside.ShapeName);
                Assert.Equal("TAA", inside.ShapeGroup);
                Assert.Equal("TAA", inside.CountryIso3);
                Assert.Equal("geoBoundaries:ADM1:TAA:INSIDE-1", inside.ExternalKey);
                Assert.NotNull(inside.CountryTerritoryId);
                Assert.NotNull(inside.BoundaryGeometry);
                Assert.NotNull(inside.BoundaryDisplayGeometry);
            }

            var secondRun = await ImportAdmBoundaryCandidatesAsync(app, "ADM1");
            Assert.Equal(5, secondRun.TotalFeatures);
            Assert.Equal(0, secondRun.ImportedCount);
            Assert.Equal(0, secondRun.UpdatedCount);
            Assert.Equal(5, secondRun.SkippedCount);
            Assert.Equal(0, secondRun.FailedCount);
        }
        finally
        {
            TryDeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public async Task Adm1_import_country_and_take_filters_resume_past_existing_candidates()
    {
        var tempRoot = CreateTempBoundaryDirectory();

        try
        {
            var adm1Path = await WriteAdm1CandidateFileAsync(tempRoot);
            await using var app = await GeotrackApiFactory.CreateAsync(
                configurationOverrides: new Dictionary<string, string?>
                {
                    ["BoundaryImport:Adm1GeoJsonPath"] = adm1Path
                });
            await CreateAdm1CandidateFixtureAsync(app);

            var firstRun = await ImportAdmBoundaryCandidatesAsync(
                app,
                "ADM1",
                countryIso3: "TAA",
                take: 2);

            Assert.Equal("TAA", firstRun.CountryIso3);
            Assert.Equal(2, firstRun.TakeLimit);
            Assert.Equal(3, firstRun.TotalFeatures);
            Assert.Equal(3, firstRun.MatchedFilterCount);
            Assert.Equal(0, firstRun.FilteredOutCount);
            Assert.Equal(2, firstRun.ProcessedCount);
            Assert.Equal(2, firstRun.ImportedCount);
            Assert.Equal(2, firstRun.ImportedCandidatesCount);
            Assert.True(firstRun.HasMore);
            Assert.False(firstRun.CompletedSourceScan);

            await using (var scope = app.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                Assert.Equal(2, await db.AdministrativeBoundaryCandidates
                    .AsNoTracking()
                    .CountAsync(candidate => candidate.AdmLevel == "ADM1"));
            }

            var secondRun = await ImportAdmBoundaryCandidatesAsync(
                app,
                "ADM1",
                countryIso3: "TAA",
                take: 2);

            Assert.Equal(5, secondRun.TotalFeatures);
            Assert.Equal(4, secondRun.MatchedFilterCount);
            Assert.Equal(1, secondRun.FilteredOutCount);
            Assert.Equal(4, secondRun.ProcessedCount);
            Assert.Equal(2, secondRun.ImportedCount);
            Assert.Equal(2, secondRun.SkippedCount);
            Assert.Equal(2, secondRun.ImportedCandidatesCount);
            Assert.Equal(2, secondRun.SkippedCandidatesCount);
            Assert.False(secondRun.HasMore);
            Assert.True(secondRun.CompletedSourceScan);

            await using (var scope = app.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                Assert.Equal(4, await db.AdministrativeBoundaryCandidates
                    .AsNoTracking()
                    .CountAsync(candidate => candidate.AdmLevel == "ADM1" && candidate.CountryIso3 == "TAA"));
                Assert.Equal(0, await db.AdministrativeBoundaryCandidates
                    .AsNoTracking()
                    .CountAsync(candidate => candidate.AdmLevel == "ADM1" && candidate.CountryIso3 == "TBB"));
            }
        }
        finally
        {
            TryDeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public async Task Import_local_adm1_endpoint_does_not_backfill_regions_automatically()
    {
        var tempRoot = CreateTempBoundaryDirectory();

        try
        {
            var adm1Path = await WriteAdm1CandidateFileAsync(tempRoot);
            await using var app = await GeotrackApiFactory.CreateAsync(
                configurationOverrides: new Dictionary<string, string?>
                {
                    ["BoundaryImport:Adm1GeoJsonPath"] = adm1Path
                });
            var fixture = await CreateAdm1CandidateFixtureAsync(app, createExistingCandidateRegion: true);
            Assert.NotNull(fixture.ExistingRegionId);

            using var admin = await LoginAdminAsync(app, "zika@gmail.com", "Proba123!");
            var response = await admin.PostAsync(
                "/api/admin/territories/boundaries/import-local-adm1?force=false",
                content: null);
            response.EnsureSuccessStatusCode();

            var summary = await response.Content.ReadFromJsonAsync<BoundaryImportSummaryDto>();
            Assert.NotNull(summary);
            Assert.Equal(5, summary!.ImportedCandidatesCount);
            Assert.Equal(0, summary.FailedCount);

            await using var scope = app.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var existingRegion = await db.Regions
                .AsNoTracking()
                .SingleAsync(region => region.Id == fixture.ExistingRegionId.Value);
            Assert.Null(existingRegion.BoundaryCandidateId);
            Assert.Equal(5, await db.AdministrativeBoundaryCandidates
                .AsNoTracking()
                .CountAsync(candidate => candidate.AdmLevel == "ADM1"));
        }
        finally
        {
            TryDeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public async Task Adm1_candidates_are_filtered_to_assigned_country_and_mark_existing_regions()
    {
        var tempRoot = CreateTempBoundaryDirectory();

        try
        {
            var adm1Path = await WriteAdm1CandidateFileAsync(tempRoot);
            await using var app = await GeotrackApiFactory.CreateAsync(
                configurationOverrides: new Dictionary<string, string?>
                {
                    ["BoundaryImport:Adm1GeoJsonPath"] = adm1Path
                });
            var fixture = await CreateAdm1CandidateFixtureAsync(app, createExistingCandidateRegion: true);
            await ImportAdmBoundaryCandidatesAsync(app, "ADM1");

            using var anonymous = app.CreateClient();
            var anonymousResponse = await anonymous.GetAsync(
                $"/api/admin/territory/regions/boundary-candidates?territoryId={fixture.AssignedTerritoryId}&level=ADM1");
            Assert.Equal(HttpStatusCode.Unauthorized, anonymousResponse.StatusCode);

            using var manager = await LoginAdminAsync(app, "mika@gmail.com", "Proba123!");
            var forbidden = await manager.GetAsync(
                $"/api/admin/territory/regions/boundary-candidates?territoryId={fixture.UnassignedTerritoryId}&level=ADM1");
            Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);

            var response = await manager.GetFromJsonAsync<BoundaryRegionCandidateResponseDto>(
                $"/api/admin/territory/regions/boundary-candidates?territoryId={fixture.AssignedTerritoryId}&level=ADM1&take=10");

            Assert.NotNull(response);
            Assert.Equal("ADM1", response!.AdmLevel);
            Assert.Equal("TAA", response.Iso3);
            Assert.Equal(4, response.Items.Count);

            var inferredResponse = await manager.GetFromJsonAsync<BoundaryRegionCandidateResponseDto>(
                "/api/admin/territory/regions/boundary-candidates?level=ADM1&take=1");
            Assert.NotNull(inferredResponse);
            Assert.Equal("TAA", inferredResponse!.Iso3);
            Assert.Equal(4, inferredResponse.Items.Count);

            var available = Assert.Single(response.Items, item => item.ShapeId == "INSIDE-1");
            Assert.Equal("Available", available.Status);
            Assert.Equal("Adm1Candidate", available.FeatureKind);
            Assert.True(available.CanSelect);
            Assert.True(available.HasBoundary);
            Assert.True(available.HasDisplayBoundary);
            Assert.Null(available.ExistingRegionId);
            Assert.Equal("geoBoundaries:ADM1:TAA:INSIDE-1", available.BoundaryExternalKey);
            Assert.Equal(available.ExternalKey, available.BoundaryExternalKey);
            Assert.NotNull(available.GeometryGeoJson);

            var existing = Assert.Single(response.Items, item => item.ShapeId == "EXISTING-1");
            Assert.Equal("ExistingRegion", existing.Status);
            Assert.False(existing.CanSelect);
            Assert.Equal(fixture.ExistingRegionId, existing.ExistingRegionId);
            Assert.Equal("Existing Region", existing.ExistingRegionName);
            Assert.Equal("This administrative region has already been created.", existing.ValidationMessage);

            var borderMismatch = Assert.Single(response.Items, item => item.ShapeId == "BORDER-MISMATCH-1");
            Assert.Equal("Available", borderMismatch.Status);
            Assert.True(borderMismatch.CanSelect);
            Assert.True(borderMismatch.IsInsideAssignedTerritory);

            var outside = Assert.Single(response.Items, item => item.ShapeId == "OUTSIDE-1");
            Assert.Equal("InvalidOutsideTerritory", outside.Status);
            Assert.False(outside.CanSelect);
            Assert.False(outside.IsInsideAssignedTerritory);

            using var admin = await LoginAdminAsync(app, "zika@gmail.com", "Proba123!");
            var diagnostics = await admin.GetFromJsonAsync<BoundaryCandidateDiagnosticsDto>(
                "/api/admin/territory/regions/boundary-candidates/diagnostics?countryIso3=TAA&level=ADM1");
            Assert.NotNull(diagnostics);
            Assert.Equal(4, diagnostics!.TotalCandidatesForIso3);
            Assert.Equal(4, diagnostics.CandidatesWithDisplayGeometry);
            Assert.Equal(2, diagnostics.CandidatesPassingStrictCovers);
            Assert.Equal(3, diagnostics.CandidatesPassingIntersects);
            Assert.Equal(1, diagnostics.CandidatesFilteredOutBySpatialCheck);
            Assert.Equal(4, diagnostics.CandidatesReturned);
            Assert.Equal(1, diagnostics.ExistingRegionMatches);
        }
        finally
        {
            TryDeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public async Task Create_region_from_adm1_candidate_stores_boundary_metadata_and_rejects_duplicates()
    {
        var tempRoot = CreateTempBoundaryDirectory();

        try
        {
            var adm1Path = await WriteAdm1CandidateFileAsync(tempRoot);
            await using var app = await GeotrackApiFactory.CreateAsync(
                configurationOverrides: new Dictionary<string, string?>
                {
                    ["BoundaryImport:Adm1GeoJsonPath"] = adm1Path
                });
            var fixture = await CreateAdm1CandidateFixtureAsync(app);
            await ImportAdmBoundaryCandidatesAsync(app, "ADM1");
            using var manager = await LoginAdminAsync(app, "mika@gmail.com", "Proba123!");

            var request = new TerritoryRegionCreateRequestDto
            {
                TerritoryId = fixture.AssignedTerritoryId,
                AdmLevel = "ADM1",
                ShapeGroup = "TAA",
                CandidateExternalKey = "geoBoundaries:ADM1:TAA:INSIDE-1"
            };

            var response = await manager.PostAsJsonAsync("/api/admin/territory/regions", request);
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            var created = await response.Content.ReadFromJsonAsync<TerritoryRegionCreateResponseDto>();
            Assert.NotNull(created);
            Assert.Equal("Inside Region", created!.Name);
            Assert.Equal("geoBoundaries", created.BoundarySource);
            Assert.Equal("ADM1", created.BoundaryAdmLevel);
            Assert.Equal("TAA", created.BoundaryShapeGroup);
            Assert.Equal("INSIDE-1", created.BoundaryShapeId);
            Assert.Equal("geoBoundaries:ADM1:TAA:INSIDE-1", created.BoundaryExternalKey);
            Assert.Equal("gbOpen", created.BoundaryReleaseType);
            Assert.NotNull(created.BoundaryCandidateId);
            Assert.NotNull(created.BoundaryGeometryGeoJson);

            await using (var scope = app.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var stored = await db.Regions.SingleAsync(region => region.Id == created.RegionId);
                Assert.Equal("geoBoundaries", stored.BoundarySource);
                Assert.Equal("ADM1", stored.BoundaryAdmLevel);
                Assert.Equal("TAA", stored.BoundaryShapeGroup);
                Assert.Equal("INSIDE-1", stored.BoundaryShapeId);
                Assert.Equal("geoBoundaries:ADM1:TAA:INSIDE-1", stored.BoundaryExternalKey);
                Assert.Equal("gbOpen", stored.BoundaryReleaseType);
                Assert.Equal("CC BY 4.0", stored.BoundaryLicense);
                Assert.NotNull(stored.BoundaryImportedAt);
                Assert.NotNull(stored.BoundaryCandidateId);
                Assert.NotNull(db.Entry(stored).Property<Geometry?>(BoundaryGeometryPropertyName).CurrentValue);
            }

            var duplicate = await manager.PostAsJsonAsync("/api/admin/territory/regions", request);
            Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);

            var borderMismatch = await manager.PostAsJsonAsync(
                "/api/admin/territory/regions",
                new TerritoryRegionCreateRequestDto
                {
                    TerritoryId = fixture.AssignedTerritoryId,
                    Name = "Border Mismatch Region",
                    AdmLevel = "ADM1",
                    ShapeGroup = "TAA",
                    ShapeId = "BORDER-MISMATCH-1"
                });
            Assert.Equal(HttpStatusCode.Created, borderMismatch.StatusCode);

            var outside = await manager.PostAsJsonAsync(
                "/api/admin/territory/regions",
                new TerritoryRegionCreateRequestDto
                {
                    TerritoryId = fixture.AssignedTerritoryId,
                    AdmLevel = "ADM1",
                    ShapeGroup = "TAA",
                    ShapeId = "OUTSIDE-1"
                });
            Assert.Equal(HttpStatusCode.BadRequest, outside.StatusCode);

            var otherCountry = await manager.PostAsJsonAsync(
                "/api/admin/territory/regions",
                new TerritoryRegionCreateRequestDto
                {
                    TerritoryId = fixture.AssignedTerritoryId,
                    AdmLevel = "ADM1",
                    ShapeGroup = "TBB",
                    ShapeId = "OTHER-1"
                });
            Assert.Equal(HttpStatusCode.BadRequest, otherCountry.StatusCode);

            var unassigned = await manager.PostAsJsonAsync(
                "/api/admin/territory/regions",
                new TerritoryRegionCreateRequestDto
                {
                    TerritoryId = fixture.UnassignedTerritoryId,
                    AdmLevel = "ADM1",
                    ShapeGroup = "TAA",
                    ShapeId = "INSIDE-1"
                });
            Assert.Equal(HttpStatusCode.Forbidden, unassigned.StatusCode);
        }
        finally
        {
            TryDeleteDirectory(tempRoot);
        }
    }

    private const string BoundaryGeometryPropertyName = "BoundaryGeometry";
    private const string BoundaryDisplayGeometryPropertyName = "BoundaryDisplayGeometry";

    private static string CreateTempBoundaryDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "geotrack-adm1-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static async Task<string> WriteAdm1CandidateFileAsync(string tempRoot)
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
                  "properties": { "shapeName": "Existing Region", "shapeID": "EXISTING-1", "shapeGroup": "TAA", "shapeType": "ADM1" },
                  "geometry": {
                    "type": "Polygon",
                    "coordinates": [[[1.0,1.0],[1.6,1.0],[1.6,1.6],[1.0,1.6],[1.0,1.0]]]
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
        return adm1Path;
    }

    private static async Task<BoundaryImportSummaryDto> ImportAdmBoundaryCandidatesAsync(
        GeotrackApiFactory app,
        string admLevel,
        string? countryIso3 = null,
        int? take = null)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var import = scope.ServiceProvider.GetRequiredService<ILocalBoundaryImportService>();
        return await import.ImportLocalBoundariesAsync(
            admLevel,
            countryIso3: countryIso3,
            take: take);
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
            // Temporary test directories are best-effort cleanup.
        }
    }

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

    private static async Task<Adm1CandidateFixture> CreateAdm1CandidateFixtureAsync(
        GeotrackApiFactory app,
        bool createExistingCandidateRegion = false)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var now = DateTime.UtcNow;
        var manager = await db.Users.SingleAsync(user => user.Email == "mika@gmail.com");

        var assignedTerritory = new Territory
        {
            Name = $"ADM1 Candidate Assigned {Guid.NewGuid():N}",
            TerritoryKind = TerritoryKinds.Country,
            CountryIso3 = "TAA",
            CreatedAt = now,
            BoundaryShapeType = "Polygon",
            BoundarySource = "geoBoundaries",
            BoundaryAdmLevel = "ADM0"
        };
        var unassignedTerritory = new Territory
        {
            Name = $"ADM1 Candidate Unassigned {Guid.NewGuid():N}",
            TerritoryKind = TerritoryKinds.Country,
            CountryIso3 = "TBB",
            CreatedAt = now,
            BoundaryShapeType = "Polygon",
            BoundarySource = "geoBoundaries",
            BoundaryAdmLevel = "ADM0"
        };

        db.Territories.AddRange(assignedTerritory, unassignedTerritory);
        await db.SaveChangesAsync();

        SetBoundary(db, assignedTerritory, Polygon(
            (0.0, 0.0),
            (0.0, 2.0),
            (2.0, 2.0),
            (2.0, 0.0)));

        SetBoundary(db, unassignedTerritory, Polygon(
            (0.0, 0.0),
            (0.0, 2.0),
            (2.0, 2.0),
            (2.0, 0.0)));

        db.TerritoryManagerTerritories.Add(new TerritoryManagerTerritory
        {
            UserId = manager.Id,
            TerritoryId = assignedTerritory.Id,
            CreatedAt = now
        });

        Region? existingRegion = null;
        if (createExistingCandidateRegion)
        {
            existingRegion = new Region
            {
                Name = "Existing Region",
                TerritoryId = assignedTerritory.Id,
                CreatedAt = now,
                BoundaryShapeType = "Polygon",
                BoundarySource = "geoBoundaries",
                BoundaryAdmLevel = "ADM1",
                BoundaryShapeGroup = "TAA",
                BoundaryShapeId = "EXISTING-1",
                BoundaryExternalKey = "geoBoundaries:ADM1:TAA:EXISTING-1",
                BoundaryReleaseType = "gbOpen",
                BoundaryLicense = "CC BY 4.0",
                BoundaryImportedAt = now,
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
                    (1.0, 1.0),
                    (1.0, 1.6),
                    (1.6, 1.6),
                    (1.6, 1.0));
            await db.SaveChangesAsync();
        }

        return new Adm1CandidateFixture(
            assignedTerritory.Id,
            unassignedTerritory.Id,
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

    private sealed record Adm1CandidateFixture(
        int AssignedTerritoryId,
        int UnassignedTerritoryId,
        int? ExistingRegionId);
}
