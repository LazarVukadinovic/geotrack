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
using Geotrack.Domain.Entities.Tags;
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
        var rankedDestination = Assert.Single(overview.DestinationRanking);
        Assert.Equal(fixture.DestinationId, rankedDestination.DestinationId);
        Assert.Equal(fixture.AssignedRegionId, rankedDestination.RegionId);
        Assert.Equal(1, rankedDestination.Visits);
        Assert.Equal(1, rankedDestination.Rank);
        Assert.Equal(100, rankedDestination.PercentageOfTopValue);
        Assert.Contains(overview.PageViewsTrend, point => point.Views > 0);
        Assert.Contains(overview.EntityTypeDistribution, item => item.EntityType == AnalyticsEntityTypes.Place && item.Count == 1);
        Assert.Equal(fixture.LocationId, overview.ActionableInsights.TopViewedLocation?.EntityId);

        var unassignedResponse = await client.GetAsync(
            $"/api/admin/regional/analytics/overview?regionId={fixture.UnassignedRegionId}&period=30d");
        Assert.Equal(HttpStatusCode.Forbidden, unassignedResponse.StatusCode);
    }

    [Fact]
    public async Task Destination_and_location_filters_roll_up_child_analytics()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        var fixture = await CreateAnalyticsFixtureAsync(app);

        using var client = await LoginAsync(app, "elena@example.com", "password123");

        var destinationOverview = await client.GetFromJsonAsync<RegionalAnalyticsOverviewDto>(
            $"/api/admin/regional/analytics/overview?regionId={fixture.AssignedRegionId}&period=30d&entityType=destination&sort=views_desc");
        var locationOverview = await client.GetFromJsonAsync<RegionalAnalyticsOverviewDto>(
            $"/api/admin/regional/analytics/overview?regionId={fixture.AssignedRegionId}&period=30d&entityType=location&sort=views_desc");

        Assert.NotNull(destinationOverview);
        Assert.Equal(5, destinationOverview!.Totals.PageViews);
        Assert.Equal(2, destinationOverview.Totals.UniqueVisitors);
        Assert.Equal(2, destinationOverview.Totals.Saves);
        Assert.Equal(2, destinationOverview.Totals.ExternalClicks);
        Assert.Equal(2, destinationOverview.Totals.TotalReviews);
        Assert.Equal(4.5, destinationOverview.Totals.AverageRating);

        var destinationRow = Assert.Single(destinationOverview.EntityRows);
        Assert.Equal(AnalyticsEntityTypes.Destination, destinationRow.EntityType);
        Assert.Equal(fixture.DestinationId, destinationRow.EntityId);
        Assert.Equal(5, destinationRow.PageViews);
        Assert.Equal(2, destinationRow.UniqueVisitors);
        Assert.Equal(2, destinationRow.Saves);
        Assert.Equal(2, destinationRow.ExternalClicks);
        Assert.Equal(2, destinationRow.ReviewCount);
        Assert.Equal(4.5, destinationRow.AverageRating);
        var rankedDestination = Assert.Single(destinationOverview.DestinationRanking);
        Assert.Equal(fixture.DestinationId, rankedDestination.DestinationId);
        Assert.Equal(5, rankedDestination.Visits);
        Assert.DoesNotContain(destinationOverview.EntityRows, row => row.Name.StartsWith("Out Region", StringComparison.Ordinal));

        Assert.NotNull(locationOverview);
        Assert.Equal(4, locationOverview!.Totals.PageViews);
        Assert.Equal(2, locationOverview.Totals.UniqueVisitors);
        Assert.Equal(2, locationOverview.Totals.Saves);
        Assert.Equal(2, locationOverview.Totals.ExternalClicks);
        Assert.Equal(2, locationOverview.Totals.TotalReviews);
        Assert.Equal(4.5, locationOverview.Totals.AverageRating);

        var locationRow = Assert.Single(locationOverview.EntityRows);
        Assert.Equal(AnalyticsEntityTypes.Location, locationRow.EntityType);
        Assert.Equal(fixture.LocationId, locationRow.EntityId);
        Assert.Equal(4, locationRow.PageViews);
        Assert.Equal(2, locationRow.UniqueVisitors);
        Assert.Equal(2, locationRow.Saves);
        Assert.Equal(2, locationRow.ExternalClicks);
        Assert.Equal(2, locationRow.ReviewCount);
        Assert.Equal(4.5, locationRow.AverageRating);
        Assert.Equal(fixture.LocationId, locationOverview.TopPerformingEntities[0].EntityId);
        Assert.DoesNotContain(locationOverview.EntityRows, row => row.Name.StartsWith("Out Region", StringComparison.Ordinal));
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
    public async Task Regional_manager_can_load_entity_details_for_assigned_region_entities()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        var fixture = await CreateEntityDetailFixtureAsync(app);

        using var client = await LoginAsync(app, "elena@example.com", "password123");

        var destination = await client.GetFromJsonAsync<RegionalAnalyticsEntityDetailDto>(
            $"/api/admin/regional/analytics/entities/destination/{fixture.DestinationId}/details?regionId={fixture.AssignedRegionId}&period=30d");
        var location = await client.GetFromJsonAsync<RegionalAnalyticsEntityDetailDto>(
            $"/api/admin/regional/analytics/entities/location/{fixture.LocationId}/details?regionId={fixture.AssignedRegionId}&period=30d");
        var place = await client.GetFromJsonAsync<RegionalAnalyticsEntityDetailDto>(
            $"/api/admin/regional/analytics/entities/place/{fixture.PlaceId}/details?regionId={fixture.AssignedRegionId}&period=30d");
        var eventDetail = await client.GetFromJsonAsync<RegionalAnalyticsEntityDetailDto>(
            $"/api/admin/regional/analytics/entities/event/{fixture.EventId}/details?regionId={fixture.AssignedRegionId}&period=30d");

        Assert.NotNull(destination);
        Assert.Equal(AnalyticsEntityTypes.Destination, destination!.EntityType);
        Assert.Equal(fixture.AssignedRegionId, destination.RegionId);
        Assert.Equal(7, destination.Analytics.Views);
        Assert.Equal(4, destination.Analytics.Saves);
        Assert.Equal(4, destination.Analytics.Reviews);
        Assert.Equal(4.5, destination.Analytics.AverageRating);
        Assert.Equal(1, destination.TypeSpecific.LocationsCount);
        Assert.True(destination.GalleryImages.Count > 0);

        Assert.NotNull(location);
        Assert.Equal(AnalyticsEntityTypes.Location, location!.EntityType);
        Assert.Equal(fixture.DestinationId, location.DestinationId);
        Assert.Equal("Detail Destination", location.TypeSpecific.ParentDestination);
        Assert.Equal(6, location.Analytics.Views);
        Assert.Equal(3, location.Analytics.Saves);
        Assert.Equal(3, location.Analytics.Reviews);
        Assert.Equal(Math.Round(13d / 3d, 2, MidpointRounding.AwayFromZero), location.Analytics.AverageRating);
        Assert.True(location.TypeSpecific.PlacesCount >= 2);

        Assert.NotNull(place);
        Assert.Equal(AnalyticsEntityTypes.Place, place!.EntityType);
        Assert.Equal(fixture.LocationId, place.LocationId);
        Assert.Equal("Cafe", place.TypeSpecific.PlaceType);
        Assert.Equal("https://example.com/detail-place", place.TypeSpecific.Website);
        Assert.Equal(3, place.Analytics.Views);
        Assert.Equal(1, place.Analytics.Saves);
        Assert.Equal(1, place.Analytics.ExternalClicks);
        Assert.Equal(1, place.Analytics.Reviews);
        Assert.Empty(place.TypeSpecific.ExternalPartnerClicks);

        Assert.NotNull(eventDetail);
        Assert.Equal(AnalyticsEntityTypes.Event, eventDetail!.EntityType);
        Assert.Equal(fixture.DestinationId, eventDetail.DestinationId);
        Assert.Equal("Detail Venue", eventDetail.TypeSpecific.VenueName);
        Assert.Equal(1, eventDetail.Analytics.Views);
        Assert.Equal(1, eventDetail.Analytics.Saves);
        Assert.Equal(1, eventDetail.Analytics.ExternalClicks);
        Assert.Equal(1, eventDetail.Analytics.Reviews);
    }

    [Fact]
    public async Task Stay_place_entity_detail_returns_accommodation_fields_safely()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        var fixture = await CreateEntityDetailFixtureAsync(app);

        using var client = await LoginAsync(app, "elena@example.com", "password123");
        var stay = await client.GetFromJsonAsync<RegionalAnalyticsEntityDetailDto>(
            $"/api/admin/regional/analytics/entities/place/{fixture.StayPlaceId}/details?regionId={fixture.AssignedRegionId}&period=30d");

        Assert.NotNull(stay);
        Assert.Equal(AnalyticsEntityTypes.Place, stay!.EntityType);
        Assert.Equal("Stay", stay.TypeSpecific.PlaceType);
        Assert.Equal("Hotel", stay.TypeSpecific.StayType);
        Assert.Contains("Free WiFi", stay.TypeSpecific.Amenities);
        Assert.Equal(3, stay.TypeSpecific.BookingPartners.Count);
        Assert.Equal(0, stay.Analytics.Views);
        Assert.Equal(8, stay.Analytics.ExternalClicks);

        var partnerClicks = stay.TypeSpecific.ExternalPartnerClicks;
        Assert.Equal(3, partnerClicks.Count);

        var booking = Assert.Single(partnerClicks, item => item.PartnerKey == "booking");
        Assert.Equal("Booking.com", booking.PartnerLabel);
        Assert.Equal("https://www.booking.com/hotel/hr/detail-stay.html", booking.Url);
        Assert.Equal(2, booking.ClickCount);
        Assert.Equal(33.3, booking.SharePercent);

        var airbnb = Assert.Single(partnerClicks, item => item.PartnerKey == "airbnb");
        Assert.Equal("Airbnb", airbnb.PartnerLabel);
        Assert.Equal(1, airbnb.ClickCount);
        Assert.Equal(16.7, airbnb.SharePercent);

        var external = Assert.Single(partnerClicks, item => item.PartnerKey == "external");
        Assert.Equal("Expedia", external.PartnerLabel);
        Assert.Equal("https://www.expedia.com/detail-stay", external.Url);
        Assert.Equal(3, external.ClickCount);
        Assert.Equal(50, external.SharePercent);
    }

    [Fact]
    public async Task Stay_partner_click_distribution_returns_configured_zero_rows_when_no_click_data()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        var fixture = await CreateEntityDetailFixtureAsync(app);

        using var client = await LoginAsync(app, "elena@example.com", "password123");
        var stay = await client.GetFromJsonAsync<RegionalAnalyticsEntityDetailDto>(
            $"/api/admin/regional/analytics/entities/place/{fixture.ZeroClickStayPlaceId}/details?regionId={fixture.AssignedRegionId}&period=30d");

        Assert.NotNull(stay);
        Assert.Equal("Stay", stay!.TypeSpecific.PlaceType);
        Assert.Equal("Hotel", stay.TypeSpecific.StayType);
        Assert.Equal(0, stay.Analytics.ExternalClicks);

        var partnerClicks = stay.TypeSpecific.ExternalPartnerClicks;
        Assert.Equal(2, partnerClicks.Count);
        Assert.Contains(partnerClicks, item => item.PartnerKey == "booking" && item.PartnerLabel == "Booking.com");
        Assert.Contains(partnerClicks, item => item.PartnerKey == "airbnb" && item.PartnerLabel == "Airbnb");
        Assert.DoesNotContain(partnerClicks, item => item.PartnerLabel == "Expedia");
        Assert.All(partnerClicks, item =>
        {
            Assert.Equal(0, item.ClickCount);
            Assert.Equal(0, item.SharePercent);
        });
    }

    [Fact]
    public async Task Entity_detail_rejects_invalid_unassigned_and_missing_entities()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        var fixture = await CreateEntityDetailFixtureAsync(app);

        using var client = await LoginAsync(app, "elena@example.com", "password123");

        var unassigned = await client.GetAsync(
            $"/api/admin/regional/analytics/entities/place/{fixture.UnassignedPlaceId}/details?regionId={fixture.AssignedRegionId}&period=30d");
        var invalidType = await client.GetAsync(
            $"/api/admin/regional/analytics/entities/planet/{fixture.PlaceId}/details?regionId={fixture.AssignedRegionId}&period=30d");
        var missing = await client.GetAsync(
            $"/api/admin/regional/analytics/entities/place/999999/details?regionId={fixture.AssignedRegionId}&period=30d");
        var unassignedRegion = await client.GetAsync(
            $"/api/admin/regional/analytics/entities/place/{fixture.PlaceId}/details?regionId={fixture.UnassignedRegionId}&period=30d");

        Assert.Equal(HttpStatusCode.Forbidden, unassigned.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, invalidType.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, unassignedRegion.StatusCode);
    }

    [Fact]
    public async Task Entity_detail_with_no_analytics_and_missing_optional_fields_returns_zero_state()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        var fixture = await CreateEntityDetailFixtureAsync(app);

        using var client = await LoginAsync(app, "elena@example.com", "password123");
        var detail = await client.GetFromJsonAsync<RegionalAnalyticsEntityDetailDto>(
            $"/api/admin/regional/analytics/entities/place/{fixture.NoAnalyticsPlaceId}/details?regionId={fixture.AssignedRegionId}&period=30d");

        Assert.NotNull(detail);
        Assert.Equal(fixture.NoAnalyticsPlaceId, detail!.EntityId);
        Assert.Equal(0, detail.Analytics.Views);
        Assert.Equal(0, detail.Analytics.UniqueVisitors);
        Assert.Equal(0, detail.Analytics.Saves);
        Assert.Equal(0, detail.Analytics.ExternalClicks);
        Assert.Equal(0, detail.Analytics.Reviews);
        Assert.Empty(detail.GalleryImages);
        Assert.Empty(detail.Tags);
        Assert.Null(detail.Address);
        Assert.Null(detail.TypeSpecific.Website);
        Assert.Empty(detail.TypeSpecific.OperatingHours);
        Assert.Empty(detail.TypeSpecific.Amenities);
        Assert.Empty(detail.TypeSpecific.ExternalPartnerClicks);
    }

    [Fact]
    public async Task Destination_ranking_returns_top_five_destination_views_for_assigned_region()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        var fixture = await CreateDestinationRankingFixtureAsync(app);

        using var client = await LoginAsync(app, "elena@example.com", "password123");
        var overview = await client.GetFromJsonAsync<RegionalAnalyticsOverviewDto>(
            $"/api/admin/regional/analytics/overview?regionId={fixture.AssignedRegionId}&period=30d&entityType=all&sort=views_desc");

        Assert.NotNull(overview);
        Assert.Equal(5, overview!.DestinationRanking.Count);
        Assert.Equal("Ranking Destination 1", overview.DestinationRanking[0].DestinationName);
        Assert.Equal(7, overview.DestinationRanking[0].Visits);
        Assert.Equal(1, overview.DestinationRanking[0].Rank);
        Assert.Equal(100, overview.DestinationRanking[0].PercentageOfTopValue);
        Assert.Equal("Ranking Destination 2", overview.DestinationRanking[1].DestinationName);
        Assert.Equal(6, overview.DestinationRanking[1].Visits);
        Assert.Equal(Math.Round(6d / 7d * 100, 1, MidpointRounding.AwayFromZero), overview.DestinationRanking[1].PercentageOfTopValue);
        Assert.Equal("Ranking Destination 5", overview.DestinationRanking[4].DestinationName);
        Assert.All(overview.DestinationRanking, item => Assert.Equal(fixture.AssignedRegionId, item.RegionId));
        Assert.DoesNotContain(overview.DestinationRanking, item => item.DestinationId == fixture.UnassignedDestinationId);
        Assert.DoesNotContain(overview.DestinationRanking, item => item.DestinationId == fixture.OldOnlyDestinationId);

        var unassignedResponse = await client.GetAsync(
            $"/api/admin/regional/analytics/overview?regionId={fixture.UnassignedRegionId}&period=30d");
        Assert.Equal(HttpStatusCode.Forbidden, unassignedResponse.StatusCode);
    }

    [Fact]
    public async Task Destination_ranking_honors_period_filter()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        var fixture = await CreateDestinationRankingFixtureAsync(app);

        using var client = await LoginAsync(app, "elena@example.com", "password123");
        var overview = await client.GetFromJsonAsync<RegionalAnalyticsOverviewDto>(
            $"/api/admin/regional/analytics/overview?regionId={fixture.AssignedRegionId}&period=all&entityType=all&sort=views_desc");

        Assert.NotNull(overview);
        Assert.Equal(fixture.OldOnlyDestinationId, overview!.DestinationRanking[0].DestinationId);
        Assert.Equal("Ranking Archive", overview.DestinationRanking[0].DestinationName);
        Assert.Equal(9, overview.DestinationRanking[0].Visits);
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
        Assert.Empty(overview.DestinationRanking);
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
        Assert.Empty(overview.DestinationRanking);
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
    public async Task Tag_views_group_real_entity_views_by_region_tags()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        var fixture = await CreateTagViewsFixtureAsync(app);

        using var client = await LoginAsync(app, "elena@example.com", "password123");
        var response = await client.GetFromJsonAsync<RegionalTagViewsAnalyticsResponseDto>(
            $"/api/admin/regional/analytics/tag-views?regionId={fixture.AssignedRegionId}&period=1m&take=10&entityType=all");

        Assert.NotNull(response);
        Assert.Equal("1m", response!.Period);
        Assert.Equal(fixture.AssignedRegionId, response.RegionId);
        Assert.Equal(13, response.TotalViews);
        Assert.Contains(response.AvailableTags, tag => tag.TagId == fixture.ZeroViewTagId);
        Assert.DoesNotContain(response.AvailableTags, tag => tag.TagId == fixture.UnassignedOnlyTagId);
        Assert.DoesNotContain(response.Items, item => item.TagId == fixture.UnassignedOnlyTagId);

        Assert.Equal(4, response.Items.Single(item => item.TagId == fixture.EventTagId).Views);
        Assert.Equal(3, response.Items.Single(item => item.TagId == fixture.MultiTagId).Views);
        Assert.Equal(3, response.Items.Single(item => item.TagId == fixture.PlaceTagId).Views);
        Assert.Equal(2, response.Items.Single(item => item.TagId == fixture.DestinationTagId).Views);
        Assert.Equal(1, response.Items.Single(item => item.TagId == fixture.LocationTagId).Views);
        Assert.Contains(AnalyticsEntityTypes.Place, response.Items.Single(item => item.TagId == fixture.MultiTagId).EntityScopes);

        var placeOnly = await client.GetFromJsonAsync<RegionalTagViewsAnalyticsResponseDto>(
            $"/api/admin/regional/analytics/tag-views?regionId={fixture.AssignedRegionId}&period=1m&entityType=place");
        Assert.NotNull(placeOnly);
        Assert.Equal(6, placeOnly!.TotalViews);
        Assert.Contains(placeOnly.Items, item => item.TagId == fixture.MultiTagId && item.Views == 3);
        Assert.Contains(placeOnly.Items, item => item.TagId == fixture.PlaceTagId && item.Views == 3);
        Assert.DoesNotContain(placeOnly.Items, item => item.TagId == fixture.EventTagId);
    }

    [Fact]
    public async Task Tag_views_are_available_for_supported_periods_and_reject_invalid_period()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        var fixture = await CreateTagViewsFixtureAsync(app);

        using var client = await LoginAsync(app, "elena@example.com", "password123");
        foreach (var period in new[] { "24h", "7d", "1m", "3m", "6m", "1y" })
        {
            var response = await client.GetAsync(
                $"/api/admin/regional/analytics/tag-views?regionId={fixture.AssignedRegionId}&period={period}");

            response.EnsureSuccessStatusCode();
            var body = await response.Content.ReadFromJsonAsync<RegionalTagViewsAnalyticsResponseDto>();
            Assert.NotNull(body);
            Assert.Equal(period, body!.Period);
        }

        var invalidPeriod = await client.GetAsync(
            $"/api/admin/regional/analytics/tag-views?regionId={fixture.AssignedRegionId}&period=forever");
        Assert.Equal(HttpStatusCode.BadRequest, invalidPeriod.StatusCode);
    }

    [Fact]
    public async Task Tag_views_selected_tag_filter_preserves_zero_view_tags()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        var fixture = await CreateTagViewsFixtureAsync(app);

        using var client = await LoginAsync(app, "elena@example.com", "password123");
        var response = await client.GetFromJsonAsync<RegionalTagViewsAnalyticsResponseDto>(
            $"/api/admin/regional/analytics/tag-views?regionId={fixture.AssignedRegionId}&period=1m&tagIds={fixture.ZeroViewTagId}&tagIds={fixture.DestinationTagId}");

        Assert.NotNull(response);
        Assert.Collection(
            response!.Items,
            item =>
            {
                Assert.Equal(fixture.ZeroViewTagId, item.TagId);
                Assert.Equal(0, item.Views);
                Assert.Equal(0, item.PercentageOfTotal);
            },
            item =>
            {
                Assert.Equal(fixture.DestinationTagId, item.TagId);
                Assert.Equal(2, item.Views);
                Assert.Equal(Math.Round(2d / 2d * 100, 1, MidpointRounding.AwayFromZero), item.PercentageOfTotal);
            });
    }

    [Fact]
    public async Task Tag_views_empty_data_and_invalid_tag_ids_return_safe_responses()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        var emptyRegionId = await CreateEmptyAssignedRegionAsync(app);
        var fixture = await CreateTagViewsFixtureAsync(app);

        using var client = await LoginAsync(app, "elena@example.com", "password123");
        var empty = await client.GetFromJsonAsync<RegionalTagViewsAnalyticsResponseDto>(
            $"/api/admin/regional/analytics/tag-views?regionId={emptyRegionId}&period=1m");

        Assert.NotNull(empty);
        Assert.Equal(0, empty!.TotalViews);
        Assert.Empty(empty.AvailableTags);
        Assert.Empty(empty.Items);

        var invalidFormat = await client.GetAsync(
            $"/api/admin/regional/analytics/tag-views?regionId={fixture.AssignedRegionId}&period=1m&tagIds=abc");
        Assert.Equal(HttpStatusCode.BadRequest, invalidFormat.StatusCode);

        var unavailableTag = await client.GetAsync(
            $"/api/admin/regional/analytics/tag-views?regionId={fixture.AssignedRegionId}&period=1m&tagIds={fixture.UnassignedOnlyTagId}");
        Assert.Equal(HttpStatusCode.BadRequest, unavailableTag.StatusCode);
    }

    [Fact]
    public async Task Tag_views_reject_unassigned_regions()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        var fixture = await CreateTagViewsFixtureAsync(app);

        using var client = await LoginAsync(app, "elena@example.com", "password123");
        var response = await client.GetAsync(
            $"/api/admin/regional/analytics/tag-views?regionId={fixture.UnassignedRegionId}&period=1m");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
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
            assignedDestination.Id,
            assignedLocation.Id,
            assignedPlace.Id,
            assignedEvent.Id);
    }

    private static async Task<EntityDetailFixture> CreateEntityDetailFixtureAsync(GeotrackApiFactory app)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var now = DateTime.UtcNow;
        var old = now.AddDays(-20);
        var recent = now.AddDays(-2);
        var territory = await db.Territories.OrderBy(item => item.Id).FirstAsync();
        var destinationType = await db.DestinationTypes.OrderBy(item => item.Id).FirstAsync();
        var locationType = await db.LocationTypes.OrderBy(item => item.Id).FirstAsync();
        var eventType = await db.EventTypes.OrderBy(item => item.Id).FirstAsync();
        var manager = await db.Users.SingleAsync(user => user.Email == "elena@example.com");
        var user = await db.Users.SingleAsync(user => user.Email == "marko.p@example.com");

        var cafePlaceType = await db.PlaceTypes.FirstOrDefaultAsync(item => item.Name == "Cafe")
            ?? await db.PlaceTypes.OrderBy(item => item.Id).FirstAsync();

        var stayPlaceType = await db.PlaceTypes.FirstOrDefaultAsync(item => item.Name == "Stay");
        if (stayPlaceType is null)
        {
            stayPlaceType = new PlaceType
            {
                Name = "Stay",
                Description = "Accommodation",
                CreatedAt = old
            };
            db.PlaceTypes.Add(stayPlaceType);
            await db.SaveChangesAsync();
        }

        var hotelStayType = await db.StayTypes.FirstOrDefaultAsync(item => item.Name == "Hotel");
        if (hotelStayType is null)
        {
            hotelStayType = new StayType
            {
                Name = "Hotel",
                Slug = "hotel-" + Guid.NewGuid().ToString("N"),
                Description = "Hotel accommodation",
                CreatedAt = old
            };
            db.StayTypes.Add(hotelStayType);
            await db.SaveChangesAsync();
        }

        var wifiAmenity = await db.StayAmenities.FirstOrDefaultAsync(item => item.Name == "Free WiFi");
        if (wifiAmenity is null)
        {
            wifiAmenity = new StayAmenity
            {
                Name = "Free WiFi",
                Slug = "free-wifi-" + Guid.NewGuid().ToString("N"),
                Description = "Wireless internet access",
                IconKey = "wifi",
                CreatedAt = old
            };
            db.StayAmenities.Add(wifiAmenity);
            await db.SaveChangesAsync();
        }

        async Task<BookingPartner> EnsureBookingPartnerAsync(string name, string variant)
        {
            var partner = await db.BookingPartners.FirstOrDefaultAsync(item => item.Name == name);
            if (partner is not null)
            {
                return partner;
            }

            partner = new BookingPartner
            {
                Name = name,
                Variant = variant
            };
            db.BookingPartners.Add(partner);
            await db.SaveChangesAsync();
            return partner;
        }

        var bookingPartner = await EnsureBookingPartnerAsync("Booking.com", "dark");
        var airbnbPartner = await EnsureBookingPartnerAsync("Airbnb", "brand");
        var expediaPartner = await EnsureBookingPartnerAsync("Expedia", "muted");

        var assignedRegion = new Region
        {
            Name = $"Entity Detail Assigned {Guid.NewGuid():N}",
            TerritoryId = territory.Id,
            CreatedAt = old
        };
        var unassignedRegion = new Region
        {
            Name = $"Entity Detail Unassigned {Guid.NewGuid():N}",
            TerritoryId = territory.Id,
            CreatedAt = old
        };
        db.Regions.AddRange(assignedRegion, unassignedRegion);
        await db.SaveChangesAsync();

        await GrantRegionalManagerCoverageAsync(db, manager, assignedRegion.Id);

        var destination = new Destination
        {
            Name = "Detail Destination",
            Summary = "Detail destination summary.",
            DestinationTypeId = destinationType.Id,
            RegionId = assignedRegion.Id,
            Description = "Detail destination description.",
            BoundaryNote = "Detail destination boundary.",
            BoundaryShapeType = "Polygon",
            Latitude = 42.01,
            Longitude = 18.01,
            CreatedAt = old,
            UpdatedAt = recent,
            PublishedAt = recent
        };
        var unassignedDestination = new Destination
        {
            Name = "Detail Unassigned Destination",
            DestinationTypeId = destinationType.Id,
            RegionId = unassignedRegion.Id,
            Description = "Unassigned detail destination.",
            Latitude = 43.01,
            Longitude = 19.01,
            CreatedAt = old
        };
        db.Destinations.AddRange(destination, unassignedDestination);
        await db.SaveChangesAsync();

        var location = new Location
        {
            Name = "Detail Location",
            Summary = "Detail location summary.",
            DestinationId = destination.Id,
            LocationTypeId = locationType.Id,
            Description = "Detail location description.",
            BoundaryNote = "Detail location boundary.",
            BoundaryShapeType = "Polygon",
            Latitude = 42.02,
            Longitude = 18.02,
            CreatedAt = old,
            UpdatedAt = recent,
            PublishedAt = recent
        };
        var unassignedLocation = new Location
        {
            Name = "Detail Unassigned Location",
            DestinationId = unassignedDestination.Id,
            LocationTypeId = locationType.Id,
            Description = "Unassigned detail location.",
            Latitude = 43.02,
            Longitude = 19.02,
            CreatedAt = old
        };
        db.Locations.AddRange(location, unassignedLocation);
        await db.SaveChangesAsync();

        var place = new Place
        {
            Name = "Detail Place",
            Subtitle = "Detail place subtitle.",
            PlaceTypeId = cafePlaceType.Id,
            LocationId = location.Id,
            Description = "Detail place description.",
            Address = "Harbor 1",
            Website = "https://example.com/detail-place",
            BookingExternalLink = "https://example.com/book-place",
            Number = "+385111111",
            ContactEmail = "place@example.com",
            AdmissionPriceRange = "$$",
            OpeningHours = "09:00 - 17:00",
            Latitude = 42.03,
            Longitude = 18.03,
            CreatedAt = old,
            UpdatedAt = recent,
            PublishedAt = recent
        };
        var stayPlace = new Place
        {
            Name = "Detail Stay",
            PlaceTypeId = stayPlaceType.Id,
            LocationId = location.Id,
            Description = "Detail stay description.",
            Address = "Harbor 2",
            Latitude = 42.04,
            Longitude = 18.04,
            CreatedAt = old
        };
        var zeroClickStayPlace = new Place
        {
            Name = "Detail Stay Without Clicks",
            PlaceTypeId = stayPlaceType.Id,
            LocationId = location.Id,
            Description = "Detail stay with configured partners but no clicks.",
            Address = "Harbor 3",
            Latitude = 42.045,
            Longitude = 18.045,
            CreatedAt = old
        };
        var noAnalyticsPlace = new Place
        {
            Name = "Detail Sparse Place",
            PlaceTypeId = cafePlaceType.Id,
            LocationId = location.Id,
            Description = string.Empty,
            Latitude = 42.05,
            Longitude = 18.05,
            CreatedAt = old
        };
        var unassignedPlace = new Place
        {
            Name = "Detail Unassigned Place",
            PlaceTypeId = cafePlaceType.Id,
            LocationId = unassignedLocation.Id,
            Description = "Unassigned detail place.",
            Latitude = 43.03,
            Longitude = 19.03,
            CreatedAt = old
        };
        db.Places.AddRange(place, stayPlace, zeroClickStayPlace, noAnalyticsPlace, unassignedPlace);
        await db.SaveChangesAsync();

        var stay = new Stay
        {
            PlaceId = stayPlace.Id,
            StayTypeId = hotelStayType.Id,
            CheckInTime = "14:00",
            CheckOutTime = "11:00"
        };
        var zeroClickStay = new Stay
        {
            PlaceId = zeroClickStayPlace.Id,
            StayTypeId = hotelStayType.Id,
            CheckInTime = "15:00",
            CheckOutTime = "10:00"
        };
        db.Stays.AddRange(stay, zeroClickStay);
        await db.SaveChangesAsync();
        db.StayStayAmenities.Add(new StayStayAmenity
        {
            StayPlaceId = stay.PlaceId,
            StayAmenityId = wifiAmenity.Id
        });

        db.PlaceBookingPartners.AddRange(
            new PlaceBookingPartner
            {
                PlaceId = place.Id,
                BookingPartnerId = bookingPartner.Id,
                Url = "https://example.com/partner-place"
            },
            new PlaceBookingPartner
            {
                PlaceId = stayPlace.Id,
                BookingPartnerId = bookingPartner.Id,
                Url = "https://www.booking.com/hotel/hr/detail-stay.html"
            },
            new PlaceBookingPartner
            {
                PlaceId = stayPlace.Id,
                BookingPartnerId = airbnbPartner.Id,
                Url = "https://www.airbnb.com/rooms/detail-stay"
            },
            new PlaceBookingPartner
            {
                PlaceId = stayPlace.Id,
                BookingPartnerId = expediaPartner.Id,
                Url = "https://www.expedia.com/detail-stay"
            },
            new PlaceBookingPartner
            {
                PlaceId = zeroClickStayPlace.Id,
                BookingPartnerId = bookingPartner.Id,
                Url = "https://www.booking.com/hotel/hr/no-click-stay.html"
            },
            new PlaceBookingPartner
            {
                PlaceId = zeroClickStayPlace.Id,
                BookingPartnerId = airbnbPartner.Id,
                Url = "https://www.airbnb.com/rooms/no-click-stay"
            });

        var eventEntity = new Event
        {
            Name = "Detail Event",
            Summary = "Detail event summary.",
            EventTypeId = eventType.Id,
            DestinationId = destination.Id,
            LocationId = location.Id,
            PlaceId = place.Id,
            ParentEntityType = "Place",
            Description = "Detail event description.",
            VenueName = "Detail Venue",
            StartDate = recent.AddDays(5),
            EndDate = recent.AddDays(5).AddHours(2),
            TicketPrice = "12 EUR",
            IsFree = false,
            ExternalTicketLink = "https://example.com/tickets",
            HostedBy = "Detail Organizer",
            ContactEmail = "event@example.com",
            AttendanceLimit = 120,
            AgeRestriction = "All ages",
            Duration = "2h",
            CreatedAt = old,
            UpdatedAt = recent,
            PublishedAt = recent
        };
        db.Events.Add(eventEntity);
        await db.SaveChangesAsync();

        db.EntityImages.AddRange(
            new EntityImage
            {
                TargetType = EntityImageTargetType.Destination,
                DestinationId = destination.Id,
                Url = "/media/detail-destination.jpg",
                IsMain = true,
                CreatedAt = old
            },
            new EntityImage
            {
                TargetType = EntityImageTargetType.Place,
                PlaceId = place.Id,
                Url = "/media/detail-place.jpg",
                IsMain = true,
                CreatedAt = old
            },
            new EntityImage
            {
                TargetType = EntityImageTargetType.Event,
                EventId = eventEntity.Id,
                Url = "/media/detail-event.jpg",
                IsMain = true,
                CreatedAt = old
            });

        AddEntityViewEvents(db, AnalyticsEntityTypes.Destination, destination.Id, user.Id, recent, 1);
        AddEntityViewEvents(db, AnalyticsEntityTypes.Location, location.Id, user.Id, recent.AddMinutes(10), 2);
        AddEntityViewEvents(db, AnalyticsEntityTypes.Place, place.Id, user.Id, recent.AddMinutes(20), 3);
        AddEntityViewEvents(db, AnalyticsEntityTypes.Event, eventEntity.Id, user.Id, recent.AddMinutes(30), 1);

        db.AnalyticsEvents.AddRange(
            new AnalyticsEvent
            {
                EventType = AnalyticsEventTypes.OutboundLinkClicked,
                EntityType = AnalyticsEntityTypes.Place,
                EntityId = place.Id,
                UserId = user.Id,
                OccurredAt = recent.AddHours(2)
            },
            new AnalyticsEvent
            {
                EventType = AnalyticsEventTypes.OutboundLinkClicked,
                EntityType = AnalyticsEntityTypes.Hotel,
                EntityId = stayPlace.Id,
                UserId = user.Id,
                MetadataJson = "{\"provider\":\"booking\",\"partnerKey\":\"booking\",\"host\":\"booking.com\"}",
                OccurredAt = recent.AddHours(2).AddMinutes(1)
            },
            new AnalyticsEvent
            {
                EventType = AnalyticsEventTypes.OutboundLinkClicked,
                EntityType = AnalyticsEntityTypes.Hotel,
                EntityId = stayPlace.Id,
                UserId = user.Id,
                MetadataJson = "{\"provider\":\"booking\",\"partnerKey\":\"booking\",\"host\":\"www.booking.com\"}",
                OccurredAt = recent.AddHours(2).AddMinutes(2)
            },
            new AnalyticsEvent
            {
                EventType = AnalyticsEventTypes.OutboundLinkClicked,
                EntityType = AnalyticsEntityTypes.Hotel,
                EntityId = stayPlace.Id,
                UserId = user.Id,
                MetadataJson = "{\"provider\":\"airbnb\",\"partnerKey\":\"airbnb\",\"host\":\"airbnb.com\"}",
                OccurredAt = recent.AddHours(2).AddMinutes(3)
            },
            new AnalyticsEvent
            {
                EventType = AnalyticsEventTypes.OutboundLinkClicked,
                EntityType = AnalyticsEntityTypes.Hotel,
                EntityId = stayPlace.Id,
                UserId = user.Id,
                MetadataJson = "{\"provider\":\"expedia\",\"partnerKey\":\"external\",\"host\":\"expedia.com\"}",
                OccurredAt = recent.AddHours(2).AddMinutes(4)
            },
            new AnalyticsEvent
            {
                EventType = AnalyticsEventTypes.OutboundLinkClicked,
                EntityType = AnalyticsEntityTypes.Hotel,
                EntityId = stayPlace.Id,
                UserId = user.Id,
                MetadataJson = "{\"provider\":\"expedia\",\"partnerKey\":\"external\",\"host\":\"www.expedia.com\"}",
                OccurredAt = recent.AddHours(2).AddMinutes(5)
            },
            new AnalyticsEvent
            {
                EventType = AnalyticsEventTypes.OutboundLinkClicked,
                EntityType = AnalyticsEntityTypes.Hotel,
                EntityId = stayPlace.Id,
                UserId = user.Id,
                MetadataJson = "{\"provider\":\"expedia\",\"partnerKey\":\"external\",\"host\":\"expedia.com\"}",
                OccurredAt = recent.AddHours(2).AddMinutes(6)
            },
            new AnalyticsEvent
            {
                EventType = AnalyticsEventTypes.OutboundLinkClicked,
                EntityType = AnalyticsEntityTypes.Hotel,
                EntityId = stayPlace.Id,
                UserId = user.Id,
                MetadataJson = "{}",
                OccurredAt = recent.AddHours(2).AddMinutes(7)
            },
            new AnalyticsEvent
            {
                EventType = AnalyticsEventTypes.OutboundLinkClicked,
                EntityType = AnalyticsEntityTypes.Hotel,
                EntityId = stayPlace.Id,
                UserId = user.Id,
                MetadataJson = "{\"provider\":\"booking\",\"partnerKey\":\"booking\",\"host\":\"booking.com\"}",
                OccurredAt = old.AddDays(-10)
            },
            new AnalyticsEvent
            {
                EventType = AnalyticsEventTypes.DirectionsStarted,
                EntityType = AnalyticsEntityTypes.Hotel,
                EntityId = stayPlace.Id,
                UserId = user.Id,
                MetadataJson = "{\"provider\":\"booking\",\"partnerKey\":\"booking\",\"host\":\"booking.com\"}",
                OccurredAt = recent.AddHours(2).AddMinutes(8)
            },
            new AnalyticsEvent
            {
                EventType = AnalyticsEventTypes.DirectionsStarted,
                EntityType = AnalyticsEntityTypes.Event,
                EntityId = eventEntity.Id,
                UserId = user.Id,
                OccurredAt = recent.AddHours(3)
            });

        var collection = new Collection
        {
            Name = "Detail Collection",
            Description = "Detail collection.",
            UserId = user.Id,
            CreatedAt = recent
        };
        db.Collections.Add(collection);
        await db.SaveChangesAsync();
        db.CollectionItems.AddRange(
            new CollectionItem
            {
                CollectionId = collection.Id,
                DestinationId = destination.Id,
                CreatedAt = recent
            },
            new CollectionItem
            {
                CollectionId = collection.Id,
                LocationId = location.Id,
                CreatedAt = recent
            });
        db.Favorites.AddRange(
            new Favorite
            {
                EntityType = AnalyticsEntityTypes.Place,
                EntityId = place.Id,
                UserId = user.Id,
                CreatedAt = recent
            },
            new Favorite
            {
                EntityType = AnalyticsEntityTypes.Event,
                EntityId = eventEntity.Id,
                UserId = user.Id,
                CreatedAt = recent
            });

        db.DestinationReviews.Add(new DestinationReview
        {
            DestinationId = destination.Id,
            UserId = user.Id,
            Rating = 5,
            Comment = "Destination detail review.",
            CreatedAt = recent
        });
        db.LocationReviews.Add(new LocationReview
        {
            LocationId = location.Id,
            UserId = user.Id,
            Rating = 4,
            Comment = "Location detail review.",
            CreatedAt = recent
        });
        db.PlaceReviews.Add(new PlaceReview
        {
            PlaceId = place.Id,
            UserId = user.Id,
            Rating = 4,
            Comment = "Place detail review.",
            CreatedAt = recent
        });
        db.EventReviews.Add(new EventReview
        {
            EventId = eventEntity.Id,
            UserId = user.Id,
            Rating = 5,
            Comment = "Event detail review.",
            CreatedAt = recent
        });

        await db.SaveChangesAsync();

        return new EntityDetailFixture(
            assignedRegion.Id,
            unassignedRegion.Id,
            destination.Id,
            location.Id,
            place.Id,
            stayPlace.Id,
            zeroClickStayPlace.Id,
            noAnalyticsPlace.Id,
            eventEntity.Id,
            unassignedPlace.Id);
    }

    private static async Task<DestinationRankingFixture> CreateDestinationRankingFixtureAsync(GeotrackApiFactory app)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var now = DateTime.UtcNow;
        var old = now.AddDays(-45);
        var recent = now.AddDays(-2);
        var territory = await db.Territories.OrderBy(item => item.Id).FirstAsync();
        var destinationType = await db.DestinationTypes.OrderBy(item => item.Id).FirstAsync();
        var manager = await db.Users.SingleAsync(user => user.Email == "elena@example.com");
        var user = await db.Users.SingleAsync(user => user.Email == "marko.p@example.com");

        var assignedRegion = new Region
        {
            Name = $"Destination Ranking Assigned {Guid.NewGuid():N}",
            TerritoryId = territory.Id,
            CreatedAt = old
        };
        var unassignedRegion = new Region
        {
            Name = $"Destination Ranking Unassigned {Guid.NewGuid():N}",
            TerritoryId = territory.Id,
            CreatedAt = old
        };
        db.Regions.AddRange(assignedRegion, unassignedRegion);
        await db.SaveChangesAsync();

        await GrantRegionalManagerCoverageAsync(db, manager, assignedRegion.Id);

        var destinations = Enumerable
            .Range(1, 6)
            .Select(index => new Destination
            {
                Name = $"Ranking Destination {index}",
                DestinationTypeId = destinationType.Id,
                RegionId = assignedRegion.Id,
                Description = $"Assigned destination ranking fixture {index}.",
                Latitude = 40 + index / 10d,
                Longitude = 17 + index / 10d,
                CreatedAt = old
            })
            .ToList();

        var oldOnlyDestination = new Destination
        {
            Name = "Ranking Archive",
            DestinationTypeId = destinationType.Id,
            RegionId = assignedRegion.Id,
            Description = "Old destination ranking fixture.",
            Latitude = 41.9,
            Longitude = 18.9,
            CreatedAt = old
        };

        var unassignedDestination = new Destination
        {
            Name = "Ranking Out Region",
            DestinationTypeId = destinationType.Id,
            RegionId = unassignedRegion.Id,
            Description = "Unassigned destination ranking fixture.",
            Latitude = 42.9,
            Longitude = 19.9,
            CreatedAt = old
        };

        db.Destinations.AddRange(destinations);
        db.Destinations.AddRange(oldOnlyDestination, unassignedDestination);
        await db.SaveChangesAsync();

        for (var index = 0; index < destinations.Count; index++)
        {
            var destination = destinations[index];
            var visitCount = 7 - index;
            AddDestinationViewEvents(db, destination.Id, user.Id, recent, visitCount);
        }

        AddDestinationViewEvents(db, oldOnlyDestination.Id, user.Id, old, 9);
        AddDestinationViewEvents(db, unassignedDestination.Id, user.Id, recent, 20);
        await db.SaveChangesAsync();

        return new DestinationRankingFixture(
            assignedRegion.Id,
            unassignedRegion.Id,
            oldOnlyDestination.Id,
            unassignedDestination.Id);
    }

    private static async Task<TagViewsFixture> CreateTagViewsFixtureAsync(GeotrackApiFactory app)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var now = DateTime.UtcNow;
        var old = now.AddDays(-60);
        var recent = now.AddHours(-2);
        var territory = await db.Territories.OrderBy(item => item.Id).FirstAsync();
        var destinationType = await db.DestinationTypes.OrderBy(item => item.Id).FirstAsync();
        var locationType = await db.LocationTypes.OrderBy(item => item.Id).FirstAsync();
        var placeType = await db.PlaceTypes.OrderBy(item => item.Id).FirstAsync();
        var eventType = await db.EventTypes.OrderBy(item => item.Id).FirstAsync();
        var manager = await db.Users.SingleAsync(user => user.Email == "elena@example.com");
        var user = await db.Users.SingleAsync(user => user.Email == "marko.p@example.com");
        var suffix = Guid.NewGuid().ToString("N");

        var assignedRegion = new Region
        {
            Name = $"Tag Views Assigned {suffix}",
            TerritoryId = territory.Id,
            CreatedAt = old
        };
        var unassignedRegion = new Region
        {
            Name = $"Tag Views Unassigned {suffix}",
            TerritoryId = territory.Id,
            CreatedAt = old
        };
        db.Regions.AddRange(assignedRegion, unassignedRegion);
        await db.SaveChangesAsync();

        await GrantRegionalManagerCoverageAsync(db, manager, assignedRegion.Id);

        var assignedDestination = new Destination
        {
            Name = "Tag Views Destination",
            DestinationTypeId = destinationType.Id,
            RegionId = assignedRegion.Id,
            Description = "Assigned tag views destination.",
            Latitude = 41.1,
            Longitude = 17.1,
            CreatedAt = old
        };
        var unassignedDestination = new Destination
        {
            Name = "Tag Views Out Destination",
            DestinationTypeId = destinationType.Id,
            RegionId = unassignedRegion.Id,
            Description = "Unassigned tag views destination.",
            Latitude = 42.1,
            Longitude = 18.1,
            CreatedAt = old
        };
        db.Destinations.AddRange(assignedDestination, unassignedDestination);
        await db.SaveChangesAsync();

        var assignedLocation = new Location
        {
            Name = "Tag Views Location",
            DestinationId = assignedDestination.Id,
            LocationTypeId = locationType.Id,
            Description = "Assigned tag views location.",
            Latitude = 41.2,
            Longitude = 17.2,
            CreatedAt = old
        };
        var unassignedLocation = new Location
        {
            Name = "Tag Views Out Location",
            DestinationId = unassignedDestination.Id,
            LocationTypeId = locationType.Id,
            Description = "Unassigned tag views location.",
            Latitude = 42.2,
            Longitude = 18.2,
            CreatedAt = old
        };
        db.Locations.AddRange(assignedLocation, unassignedLocation);
        await db.SaveChangesAsync();

        var assignedPlace = new Place
        {
            Name = "Tag Views Place",
            PlaceTypeId = placeType.Id,
            LocationId = assignedLocation.Id,
            Description = "Assigned tag views place.",
            Latitude = 41.3,
            Longitude = 17.3,
            CreatedAt = old
        };
        var zeroViewPlace = new Place
        {
            Name = "Tag Views Zero Place",
            PlaceTypeId = placeType.Id,
            LocationId = assignedLocation.Id,
            Description = "Assigned zero-view tag views place.",
            Latitude = 41.4,
            Longitude = 17.4,
            CreatedAt = old
        };
        var unassignedPlace = new Place
        {
            Name = "Tag Views Out Place",
            PlaceTypeId = placeType.Id,
            LocationId = unassignedLocation.Id,
            Description = "Unassigned tag views place.",
            Latitude = 42.3,
            Longitude = 18.3,
            CreatedAt = old
        };
        db.Places.AddRange(assignedPlace, zeroViewPlace, unassignedPlace);
        await db.SaveChangesAsync();

        var assignedEvent = new Event
        {
            Name = "Tag Views Event",
            EventTypeId = eventType.Id,
            DestinationId = assignedDestination.Id,
            LocationId = assignedLocation.Id,
            Description = "Assigned tag views event.",
            StartDate = recent.AddDays(3),
            EndDate = recent.AddDays(4),
            CreatedAt = old
        };
        db.Events.Add(assignedEvent);
        await db.SaveChangesAsync();

        Tag NewTag(string label, string scope)
        {
            return new Tag
            {
                Name = $"{label} {suffix[..8]}",
                Slug = $"{label.ToLowerInvariant().Replace(" ", "-", StringComparison.Ordinal)}-{suffix}",
                Description = $"{label} tag views fixture.",
                ForDestination = scope == AnalyticsEntityTypes.Destination,
                ForLocation = scope == AnalyticsEntityTypes.Location,
                ForPlace = scope == AnalyticsEntityTypes.Place,
                ForEvent = scope == AnalyticsEntityTypes.Event,
                CreatedAt = old
            };
        }

        var destinationTag = NewTag("Tag Views Destination Tag", AnalyticsEntityTypes.Destination);
        var locationTag = NewTag("Tag Views Location Tag", AnalyticsEntityTypes.Location);
        var multiTag = NewTag("Tag Views Multi Tag", AnalyticsEntityTypes.Place);
        var placeTag = NewTag("Tag Views Place Tag", AnalyticsEntityTypes.Place);
        var eventTag = NewTag("Tag Views Event Tag", AnalyticsEntityTypes.Event);
        var zeroViewTag = NewTag("Tag Views Zero Tag", AnalyticsEntityTypes.Place);
        var unassignedOnlyTag = NewTag("Tag Views Out Tag", AnalyticsEntityTypes.Place);
        db.Tags.AddRange(destinationTag, locationTag, multiTag, placeTag, eventTag, zeroViewTag, unassignedOnlyTag);
        await db.SaveChangesAsync();

        db.DestinationTags.Add(new DestinationTag { DestinationId = assignedDestination.Id, TagId = destinationTag.Id });
        db.LocationTags.Add(new LocationTag { LocationId = assignedLocation.Id, TagId = locationTag.Id });
        db.PlaceTags.AddRange(
            new PlaceTag { PlaceId = assignedPlace.Id, TagId = multiTag.Id },
            new PlaceTag { PlaceId = assignedPlace.Id, TagId = placeTag.Id },
            new PlaceTag { PlaceId = zeroViewPlace.Id, TagId = zeroViewTag.Id },
            new PlaceTag { PlaceId = unassignedPlace.Id, TagId = unassignedOnlyTag.Id });
        db.EventTags.Add(new EventTag { EventId = assignedEvent.Id, TagId = eventTag.Id });

        AddEntityViewEvents(db, AnalyticsEntityTypes.Destination, assignedDestination.Id, user.Id, recent, 2);
        AddEntityViewEvents(db, AnalyticsEntityTypes.Location, assignedLocation.Id, user.Id, recent.AddMinutes(10), 1);
        AddEntityViewEvents(db, AnalyticsEntityTypes.Place, assignedPlace.Id, user.Id, recent.AddMinutes(20), 3);
        AddEntityViewEvents(db, AnalyticsEntityTypes.Event, assignedEvent.Id, user.Id, recent.AddMinutes(30), 4);
        AddEntityViewEvents(db, AnalyticsEntityTypes.Place, unassignedPlace.Id, user.Id, recent, 12);
        await db.SaveChangesAsync();

        return new TagViewsFixture(
            assignedRegion.Id,
            unassignedRegion.Id,
            destinationTag.Id,
            locationTag.Id,
            multiTag.Id,
            placeTag.Id,
            eventTag.Id,
            zeroViewTag.Id,
            unassignedOnlyTag.Id);
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

    private static void AddDestinationViewEvents(
        AppDbContext db,
        int destinationId,
        int userId,
        DateTime firstOccurredAt,
        int count)
    {
        for (var index = 0; index < count; index++)
        {
            db.AnalyticsEvents.Add(new AnalyticsEvent
            {
                EventType = index % 2 == 0 ? AnalyticsEventTypes.DetailView : AnalyticsEventTypes.PageView,
                EntityType = AnalyticsEntityTypes.Destination,
                EntityId = destinationId,
                UserId = userId,
                OccurredAt = firstOccurredAt.AddMinutes(index)
            });
        }
    }

    private static void AddEntityViewEvents(
        AppDbContext db,
        string entityType,
        int entityId,
        int userId,
        DateTime firstOccurredAt,
        int count)
    {
        for (var index = 0; index < count; index++)
        {
            db.AnalyticsEvents.Add(new AnalyticsEvent
            {
                EventType = index % 2 == 0 ? AnalyticsEventTypes.DetailView : AnalyticsEventTypes.PageView,
                EntityType = entityType,
                EntityId = entityId,
                UserId = userId,
                OccurredAt = firstOccurredAt.AddMinutes(index)
            });
        }
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
        int DestinationId,
        int LocationId,
        int PlaceId,
        int EventId);

    private sealed record EntityDetailFixture(
        int AssignedRegionId,
        int UnassignedRegionId,
        int DestinationId,
        int LocationId,
        int PlaceId,
        int StayPlaceId,
        int ZeroClickStayPlaceId,
        int NoAnalyticsPlaceId,
        int EventId,
        int UnassignedPlaceId);

    private sealed record DestinationRankingFixture(
        int AssignedRegionId,
        int UnassignedRegionId,
        int OldOnlyDestinationId,
        int UnassignedDestinationId);

    private sealed record TagViewsFixture(
        int AssignedRegionId,
        int UnassignedRegionId,
        int DestinationTagId,
        int LocationTagId,
        int MultiTagId,
        int PlaceTagId,
        int EventTagId,
        int ZeroViewTagId,
        int UnassignedOnlyTagId);
}
