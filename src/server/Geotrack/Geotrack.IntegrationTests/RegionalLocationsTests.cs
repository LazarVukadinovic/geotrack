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
using Xunit;

namespace Geotrack.IntegrationTests;

public sealed class RegionalLocationsTests
{
    [Fact]
    public async Task Regional_manager_lists_only_assigned_region_locations_with_default_25_item_page()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        var fixture = await CreateLocationsFixtureAsync(app);

        using var anonymous = app.CreateClient();
        var anonymousResponse = await anonymous.GetAsync(
            $"/api/admin/regional/locations?regionId={fixture.AssignedRegionId}");
        Assert.Equal(HttpStatusCode.Unauthorized, anonymousResponse.StatusCode);

        using var regularUser = await LoginAsync(app, "marko.p@example.com", "password123");
        var nonRegionalResponse = await regularUser.GetAsync(
            $"/api/admin/regional/locations?regionId={fixture.AssignedRegionId}");
        Assert.Equal(HttpStatusCode.Forbidden, nonRegionalResponse.StatusCode);

        using var manager = await LoginAsync(app, "elena@example.com", "password123");
        var unassignedRegionResponse = await manager.GetAsync(
            $"/api/admin/regional/locations?regionId={fixture.UnassignedRegionId}");
        Assert.Equal(HttpStatusCode.Forbidden, unassignedRegionResponse.StatusCode);

        var page = await GetLocationsAsync(manager, fixture.AssignedRegionId);

        Assert.Equal(30, page.TotalCount);
        Assert.Equal(0, page.Skip);
        Assert.Equal(25, page.Take);
        Assert.Equal(25, page.Items.Count);
        Assert.All(page.Items, item => Assert.Equal(fixture.AssignedRegionId, item.RegionId));
        Assert.DoesNotContain(page.Items, item => item.Id == fixture.UnassignedLocationId);
    }

    [Fact]
    public async Task Pagination_search_and_sort_use_backend_query_parameters()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        var fixture = await CreateLocationsFixtureAsync(app);

        using var manager = await LoginAsync(app, "elena@example.com", "password123");

        var nextPage = await GetLocationsAsync(manager, fixture.AssignedRegionId, skip: 25, take: 10);
        Assert.Equal(30, nextPage.TotalCount);
        Assert.Equal(25, nextPage.Skip);
        Assert.Equal(10, nextPage.Take);
        Assert.Equal(5, nextPage.Items.Count);

        var searchedByName = await GetLocationsAsync(
            manager,
            fixture.AssignedRegionId,
            search: "Regional Location 07");
        Assert.Single(searchedByName.Items);
        Assert.Equal(fixture.SearchLocationId, searchedByName.Items[0].Id);

        var searchedByRelatedSummary = await GetLocationsAsync(
            manager,
            fixture.AssignedRegionId,
            search: "sunlit promenades");
        Assert.Single(searchedByRelatedSummary.Items);
        Assert.Equal(fixture.SearchLocationId, searchedByRelatedSummary.Items[0].Id);

        var newest = await GetLocationsAsync(manager, fixture.AssignedRegionId, sort: "newest", take: 3);
        Assert.Equal("Regional Location 00", newest.Items[0].Name);

        var oldest = await GetLocationsAsync(manager, fixture.AssignedRegionId, sort: "oldest", take: 3);
        Assert.Equal("Regional Location 29", oldest.Items[0].Name);

        var nameAsc = await GetLocationsAsync(manager, fixture.AssignedRegionId, sort: "nameAsc", take: 3);
        Assert.Equal("Regional Location 00", nameAsc.Items[0].Name);

        var nameDesc = await GetLocationsAsync(manager, fixture.AssignedRegionId, sort: "nameDesc", take: 3);
        Assert.Equal("Regional Location 29", nameDesc.Items[0].Name);
    }

    [Fact]
    public async Task Delete_location_is_region_scoped_and_blocks_related_content()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        var fixture = await CreateLocationsFixtureAsync(app);

        using var manager = await LoginAsync(app, "elena@example.com", "password123");

        var outOfRegionResponse = await manager.DeleteAsync(
            $"/api/admin/regional/locations/{fixture.UnassignedLocationId}?regionId={fixture.AssignedRegionId}");
        Assert.Equal(HttpStatusCode.NotFound, outOfRegionResponse.StatusCode);

        var blockedResponse = await manager.DeleteAsync(
            $"/api/admin/regional/locations/{fixture.LocationWithContentId}?regionId={fixture.AssignedRegionId}");
        Assert.Equal(HttpStatusCode.Conflict, blockedResponse.StatusCode);
        var blockedBody = await blockedResponse.Content.ReadFromJsonAsync<Dictionary<string, string>>();
        Assert.Equal("This location cannot be deleted while it has related content.", blockedBody?["message"]);

        var deleteResponse = await manager.DeleteAsync(
            $"/api/admin/regional/locations/{fixture.BareLocationId}?regionId={fixture.AssignedRegionId}");
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.False(await db.Locations.AnyAsync(item => item.Id == fixture.BareLocationId));
    }

    [Fact]
    public async Task Detail_and_update_are_restricted_to_assigned_region()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        var fixture = await CreateLocationsFixtureAsync(app);

        using var manager = await LoginAsync(app, "elena@example.com", "password123");

        var outDetail = await manager.GetAsync(
            $"/api/admin/regional/locations/{fixture.UnassignedLocationId}?regionId={fixture.AssignedRegionId}");
        Assert.Equal(HttpStatusCode.NotFound, outDetail.StatusCode);

        var updateResponse = await manager.PutAsJsonAsync(
            $"/api/admin/regional/locations/{fixture.SearchLocationId}?regionId={fixture.AssignedRegionId}",
            new RegionalLocationUpdateRequestDto
            {
                Name = "Regional Location 07 Updated",
                Latitude = 42.77,
                Longitude = 18.77
            });
        updateResponse.EnsureSuccessStatusCode();

        var updated = await updateResponse.Content.ReadFromJsonAsync<RegionalLocationDetailDto>();
        Assert.NotNull(updated);
        Assert.Equal(fixture.AssignedRegionId, updated!.RegionId);
        Assert.Equal("Regional Location 07 Updated", updated.Name);
        Assert.Equal(42.77, updated.Latitude);
        Assert.Equal(18.77, updated.Longitude);
    }

    private static async Task<RegionalLocationListResponseDto> GetLocationsAsync(
        HttpClient client,
        int regionId,
        string? search = null,
        string sort = "newest",
        int skip = 0,
        int? take = null)
    {
        var url = $"/api/admin/regional/locations?regionId={regionId}&sort={sort}&skip={skip}";
        if (take.HasValue)
        {
            url += $"&take={take.Value}";
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            url += "&search=" + Uri.EscapeDataString(search);
        }

        var page = await client.GetFromJsonAsync<RegionalLocationListResponseDto>(url);
        Assert.NotNull(page);
        return page!;
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

    private static async Task<LocationsFixture> CreateLocationsFixtureAsync(GeotrackApiFactory app)
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
            Name = $"Locations Assigned {Guid.NewGuid():N}",
            TerritoryId = territory.Id,
            CreatedAt = now
        };
        var unassignedRegion = new Region
        {
            Name = $"Locations Unassigned {Guid.NewGuid():N}",
            TerritoryId = territory.Id,
            CreatedAt = now
        };
        db.Regions.AddRange(assignedRegion, unassignedRegion);
        await db.SaveChangesAsync();

        await GrantRegionalManagerCoverageAsync(db, manager, assignedRegion.Id);

        var assignedDestinations = Enumerable.Range(0, 30)
            .Select(index => new Destination
            {
                Name = $"Location Parent Destination {index:D2}",
                DestinationTypeId = destinationType.Id,
                RegionId = assignedRegion.Id,
                Description = index == 7
                    ? "Searchable destination summary with sunlit promenades."
                    : $"Location parent destination description {index:D2}.",
                Latitude = 42 + index / 100d,
                Longitude = 18 + index / 100d,
                CreatedAt = now.AddMinutes(-index)
            })
            .ToList();

        var unassignedDestination = new Destination
        {
            Name = "Unassigned Location Parent Destination",
            DestinationTypeId = destinationType.Id,
            RegionId = unassignedRegion.Id,
            Description = "This destination must never appear in assigned-region location results.",
            Latitude = 45,
            Longitude = 19,
            CreatedAt = now
        };

        db.Destinations.AddRange(assignedDestinations);
        db.Destinations.Add(unassignedDestination);
        await db.SaveChangesAsync();

        var locations = Enumerable.Range(0, 30)
            .Select(index => new Location
            {
                Name = $"Regional Location {index:D2}",
                DestinationId = assignedDestinations[index].Id,
                LocationTypeId = locationType.Id,
                Latitude = 42 + index / 100d,
                Longitude = 18 + index / 100d,
                CreatedAt = now.AddMinutes(-index)
            })
            .ToList();

        var unassignedLocation = new Location
        {
            Name = "Unassigned Regional Location",
            DestinationId = unassignedDestination.Id,
            LocationTypeId = locationType.Id,
            Latitude = 45,
            Longitude = 19,
            CreatedAt = now
        };

        db.Locations.AddRange(locations);
        db.Locations.Add(unassignedLocation);
        await db.SaveChangesAsync();

        db.Places.Add(new Place
        {
            Name = "Location Related Place",
            PlaceTypeId = placeType.Id,
            LocationId = locations[29].Id,
            Description = "Related content that protects the location from deletion.",
            Latitude = 42.9,
            Longitude = 18.9,
            CreatedAt = now
        });
        await db.SaveChangesAsync();

        return new LocationsFixture(
            assignedRegion.Id,
            unassignedRegion.Id,
            locations[7].Id,
            locations[28].Id,
            locations[29].Id,
            unassignedLocation.Id);
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

    private sealed record LocationsFixture(
        int AssignedRegionId,
        int UnassignedRegionId,
        int SearchLocationId,
        int BareLocationId,
        int LocationWithContentId,
        int UnassignedLocationId);
}
