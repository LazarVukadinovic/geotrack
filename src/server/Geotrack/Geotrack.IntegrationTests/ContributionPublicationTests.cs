using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Geotrack.Application.DTOs.Admin;
using Geotrack.Application.DTOs.Auth;
using Geotrack.Application.DTOs.Contributions;
using Geotrack.Application.DTOs.Notifications;
using Geotrack.Application.DTOs.Places;
using Geotrack.Domain.Entities.Core;
using Geotrack.Domain.Entities.Users;
using Geotrack.Infrastructure;
using Geotrack.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Coordinate = NetTopologySuite.Geometries.Coordinate;
using Geometry = NetTopologySuite.Geometries.Geometry;
using GeometryFactory = NetTopologySuite.Geometries.GeometryFactory;
using Polygon = NetTopologySuite.Geometries.Polygon;
using PrecisionModel = NetTopologySuite.Geometries.PrecisionModel;

namespace Geotrack.IntegrationTests;

public sealed class ContributionPublicationTests
{
    private const string BoundaryGeometryPropertyName = "BoundaryGeometry";
    private const string SeedRegionName = "Dubrovnik-Neretva";
    private static readonly GeometryFactory GeometryFactory = new(new PrecisionModel(), 4326);

    [Fact]
    public async Task Approving_a_place_contribution_for_a_stay_preserves_contact_price_type_and_partner_urls()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();

        using var contributorClient = await LoginAsync(app, "marko.p@example.com", "password123");
        var createdContribution = await CreatePlaceContributionAsync(contributorClient);

        Assert.Equal("hello@betavilla.example", createdContribution.Contribution.ContactEmail);
        Assert.Equal(123.45m, createdContribution.Contribution.PricePerNight);
        Assert.Contains(
            createdContribution.Contribution.BookingPartners,
            partner => partner.Id == "booking" && partner.Url == "https://partners.example/booking/beta-villa");

        await GrantRegionalManagerCoverageAsync(app, "elena@example.com", SeedRegionName);

        using var reviewerClient = await LoginAsync(app, "elena@example.com", "password123");
        var approvalResponse = await reviewerClient.PostAsJsonAsync(
            $"/api/contributions/{createdContribution.Contribution.Id}/approve",
            new ReviewPlaceContributionRequestDto { Note = "Ready for publication." });
        approvalResponse.EnsureSuccessStatusCode();

        var approved = await approvalResponse.Content.ReadFromJsonAsync<PlaceContributionResponseDto>();
        Assert.NotNull(approved);
        Assert.Equal("Approved", approved.Contribution.Status);
        Assert.NotNull(approved.Contribution.PlaceId);

        var repeatedApprovalResponse = await reviewerClient.PostAsJsonAsync(
            $"/api/contributions/{createdContribution.Contribution.Id}/approve",
            new ReviewPlaceContributionRequestDto { Note = "Retry should not duplicate." });
        Assert.Equal(HttpStatusCode.BadRequest, repeatedApprovalResponse.StatusCode);

        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var publishedPlace = await db.Places
            .Include(place => place.PlaceType)
            .Include(place => place.Stay)
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
        Assert.Equal("Your contribution has been approved and published.", approvalNotification.Message);
        Assert.Equal($"/stays/{approved.Contribution.PlaceId.Value}", approvalNotification.ActionUrl);
        Assert.Equal(approvalNotification.ActionUrl, approvalNotification.TargetRoute);
        Assert.False(approvalNotification.IsRead);
        Assert.True(notifications.UnreadCount >= 1);
    }

    [Fact]
    public async Task Rejecting_a_place_contribution_for_a_stay_creates_a_notification_for_the_contributor()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();

        using var contributorClient = await LoginAsync(app, "marko.p@example.com", "password123");
        var createdContribution = await CreatePlaceContributionAsync(contributorClient);

        await GrantRegionalManagerCoverageAsync(app, "elena@example.com", SeedRegionName);

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

    [Fact]
    public async Task Regional_manager_cannot_review_update_for_target_place_outside_assigned_region()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        var fixture = await CreateContributionScopeFixtureAsync(app);

        using var contributorClient = await LoginAsync(app, "marko.p@example.com", "password123");
        var contribution = await CreateUpdatePlaceContributionAsync(
            contributorClient,
            fixture.OutOfScopePlaceId,
            fixture.InScopeLocationId,
            fixture.PlaceTypeId,
            "Out of scope overwrite");

        await GrantRegionalManagerCoverageAsync(app, "elena@example.com", SeedRegionName);

        using var reviewerClient = await LoginAsync(app, "elena@example.com", "password123");
        var queue = await reviewerClient.GetFromJsonAsync<GetPlaceContributionsResponseDto>(
            "/api/contributions/review?status=pending");

        Assert.NotNull(queue);
        Assert.DoesNotContain(queue.Contributions, item => item.Id == contribution.Contribution.Id);

        var rejectResponse = await reviewerClient.PostAsJsonAsync(
            $"/api/contributions/{contribution.Contribution.Id}/reject",
            new ReviewPlaceContributionRequestDto { Note = "Out of scope." });
        await AssertForbiddenAsync(rejectResponse);

        var approvalResponse = await reviewerClient.PostAsJsonAsync(
            $"/api/contributions/{contribution.Contribution.Id}/approve",
            new ReviewPlaceContributionRequestDto { Note = "Exploit attempt." });
        await AssertForbiddenAsync(approvalResponse);

        await AssertPlaceUnchangedAndContributionPendingAsync(
            app,
            fixture.OutOfScopePlaceId,
            fixture.OutOfScopePlaceName,
            fixture.OutOfScopeLocationId,
            contribution.Contribution.Id);
    }

    [Fact]
    public async Task Regional_manager_cannot_move_in_scope_place_to_out_of_scope_region()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        var fixture = await CreateContributionScopeFixtureAsync(app);

        using var contributorClient = await LoginAsync(app, "marko.p@example.com", "password123");
        var contribution = await CreateUpdatePlaceContributionAsync(
            contributorClient,
            fixture.InScopePlaceId,
            fixture.OutOfScopeLocationId,
            fixture.PlaceTypeId,
            "Out of region move");

        await GrantRegionalManagerCoverageAsync(app, "elena@example.com", SeedRegionName);

        using var reviewerClient = await LoginAsync(app, "elena@example.com", "password123");
        var approvalResponse = await reviewerClient.PostAsJsonAsync(
            $"/api/contributions/{contribution.Contribution.Id}/approve",
            new ReviewPlaceContributionRequestDto { Note = "Move out of scope." });

        await AssertForbiddenAsync(approvalResponse);
        await AssertPlaceUnchangedAndContributionPendingAsync(
            app,
            fixture.InScopePlaceId,
            fixture.InScopePlaceName,
            fixture.InScopeLocationId,
            contribution.Contribution.Id);
    }

    [Fact]
    public async Task Regional_manager_can_approve_update_when_current_and_proposed_regions_are_in_scope()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        var fixture = await CreateContributionScopeFixtureAsync(app);

        using var contributorClient = await LoginAsync(app, "marko.p@example.com", "password123");
        var contribution = await CreateUpdatePlaceContributionAsync(
            contributorClient,
            fixture.InScopePlaceId,
            fixture.InScopeLocationId,
            fixture.PlaceTypeId,
            "Approved in-region update");

        await GrantRegionalManagerCoverageAsync(app, "elena@example.com", SeedRegionName);

        using var reviewerClient = await LoginAsync(app, "elena@example.com", "password123");
        var approvalResponse = await reviewerClient.PostAsJsonAsync(
            $"/api/contributions/{contribution.Contribution.Id}/approve",
            new ReviewPlaceContributionRequestDto { Note = "In region." });
        approvalResponse.EnsureSuccessStatusCode();

        var approved = await approvalResponse.Content.ReadFromJsonAsync<PlaceContributionResponseDto>();
        Assert.NotNull(approved);
        Assert.Equal("Approved", approved.Contribution.Status);

        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var place = await db.Places.SingleAsync(existingPlace => existingPlace.Id == fixture.InScopePlaceId);

        Assert.Equal("Approved in-region update", place.Name);
        Assert.Equal(fixture.InScopeLocationId, place.LocationId);
    }

    [Fact]
    public async Task Admin_can_approve_cross_region_update_contribution()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        var fixture = await CreateContributionScopeFixtureAsync(app);

        using var contributorClient = await LoginAsync(app, "marko.p@example.com", "password123");
        var contribution = await CreateUpdatePlaceContributionAsync(
            contributorClient,
            fixture.OutOfScopePlaceId,
            fixture.InScopeLocationId,
            fixture.PlaceTypeId,
            "Admin cross-region update");

        using var adminClient = await RegisterAdminAndLoginAsync(app, "contribution-admin");
        var queue = await adminClient.GetFromJsonAsync<GetPlaceContributionsResponseDto>(
            "/api/contributions/review?status=pending");

        Assert.NotNull(queue);
        Assert.Contains(queue.Contributions, item => item.Id == contribution.Contribution.Id);

        var approvalResponse = await adminClient.PostAsJsonAsync(
            $"/api/contributions/{contribution.Contribution.Id}/approve",
            new ReviewPlaceContributionRequestDto { Note = "Admin-only move." });
        approvalResponse.EnsureSuccessStatusCode();

        var approved = await approvalResponse.Content.ReadFromJsonAsync<PlaceContributionResponseDto>();
        Assert.NotNull(approved);
        Assert.Equal("Approved", approved.Contribution.Status);

        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var place = await db.Places.SingleAsync(existingPlace => existingPlace.Id == fixture.OutOfScopePlaceId);

        Assert.Equal("Admin cross-region update", place.Name);
        Assert.Equal(fixture.InScopeLocationId, place.LocationId);
    }

    [Fact]
    public async Task Canonical_places_route_creates_place_contribution_without_stay_only_fields()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        using var client = await LoginAsync(app, "marko.p@example.com", "password123");

        var payload = CreateGenericPlaceContributionPayload("Canonical Place Contribution Cafe");

        var response = await PostContributionAsync(client, payload, "/api/contributions/places");

        response.EnsureSuccessStatusCode();
        var created = await response.Content.ReadFromJsonAsync<PlaceContributionResponseDto>();
        Assert.NotNull(created);
        Assert.Equal("Place", created.Contribution.EntityType);
        Assert.Equal("Cafe", created.Contribution.PlaceType);
        Assert.Null(created.Contribution.StayType);
        Assert.Null(created.Contribution.StayTypeId);
        Assert.Null(created.Contribution.PricePerNight);
        Assert.Empty(created.Contribution.BookingPartners);

        var mine = await client.GetFromJsonAsync<GetPlaceContributionsResponseDto>("/api/contributions");
        Assert.NotNull(mine);
        var listItem = Assert.Single(mine!.Contributions, item => item.Id == created.Contribution.Id);
        Assert.Equal("Place", listItem.EntityType);
        Assert.Equal("Cafe", listItem.PlaceType);
    }

    [Theory]
    [InlineData("Stay")]
    [InlineData("Restaurant")]
    [InlineData("Cafe")]
    public async Task Create_requires_ownership_proof_for_proof_required_place_types(string placeType)
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        using var client = await LoginAsync(app, "marko.p@example.com", "password123");
        var payload = CreatePayloadForPlaceType($"Missing Proof {placeType}", placeType);

        var response = await PostContributionAsync(
            client,
            payload,
            ownershipProof: OwnershipProofUpload.Missing);

        await AssertErrorAsync(
            response,
            HttpStatusCode.BadRequest,
            "Ownership proof document is required.");
    }

    [Theory]
    [InlineData("Landmark")]
    [InlineData("Nature")]
    [InlineData("Beach")]
    public async Task Create_does_not_require_ownership_proof_for_non_business_place_types(string placeType)
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        using var client = await LoginAsync(app, "marko.p@example.com", "password123");
        var payload = CreatePayloadForPlaceType($"No Proof {placeType}", placeType);

        var response = await PostContributionAsync(
            client,
            payload,
            ownershipProof: OwnershipProofUpload.Missing);

        response.EnsureSuccessStatusCode();
        var created = await response.Content.ReadFromJsonAsync<PlaceContributionResponseDto>();
        Assert.NotNull(created);
        Assert.Equal("PendingReview", created!.Contribution.Status);
        Assert.Equal(placeType, created.Contribution.PlaceType);
        Assert.False(created.Contribution.HasOwnershipProofDocument);
    }

    [Theory]
    [InlineData("Restaurant", OwnershipProofUpload.InvalidText, "Ownership proof document must be a PDF file.")]
    [InlineData("Cafe", OwnershipProofUpload.OversizedPdf, "Ownership proof document must be 10 MB or smaller.")]
    public async Task Create_rejects_invalid_ownership_proof_documents(
        string placeType,
        OwnershipProofUpload ownershipProof,
        string expectedMessage)
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        using var client = await LoginAsync(app, "marko.p@example.com", "password123");
        var payload = CreatePayloadForPlaceType($"Invalid Proof {placeType}", placeType);

        var response = await PostContributionAsync(
            client,
            payload,
            ownershipProof: ownershipProof);

        await AssertErrorAsync(response, HttpStatusCode.BadRequest, expectedMessage);
    }

    [Theory]
    [InlineData("Stay")]
    [InlineData("Restaurant")]
    [InlineData("Cafe")]
    public async Task Create_saves_valid_proof_required_place_contributions_as_pending(string placeType)
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        using var client = await LoginAsync(app, "marko.p@example.com", "password123");
        var payload = CreatePayloadForPlaceType($"Valid Proof {placeType}", placeType);

        var response = await PostContributionAsync(client, payload);

        response.EnsureSuccessStatusCode();
        var created = await response.Content.ReadFromJsonAsync<PlaceContributionResponseDto>();
        Assert.NotNull(created);
        Assert.Equal("PendingReview", created!.Contribution.Status);
        Assert.Equal(placeType, created.Contribution.PlaceType);
        Assert.True(created.Contribution.HasOwnershipProofDocument);
        Assert.Equal("ownership-proof.pdf", created.Contribution.OwnershipProofOriginalFileName);
        Assert.Equal("application/pdf", created.Contribution.OwnershipProofContentType);
        Assert.StartsWith("/media/uploads/contributions/", created.Contribution.OwnershipProofDocumentUrl);

        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var saved = await db.PlaceContributions
            .Include(item => item.Location)
                .ThenInclude(location => location!.Destination)
                    .ThenInclude(destination => destination.Region)
            .SingleAsync(item => item.Id == created.Contribution.Id);

        Assert.Equal(PlaceContributionStatus.PendingReview, saved.Status);
        Assert.False(string.IsNullOrWhiteSpace(saved.OwnershipProofDocumentUrl));
        Assert.Equal(SeedRegionName, saved.Location!.Destination.Region.Name);
    }

    [Fact]
    public async Task Submitted_place_contribution_appears_in_regional_requests_queue_for_assigned_region_only()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();

        using var contributorClient = await LoginAsync(app, "marko.p@example.com", "password123");
        var payload = CreatePayloadForPlaceType("Queue Visible Restaurant", "Restaurant");
        payload["city"] = "Unmatched submitted city";
        payload["region"] = "Untrusted submitted region";

        var createResponse = await PostContributionAsync(contributorClient, payload);
        createResponse.EnsureSuccessStatusCode();
        var created = await createResponse.Content.ReadFromJsonAsync<PlaceContributionResponseDto>();
        Assert.NotNull(created);
        var createdId = created!.Contribution.Id;

        await GrantRegionalManagerCoverageAsync(app, "elena@example.com", SeedRegionName);

        using var reviewerClient = await LoginAsync(app, "elena@example.com", "password123");
        var seedRegionId = await GetRegionIdAsync(app, SeedRegionName);
        var queue = await reviewerClient.GetFromJsonAsync<RegionalRequestQueueResponseDto>(
            $"/api/admin/regional/requests?regionId={seedRegionId}&type=place&status=pending");

        Assert.NotNull(queue);
        Assert.Contains(queue!.Items, item =>
            item.RequestKey == $"contribution:{createdId}" &&
            item.EntityType == "Place" &&
            item.Status == "Pending");
        Assert.True(queue.CountsByType.Places >= 1);

        var detail = await reviewerClient.GetFromJsonAsync<RegionalRequestQueueDetailDto>(
            $"/api/admin/regional/requests/contribution/{createdId}?regionId={seedRegionId}");
        Assert.NotNull(detail);
        Assert.True(detail!.HasOwnershipProofDocument);
        Assert.Equal("ownership-proof.pdf", detail.OwnershipProofOriginalFileName);
        Assert.Equal("application/pdf", detail.OwnershipProofContentType);
        Assert.StartsWith("/media/uploads/contributions/", detail.OwnershipProofDocumentUrl);

        var unassignedRegionId = await CreateUnassignedRegionAsync(app);
        var wrongRegionResponse = await reviewerClient.GetAsync(
            $"/api/admin/regional/requests?regionId={unassignedRegionId}&type=place&status=pending");
        Assert.Equal(HttpStatusCode.Forbidden, wrongRegionResponse.StatusCode);

        using var outOfRegionManager = await RegisterRegionalManagerAndLoginAsync(app, "out-region-manager", unassignedRegionId);
        var outOfRegionQueue = await outOfRegionManager.GetFromJsonAsync<RegionalRequestQueueResponseDto>(
            $"/api/admin/regional/requests?regionId={unassignedRegionId}&type=place&status=pending");
        Assert.NotNull(outOfRegionQueue);
        Assert.DoesNotContain(outOfRegionQueue!.Items, item => item.RequestKey == $"contribution:{createdId}");

        var outOfRegionDetail = await outOfRegionManager.GetAsync(
            $"/api/admin/regional/requests/contribution/{createdId}?regionId={unassignedRegionId}");
        Assert.Equal(HttpStatusCode.NotFound, outOfRegionDetail.StatusCode);
    }

    [Fact]
    public async Task Legacy_stays_route_remains_a_compatibility_alias_for_place_contributions()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        using var client = await LoginAsync(app, "marko.p@example.com", "password123");

        var payload = CreateValidContributionPayload("Legacy Alias Place Contribution Villa");

        var response = await PostContributionAsync(client, payload, "/api/contributions/stays");

        response.EnsureSuccessStatusCode();
        var created = await response.Content.ReadFromJsonAsync<PlaceContributionResponseDto>();
        Assert.NotNull(created);
        Assert.Equal("Place", created.Contribution.EntityType);
        Assert.Equal("Stay", created.Contribution.PlaceType);
        Assert.Equal("Villa", created.Contribution.StayType);
    }

    [Fact]
    public async Task Create_rejects_contribution_without_confirmed_location_before_queueing()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        using var client = await LoginAsync(app, "marko.p@example.com", "password123");
        var payload = CreateValidContributionPayload("Missing Location Validation Villa");
        payload["latitude"] = null;
        payload["longitude"] = null;

        var response = await PostContributionAsync(client, payload);

        await AssertErrorAsync(
            response,
            HttpStatusCode.BadRequest,
            "Please choose a valid location before submitting.");

        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.False(await db.PlaceContributions.AnyAsync(x => x.Name == "Missing Location Validation Villa"));
    }

    [Fact]
    public async Task Create_rejects_malformed_website_booking_and_external_image_urls()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        using var client = await LoginAsync(app, "marko.p@example.com", "password123");

        var websitePayload = CreateValidContributionPayload("Bad Website Validation Villa");
        websitePayload["website"] = "javascript:alert(1)";
        await AssertErrorAsync(
            await PostContributionAsync(client, websitePayload),
            HttpStatusCode.BadRequest,
            "Website URL must be a valid https URL.");

        var bookingPayload = CreateValidContributionPayload("Bad Booking Validation Villa");
        bookingPayload["bookingPartners"] = new[]
        {
            new Dictionary<string, object?>
            {
                ["id"] = "booking",
                ["url"] = "data:text/html,hello"
            }
        };
        await AssertErrorAsync(
            await PostContributionAsync(client, bookingPayload),
            HttpStatusCode.BadRequest,
            "Booking partner URL must be a valid https URL.");

        var imagePayload = CreateValidContributionPayload("Bad Image Validation Villa");
        imagePayload["images"] = new[]
        {
            new Dictionary<string, object?>
            {
                ["url"] = "file:///tmp/stay.jpg",
                ["isMain"] = true,
                ["sortOrder"] = 0
            }
        };
        await AssertErrorAsync(
            await PostContributionAsync(client, imagePayload),
            HttpStatusCode.BadRequest,
            "Image URL must be a valid https URL.");
    }

    [Fact]
    public async Task Create_rejects_overly_long_url_fields()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        using var client = await LoginAsync(app, "marko.p@example.com", "password123");
        var payload = CreateValidContributionPayload("Long Url Validation Villa");
        payload["website"] = $"https://example.com/{new string('a', 500)}";

        var response = await PostContributionAsync(client, payload);

        await AssertErrorAsync(
            response,
            HttpStatusCode.BadRequest,
            "Website URL must be 500 characters or fewer.");
    }

    [Fact]
    public async Task Create_allows_safe_managed_contribution_media_path()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        using var client = await LoginAsync(app, "marko.p@example.com", "password123");
        const string mediaPath = "/media/uploads/contributions/user-1-stay-0123456789abcdef0123456789abcdef.jpg";
        var payload = CreateValidContributionPayload("Managed Media Validation Villa");
        payload["images"] = new[]
        {
            new Dictionary<string, object?>
            {
                ["url"] = mediaPath,
                ["isMain"] = true,
                ["sortOrder"] = 0
            }
        };

        var response = await PostContributionAsync(client, payload);

        response.EnsureSuccessStatusCode();
        var created = await response.Content.ReadFromJsonAsync<PlaceContributionResponseDto>();
        Assert.NotNull(created);
        Assert.Contains(created.Contribution.Images, image => image.Url == mediaPath && image.IsMain);
    }

    [Fact]
    public async Task Create_update_place_rejects_nonexistent_target_place_early()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        using var client = await LoginAsync(app, "marko.p@example.com", "password123");
        var payload = CreateValidContributionPayload("Missing Target Update Villa");
        payload["type"] = "UpdatePlace";
        payload["placeId"] = 999_999_999;

        var response = await PostContributionAsync(client, payload);

        await AssertErrorAsync(
            response,
            HttpStatusCode.NotFound,
            "Target place not found.");
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

    private static async Task<HttpClient> RegisterAdminAndLoginAsync(GeotrackApiFactory app, string prefix)
    {
        var email = $"{prefix}-{Guid.NewGuid():N}@example.com";

        using var registrationClient = app.CreateClient();
        var registerResponse = await registrationClient.PostAsJsonAsync(
            "/api/auth/register",
            new RegisterRequestDto
            {
                FullName = "Contribution Admin",
                Email = email,
                Password = "Password123!",
            });
        registerResponse.EnsureSuccessStatusCode();

        await GrantRoleAsync(app, email, "Admin");

        return await LoginAsync(app, email, "Password123!");
    }

    private static async Task<HttpClient> RegisterRegionalManagerAndLoginAsync(
        GeotrackApiFactory app,
        string prefix,
        int regionId)
    {
        var email = $"{prefix}-{Guid.NewGuid():N}@example.com";

        using var registrationClient = app.CreateClient();
        var registerResponse = await registrationClient.PostAsJsonAsync(
            "/api/auth/register",
            new RegisterRequestDto
            {
                FullName = "Out Region Manager",
                Email = email,
                Password = "Password123!",
            });
        registerResponse.EnsureSuccessStatusCode();

        await GrantRoleAsync(app, email, "RegionalManager");

        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = await db.Users.SingleAsync(item => item.Email == email);

        db.RegionalManagerRegions.Add(new RegionalManagerRegion
        {
            UserId = user.Id,
            RegionId = regionId,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        return await LoginAsync(app, email, "Password123!");
    }

    private static async Task<int> GetRegionIdAsync(GeotrackApiFactory app, string regionName)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Regions
            .Where(region => region.Name == regionName)
            .Select(region => region.Id)
            .SingleAsync();
    }

    private static async Task<int> CreateUnassignedRegionAsync(GeotrackApiFactory app)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var territory = await db.Territories.OrderBy(item => item.Id).FirstAsync();
        var region = new Region
        {
            Name = $"Contribution Queue Out {Guid.NewGuid():N}",
            TerritoryId = territory.Id,
            CreatedAt = DateTime.UtcNow
        };

        db.Regions.Add(region);
        await db.SaveChangesAsync();
        return region.Id;
    }

    private static Dictionary<string, object?> CreateValidContributionPayload(string name)
    {
        return new Dictionary<string, object?>
        {
            ["name"] = name,
            ["stayType"] = "Villa",
            ["address"] = "123 Coast Road",
            ["city"] = "Dubrovnik",
            ["region"] = SeedRegionName,
            ["country"] = "Croatia",
            ["latitude"] = 42.6507,
            ["longitude"] = 18.0944,
            ["imagePosition"] = "50% 50%",
            ["imageSize"] = "cover",
            ["description"] = "A contribution used to validate input hardening.",
            ["phone"] = "+3851234567",
            ["contactEmail"] = "hello@validation.example",
            ["website"] = "https://validation.example",
            ["tags"] = new[] { "Coastal", "Private" },
            ["amenities"] = new[] { "wifi", "pool" },
            ["bookingPartners"] = Array.Empty<object>()
        };
    }

    private static Dictionary<string, object?> CreateGenericPlaceContributionPayload(string name)
    {
        return new Dictionary<string, object?>
        {
            ["name"] = name,
            ["placeType"] = "Cafe",
            ["address"] = "10 Market Lane",
            ["city"] = "Dubrovnik",
            ["region"] = SeedRegionName,
            ["country"] = "Croatia",
            ["latitude"] = 42.6413,
            ["longitude"] = 18.1089,
            ["imagePosition"] = "50% 50%",
            ["imageSize"] = "cover",
            ["description"] = "A generic place contribution used to validate the canonical route.",
            ["phone"] = "+385111222",
            ["contactEmail"] = "hello@canonical-cafe.example",
            ["website"] = "https://canonical-cafe.example",
            ["tags"] = new[] { "Coffee", "Local" },
            ["amenities"] = new[] { "wifi" },
            ["bookingPartners"] = Array.Empty<object>()
        };
    }

    private static Dictionary<string, object?> CreatePayloadForPlaceType(string name, string placeType)
    {
        if (string.Equals(placeType, "Stay", StringComparison.OrdinalIgnoreCase))
            return CreateValidContributionPayload(name);

        var payload = CreateGenericPlaceContributionPayload(name);
        payload["placeType"] = placeType;
        payload["stayType"] = null;
        return payload;
    }

    private static async Task<PlaceContributionResponseDto> CreatePlaceContributionAsync(HttpClient client)
    {
        var payload = new
        {
            name = "Beta Villa",
            stayType = "Villa",
            address = "123 Coast Road",
            city = "Dubrovnik",
            region = SeedRegionName,
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
        AddOwnershipProofDocument(formData);

        var response = await client.PostAsync("/api/contributions", formData);
        response.EnsureSuccessStatusCode();

        var created = await response.Content.ReadFromJsonAsync<PlaceContributionResponseDto>();
        Assert.NotNull(created);
        return created;
    }

    private static async Task<PlaceContributionResponseDto> CreateUpdatePlaceContributionAsync(
        HttpClient client,
        int targetPlaceId,
        int proposedLocationId,
        int placeTypeId,
        string name)
    {
        var payload = new
        {
            type = "UpdatePlace",
            placeId = targetPlaceId,
            placeTypeId,
            locationId = proposedLocationId,
            name,
            subtitle = "Updated by contribution",
            address = "Updated address",
            description = "A scoped update contribution used to validate moderation authorization.",
            latitude = 42.6507,
            longitude = 18.0944,
            contactEmail = "scoped-update@example.com",
            phone = "+3859876543",
            website = "https://scoped-update.example"
        };

        using var formData = new MultipartFormDataContent();
        formData.Add(
            new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json"),
            "payload");
        AddOwnershipProofDocument(formData);

        var response = await client.PostAsync("/api/contributions", formData);
        response.EnsureSuccessStatusCode();

        var created = await response.Content.ReadFromJsonAsync<PlaceContributionResponseDto>();
        Assert.NotNull(created);
        return created;
    }

    private static async Task<HttpResponseMessage> PostContributionAsync(
        HttpClient client,
        object payload,
        string endpoint = "/api/contributions",
        OwnershipProofUpload ownershipProof = OwnershipProofUpload.ValidPdf)
    {
        using var formData = new MultipartFormDataContent();
        formData.Add(
            new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json"),
            "payload");
        AddOwnershipProofDocument(formData, ownershipProof);

        return await client.PostAsync(endpoint, formData);
    }

    private static void AddOwnershipProofDocument(
        MultipartFormDataContent formData,
        OwnershipProofUpload ownershipProof = OwnershipProofUpload.ValidPdf)
    {
        if (ownershipProof == OwnershipProofUpload.Missing)
            return;

        ByteArrayContent ownershipProofContent;
        string fileName;
        string contentType;

        if (ownershipProof == OwnershipProofUpload.InvalidText)
        {
            ownershipProofContent = new ByteArrayContent(Encoding.UTF8.GetBytes("not a pdf"));
            fileName = "ownership-proof.txt";
            contentType = "text/plain";
        }
        else if (ownershipProof == OwnershipProofUpload.OversizedPdf)
        {
            var bytes = new byte[(10 * 1024 * 1024) + 1];
            "%PDF-"u8.CopyTo(bytes);
            ownershipProofContent = new ByteArrayContent(bytes);
            fileName = "ownership-proof.pdf";
            contentType = "application/pdf";
        }
        else
        {
            ownershipProofContent = new ByteArrayContent(Encoding.UTF8.GetBytes("%PDF-1.4\n% GeoTrack ownership proof test document\n"));
            fileName = "ownership-proof.pdf";
            contentType = "application/pdf";
        }

        ownershipProofContent.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        formData.Add(ownershipProofContent, "ownershipProofDocument", fileName);
    }

    public enum OwnershipProofUpload
    {
        ValidPdf,
        Missing,
        InvalidText,
        OversizedPdf
    }

    private static async Task AssertForbiddenAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        var error = await response.Content.ReadFromJsonAsync<Dictionary<string, string>>();
        Assert.NotNull(error);
        Assert.True(error.TryGetValue("message", out var message));
        Assert.Equal("Forbidden.", message);
    }

    private static async Task AssertErrorAsync(
        HttpResponseMessage response,
        HttpStatusCode expectedStatusCode,
        string expectedMessage)
    {
        Assert.Equal(expectedStatusCode, response.StatusCode);

        var error = await response.Content.ReadFromJsonAsync<Dictionary<string, string>>();
        Assert.NotNull(error);
        Assert.True(error.TryGetValue("message", out var message));
        Assert.Equal(expectedMessage, message);
    }

    private static async Task<ContributionScopeFixture> CreateContributionScopeFixtureAsync(GeotrackApiFactory app)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = DateTime.UtcNow;

        var inScopeRegion = await db.Regions.SingleAsync(region => region.Name == SeedRegionName);
        var territory = await db.Territories.SingleAsync(item => item.Id == inScopeRegion.TerritoryId);
        territory.BoundaryShapeType = "Polygon";
        territory.BoundarySource = "IntegrationTest";
        SetBoundary(db, territory, Polygon(
            (40.0, 13.0),
            (40.0, 22.0),
            (47.0, 22.0),
            (47.0, 13.0)));

        var inScopeLocation = await db.Locations
            .Where(location => location.Destination.RegionId == inScopeRegion.Id)
            .OrderBy(location => location.Id)
            .FirstAsync();
        var destinationType = await db.DestinationTypes.OrderBy(type => type.Id).FirstAsync();
        var locationType = await db.LocationTypes.OrderBy(type => type.Id).FirstAsync();
        var placeType = await db.PlaceTypes.SingleAsync(type => type.Name == "Cafe");

        var outOfScopeRegion = new Region
        {
            Name = $"Out Scope {Guid.NewGuid():N}",
            TerritoryId = inScopeRegion.TerritoryId,
            CreatedAt = now
        };

        db.Regions.Add(outOfScopeRegion);
        await db.SaveChangesAsync();

        var outOfScopeDestination = new Destination
        {
            Name = $"Out Scope Destination {Guid.NewGuid():N}",
            DestinationTypeId = destinationType.Id,
            RegionId = outOfScopeRegion.Id,
            Description = "Out-of-scope test destination.",
            Latitude = 44.8666,
            Longitude = 13.8496,
            CreatedAt = now
        };

        db.Destinations.Add(outOfScopeDestination);
        await db.SaveChangesAsync();

        var outOfScopeLocation = new Location
        {
            Name = $"Out Scope Location {Guid.NewGuid():N}",
            DestinationId = outOfScopeDestination.Id,
            LocationTypeId = locationType.Id,
            Latitude = 44.8666,
            Longitude = 13.8496,
            CreatedAt = now
        };

        db.Locations.Add(outOfScopeLocation);
        await db.SaveChangesAsync();

        const string inScopePlaceName = "In Scope Contribution Test Cafe";
        var inScopePlace = new Place
        {
            Name = inScopePlaceName,
            Subtitle = "Original in-region place",
            PlaceTypeId = placeType.Id,
            LocationId = inScopeLocation.Id,
            Description = "Original in-scope place.",
            Latitude = 42.6507,
            Longitude = 18.0944,
            CreatedAt = now
        };

        const string outOfScopePlaceName = "Out Scope Contribution Test Cafe";
        var outOfScopePlace = new Place
        {
            Name = outOfScopePlaceName,
            Subtitle = "Original out-of-region place",
            PlaceTypeId = placeType.Id,
            LocationId = outOfScopeLocation.Id,
            Description = "Original out-of-scope place.",
            Latitude = 44.8666,
            Longitude = 13.8496,
            CreatedAt = now
        };

        db.Places.AddRange(inScopePlace, outOfScopePlace);
        await db.SaveChangesAsync();

        return new ContributionScopeFixture(
            inScopeRegion.Id,
            outOfScopeRegion.Id,
            inScopeLocation.Id,
            outOfScopeLocation.Id,
            inScopePlace.Id,
            outOfScopePlace.Id,
            placeType.Id,
            inScopePlaceName,
            outOfScopePlaceName);
    }

    private static async Task AssertPlaceUnchangedAndContributionPendingAsync(
        GeotrackApiFactory app,
        int placeId,
        string expectedPlaceName,
        int expectedLocationId,
        int contributionId)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var place = await db.Places.SingleAsync(existingPlace => existingPlace.Id == placeId);
        Assert.Equal(expectedPlaceName, place.Name);
        Assert.Equal(expectedLocationId, place.LocationId);

        var contribution = await db.PlaceContributions.SingleAsync(existingContribution => existingContribution.Id == contributionId);
        Assert.Equal(PlaceContributionStatus.PendingReview, contribution.Status);
        Assert.Null(contribution.ReviewedAt);
        Assert.Null(contribution.ReviewedByUserId);
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

        var region = await db.Regions.SingleAsync(existingRegion => existingRegion.Name == regionName);
        var hasAssignment = await db.RegionalManagerRegions.AnyAsync(link =>
            link.UserId == reviewer.Id &&
            link.RegionId == region.Id);

        if (!hasAssignment)
        {
            db.RegionalManagerRegions.Add(new RegionalManagerRegion
            {
                UserId = reviewer.Id,
                RegionId = region.Id,
                Region = region,
                User = reviewer,
                IsActive = true,
                CreatedAt = DateTime.UtcNow,
            });
        }

        await db.SaveChangesAsync();
    }

    private static async Task GrantRoleAsync(
        GeotrackApiFactory app,
        string email,
        string roleName)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var user = await db.Users
            .Include(existingUser => existingUser.UserRoles)
            .SingleAsync(existingUser => existingUser.Email == email);

        var role = await db.Roles.SingleAsync(existingRole => existingRole.Name == roleName);
        if (user.UserRoles.Any(existingRole => existingRole.RoleId == role.Id))
            return;

        user.UserRoles.Add(new UserRole
        {
            UserId = user.Id,
            RoleId = role.Id,
            User = user,
            Role = role
        });

        await db.SaveChangesAsync();
    }

    private static void SetBoundary(AppDbContext db, Territory territory, Geometry geometry)
    {
        db.Entry(territory)
            .Property<Geometry?>(BoundaryGeometryPropertyName)
            .CurrentValue = geometry;
    }

    private static Polygon Polygon(params (double Latitude, double Longitude)[] points)
    {
        var coordinates = points
            .Select(point => new Coordinate(point.Longitude, point.Latitude))
            .ToList();

        if (!coordinates[0].Equals2D(coordinates[^1]))
        {
            coordinates.Add(new Coordinate(coordinates[0].X, coordinates[0].Y));
        }

        var polygon = GeometryFactory.CreatePolygon(coordinates.ToArray());
        polygon.SRID = 4326;
        return polygon;
    }

    private sealed record ContributionScopeFixture(
        int InScopeRegionId,
        int OutOfScopeRegionId,
        int InScopeLocationId,
        int OutOfScopeLocationId,
        int InScopePlaceId,
        int OutOfScopePlaceId,
        int PlaceTypeId,
        string InScopePlaceName,
        string OutOfScopePlaceName);
}

