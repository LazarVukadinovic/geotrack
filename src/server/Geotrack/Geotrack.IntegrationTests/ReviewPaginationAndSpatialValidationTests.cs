using System.Net;
using System.Net.Http.Json;
using Geotrack.Application.DTOs.Destinations;
using Geotrack.Application.DTOs.Locations;
using Geotrack.Application.DTOs.Places;
using Geotrack.Application.DTOs.Reviews;
using Geotrack.Domain.Entities.Core;
using Geotrack.Domain.Entities.Reviews;
using Geotrack.Domain.Entities.Users;
using Geotrack.Infrastructure;
using Geotrack.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Geotrack.IntegrationTests;

public sealed class ReviewPaginationAndSpatialValidationTests
{
    [Fact]
    public async Task Public_place_reviews_default_page_is_bounded_and_reports_metadata()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        using var client = app.CreateClient();
        var placeId = await CreatePlaceWithReviewsAsync(app, 12);

        var response = await client.GetFromJsonAsync<PagedReviewsResponseDto>($"/api/reviews/places/{placeId}");

        Assert.NotNull(response);
        Assert.Equal(10, response.Items.Count);
        Assert.Equal(12, response.TotalCount);
        Assert.Equal(0, response.Skip);
        Assert.Equal(10, response.Take);
        Assert.Equal(10, response.NextSkip);
        Assert.True(response.HasMore);
        Assert.Equal("Review 00", response.Items[0].Body);
    }

    [Fact]
    public async Task Public_place_reviews_custom_page_works_and_take_is_clamped()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        using var client = app.CreateClient();
        var placeId = await CreatePlaceWithReviewsAsync(app, 55);

        var customPage = await client.GetFromJsonAsync<PagedReviewsResponseDto>(
            $"/api/reviews/places/{placeId}?skip=10&take=5");

        Assert.NotNull(customPage);
        Assert.Equal(5, customPage.Items.Count);
        Assert.Equal(55, customPage.TotalCount);
        Assert.Equal(10, customPage.Skip);
        Assert.Equal(5, customPage.Take);
        Assert.Equal(15, customPage.NextSkip);
        Assert.True(customPage.HasMore);
        Assert.Equal("Review 10", customPage.Items[0].Body);

        var clampedPage = await client.GetFromJsonAsync<PagedReviewsResponseDto>(
            $"/api/reviews/places/{placeId}?skip=0&take=999");

        Assert.NotNull(clampedPage);
        Assert.Equal(50, clampedPage.Items.Count);
        Assert.Equal(50, clampedPage.Take);
        Assert.True(clampedPage.HasMore);
    }

    [Fact]
    public async Task Public_review_endpoints_reject_invalid_paging_parameters()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        using var client = app.CreateClient();
        var placeId = await CreatePlaceWithReviewsAsync(app, 1);
        var eventId = await CreateEventWithReviewsAsync(app, 1);

        var negativeSkip = await client.GetAsync($"/api/reviews/places/{placeId}?skip=-1&take=10");
        var invalidTake = await client.GetAsync($"/api/reviews/events/{eventId}?skip=0&take=0");
        var wrapperInvalidTake = await client.GetAsync($"/api/places/{placeId}/reviews?skip=0&take=0");

        Assert.Equal(HttpStatusCode.BadRequest, negativeSkip.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, invalidTake.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, wrapperInvalidTake.StatusCode);
    }

    [Fact]
    public async Task Place_review_wrapper_is_paged_and_detail_preview_remains_bounded()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        using var client = app.CreateClient();
        var placeId = await CreatePlaceWithReviewsAsync(app, 12);

        var reviews = await client.GetFromJsonAsync<GetPlaceReviewsResponseDto>(
            $"/api/places/{placeId}/reviews?skip=2&take=3");

        Assert.NotNull(reviews);
        Assert.Equal(3, reviews.Reviews.Count);
        Assert.Equal(12, reviews.TotalCount);
        Assert.Equal(12, reviews.ReviewsCount);
        Assert.Equal(2, reviews.Skip);
        Assert.Equal(3, reviews.Take);
        Assert.True(reviews.HasMore);

        var detail = await client.GetFromJsonAsync<PlaceDetailDto>($"/api/places/{placeId}");

        Assert.NotNull(detail);
        Assert.Equal(10, detail.Reviews.Count);
        Assert.Equal("12 reviews", detail.ReviewCount);
    }

    [Fact]
    public async Task Destination_and_location_review_wrappers_are_paged()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        using var client = app.CreateClient();
        var destinationId = await CreateDestinationWithReviewsAsync(app, 6);
        var locationId = await CreateLocationWithReviewsAsync(app, 6);

        var destinationReviews = await client.GetFromJsonAsync<GetDestinationReviewsResponseDto>(
            $"/api/destinations/{destinationId}/reviews?skip=1&take=2");
        var locationReviews = await client.GetFromJsonAsync<GetLocationReviewsResponseDto>(
            $"/api/locations/{locationId}/reviews?skip=1&take=2");

        Assert.NotNull(destinationReviews);
        Assert.NotNull(locationReviews);
        Assert.Equal(2, destinationReviews.Reviews.Count);
        Assert.Equal(6, destinationReviews.TotalCount);
        Assert.Equal(1, destinationReviews.Skip);
        Assert.Equal(2, destinationReviews.Take);
        Assert.True(destinationReviews.HasMore);
        Assert.Equal(2, locationReviews.Reviews.Count);
        Assert.Equal(6, locationReviews.TotalCount);
        Assert.Equal(1, locationReviews.Skip);
        Assert.Equal(2, locationReviews.Take);
        Assert.True(locationReviews.HasMore);
    }

    [Fact]
    public async Task Legacy_destination_explore_rejects_invalid_coordinates_before_shared_explore()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        using var client = app.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/destinations/explore-by-location",
            new { latitude = 91, longitude = 20 });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Place_and_event_listing_coordinates_are_validated_before_spatial_queries()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        using var client = app.CreateClient();
        var destination = await GetFirstDestinationAsync(app);

        var invalidPlaces = await client.GetAsync(
            $"/api/places?destinationId={destination.Id}&latitude=91&longitude={destination.Longitude}&skip=0&take=1");
        var invalidEvents = await client.GetAsync(
            $"/api/events?destinationId={destination.Id}&latitude={destination.Latitude}&longitude=181&skip=0&take=1");

        Assert.Equal(HttpStatusCode.BadRequest, invalidPlaces.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, invalidEvents.StatusCode);

        var validPlaces = await client.GetAsync(
            $"/api/places?destinationId={destination.Id}&latitude={destination.Latitude}&longitude={destination.Longitude}&skip=0&take=1");
        var validEvents = await client.GetAsync(
            $"/api/events?destinationId={destination.Id}&latitude={destination.Latitude}&longitude={destination.Longitude}&skip=0&take=1");

        validPlaces.EnsureSuccessStatusCode();
        validEvents.EnsureSuccessStatusCode();
    }

    private static async Task<int> CreatePlaceWithReviewsAsync(GeotrackApiFactory app, int reviewCount)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var location = await db.Locations.OrderBy(item => item.Id).FirstAsync();
        var placeType = await db.PlaceTypes.OrderBy(item => item.Id).FirstAsync();
        var now = DateTime.UtcNow;
        var suffix = Guid.NewGuid().ToString("N");

        var place = new Place
        {
            Name = $"Paged review place {suffix}",
            Description = "Place used by review pagination tests.",
            PlaceTypeId = placeType.Id,
            LocationId = location.Id,
            Latitude = location.Latitude,
            Longitude = location.Longitude,
            CreatedAt = now
        };

        db.Places.Add(place);
        await db.SaveChangesAsync();

        for (var i = 0; i < reviewCount; i++)
        {
            var user = CreateUser($"place-review-{suffix}-{i:D2}");
            db.Users.Add(user);
            db.PlaceReviews.Add(new PlaceReview
            {
                PlaceId = place.Id,
                User = user,
                Rating = 5 - (i % 2),
                Comment = $"Review {i:D2}",
                CreatedAt = now.AddMinutes(-i),
                UpdatedAt = now.AddMinutes(-i)
            });
        }

        await db.SaveChangesAsync();
        return place.Id;
    }

    private static async Task<int> CreateEventWithReviewsAsync(GeotrackApiFactory app, int reviewCount)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var location = await db.Locations.OrderBy(item => item.Id).FirstAsync();
        var eventType = await db.EventTypes.OrderBy(item => item.Id).FirstAsync();
        var now = DateTime.UtcNow;
        var suffix = Guid.NewGuid().ToString("N");

        var @event = new Event
        {
            Name = $"Paged review event {suffix}",
            Description = "Event used by review pagination tests.",
            EventTypeId = eventType.Id,
            LocationId = location.Id,
            StartDate = now.AddDays(1),
            EndDate = now.AddDays(1).AddHours(2),
            CreatedAt = now
        };

        db.Events.Add(@event);
        await db.SaveChangesAsync();

        for (var i = 0; i < reviewCount; i++)
        {
            var user = CreateUser($"event-review-{suffix}-{i:D2}");
            db.Users.Add(user);
            db.EventReviews.Add(new EventReview
            {
                EventId = @event.Id,
                User = user,
                Rating = 4,
                Comment = $"Event review {i:D2}",
                CreatedAt = now.AddMinutes(-i),
                UpdatedAt = now.AddMinutes(-i)
            });
        }

        await db.SaveChangesAsync();
        return @event.Id;
    }

    private static async Task<int> CreateDestinationWithReviewsAsync(GeotrackApiFactory app, int reviewCount)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var region = await db.Regions.OrderBy(item => item.Id).FirstAsync();
        var destinationType = await db.DestinationTypes.OrderBy(item => item.Id).FirstAsync();
        var now = DateTime.UtcNow;
        var suffix = Guid.NewGuid().ToString("N");

        var destination = new Destination
        {
            Name = $"Paged review destination {suffix}",
            Description = "Destination used by review pagination tests.",
            RegionId = region.Id,
            DestinationTypeId = destinationType.Id,
            Latitude = 44.8125,
            Longitude = 20.4612,
            CreatedAt = now
        };

        db.Destinations.Add(destination);
        await db.SaveChangesAsync();

        for (var i = 0; i < reviewCount; i++)
        {
            var user = CreateUser($"destination-review-{suffix}-{i:D2}");
            db.Users.Add(user);
            db.DestinationReviews.Add(new DestinationReview
            {
                DestinationId = destination.Id,
                User = user,
                Rating = 4,
                Comment = $"Destination review {i:D2}",
                CreatedAt = now.AddMinutes(-i),
                UpdatedAt = now.AddMinutes(-i)
            });
        }

        await db.SaveChangesAsync();
        return destination.Id;
    }

    private static async Task<int> CreateLocationWithReviewsAsync(GeotrackApiFactory app, int reviewCount)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var destination = await db.Destinations.OrderBy(item => item.Id).FirstAsync();
        var locationType = await db.LocationTypes.OrderBy(item => item.Id).FirstAsync();
        var now = DateTime.UtcNow;
        var suffix = Guid.NewGuid().ToString("N");

        var location = new Location
        {
            Name = $"Paged review location {suffix}",
            DestinationId = destination.Id,
            LocationTypeId = locationType.Id,
            Latitude = destination.Latitude,
            Longitude = destination.Longitude,
            CreatedAt = now
        };

        db.Locations.Add(location);
        await db.SaveChangesAsync();

        for (var i = 0; i < reviewCount; i++)
        {
            var user = CreateUser($"location-review-{suffix}-{i:D2}");
            db.Users.Add(user);
            db.LocationReviews.Add(new LocationReview
            {
                LocationId = location.Id,
                User = user,
                Rating = 4,
                Comment = $"Location review {i:D2}",
                CreatedAt = now.AddMinutes(-i),
                UpdatedAt = now.AddMinutes(-i)
            });
        }

        await db.SaveChangesAsync();
        return location.Id;
    }

    private static User CreateUser(string prefix)
    {
        return new User
        {
            Username = prefix,
            Email = $"{prefix}@example.com",
            FirstName = "Review",
            LastName = "Tester",
            PasswordHash = "unused",
            PhoneNumber = string.Empty,
            CreatedAt = DateTime.UtcNow
        };
    }

    private static async Task<(int Id, double Latitude, double Longitude)> GetFirstDestinationAsync(GeotrackApiFactory app)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var destination = await db.Destinations
            .AsNoTracking()
            .OrderBy(item => item.Id)
            .Select(item => new { item.Id, item.Latitude, item.Longitude })
            .FirstAsync();

        return (destination.Id, destination.Latitude, destination.Longitude);
    }
}
