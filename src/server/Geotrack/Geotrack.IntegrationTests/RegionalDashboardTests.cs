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

public sealed class RegionalDashboardTests
{
    [Fact]
    public async Task Regional_manager_can_fetch_only_assigned_regions()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        var fixture = await CreateDashboardFixtureAsync(app);

        using var client = await LoginAsync(app, "elena@example.com", "password123");
        var regions = await client.GetFromJsonAsync<List<RegionalAssignedRegionDto>>("/api/admin/regional/regions");

        Assert.NotNull(regions);
        Assert.Contains(regions!, region => region.Id == fixture.AssignedRegionId);
        Assert.DoesNotContain(regions!, region => region.Id == fixture.UnassignedRegionId);
    }

    [Fact]
    public async Task Regional_dashboard_rejects_anonymous_non_regional_and_unassigned_region_requests()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        var fixture = await CreateDashboardFixtureAsync(app);

        using var anonymousClient = app.CreateClient();
        var anonymousResponse = await anonymousClient.GetAsync($"/api/admin/regional/dashboard?regionId={fixture.AssignedRegionId}");
        Assert.Equal(HttpStatusCode.Unauthorized, anonymousResponse.StatusCode);

        using var registeredUserClient = await LoginAsync(app, "marko.p@example.com", "password123");
        var nonRegionalResponse = await registeredUserClient.GetAsync($"/api/admin/regional/dashboard?regionId={fixture.AssignedRegionId}");
        Assert.Equal(HttpStatusCode.Forbidden, nonRegionalResponse.StatusCode);

        using var regionalManagerClient = await LoginAsync(app, "elena@example.com", "password123");
        var unassignedResponse = await regionalManagerClient.GetAsync($"/api/admin/regional/dashboard?regionId={fixture.UnassignedRegionId}");
        Assert.Equal(HttpStatusCode.Forbidden, unassignedResponse.StatusCode);
    }

    [Fact]
    public async Task Dashboard_stats_preview_and_activity_are_scoped_to_selected_region()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        var fixture = await CreateDashboardFixtureAsync(app);

        using var client = await LoginAsync(app, "elena@example.com", "password123");
        var dashboard = await client.GetFromJsonAsync<RegionalDashboardDto>(
            $"/api/admin/regional/dashboard?regionId={fixture.AssignedRegionId}");

        Assert.NotNull(dashboard);
        Assert.Equal(fixture.AssignedRegionId, dashboard!.ActiveRegion.Id);

        Assert.Equal(6, dashboard.Stats.PendingRequests);
        Assert.Equal(2, dashboard.Stats.TotalDestinations);
        Assert.Equal(1, dashboard.Stats.DestinationsAddedLastMonth);
        Assert.Equal(2, dashboard.Stats.TotalPlaces);
        Assert.Equal(1, dashboard.Stats.PlacesAddedLastMonth);
        Assert.Equal(2, dashboard.Stats.TotalEvents);
        Assert.Equal(1, dashboard.Stats.EventsAddedLastMonth);

        Assert.Equal(4, dashboard.RequestQueuePreview.Count);
        Assert.All(dashboard.RequestQueuePreview, item =>
        {
            Assert.Equal("PendingReview", item.Status);
            Assert.StartsWith("Scoped Pending", item.Title);
            Assert.DoesNotContain("Out Pending", item.Title);
        });
        Assert.Equal("Scoped Pending 0", dashboard.RequestQueuePreview[0].Title);

        Assert.Contains(dashboard.RecentActivity, item =>
            item.EntityType == "request" &&
            item.Status == "Approved" &&
            item.Description.Contains("Scoped Approved", StringComparison.Ordinal));
        Assert.DoesNotContain(dashboard.RecentActivity, item =>
            item.Description.Contains("Out Approved", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Dashboard_returns_empty_state_data_for_assigned_region_without_content()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        var regionId = await CreateEmptyAssignedRegionAsync(app);

        using var client = await LoginAsync(app, "elena@example.com", "password123");
        var dashboard = await client.GetFromJsonAsync<RegionalDashboardDto>(
            $"/api/admin/regional/dashboard?regionId={regionId}");

        Assert.NotNull(dashboard);
        Assert.Equal(0, dashboard!.Stats.PendingRequests);
        Assert.Equal(0, dashboard.Stats.TotalDestinations);
        Assert.Equal(0, dashboard.Stats.TotalPlaces);
        Assert.Equal(0, dashboard.Stats.TotalEvents);
        Assert.Empty(dashboard.RequestQueuePreview);
        Assert.Empty(dashboard.RecentActivity);
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

    private static async Task<RegionalDashboardFixture> CreateDashboardFixtureAsync(GeotrackApiFactory app)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var now = DateTime.UtcNow;
        var oldDate = now.AddDays(-45);
        var recentDate = now.AddDays(-7);

        var territory = await db.Territories.OrderBy(existing => existing.Id).FirstAsync();
        var destinationType = await db.DestinationTypes.OrderBy(existing => existing.Id).FirstAsync();
        var locationType = await db.LocationTypes.OrderBy(existing => existing.Id).FirstAsync();
        var placeType = await db.PlaceTypes.OrderBy(existing => existing.Id).FirstAsync();
        var eventType = await db.EventTypes.OrderBy(existing => existing.Id).FirstAsync();
        var contributor = await db.Users.SingleAsync(user => user.Email == "marko.p@example.com");
        var reviewer = await db.Users.SingleAsync(user => user.Email == "elena@example.com");

        var assignedRegion = new Region
        {
            Name = $"Dashboard Assigned {Guid.NewGuid():N}",
            TerritoryId = territory.Id,
            CreatedAt = oldDate
        };
        var unassignedRegion = new Region
        {
            Name = $"Dashboard Unassigned {Guid.NewGuid():N}",
            TerritoryId = territory.Id,
            CreatedAt = oldDate
        };

        db.Regions.AddRange(assignedRegion, unassignedRegion);
        await db.SaveChangesAsync();

        await GrantRegionalManagerCoverageAsync(db, reviewer, assignedRegion.Id);

        var oldDestination = new Destination
        {
            Name = "Scoped Old Destination",
            DestinationTypeId = destinationType.Id,
            RegionId = assignedRegion.Id,
            Description = "Older dashboard destination.",
            Latitude = 42.1,
            Longitude = 18.1,
            CreatedAt = oldDate
        };
        var recentDestination = new Destination
        {
            Name = "Scoped Recent Destination",
            DestinationTypeId = destinationType.Id,
            RegionId = assignedRegion.Id,
            Description = "Recent dashboard destination.",
            Latitude = 42.2,
            Longitude = 18.2,
            CreatedAt = recentDate
        };
        var outDestination = new Destination
        {
            Name = "Out Destination",
            DestinationTypeId = destinationType.Id,
            RegionId = unassignedRegion.Id,
            Description = "Unassigned destination.",
            Latitude = 43.1,
            Longitude = 19.1,
            CreatedAt = recentDate
        };

        db.Destinations.AddRange(oldDestination, recentDestination, outDestination);
        await db.SaveChangesAsync();

        var oldLocation = new Location
        {
            Name = "Scoped Old Location",
            DestinationId = oldDestination.Id,
            LocationTypeId = locationType.Id,
            Latitude = 42.1,
            Longitude = 18.1,
            CreatedAt = oldDate
        };
        var recentLocation = new Location
        {
            Name = "Scoped Recent Location",
            DestinationId = recentDestination.Id,
            LocationTypeId = locationType.Id,
            Latitude = 42.2,
            Longitude = 18.2,
            CreatedAt = recentDate
        };
        var outLocation = new Location
        {
            Name = "Out Location",
            DestinationId = outDestination.Id,
            LocationTypeId = locationType.Id,
            Latitude = 43.1,
            Longitude = 19.1,
            CreatedAt = recentDate
        };

        db.Locations.AddRange(oldLocation, recentLocation, outLocation);
        await db.SaveChangesAsync();

        var oldPlace = new Place
        {
            Name = "Scoped Old Place",
            PlaceTypeId = placeType.Id,
            LocationId = oldLocation.Id,
            Description = "Older dashboard place.",
            Latitude = 42.1,
            Longitude = 18.1,
            CreatedAt = oldDate
        };
        var recentPlace = new Place
        {
            Name = "Scoped Recent Place",
            PlaceTypeId = placeType.Id,
            LocationId = recentLocation.Id,
            Description = "Recent dashboard place.",
            Latitude = 42.2,
            Longitude = 18.2,
            CreatedAt = recentDate
        };
        var outPlace = new Place
        {
            Name = "Out Place",
            PlaceTypeId = placeType.Id,
            LocationId = outLocation.Id,
            Description = "Unassigned place.",
            Latitude = 43.1,
            Longitude = 19.1,
            CreatedAt = recentDate
        };

        db.Places.AddRange(oldPlace, recentPlace, outPlace);
        await db.SaveChangesAsync();

        db.Events.AddRange(
            new Event
            {
                Name = "Scoped Old Event",
                EventTypeId = eventType.Id,
                LocationId = oldLocation.Id,
                Description = "Older dashboard event.",
                StartDate = oldDate.AddDays(1),
                EndDate = oldDate.AddDays(2),
                CreatedAt = oldDate
            },
            new Event
            {
                Name = "Scoped Recent Event",
                EventTypeId = eventType.Id,
                LocationId = recentLocation.Id,
                Description = "Recent dashboard event.",
                StartDate = recentDate.AddDays(1),
                EndDate = recentDate.AddDays(2),
                CreatedAt = recentDate
            },
            new Event
            {
                Name = "Out Event",
                EventTypeId = eventType.Id,
                LocationId = outLocation.Id,
                Description = "Unassigned event.",
                StartDate = recentDate.AddDays(1),
                EndDate = recentDate.AddDays(2),
                CreatedAt = recentDate
            });

        for (var index = 0; index < 6; index++)
        {
            db.PlaceContributions.Add(new PlaceContribution
            {
                Type = PlaceContributionType.CreatePlace,
                Status = PlaceContributionStatus.PendingReview,
                UserId = contributor.Id,
                PlaceTypeId = placeType.Id,
                LocationId = recentLocation.Id,
                Name = $"Scoped Pending {index}",
                Description = "Pending scoped dashboard contribution.",
                Latitude = 42.2,
                Longitude = 18.2,
                SubmittedAt = now.AddMinutes(-index),
                CreatedAt = now.AddMinutes(-index)
            });
        }

        db.PlaceContributions.AddRange(
            new PlaceContribution
            {
                Type = PlaceContributionType.CreatePlace,
                Status = PlaceContributionStatus.Approved,
                UserId = contributor.Id,
                ReviewedByUserId = reviewer.Id,
                ReviewedAt = now.AddMinutes(-1),
                PlaceTypeId = placeType.Id,
                LocationId = recentLocation.Id,
                Name = "Scoped Approved",
                Description = "Approved scoped dashboard contribution.",
                Latitude = 42.2,
                Longitude = 18.2,
                SubmittedAt = now.AddDays(-2),
                CreatedAt = now.AddDays(-2)
            },
            new PlaceContribution
            {
                Type = PlaceContributionType.CreatePlace,
                Status = PlaceContributionStatus.Rejected,
                UserId = contributor.Id,
                ReviewedByUserId = reviewer.Id,
                ReviewedAt = now.AddMinutes(-2),
                PlaceTypeId = placeType.Id,
                LocationId = recentLocation.Id,
                Name = "Scoped Rejected",
                Description = "Rejected scoped dashboard contribution.",
                Latitude = 42.2,
                Longitude = 18.2,
                SubmittedAt = now.AddDays(-2),
                CreatedAt = now.AddDays(-2)
            },
            new PlaceContribution
            {
                Type = PlaceContributionType.CreatePlace,
                Status = PlaceContributionStatus.PendingReview,
                UserId = contributor.Id,
                PlaceTypeId = placeType.Id,
                LocationId = outLocation.Id,
                Name = "Out Pending",
                Description = "Pending unassigned contribution.",
                Latitude = 43.1,
                Longitude = 19.1,
                SubmittedAt = now,
                CreatedAt = now
            },
            new PlaceContribution
            {
                Type = PlaceContributionType.CreatePlace,
                Status = PlaceContributionStatus.Approved,
                UserId = contributor.Id,
                ReviewedByUserId = reviewer.Id,
                ReviewedAt = now,
                PlaceTypeId = placeType.Id,
                LocationId = outLocation.Id,
                Name = "Out Approved",
                Description = "Approved unassigned contribution.",
                Latitude = 43.1,
                Longitude = 19.1,
                SubmittedAt = now.AddDays(-2),
                CreatedAt = now.AddDays(-2)
            });

        await db.SaveChangesAsync();

        return new RegionalDashboardFixture(assignedRegion.Id, unassignedRegion.Id);
    }

    private static async Task<int> CreateEmptyAssignedRegionAsync(GeotrackApiFactory app)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var territory = await db.Territories.OrderBy(existing => existing.Id).FirstAsync();
        var reviewer = await db.Users.SingleAsync(user => user.Email == "elena@example.com");
        var region = new Region
        {
            Name = $"Dashboard Empty {Guid.NewGuid():N}",
            TerritoryId = territory.Id,
            CreatedAt = DateTime.UtcNow
        };

        db.Regions.Add(region);
        await db.SaveChangesAsync();

        await GrantRegionalManagerCoverageAsync(db, reviewer, region.Id);

        return region.Id;
    }

    private static async Task GrantRegionalManagerCoverageAsync(AppDbContext db, User reviewer, int regionId)
    {
        var regionalManagerRole = await db.Roles.SingleAsync(role => role.Name == "RegionalManager");

        var hasRole = await db.UserRoles.AnyAsync(userRole =>
            userRole.UserId == reviewer.Id &&
            userRole.RoleId == regionalManagerRole.Id);

        if (!hasRole)
        {
            db.UserRoles.Add(new UserRole
            {
                UserId = reviewer.Id,
                RoleId = regionalManagerRole.Id
            });
        }

        var hasRegionalManager = await db.RegionalManagers.AnyAsync(manager => manager.UserId == reviewer.Id);
        if (!hasRegionalManager)
        {
            db.RegionalManagers.Add(new RegionalManager
            {
                UserId = reviewer.Id
            });
            await db.SaveChangesAsync();
        }

        var hasAssignment = await db.RegionalManagerRegions.AnyAsync(assignment =>
            assignment.UserId == reviewer.Id &&
            assignment.RegionId == regionId);

        if (!hasAssignment)
        {
            db.RegionalManagerRegions.Add(new RegionalManagerRegion
            {
                UserId = reviewer.Id,
                RegionId = regionId,
                CreatedAt = DateTime.UtcNow
            });
        }

        await db.SaveChangesAsync();
    }

    private sealed record RegionalDashboardFixture(int AssignedRegionId, int UnassignedRegionId);
}
