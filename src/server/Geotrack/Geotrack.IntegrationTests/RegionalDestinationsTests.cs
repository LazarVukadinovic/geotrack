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

public sealed class RegionalDestinationsTests
{
    [Fact]
    public async Task Regional_manager_lists_only_assigned_region_destinations_with_default_25_item_page()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        var fixture = await CreateDestinationsFixtureAsync(app);

        using var anonymous = app.CreateClient();
        var anonymousResponse = await anonymous.GetAsync(
            $"/api/admin/regional/destinations?regionId={fixture.AssignedRegionId}");
        Assert.Equal(HttpStatusCode.Unauthorized, anonymousResponse.StatusCode);

        using var regularUser = await LoginAsync(app, "marko.p@example.com", "password123");
        var nonRegionalResponse = await regularUser.GetAsync(
            $"/api/admin/regional/destinations?regionId={fixture.AssignedRegionId}");
        Assert.Equal(HttpStatusCode.Forbidden, nonRegionalResponse.StatusCode);

        using var manager = await LoginAsync(app, "elena@example.com", "password123");
        var unassignedRegionResponse = await manager.GetAsync(
            $"/api/admin/regional/destinations?regionId={fixture.UnassignedRegionId}");
        Assert.Equal(HttpStatusCode.Forbidden, unassignedRegionResponse.StatusCode);

        var page = await GetDestinationsAsync(manager, fixture.AssignedRegionId);

        Assert.Equal(30, page.TotalCount);
        Assert.Equal(0, page.Skip);
        Assert.Equal(25, page.Take);
        Assert.Equal(25, page.Items.Count);
        Assert.All(page.Items, item => Assert.Equal(fixture.AssignedRegionId, item.RegionId));
        Assert.DoesNotContain(page.Items, item => item.Id == fixture.UnassignedDestinationId);
    }

    [Fact]
    public async Task Pagination_search_and_sort_use_backend_query_parameters()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        var fixture = await CreateDestinationsFixtureAsync(app);

        using var manager = await LoginAsync(app, "elena@example.com", "password123");

        var nextPage = await GetDestinationsAsync(manager, fixture.AssignedRegionId, skip: 25, take: 10);
        Assert.Equal(30, nextPage.TotalCount);
        Assert.Equal(25, nextPage.Skip);
        Assert.Equal(10, nextPage.Take);
        Assert.Equal(5, nextPage.Items.Count);

        var searchedByName = await GetDestinationsAsync(
            manager,
            fixture.AssignedRegionId,
            search: "Regional Destination 07");
        Assert.Single(searchedByName.Items);
        Assert.Equal(fixture.SearchDestinationId, searchedByName.Items[0].Id);

        var searchedBySummary = await GetDestinationsAsync(
            manager,
            fixture.AssignedRegionId,
            search: "sunlit orchards");
        Assert.Single(searchedBySummary.Items);
        Assert.Equal(fixture.SearchDestinationId, searchedBySummary.Items[0].Id);

        var newest = await GetDestinationsAsync(manager, fixture.AssignedRegionId, sort: "newest", take: 3);
        Assert.Equal("Regional Destination 00", newest.Items[0].Name);

        var oldest = await GetDestinationsAsync(manager, fixture.AssignedRegionId, sort: "oldest", take: 3);
        Assert.Equal("Regional Destination 29", oldest.Items[0].Name);

        var nameAsc = await GetDestinationsAsync(manager, fixture.AssignedRegionId, sort: "nameAsc", take: 3);
        Assert.Equal("Regional Destination 00", nameAsc.Items[0].Name);

        var nameDesc = await GetDestinationsAsync(manager, fixture.AssignedRegionId, sort: "nameDesc", take: 3);
        Assert.Equal("Regional Destination 29", nameDesc.Items[0].Name);
    }

    [Fact]
    public async Task Delete_destination_is_region_scoped_and_blocks_related_content()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        var fixture = await CreateDestinationsFixtureAsync(app);

        using var manager = await LoginAsync(app, "elena@example.com", "password123");

        var outOfRegionResponse = await manager.DeleteAsync(
            $"/api/admin/regional/destinations/{fixture.UnassignedDestinationId}?regionId={fixture.AssignedRegionId}");
        Assert.Equal(HttpStatusCode.NotFound, outOfRegionResponse.StatusCode);

        var blockedResponse = await manager.DeleteAsync(
            $"/api/admin/regional/destinations/{fixture.DestinationWithContentId}?regionId={fixture.AssignedRegionId}");
        Assert.Equal(HttpStatusCode.Conflict, blockedResponse.StatusCode);
        var blockedBody = await blockedResponse.Content.ReadFromJsonAsync<Dictionary<string, string>>();
        Assert.Equal("This destination cannot be deleted while it has related content.", blockedBody?["message"]);

        var deleteResponse = await manager.DeleteAsync(
            $"/api/admin/regional/destinations/{fixture.BareDestinationId}?regionId={fixture.AssignedRegionId}");
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.False(await db.Destinations.AnyAsync(item => item.Id == fixture.BareDestinationId));
    }

    [Fact]
    public async Task Detail_and_update_are_restricted_to_assigned_region()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        var fixture = await CreateDestinationsFixtureAsync(app);

        using var manager = await LoginAsync(app, "elena@example.com", "password123");

        var outDetail = await manager.GetAsync(
            $"/api/admin/regional/destinations/{fixture.UnassignedDestinationId}?regionId={fixture.AssignedRegionId}");
        Assert.Equal(HttpStatusCode.NotFound, outDetail.StatusCode);

        var updateResponse = await manager.PutAsJsonAsync(
            $"/api/admin/regional/destinations/{fixture.SearchDestinationId}?regionId={fixture.AssignedRegionId}",
            new RegionalDestinationUpdateRequestDto
            {
                Name = "Regional Destination 07 Updated",
                Description = "Updated destination description."
            });
        updateResponse.EnsureSuccessStatusCode();

        var updated = await updateResponse.Content.ReadFromJsonAsync<RegionalDestinationDetailDto>();
        Assert.NotNull(updated);
        Assert.Equal(fixture.AssignedRegionId, updated!.RegionId);
        Assert.Equal("Regional Destination 07 Updated", updated.Name);
        Assert.Equal("Updated destination description.", updated.Description);
    }

    private static async Task<RegionalDestinationListResponseDto> GetDestinationsAsync(
        HttpClient client,
        int regionId,
        string? search = null,
        string sort = "newest",
        int skip = 0,
        int? take = null)
    {
        var url = $"/api/admin/regional/destinations?regionId={regionId}&sort={sort}&skip={skip}";
        if (take.HasValue)
        {
            url += $"&take={take.Value}";
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            url += "&search=" + Uri.EscapeDataString(search);
        }

        var page = await client.GetFromJsonAsync<RegionalDestinationListResponseDto>(url);
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

    private static async Task<DestinationsFixture> CreateDestinationsFixtureAsync(GeotrackApiFactory app)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var now = DateTime.UtcNow;
        var territory = await db.Territories.OrderBy(item => item.Id).FirstAsync();
        var destinationType = await db.DestinationTypes.OrderBy(item => item.Id).FirstAsync();
        var locationType = await db.LocationTypes.OrderBy(item => item.Id).FirstAsync();
        var manager = await db.Users.SingleAsync(item => item.Email == "elena@example.com");

        var assignedRegion = new Region
        {
            Name = $"Destinations Assigned {Guid.NewGuid():N}",
            TerritoryId = territory.Id,
            CreatedAt = now
        };
        var unassignedRegion = new Region
        {
            Name = $"Destinations Unassigned {Guid.NewGuid():N}",
            TerritoryId = territory.Id,
            CreatedAt = now
        };
        db.Regions.AddRange(assignedRegion, unassignedRegion);
        await db.SaveChangesAsync();

        await GrantRegionalManagerCoverageAsync(db, manager, assignedRegion.Id);

        var destinations = Enumerable.Range(0, 30)
            .Select(index => new Destination
            {
                Name = $"Regional Destination {index:D2}",
                DestinationTypeId = destinationType.Id,
                RegionId = assignedRegion.Id,
                Description = index == 7
                    ? "Searchable destination summary with sunlit orchards."
                    : $"Destination description {index:D2}.",
                Latitude = 42 + index / 100d,
                Longitude = 18 + index / 100d,
                CreatedAt = now.AddMinutes(-index)
            })
            .ToList();

        var unassignedDestination = new Destination
        {
            Name = "Unassigned Regional Destination",
            DestinationTypeId = destinationType.Id,
            RegionId = unassignedRegion.Id,
            Description = "This destination must never appear in assigned-region results.",
            Latitude = 45,
            Longitude = 19,
            CreatedAt = now
        };

        db.Destinations.AddRange(destinations);
        db.Destinations.Add(unassignedDestination);
        await db.SaveChangesAsync();

        db.Locations.Add(new Location
        {
            Name = "Destination Related Location",
            DestinationId = destinations[29].Id,
            LocationTypeId = locationType.Id,
            Latitude = 42.9,
            Longitude = 18.9,
            CreatedAt = now
        });
        await db.SaveChangesAsync();

        return new DestinationsFixture(
            assignedRegion.Id,
            unassignedRegion.Id,
            destinations[7].Id,
            destinations[28].Id,
            destinations[29].Id,
            unassignedDestination.Id);
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

    private sealed record DestinationsFixture(
        int AssignedRegionId,
        int UnassignedRegionId,
        int SearchDestinationId,
        int BareDestinationId,
        int DestinationWithContentId,
        int UnassignedDestinationId);
}
