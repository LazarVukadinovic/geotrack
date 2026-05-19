using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
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

namespace Geotrack.IntegrationTests;

public sealed class TerritoryDashboardTests
{
    [Fact]
    public async Task Territory_manager_can_load_dashboard_for_assigned_territory()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        var fixture = await CreateTerritoryDashboardFixtureAsync(app);

        using var client = await LoginAdminAsync(app, "mika@gmail.com", "Proba123!");
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

        using var territoryClient = await LoginAdminAsync(app, "mika@gmail.com", "Proba123!");
        var unassigned = await territoryClient.GetAsync(
            $"/api/admin/territory/dashboard?territoryId={fixture.UnassignedTerritoryId}");
        Assert.Equal(HttpStatusCode.Forbidden, unassigned.StatusCode);
    }

    [Fact]
    public async Task Dashboard_stats_regions_and_top_content_are_scoped_to_assigned_territory()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        var fixture = await CreateTerritoryDashboardFixtureAsync(app);

        using var client = await LoginAdminAsync(app, "mika@gmail.com", "Proba123!");
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
        Assert.Equal(2, dashboard.Stats.TotalPlaces);
        Assert.Equal(1, dashboard.Stats.PlacesAddedLastMonth);
        Assert.Equal(2, dashboard.Stats.TotalEvents);
        Assert.Equal(1, dashboard.Stats.EventsAddedLastMonth);

        var health = Assert.Single(dashboard.RegionsHealth);
        Assert.Equal(fixture.AssignedRegionId, health.RegionId);
        Assert.Equal(2, health.DestinationsCount);
        Assert.Equal(2, health.LocationsCount);
        Assert.Equal(2, health.PlacesCount);
        Assert.Equal(2, health.EventsCount);
        Assert.Equal(1, health.PendingRequestsCount);
        Assert.InRange(health.ActivityScore, 0, 100);
        Assert.False(string.IsNullOrWhiteSpace(health.ManagerDisplayName));

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
    public async Task Territory_directories_return_only_assigned_territory_data()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        var fixture = await CreateTerritoryDashboardFixtureAsync(app);

        using var client = await LoginAdminAsync(app, "mika@gmail.com", "Proba123!");

        var regions = await client.GetFromJsonAsync<TerritoryRegionDirectoryResponseDto>(
            $"/api/admin/territory/regions?territoryId={fixture.AssignedTerritoryId}");
        Assert.NotNull(regions);
        var region = Assert.Single(regions!.Items);
        Assert.Equal(fixture.AssignedRegionId, region.Id);
        Assert.Equal(2, region.DestinationsCount);

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
    public async Task Empty_assigned_territory_returns_safe_zero_dashboard()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        var territoryId = await CreateEmptyAssignedTerritoryAsync(app);

        using var client = await LoginAdminAsync(app, "mika@gmail.com", "Proba123!");
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
        var eventType = await db.EventTypes.OrderBy(existing => existing.Id).FirstAsync();
        var territoryManager = await db.Users.SingleAsync(user => user.Email == "mika@gmail.com");
        var regionalManager = await db.Users.SingleAsync(user => user.Email == "elena@example.com");
        var user = await db.Users.SingleAsync(existing => existing.Email == "marko.p@example.com");

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

        db.Places.AddRange(oldPlace, recentPlace, outPlace);
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
            oldDestination.Id,
            oldLocation.Id,
            recentPlace.Id,
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

    private sealed record TerritoryDashboardFixture(
        int AssignedTerritoryId,
        string AssignedTerritoryName,
        int UnassignedTerritoryId,
        int AssignedRegionId,
        int AssignedDestinationId,
        int AssignedLocationId,
        int AssignedPlaceId,
        int UpcomingEventId);
}
