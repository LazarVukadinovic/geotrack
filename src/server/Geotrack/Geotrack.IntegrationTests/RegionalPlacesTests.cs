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

public sealed class RegionalPlacesTests
{
    [Fact]
    public async Task Regional_manager_lists_only_assigned_region_places_with_default_25_item_page()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        var fixture = await CreatePlacesFixtureAsync(app);

        using var anonymous = app.CreateClient();
        var anonymousResponse = await anonymous.GetAsync(
            $"/api/admin/regional/places?regionId={fixture.AssignedRegionId}");
        Assert.Equal(HttpStatusCode.Unauthorized, anonymousResponse.StatusCode);

        using var regularUser = await LoginAsync(app, "marko.p@example.com", "password123");
        var nonRegionalResponse = await regularUser.GetAsync(
            $"/api/admin/regional/places?regionId={fixture.AssignedRegionId}");
        Assert.Equal(HttpStatusCode.Forbidden, nonRegionalResponse.StatusCode);

        using var manager = await LoginAsync(app, "elena@example.com", "password123");
        var unassignedRegionResponse = await manager.GetAsync(
            $"/api/admin/regional/places?regionId={fixture.UnassignedRegionId}");
        Assert.Equal(HttpStatusCode.Forbidden, unassignedRegionResponse.StatusCode);

        var page = await GetPlacesAsync(manager, fixture.AssignedRegionId);

        Assert.Equal(30, page.TotalCount);
        Assert.Equal(0, page.Skip);
        Assert.Equal(25, page.Take);
        Assert.Equal(25, page.Items.Count);
        Assert.All(page.Items, item => Assert.Equal(fixture.AssignedRegionId, item.RegionId));
        Assert.DoesNotContain(page.Items, item => item.Id == fixture.UnassignedPlaceId);
        Assert.Contains(page.Items, item => item.Id == fixture.StayPlaceId && item.StayType == fixture.StayTypeName);
    }

    [Fact]
    public async Task Pagination_search_and_sort_use_backend_query_parameters()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        var fixture = await CreatePlacesFixtureAsync(app);

        using var manager = await LoginAsync(app, "elena@example.com", "password123");

        var nextPage = await GetPlacesAsync(manager, fixture.AssignedRegionId, skip: 25, take: 10);
        Assert.Equal(30, nextPage.TotalCount);
        Assert.Equal(25, nextPage.Skip);
        Assert.Equal(10, nextPage.Take);
        Assert.Equal(5, nextPage.Items.Count);

        var searchedByName = await GetPlacesAsync(
            manager,
            fixture.AssignedRegionId,
            search: "Regional Place 07");
        Assert.Single(searchedByName.Items);
        Assert.Equal(fixture.SearchPlaceId, searchedByName.Items[0].Id);

        var searchedBySummary = await GetPlacesAsync(
            manager,
            fixture.AssignedRegionId,
            search: "vaulted galleries");
        Assert.Single(searchedBySummary.Items);
        Assert.Equal(fixture.SearchPlaceId, searchedBySummary.Items[0].Id);

        var searchedByType = await GetPlacesAsync(
            manager,
            fixture.AssignedRegionId,
            search: fixture.SearchPlaceTypeName);
        Assert.Single(searchedByType.Items);
        Assert.Equal(fixture.SearchPlaceId, searchedByType.Items[0].Id);

        var searchedByStayType = await GetPlacesAsync(
            manager,
            fixture.AssignedRegionId,
            search: fixture.StayTypeName);
        Assert.Single(searchedByStayType.Items);
        Assert.Equal(fixture.StayPlaceId, searchedByStayType.Items[0].Id);

        var newest = await GetPlacesAsync(manager, fixture.AssignedRegionId, sort: "newest", take: 3);
        Assert.Equal("Regional Place 00", newest.Items[0].Name);

        var oldest = await GetPlacesAsync(manager, fixture.AssignedRegionId, sort: "oldest", take: 3);
        Assert.Equal("Regional Place 29", oldest.Items[0].Name);

        var nameAsc = await GetPlacesAsync(manager, fixture.AssignedRegionId, sort: "nameAsc", take: 3);
        Assert.Equal("Regional Place 00", nameAsc.Items[0].Name);

        var nameDesc = await GetPlacesAsync(manager, fixture.AssignedRegionId, sort: "nameDesc", take: 3);
        Assert.Equal("Regional Place 29", nameDesc.Items[0].Name);
    }

    [Fact]
    public async Task Delete_place_is_region_scoped_and_blocks_related_content()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        var fixture = await CreatePlacesFixtureAsync(app);

        using var manager = await LoginAsync(app, "elena@example.com", "password123");

        var outOfRegionResponse = await manager.DeleteAsync(
            $"/api/admin/regional/places/{fixture.UnassignedPlaceId}?regionId={fixture.AssignedRegionId}");
        Assert.Equal(HttpStatusCode.NotFound, outOfRegionResponse.StatusCode);

        var blockedResponse = await manager.DeleteAsync(
            $"/api/admin/regional/places/{fixture.PlaceWithContentId}?regionId={fixture.AssignedRegionId}");
        Assert.Equal(HttpStatusCode.Conflict, blockedResponse.StatusCode);
        var blockedBody = await blockedResponse.Content.ReadFromJsonAsync<Dictionary<string, string>>();
        Assert.Equal("This place cannot be deleted while it has related content.", blockedBody?["message"]);

        var deleteResponse = await manager.DeleteAsync(
            $"/api/admin/regional/places/{fixture.BarePlaceId}?regionId={fixture.AssignedRegionId}");
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.False(await db.Places.AnyAsync(item => item.Id == fixture.BarePlaceId));
    }

    [Fact]
    public async Task Detail_and_update_are_restricted_to_assigned_region()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        var fixture = await CreatePlacesFixtureAsync(app);

        using var manager = await LoginAsync(app, "elena@example.com", "password123");

        var outDetail = await manager.GetAsync(
            $"/api/admin/regional/places/{fixture.UnassignedPlaceId}?regionId={fixture.AssignedRegionId}");
        Assert.Equal(HttpStatusCode.NotFound, outDetail.StatusCode);

        var updateResponse = await manager.PutAsJsonAsync(
            $"/api/admin/regional/places/{fixture.SearchPlaceId}?regionId={fixture.AssignedRegionId}",
            new RegionalPlaceUpdateRequestDto
            {
                Name = "Regional Place 07 Updated",
                Description = "Updated place description.",
                Latitude = 42.77,
                Longitude = 18.77
            });
        updateResponse.EnsureSuccessStatusCode();

        var updated = await updateResponse.Content.ReadFromJsonAsync<RegionalPlaceDetailDto>();
        Assert.NotNull(updated);
        Assert.Equal(fixture.AssignedRegionId, updated!.RegionId);
        Assert.Equal("Regional Place 07 Updated", updated.Name);
        Assert.Equal("Updated place description.", updated.Description);
        Assert.Equal(42.77, updated.Latitude);
        Assert.Equal(18.77, updated.Longitude);
    }

    private static async Task<RegionalPlaceListResponseDto> GetPlacesAsync(
        HttpClient client,
        int regionId,
        string? search = null,
        string sort = "newest",
        int skip = 0,
        int? take = null)
    {
        var url = $"/api/admin/regional/places?regionId={regionId}&sort={sort}&skip={skip}";
        if (take.HasValue)
        {
            url += $"&take={take.Value}";
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            url += "&search=" + Uri.EscapeDataString(search);
        }

        var page = await client.GetFromJsonAsync<RegionalPlaceListResponseDto>(url);
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

    private static async Task<PlacesFixture> CreatePlacesFixtureAsync(GeotrackApiFactory app)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var now = DateTime.UtcNow;
        var territory = await db.Territories.OrderBy(item => item.Id).FirstAsync();
        var destinationType = await db.DestinationTypes.OrderBy(item => item.Id).FirstAsync();
        var locationType = await db.LocationTypes.OrderBy(item => item.Id).FirstAsync();
        var eventType = await db.EventTypes.OrderBy(item => item.Id).FirstAsync();
        var manager = await db.Users.SingleAsync(item => item.Email == "elena@example.com");

        var basePlaceType = new PlaceType
        {
            Name = $"Regional Base Place Type {Guid.NewGuid():N}",
            Description = "Base place type for regional place tests.",
            CreatedAt = now
        };
        var searchPlaceType = new PlaceType
        {
            Name = $"Regional Gallery Place Type {Guid.NewGuid():N}",
            Description = "Search-specific place type for regional place tests.",
            CreatedAt = now
        };
        var accommodationPlaceType = new PlaceType
        {
            Name = $"Regional Accommodation Place Type {Guid.NewGuid():N}",
            Description = "Accommodation place type for regional place tests.",
            CreatedAt = now
        };
        var stayType = new StayType
        {
            Name = $"Boutique Lodge {Guid.NewGuid():N}",
            Slug = $"boutique-lodge-{Guid.NewGuid():N}",
            Description = "Stay type for accommodation inclusion tests.",
            CreatedAt = now
        };
        db.PlaceTypes.AddRange(basePlaceType, searchPlaceType, accommodationPlaceType);
        db.StayTypes.Add(stayType);
        await db.SaveChangesAsync();

        var assignedRegion = new Region
        {
            Name = $"Places Assigned {Guid.NewGuid():N}",
            TerritoryId = territory.Id,
            CreatedAt = now
        };
        var unassignedRegion = new Region
        {
            Name = $"Places Unassigned {Guid.NewGuid():N}",
            TerritoryId = territory.Id,
            CreatedAt = now
        };
        db.Regions.AddRange(assignedRegion, unassignedRegion);
        await db.SaveChangesAsync();

        await GrantRegionalManagerCoverageAsync(db, manager, assignedRegion.Id);

        var assignedDestination = new Destination
        {
            Name = "Regional Place Parent Destination",
            DestinationTypeId = destinationType.Id,
            RegionId = assignedRegion.Id,
            Description = "Destination for regional place tests.",
            Latitude = 42,
            Longitude = 18,
            CreatedAt = now
        };
        var unassignedDestination = new Destination
        {
            Name = "Unassigned Regional Place Parent Destination",
            DestinationTypeId = destinationType.Id,
            RegionId = unassignedRegion.Id,
            Description = "This destination must never appear in assigned-region place results.",
            Latitude = 45,
            Longitude = 19,
            CreatedAt = now
        };
        db.Destinations.AddRange(assignedDestination, unassignedDestination);
        await db.SaveChangesAsync();

        var assignedLocation = new Location
        {
            Name = "Regional Place Parent Location",
            DestinationId = assignedDestination.Id,
            LocationTypeId = locationType.Id,
            Latitude = 42,
            Longitude = 18,
            CreatedAt = now
        };
        var unassignedLocation = new Location
        {
            Name = "Unassigned Regional Place Parent Location",
            DestinationId = unassignedDestination.Id,
            LocationTypeId = locationType.Id,
            Latitude = 45,
            Longitude = 19,
            CreatedAt = now
        };
        db.Locations.AddRange(assignedLocation, unassignedLocation);
        await db.SaveChangesAsync();

        var places = Enumerable.Range(0, 30)
            .Select(index => new Place
            {
                Name = $"Regional Place {index:D2}",
                PlaceTypeId = index == 7
                    ? searchPlaceType.Id
                    : index == 5 ? accommodationPlaceType.Id : basePlaceType.Id,
                LocationId = assignedLocation.Id,
                Description = index == 7
                    ? "Searchable place summary with vaulted galleries."
                    : $"Place description {index:D2}.",
                Latitude = 42 + index / 100d,
                Longitude = 18 + index / 100d,
                CreatedAt = now.AddMinutes(-index)
            })
            .ToList();

        var unassignedPlace = new Place
        {
            Name = "Unassigned Regional Place",
            PlaceTypeId = basePlaceType.Id,
            LocationId = unassignedLocation.Id,
            Description = "This place must never appear in assigned-region results.",
            Latitude = 45,
            Longitude = 19,
            CreatedAt = now
        };

        db.Places.AddRange(places);
        db.Places.Add(unassignedPlace);
        await db.SaveChangesAsync();

        db.Stays.Add(new Stay
        {
            PlaceId = places[5].Id,
            StayTypeId = stayType.Id,
            CreatedAt = now
        });

        db.Events.Add(new Event
        {
            Name = "Place Related Event",
            EventTypeId = eventType.Id,
            LocationId = assignedLocation.Id,
            PlaceId = places[29].Id,
            Description = "Related content that protects the place from deletion.",
            StartDate = now.AddDays(1),
            EndDate = now.AddDays(1).AddHours(2),
            CreatedAt = now
        });
        await db.SaveChangesAsync();

        return new PlacesFixture(
            assignedRegion.Id,
            unassignedRegion.Id,
            places[7].Id,
            places[28].Id,
            places[29].Id,
            unassignedPlace.Id,
            places[5].Id,
            searchPlaceType.Name,
            stayType.Name);
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

    private sealed record PlacesFixture(
        int AssignedRegionId,
        int UnassignedRegionId,
        int SearchPlaceId,
        int BarePlaceId,
        int PlaceWithContentId,
        int UnassignedPlaceId,
        int StayPlaceId,
        string SearchPlaceTypeName,
        string StayTypeName);
}
