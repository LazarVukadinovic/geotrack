using System.Net.Http.Json;
using Geotrack.Application.DTOs.Explore;
using Geotrack.Application.DTOs.Map;
using Geotrack.Domain.Entities.Core;
using Geotrack.Infrastructure;
using Geotrack.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Geotrack.IntegrationTests;

public sealed class PublicDestinationDiscoveryTests
{
    [Fact]
    public async Task Public_map_destinations_include_dubrovnik_and_serbian_seed_destinations_without_boundary_candidates()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        using var client = app.CreateClient();

        var destinations = await client.GetFromJsonAsync<List<MapSearchResultDto>>("/api/map/destinations");

        Assert.NotNull(destinations);
        Assert.Contains(destinations!, destination => destination.Name == "Dubrovnik");
        Assert.Contains(destinations!, destination => destination.Name == "Beograd");
        Assert.Contains(destinations!, destination => destination.Name == "Novi Sad");
        Assert.Contains(destinations!, destination => destination.Name == "Zlatibor");
        Assert.Contains(destinations!, destination => destination.Name == "Kopaonik");
        Assert.Contains(destinations!, destination => destination.Name == "Tara");
        Assert.Contains(destinations!, destination => destination.Name == "Kragujevac");

        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var croatia = await db.Territories.SingleAsync(territory => territory.CountryIso3 == "HRV");
        var dubrovnikRegion = await db.Destinations
            .AsNoTracking()
            .Where(destination => destination.Name == "Dubrovnik")
            .Select(destination => new
            {
                RegionName = destination.Region.Name,
                destination.Region.BoundaryShapeGroup,
                destination.Region.BoundaryAdmLevel,
                destination.Region.BoundarySource,
                destination.Region.TerritoryId
            })
            .SingleAsync();

        Assert.Equal(croatia.Id, dubrovnikRegion.TerritoryId);
        Assert.Equal("Dubrovnik-Neretva", dubrovnikRegion.RegionName);
        Assert.Equal("HRV", dubrovnikRegion.BoundaryShapeGroup);
        Assert.Equal("ADM1", dubrovnikRegion.BoundaryAdmLevel);
        Assert.Equal("seed-explicit", dubrovnikRegion.BoundarySource);

        var serbia = await db.Territories.SingleAsync(territory => territory.CountryIso3 == "SRB");
        var serbianDestinationRegions = await db.Destinations
            .AsNoTracking()
            .Where(destination => destination.Region.TerritoryId == serbia.Id)
            .Select(destination => new
            {
                DestinationName = destination.Name,
                RegionName = destination.Region.Name,
                destination.Region.BoundaryShapeGroup,
                destination.Region.BoundaryAdmLevel
            })
            .ToListAsync();

        Assert.Contains(serbianDestinationRegions, row =>
            row.DestinationName == "Kragujevac" &&
            row.RegionName == "Sumadija District" &&
            row.BoundaryShapeGroup == "SRB" &&
            row.BoundaryAdmLevel == "ADM1");
        Assert.Contains(serbianDestinationRegions, row =>
            row.DestinationName == "Novi Sad" &&
            row.RegionName == "South Backa District");
        Assert.Contains(serbianDestinationRegions, row =>
            row.DestinationName == "Kopaonik" &&
            row.RegionName == "Raska District");
        Assert.DoesNotContain(await db.Regions
                .Where(region => region.TerritoryId == serbia.Id)
                .Select(region => region.Name)
                .ToListAsync(),
            regionName => regionName is "Central Serbia" or "Vojvodina" or "Western Serbia");
    }

    [Fact]
    public async Task Public_content_seed_bootstraps_dubrovnik_and_croatia_adm1_region_without_demo_seed()
    {
        await using var app = await GeotrackApiFactory.CreateAsync(
            configurationOverrides: new Dictionary<string, string?>
            {
                ["Seed:EnableDemoContentSeed"] = "false",
                ["Seed:EnablePublicContentSeed"] = "true",
                ["Seed:ImportLocalBoundaries:Enabled"] = "false",
                ["Seed:SyncAdm1Regions:Enabled"] = "false"
            });
        using var client = app.CreateClient();

        var destinations = await client.GetFromJsonAsync<List<MapSearchResultDto>>("/api/map/destinations");

        Assert.NotNull(destinations);
        Assert.Contains(destinations!, destination => destination.Name == "Dubrovnik");
        Assert.Contains(destinations!, destination => destination.Name == "Kragujevac");

        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var dubrovnikRegion = await db.Destinations
            .AsNoTracking()
            .Where(destination => destination.Name == "Dubrovnik")
            .Select(destination => new
            {
                destination.Status,
                RegionName = destination.Region.Name,
                TerritoryIso3 = destination.Region.Territory.CountryIso3,
                destination.Region.BoundaryShapeGroup,
                destination.Region.BoundaryAdmLevel,
                destination.Region.BoundarySource
            })
            .SingleAsync();

        Assert.Equal("Published", dubrovnikRegion.Status);
        Assert.Equal("Dubrovnik-Neretva", dubrovnikRegion.RegionName);
        Assert.Equal("HRV", dubrovnikRegion.TerritoryIso3);
        Assert.Equal("HRV", dubrovnikRegion.BoundaryShapeGroup);
        Assert.Equal("ADM1", dubrovnikRegion.BoundaryAdmLevel);
        Assert.Equal("seed-explicit", dubrovnikRegion.BoundarySource);
    }

    [Fact]
    public async Task Public_map_destinations_exclude_unpublished_destinations_but_do_not_require_regional_manager_assignment()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();

        var visibleName = $"Unassigned Public Destination {Guid.NewGuid():N}";
        var hiddenName = $"Unpublished Destination {Guid.NewGuid():N}";

        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var territoryId = await db.Territories
                .OrderBy(territory => territory.Id)
                .Select(territory => territory.Id)
                .FirstAsync();
            var destinationTypeId = await db.DestinationTypes
                .OrderBy(type => type.Id)
                .Select(type => type.Id)
                .FirstAsync();
            var now = DateTime.UtcNow;
            var region = new Region
            {
                Name = $"Unassigned Discovery Region {Guid.NewGuid():N}",
                TerritoryId = territoryId,
                CreatedAt = now
            };

            db.Regions.Add(region);
            await db.SaveChangesAsync();

            db.Destinations.AddRange(
                new Destination
                {
                    Name = visibleName,
                    Status = "Published",
                    Description = "Published destination without an assigned regional manager.",
                    DestinationTypeId = destinationTypeId,
                    RegionId = region.Id,
                    Latitude = 44.05,
                    Longitude = 20.9,
                    CreatedAt = now,
                    PublishedAt = now
                },
                new Destination
                {
                    Name = hiddenName,
                    Status = "Draft",
                    Description = "Draft destination that must not be public.",
                    DestinationTypeId = destinationTypeId,
                    RegionId = region.Id,
                    Latitude = 44.06,
                    Longitude = 20.91,
                    CreatedAt = now
                });
            await db.SaveChangesAsync();

            Assert.False(await db.RegionalManagerRegions.AnyAsync(assignment => assignment.RegionId == region.Id));
        }

        using var client = app.CreateClient();
        var destinations = await client.GetFromJsonAsync<List<MapSearchResultDto>>("/api/map/destinations");

        Assert.NotNull(destinations);
        Assert.Contains(destinations!, destination => destination.Name == visibleName);
        Assert.DoesNotContain(destinations!, destination => destination.Name == hiddenName);
    }

    [Fact]
    public async Task Explore_destination_endpoint_returns_serbian_content_for_seeded_serbian_destination()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();

        int kragujevacId;
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            kragujevacId = await db.Destinations
                .Where(destination => destination.Name == "Kragujevac")
                .Select(destination => destination.Id)
                .SingleAsync();
        }

        using var client = app.CreateClient();
        var page = await client.GetFromJsonAsync<ExplorePageDto>($"/api/explore?destinationId={kragujevacId}");

        Assert.NotNull(page);
        Assert.Equal("Kragujevac", page!.SelectedDestination?.Name);
        var items = page.Sections.SelectMany(section => section.Items).ToList();
        Assert.NotEmpty(items);
        Assert.Contains(items, item =>
            item.Title.Contains("Kragujevac", StringComparison.OrdinalIgnoreCase) ||
            item.Title.Contains("Šumarice", StringComparison.OrdinalIgnoreCase) ||
            item.LocationName == "Centar");
    }

    [Fact]
    public async Task No_nearby_explore_result_can_still_use_public_destination_fallback()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        using var client = app.CreateClient();

        var explore = await client.GetFromJsonAsync<ExplorePageDto>("/api/explore?latitude=0&longitude=1");
        var destinations = await client.GetFromJsonAsync<List<MapSearchResultDto>>("/api/map/destinations");

        Assert.NotNull(explore);
        Assert.Null(explore!.SelectedDestination);
        Assert.NotNull(destinations);
        Assert.Contains(destinations!, destination => destination.Name == "Dubrovnik");
        Assert.Contains(destinations!, destination => destination.Name == "Kragujevac");
    }
}
