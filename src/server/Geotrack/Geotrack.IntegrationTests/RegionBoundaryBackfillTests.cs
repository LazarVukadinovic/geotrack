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
using Geotrack.Server;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Coordinate = NetTopologySuite.Geometries.Coordinate;
using Geometry = NetTopologySuite.Geometries.Geometry;
using GeometryFactory = NetTopologySuite.Geometries.GeometryFactory;
using Polygon = NetTopologySuite.Geometries.Polygon;
using PrecisionModel = NetTopologySuite.Geometries.PrecisionModel;

namespace Geotrack.IntegrationTests;

public sealed class RegionBoundaryBackfillTests
{
    private const string BoundaryGeometryPropertyName = "BoundaryGeometry";
    private const string BoundaryDisplayGeometryPropertyName = "BoundaryDisplayGeometry";
    private static readonly GeometryFactory GeometryFactory = new(new PrecisionModel(), 4326);

    [Fact]
    public async Task Fresh_database_migration_creates_regions_boundary_display_geometry_column()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var regionEntityType = db.Model.FindEntityType(typeof(Region));
        Assert.NotNull(regionEntityType?.FindProperty(BoundaryDisplayGeometryPropertyName));
        Assert.True(await RegionsBoundaryDisplayGeometryColumnExistsAsync(db));
    }

    [Fact]
    public async Task Backfill_dry_run_reports_exact_match_without_updating_region()
    {
        var tempRoot = CreateTempBoundaryDirectory();

        try
        {
            var adm1Path = await WriteBackfillCandidateFileAsync(tempRoot);
            await using var app = await GeotrackApiFactory.CreateAsync(
                configurationOverrides: new Dictionary<string, string?>
                {
                    ["BoundaryImport:Adm1GeoJsonPath"] = adm1Path
                });
            var fixture = await CreateBackfillFixtureAsync(app, "Inside Region");
            await ImportAdmBoundaryCandidatesAsync(app);

            using var admin = await LoginAdminAsync(app, "zika@gmail.com", "Proba123!");
            var response = await admin.PostAsJsonAsync(
                "/api/admin/regions/backfill-boundary-candidates?countryIso3=TAA&dryRun=true",
                new { });

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var summary = await response.Content.ReadFromJsonAsync<RegionBoundaryBackfillSummaryDto>();
            Assert.NotNull(summary);
            Assert.Equal(1, summary!.TotalRegionsChecked);
            Assert.Equal(1, summary.MatchedCount);
            Assert.Equal(0, summary.UpdatedCount);
            var item = Assert.Single(summary.Items);
            Assert.Equal("Inside Region", item.RegionName);
            Assert.Equal("Matched", item.Status);
            Assert.Equal("Inside Region", item.MatchedCandidateName);

            await using var scope = app.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var stored = await db.Regions.SingleAsync(region => region.Id == fixture.RegionId);
            Assert.Null(stored.BoundaryCandidateId);
            Assert.Null(stored.BoundaryExternalKey);
            Assert.Null(db.Entry(stored).Property<Geometry?>(BoundaryGeometryPropertyName).CurrentValue);
        }
        finally
        {
            TryDeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public async Task Backfill_updates_exact_match_preserves_content_and_blocks_duplicate_candidate_creation()
    {
        var tempRoot = CreateTempBoundaryDirectory();

        try
        {
            var adm1Path = await WriteBackfillCandidateFileAsync(tempRoot);
            await using var app = await GeotrackApiFactory.CreateAsync(
                configurationOverrides: new Dictionary<string, string?>
                {
                    ["BoundaryImport:Adm1GeoJsonPath"] = adm1Path
                });
            var fixture = await CreateBackfillFixtureAsync(app, "Inside Region", includeDestination: true);
            await ImportAdmBoundaryCandidatesAsync(app);

            using var admin = await LoginAdminAsync(app, "zika@gmail.com", "Proba123!");
            var response = await admin.PostAsJsonAsync(
                "/api/admin/regions/backfill-boundary-candidates?countryIso3=TAA&dryRun=false",
                new { });

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var summary = await response.Content.ReadFromJsonAsync<RegionBoundaryBackfillSummaryDto>();
            Assert.NotNull(summary);
            Assert.Equal(1, summary!.UpdatedCount);
            var item = Assert.Single(summary.Items);
            Assert.Equal("Updated", item.Status);

            await using (var scope = app.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var stored = await db.Regions.SingleAsync(region => region.Id == fixture.RegionId);
                Assert.Equal(fixture.RegionId, stored.Id);
                Assert.Equal("Inside Region", stored.Name);
                Assert.Equal("geoBoundaries", stored.BoundarySource);
                Assert.Equal("ADM1", stored.BoundaryAdmLevel);
                Assert.Equal("TAA", stored.BoundaryShapeGroup);
                Assert.Equal("INSIDE-1", stored.BoundaryShapeId);
                Assert.Equal("geoBoundaries:ADM1:TAA:INSIDE-1", stored.BoundaryExternalKey);
                Assert.Equal("gbOpen", stored.BoundaryReleaseType);
                Assert.NotNull(stored.BoundaryCandidateId);
                Assert.NotNull(db.Entry(stored).Property<Geometry?>(BoundaryGeometryPropertyName).CurrentValue);
                Assert.NotNull(db.Entry(stored).Property<Geometry?>(BoundaryDisplayGeometryPropertyName).CurrentValue);
                Assert.Equal(1, await db.Regions.CountAsync(region => region.TerritoryId == fixture.TerritoryId));
                Assert.Equal(
                    fixture.RegionId,
                    await db.Destinations
                        .Where(destination => destination.Id == fixture.DestinationId!.Value)
                        .Select(destination => destination.RegionId)
                        .SingleAsync());
            }

            using var manager = await LoginAdminAsync(app, "mika@gmail.com", "Proba123!");
            var candidates = await manager.GetFromJsonAsync<BoundaryRegionCandidateResponseDto>(
                $"/api/admin/territory/regions/boundary-candidates?territoryId={fixture.TerritoryId}&level=ADM1&take=10");
            Assert.NotNull(candidates);
            var existing = Assert.Single(candidates!.Items, candidate => candidate.ShapeId == "INSIDE-1");
            Assert.Equal("ExistingRegion", existing.Status);
            Assert.False(existing.CanSelect);
            Assert.Equal(fixture.RegionId, existing.ExistingRegionId);
            Assert.Equal("Inside Region", existing.ExistingRegionName);

            var duplicate = await manager.PostAsJsonAsync(
                "/api/admin/territory/regions",
                new TerritoryRegionCreateRequestDto
                {
                    TerritoryId = fixture.TerritoryId,
                    AdmLevel = "ADM1",
                    ShapeGroup = "TAA",
                    CandidateExternalKey = "geoBoundaries:ADM1:TAA:INSIDE-1"
                });
            Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        }
        finally
        {
            TryDeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public async Task Backfill_links_legacy_dalmatia_seed_region_to_dubrovnik_neretva_alias()
    {
        var tempRoot = CreateTempBoundaryDirectory();

        try
        {
            var adm1Path = await WriteBackfillCandidateFileAsync(tempRoot);
            await using var app = await GeotrackApiFactory.CreateAsync(
                configurationOverrides: new Dictionary<string, string?>
                {
                    ["BoundaryImport:Adm1GeoJsonPath"] = adm1Path
                });
            var fixture = await CreateBackfillFixtureAsync(app, "Dalmatia", iso3: "HRV", includeDestination: true);
            await ImportAdmBoundaryCandidatesAsync(app);

            using var admin = await LoginAdminAsync(app, "zika@gmail.com", "Proba123!");
            var response = await admin.PostAsJsonAsync(
                "/api/admin/regions/backfill-boundary-candidates?countryIso3=HRV&dryRun=false",
                new { });

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var summary = await response.Content.ReadFromJsonAsync<RegionBoundaryBackfillSummaryDto>();
            Assert.NotNull(summary);
            var item = Assert.Single(summary!.Items, existing => existing.RegionId == fixture.RegionId);
            Assert.Equal("Updated", item.Status);
            Assert.Equal("Dubrovnik-Neretva", item.MatchedCandidateName);

            await using var scope = app.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var stored = await db.Regions.SingleAsync(region => region.Id == fixture.RegionId);
            Assert.Equal("Dalmatia", stored.Name);
            Assert.Equal("geoBoundaries", stored.BoundarySource);
            Assert.Equal("ADM1", stored.BoundaryAdmLevel);
            Assert.Equal("HRV", stored.BoundaryShapeGroup);
            Assert.Equal("DUBROVNIK-NERETVA-1", stored.BoundaryShapeId);
            Assert.NotNull(stored.BoundaryCandidateId);
            Assert.NotNull(db.Entry(stored).Property<Geometry?>(BoundaryGeometryPropertyName).CurrentValue);
            Assert.NotNull(db.Entry(stored).Property<Geometry?>(BoundaryDisplayGeometryPropertyName).CurrentValue);
            Assert.Equal(
                fixture.RegionId,
                await db.Destinations
                    .Where(destination => destination.Id == fixture.DestinationId!.Value)
                    .Select(destination => destination.RegionId)
                    .SingleAsync());
        }
        finally
        {
            TryDeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public async Task Seed_relink_after_adm1_import_links_seeded_dubrovnik_neretva_region()
    {
        var tempRoot = CreateTempBoundaryDirectory();

        try
        {
            var adm1Path = await WriteBackfillCandidateFileAsync(tempRoot);
            await using var app = await GeotrackApiFactory.CreateAsync(
                configurationOverrides: new Dictionary<string, string?>
                {
                    ["BoundaryImport:Adm1GeoJsonPath"] = adm1Path
                });

            await ImportAdmBoundaryCandidatesAsync(app);

            await using var scope = app.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await SeedData.InitializeAsync(db);

            var seedRegion = await db.Regions.SingleAsync(region => region.Name == "Dubrovnik-Neretva");
            var candidate = await db.AdministrativeBoundaryCandidates.SingleAsync(candidate =>
                candidate.CountryIso3 == "HRV" &&
                candidate.ShapeName == "Dubrovnik-Neretva");

            Assert.False(await db.Regions.AnyAsync(region => region.Name == "Dalmatia"));
            Assert.Equal(candidate.Id, seedRegion.BoundaryCandidateId);
            Assert.Equal(candidate.ExternalKey, seedRegion.BoundaryExternalKey);
            Assert.Equal(candidate.ShapeGroup, seedRegion.BoundaryShapeGroup);
            Assert.Equal(candidate.ShapeId, seedRegion.BoundaryShapeId);
            Assert.Equal("ADM1", seedRegion.BoundaryAdmLevel);
            Assert.Equal("geoBoundaries", seedRegion.BoundarySource);
            Assert.Equal("gbOpen", seedRegion.BoundaryReleaseType);
            Assert.Equal("CC BY 4.0", seedRegion.BoundaryLicense);
            Assert.NotNull(db.Entry(seedRegion).Property<Geometry?>(BoundaryGeometryPropertyName).CurrentValue);
            Assert.NotNull(db.Entry(seedRegion).Property<Geometry?>(BoundaryDisplayGeometryPropertyName).CurrentValue);
            Assert.NotNull(db.Entry(candidate).Property<Geometry?>(BoundaryDisplayGeometryPropertyName).CurrentValue);
            Assert.True(await db.Destinations.AnyAsync(destination =>
                destination.Name == "Dubrovnik" &&
                destination.RegionId == seedRegion.Id));
        }
        finally
        {
            TryDeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public async Task Fresh_seed_with_boundary_import_creates_linked_dubrovnik_neretva_and_blocks_duplicate_candidate()
    {
        var tempRoot = CreateTempBoundaryDirectory();

        try
        {
            var adm0Path = await WriteBackfillAdm0FileAsync(tempRoot);
            var adm1Path = await WriteBackfillCandidateFileAsync(tempRoot);
            await using var app = await GeotrackApiFactory.CreateAsync(
                configurationOverrides: new Dictionary<string, string?>
                {
                    ["BoundaryImport:Adm0GeoJsonPath"] = adm0Path,
                    ["BoundaryImport:Adm1GeoJsonPath"] = adm1Path,
                    ["Seed:ImportLocalBoundaries:Enabled"] = "true",
                    ["Seed:ImportLocalBoundaries:Adm0"] = "true",
                    ["Seed:ImportLocalBoundaries:Adm1"] = "true"
                });

            int territoryId;
            int regionId;
            int candidateId;

            await using (var scope = app.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var region = await db.Regions.SingleAsync(item => item.Name == "Dubrovnik-Neretva");
                var candidate = await db.AdministrativeBoundaryCandidates.SingleAsync(item =>
                    item.CountryIso3 == "HRV" &&
                    item.ShapeName == "Dubrovnik-Neretva");

                Assert.True(await RegionsBoundaryDisplayGeometryColumnExistsAsync(db));
                territoryId = region.TerritoryId;
                regionId = region.Id;
                candidateId = candidate.Id;

                Assert.False(await db.Regions.AnyAsync(item => item.Name == "Dalmatia"));
                Assert.Equal(candidate.Id, region.BoundaryCandidateId);
                Assert.Equal("HRV", region.BoundaryShapeGroup);
                Assert.Equal("DUBROVNIK-NERETVA-1", region.BoundaryShapeId);
                Assert.Equal("geoBoundaries:ADM1:HRV:DUBROVNIK-NERETVA-1", region.BoundaryExternalKey);
                Assert.Equal("ADM1", region.BoundaryAdmLevel);
                Assert.Equal("geoBoundaries", region.BoundarySource);
                Assert.Equal("gbOpen", region.BoundaryReleaseType);
                Assert.NotNull(db.Entry(region).Property<Geometry?>(BoundaryGeometryPropertyName).CurrentValue);
                Assert.NotNull(db.Entry(candidate).Property<Geometry?>(BoundaryGeometryPropertyName).CurrentValue);
                Assert.NotNull(db.Entry(region).Property<Geometry?>(BoundaryDisplayGeometryPropertyName).CurrentValue);
                Assert.NotNull(db.Entry(candidate).Property<Geometry?>(BoundaryDisplayGeometryPropertyName).CurrentValue);
                Assert.True(await db.Destinations.AnyAsync(destination =>
                    destination.Name == "Dubrovnik" &&
                    destination.RegionId == region.Id));
                Assert.True(await db.Places.AnyAsync(place =>
                    place.Location != null &&
                    place.Location.Destination.RegionId == region.Id));
                Assert.True(await db.Events.AnyAsync(evt =>
                    evt.Destination.RegionId == region.Id));
                Assert.True(await db.RegionalManagerRegions.AnyAsync(assignment =>
                    assignment.RegionId == region.Id &&
                    assignment.User.Email == "pera@gmail.com" &&
                    assignment.IsActive));
            }

            using var manager = await LoginAdminAsync(app, "mika@gmail.com", "Proba123!");
            var candidates = await manager.GetFromJsonAsync<BoundaryRegionCandidateResponseDto>(
                $"/api/admin/territory/regions/boundary-candidates?territoryId={territoryId}&level=ADM1&take=10");

            Assert.NotNull(candidates);
            var existing = Assert.Single(candidates!.Items, item => item.ShapeName == "Dubrovnik-Neretva");
            Assert.Equal(candidateId, existing.BoundaryCandidateId);
            Assert.Equal("ExistingRegion", existing.Status);
            Assert.False(existing.CanSelect);
            Assert.Equal(regionId, existing.ExistingRegionId);
            Assert.Equal("Dubrovnik-Neretva", existing.ExistingRegionName);

            var duplicate = await manager.PostAsJsonAsync(
                "/api/admin/territory/regions/from-boundary-candidate",
                new TerritoryRegionCreateRequestDto
                {
                    TerritoryId = territoryId,
                    AdmLevel = "ADM1",
                    BoundaryCandidateId = candidateId.ToString()
                });

            Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
            var error = await duplicate.Content.ReadFromJsonAsync<Dictionary<string, string>>();
            Assert.Equal("This administrative region has already been created.", error?["message"]);
        }
        finally
        {
            TryDeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public async Task Backfill_skips_ambiguous_matches_and_uses_alias_only_when_configured()
    {
        var tempRoot = CreateTempBoundaryDirectory();

        try
        {
            var adm1Path = await WriteBackfillCandidateFileAsync(tempRoot);
            await using (var noAliasApp = await GeotrackApiFactory.CreateAsync(
                configurationOverrides: new Dictionary<string, string?>
                {
                    ["BoundaryImport:Adm1GeoJsonPath"] = adm1Path
                }))
            {
                await CreateBackfillFixtureAsync(noAliasApp, "Ambiguous Region");
                await CreateBackfillFixtureAsync(noAliasApp, "Dalmatia", iso3: "TBB");
                await ImportAdmBoundaryCandidatesAsync(noAliasApp);

                using var admin = await LoginAdminAsync(noAliasApp, "zika@gmail.com", "Proba123!");
                var ambiguousResponse = await admin.PostAsJsonAsync(
                    "/api/admin/regions/backfill-boundary-candidates?countryIso3=TAA&dryRun=true",
                    new { });
                var ambiguousSummary =
                    await ambiguousResponse.Content.ReadFromJsonAsync<RegionBoundaryBackfillSummaryDto>();
                Assert.NotNull(ambiguousSummary);
                var ambiguous = Assert.Single(ambiguousSummary!.Items);
                Assert.Equal("Ambiguous", ambiguous.Status);

                var noAliasResponse = await admin.PostAsJsonAsync(
                    "/api/admin/regions/backfill-boundary-candidates?countryIso3=TBB&dryRun=true",
                    new { });
                var noAliasSummary =
                    await noAliasResponse.Content.ReadFromJsonAsync<RegionBoundaryBackfillSummaryDto>();
                Assert.NotNull(noAliasSummary);
                var noAlias = Assert.Single(noAliasSummary!.Items);
                Assert.Equal("NoMatch", noAlias.Status);
            }

            await using (var aliasApp = await GeotrackApiFactory.CreateAsync(
                configurationOverrides: new Dictionary<string, string?>
                {
                    ["BoundaryImport:Adm1GeoJsonPath"] = adm1Path,
                    ["RegionBoundaryBackfill:Aliases:TBB:Dalmatia"] = "Split-Dalmatia"
                }))
            {
                await CreateBackfillFixtureAsync(aliasApp, "Dalmatia", iso3: "TBB");
                await ImportAdmBoundaryCandidatesAsync(aliasApp);

                using var admin = await LoginAdminAsync(aliasApp, "zika@gmail.com", "Proba123!");
                var response = await admin.PostAsJsonAsync(
                    "/api/admin/regions/backfill-boundary-candidates?countryIso3=TBB&dryRun=true",
                    new { });

                var summary = await response.Content.ReadFromJsonAsync<RegionBoundaryBackfillSummaryDto>();
                Assert.NotNull(summary);
                var item = Assert.Single(summary!.Items);
                Assert.Equal("Matched", item.Status);
                Assert.Equal("Split-Dalmatia", item.MatchedCandidateName);
            }
        }
        finally
        {
            TryDeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public async Task Backfill_endpoint_requires_admin_role()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();

        using var anonymous = app.CreateClient();
        var anonymousResponse = await anonymous.PostAsJsonAsync(
            "/api/admin/regions/backfill-boundary-candidates?countryIso3=TAA&dryRun=true",
            new { });
        Assert.Equal(HttpStatusCode.Unauthorized, anonymousResponse.StatusCode);

        using var manager = await LoginAdminAsync(app, "mika@gmail.com", "Proba123!");
        var managerResponse = await manager.PostAsJsonAsync(
            "/api/admin/regions/backfill-boundary-candidates?countryIso3=TAA&dryRun=true",
            new { });
        Assert.Equal(HttpStatusCode.Forbidden, managerResponse.StatusCode);
    }

    private static async Task<BackfillFixture> CreateBackfillFixtureAsync(
        GeotrackApiFactory app,
        string regionName,
        string iso3 = "TAA",
        bool includeDestination = false)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var manager = await db.Users.SingleAsync(user => user.Email == "mika@gmail.com");
        var now = DateTime.UtcNow;

        var territory = new Territory
        {
            Name = $"Backfill {iso3} {Guid.NewGuid():N}",
            TerritoryKind = TerritoryKinds.Country,
            CountryIso3 = iso3,
            CreatedAt = now,
            BoundaryShapeType = "Polygon",
            BoundarySource = "geoBoundaries",
            BoundaryAdmLevel = "ADM0"
        };

        var region = new Region
        {
            Name = regionName,
            Territory = territory,
            CreatedAt = now
        };

        db.Territories.Add(territory);
        db.Regions.Add(region);
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
            CreatedAt = now
        });

        int? destinationId = null;
        if (includeDestination)
        {
            var destinationType = new DestinationType
            {
                Name = $"Backfill Destination Type {Guid.NewGuid():N}",
                Description = "Backfill test destination type",
                CreatedAt = now
            };
            db.DestinationTypes.Add(destinationType);
            await db.SaveChangesAsync();

            var destination = new Destination
            {
                Name = $"Backfill Destination {Guid.NewGuid():N}",
                DestinationTypeId = destinationType.Id,
                RegionId = region.Id,
                Description = "Backfill preservation test destination",
                Latitude = 0.5,
                Longitude = 0.5,
                CreatedAt = now
            };
            db.Destinations.Add(destination);
            await db.SaveChangesAsync();
            destinationId = destination.Id;
        }

        await db.SaveChangesAsync();
        return new BackfillFixture(territory.Id, region.Id, destinationId);
    }

    private static async Task<bool> RegionsBoundaryDisplayGeometryColumnExistsAsync(AppDbContext db)
    {
        var connection = db.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync();
        }

        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT EXISTS (
                SELECT 1
                FROM information_schema.columns
                WHERE table_schema = 'public'
                  AND table_name = 'Regions'
                  AND column_name = 'BoundaryDisplayGeometry'
            );
            """;

        return await command.ExecuteScalarAsync() is true;
    }

    private static string CreateTempBoundaryDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "geotrack-boundary-backfill-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static async Task<string> WriteBackfillCandidateFileAsync(string tempRoot)
    {
        var adm1Path = Path.Combine(tempRoot, "local-adm1-backfill.geojson");
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
                  "properties": { "shapeName": "Ambiguous Region", "shapeID": "AMBIGUOUS-1", "shapeGroup": "TAA", "shapeType": "ADM1" },
                  "geometry": {
                    "type": "Polygon",
                    "coordinates": [[[1.0,0.2],[1.2,0.2],[1.2,0.4],[1.0,0.4],[1.0,0.2]]]
                  }
                },
                {
                  "type": "Feature",
                  "properties": { "shapeName": "Ambiguous Region", "shapeID": "AMBIGUOUS-2", "shapeGroup": "TAA", "shapeType": "ADM1" },
                  "geometry": {
                    "type": "Polygon",
                    "coordinates": [[[1.3,0.2],[1.5,0.2],[1.5,0.4],[1.3,0.4],[1.3,0.2]]]
                  }
                },
                {
                  "type": "Feature",
                  "properties": { "shapeName": "Split-Dalmatia", "shapeID": "SPLIT-DALMATIA-1", "shapeGroup": "TBB", "shapeType": "ADM1" },
                  "geometry": {
                    "type": "Polygon",
                    "coordinates": [[[0.2,0.2],[0.8,0.2],[0.8,0.8],[0.2,0.8],[0.2,0.2]]]
                  }
                },
                {
                  "type": "Feature",
                  "properties": { "shapeName": "Dubrovnik-Neretva", "shapeID": "DUBROVNIK-NERETVA-1", "shapeGroup": "HRV", "shapeType": "ADM1" },
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

    private static async Task<string> WriteBackfillAdm0FileAsync(string tempRoot)
    {
        var adm0Path = Path.Combine(tempRoot, "local-adm0-backfill.geojson");
        await File.WriteAllTextAsync(adm0Path, """
            {
              "type": "FeatureCollection",
              "features": [
                {
                  "type": "Feature",
                  "properties": { "shapeName": "Croatia", "shapeID": "HRV-ADM0", "shapeGroup": "HRV", "shapeType": "ADM0" },
                  "geometry": {
                    "type": "Polygon",
                    "coordinates": [[[0.0,0.0],[2.0,0.0],[2.0,2.0],[0.0,2.0],[0.0,0.0]]]
                  }
                }
              ]
            }
            """);
        return adm0Path;
    }

    private static async Task<BoundaryImportSummaryDto> ImportAdmBoundaryCandidatesAsync(
        GeotrackApiFactory app)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var import = scope.ServiceProvider.GetRequiredService<ILocalBoundaryImportService>();
        return await import.ImportLocalBoundariesAsync("ADM1");
    }

    private static async Task<HttpClient> LoginAdminAsync(
        GeotrackApiFactory app,
        string email,
        string password)
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

    private sealed record BackfillFixture(
        int TerritoryId,
        int RegionId,
        int? DestinationId);
}
