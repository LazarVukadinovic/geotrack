using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Geotrack.Application.DTOs.Auth;
using Geotrack.Application.DTOs.Contributions;
using Geotrack.Application.DTOs.Notifications;
using Geotrack.Application.DTOs.Places;
using Geotrack.Domain.Entities.Users;
using Geotrack.Infrastructure;
using Geotrack.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Geotrack.IntegrationTests;

public sealed class ContributionPublicationTests
{
    [Fact]
    public async Task Approving_a_stay_contribution_preserves_contact_price_type_and_partner_urls()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();

        using var contributorClient = await LoginAsync(app, "marko.p@example.com", "password123");
        var createdContribution = await CreatePlaceContributionAsync(contributorClient);

        Assert.Equal("hello@betavilla.example", createdContribution.Contribution.ContactEmail);
        Assert.Equal(123.45m, createdContribution.Contribution.PricePerNight);
        Assert.Contains(
            createdContribution.Contribution.BookingPartners,
            partner => partner.Id == "booking" && partner.Url == "https://partners.example/booking/beta-villa");

        await GrantRegionalManagerCoverageAsync(app, "elena@example.com", "Dalmatia");

        using var reviewerClient = await LoginAsync(app, "elena@example.com", "password123");
        var approvalResponse = await reviewerClient.PostAsJsonAsync(
            $"/api/contributions/{createdContribution.Contribution.Id}/approve",
            new ReviewPlaceContributionRequestDto { Note = "Ready for publication." });
        approvalResponse.EnsureSuccessStatusCode();

        var approved = await approvalResponse.Content.ReadFromJsonAsync<PlaceContributionResponseDto>();
        Assert.NotNull(approved);
        Assert.Equal("Approved", approved.Contribution.Status);
        Assert.NotNull(approved.Contribution.PlaceId);

        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var publishedPlace = await db.Places
            .Include(place => place.PlaceType)
            .Include(place => place.PlaceBookingPartners)
                .ThenInclude(partner => partner.BookingPartner)
            .SingleAsync(place => place.Id == approved.Contribution.PlaceId!.Value);

        Assert.Equal("Beta Villa", publishedPlace.Name);
        Assert.Equal("Stay", publishedPlace.PlaceType.Name);
        Assert.Equal("hello@betavilla.example", publishedPlace.ContactEmail);
        Assert.Equal(123.45m, publishedPlace.Stay!.PricePerNight);

        var bookingPartner = publishedPlace.PlaceBookingPartners
            .Single(partner => partner.BookingPartner.Name == "Booking.com");
        Assert.Equal("https://partners.example/booking/beta-villa", bookingPartner.Url);

        var publicHotelDetail = await contributorClient.GetFromJsonAsync<HotelDetailDto>(
            $"/api/places/hotels/{approved.Contribution.PlaceId.Value}");
        Assert.NotNull(publicHotelDetail);
        Assert.Equal("hello@betavilla.example", publicHotelDetail.ContactEmail);
        Assert.Equal(123.45m, publicHotelDetail.PricePerNight);
        Assert.Contains(
            publicHotelDetail.BookingPartners,
            partner => partner.Name == "Booking.com" && partner.Url == "https://partners.example/booking/beta-villa");

        var notifications = await contributorClient.GetFromJsonAsync<GetUserNotificationsResponseDto>("/api/notifications");
        Assert.NotNull(notifications);
        var approvalNotification = Assert.Single(
            notifications!.Notifications,
            notification => notification.RelatedEntityId == createdContribution.Contribution.Id);
        Assert.Equal("contribution_approved", approvalNotification.Type);
        Assert.Equal(createdContribution.Contribution.Id, approvalNotification.RelatedEntityId);
        Assert.False(approvalNotification.IsRead);
        Assert.True(notifications.UnreadCount >= 1);
    }

    [Fact]
    public async Task Rejecting_a_stay_contribution_creates_a_notification_for_the_contributor()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();

        using var contributorClient = await LoginAsync(app, "marko.p@example.com", "password123");
        var createdContribution = await CreatePlaceContributionAsync(contributorClient);

        await GrantRegionalManagerCoverageAsync(app, "elena@example.com", "Dalmatia");

        using var reviewerClient = await LoginAsync(app, "elena@example.com", "password123");
        var rejectionResponse = await reviewerClient.PostAsJsonAsync(
            $"/api/contributions/{createdContribution.Contribution.Id}/reject",
            new ReviewPlaceContributionRequestDto { Note = "Please add clearer booking details." });
        rejectionResponse.EnsureSuccessStatusCode();

        var notifications = await contributorClient.GetFromJsonAsync<GetUserNotificationsResponseDto>("/api/notifications");
        Assert.NotNull(notifications);
        var rejectionNotification = Assert.Single(
            notifications!.Notifications,
            notification => notification.RelatedEntityId == createdContribution.Contribution.Id);
        Assert.Equal("contribution_rejected", rejectionNotification.Type);
        Assert.Equal("contribution", rejectionNotification.RelatedEntityType);
        Assert.Equal(createdContribution.Contribution.Id, rejectionNotification.RelatedEntityId);
        Assert.Equal($"/contributions/{createdContribution.Contribution.Id}", rejectionNotification.ActionUrl);
        Assert.False(rejectionNotification.IsRead);
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

    private static async Task<PlaceContributionResponseDto> CreatePlaceContributionAsync(HttpClient client)
    {
        var payload = new
        {
            name = "Beta Villa",
            stayType = "Villa",
            address = "123 Coast Road",
            city = "Dubrovnik",
            region = "Dalmatia",
            country = "Croatia",
            latitude = 42.6507,
            longitude = 18.0944,
            imagePosition = "50% 50%",
            imageSize = "cover",
            description = "A contribution used to validate publication mapping.",
            phone = "+3851234567",
            contactEmail = "hello@betavilla.example",
            website = "https://betavilla.example",
            pricePerNight = 123.45m,
            tags = new[] { "Coastal", "Private" },
            amenities = new[] { "wifi", "pool" },
            bookingPartners = new[]
            {
                new { id = "booking", url = "https://partners.example/booking/beta-villa" },
                new { id = "hotel", url = "https://betavilla.example/book" },
            },
        };

        using var formData = new MultipartFormDataContent();
        formData.Add(
            new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json"),
            "payload");

        var response = await client.PostAsync("/api/contributions", formData);
        response.EnsureSuccessStatusCode();

        var created = await response.Content.ReadFromJsonAsync<PlaceContributionResponseDto>();
        Assert.NotNull(created);
        return created;
    }

    private static async Task GrantRegionalManagerCoverageAsync(
        GeotrackApiFactory app,
        string reviewerEmail,
        string regionName)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var reviewer = await db.Users
            .Include(user => user.UserRoles)
                .ThenInclude(userRole => userRole.Role)
            .Include(user => user.RegionalManager)
                .ThenInclude(organization => organization!.RegionalManagerRegions)
                .ThenInclude(link => link.Region)
            .SingleAsync(user => user.Email == reviewerEmail);

        var regionalManagerRole = await db.Roles.SingleAsync(role => role.Name == "RegionalManager");
        if (!reviewer.UserRoles.Any(userRole => userRole.RoleId == regionalManagerRole.Id))
        {
            reviewer.UserRoles.Add(new UserRole
            {
                UserId = reviewer.Id,
                RoleId = regionalManagerRole.Id,
                Role = regionalManagerRole,
                User = reviewer,
            });
        }

        reviewer.RegionalManager ??= new RegionalManager
        {
            UserId = reviewer.Id,
            User = reviewer,
        };

        var region = await db.Regions.SingleAsync(existingRegion => existingRegion.Name == regionName);
        if (!reviewer.RegionalManager.RegionalManagerRegions.Any(link => link.RegionId == region.Id))
        {
            reviewer.RegionalManager.RegionalManagerRegions.Add(new RegionalManagerRegion
            {
                UserId = reviewer.Id,
                RegionId = region.Id,
                Region = region,
                CreatedAt = DateTime.UtcNow,
            });
        }

        await db.SaveChangesAsync();
    }
}

