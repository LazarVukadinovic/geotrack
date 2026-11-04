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
using PrecisionModel = NetTopologySuite.Geometries.PrecisionModel;

namespace Geotrack.IntegrationTests;

public sealed class AdmBoundaryRegionSyncTests
{
    private const string BoundaryGeometryPropertyName = "BoundaryGeometry";
    private const string BoundaryDisplayGeometryPropertyName = "BoundaryDisplayGeometry";
    private static readonly GeometryFactory GeometryFactory = new(new PrecisionModel(), 4326);

    [Fact]
    public async Task Sync_service_creates_missing_regions_is_idempotent_and_dry_run_safe()
    {
        var tempRoot = CreateTempBoundaryDirectory();

        try
        {
            var adm1Path = await WriteSyncAdm1FileAsync(tempRoot);
            await using var app = await GeotrackApiFactory.CreateAsync(
                configurationOverrides: new Dictionary<string, string?>
                {
                    ["BoundaryImport:Adm1GeoJsonPath"] = adm1Path
                });

            var territoryId = await CreateTerritoryManagerFixtureAsync(app, "TAA");
            await ImportAdm1CandidatesAsync(app);

            await using (var scope = app.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var sync = scope.ServiceProvider.GetRequiredService<IAdmBoundaryRegionSyncService>();

                var dryRun = await sync.SyncRegionsFromAdm1CandidatesAsync(territoryId, dryRun: true);
                Assert.Equal(2, dryRun.TotalCandidates);
                Assert.Equal(2, dryRun.CreatedCount);
                Assert.Equal(0, await db.Regions.CountAsync(region => region.TerritoryId == territoryId));

                var firstRun = await sync.SyncRegionsFromAdm1CandidatesAsync(territoryId, dryRun: false);
                Assert.Equal(2, firstRun.CreatedCount);
                Assert.Equal(2, await db.Regions.CountAsync(region => region.TerritoryId == territoryId));

                var secondRun = await sync.SyncRegionsFromAdm1CandidatesAsync(territoryId, dryRun: false);
                Assert.Equal(0, secondRun.CreatedCount);
                Assert.Equal(2, secondRun.ExistingLinkedCount);
                Assert.Equal(2, await db.Regions.CountAsync(region => region.TerritoryId == territoryId));

                var regions = await db.Regions
                    .Where(region => region.TerritoryId == territoryId)
                    .OrderBy(region => region.Name)
                    .ToListAsync();

                Assert.All(regions, region =>
                {
                    Assert.Equal("geoBoundaries", region.BoundarySource);
                    Assert.Equal("ADM1", region.BoundaryAdmLevel);
                    Assert.Equal("gbOpen", region.BoundaryReleaseType);
                    Assert.Equal("CC BY 4.0", region.BoundaryLicense);
                    Assert.NotNull(region.BoundaryCandidateId);
                    Assert.NotNull(db.Entry(region).Property<Geometry?>(BoundaryGeometryPropertyName).CurrentValue);
                });

                Assert.False(await db.RegionalManagerRegions.AnyAsync(assignment =>
                    regions.Select(region => region.Id).Contains(assignment.RegionId)));
            }
        }
        finally
        {
            TryDeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public async Task Seed_sync_links_dubrovnik_preserves_content_manager_creates_other_hrv_regions_and_does_not_sync_serbia()
    {
        var tempRoot = CreateTempBoundaryDirectory();

        try
        {
            var adm0Path = await WriteSeedAdm0FileAsync(tempRoot);
            var adm1Path = await WriteSeedAdm1FileAsync(tempRoot);
            await using var app = await GeotrackApiFactory.CreateAsync(
                configurationOverrides: new Dictionary<string, string?>
                {
                    ["BoundaryImport:Adm0GeoJsonPath"] = adm0Path,
                    ["BoundaryImport:Adm1GeoJsonPath"] = adm1Path,
                    ["Seed:ImportLocalBoundaries:Enabled"] = "true",
                    ["Seed:ImportLocalBoundaries:Adm0"] = "true",
                    ["Seed:ImportLocalBoundaries:Adm1"] = "true",
                    ["Seed:SyncAdm1Regions:Enabled"] = "true",
                    ["Seed:SyncAdm1Regions:CountryIso3"] = "HRV"
                });

            await using var scope = app.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var croatia = await db.Territories.SingleAsync(territory => territory.CountryIso3 == "HRV");
            var dubrovnik = await db.Regions.SingleAsync(region =>
                region.TerritoryId == croatia.Id &&
                region.Name == "Dubrovnik-Neretva");
            var istria = await db.Regions.SingleAsync(region =>
                region.TerritoryId == croatia.Id &&
                region.Name == "Istria");
            var candidate = await db.AdministrativeBoundaryCandidates.SingleAsync(item =>
                item.CountryIso3 == "HRV" &&
                item.ShapeName == "Dubrovnik-Neretva");

            Assert.Equal(candidate.Id, dubrovnik.BoundaryCandidateId);
            Assert.Equal(candidate.ExternalKey, dubrovnik.BoundaryExternalKey);
            Assert.Equal("HRV", dubrovnik.BoundaryShapeGroup);
            Assert.Equal("DUBROVNIK-NERETVA-1", dubrovnik.BoundaryShapeId);
            Assert.Equal("ADM1", dubrovnik.BoundaryAdmLevel);
            Assert.Equal("geoBoundaries", dubrovnik.BoundarySource);
            Assert.Equal("gbOpen", dubrovnik.BoundaryReleaseType);
            Assert.NotNull(db.Entry(dubrovnik).Property<Geometry?>(BoundaryGeometryPropertyName).CurrentValue);

            Assert.NotNull(istria.BoundaryCandidateId);
            Assert.Equal("ISTRIA-1", istria.BoundaryShapeId);
            Assert.False(await db.RegionalManagerRegions.AnyAsync(assignment => assignment.RegionId == istria.Id));

            Assert.True(await db.Destinations.AnyAsync(destination =>
                destination.Name == "Dubrovnik" &&
                destination.RegionId == dubrovnik.Id));
            Assert.True(await db.Places.AnyAsync(place =>
                place.Location != null &&
                place.Location.Destination.RegionId == dubrovnik.Id));
            Assert.True(await db.Events.AnyAsync(evt =>
                evt.Destination.RegionId == dubrovnik.Id));
            Assert.True(await db.RegionalManagerRegions.AnyAsync(assignment =>
                assignment.RegionId == dubrovnik.Id &&
                assignment.User.Email == "pera@gmail.com" &&
                assignment.IsActive));

            Assert.False(await db.Regions.AnyAsync(region =>
                region.BoundaryAdmLevel == "ADM1" &&
                region.BoundaryShapeGroup == "SRB"));
        }
        finally
        {
            TryDeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public async Task Seed_sync_creates_real_serbia_regions_assigns_content_and_seeds_serbia_managers()
    {
        var tempRoot = CreateTempBoundaryDirectory();

        try
        {
            var adm0Path = await WriteSerbiaSeedAdm0FileAsync(tempRoot);
            var adm1Path = await WriteSeedAdm1FileWithSerbiaDistrictsAsync(tempRoot);
            await using var app = await GeotrackApiFactory.CreateAsync(
                configurationOverrides: new Dictionary<string, string?>
                {
                    ["BoundaryImport:Adm0GeoJsonPath"] = adm0Path,
                    ["BoundaryImport:Adm1GeoJsonPath"] = adm1Path,
                    ["Seed:ImportLocalBoundaries:Enabled"] = "true",
                    ["Seed:ImportLocalBoundaries:Adm0"] = "true",
                    ["Seed:ImportLocalBoundaries:Adm1"] = "true",
                    ["Seed:SyncAdm1Regions:Enabled"] = "true",
                    ["Seed:SyncAdm1Regions:CountryIso3"] = "HRV,SRB"
                });

            int serbiaId;
            int croatiaId;
            int sumadijaRegionId;

            await using (var scope = app.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var serbia = await db.Territories.SingleAsync(territory => territory.CountryIso3 == "SRB");
                var croatia = await db.Territories.SingleAsync(territory => territory.CountryIso3 == "HRV");
                serbiaId = serbia.Id;
                croatiaId = croatia.Id;

                Assert.Equal("Serbia", serbia.Name);
                Assert.Equal("RS", serbia.CountryCode);
                Assert.DoesNotContain(await db.Regions.Where(region => region.TerritoryId == serbia.Id).ToListAsync(),
                    region => region.Name is "Central Serbia" or "Vojvodina" or "Western Serbia");

                var serbiaRegions = await db.Regions
                    .Where(region => region.TerritoryId == serbia.Id)
                    .OrderBy(region => region.Name)
                    .ToListAsync();
                Assert.Equal(5, serbiaRegions.Count);
                Assert.All(serbiaRegions, region =>
                {
                    Assert.Equal("geoBoundaries", region.BoundarySource);
                    Assert.Equal("ADM1", region.BoundaryAdmLevel);
                    Assert.Equal("SRB", region.BoundaryShapeGroup);
                    Assert.Equal("gbOpen", region.BoundaryReleaseType);
                    Assert.Equal("CC BY 4.0", region.BoundaryLicense);
                    Assert.NotNull(region.BoundaryCandidateId);
                    Assert.NotNull(region.BoundaryExternalKey);
                    Assert.NotNull(db.Entry(region).Property<Geometry?>(BoundaryGeometryPropertyName).CurrentValue);
                });

                Assert.Equal(serbiaRegions.Count, await db.Regions
                    .Where(region => region.TerritoryId == serbia.Id && region.BoundaryCandidateId != null)
                    .Select(region => region.BoundaryCandidateId)
                    .Distinct()
                    .CountAsync());

                await AssertDestinationRegionAsync(db, "Beograd", "Belgrade");
                await AssertDestinationRegionAsync(db, "Novi Sad", "South Backa District");
                await AssertDestinationRegionAsync(db, "Zlatibor", "Zlatibor District");
                await AssertDestinationRegionAsync(db, "Tara", "Zlatibor District");
                await AssertDestinationRegionAsync(db, "Kopaonik", "Raska District");
                await AssertDestinationRegionAsync(db, "Kragujevac", "Sumadija District");

                var nikola = await db.Users
                    .Include(user => user.UserRoles)
                        .ThenInclude(userRole => userRole.Role)
                    .Include(user => user.TerritoryManager!)
                        .ThenInclude(manager => manager.TerritoryManagerTerritories)
                    .SingleAsync(user => user.Email == "nikola@gmail.com");
                Assert.False(nikola.IsDeleted);
                Assert.False(nikola.IsSuspended);
                Assert.Contains(nikola.UserRoles, userRole => userRole.Role.Name == RoleNames.TerritoryManager);
                Assert.Contains(nikola.TerritoryManager!.TerritoryManagerTerritories,
                    assignment => assignment.TerritoryId == serbia.Id);
                Assert.DoesNotContain(nikola.TerritoryManager!.TerritoryManagerTerritories,
                    assignment => assignment.TerritoryId == croatia.Id);

                var regional = await db.Users
                    .Include(user => user.UserRoles)
                        .ThenInclude(userRole => userRole.Role)
                    .Include(user => user.RegionalManagerRegions)
                        .ThenInclude(assignment => assignment.Region)
                    .SingleAsync(user => user.Email == "serbia.regional@gmail.com");
                var sumadija = serbiaRegions.Single(region => region.Name == "Sumadija District");
                sumadijaRegionId = sumadija.Id;
                Assert.Contains(regional.UserRoles, userRole => userRole.Role.Name == RoleNames.RegionalManager);
                Assert.Contains(regional.RegionalManagerRegions,
                    assignment => assignment.RegionId == sumadija.Id && assignment.IsActive);
                Assert.DoesNotContain(regional.RegionalManagerRegions,
                    assignment => assignment.IsActive &&
                        assignment.Region.TerritoryId == serbia.Id &&
                        assignment.RegionId != sumadija.Id);

                var dubrovnik = await db.Regions.SingleAsync(region =>
                    region.TerritoryId == croatia.Id &&
                    region.Name == "Dubrovnik-Neretva");
                Assert.True(await db.RegionalManagerRegions.AnyAsync(assignment =>
                    assignment.RegionId == dubrovnik.Id &&
                    assignment.User.Email == "pera@gmail.com" &&
                    assignment.IsActive));

                var mika = await db.Users
                    .Include(user => user.TerritoryManager!)
                        .ThenInclude(manager => manager.TerritoryManagerTerritories)
                    .SingleAsync(user => user.Email == "mika@gmail.com");
                Assert.Contains(mika.TerritoryManager!.TerritoryManagerTerritories,
                    assignment => assignment.TerritoryId == croatia.Id);
                Assert.DoesNotContain(mika.TerritoryManager!.TerritoryManagerTerritories,
                    assignment => assignment.TerritoryId == serbia.Id);
            }

            using (var nikolaClient = await LoginAdminAsync(app, "nikola@gmail.com", "TEST-only-Fixture-9284!"))
            {
                var assignedTerritories = await nikolaClient.GetFromJsonAsync<List<TerritoryAssignedTerritoryDto>>(
                    "/api/admin/territory/territories");
                Assert.NotNull(assignedTerritories);
                Assert.Contains(assignedTerritories!, territory => territory.Id == serbiaId && territory.Name == "Serbia");
                Assert.DoesNotContain(assignedTerritories!, territory => territory.Id == croatiaId);

                var regions = await nikolaClient.GetFromJsonAsync<TerritoryRegionDirectoryResponseDto>(
                    $"/api/admin/territory/regions?territoryId={serbiaId}&take=50");
                Assert.NotNull(regions);
                Assert.Contains(regions!.Items, region => region.Name == "Sumadija District");
                Assert.DoesNotContain(regions.Items,
                    region => region.Name is "Central Serbia" or "Vojvodina" or "Western Serbia");

                var croatiaResponse = await nikolaClient.GetAsync(
                    $"/api/admin/territory/regions?territoryId={croatiaId}&take=50");
                Assert.Equal(HttpStatusCode.Forbidden, croatiaResponse.StatusCode);
            }

            using (var mikaClient = await LoginAdminAsync(app, "mika@gmail.com", "TEST-only-Fixture-9284!"))
            {
                var serbiaResponse = await mikaClient.GetAsync(
                    $"/api/admin/territory/regions?territoryId={serbiaId}&take=50");
                Assert.Equal(HttpStatusCode.Forbidden, serbiaResponse.StatusCode);
            }

            using (var regionalClient = await LoginAdminAsync(app, "serbia.regional@gmail.com", "TEST-only-Fixture-9284!"))
            {
                var assignedRegions = await regionalClient.GetFromJsonAsync<List<RegionalAssignedRegionDto>>(
                    "/api/admin/regional/regions");
                var assigned = Assert.Single(assignedRegions!);
                Assert.Equal(sumadijaRegionId, assigned.Id);
                Assert.Equal("Sumadija District", assigned.Name);
            }
        }
        finally
        {
            TryDeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public async Task Assign_content_by_boundary_dry_run_reports_changes_without_writing_and_apply_repairs()
    {
        var tempRoot = CreateTempBoundaryDirectory();

        try
        {
            var adm0Path = await WriteSerbiaSeedAdm0FileAsync(tempRoot);
            var adm1Path = await WriteSeedAdm1FileWithSerbiaDistrictsAsync(tempRoot);
            await using var app = await GeotrackApiFactory.CreateAsync(
                configurationOverrides: new Dictionary<string, string?>
                {
                    ["BoundaryImport:Adm0GeoJsonPath"] = adm0Path,
                    ["BoundaryImport:Adm1GeoJsonPath"] = adm1Path,
                    ["Seed:ImportLocalBoundaries:Enabled"] = "true",
                    ["Seed:ImportLocalBoundaries:Adm0"] = "true",
                    ["Seed:ImportLocalBoundaries:Adm1"] = "true",
                    ["Seed:SyncAdm1Regions:Enabled"] = "true",
                    ["Seed:SyncAdm1Regions:CountryIso3"] = "HRV,SRB"
                });

            int serbiaId;
            int wrongRegionId;
            int correctRegionId;

            await using (var scope = app.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var serbia = await db.Territories.SingleAsync(territory => territory.CountryIso3 == "SRB");
                serbiaId = serbia.Id;
                wrongRegionId = await db.Regions
                    .Where(region => region.TerritoryId == serbia.Id && region.Name == "Belgrade")
                    .Select(region => region.Id)
                    .SingleAsync();
                correctRegionId = await db.Regions
                    .Where(region => region.TerritoryId == serbia.Id && region.Name == "Sumadija District")
                    .Select(region => region.Id)
                    .SingleAsync();

                var kragujevac = await db.Destinations.SingleAsync(destination => destination.Name == "Kragujevac");
                kragujevac.RegionId = wrongRegionId;
                await db.SaveChangesAsync();
            }

            using var admin = await LoginAdminAsync(app, "zika@gmail.com", "TEST-only-Fixture-9284!");
            var dryRunResponse = await admin.PostAsJsonAsync(
                $"/api/admin/territories/{serbiaId}/regions/assign-content-by-boundary?dryRun=true",
                new { });
            dryRunResponse.EnsureSuccessStatusCode();

            var dryRun = await dryRunResponse.Content.ReadFromJsonAsync<RegionContentAssignmentSummaryDto>();
            Assert.NotNull(dryRun);
            Assert.True(dryRun!.DryRun);
            Assert.Equal(1, dryRun.WouldUpdateCount);
            Assert.Equal(0, dryRun.UpdatedCount);
            Assert.Contains(dryRun.Items, item =>
                item.ContentType == "Destination" &&
                item.ContentName == "Kragujevac" &&
                item.CurrentRegionId == wrongRegionId &&
                item.MatchedRegionId == correctRegionId &&
                item.Status == "WouldUpdate");

            await using (var scope = app.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var kragujevac = await db.Destinations.SingleAsync(destination => destination.Name == "Kragujevac");
                Assert.Equal(wrongRegionId, kragujevac.RegionId);
            }

            var applyResponse = await admin.PostAsJsonAsync(
                $"/api/admin/territories/{serbiaId}/regions/assign-content-by-boundary?dryRun=false",
                new { });
            applyResponse.EnsureSuccessStatusCode();

            var applied = await applyResponse.Content.ReadFromJsonAsync<RegionContentAssignmentSummaryDto>();
            Assert.NotNull(applied);
            Assert.False(applied!.DryRun);
            Assert.Equal(1, applied.UpdatedCount);

            await using (var scope = app.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var kragujevac = await db.Destinations.SingleAsync(destination => destination.Name == "Kragujevac");
                Assert.Equal(correctRegionId, kragujevac.RegionId);
            }
        }
        finally
        {
            TryDeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public async Task Territory_manager_sync_endpoint_allows_only_assigned_territory()
    {
        var tempRoot = CreateTempBoundaryDirectory();

        try
        {
            var adm1Path = await WriteSyncAdm1FileAsync(tempRoot);
            await using var app = await GeotrackApiFactory.CreateAsync(
                configurationOverrides: new Dictionary<string, string?>
                {
                    ["BoundaryImport:Adm1GeoJsonPath"] = adm1Path
                });

            var assignedTerritoryId = await CreateTerritoryManagerFixtureAsync(app, "TAA");
            var otherTerritoryId = await CreateTerritoryAsync(app, "TBB");
            await ImportAdm1CandidatesAsync(app);

            using var manager = await LoginAdminAsync(app, "mika@gmail.com", "TEST-only-Fixture-9284!");
            var assignedResponse = await manager.PostAsJsonAsync(
                $"/api/admin/territory/regions/sync-from-adm1?territoryId={assignedTerritoryId}&dryRun=true",
                new { });
            Assert.Equal(HttpStatusCode.OK, assignedResponse.StatusCode);

            var summary = await assignedResponse.Content.ReadFromJsonAsync<RegionSyncSummaryDto>();
            Assert.NotNull(summary);
            Assert.Equal(2, summary!.TotalCandidates);
            Assert.True(summary.DryRun);

            var forbiddenResponse = await manager.PostAsJsonAsync(
                $"/api/admin/territory/regions/sync-from-adm1?territoryId={otherTerritoryId}&dryRun=true",
                new { });
            Assert.Equal(HttpStatusCode.Forbidden, forbiddenResponse.StatusCode);
        }
        finally
        {
            TryDeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public async Task Territory_manager_setup_status_requires_initial_sync_then_reports_up_to_date_and_new_candidates()
    {
        var tempRoot = CreateTempBoundaryDirectory();

        try
        {
            var adm1Path = await WriteSyncAdm1FileAsync(tempRoot);
            await using var app = await GeotrackApiFactory.CreateAsync(
                configurationOverrides: new Dictionary<string, string?>
                {
                    ["BoundaryImport:Adm1GeoJsonPath"] = adm1Path
                });

            var territoryId = await CreateTerritoryManagerFixtureAsync(app, "TAA");
            await ImportAdm1CandidatesAsync(app);

            using var manager = await LoginAdminAsync(app, "mika@gmail.com", "TEST-only-Fixture-9284!");
            var initial = await manager.GetFromJsonAsync<TerritoryRegionSetupStatusDto>(
                $"/api/admin/territory/regions/setup-status?territoryId={territoryId}");

            Assert.NotNull(initial);
            Assert.Equal(territoryId, initial!.TerritoryId);
            Assert.Equal("TAA", initial.CountryIso3);
            Assert.True(initial.RequiresInitialSync);
            Assert.False(initial.OnboardingCompleted);
            Assert.False(initial.HasSyncedRegions);
            Assert.Equal(0, initial.SyncedRegionCount);
            Assert.Equal(2, initial.CandidateCount);
            Assert.True(initial.HasCandidateData);
            Assert.True(initial.CanSync);
            Assert.False(initial.CanUpdate);
            Assert.True(initial.UpdateAvailable);
            Assert.False(initial.MissingCandidateData);
            Assert.Null(initial.RegionSetupCompletedAt);
            Assert.NotNull(initial.CandidateVersionHash);
            Assert.Null(initial.SyncedVersionHash);

            var syncResponse = await manager.PostAsJsonAsync(
                $"/api/admin/territory/regions/sync-from-adm1?territoryId={territoryId}",
                new { });
            syncResponse.EnsureSuccessStatusCode();

            var firstSync = await syncResponse.Content.ReadFromJsonAsync<RegionSyncSummaryDto>();
            Assert.NotNull(firstSync);
            Assert.False(firstSync!.DryRun);
            Assert.Equal(2, firstSync.CreatedCount);
            Assert.True(firstSync.CompletedAt > DateTime.MinValue);

            var afterSync = await manager.GetFromJsonAsync<TerritoryRegionSetupStatusDto>(
                $"/api/admin/territory/regions/setup-status?territoryId={territoryId}");
            Assert.NotNull(afterSync);
            Assert.False(afterSync!.RequiresInitialSync);
            Assert.True(afterSync.OnboardingCompleted);
            Assert.True(afterSync.HasSyncedRegions);
            Assert.Equal(2, afterSync.SyncedRegionCount);
            Assert.False(afterSync.UpdateAvailable);
            Assert.False(afterSync.CanUpdate);
            Assert.Equal("Regions are up to date.", afterSync.Message);
            Assert.NotNull(afterSync.RegionSetupCompletedAt);
            Assert.NotNull(afterSync.SyncedVersionHash);

            await using (var completionScope = app.Services.CreateAsyncScope())
            {
                var db = completionScope.ServiceProvider.GetRequiredService<AppDbContext>();
                var managerUser = await db.Users.SingleAsync(user => user.Email == "mika@gmail.com");
                var assignment = await db.TerritoryManagerTerritories.SingleAsync(item =>
                    item.UserId == managerUser.Id &&
                    item.TerritoryId == territoryId);
                Assert.NotNull(assignment.RegionSetupCompletedAt);
            }

            var idempotentResponse = await manager.PostAsJsonAsync(
                $"/api/admin/territory/regions/sync-from-adm1?territoryId={territoryId}",
                new { });
            idempotentResponse.EnsureSuccessStatusCode();
            var idempotent = await idempotentResponse.Content.ReadFromJsonAsync<RegionSyncSummaryDto>();
            Assert.NotNull(idempotent);
            Assert.Equal(0, idempotent!.CreatedCount);
            Assert.Equal(2, idempotent.ExistingLinkedCount);

            await using (var scope = app.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                Assert.Equal(2, await db.Regions.CountAsync(region => region.TerritoryId == territoryId));
                await AddAdm1CandidateAsync(db, "TAA", "New Interior Region", "NEW-1", 3.0, 0.2, 3.6, 0.8);
            }

            var afterCandidateChange = await manager.GetFromJsonAsync<TerritoryRegionSetupStatusDto>(
                $"/api/admin/territory/regions/setup-status?territoryId={territoryId}");
            Assert.NotNull(afterCandidateChange);
            Assert.False(afterCandidateChange!.RequiresInitialSync);
            Assert.True(afterCandidateChange.OnboardingCompleted);
            Assert.True(afterCandidateChange.UpdateAvailable);
            Assert.True(afterCandidateChange.CanUpdate);
            Assert.Equal(3, afterCandidateChange.CandidateCount);
            Assert.Equal(2, afterCandidateChange.SyncedRegionCount);
        }
        finally
        {
            TryDeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public async Task Territory_manager_setup_status_reports_missing_candidate_data()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        var territoryId = await CreateTerritoryManagerFixtureAsync(app, "QAA");

        using var manager = await LoginAdminAsync(app, "mika@gmail.com", "TEST-only-Fixture-9284!");
        var status = await manager.GetFromJsonAsync<TerritoryRegionSetupStatusDto>(
            $"/api/admin/territory/regions/setup-status?territoryId={territoryId}");

        Assert.NotNull(status);
        Assert.Equal(territoryId, status!.TerritoryId);
        Assert.Equal("QAA", status.CountryIso3);
        Assert.True(status.RequiresInitialSync);
        Assert.False(status.OnboardingCompleted);
        Assert.True(status.MissingCandidateData);
        Assert.False(status.HasCandidateData);
        Assert.False(status.CanSync);
        Assert.False(status.CanUpdate);
        Assert.False(status.UpdateAvailable);
        Assert.Equal(0, status.CandidateCount);
        Assert.Contains("ADM1 boundary data is missing", status.Message);
    }

    [Fact]
    public async Task Territory_manager_setup_status_marks_completed_when_regions_were_already_synced()
    {
        var tempRoot = CreateTempBoundaryDirectory();

        try
        {
            var adm1Path = await WriteSyncAdm1FileAsync(tempRoot);
            await using var app = await GeotrackApiFactory.CreateAsync(
                configurationOverrides: new Dictionary<string, string?>
                {
                    ["BoundaryImport:Adm1GeoJsonPath"] = adm1Path
                });

            var territoryId = await CreateTerritoryManagerFixtureAsync(app, "TAA");
            await ImportAdm1CandidatesAsync(app);

            await using (var scope = app.Services.CreateAsyncScope())
            {
                var sync = scope.ServiceProvider.GetRequiredService<IAdmBoundaryRegionSyncService>();
                var summary = await sync.SyncRegionsFromAdm1CandidatesAsync(territoryId, dryRun: false);
                Assert.Equal(2, summary.CreatedCount);
            }

            using var manager = await LoginAdminAsync(app, "mika@gmail.com", "TEST-only-Fixture-9284!");
            var status = await manager.GetFromJsonAsync<TerritoryRegionSetupStatusDto>(
                $"/api/admin/territory/regions/setup-status?territoryId={territoryId}");

            Assert.NotNull(status);
            Assert.False(status!.RequiresInitialSync);
            Assert.True(status.OnboardingCompleted);
            Assert.True(status.HasSyncedRegions);
            Assert.Equal(2, status.SyncedRegionCount);
            Assert.NotNull(status.RegionSetupCompletedAt);

            await using (var scope = app.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var user = await db.Users.SingleAsync(item => item.Email == "mika@gmail.com");
                var assignment = await db.TerritoryManagerTerritories.SingleAsync(item =>
                    item.UserId == user.Id &&
                    item.TerritoryId == territoryId);
                Assert.NotNull(assignment.RegionSetupCompletedAt);
            }
        }
        finally
        {
            TryDeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public async Task Non_territory_manager_cannot_call_territory_setup_endpoints()
    {
        var tempRoot = CreateTempBoundaryDirectory();

        try
        {
            var adm1Path = await WriteSyncAdm1FileAsync(tempRoot);
            await using var app = await GeotrackApiFactory.CreateAsync(
                configurationOverrides: new Dictionary<string, string?>
                {
                    ["BoundaryImport:Adm1GeoJsonPath"] = adm1Path
                });

            var territoryId = await CreateTerritoryManagerFixtureAsync(app, "TAA");
            await ImportAdm1CandidatesAsync(app);

            using var admin = await LoginAdminAsync(app, "zika@gmail.com", "TEST-only-Fixture-9284!");

            var setupStatusResponse = await admin.GetAsync(
                $"/api/admin/territory/regions/setup-status?territoryId={territoryId}");
            Assert.Equal(HttpStatusCode.Forbidden, setupStatusResponse.StatusCode);

            var syncResponse = await admin.PostAsJsonAsync(
                $"/api/admin/territory/regions/sync-from-adm1?territoryId={territoryId}",
                new { });
            Assert.Equal(HttpStatusCode.Forbidden, syncResponse.StatusCode);
        }
        finally
        {
            TryDeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public async Task Territory_manager_sync_links_existing_regions_and_preserves_assignments_without_duplicates()
    {
        var tempRoot = CreateTempBoundaryDirectory();

        try
        {
            var adm1Path = await WriteSyncAdm1FileAsync(tempRoot);
            await using var app = await GeotrackApiFactory.CreateAsync(
                configurationOverrides: new Dictionary<string, string?>
                {
                    ["BoundaryImport:Adm1GeoJsonPath"] = adm1Path
                });

            var territoryId = await CreateTerritoryManagerFixtureAsync(app, "TAA");
            await ImportAdm1CandidatesAsync(app);

            int existingRegionId;
            int regionalManagerId;
            await using (var scope = app.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var regionalManager = await db.Users.SingleAsync(user => user.Email == "elena@example.com");
                regionalManagerId = regionalManager.Id;
                var now = DateTime.UtcNow;
                var region = new Region
                {
                    Name = "Inside Region",
                    TerritoryId = territoryId,
                    CreatedAt = now
                };

                db.Regions.Add(region);
                await db.SaveChangesAsync();
                existingRegionId = region.Id;

                db.RegionalManagerRegions.Add(new RegionalManagerRegion
                {
                    UserId = regionalManager.Id,
                    RegionId = region.Id,
                    IsActive = true,
                    CreatedAt = now
                });
                await db.SaveChangesAsync();
            }

            using var manager = await LoginAdminAsync(app, "mika@gmail.com", "TEST-only-Fixture-9284!");
            var before = await manager.GetFromJsonAsync<TerritoryRegionSetupStatusDto>(
                $"/api/admin/territory/regions/setup-status?territoryId={territoryId}");
            Assert.NotNull(before);
            Assert.True(before!.RequiresInitialSync);
            Assert.True(before.UpdateAvailable);

            var response = await manager.PostAsJsonAsync(
                $"/api/admin/territory/regions/sync-from-adm1?territoryId={territoryId}",
                new { });
            response.EnsureSuccessStatusCode();

            var summary = await response.Content.ReadFromJsonAsync<RegionSyncSummaryDto>();
            Assert.NotNull(summary);
            Assert.Equal(1, summary!.LinkedExistingCount);
            Assert.Equal(1, summary.CreatedCount);
            Assert.Equal(0, summary.FailedCount);

            await using (var scope = app.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                Assert.Equal(2, await db.Regions.CountAsync(region => region.TerritoryId == territoryId));
                Assert.Equal(1, await db.Regions.CountAsync(region =>
                    region.TerritoryId == territoryId &&
                    region.Name == "Inside Region"));
                Assert.True(await db.RegionalManagerRegions.AnyAsync(assignment =>
                    assignment.UserId == regionalManagerId &&
                    assignment.RegionId == existingRegionId &&
                    assignment.IsActive));

                var linked = await db.Regions.SingleAsync(region => region.Id == existingRegionId);
                Assert.NotNull(linked.BoundaryCandidateId);
                Assert.Equal("geoBoundaries:ADM1:TAA:INSIDE-1", linked.BoundaryExternalKey);
                Assert.NotNull(db.Entry(linked).Property<Geometry?>(BoundaryGeometryPropertyName).CurrentValue);
            }
        }
        finally
        {
            TryDeleteDirectory(tempRoot);
        }
    }

    private static async Task<int> CreateTerritoryManagerFixtureAsync(
        GeotrackApiFactory app,
        string iso3)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var manager = await db.Users.SingleAsync(user => user.Email == "mika@gmail.com");
        var territoryId = await CreateTerritoryAsync(db, iso3);

        db.TerritoryManagerTerritories.Add(new TerritoryManagerTerritory
        {
            UserId = manager.Id,
            TerritoryId = territoryId,
            CreatedAt = DateTime.UtcNow
        });

        await db.SaveChangesAsync();
        return territoryId;
    }

    private static async Task<int> CreateTerritoryAsync(
        GeotrackApiFactory app,
        string iso3)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await CreateTerritoryAsync(db, iso3);
    }

    private static async Task<int> CreateTerritoryAsync(
        AppDbContext db,
        string iso3)
    {
        var now = DateTime.UtcNow;
        var territory = new Territory
        {
            Name = $"Sync {iso3} {Guid.NewGuid():N}",
            TerritoryKind = TerritoryKinds.Country,
            CountryIso3 = iso3,
            CreatedAt = now,
            BoundaryShapeType = "Polygon",
            BoundarySource = "geoBoundaries",
            BoundaryAdmLevel = "ADM0"
        };

        db.Territories.Add(territory);
        await db.SaveChangesAsync();
        return territory.Id;
    }

    private static async Task<BoundaryImportSummaryDto> ImportAdm1CandidatesAsync(
        GeotrackApiFactory app)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var import = scope.ServiceProvider.GetRequiredService<ILocalBoundaryImportService>();
        return await import.ImportLocalBoundariesAsync("ADM1");
    }

    private static async Task AddAdm1CandidateAsync(
        AppDbContext db,
        string iso3,
        string name,
        string shapeId,
        double west,
        double south,
        double east,
        double north)
    {
        var now = DateTime.UtcNow;
        var candidate = new AdministrativeBoundaryCandidate
        {
            Source = "geoBoundaries",
            ReleaseType = "gbOpen",
            AdmLevel = "ADM1",
            ShapeGroup = iso3,
            ShapeId = shapeId,
            ShapeName = name,
            ShapeType = "ADM1",
            ExternalKey = $"geoBoundaries:ADM1:{iso3}:{shapeId}",
            CountryIso3 = iso3,
            ImportedAt = now,
            CreatedAt = now
        };

        db.AdministrativeBoundaryCandidates.Add(candidate);
        db.Entry(candidate).Property<Geometry?>(BoundaryGeometryPropertyName).CurrentValue =
            Polygon(west, south, east, north);
        db.Entry(candidate).Property<Geometry?>(BoundaryDisplayGeometryPropertyName).CurrentValue =
            Polygon(west, south, east, north);

        await db.SaveChangesAsync();
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

    private static async Task AssertDestinationRegionAsync(
        AppDbContext db,
        string destinationName,
        string expectedRegionName)
    {
        var destination = await db.Destinations
            .Include(item => item.Region)
            .SingleAsync(item => item.Name == destinationName);

        Assert.Equal(expectedRegionName, destination.Region.Name);
        Assert.Equal("SRB", destination.Region.BoundaryShapeGroup);
        Assert.Equal("ADM1", destination.Region.BoundaryAdmLevel);
    }

    private static Geometry Polygon(double west, double south, double east, double north)
    {
        var polygon = GeometryFactory.CreatePolygon(new[]
        {
            new Coordinate(west, south),
            new Coordinate(east, south),
            new Coordinate(east, north),
            new Coordinate(west, north),
            new Coordinate(west, south)
        });
        polygon.SRID = 4326;
        return polygon;
    }

    private static string CreateTempBoundaryDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "geotrack-adm1-region-sync-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static async Task<string> WriteSyncAdm1FileAsync(string tempRoot)
    {
        var adm1Path = Path.Combine(tempRoot, "local-adm1-sync.geojson");
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
                  "properties": { "shapeName": "Coastal Region", "shapeID": "COASTAL-1", "shapeGroup": "TAA", "shapeType": "ADM1" },
                  "geometry": {
                    "type": "Polygon",
                    "coordinates": [[[1.0,0.2],[1.6,0.2],[1.6,0.8],[1.0,0.8],[1.0,0.2]]]
                  }
                },
                {
                  "type": "Feature",
                  "properties": { "shapeName": "Other Region", "shapeID": "OTHER-1", "shapeGroup": "TBB", "shapeType": "ADM1" },
                  "geometry": {
                    "type": "Polygon",
                    "coordinates": [[[2.0,0.2],[2.6,0.2],[2.6,0.8],[2.0,0.8],[2.0,0.2]]]
                  }
                }
              ]
            }
            """);
        return adm1Path;
    }

    private static async Task<string> WriteSerbiaSeedAdm0FileAsync(string tempRoot)
    {
        var adm0Path = Path.Combine(tempRoot, "local-adm0-serbia-seed-sync.geojson");
        await File.WriteAllTextAsync(adm0Path, """
            {
              "type": "FeatureCollection",
              "features": [
                {
                  "type": "Feature",
                  "properties": { "shapeName": "Croatia", "shapeID": "HRV-ADM0", "shapeGroup": "HRV", "shapeType": "ADM0" },
                  "geometry": {
                    "type": "Polygon",
                    "coordinates": [[[13.0,42.0],[20.0,42.0],[20.0,47.0],[13.0,47.0],[13.0,42.0]]]
                  }
                },
                {
                  "type": "Feature",
                  "properties": { "shapeName": "Serbia", "shapeID": "SRB-ADM0", "shapeGroup": "SRB", "shapeType": "ADM0" },
                  "geometry": {
                    "type": "Polygon",
                    "coordinates": [[[18.0,42.0],[23.5,42.0],[23.5,46.5],[18.0,46.5],[18.0,42.0]]]
                  }
                }
              ]
            }
            """);
        return adm0Path;
    }

    private static async Task<string> WriteSeedAdm1FileWithSerbiaDistrictsAsync(string tempRoot)
    {
        var adm1Path = Path.Combine(tempRoot, "local-adm1-serbia-seed-sync.geojson");
        await File.WriteAllTextAsync(adm1Path, """
            {
              "type": "FeatureCollection",
              "features": [
                {
                  "type": "Feature",
                  "properties": { "shapeName": "Dubrovnik-Neretva", "shapeID": "DUBROVNIK-NERETVA-1", "shapeGroup": "HRV", "shapeType": "ADM1" },
                  "geometry": {
                    "type": "Polygon",
                    "coordinates": [[[17.8,42.2],[18.8,42.2],[18.8,43.2],[17.8,43.2],[17.8,42.2]]]
                  }
                },
                {
                  "type": "Feature",
                  "properties": { "shapeName": "Istria", "shapeID": "ISTRIA-1", "shapeGroup": "HRV", "shapeType": "ADM1" },
                  "geometry": {
                    "type": "Polygon",
                    "coordinates": [[[13.4,44.6],[14.4,44.6],[14.4,45.6],[13.4,45.6],[13.4,44.6]]]
                  }
                },
                {
                  "type": "Feature",
                  "properties": { "shapeName": "Belgrade", "shapeID": "SRB-BELGRADE-1", "shapeGroup": "SRB", "shapeType": "ADM1" },
                  "geometry": {
                    "type": "Polygon",
                    "coordinates": [[[20.2,44.6],[20.7,44.6],[20.7,45.0],[20.2,45.0],[20.2,44.6]]]
                  }
                },
                {
                  "type": "Feature",
                  "properties": { "shapeName": "South Backa District", "shapeID": "SRB-SOUTH-BACKA-1", "shapeGroup": "SRB", "shapeType": "ADM1" },
                  "geometry": {
                    "type": "Polygon",
                    "coordinates": [[[19.6,45.1],[20.1,45.1],[20.1,45.5],[19.6,45.5],[19.6,45.1]]]
                  }
                },
                {
                  "type": "Feature",
                  "properties": { "shapeName": "Zlatibor District", "shapeID": "SRB-ZLATIBOR-1", "shapeGroup": "SRB", "shapeType": "ADM1" },
                  "geometry": {
                    "type": "Polygon",
                    "coordinates": [[[19.2,43.5],[20.0,43.5],[20.0,44.1],[19.2,44.1],[19.2,43.5]]]
                  }
                },
                {
                  "type": "Feature",
                  "properties": { "shapeName": "Raska District", "shapeID": "SRB-RASKA-1", "shapeGroup": "SRB", "shapeType": "ADM1" },
                  "geometry": {
                    "type": "Polygon",
                    "coordinates": [[[20.5,43.0],[21.1,43.0],[21.1,43.5],[20.5,43.5],[20.5,43.0]]]
                  }
                },
                {
                  "type": "Feature",
                  "properties": { "shapeName": "Sumadija District", "shapeID": "SRB-SUMADIJA-1", "shapeGroup": "SRB", "shapeType": "ADM1" },
                  "geometry": {
                    "type": "Polygon",
                    "coordinates": [[[20.7,43.8],[21.2,43.8],[21.2,44.2],[20.7,44.2],[20.7,43.8]]]
                  }
                }
              ]
            }
            """);
        return adm1Path;
    }

    private static async Task<string> WriteSeedAdm0FileAsync(string tempRoot)
    {
        var adm0Path = Path.Combine(tempRoot, "local-adm0-seed-sync.geojson");
        await File.WriteAllTextAsync(adm0Path, """
            {
              "type": "FeatureCollection",
              "features": [
                {
                  "type": "Feature",
                  "properties": { "shapeName": "Croatia", "shapeID": "HRV-ADM0", "shapeGroup": "HRV", "shapeType": "ADM0" },
                  "geometry": {
                    "type": "Polygon",
                    "coordinates": [[[0.0,0.0],[4.0,0.0],[4.0,4.0],[0.0,4.0],[0.0,0.0]]]
                  }
                },
                {
                  "type": "Feature",
                  "properties": { "shapeName": "Serbia", "shapeID": "SRB-ADM0", "shapeGroup": "SRB", "shapeType": "ADM0" },
                  "geometry": {
                    "type": "Polygon",
                    "coordinates": [[[10.0,10.0],[14.0,10.0],[14.0,14.0],[10.0,14.0],[10.0,10.0]]]
                  }
                }
              ]
            }
            """);
        return adm0Path;
    }

    private static async Task<string> WriteSeedAdm1FileAsync(string tempRoot)
    {
        var adm1Path = Path.Combine(tempRoot, "local-adm1-seed-sync.geojson");
        await File.WriteAllTextAsync(adm1Path, """
            {
              "type": "FeatureCollection",
              "features": [
                {
                  "type": "Feature",
                  "properties": { "shapeName": "Dubrovnik-Neretva", "shapeID": "DUBROVNIK-NERETVA-1", "shapeGroup": "HRV", "shapeType": "ADM1" },
                  "geometry": {
                    "type": "Polygon",
                    "coordinates": [[[0.2,0.2],[0.8,0.2],[0.8,0.8],[0.2,0.8],[0.2,0.2]]]
                  }
                },
                {
                  "type": "Feature",
                  "properties": { "shapeName": "Istria", "shapeID": "ISTRIA-1", "shapeGroup": "HRV", "shapeType": "ADM1" },
                  "geometry": {
                    "type": "Polygon",
                    "coordinates": [[[1.2,0.2],[1.8,0.2],[1.8,0.8],[1.2,0.8],[1.2,0.2]]]
                  }
                },
                {
                  "type": "Feature",
                  "properties": { "shapeName": "Serbia North", "shapeID": "SERBIA-NORTH-1", "shapeGroup": "SRB", "shapeType": "ADM1" },
                  "geometry": {
                    "type": "Polygon",
                    "coordinates": [[[10.2,10.2],[10.8,10.2],[10.8,10.8],[10.2,10.8],[10.2,10.2]]]
                  }
                }
              ]
            }
            """);
        return adm1Path;
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
}
