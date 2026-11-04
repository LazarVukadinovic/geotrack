using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Geotrack.Application.Analytics;
using Geotrack.Application.DTOs.Admin;
using Geotrack.Application.DTOs.Auth;
using Geotrack.Application.Security;
using Geotrack.Domain.Entities.Analytics;
using Geotrack.Domain.Entities.Core;
using Geotrack.Domain.Entities.Reviews;
using Geotrack.Domain.Entities.Users;
using Geotrack.Infrastructure;
using Geotrack.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using CoreEvent = Geotrack.Domain.Entities.Core.Event;

namespace Geotrack.IntegrationTests;

public sealed class TerritoryAnalyticsTests
{
    [Fact]
    public async Task Territory_manager_loads_real_analytics_for_assigned_territory()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        var fixture = await CreateTerritoryAnalyticsFixtureAsync(app);

        using var client = await LoginAdminAsync(app, "mika@gmail.com", "TEST-only-Fixture-9284!");
        var overview = await client.GetFromJsonAsync<TerritoryAnalyticsOverviewDto>(
            $"/api/admin/territory/analytics/overview?territoryId={fixture.AssignedTerritoryId}&period=6m&status=active&take=5");

        Assert.NotNull(overview);
        Assert.Equal(fixture.AssignedTerritoryId, overview!.ActiveTerritory!.Id);
        Assert.Equal(5, overview.Totals.TotalPageViews);
        Assert.Equal(4, overview.Totals.UniqueVisitors);
        Assert.Equal(2, overview.Totals.ExternalLinkClicks);
        Assert.Equal(3, overview.Totals.TotalSaves);
        Assert.Equal(4, overview.Totals.TotalReviews);
        Assert.Equal(4, overview.Totals.AverageRating);
        Assert.Equal(400, overview.Comparison.PageViewsChangePercent);

        Assert.NotNull(overview.TopRegion);
        Assert.Equal(fixture.PrimaryRegionId, overview.TopRegion!.RegionId);
        Assert.Equal(overview.TopRegion.Score, overview.TopRegion.HealthScore);
        Assert.False(string.IsNullOrWhiteSpace(overview.TopRegion.StatusLabel));
        Assert.NotNull(overview.FastestGrowing);
        Assert.Equal(fixture.PrimaryRegionId, overview.FastestGrowing!.RegionId);
        Assert.Contains(overview.TopDestinations, item => item.Id == fixture.DestinationId && item.Views == 1);
        Assert.Contains(overview.TopPlaces, item => item.Id == fixture.PlaceId && item.Views == 1);
        Assert.Contains(overview.TopEvents, item => item.Id == fixture.EventId && item.Saves == 1);
        Assert.DoesNotContain(overview.TopDestinations, item => item.Id == fixture.OutDestinationId);

        var primaryRow = Assert.Single(
            overview.RegionalPerformance.Items,
            row => row.RegionId == fixture.PrimaryRegionId);
        Assert.Equal(4, primaryRow.PageViews);
        Assert.Equal(3, primaryRow.Visitors);
        Assert.Equal(3, primaryRow.Saves);
        Assert.Equal(4, primaryRow.AverageRating);
        Assert.Equal("Assigned", primaryRow.ManagerStatus);
        Assert.InRange(primaryRow.HealthScore, 0, 100);
        Assert.Equal(primaryRow.HealthScore, primaryRow.QualityScore);
        Assert.Equal(primaryRow.HealthScore, primaryRow.Health!.Score);

        var primaryQuality = Assert.Single(
            overview.ContentQualityByRegion,
            row => row.RegionId == fixture.PrimaryRegionId);
        Assert.Equal(4, primaryQuality.TotalReviews);
        Assert.Equal(4, primaryQuality.AverageRating);
        Assert.Equal(5, overview.PageViewsOverTime.Series.Sum(series => series.Values.Sum()));
    }

    [Fact]
    public async Task Territory_analytics_enforces_auth_and_assignment_scope()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        var fixture = await CreateTerritoryAnalyticsFixtureAsync(app);

        using var anonymousClient = app.CreateClient();
        var anonymous = await anonymousClient.GetAsync(
            $"/api/admin/territory/analytics/overview?territoryId={fixture.AssignedTerritoryId}");
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);

        using var regionalClient = await LoginAdminAsync(app, "elena@example.com", "password123");
        var regional = await regionalClient.GetAsync(
            $"/api/admin/territory/analytics/overview?territoryId={fixture.AssignedTerritoryId}");
        Assert.Equal(HttpStatusCode.Forbidden, regional.StatusCode);

        using var territoryClient = await LoginAdminAsync(app, "mika@gmail.com", "TEST-only-Fixture-9284!");
        var unassignedTerritory = await territoryClient.GetAsync(
            $"/api/admin/territory/analytics/overview?territoryId={fixture.UnassignedTerritoryId}");
        Assert.Equal(HttpStatusCode.Forbidden, unassignedTerritory.StatusCode);

        var unassignedRegion = await territoryClient.GetAsync(
            $"/api/admin/territory/analytics/overview?territoryId={fixture.AssignedTerritoryId}&regionIds={fixture.OutRegionId}");
        Assert.Equal(HttpStatusCode.Forbidden, unassignedRegion.StatusCode);
    }

    [Fact]
    public async Task Territory_analytics_filters_period_region_and_status()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        var fixture = await CreateTerritoryAnalyticsFixtureAsync(app);
        using var client = await LoginAdminAsync(app, "mika@gmail.com", "TEST-only-Fixture-9284!");

        var secondary = await client.GetFromJsonAsync<TerritoryAnalyticsOverviewDto>(
            $"/api/admin/territory/analytics/overview?territoryId={fixture.AssignedTerritoryId}&period=6m&status=active&regionIds={fixture.SecondaryRegionId}");
        Assert.NotNull(secondary);
        Assert.Equal(1, secondary!.Totals.TotalPageViews);
        Assert.All(secondary.RegionalPerformance.Items, row => Assert.Equal(fixture.SecondaryRegionId, row.RegionId));

        var lastDay = await client.GetFromJsonAsync<TerritoryAnalyticsOverviewDto>(
            $"/api/admin/territory/analytics/overview?territoryId={fixture.AssignedTerritoryId}&period=24h&status=active");
        Assert.NotNull(lastDay);
        Assert.Equal(0, lastDay!.Totals.TotalPageViews);
        Assert.Empty(lastDay.TopEntities);

        var inactive = await client.GetFromJsonAsync<TerritoryAnalyticsOverviewDto>(
            $"/api/admin/territory/analytics/overview?territoryId={fixture.AssignedTerritoryId}&period=6m&status=inactive&regionIds={fixture.PrimaryRegionId}");
        Assert.NotNull(inactive);
        Assert.Equal(1, inactive!.Totals.TotalPageViews);
        Assert.Contains(inactive.TopDestinations, item => item.Id == fixture.InactiveDestinationId);

        var invalidPeriod = await client.GetAsync(
            $"/api/admin/territory/analytics/overview?territoryId={fixture.AssignedTerritoryId}&period=never");
        Assert.Equal(HttpStatusCode.BadRequest, invalidPeriod.StatusCode);

        var invalidStatus = await client.GetAsync(
            $"/api/admin/territory/analytics/overview?territoryId={fixture.AssignedTerritoryId}&status=archived");
        Assert.Equal(HttpStatusCode.BadRequest, invalidStatus.StatusCode);
    }

    [Fact]
    public async Task Territory_analytics_empty_data_and_export_are_safe_and_scoped()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        var fixture = await CreateTerritoryAnalyticsFixtureAsync(app);
        var emptyTerritoryId = await CreateEmptyAssignedTerritoryAsync(app);
        using var client = await LoginAdminAsync(app, "mika@gmail.com", "TEST-only-Fixture-9284!");

        var empty = await client.GetFromJsonAsync<TerritoryAnalyticsOverviewDto>(
            $"/api/admin/territory/analytics/overview?territoryId={emptyTerritoryId}&period=6m&status=active");
        Assert.NotNull(empty);
        Assert.Equal(0, empty!.Totals.TotalPageViews);
        Assert.Empty(empty.TopEntities);
        Assert.Empty(empty.RegionalPerformance.Items);

        var export = await client.GetAsync(
            $"/api/admin/territory/analytics/export?territoryId={fixture.AssignedTerritoryId}&period=6m&status=active");
        export.EnsureSuccessStatusCode();
        Assert.Equal("text/csv", export.Content.Headers.ContentType?.MediaType);
        var csv = await export.Content.ReadAsStringAsync();
        Assert.Contains("Regional Performance", csv);
        Assert.Contains(fixture.PrimaryRegionName, csv);
        Assert.DoesNotContain("Out Analytics", csv);
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

    private static async Task<TerritoryAnalyticsFixture> CreateTerritoryAnalyticsFixtureAsync(GeotrackApiFactory app)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var now = DateTime.UtcNow;
        var current = now.AddDays(-10);
        var previous = now.AddMonths(-7);
        var destinationType = await db.DestinationTypes.OrderBy(type => type.Id).FirstAsync();
        var locationType = await db.LocationTypes.OrderBy(type => type.Id).FirstAsync();
        var placeType = await db.PlaceTypes.OrderBy(type => type.Id).FirstAsync();
        var eventType = await db.EventTypes.OrderBy(type => type.Id).FirstAsync();
        var territoryManager = await db.Users.SingleAsync(user => user.Email == "mika@gmail.com");
        var regionalManager = await db.Users.SingleAsync(user => user.Email == "elena@example.com");
        var user = await db.Users.SingleAsync(existing => existing.Email == "marko.p@example.com");

        var assignedTerritory = new Territory
        {
            Name = $"Territory Analytics Assigned {Guid.NewGuid():N}",
            CreatedAt = now.AddYears(-1)
        };
        var unassignedTerritory = new Territory
        {
            Name = $"Territory Analytics Unassigned {Guid.NewGuid():N}",
            CreatedAt = now.AddYears(-1)
        };
        db.Territories.AddRange(assignedTerritory, unassignedTerritory);
        await db.SaveChangesAsync();

        db.TerritoryManagerTerritories.Add(new TerritoryManagerTerritory
        {
            UserId = territoryManager.Id,
            TerritoryId = assignedTerritory.Id,
            CreatedAt = now
        });

        var primaryRegion = new Region
        {
            Name = $"Primary Analytics Region {Guid.NewGuid():N}",
            TerritoryId = assignedTerritory.Id,
            CreatedAt = now.AddYears(-1)
        };
        var secondaryRegion = new Region
        {
            Name = $"Secondary Analytics Region {Guid.NewGuid():N}",
            TerritoryId = assignedTerritory.Id,
            CreatedAt = now.AddYears(-1)
        };
        var outRegion = new Region
        {
            Name = $"Out Analytics Region {Guid.NewGuid():N}",
            TerritoryId = unassignedTerritory.Id,
            CreatedAt = now.AddYears(-1)
        };
        db.Regions.AddRange(primaryRegion, secondaryRegion, outRegion);
        await db.SaveChangesAsync();

        await GrantRegionalManagerCoverageAsync(db, regionalManager, primaryRegion.Id);

        var destination = new Destination
        {
            Name = "Assigned Analytics Destination",
            DestinationTypeId = destinationType.Id,
            RegionId = primaryRegion.Id,
            Description = "Assigned analytics destination.",
            Latitude = 45.1,
            Longitude = 15.1,
            CreatedAt = now.AddMonths(-2),
            Status = "Published"
        };
        var inactiveDestination = new Destination
        {
            Name = "Inactive Analytics Destination",
            DestinationTypeId = destinationType.Id,
            RegionId = primaryRegion.Id,
            Description = "Inactive analytics destination.",
            Latitude = 45.12,
            Longitude = 15.12,
            CreatedAt = now.AddMonths(-2),
            Status = "Draft"
        };
        var secondaryDestination = new Destination
        {
            Name = "Secondary Analytics Destination",
            DestinationTypeId = destinationType.Id,
            RegionId = secondaryRegion.Id,
            Description = "Secondary analytics destination.",
            Latitude = 45.2,
            Longitude = 15.2,
            CreatedAt = now.AddMonths(-2),
            Status = "Published"
        };
        var outDestination = new Destination
        {
            Name = "Out Analytics Destination",
            DestinationTypeId = destinationType.Id,
            RegionId = outRegion.Id,
            Description = "Out analytics destination.",
            Latitude = 46.1,
            Longitude = 16.1,
            CreatedAt = now.AddMonths(-2),
            Status = "Published"
        };
        db.Destinations.AddRange(destination, inactiveDestination, secondaryDestination, outDestination);
        await db.SaveChangesAsync();

        var location = new Location
        {
            Name = "Assigned Analytics Location",
            DestinationId = destination.Id,
            LocationTypeId = locationType.Id,
            Latitude = 45.1,
            Longitude = 15.1,
            CreatedAt = now.AddMonths(-2),
            Status = "Published"
        };
        var outLocation = new Location
        {
            Name = "Out Analytics Location",
            DestinationId = outDestination.Id,
            LocationTypeId = locationType.Id,
            Latitude = 46.1,
            Longitude = 16.1,
            CreatedAt = now.AddMonths(-2),
            Status = "Published"
        };
        db.Locations.AddRange(location, outLocation);
        await db.SaveChangesAsync();

        var place = new Place
        {
            Name = "Assigned Analytics Place",
            PlaceTypeId = placeType.Id,
            LocationId = location.Id,
            Description = "Assigned analytics place.",
            Latitude = 45.1,
            Longitude = 15.1,
            CreatedAt = now.AddMonths(-2),
            Status = "Published"
        };
        db.Places.Add(place);
        await db.SaveChangesAsync();

        var eventEntity = new CoreEvent
        {
            Name = "Assigned Analytics Event",
            EventTypeId = eventType.Id,
            DestinationId = destination.Id,
            LocationId = location.Id,
            Description = "Assigned analytics event.",
            StartDate = now.AddDays(20),
            EndDate = now.AddDays(21),
            CreatedAt = now.AddMonths(-2),
            Status = "Published"
        };
        db.Events.Add(eventEntity);
        await db.SaveChangesAsync();

        var userSession = new AnonymousSession
        {
            Id = Guid.NewGuid(),
            CreatedAt = current,
            LastSeenAt = current
        };
        var anonymousSession = new AnonymousSession
        {
            Id = Guid.NewGuid(),
            CreatedAt = current,
            LastSeenAt = current
        };
        var secondAnonymousSession = new AnonymousSession
        {
            Id = Guid.NewGuid(),
            CreatedAt = current,
            LastSeenAt = current
        };
        db.AnonymousSessions.AddRange(userSession, anonymousSession, secondAnonymousSession);

        db.AnalyticsEvents.AddRange(
            AnalyticsView(user.Id, userSession.Id, AnalyticsEntityTypes.Destination, destination.Id, destination.Id, current),
            AnalyticsView(user.Id, userSession.Id, AnalyticsEntityTypes.Event, eventEntity.Id, destination.Id, current.AddHours(1)),
            AnalyticsView(null, anonymousSession.Id, AnalyticsEntityTypes.Place, place.Id, destination.Id, current.AddHours(2)),
            AnalyticsView(null, secondAnonymousSession.Id, AnalyticsEntityTypes.Location, location.Id, destination.Id, current.AddHours(3)),
            AnalyticsView(regionalManager.Id, null, AnalyticsEntityTypes.Destination, secondaryDestination.Id, secondaryDestination.Id, current.AddHours(4)),
            AnalyticsView(user.Id, userSession.Id, AnalyticsEntityTypes.Destination, destination.Id, destination.Id, previous),
            AnalyticsView(user.Id, userSession.Id, AnalyticsEntityTypes.Destination, inactiveDestination.Id, inactiveDestination.Id, current.AddHours(5)),
            AnalyticsView(user.Id, userSession.Id, AnalyticsEntityTypes.Destination, outDestination.Id, outDestination.Id, current.AddHours(6)),
            AnalyticsExternal(AnalyticsEventTypes.OutboundLinkClicked, user.Id, AnalyticsEntityTypes.Place, place.Id, destination.Id, current.AddHours(7)),
            AnalyticsExternal(AnalyticsEventTypes.DirectionsStarted, null, AnalyticsEntityTypes.Event, eventEntity.Id, destination.Id, current.AddHours(8)));

        var collection = new Collection
        {
            Name = "Analytics Collection",
            UserId = user.Id,
            CreatedAt = current
        };
        db.Collections.Add(collection);
        await db.SaveChangesAsync();

        db.Favorites.Add(new Favorite
        {
            UserId = user.Id,
            EntityType = AnalyticsEntityTypes.Place,
            EntityId = place.Id,
            CreatedAt = current
        });
        db.CollectionItems.AddRange(
            new CollectionItem
            {
                CollectionId = collection.Id,
                DestinationId = destination.Id,
                CreatedAt = current
            },
            new CollectionItem
            {
                CollectionId = collection.Id,
                EventId = eventEntity.Id,
                CreatedAt = current
            },
            new CollectionItem
            {
                CollectionId = collection.Id,
                DestinationId = outDestination.Id,
                CreatedAt = current
            });

        db.DestinationReviews.Add(new DestinationReview
        {
            UserId = user.Id,
            DestinationId = destination.Id,
            Rating = 4,
            Comment = "Good destination.",
            CreatedAt = current
        });
        db.LocationReviews.Add(new LocationReview
        {
            UserId = user.Id,
            LocationId = location.Id,
            Rating = 4,
            Comment = "Good location.",
            CreatedAt = current
        });
        db.PlaceReviews.Add(new PlaceReview
        {
            UserId = user.Id,
            PlaceId = place.Id,
            Rating = 5,
            Comment = "Great place.",
            CreatedAt = current
        });
        db.EventReviews.Add(new EventReview
        {
            UserId = user.Id,
            EventId = eventEntity.Id,
            Rating = 3,
            Comment = "Fine event.",
            CreatedAt = current
        });

        await db.SaveChangesAsync();

        return new TerritoryAnalyticsFixture(
            assignedTerritory.Id,
            unassignedTerritory.Id,
            primaryRegion.Id,
            primaryRegion.Name,
            secondaryRegion.Id,
            outRegion.Id,
            destination.Id,
            inactiveDestination.Id,
            outDestination.Id,
            place.Id,
            eventEntity.Id);
    }

    private static async Task<int> CreateEmptyAssignedTerritoryAsync(GeotrackApiFactory app)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var manager = await db.Users.SingleAsync(user => user.Email == "mika@gmail.com");
        var territory = new Territory
        {
            Name = $"Empty Analytics Territory {Guid.NewGuid():N}",
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

    private static async Task GrantRegionalManagerCoverageAsync(AppDbContext db, User user, int regionId)
    {
        var regionalManagerRole = await db.Roles.SingleAsync(role => role.Name == RoleNames.RegionalManager);

        if (!await db.UserRoles.AnyAsync(userRole =>
            userRole.UserId == user.Id &&
            userRole.RoleId == regionalManagerRole.Id))
        {
            db.UserRoles.Add(new UserRole
            {
                UserId = user.Id,
                RoleId = regionalManagerRole.Id
            });
        }

        if (!await db.RegionalManagerRegions.AnyAsync(assignment =>
            assignment.UserId == user.Id &&
            assignment.RegionId == regionId))
        {
            db.RegionalManagerRegions.Add(new RegionalManagerRegion
            {
                UserId = user.Id,
                RegionId = regionId,
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            });
        }

        await db.SaveChangesAsync();
    }

    private static AnalyticsEvent AnalyticsView(
        int? userId,
        Guid? sessionId,
        string entityType,
        int entityId,
        int destinationId,
        DateTime occurredAt) =>
        new()
        {
            UserId = userId,
            AnonymousSessionId = sessionId,
            EventType = AnalyticsEventTypes.DetailView,
            EntityType = entityType,
            EntityId = entityId,
            DestinationId = destinationId,
            OccurredAt = occurredAt,
            MetadataJson = "{}"
        };

    private static AnalyticsEvent AnalyticsExternal(
        string eventType,
        int? userId,
        string entityType,
        int entityId,
        int destinationId,
        DateTime occurredAt) =>
        new()
        {
            UserId = userId,
            EventType = eventType,
            EntityType = entityType,
            EntityId = entityId,
            DestinationId = destinationId,
            OccurredAt = occurredAt,
            MetadataJson = "{}"
        };

    private sealed record TerritoryAnalyticsFixture(
        int AssignedTerritoryId,
        int UnassignedTerritoryId,
        int PrimaryRegionId,
        string PrimaryRegionName,
        int SecondaryRegionId,
        int OutRegionId,
        int DestinationId,
        int InactiveDestinationId,
        int OutDestinationId,
        int PlaceId,
        int EventId);
}
