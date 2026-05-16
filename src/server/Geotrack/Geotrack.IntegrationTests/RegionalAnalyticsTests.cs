using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Globalization;
using Geotrack.Application.Analytics;
using Geotrack.Application.DTOs.Admin;
using Geotrack.Application.DTOs.Auth;
using Geotrack.Domain.Entities.Analytics;
using Geotrack.Domain.Entities.Core;
using Geotrack.Domain.Entities.Reviews;
using Geotrack.Domain.Entities.Users;
using Geotrack.Infrastructure;
using Geotrack.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Geotrack.IntegrationTests;

public sealed class RegionalAnalyticsTests
{
    [Fact]
    public async Task Regional_manager_can_load_analytics_for_assigned_region_only()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        var fixture = await CreateAnalyticsFixtureAsync(app);

        using var client = await LoginAsync(app, "elena@example.com", "password123");
        var overview = await client.GetFromJsonAsync<RegionalAnalyticsOverviewDto>(
            $"/api/admin/regional/analytics/overview?regionId={fixture.AssignedRegionId}&period=30d&entityType=all&sort=views_desc");

        Assert.NotNull(overview);
        Assert.Equal(fixture.AssignedRegionId, overview!.ActiveRegion.Id);
        Assert.Equal(5, overview.Totals.PageViews);
        Assert.Equal(2, overview.Totals.UniqueVisitors);
        Assert.Equal(2, overview.Totals.Saves);
        Assert.Equal(2, overview.Totals.ExternalClicks);
        Assert.Equal(2, overview.Totals.TotalReviews);
        Assert.Equal(4.5, overview.Totals.AverageRating);
        Assert.Equal(2, overview.PendingRequestsCount);
        Assert.Equal(1, overview.VisitorBehavior.LoggedInVisitors);
        Assert.Equal(1, overview.VisitorBehavior.AnonymousVisitors);
        Assert.Equal(1, overview.VisitorBehavior.ReturningVisitors);
        Assert.Equal(50, overview.VisitorBehavior.ReturningVisitorRatePercent);
        Assert.True(overview.VisitorBehavior.HasTimeSpentData);
        Assert.Equal(fixture.EventId, overview.VisitorBehavior.TopTimeSpentEntity?.EntityId);
        Assert.Equal(AnalyticsEntityTypes.Event, overview.VisitorBehavior.TopTimeSpentEntity?.EntityType);
        Assert.Equal(180, overview.VisitorBehavior.TopTimeSpentEntity?.AverageTimeSpentSeconds);

        Assert.Contains(overview.EntityRows, row =>
            row.EntityType == AnalyticsEntityTypes.Place &&
            row.EntityId == fixture.PlaceId &&
            row.PageViews == 2 &&
            row.Saves == 1 &&
            row.ExternalClicks == 1 &&
            row.ReviewCount == 2);
        Assert.DoesNotContain(overview.EntityRows, row => row.Name.StartsWith("Out Region", StringComparison.Ordinal));
        Assert.Contains(overview.PageViewsTrend, point => point.Views > 0);
        Assert.Contains(overview.EntityTypeDistribution, item => item.EntityType == AnalyticsEntityTypes.Place && item.Count == 1);
        Assert.Equal(fixture.LocationId, overview.ActionableInsights.TopViewedLocation?.EntityId);

        var unassignedResponse = await client.GetAsync(
            $"/api/admin/regional/analytics/overview?regionId={fixture.UnassignedRegionId}&period=30d");
        Assert.Equal(HttpStatusCode.Forbidden, unassignedResponse.StatusCode);
    }

    [Fact]
    public async Task Analytics_filters_search_sort_and_export_are_scoped_to_region()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        var fixture = await CreateAnalyticsFixtureAsync(app);

        using var client = await LoginAsync(app, "elena@example.com", "password123");
        var overview = await client.GetFromJsonAsync<RegionalAnalyticsOverviewDto>(
            $"/api/admin/regional/analytics/overview?regionId={fixture.AssignedRegionId}&period=30d&entityType=place&search=Assigned%20Place&sort=rating_desc");

        Assert.NotNull(overview);
        var row = Assert.Single(overview!.EntityRows);
        Assert.Equal(AnalyticsEntityTypes.Place, row.EntityType);
        Assert.Equal(fixture.PlaceId, row.EntityId);
        Assert.Equal(2, row.PageViews);
        Assert.Equal(4.5, row.AverageRating);

        var export = await client.GetAsync(
            $"/api/admin/regional/analytics/export?regionId={fixture.AssignedRegionId}&period=30d&entityType=all&sort=views_desc");
        export.EnsureSuccessStatusCode();
        var csv = await export.Content.ReadAsStringAsync();
        Assert.Contains("Assigned Place", csv);
        Assert.Contains("VisitorBehavior", csv);
        Assert.Contains("LoggedInVisitors", csv);
        Assert.DoesNotContain("Out Region Place", csv);
        Assert.Equal(AnalyticsEntityTypes.Place, overview.VisitorBehavior.TopTimeSpentEntity?.EntityType);
        Assert.Equal(fixture.PlaceId, overview.VisitorBehavior.TopTimeSpentEntity?.EntityId);
        Assert.Equal(2, overview.VisitorBehavior.TopTimeSpentEntity?.VisitCount);
    }

    [Fact]
    public async Task Analytics_empty_region_and_sidebar_summary_return_safe_zero_state()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        var regionId = await CreateEmptyAssignedRegionAsync(app);

        using var client = await LoginAsync(app, "elena@example.com", "password123");
        var overview = await client.GetFromJsonAsync<RegionalAnalyticsOverviewDto>(
            $"/api/admin/regional/analytics/overview?regionId={regionId}&period=30d");

        Assert.NotNull(overview);
        Assert.Equal(0, overview!.Totals.PageViews);
        Assert.Equal(0, overview.Totals.UniqueVisitors);
        Assert.Equal(0, overview.Totals.Saves);
        Assert.Equal(0, overview.Totals.ExternalClicks);
        Assert.Equal(0, overview.Totals.TotalReviews);
        Assert.Equal(0, overview.VisitorBehavior.LoggedInVisitors);
        Assert.Equal(0, overview.VisitorBehavior.AnonymousVisitors);
        Assert.Equal(0, overview.VisitorBehavior.ReturningVisitors);
        Assert.False(overview.VisitorBehavior.HasTimeSpentData);
        Assert.Null(overview.VisitorBehavior.TopTimeSpentEntity);
        Assert.Empty(overview.EntityRows);
        Assert.Contains(overview.ActionableInsights.Messages, message =>
            message.Contains("No entities", StringComparison.OrdinalIgnoreCase));

        var summary = await client.GetFromJsonAsync<RegionalSidebarSummaryDto>(
            $"/api/admin/regional/sidebar-summary?regionId={regionId}");

        Assert.NotNull(summary);
        Assert.Equal(regionId, summary!.ActiveRegion?.Id);
        Assert.Equal(0, summary.PendingRequestsCount);
    }

    [Fact]
    public async Task Analytics_region_with_inventory_but_no_engagement_returns_zero_metrics_and_empty_performance_rows()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        var regionId = await CreateInventoryOnlyAssignedRegionAsync(app);

        using var client = await LoginAsync(app, "elena@example.com", "password123");
        var overview = await client.GetFromJsonAsync<RegionalAnalyticsOverviewDto>(
            $"/api/admin/regional/analytics/overview?regionId={regionId}&period=30d&entityType=all&skip=0&take=25&sort=views_desc");

        Assert.NotNull(overview);
        Assert.Equal(0, overview!.Totals.PageViews);
        Assert.Equal(0, overview.Totals.UniqueVisitors);
        Assert.Equal(0, overview.Totals.Saves);
        Assert.Equal(0, overview.Totals.ExternalClicks);
        Assert.Equal(0, overview.Totals.TotalReviews);
        Assert.Empty(overview.EntityRows);
        Assert.Empty(overview.TopPerformingEntities);
        Assert.Equal(0, overview.Pagination.TotalCount);
        Assert.False(overview.Pagination.HasMore);
        Assert.NotEmpty(overview.PageViewsTrend);
        Assert.All(overview.PageViewsTrend, point => Assert.Equal(0, point.Views));
        Assert.Contains(overview.EntityTypeDistribution, item => item.EntityType == AnalyticsEntityTypes.Destination && item.Count == 1);
        Assert.Contains(overview.EntityTypeDistribution, item => item.EntityType == AnalyticsEntityTypes.Location && item.Count == 1);
        Assert.Contains(overview.EntityTypeDistribution, item => item.EntityType == AnalyticsEntityTypes.Place && item.Count == 1);
        Assert.Contains(overview.EntityTypeDistribution, item => item.EntityType == AnalyticsEntityTypes.Event && item.Count == 1);
        Assert.Contains(overview.ActionableInsights.Messages, message =>
            message.Contains("No engagement", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Analytics_invalid_query_filters_return_bad_request_not_server_error()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        var regionId = await CreateEmptyAssignedRegionAsync(app);

        using var client = await LoginAsync(app, "elena@example.com", "password123");

        var invalidPeriod = await client.GetAsync(
            $"/api/admin/regional/analytics/overview?regionId={regionId}&period=forever&entityType=all&sort=views_desc");
        var invalidEntityType = await client.GetAsync(
            $"/api/admin/regional/analytics/overview?regionId={regionId}&period=30d&entityType=unknown&sort=views_desc");
        var invalidSort = await client.GetAsync(
            $"/api/admin/regional/analytics/overview?regionId={regionId}&period=30d&entityType=all&sort=not_a_sort");

        Assert.Equal(HttpStatusCode.BadRequest, invalidPeriod.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, invalidEntityType.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, invalidSort.StatusCode);

        var invalidYearsBack = await client.GetAsync(
            $"/api/admin/regional/analytics/monthly-visits?regionId={regionId}&selectedYearsBack=4");
        Assert.Equal(HttpStatusCode.BadRequest, invalidYearsBack.StatusCode);
    }

    [Fact]
    public async Task Monthly_visits_are_grouped_by_trailing_years_and_zero_filled()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        var fixture = await CreateAnalyticsFixtureAsync(app);
        await AddMonthlyVisitComparisonEventsAsync(app, fixture.PlaceId);

        using var client = await LoginAsync(app, "elena@example.com", "password123");
        var monthly = await client.GetFromJsonAsync<RegionalMonthlyVisitsDto>(
            $"/api/admin/regional/analytics/monthly-visits?regionId={fixture.AssignedRegionId}&selectedYearsBack=3&entityType=place");

        Assert.NotNull(monthly);
        Assert.Equal(fixture.AssignedRegionId, monthly!.ActiveRegion.Id);
        Assert.Equal(3, monthly.SelectedYearsBack);
        Assert.Equal(12, monthly.MonthLabels.Count);
        Assert.Equal(3, monthly.Series.Count);
        Assert.All(monthly.Series, series => Assert.Equal(12, series.MonthlyVisitCounts.Count));
        Assert.Contains(AnalyticsEventTypes.DetailView, monthly.VisitEventTypes);
        Assert.Contains(AnalyticsEventTypes.PageView, monthly.VisitEventTypes);

        var currentMonthLabel = DateTime.UtcNow.ToString("MMM", CultureInfo.InvariantCulture);
        var currentMonthIndex = monthly.MonthLabels.IndexOf(currentMonthLabel);
        Assert.True(currentMonthIndex >= 0);
        Assert.Equal(1, monthly.Series[0].MonthlyVisitCounts[currentMonthIndex]);
        Assert.Equal(1, monthly.Series[1].MonthlyVisitCounts[currentMonthIndex]);
        Assert.True(monthly.Series[2].MonthlyVisitCounts[currentMonthIndex] >= 1);
        Assert.Contains(monthly.Series.SelectMany(series => series.MonthlyVisitCounts), count => count == 0);
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

    private static async Task<RegionalAnalyticsFixture> CreateAnalyticsFixtureAsync(GeotrackApiFactory app)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var now = DateTime.UtcNow;
        var old = now.AddDays(-45);
        var recent = now.AddDays(-3);

        var territory = await db.Territories.OrderBy(item => item.Id).FirstAsync();
        var destinationType = await db.DestinationTypes.OrderBy(item => item.Id).FirstAsync();
        var locationType = await db.LocationTypes.OrderBy(item => item.Id).FirstAsync();
        var placeType = await db.PlaceTypes.OrderBy(item => item.Id).FirstAsync();
        var eventType = await db.EventTypes.OrderBy(item => item.Id).FirstAsync();
        var manager = await db.Users.SingleAsync(user => user.Email == "elena@example.com");
        var user = await db.Users.SingleAsync(user => user.Email == "marko.p@example.com");

        var assignedRegion = new Region
        {
            Name = $"Analytics Assigned {Guid.NewGuid():N}",
            TerritoryId = territory.Id,
            CreatedAt = old
        };
        var unassignedRegion = new Region
        {
            Name = $"Analytics Unassigned {Guid.NewGuid():N}",
            TerritoryId = territory.Id,
            CreatedAt = old
        };
        db.Regions.AddRange(assignedRegion, unassignedRegion);
        await db.SaveChangesAsync();

        await GrantRegionalManagerCoverageAsync(db, manager, assignedRegion.Id);

        var assignedDestination = new Destination
        {
            Name = "Assigned Destination",
            DestinationTypeId = destinationType.Id,
            RegionId = assignedRegion.Id,
            Description = "Assigned analytics destination.",
            Latitude = 42.1,
            Longitude = 18.1,
            CreatedAt = old
        };
        var outDestination = new Destination
        {
            Name = "Out Region Destination",
            DestinationTypeId = destinationType.Id,
            RegionId = unassignedRegion.Id,
            Description = "Unassigned analytics destination.",
            Latitude = 43.1,
            Longitude = 19.1,
            CreatedAt = old
        };
        db.Destinations.AddRange(assignedDestination, outDestination);
        await db.SaveChangesAsync();

        var assignedLocation = new Location
        {
            Name = "Assigned Location",
            DestinationId = assignedDestination.Id,
            LocationTypeId = locationType.Id,
            Description = "Assigned analytics location.",
            Latitude = 42.2,
            Longitude = 18.2,
            CreatedAt = old
        };
        var outLocation = new Location
        {
            Name = "Out Region Location",
            DestinationId = outDestination.Id,
            LocationTypeId = locationType.Id,
            Description = "Out analytics location.",
            Latitude = 43.2,
            Longitude = 19.2,
            CreatedAt = old
        };
        db.Locations.AddRange(assignedLocation, outLocation);
        await db.SaveChangesAsync();

        var assignedPlace = new Place
        {
            Name = "Assigned Place",
            PlaceTypeId = placeType.Id,
            LocationId = assignedLocation.Id,
            Description = "Assigned analytics place.",
            Latitude = 42.3,
            Longitude = 18.3,
            CreatedAt = old
        };
        var outPlace = new Place
        {
            Name = "Out Region Place",
            PlaceTypeId = placeType.Id,
            LocationId = outLocation.Id,
            Description = "Out analytics place.",
            Latitude = 43.3,
            Longitude = 19.3,
            CreatedAt = old
        };
        db.Places.AddRange(assignedPlace, outPlace);
        await db.SaveChangesAsync();

        var assignedEvent = new Event
        {
            Name = "Assigned Event",
            EventTypeId = eventType.Id,
            DestinationId = assignedDestination.Id,
            LocationId = assignedLocation.Id,
            Description = "Assigned analytics event.",
            StartDate = recent.AddDays(1),
            EndDate = recent.AddDays(2),
            CreatedAt = old
        };
        db.Events.Add(assignedEvent);
        await db.SaveChangesAsync();

        var anonymousOne = new AnonymousSession
        {
            Id = Guid.NewGuid(),
            CreatedAt = recent,
            LastSeenAt = recent,
            EntryRoute = "/places/" + assignedPlace.Id,
            UserAgentFamily = "test"
        };
        var anonymousTwo = new AnonymousSession
        {
            Id = Guid.NewGuid(),
            CreatedAt = recent,
            LastSeenAt = recent,
            EntryRoute = "/locations/" + assignedLocation.Id,
            UserAgentFamily = "test"
        };
        db.AnonymousSessions.AddRange(anonymousOne, anonymousTwo);

        db.AnalyticsEvents.AddRange(
            new AnalyticsEvent
            {
                EventType = AnalyticsEventTypes.DetailView,
                EntityType = AnalyticsEntityTypes.Place,
                EntityId = assignedPlace.Id,
                UserId = user.Id,
                AnonymousSessionId = anonymousOne.Id,
                OccurredAt = recent
            },
            new AnalyticsEvent
            {
                EventType = AnalyticsEventTypes.PageView,
                EntityType = AnalyticsEntityTypes.Place,
                EntityId = assignedPlace.Id,
                AnonymousSessionId = anonymousOne.Id,
                OccurredAt = recent.AddHours(1)
            },
            new AnalyticsEvent
            {
                EventType = AnalyticsEventTypes.DetailView,
                EntityType = AnalyticsEntityTypes.Location,
                EntityId = assignedLocation.Id,
                AnonymousSessionId = anonymousTwo.Id,
                OccurredAt = recent.AddHours(2)
            },
            new AnalyticsEvent
            {
                EventType = AnalyticsEventTypes.DetailView,
                EntityType = AnalyticsEntityTypes.Event,
                EntityId = assignedEvent.Id,
                UserId = user.Id,
                OccurredAt = recent.AddHours(3)
            },
            new AnalyticsEvent
            {
                EventType = AnalyticsEventTypes.DetailView,
                EntityType = AnalyticsEntityTypes.Destination,
                EntityId = assignedDestination.Id,
                UserId = user.Id,
                OccurredAt = recent.AddHours(4)
            },
            new AnalyticsEvent
            {
                EventType = AnalyticsEventTypes.OutboundLinkClicked,
                EntityType = AnalyticsEntityTypes.Place,
                EntityId = assignedPlace.Id,
                UserId = user.Id,
                OccurredAt = recent.AddHours(5)
            },
            new AnalyticsEvent
            {
                EventType = AnalyticsEventTypes.DirectionsStarted,
                EntityType = AnalyticsEntityTypes.Event,
                EntityId = assignedEvent.Id,
                AnonymousSessionId = anonymousOne.Id,
                OccurredAt = recent.AddHours(6)
            },
            new AnalyticsEvent
            {
                EventType = AnalyticsEventTypes.DetailView,
                EntityType = AnalyticsEntityTypes.Place,
                EntityId = assignedPlace.Id,
                UserId = user.Id,
                OccurredAt = old
            },
            new AnalyticsEvent
            {
                EventType = AnalyticsEventTypes.DetailView,
                EntityType = AnalyticsEntityTypes.Place,
                EntityId = outPlace.Id,
                UserId = user.Id,
                OccurredAt = recent
            },
            new AnalyticsEvent
            {
                EventType = AnalyticsEventTypes.PageDuration,
                EntityType = AnalyticsEntityTypes.Place,
                EntityId = assignedPlace.Id,
                UserId = user.Id,
                DurationMs = 90_000,
                Route = "/places/:id",
                OccurredAt = recent.AddMinutes(10)
            },
            new AnalyticsEvent
            {
                EventType = AnalyticsEventTypes.PageDuration,
                EntityType = AnalyticsEntityTypes.Place,
                EntityId = assignedPlace.Id,
                AnonymousSessionId = anonymousOne.Id,
                DurationMs = 30_000,
                Route = "/places/:id",
                OccurredAt = recent.AddMinutes(20)
            },
            new AnalyticsEvent
            {
                EventType = AnalyticsEventTypes.PageDuration,
                EntityType = AnalyticsEntityTypes.Event,
                EntityId = assignedEvent.Id,
                UserId = user.Id,
                DurationMs = 180_000,
                Route = "/events/:id",
                OccurredAt = recent.AddMinutes(30)
            },
            new AnalyticsEvent
            {
                EventType = AnalyticsEventTypes.PageDuration,
                EntityType = AnalyticsEntityTypes.Place,
                EntityId = assignedPlace.Id,
                UserId = user.Id,
                DurationMs = 1_800_000,
                Route = "/places/:id",
                OccurredAt = old
            },
            new AnalyticsEvent
            {
                EventType = AnalyticsEventTypes.PageDuration,
                EntityType = AnalyticsEntityTypes.Place,
                EntityId = outPlace.Id,
                UserId = user.Id,
                DurationMs = 600_000,
                Route = "/places/:id",
                OccurredAt = recent
            });

        db.Favorites.AddRange(
            new Favorite
            {
                EntityType = AnalyticsEntityTypes.Place,
                EntityId = assignedPlace.Id,
                UserId = user.Id,
                CreatedAt = recent
            },
            new Favorite
            {
                EntityType = AnalyticsEntityTypes.Event,
                EntityId = assignedEvent.Id,
                UserId = user.Id,
                CreatedAt = recent
            },
            new Favorite
            {
                EntityType = AnalyticsEntityTypes.Place,
                EntityId = outPlace.Id,
                UserId = user.Id,
                CreatedAt = recent
            });

        db.PlaceReviews.AddRange(
            new PlaceReview
            {
                PlaceId = assignedPlace.Id,
                UserId = user.Id,
                Rating = 5,
                Comment = "Great.",
                CreatedAt = recent
            },
            new PlaceReview
            {
                PlaceId = assignedPlace.Id,
                UserId = manager.Id,
                Rating = 4,
                Comment = "Good.",
                CreatedAt = recent
            },
            new PlaceReview
            {
                PlaceId = outPlace.Id,
                UserId = user.Id,
                Rating = 1,
                Comment = "Out.",
                CreatedAt = recent
            });

        db.PlaceContributions.AddRange(
            new PlaceContribution
            {
                Type = PlaceContributionType.CreatePlace,
                Status = PlaceContributionStatus.PendingReview,
                UserId = user.Id,
                PlaceTypeId = placeType.Id,
                LocationId = assignedLocation.Id,
                Name = "Analytics Pending One",
                Description = "Pending assigned analytics contribution.",
                Latitude = 42.4,
                Longitude = 18.4,
                SubmittedAt = recent,
                CreatedAt = recent
            },
            new PlaceContribution
            {
                Type = PlaceContributionType.CreatePlace,
                Status = PlaceContributionStatus.PendingReview,
                UserId = user.Id,
                PlaceTypeId = placeType.Id,
                LocationId = assignedLocation.Id,
                Name = "Analytics Pending Two",
                Description = "Pending assigned analytics contribution.",
                Latitude = 42.5,
                Longitude = 18.5,
                SubmittedAt = recent.AddMinutes(-1),
                CreatedAt = recent.AddMinutes(-1)
            },
            new PlaceContribution
            {
                Type = PlaceContributionType.CreatePlace,
                Status = PlaceContributionStatus.PendingReview,
                UserId = user.Id,
                PlaceTypeId = placeType.Id,
                LocationId = outLocation.Id,
                Name = "Out Region Pending",
                Description = "Pending unassigned analytics contribution.",
                Latitude = 43.4,
                Longitude = 19.4,
                SubmittedAt = recent,
                CreatedAt = recent
            });

        await db.SaveChangesAsync();

        return new RegionalAnalyticsFixture(
            assignedRegion.Id,
            unassignedRegion.Id,
            assignedLocation.Id,
            assignedPlace.Id,
            assignedEvent.Id);
    }

    private static async Task<int> CreateEmptyAssignedRegionAsync(GeotrackApiFactory app)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var territory = await db.Territories.OrderBy(item => item.Id).FirstAsync();
        var manager = await db.Users.SingleAsync(user => user.Email == "elena@example.com");
        var region = new Region
        {
            Name = $"Analytics Empty {Guid.NewGuid():N}",
            TerritoryId = territory.Id,
            CreatedAt = DateTime.UtcNow
        };

        db.Regions.Add(region);
        await db.SaveChangesAsync();

        await GrantRegionalManagerCoverageAsync(db, manager, region.Id);

        return region.Id;
    }

    private static async Task<int> CreateInventoryOnlyAssignedRegionAsync(GeotrackApiFactory app)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var now = DateTime.UtcNow.AddDays(-5);
        var territory = await db.Territories.OrderBy(item => item.Id).FirstAsync();
        var destinationType = await db.DestinationTypes.OrderBy(item => item.Id).FirstAsync();
        var locationType = await db.LocationTypes.OrderBy(item => item.Id).FirstAsync();
        var placeType = await db.PlaceTypes.OrderBy(item => item.Id).FirstAsync();
        var eventType = await db.EventTypes.OrderBy(item => item.Id).FirstAsync();
        var manager = await db.Users.SingleAsync(user => user.Email == "elena@example.com");

        var region = new Region
        {
            Name = $"Analytics Inventory Only {Guid.NewGuid():N}",
            TerritoryId = territory.Id,
            CreatedAt = now
        };

        db.Regions.Add(region);
        await db.SaveChangesAsync();

        await GrantRegionalManagerCoverageAsync(db, manager, region.Id);

        var destination = new Destination
        {
            Name = "Inventory Only Destination",
            DestinationTypeId = destinationType.Id,
            RegionId = region.Id,
            Description = "Inventory-only analytics destination.",
            Latitude = 41.1,
            Longitude = 17.1,
            CreatedAt = now
        };

        db.Destinations.Add(destination);
        await db.SaveChangesAsync();

        var location = new Location
        {
            Name = "Inventory Only Location",
            DestinationId = destination.Id,
            LocationTypeId = locationType.Id,
            Description = "Inventory-only analytics location.",
            Latitude = 41.2,
            Longitude = 17.2,
            CreatedAt = now
        };

        db.Locations.Add(location);
        await db.SaveChangesAsync();

        var place = new Place
        {
            Name = "Inventory Only Place",
            PlaceTypeId = placeType.Id,
            LocationId = location.Id,
            Description = "Inventory-only analytics place.",
            Latitude = 41.3,
            Longitude = 17.3,
            CreatedAt = now
        };

        var eventEntity = new Event
        {
            Name = "Inventory Only Event",
            EventTypeId = eventType.Id,
            DestinationId = destination.Id,
            LocationId = location.Id,
            Description = "Inventory-only analytics event.",
            StartDate = now.AddDays(3),
            EndDate = now.AddDays(4),
            CreatedAt = now
        };

        db.Places.Add(place);
        db.Events.Add(eventEntity);
        await db.SaveChangesAsync();

        return region.Id;
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

    private static async Task AddMonthlyVisitComparisonEventsAsync(GeotrackApiFactory app, int placeId)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = await db.Users.SingleAsync(item => item.Email == "marko.p@example.com");
        var now = DateTime.UtcNow;
        var currentMonth = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc).AddHours(1);

        db.AnalyticsEvents.AddRange(
            new AnalyticsEvent
            {
                EventType = AnalyticsEventTypes.DetailView,
                EntityType = AnalyticsEntityTypes.Place,
                EntityId = placeId,
                UserId = user.Id,
                OccurredAt = currentMonth
            },
            new AnalyticsEvent
            {
                EventType = AnalyticsEventTypes.DetailView,
                EntityType = AnalyticsEntityTypes.Place,
                EntityId = placeId,
                UserId = user.Id,
                OccurredAt = currentMonth.AddYears(-1)
            },
            new AnalyticsEvent
            {
                EventType = AnalyticsEventTypes.PageView,
                EntityType = AnalyticsEntityTypes.Place,
                EntityId = placeId,
                UserId = user.Id,
                OccurredAt = currentMonth.AddYears(-2)
            });

        await db.SaveChangesAsync();
    }

    private sealed record RegionalAnalyticsFixture(
        int AssignedRegionId,
        int UnassignedRegionId,
        int LocationId,
        int PlaceId,
        int EventId);
}
