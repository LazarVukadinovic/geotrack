using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Geotrack.Application.Analytics;
using Geotrack.Application.DTOs.Admin;
using Geotrack.Application.DTOs.Auth;
using Geotrack.Application.Security;
using Geotrack.Domain.Entities.Analytics;
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
using NtsPolygon = NetTopologySuite.Geometries.Polygon;
using PrecisionModel = NetTopologySuite.Geometries.PrecisionModel;

namespace Geotrack.IntegrationTests;

public sealed class TerritoryDashboardTests
{
    private const string BoundaryGeometryPropertyName = "BoundaryGeometry";
    private const string BoundaryDisplayGeometryPropertyName = "BoundaryDisplayGeometry";
    private static readonly GeometryFactory GeometryFactory = new(new PrecisionModel(), 4326);

    [Fact]
    public async Task Territory_manager_can_load_dashboard_for_assigned_territory()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        var fixture = await CreateTerritoryDashboardFixtureAsync(app);

        using var client = await LoginAdminAsync(app, "mika@gmail.com", "TEST-only-Fixture-9284!");
        var dashboard = await client.GetFromJsonAsync<TerritoryDashboardDto>(
            $"/api/admin/territory/dashboard?territoryId={fixture.AssignedTerritoryId}");

        Assert.NotNull(dashboard);
        Assert.Equal(fixture.AssignedTerritoryId, dashboard!.ActiveTerritory!.Id);
        Assert.Equal(fixture.AssignedTerritoryName, dashboard.ActiveTerritory.Name);
    }

    [Fact]
    public async Task Territory_dashboard_rejects_anonymous_regional_manager_and_unassigned_territory()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        var fixture = await CreateTerritoryDashboardFixtureAsync(app);

        using var anonymousClient = app.CreateClient();
        var anonymous = await anonymousClient.GetAsync(
            $"/api/admin/territory/dashboard?territoryId={fixture.AssignedTerritoryId}");
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);

        using var regionalClient = await LoginAdminAsync(app, "elena@example.com", "password123");
        var regional = await regionalClient.GetAsync(
            $"/api/admin/territory/dashboard?territoryId={fixture.AssignedTerritoryId}");
        Assert.Equal(HttpStatusCode.Forbidden, regional.StatusCode);

        using var territoryClient = await LoginAdminAsync(app, "mika@gmail.com", "TEST-only-Fixture-9284!");
        var unassigned = await territoryClient.GetAsync(
            $"/api/admin/territory/dashboard?territoryId={fixture.UnassignedTerritoryId}");
        Assert.Equal(HttpStatusCode.Forbidden, unassigned.StatusCode);
    }

    [Fact]
    public async Task Dashboard_stats_regions_and_top_content_are_scoped_to_assigned_territory()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        var fixture = await CreateTerritoryDashboardFixtureAsync(app);

        using var client = await LoginAdminAsync(app, "mika@gmail.com", "TEST-only-Fixture-9284!");
        var dashboard = await client.GetFromJsonAsync<TerritoryDashboardDto>(
            $"/api/admin/territory/dashboard?territoryId={fixture.AssignedTerritoryId}");

        Assert.NotNull(dashboard);
        Assert.Equal(1, dashboard!.Stats.TotalRegions);
        Assert.Equal(0, dashboard.Stats.RegionsAddedLastMonth);
        Assert.Equal(2, dashboard.Stats.ActiveUsers);
        Assert.Equal(100, dashboard.Stats.ActiveUsersChangePercent);
        Assert.Equal(2, dashboard.Stats.TotalDestinations);
        Assert.Equal(1, dashboard.Stats.DestinationsAddedLastMonth);
        Assert.Equal(2, dashboard.Stats.TotalLocations);
        Assert.Equal(1, dashboard.Stats.LocationsAddedLastMonth);
        Assert.Equal(3, dashboard.Stats.TotalPlaces);
        Assert.Equal(1, dashboard.Stats.PlacesAddedLastMonth);
        Assert.Equal(2, dashboard.Stats.TotalEvents);
        Assert.Equal(1, dashboard.Stats.EventsAddedLastMonth);

        var health = Assert.Single(dashboard.RegionsHealth);
        Assert.Equal(fixture.AssignedRegionId, health.RegionId);
        Assert.Equal(2, health.DestinationsCount);
        Assert.Equal(2, health.LocationsCount);
        Assert.Equal(2, health.PlacesCount);
        Assert.Equal(1, health.StaysCount);
        Assert.Equal(2, health.EventsCount);
        Assert.Equal(1, health.PendingRequestsCount);
        Assert.InRange(health.HealthScore, 0, 100);
        Assert.Equal(health.HealthScore, health.Health.Score);
        Assert.Equal(health.HealthScore, health.ActivityScore);
        Assert.NotEmpty(health.Health.Drivers);
        Assert.InRange(health.ActivityScore, 0, 100);
        Assert.False(string.IsNullOrWhiteSpace(health.ManagerDisplayName));
        Assert.Contains(health.AssignedManagers, manager =>
            manager.AvatarUrl == "/media/uploads/avatars/elena-territory-test.png" &&
            !string.IsNullOrWhiteSpace(manager.Initials));
        Assert.Contains(health.AssignedManagers, manager =>
            manager.AvatarUrl is null &&
            !string.IsNullOrWhiteSpace(manager.Initials));

        var topRegion = Assert.Single(dashboard.TopPerformingRegions);
        Assert.Equal(fixture.AssignedRegionId, topRegion.RegionId);
        Assert.Equal(4, topRegion.VisitsThisMonth);
        Assert.Equal(300, topRegion.ChangePercent);

        Assert.Contains(dashboard.TopContent.TopDestinations, item =>
            item.Id == fixture.AssignedDestinationId && item.MetricValue == 1);
        Assert.DoesNotContain(dashboard.TopContent.TopDestinations, item =>
            item.Name.Contains("Out", StringComparison.OrdinalIgnoreCase));

        Assert.Contains(dashboard.TopContent.TopLocations, item =>
            item.Id == fixture.AssignedLocationId && item.MetricValue == 1);
        Assert.Contains(dashboard.TopContent.TopPlaces, item =>
            item.Id == fixture.AssignedPlaceId && item.MetricValue == 1);
        Assert.Contains(dashboard.TopContent.UpcomingEvents, item =>
            item.Id == fixture.UpcomingEventId && item.MetricValue == 1);
    }

    [Fact]
    public async Task Territory_dashboard_regions_health_returns_top_four_ranked_regions_only()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        var fixture = await CreateTerritoryDashboardFixtureAsync(app);
        var secondaryRegionId = await AddSparseContentRegionAsync(
            app,
            fixture.AssignedTerritoryId,
            "Secondary Health Region");
        await AddAdditionalRegionsAsync(app, fixture.AssignedTerritoryId, 30);

        using var client = await LoginAdminAsync(app, "mika@gmail.com", "TEST-only-Fixture-9284!");
        var dashboard = await client.GetFromJsonAsync<TerritoryDashboardDto>(
            $"/api/admin/territory/dashboard?territoryId={fixture.AssignedTerritoryId}");
        var regionalList = await client.GetFromJsonAsync<TerritoryRegionDirectoryResponseDto>(
            $"/api/admin/territory/regions?territoryId={fixture.AssignedTerritoryId}&sort=health-high&skip=0&take=100");

        Assert.NotNull(dashboard);
        Assert.NotNull(regionalList);
        Assert.Equal(4, dashboard!.RegionsHealth.Count);
        Assert.Equal(32, regionalList!.TotalCount);
        Assert.True(regionalList.Items.Count > dashboard.RegionsHealth.Count);
        Assert.Contains(dashboard.RegionsHealth, region => region.RegionId == fixture.AssignedRegionId);
        Assert.Contains(dashboard.RegionsHealth, region => region.RegionId == secondaryRegionId);
        Assert.Equal(
            regionalList.Items.Take(4).Select(region => region.RegionId),
            dashboard.RegionsHealth.Select(region => region.RegionId));

        var healthScores = dashboard.RegionsHealth.Select(region => region.HealthScore).ToList();
        Assert.Equal(healthScores.OrderByDescending(score => score), healthScores);
    }

    [Fact]
    public async Task Territory_dashboard_regions_health_uses_deterministic_tie_breakers_for_zero_score_regions()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        var territoryId = await CreateEmptyAssignedTerritoryAsync(app);
        await AddNamedRegionsAsync(
            app,
            territoryId,
            "Zulu Zero Region",
            "Alpha Zero Region",
            "Gamma Zero Region",
            "Beta Zero Region",
            "Delta Zero Region");

        using var client = await LoginAdminAsync(app, "mika@gmail.com", "TEST-only-Fixture-9284!");
        var dashboard = await client.GetFromJsonAsync<TerritoryDashboardDto>(
            $"/api/admin/territory/dashboard?territoryId={territoryId}");

        Assert.NotNull(dashboard);
        Assert.Equal(4, dashboard!.RegionsHealth.Count);
        Assert.Equal(
            new[] { "Alpha Zero Region", "Beta Zero Region", "Delta Zero Region", "Gamma Zero Region" },
            dashboard.RegionsHealth.Select(region => region.RegionName));
        Assert.All(dashboard.RegionsHealth, region => Assert.Equal(0, region.HealthScore));
    }

    [Fact]
    public async Task Territory_dashboard_regions_health_returns_all_regions_when_fewer_than_four_exist()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        var territoryId = await CreateEmptyAssignedTerritoryAsync(app);
        await AddNamedRegionsAsync(
            app,
            territoryId,
            "Alpha Small Region",
            "Beta Small Region",
            "Gamma Small Region");

        using var client = await LoginAdminAsync(app, "mika@gmail.com", "TEST-only-Fixture-9284!");
        var dashboard = await client.GetFromJsonAsync<TerritoryDashboardDto>(
            $"/api/admin/territory/dashboard?territoryId={territoryId}");

        Assert.NotNull(dashboard);
        Assert.Equal(3, dashboard!.RegionsHealth.Count);
        Assert.Equal(
            new[] { "Alpha Small Region", "Beta Small Region", "Gamma Small Region" },
            dashboard.RegionsHealth.Select(region => region.RegionName));
    }

    [Fact]
    public async Task Territory_directories_return_only_assigned_territory_data()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        var fixture = await CreateTerritoryDashboardFixtureAsync(app);

        using var client = await LoginAdminAsync(app, "mika@gmail.com", "TEST-only-Fixture-9284!");

        var regions = await client.GetFromJsonAsync<TerritoryRegionDirectoryResponseDto>(
            $"/api/admin/territory/regions?territoryId={fixture.AssignedTerritoryId}");
        Assert.NotNull(regions);
        var region = Assert.Single(regions!.Items);
        Assert.Equal(fixture.AssignedRegionId, region.Id);
        Assert.Equal(2, region.DestinationsCount);
        Assert.Equal(1, region.StaysCount);
        Assert.NotNull(region.Health);
        Assert.Equal(region.HealthScore, region.Health!.Score);
        Assert.Equal(region.HealthScore, region.ActivityScore);
        Assert.Contains(region.AssignedManagers, manager =>
            manager.AvatarUrl == "/media/uploads/avatars/elena-territory-test.png");

        var destinations = await client.GetFromJsonAsync<TerritoryContentDirectoryResponseDto>(
            $"/api/admin/territory/content?territoryId={fixture.AssignedTerritoryId}&type=destinations");
        Assert.NotNull(destinations);
        Assert.Contains(destinations!.Items, item => item.Id == fixture.AssignedDestinationId);
        Assert.DoesNotContain(destinations.Items, item => item.Name.Contains("Out", StringComparison.OrdinalIgnoreCase));

        var regionalMutation = await client.PostAsJsonAsync(
            "/api/admin/regional/destinations",
            new { regionId = fixture.AssignedRegionId });
        Assert.Equal(HttpStatusCode.Forbidden, regionalMutation.StatusCode);
    }

    [Fact]
    public async Task Territory_region_list_returns_destination_representative_image_from_content()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        var fixture = await CreateTerritoryDashboardFixtureAsync(app);

        await AddEntityImageAsync(
            app,
            EntityImageTargetType.Destination,
            fixture.AssignedDestinationId,
            "/media/test/region-destination-gallery.svg",
            isMain: false,
            imageRole: EntityImageRole.Gallery,
            sortOrder: 0);
        await AddEntityImageAsync(
            app,
            EntityImageTargetType.Destination,
            fixture.AssignedDestinationId,
            "/media/test/region-destination-main.svg",
            isMain: true,
            imageRole: EntityImageRole.Cover,
            sortOrder: 10,
            altText: "Destination main image");

        using var client = await LoginAdminAsync(app, "mika@gmail.com", "TEST-only-Fixture-9284!");
        var regions = await client.GetFromJsonAsync<TerritoryRegionDirectoryResponseDto>(
            $"/api/admin/territory/regions?territoryId={fixture.AssignedTerritoryId}");

        Assert.NotNull(regions);
        var region = Assert.Single(regions!.Items);
        Assert.NotNull(region.RepresentativeImage);
        Assert.Equal("/media/test/region-destination-main.svg", region.RepresentativeImage!.Url);
        Assert.Equal("Destination main image", region.RepresentativeImage.AltText);
        Assert.Equal("Destination", region.RepresentativeImage.SourceType);
        Assert.Equal(fixture.AssignedDestinationId, region.RepresentativeImage.SourceId);
        Assert.Equal(region.RepresentativeImage.Url, region.ImageUrl);
        Assert.Equal(2, region.DestinationsCount);
        Assert.Equal(1, region.StaysCount);
        Assert.NotNull(region.Health);
    }

    [Fact]
    public async Task Territory_region_list_uses_place_image_when_no_destination_image_exists()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        var fixture = await CreateTerritoryDashboardFixtureAsync(app);

        await AddEntityImageAsync(
            app,
            EntityImageTargetType.Place,
            fixture.AssignedPlaceId,
            "/media/test/region-place-main.svg",
            isMain: true,
            imageRole: EntityImageRole.Cover,
            sortOrder: 0,
            altText: "Place main image");

        using var client = await LoginAdminAsync(app, "mika@gmail.com", "TEST-only-Fixture-9284!");
        var regions = await client.GetFromJsonAsync<TerritoryRegionDirectoryResponseDto>(
            $"/api/admin/territory/regions?territoryId={fixture.AssignedTerritoryId}");

        Assert.NotNull(regions);
        var region = Assert.Single(regions!.Items);
        Assert.NotNull(region.RepresentativeImage);
        Assert.Equal("/media/test/region-place-main.svg", region.RepresentativeImage!.Url);
        Assert.Equal("Place", region.RepresentativeImage.SourceType);
        Assert.Equal(fixture.AssignedPlaceId, region.RepresentativeImage.SourceId);
    }

    [Fact]
    public async Task Territory_region_list_returns_null_representative_image_when_no_entity_images_exist()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        var (territoryId, regionId) = await CreateEmptyAssignedRegionAsync(app);

        using var client = await LoginAdminAsync(app, "mika@gmail.com", "TEST-only-Fixture-9284!");
        var regions = await client.GetFromJsonAsync<TerritoryRegionDirectoryResponseDto>(
            $"/api/admin/territory/regions?territoryId={territoryId}");

        Assert.NotNull(regions);
        var region = Assert.Single(regions!.Items);
        Assert.Equal(regionId, region.RegionId);
        Assert.Null(region.RepresentativeImage);
        Assert.Null(region.ImageUrl);
    }

    [Fact]
    public async Task Territory_region_list_ignores_invalid_or_suspended_source_images()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        var fixture = await CreateTerritoryDashboardFixtureAsync(app);

        await SetDestinationStatusAsync(app, fixture.AssignedDestinationId, "Suspended");
        await AddEntityImageAsync(
            app,
            EntityImageTargetType.Destination,
            fixture.AssignedDestinationId,
            "/media/test/region-suspended-destination.svg",
            isMain: true,
            imageRole: EntityImageRole.Cover,
            sortOrder: 0);
        await AddEntityImageAsync(
            app,
            EntityImageTargetType.Location,
            fixture.AssignedLocationId,
            " ",
            isMain: true,
            imageRole: EntityImageRole.Cover,
            sortOrder: 0);
        await AddEntityImageAsync(
            app,
            EntityImageTargetType.Place,
            fixture.AssignedPlaceId,
            "/media/test/region-valid-place.svg",
            isMain: true,
            imageRole: EntityImageRole.Cover,
            sortOrder: 0);

        using var client = await LoginAdminAsync(app, "mika@gmail.com", "TEST-only-Fixture-9284!");
        var regions = await client.GetFromJsonAsync<TerritoryRegionDirectoryResponseDto>(
            $"/api/admin/territory/regions?territoryId={fixture.AssignedTerritoryId}");

        Assert.NotNull(regions);
        var region = Assert.Single(regions!.Items);
        Assert.NotNull(region.RepresentativeImage);
        Assert.Equal("/media/test/region-valid-place.svg", region.RepresentativeImage!.Url);
        Assert.Equal("Place", region.RepresentativeImage.SourceType);
        Assert.Equal(fixture.AssignedPlaceId, region.RepresentativeImage.SourceId);
    }

    [Fact]
    public async Task Territory_region_map_returns_real_region_boundaries_assignments_and_counts()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        var fixture = await CreateTerritoryRegionMapFixtureAsync(app);

        using var client = await LoginAdminAsync(app, "mika@gmail.com", "TEST-only-Fixture-9284!");
        var map = await client.GetFromJsonAsync<TerritoryRegionMapDto>(
            $"/api/admin/territory/regions/map?territoryId={fixture.AssignedTerritoryId}");

        Assert.NotNull(map);
        Assert.Equal(fixture.AssignedTerritoryId, map!.Territory!.TerritoryId);
        Assert.Equal("TMA", map.Territory.CountryIso3);
        Assert.True(map.Territory.HasBoundary);
        Assert.Equal("Territory.BoundaryGeometry", map.Territory.GeometrySource);
        Assert.NotNull(map.Territory.GeometryGeoJson);
        Assert.Contains("[0,0]", CoordinatesJson(map.Territory.GeometryGeoJson));
        Assert.DoesNotContain("[100,100]", CoordinatesJson(map.Territory.GeometryGeoJson));
        Assert.False(map.Territory.Viewport.IsEmpty);
        Assert.False(map.Viewport.IsEmpty);

        Assert.Equal(3, map.Regions.Count);
        Assert.DoesNotContain(map.Regions, region => region.RegionId == fixture.OtherTerritoryRegionId);
        Assert.DoesNotContain(map.Regions, region => region.Name == "Candidate Only ADM1");

        var assigned = Assert.Single(map.Regions, region => region.RegionId == fixture.AssignedRegionId);
        Assert.Equal("Active", assigned.Status);
        Assert.True(assigned.HasBoundary);
        Assert.Equal("Region.BoundaryGeometry", assigned.GeometrySource);
        Assert.NotNull(assigned.GeometryGeoJson);
        Assert.Contains("[0.2,0.2]", CoordinatesJson(assigned.GeometryGeoJson));
        Assert.DoesNotContain("[8.2,8.2]", CoordinatesJson(assigned.GeometryGeoJson));
        Assert.NotNull(assigned.CenterLatitude);
        Assert.NotNull(assigned.CenterLongitude);
        Assert.Single(assigned.AssignedManagers, manager =>
            manager.Email == "elena@example.com" &&
            manager.Role == RoleNames.RegionalManager);
        Assert.Equal(1, assigned.ContentCounts.Destinations);
        Assert.Equal(1, assigned.ContentCounts.Locations);
        Assert.Equal(1, assigned.ContentCounts.Places);
        Assert.Equal(0, assigned.ContentCounts.Stays);
        Assert.Equal(1, assigned.ContentCounts.Events);
        Assert.Equal(1, assigned.ContentCounts.Pending);
        Assert.InRange(assigned.HealthScore ?? 0, 0, 100);
        Assert.Equal($"/territory/regions/{fixture.AssignedRegionId}", assigned.DetailRoute);

        var unassigned = Assert.Single(map.Regions, region => region.RegionId == fixture.UnassignedRegionId);
        Assert.Equal("Inactive", unassigned.Status);
        Assert.Empty(unassigned.AssignedManagers);
        Assert.True(unassigned.HasBoundary);
        Assert.Equal("Region.BoundaryGeometry", unassigned.GeometrySource);

        var missingBoundary = Assert.Single(map.Regions, region => region.RegionId == fixture.MissingBoundaryRegionId);
        Assert.False(missingBoundary.HasBoundary);
        Assert.Null(missingBoundary.GeometrySource);
        Assert.Null(missingBoundary.GeometryGeoJson);
        Assert.True(missingBoundary.Viewport.IsEmpty);
        Assert.Equal(
            "Full region boundary is missing. Display geometry exists but is not used on this accuracy map.",
            missingBoundary.BoundaryMissingReason);

        Assert.Equal("Boundary data is missing for this territory or some regions.", map.BoundaryWarningMessage);
    }

    [Fact]
    public async Task Territory_region_map_authorization_is_scoped_to_assigned_territory()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        var fixture = await CreateTerritoryRegionMapFixtureAsync(app);

        using var anonymousClient = app.CreateClient();
        var anonymous = await anonymousClient.GetAsync(
            $"/api/admin/territory/regions/map?territoryId={fixture.AssignedTerritoryId}");
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);

        using var regionalClient = await LoginAdminAsync(app, "elena@example.com", "password123");
        var regional = await regionalClient.GetAsync(
            $"/api/admin/territory/regions/map?territoryId={fixture.AssignedTerritoryId}");
        Assert.Equal(HttpStatusCode.Forbidden, regional.StatusCode);

        using var territoryClient = await LoginAdminAsync(app, "mika@gmail.com", "TEST-only-Fixture-9284!");
        var unassigned = await territoryClient.GetAsync(
            $"/api/admin/territory/regions/map?territoryId={fixture.OtherTerritoryId}");
        Assert.Equal(HttpStatusCode.Forbidden, unassigned.StatusCode);
    }

    [Fact]
    public async Task Territory_region_map_returns_safe_empty_response_when_no_regions_exist()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        var territoryId = await CreateEmptyAssignedTerritoryAsync(app);

        using var client = await LoginAdminAsync(app, "mika@gmail.com", "TEST-only-Fixture-9284!");
        var map = await client.GetFromJsonAsync<TerritoryRegionMapDto>(
            $"/api/admin/territory/regions/map?territoryId={territoryId}");

        Assert.NotNull(map);
        Assert.Equal(territoryId, map!.Territory!.TerritoryId);
        Assert.Empty(map.Regions);
        Assert.Equal("No regions are available for this territory. Sync ADM1 regions first.", map.EmptyStateMessage);
        Assert.Equal("Boundary data is missing for this territory or some regions.", map.BoundaryWarningMessage);
    }

    [Fact]
    public async Task Territory_region_list_supports_pagination_search_and_sorting()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        var fixture = await CreateTerritoryDashboardFixtureAsync(app);
        await AddAdditionalRegionsAsync(app, fixture.AssignedTerritoryId, 30);

        using var client = await LoginAdminAsync(app, "mika@gmail.com", "TEST-only-Fixture-9284!");

        var firstPage = await client.GetFromJsonAsync<TerritoryRegionDirectoryResponseDto>(
            $"/api/admin/territory/regions?territoryId={fixture.AssignedTerritoryId}&skip=0&take=25");
        Assert.NotNull(firstPage);
        Assert.Equal(25, firstPage!.Items.Count);
        Assert.Equal(0, firstPage.Skip);
        Assert.Equal(25, firstPage.Take);
        Assert.True(firstPage.HasMore);

        var secondPage = await client.GetFromJsonAsync<TerritoryRegionDirectoryResponseDto>(
            $"/api/admin/territory/regions?territoryId={fixture.AssignedTerritoryId}&skip=25&take=25");
        Assert.NotNull(secondPage);
        Assert.NotEmpty(secondPage!.Items);
        Assert.DoesNotContain(secondPage.Items, item => firstPage.Items.Any(first => first.RegionId == item.RegionId));

        var search = await client.GetFromJsonAsync<TerritoryRegionDirectoryResponseDto>(
            $"/api/admin/territory/regions?territoryId={fixture.AssignedTerritoryId}&search=Scoped%20Territory%20Region&skip=0&take=25");
        Assert.NotNull(search);
        var searchedRegion = Assert.Single(search!.Items);
        Assert.Equal(fixture.AssignedRegionId, searchedRegion.RegionId);

        var sorted = await client.GetFromJsonAsync<TerritoryRegionDirectoryResponseDto>(
            $"/api/admin/territory/regions?territoryId={fixture.AssignedTerritoryId}&sort=most-destinations&skip=0&take=5");
        Assert.NotNull(sorted);
        Assert.Equal(fixture.AssignedRegionId, sorted!.Items.First().RegionId);
        Assert.Equal(2, sorted.Items.First().DestinationsCount);
    }

    [Fact]
    public async Task Territory_manager_cannot_list_or_view_unassigned_region_data()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        var fixture = await CreateTerritoryDashboardFixtureAsync(app);

        using var client = await LoginAdminAsync(app, "mika@gmail.com", "TEST-only-Fixture-9284!");

        var list = await client.GetAsync($"/api/admin/territory/regions?territoryId={fixture.UnassignedTerritoryId}");
        Assert.Equal(HttpStatusCode.Forbidden, list.StatusCode);

        var detail = await client.GetAsync(
            $"/api/admin/territory/regions/{fixture.UnassignedRegionId}/detail?territoryId={fixture.AssignedTerritoryId}");
        Assert.Equal(HttpStatusCode.Forbidden, detail.StatusCode);

        var content = await client.GetAsync(
            $"/api/admin/territory/regions/{fixture.UnassignedRegionId}/content?territoryId={fixture.AssignedTerritoryId}&entityType=destinations");
        Assert.Equal(HttpStatusCode.Forbidden, content.StatusCode);
    }

    [Fact]
    public async Task Region_detail_returns_summary_previews_and_scoped_content_lists()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        var fixture = await CreateTerritoryDashboardFixtureAsync(app);

        using var client = await LoginAdminAsync(app, "mika@gmail.com", "TEST-only-Fixture-9284!");

        var detail = await client.GetFromJsonAsync<TerritoryRegionDetailDto>(
            $"/api/admin/territory/regions/{fixture.AssignedRegionId}/detail?territoryId={fixture.AssignedTerritoryId}");

        Assert.NotNull(detail);
        Assert.Equal(fixture.AssignedRegionId, detail!.Region.Id);
        Assert.Equal(fixture.AssignedTerritoryId, detail.Region.TerritoryId);
        Assert.Equal(2, detail.Summary.DestinationsCount);
        Assert.Equal(2, detail.Summary.LocationsCount);
        Assert.Equal(2, detail.Summary.PlacesCount);
        Assert.Equal(1, detail.Summary.StaysCount);
        Assert.Equal(2, detail.Summary.EventsCount);
        Assert.Equal(1, detail.Summary.PendingRequestsCount);
        Assert.NotEmpty(detail.Managers);
        Assert.Contains(detail.Managers, manager =>
            manager.AvatarUrl == "/media/uploads/avatars/elena-territory-test.png" &&
            !string.IsNullOrWhiteSpace(manager.Initials));
        Assert.Contains(detail.Managers, manager => manager.AvatarUrl is null);
        Assert.InRange(detail.Health.Score ?? 0, 0, 100);
        Assert.NotEmpty(detail.Content.Destinations);
        Assert.NotEmpty(detail.Content.Locations);
        Assert.NotEmpty(detail.Content.Places);
        Assert.NotEmpty(detail.Content.Stays);
        Assert.NotEmpty(detail.Content.Events);
        Assert.Contains(detail.Analytics.TopEntities, item => item.EntityId == fixture.AssignedDestinationId);
        Assert.Contains(detail.Content.Destinations, item =>
            item.Id == fixture.AssignedDestinationId &&
            item.PublicDetailRoute == $"/explore?destinationId={fixture.AssignedDestinationId}");
        Assert.Contains(detail.Content.Locations, item =>
            item.Id == fixture.AssignedLocationId &&
            item.PublicDetailRoute is null);
        Assert.Contains(detail.Content.Places, item =>
            item.Id == fixture.AssignedPlaceId &&
            item.PublicDetailRoute == $"/places/{fixture.AssignedPlaceId}");
        Assert.Contains(detail.Content.Stays, item =>
            item.Id == fixture.AssignedStayPlaceId &&
            item.PublicDetailRoute == $"/stays/{fixture.AssignedStayPlaceId}");
        Assert.Contains(detail.Content.Events, item =>
            item.Id == fixture.UpcomingEventId &&
            item.PublicDetailRoute == $"/events/{fixture.UpcomingEventId}");

        var dashboard = await client.GetFromJsonAsync<TerritoryDashboardDto>(
            $"/api/admin/territory/dashboard?territoryId={fixture.AssignedTerritoryId}");
        var dashboardHealth = Assert.Single(dashboard!.RegionsHealth);
        var regionList = await client.GetFromJsonAsync<TerritoryRegionDirectoryResponseDto>(
            $"/api/admin/territory/regions?territoryId={fixture.AssignedTerritoryId}");
        var regionRow = Assert.Single(regionList!.Items);
        var analytics = await client.GetFromJsonAsync<TerritoryAnalyticsOverviewDto>(
            $"/api/admin/territory/analytics/overview?territoryId={fixture.AssignedTerritoryId}&period=1m&take=5");
        var analyticsRow = Assert.Single(analytics!.RegionalPerformance.Items);

        Assert.Equal(dashboardHealth.HealthScore, regionRow.HealthScore);
        Assert.Equal(dashboardHealth.HealthScore, detail.Health.Score);
        Assert.Equal(dashboardHealth.HealthScore, analyticsRow.HealthScore);
        Assert.Equal(analyticsRow.HealthScore, analyticsRow.QualityScore);
        Assert.Equal(dashboardHealth.HealthScore, analytics.TopRegion!.HealthScore);

        var destination = await AssertRegionContentContainsAsync(
            client,
            fixture.AssignedTerritoryId,
            fixture.AssignedRegionId,
            "destinations",
            fixture.AssignedDestinationId);
        var location = await AssertRegionContentContainsAsync(
            client,
            fixture.AssignedTerritoryId,
            fixture.AssignedRegionId,
            "locations",
            fixture.AssignedLocationId);
        var place = await AssertRegionContentContainsAsync(
            client,
            fixture.AssignedTerritoryId,
            fixture.AssignedRegionId,
            "places",
            fixture.AssignedPlaceId);
        var stay = await AssertRegionContentContainsAsync(
            client,
            fixture.AssignedTerritoryId,
            fixture.AssignedRegionId,
            "stays",
            fixture.AssignedStayPlaceId);
        var @event = await AssertRegionContentContainsAsync(
            client,
            fixture.AssignedTerritoryId,
            fixture.AssignedRegionId,
            "events",
            fixture.UpcomingEventId);

        Assert.Equal($"/explore?destinationId={fixture.AssignedDestinationId}", destination.PublicDetailRoute);
        Assert.Null(location.PublicDetailRoute);
        Assert.Equal($"/places/{fixture.AssignedPlaceId}", place.PublicDetailRoute);
        Assert.Equal($"/stays/{fixture.AssignedStayPlaceId}", stay.PublicDetailRoute);
        Assert.Equal($"/events/{fixture.UpcomingEventId}", @event.PublicDetailRoute);
        Assert.All(new[] { destination, place, stay, @event }, item =>
            Assert.DoesNotContain("/territory", item.PublicDetailRoute, StringComparison.OrdinalIgnoreCase));
        Assert.All(new[] { destination, place, stay, @event }, item =>
            Assert.DoesNotContain("/admin", item.PublicDetailRoute, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Destination_health_recommendations_are_generated_from_real_missing_data()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        var fixture = await CreateTerritoryDashboardFixtureAsync(app);

        using var client = await LoginAdminAsync(app, "mika@gmail.com", "TEST-only-Fixture-9284!");
        var content = await client.GetFromJsonAsync<TerritoryRegionContentResponseDto>(
            $"/api/admin/territory/regions/{fixture.AssignedRegionId}/content?territoryId={fixture.AssignedTerritoryId}&entityType=destinations&skip=0&take=25");

        Assert.NotNull(content);
        var destination = Assert.Single(content!.Items, item => item.Id == fixture.AssignedDestinationId);
        Assert.NotNull(destination.Health);
        Assert.InRange(destination.Health!.Score ?? 0, 0, 100);
        Assert.Contains(destination.Health.Issues, issue =>
            issue.Label.Contains("Missing boundary", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(destination.Health.Issues, issue =>
            issue.Label.Contains("No reviews", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(destination.Health.Recommendations, recommendation =>
            recommendation.Detail.Contains("boundary", StringComparison.OrdinalIgnoreCase) ||
            recommendation.Detail.Contains("reviews", StringComparison.OrdinalIgnoreCase));

        var noViewsDestination = Assert.Single(content.Items, item =>
            item.Name.Contains("Recent Territory Destination", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(noViewsDestination.Health!.Issues, issue =>
            issue.Label.Contains("No recent views", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(noViewsDestination.Health!.Issues, issue =>
            issue.Label.Contains("Pending requests", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(noViewsDestination.Health!.Recommendations, recommendation =>
            recommendation.Detail.Contains("pending", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Empty_region_detail_returns_safe_zero_and_empty_dtos()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        var (territoryId, regionId) = await CreateEmptyAssignedRegionAsync(app);

        using var client = await LoginAdminAsync(app, "mika@gmail.com", "TEST-only-Fixture-9284!");
        var detail = await client.GetFromJsonAsync<TerritoryRegionDetailDto>(
            $"/api/admin/territory/regions/{regionId}/detail?territoryId={territoryId}");

        Assert.NotNull(detail);
        Assert.Equal(0, detail!.Summary.DestinationsCount);
        Assert.Equal(0, detail.Summary.LocationsCount);
        Assert.Equal(0, detail.Summary.PlacesCount);
        Assert.Equal(0, detail.Summary.StaysCount);
        Assert.Equal(0, detail.Summary.EventsCount);
        Assert.Equal(0, detail.Summary.PendingRequestsCount);
        Assert.Empty(detail.Content.Destinations);
        Assert.Empty(detail.Content.Locations);
        Assert.Empty(detail.Content.Places);
        Assert.Empty(detail.Content.Stays);
        Assert.Empty(detail.Content.Events);
        Assert.Equal(0, detail.Health.Score);
        Assert.Equal("Critical", detail.Health.StatusLabel);
        Assert.Contains(detail.Health.Issues, issue =>
            issue.Label.Contains("No regional content", StringComparison.OrdinalIgnoreCase));

        var content = await client.GetFromJsonAsync<TerritoryRegionContentResponseDto>(
            $"/api/admin/territory/regions/{regionId}/content?territoryId={territoryId}&entityType=destinations&skip=0&take=25");
        Assert.NotNull(content);
        Assert.Empty(content!.Items);
        Assert.Equal(0, content.TotalCount);
        Assert.False(content.HasMore);
    }

    [Fact]
    public async Task Empty_assigned_territory_returns_safe_zero_dashboard()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        var territoryId = await CreateEmptyAssignedTerritoryAsync(app);

        using var client = await LoginAdminAsync(app, "mika@gmail.com", "TEST-only-Fixture-9284!");
        var dashboard = await client.GetFromJsonAsync<TerritoryDashboardDto>(
            $"/api/admin/territory/dashboard?territoryId={territoryId}");

        Assert.NotNull(dashboard);
        Assert.Equal(territoryId, dashboard!.ActiveTerritory!.Id);
        Assert.Equal(0, dashboard.Stats.TotalRegions);
        Assert.Equal(0, dashboard.Stats.ActiveUsers);
        Assert.Equal(0, dashboard.Stats.TotalDestinations);
        Assert.Equal(0, dashboard.Stats.TotalLocations);
        Assert.Equal(0, dashboard.Stats.TotalPlaces);
        Assert.Equal(0, dashboard.Stats.TotalEvents);
        Assert.Empty(dashboard.RegionsHealth);
        Assert.Empty(dashboard.TopPerformingRegions);
        Assert.Empty(dashboard.TopContent.TopDestinations);
        Assert.Empty(dashboard.TopContent.TopLocations);
        Assert.Empty(dashboard.TopContent.TopPlaces);
        Assert.Empty(dashboard.TopContent.UpcomingEvents);
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

    private static async Task SetDestinationStatusAsync(GeotrackApiFactory app, int destinationId, string status)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var destination = await db.Destinations.SingleAsync(item => item.Id == destinationId);
        destination.Status = status;
        await db.SaveChangesAsync();
    }

    private static async Task AddEntityImageAsync(
        GeotrackApiFactory app,
        EntityImageTargetType targetType,
        int entityId,
        string url,
        bool isMain,
        string imageRole,
        int sortOrder,
        string? altText = null)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var image = new EntityImage
        {
            TargetType = targetType,
            Url = url,
            AltText = altText,
            IsMain = isMain,
            ImageRole = imageRole,
            SortOrder = sortOrder,
            CreatedAt = DateTime.UtcNow.AddMilliseconds(sortOrder)
        };

        switch (targetType)
        {
            case EntityImageTargetType.Destination:
                image.DestinationId = entityId;
                break;
            case EntityImageTargetType.Location:
                image.LocationId = entityId;
                break;
            case EntityImageTargetType.Place:
                image.PlaceId = entityId;
                break;
            case EntityImageTargetType.Event:
                image.EventId = entityId;
                break;
            default:
                throw new InvalidOperationException("Unsupported representative image test target.");
        }

        db.EntityImages.Add(image);
        await db.SaveChangesAsync();
    }

    private static async Task<TerritoryRegionMapFixture> CreateTerritoryRegionMapFixtureAsync(GeotrackApiFactory app)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var now = DateTime.UtcNow;
        var destinationType = await db.DestinationTypes.OrderBy(existing => existing.Id).FirstAsync();
        var locationType = await db.LocationTypes.OrderBy(existing => existing.Id).FirstAsync();
        var placeType = await db.PlaceTypes.OrderBy(existing => existing.Id).FirstAsync();
        var eventType = await db.EventTypes.OrderBy(existing => existing.Id).FirstAsync();
        var territoryManager = await db.Users.SingleAsync(user => user.Email == "mika@gmail.com");
        var regionalManager = await db.Users.SingleAsync(user => user.Email == "elena@example.com");
        var contributor = await db.Users.SingleAsync(user => user.Email == "marko.p@example.com");

        var assignedTerritory = new Territory
        {
            Name = $"Territory Map Assigned {Guid.NewGuid():N}",
            TerritoryKind = TerritoryKinds.Country,
            CountryCode = "TM",
            CountryIso3 = "TMA",
            BoundaryShapeType = "Polygon",
            BoundarySource = "geoBoundaries",
            BoundaryAdmLevel = "ADM0",
            CreatedAt = now
        };
        var otherTerritory = new Territory
        {
            Name = $"Territory Map Other {Guid.NewGuid():N}",
            TerritoryKind = TerritoryKinds.Country,
            CountryCode = "TO",
            CountryIso3 = "TMO",
            BoundaryShapeType = "Polygon",
            BoundarySource = "geoBoundaries",
            BoundaryAdmLevel = "ADM0",
            CreatedAt = now
        };

        db.Territories.AddRange(assignedTerritory, otherTerritory);
        SetBoundary(db, assignedTerritory, Polygon(0, 0, 4, 4));
        SetDisplayBoundary(db, assignedTerritory, Polygon(100, 100, 101, 101));
        SetBoundary(db, otherTerritory, Polygon(10, 10, 14, 14));
        await db.SaveChangesAsync();

        db.TerritoryManagerTerritories.Add(new TerritoryManagerTerritory
        {
            UserId = territoryManager.Id,
            TerritoryId = assignedTerritory.Id,
            CreatedAt = now
        });

        var candidateOnly = new AdministrativeBoundaryCandidate
        {
            Source = "geoBoundaries",
            ReleaseType = "gbOpen",
            AdmLevel = "ADM1",
            ShapeGroup = "TMA",
            ShapeId = $"candidate-only-{Guid.NewGuid():N}",
            ShapeName = "Candidate Only ADM1",
            ShapeType = "Polygon",
            ExternalKey = $"TMA-ADM1-candidate-only-{Guid.NewGuid():N}",
            CountryIso3 = "TMA",
            CountryTerritoryId = assignedTerritory.Id,
            ImportedAt = now,
            CreatedAt = now
        };
        db.AdministrativeBoundaryCandidates.Add(candidateOnly);
        SetBoundary(db, candidateOnly, Polygon(2.8, 2.8, 3.2, 3.2));
        SetDisplayBoundary(db, candidateOnly, Polygon(2.9, 2.9, 3.1, 3.1));

        var assignedRegion = new Region
        {
            Name = "Assigned Map Region",
            TerritoryId = assignedTerritory.Id,
            BoundaryShapeType = "Polygon",
            BoundarySource = "geoBoundaries",
            BoundaryAdmLevel = "ADM1",
            CreatedAt = now
        };
        var unassignedRegion = new Region
        {
            Name = "Unassigned Map Region",
            TerritoryId = assignedTerritory.Id,
            BoundaryShapeType = "Polygon",
            BoundarySource = "geoBoundaries",
            BoundaryAdmLevel = "ADM1",
            CreatedAt = now
        };
        var missingBoundaryRegion = new Region
        {
            Name = "Missing Boundary Map Region",
            TerritoryId = assignedTerritory.Id,
            CreatedAt = now
        };
        var otherRegion = new Region
        {
            Name = "Other Territory Map Region",
            TerritoryId = otherTerritory.Id,
            BoundaryShapeType = "Polygon",
            BoundarySource = "geoBoundaries",
            BoundaryAdmLevel = "ADM1",
            CreatedAt = now
        };

        db.Regions.AddRange(assignedRegion, unassignedRegion, missingBoundaryRegion, otherRegion);
        SetBoundary(db, assignedRegion, Polygon(0.2, 0.2, 1.2, 1.2));
        SetDisplayBoundary(db, assignedRegion, Polygon(8.2, 8.2, 9.2, 9.2));
        SetBoundary(db, unassignedRegion, Polygon(1.4, 0.2, 2.4, 1.2));
        SetDisplayBoundary(db, unassignedRegion, Polygon(9.4, 8.2, 10.4, 9.2));
        SetDisplayBoundary(db, missingBoundaryRegion, Polygon(2.6, 0.2, 3.6, 1.2));
        SetBoundary(db, otherRegion, Polygon(10.2, 10.2, 11.2, 11.2));
        await db.SaveChangesAsync();

        await GrantRegionalManagerCoverageAsync(db, regionalManager, assignedRegion.Id);

        var destination = new Destination
        {
            Name = "Map Scoped Destination",
            DestinationTypeId = destinationType.Id,
            RegionId = assignedRegion.Id,
            Description = "Destination inside map scoped region.",
            Latitude = 0.5,
            Longitude = 0.5,
            CreatedAt = now
        };
        db.Destinations.Add(destination);
        await db.SaveChangesAsync();

        var location = new Location
        {
            Name = "Map Scoped Location",
            DestinationId = destination.Id,
            LocationTypeId = locationType.Id,
            Latitude = 0.55,
            Longitude = 0.55,
            CreatedAt = now
        };
        db.Locations.Add(location);
        await db.SaveChangesAsync();

        var place = new Place
        {
            Name = "Map Scoped Place",
            PlaceTypeId = placeType.Id,
            LocationId = location.Id,
            Description = "Place inside map scoped region.",
            Latitude = 0.6,
            Longitude = 0.6,
            CreatedAt = now
        };
        db.Places.Add(place);

        db.Events.Add(new Event
        {
            Name = "Map Scoped Event",
            EventTypeId = eventType.Id,
            DestinationId = destination.Id,
            LocationId = location.Id,
            Description = "Event inside map scoped region.",
            StartDate = now.AddDays(5),
            EndDate = now.AddDays(6),
            CreatedAt = now
        });

        db.PlaceContributions.Add(new PlaceContribution
        {
            Type = PlaceContributionType.CreatePlace,
            Status = PlaceContributionStatus.PendingReview,
            UserId = contributor.Id,
            PlaceTypeId = placeType.Id,
            LocationId = location.Id,
            Name = "Map Scoped Pending Contribution",
            Description = "Pending contribution for map scoped region.",
            Latitude = 0.62,
            Longitude = 0.62,
            SubmittedAt = now,
            CreatedAt = now
        });

        await db.SaveChangesAsync();

        return new TerritoryRegionMapFixture(
            assignedTerritory.Id,
            otherTerritory.Id,
            assignedRegion.Id,
            unassignedRegion.Id,
            missingBoundaryRegion.Id,
            otherRegion.Id);
    }

    private static async Task<TerritoryDashboardFixture> CreateTerritoryDashboardFixtureAsync(GeotrackApiFactory app)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var now = DateTime.UtcNow;
        var oldDate = now.AddDays(-45);
        var recentDate = now.AddDays(-7);
        var previousDate = now.AddDays(-40);

        var destinationType = await db.DestinationTypes.OrderBy(existing => existing.Id).FirstAsync();
        var locationType = await db.LocationTypes.OrderBy(existing => existing.Id).FirstAsync();
        var placeType = await db.PlaceTypes.OrderBy(existing => existing.Id).FirstAsync();
        var stayType = await db.StayTypes.OrderBy(existing => existing.Id).FirstAsync();
        var eventType = await db.EventTypes.OrderBy(existing => existing.Id).FirstAsync();
        var territoryManager = await db.Users.SingleAsync(user => user.Email == "mika@gmail.com");
        var regionalManager = await db.Users.SingleAsync(user => user.Email == "elena@example.com");
        var user = await db.Users.SingleAsync(existing => existing.Email == "marko.p@example.com");
        regionalManager.AvatarUrl = "/media/uploads/avatars/elena-territory-test.png";
        user.AvatarUrl = null;
        await db.SaveChangesAsync();

        var assignedTerritory = new Territory
        {
            Name = $"Territory Dashboard Assigned {Guid.NewGuid():N}",
            CreatedAt = oldDate
        };
        var unassignedTerritory = new Territory
        {
            Name = $"Territory Dashboard Out {Guid.NewGuid():N}",
            CreatedAt = oldDate
        };

        db.Territories.AddRange(assignedTerritory, unassignedTerritory);
        await db.SaveChangesAsync();

        db.TerritoryManagerTerritories.Add(new TerritoryManagerTerritory
        {
            UserId = territoryManager.Id,
            TerritoryId = assignedTerritory.Id,
            CreatedAt = now
        });

        var assignedRegion = new Region
        {
            Name = "Scoped Territory Region",
            TerritoryId = assignedTerritory.Id,
            CreatedAt = oldDate
        };
        var outRegion = new Region
        {
            Name = "Out Territory Region",
            TerritoryId = unassignedTerritory.Id,
            CreatedAt = oldDate
        };

        db.Regions.AddRange(assignedRegion, outRegion);
        await db.SaveChangesAsync();

        await GrantRegionalManagerCoverageAsync(db, regionalManager, assignedRegion.Id);
        await GrantRegionalManagerCoverageAsync(db, user, assignedRegion.Id);

        var oldDestination = new Destination
        {
            Name = "Scoped Old Territory Destination",
            DestinationTypeId = destinationType.Id,
            RegionId = assignedRegion.Id,
            Description = "Old scoped territory destination.",
            Latitude = 45.1,
            Longitude = 15.1,
            CreatedAt = oldDate
        };
        var recentDestination = new Destination
        {
            Name = "Scoped Recent Territory Destination",
            DestinationTypeId = destinationType.Id,
            RegionId = assignedRegion.Id,
            Description = "Recent scoped territory destination.",
            Latitude = 45.2,
            Longitude = 15.2,
            CreatedAt = recentDate
        };
        var outDestination = new Destination
        {
            Name = "Out Territory Destination",
            DestinationTypeId = destinationType.Id,
            RegionId = outRegion.Id,
            Description = "Out territory destination.",
            Latitude = 46.1,
            Longitude = 16.1,
            CreatedAt = recentDate
        };

        db.Destinations.AddRange(oldDestination, recentDestination, outDestination);
        await db.SaveChangesAsync();

        var oldLocation = new Location
        {
            Name = "Scoped Old Territory Location",
            DestinationId = oldDestination.Id,
            LocationTypeId = locationType.Id,
            Latitude = 45.1,
            Longitude = 15.1,
            CreatedAt = oldDate
        };
        var recentLocation = new Location
        {
            Name = "Scoped Recent Territory Location",
            DestinationId = recentDestination.Id,
            LocationTypeId = locationType.Id,
            Latitude = 45.2,
            Longitude = 15.2,
            CreatedAt = recentDate
        };
        var outLocation = new Location
        {
            Name = "Out Territory Location",
            DestinationId = outDestination.Id,
            LocationTypeId = locationType.Id,
            Latitude = 46.1,
            Longitude = 16.1,
            CreatedAt = recentDate
        };

        db.Locations.AddRange(oldLocation, recentLocation, outLocation);
        await db.SaveChangesAsync();

        var oldPlace = new Place
        {
            Name = "Scoped Old Territory Place",
            PlaceTypeId = placeType.Id,
            LocationId = oldLocation.Id,
            Description = "Old scoped territory place.",
            Latitude = 45.1,
            Longitude = 15.1,
            CreatedAt = oldDate
        };
        var recentPlace = new Place
        {
            Name = "Scoped Recent Territory Place",
            PlaceTypeId = placeType.Id,
            LocationId = recentLocation.Id,
            Description = "Recent scoped territory place.",
            Latitude = 45.2,
            Longitude = 15.2,
            CreatedAt = recentDate
        };
        var stayPlace = new Place
        {
            Name = "Scoped Territory Stay",
            PlaceTypeId = placeType.Id,
            LocationId = recentLocation.Id,
            Description = "Scoped territory accommodation.",
            Latitude = 45.25,
            Longitude = 15.25,
            CreatedAt = oldDate
        };
        var outPlace = new Place
        {
            Name = "Out Territory Place",
            PlaceTypeId = placeType.Id,
            LocationId = outLocation.Id,
            Description = "Out territory place.",
            Latitude = 46.1,
            Longitude = 16.1,
            CreatedAt = recentDate
        };

        db.Places.AddRange(oldPlace, recentPlace, stayPlace, outPlace);
        await db.SaveChangesAsync();

        db.Stays.Add(new Stay
        {
            PlaceId = stayPlace.Id,
            StayTypeId = stayType.Id,
            PricePerNight = 120m
        });
        await db.SaveChangesAsync();

        var oldEvent = new Event
        {
            Name = "Scoped Old Territory Event",
            EventTypeId = eventType.Id,
            DestinationId = oldDestination.Id,
            LocationId = oldLocation.Id,
            Description = "Old scoped territory event.",
            StartDate = oldDate.AddDays(1),
            EndDate = oldDate.AddDays(2),
            CreatedAt = oldDate
        };
        var upcomingEvent = new Event
        {
            Name = "Scoped Upcoming Territory Event",
            EventTypeId = eventType.Id,
            DestinationId = recentDestination.Id,
            LocationId = recentLocation.Id,
            Description = "Upcoming scoped territory event.",
            StartDate = now.AddDays(10),
            EndDate = now.AddDays(11),
            CreatedAt = recentDate
        };
        var outEvent = new Event
        {
            Name = "Out Territory Event",
            EventTypeId = eventType.Id,
            DestinationId = outDestination.Id,
            LocationId = outLocation.Id,
            Description = "Out territory event.",
            StartDate = now.AddDays(10),
            EndDate = now.AddDays(11),
            CreatedAt = recentDate
        };

        db.Events.AddRange(oldEvent, upcomingEvent, outEvent);

        db.PlaceContributions.Add(new PlaceContribution
        {
            Type = PlaceContributionType.CreatePlace,
            Status = PlaceContributionStatus.PendingReview,
            UserId = user.Id,
            PlaceTypeId = placeType.Id,
            LocationId = recentLocation.Id,
            Name = "Scoped Territory Pending",
            Description = "Pending scoped territory contribution.",
            Latitude = 45.2,
            Longitude = 15.2,
            SubmittedAt = now,
            CreatedAt = now
        });

        await db.SaveChangesAsync();

        db.AnalyticsEvents.AddRange(
            new AnalyticsEvent
            {
                UserId = user.Id,
                EventType = AnalyticsEventTypes.DetailView,
                EntityType = AnalyticsEntityTypes.Destination,
                EntityId = oldDestination.Id,
                DestinationId = oldDestination.Id,
                OccurredAt = recentDate,
                MetadataJson = "{}"
            },
            new AnalyticsEvent
            {
                UserId = regionalManager.Id,
                EventType = AnalyticsEventTypes.PageView,
                EntityType = AnalyticsEntityTypes.Place,
                EntityId = recentPlace.Id,
                DestinationId = recentDestination.Id,
                OccurredAt = recentDate.AddHours(1),
                MetadataJson = "{}"
            },
            new AnalyticsEvent
            {
                EventType = AnalyticsEventTypes.DetailView,
                EntityType = AnalyticsEntityTypes.Location,
                EntityId = oldLocation.Id,
                DestinationId = oldDestination.Id,
                OccurredAt = recentDate.AddHours(2),
                MetadataJson = "{}"
            },
            new AnalyticsEvent
            {
                UserId = user.Id,
                EventType = AnalyticsEventTypes.DetailView,
                EntityType = AnalyticsEntityTypes.Event,
                EntityId = upcomingEvent.Id,
                DestinationId = recentDestination.Id,
                OccurredAt = recentDate.AddHours(3),
                MetadataJson = "{}"
            },
            new AnalyticsEvent
            {
                UserId = user.Id,
                EventType = AnalyticsEventTypes.DetailView,
                EntityType = AnalyticsEntityTypes.Destination,
                EntityId = oldDestination.Id,
                DestinationId = oldDestination.Id,
                OccurredAt = previousDate,
                MetadataJson = "{}"
            },
            new AnalyticsEvent
            {
                UserId = user.Id,
                EventType = AnalyticsEventTypes.DetailView,
                EntityType = AnalyticsEntityTypes.Destination,
                EntityId = outDestination.Id,
                DestinationId = outDestination.Id,
                OccurredAt = recentDate,
                MetadataJson = "{}"
            });

        await db.SaveChangesAsync();

        return new TerritoryDashboardFixture(
            assignedTerritory.Id,
            assignedTerritory.Name,
            unassignedTerritory.Id,
            assignedRegion.Id,
            outRegion.Id,
            oldDestination.Id,
            oldLocation.Id,
            recentPlace.Id,
            stayPlace.Id,
            upcomingEvent.Id);
    }

    private static async Task<int> CreateEmptyAssignedTerritoryAsync(GeotrackApiFactory app)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var manager = await db.Users.SingleAsync(user => user.Email == "mika@gmail.com");
        var territory = new Territory
        {
            Name = $"Territory Dashboard Empty {Guid.NewGuid():N}",
            CreatedAt = DateTime.UtcNow
        };

        db.Territories.Add(territory);
        await db.SaveChangesAsync();

        db.TerritoryManagerTerritories.Add(new TerritoryManagerTerritory
        {
            UserId = manager.Id,
            TerritoryId = territory.Id,
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        return territory.Id;
    }

    private static async Task<(int TerritoryId, int RegionId)> CreateEmptyAssignedRegionAsync(GeotrackApiFactory app)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var manager = await db.Users.SingleAsync(user => user.Email == "mika@gmail.com");
        var now = DateTime.UtcNow;

        var territory = new Territory
        {
            Name = $"Territory Empty Region {Guid.NewGuid():N}",
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

        var region = new Region
        {
            Name = "Empty Scoped Region",
            TerritoryId = territory.Id,
            CreatedAt = now
        };
        db.Regions.Add(region);
        await db.SaveChangesAsync();

        return (territory.Id, region.Id);
    }

    private static async Task<int> AddSparseContentRegionAsync(
        GeotrackApiFactory app,
        int territoryId,
        string regionName)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var destinationType = await db.DestinationTypes.OrderBy(existing => existing.Id).FirstAsync();
        var locationType = await db.LocationTypes.OrderBy(existing => existing.Id).FirstAsync();
        var now = DateTime.UtcNow;

        var region = new Region
        {
            Name = regionName,
            TerritoryId = territoryId,
            CreatedAt = now.AddDays(-2)
        };
        db.Regions.Add(region);
        await db.SaveChangesAsync();

        var destination = new Destination
        {
            Name = $"{regionName} Destination",
            DestinationTypeId = destinationType.Id,
            RegionId = region.Id,
            Description = "Sparse content used for dashboard health ranking.",
            Latitude = 44.1,
            Longitude = 15.1,
            CreatedAt = now.AddDays(-1)
        };
        db.Destinations.Add(destination);
        await db.SaveChangesAsync();

        db.Locations.Add(new Location
        {
            Name = $"{regionName} Location",
            DestinationId = destination.Id,
            LocationTypeId = locationType.Id,
            Description = "Sparse location used for dashboard health ranking.",
            Latitude = 44.2,
            Longitude = 15.2,
            CreatedAt = now.AddDays(-1)
        });
        await db.SaveChangesAsync();

        return region.Id;
    }

    private static async Task<List<int>> AddNamedRegionsAsync(
        GeotrackApiFactory app,
        int territoryId,
        params string[] regionNames)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = DateTime.UtcNow;
        var regions = regionNames.Select((name, index) => new Region
        {
            Name = name,
            TerritoryId = territoryId,
            CreatedAt = now.AddMinutes(-index)
        }).ToList();

        db.Regions.AddRange(regions);
        await db.SaveChangesAsync();

        return regions.Select(region => region.Id).ToList();
    }

    private static async Task AddAdditionalRegionsAsync(
        GeotrackApiFactory app,
        int territoryId,
        int count)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = DateTime.UtcNow;

        for (var i = 1; i <= count; i++)
        {
            db.Regions.Add(new Region
            {
                Name = $"Paged Territory Region {i:D2}",
                TerritoryId = territoryId,
                CreatedAt = now.AddMinutes(-i)
            });
        }

        await db.SaveChangesAsync();
    }

    private static async Task<TerritoryRegionContentItemDto> AssertRegionContentContainsAsync(
        HttpClient client,
        int territoryId,
        int regionId,
        string entityType,
        int expectedId)
    {
        var response = await client.GetFromJsonAsync<TerritoryRegionContentResponseDto>(
            $"/api/admin/territory/regions/{regionId}/content?territoryId={territoryId}&entityType={entityType}&skip=0&take=25");

        Assert.NotNull(response);
        var item = Assert.Single(response!.Items, item => item.Id == expectedId);
        Assert.Equal(0, response.Skip);
        Assert.Equal(25, response.Take);
        return item;
    }

    private static async Task GrantRegionalManagerCoverageAsync(AppDbContext db, User reviewer, int regionId)
    {
        var regionalManagerRole = await db.Roles.SingleAsync(role => role.Name == RoleNames.RegionalManager);

        if (!await db.UserRoles.AnyAsync(userRole =>
            userRole.UserId == reviewer.Id &&
            userRole.RoleId == regionalManagerRole.Id))
        {
            db.UserRoles.Add(new UserRole
            {
                UserId = reviewer.Id,
                RoleId = regionalManagerRole.Id
            });
        }

        if (!await db.RegionalManagerRegions.AnyAsync(assignment =>
            assignment.UserId == reviewer.Id &&
            assignment.RegionId == regionId))
        {
            db.RegionalManagerRegions.Add(new RegionalManagerRegion
            {
                UserId = reviewer.Id,
                RegionId = regionId,
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            });
        }

        await db.SaveChangesAsync();
    }

    private static void SetBoundary(AppDbContext db, object entity, Geometry geometry)
    {
        geometry.SRID = 4326;
        db.Entry(entity)
            .Property(BoundaryGeometryPropertyName)
            .CurrentValue = geometry;
    }

    private static void SetDisplayBoundary(AppDbContext db, object entity, Geometry geometry)
    {
        geometry.SRID = 4326;
        db.Entry(entity)
            .Property(BoundaryDisplayGeometryPropertyName)
            .CurrentValue = geometry;
    }

    private static string CoordinatesJson(TerritoryRegionGeometryDto geometry) =>
        JsonSerializer.Serialize(geometry.Coordinates);

    private static NtsPolygon Polygon(double west, double south, double east, double north)
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

    private sealed record TerritoryRegionMapFixture(
        int AssignedTerritoryId,
        int OtherTerritoryId,
        int AssignedRegionId,
        int UnassignedRegionId,
        int MissingBoundaryRegionId,
        int OtherTerritoryRegionId);

    private sealed record TerritoryDashboardFixture(
        int AssignedTerritoryId,
        string AssignedTerritoryName,
        int UnassignedTerritoryId,
        int AssignedRegionId,
        int UnassignedRegionId,
        int AssignedDestinationId,
        int AssignedLocationId,
        int AssignedPlaceId,
        int AssignedStayPlaceId,
        int UpcomingEventId);
}
