using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Geotrack.Application.DTOs.Admin;
using Geotrack.Application.DTOs.Auth;
using Geotrack.Application.DTOs.Contributions;
using Geotrack.Domain.Entities.Core;
using Geotrack.Domain.Entities.Reviews;
using Geotrack.Domain.Entities.Users;
using Geotrack.Infrastructure;
using Geotrack.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Geotrack.IntegrationTests;

public sealed class RegionalRequestQueueTests
{
    [Fact]
    public async Task Queue_lists_only_active_region_items_and_applies_filters_search_sort_and_pagination()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        var fixture = await CreateQueueFixtureAsync(app);

        using var client = await LoginAsync(app, "elena@example.com", "password123");
        var queue = await GetQueueAsync(client, fixture.AssignedRegionId);

        Assert.Equal(2, queue.TotalCount);
        Assert.Equal(2, queue.PendingCount);
        Assert.Equal(1, queue.ApprovedCount);
        Assert.Equal(1, queue.RejectedCount);
        Assert.Equal(2, queue.CountsByType.Places);
        Assert.Equal(0, queue.CountsByType.Destinations);
        Assert.Equal(0, queue.CountsByType.Locations);
        Assert.Equal(0, queue.CountsByType.Events);
        Assert.All(queue.Items, item => Assert.Equal(fixture.AssignedRegionId, item.RegionId));
        Assert.DoesNotContain(queue.Items, item => item.RequestId == fixture.OutOfRegionContributionId);
        Assert.Contains(queue.Items, item => item.RequestKey == $"contribution:{fixture.PendingContributionId}");
        Assert.DoesNotContain(queue.Items, item => item.Source == "ReviewReport");
        Assert.DoesNotContain(queue.Items, item => item.EntityType == "Review");

        var sidebar = await client.GetFromJsonAsync<RegionalSidebarSummaryDto>(
            $"/api/admin/regional/sidebar-summary?regionId={fixture.AssignedRegionId}");
        Assert.NotNull(sidebar);
        Assert.Equal(2, sidebar!.PendingRequestsCount);

        var places = await GetQueueAsync(client, fixture.AssignedRegionId, type: "place");
        Assert.Equal(2, places.TotalCount);
        Assert.All(places.Items, item => Assert.Equal("Place", item.EntityType));

        var reviewTypeResponse = await client.GetAsync(
            $"/api/admin/regional/requests?regionId={fixture.AssignedRegionId}&type=review");
        Assert.Equal(HttpStatusCode.BadRequest, reviewTypeResponse.StatusCode);

        foreach (var unsupportedType in new[] { "destination", "location", "event" })
        {
            var emptyType = await GetQueueAsync(client, fixture.AssignedRegionId, type: unsupportedType);
            Assert.Equal(0, emptyType.TotalCount);
            Assert.Empty(emptyType.Items);
        }

        var approved = await GetQueueAsync(client, fixture.AssignedRegionId, status: "approved");
        Assert.Equal(1, approved.TotalCount);
        Assert.All(approved.Items, item => Assert.Equal("Approved", item.Status));

        var rejected = await GetQueueAsync(client, fixture.AssignedRegionId, status: "rejected");
        Assert.Equal(1, rejected.TotalCount);
        Assert.All(rejected.Items, item => Assert.Equal("Rejected", item.Status));

        var searchedByTitle = await GetQueueAsync(client, fixture.AssignedRegionId, search: "Queue Beta");
        Assert.Single(searchedByTitle.Items);
        Assert.Equal(fixture.SecondPendingContributionId, searchedByTitle.Items[0].RequestId);

        var searchedByDisplayId = await GetQueueAsync(
            client,
            fixture.AssignedRegionId,
            search: $"REP-{fixture.PendingReportToRejectId:D4}");
        Assert.Empty(searchedByDisplayId.Items);

        var oldest = await GetQueueAsync(client, fixture.AssignedRegionId, sort: "oldest");
        Assert.Equal("Queue Beta Place", oldest.Items[0].Title);

        var page = await GetQueueAsync(client, fixture.AssignedRegionId, skip: 1, take: 1);
        Assert.Equal(2, page.TotalCount);
        Assert.Equal(1, page.Skip);
        Assert.Equal(1, page.Take);
        Assert.Single(page.Items);
    }

    [Fact]
    public async Task Queue_detail_returns_contribution_preview_data_and_rejects_review_report_source()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        var fixture = await CreateQueueFixtureAsync(app);

        using var client = await LoginAsync(app, "elena@example.com", "password123");

        var contribution = await client.GetFromJsonAsync<RegionalRequestQueueDetailDto>(
            $"/api/admin/regional/requests/contribution/{fixture.PendingContributionId}?regionId={fixture.AssignedRegionId}");
        Assert.NotNull(contribution);
        Assert.Equal("Contribution", contribution!.Source);
        Assert.Equal("Queue Alpha Place", contribution.Title);
        Assert.Equal("Queue Assigned Destination", contribution.DestinationName);
        Assert.Equal("Queue Assigned Location", contribution.LocationName);
        Assert.Equal("Queue contribution body.", contribution.Description);
        Assert.True(contribution.HasOwnershipProofDocument);
        Assert.Equal("queue-proof.pdf", contribution.OwnershipProofOriginalFileName);
        Assert.Equal("application/pdf", contribution.OwnershipProofContentType);
        Assert.StartsWith("/media/uploads/contributions/", contribution.OwnershipProofDocumentUrl);

        var reportResponse = await client.GetAsync(
            $"/api/admin/regional/requests/review-report/{fixture.PendingReportToApproveId}?regionId={fixture.AssignedRegionId}");
        Assert.Equal(HttpStatusCode.BadRequest, reportResponse.StatusCode);
    }

    [Fact]
    public async Task Queue_endpoints_enforce_auth_role_region_and_request_scope()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        var fixture = await CreateQueueFixtureAsync(app);

        using var anonymous = app.CreateClient();
        var anonymousResponse = await anonymous.GetAsync($"/api/admin/regional/requests?regionId={fixture.AssignedRegionId}");
        Assert.Equal(HttpStatusCode.Unauthorized, anonymousResponse.StatusCode);

        using var regularUser = await LoginAsync(app, "marko.p@example.com", "password123");
        var nonRegionalResponse = await regularUser.GetAsync($"/api/admin/regional/requests?regionId={fixture.AssignedRegionId}");
        Assert.Equal(HttpStatusCode.Forbidden, nonRegionalResponse.StatusCode);

        using var manager = await LoginAsync(app, "elena@example.com", "password123");
        var unassignedRegionResponse = await manager.GetAsync($"/api/admin/regional/requests?regionId={fixture.UnassignedRegionId}");
        Assert.Equal(HttpStatusCode.Forbidden, unassignedRegionResponse.StatusCode);

        var outOfRegionAction = await manager.PostAsJsonAsync(
            $"/api/admin/regional/requests/contribution/{fixture.OutOfRegionContributionId}/approve",
            new RegionalRequestDecisionRequestDto { RegionId = fixture.AssignedRegionId });
        Assert.Equal(HttpStatusCode.NotFound, outOfRegionAction.StatusCode);

        var tamperedRegionAction = await manager.PostAsJsonAsync(
            $"/api/admin/regional/requests/contribution/{fixture.PendingContributionId}/approve",
            new RegionalRequestDecisionRequestDto { RegionId = fixture.UnassignedRegionId });
        Assert.Equal(HttpStatusCode.Forbidden, tamperedRegionAction.StatusCode);

        var alreadyProcessed = await manager.PostAsJsonAsync(
            $"/api/admin/regional/requests/contribution/{fixture.ApprovedContributionId}/approve",
            new RegionalRequestDecisionRequestDto { RegionId = fixture.AssignedRegionId });
        Assert.Equal(HttpStatusCode.Conflict, alreadyProcessed.StatusCode);

        var reviewReportDetail = await manager.GetAsync(
            $"/api/admin/regional/requests/review-report/{fixture.PendingReportToApproveId}?regionId={fixture.AssignedRegionId}");
        Assert.Equal(HttpStatusCode.BadRequest, reviewReportDetail.StatusCode);

        var reviewReportAction = await manager.PostAsJsonAsync(
            $"/api/admin/regional/requests/review-report/{fixture.PendingReportToApproveId}/approve",
            new RegionalRequestDecisionRequestDto { RegionId = fixture.AssignedRegionId });
        Assert.Equal(HttpStatusCode.BadRequest, reviewReportAction.StatusCode);

        var legacyReviewReports = await manager.GetAsync("/api/reviews/reports");
        Assert.Equal(HttpStatusCode.Forbidden, legacyReviewReports.StatusCode);

        var adminReviewManagement = await manager.GetAsync("/api/admin/reviews");
        Assert.Equal(HttpStatusCode.Forbidden, adminReviewManagement.StatusCode);
    }

    [Fact]
    public async Task Approve_and_reject_contribution_from_queue_publish_or_preserve_state_and_notify_submitter()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        var fixture = await CreateQueueFixtureAsync(app);

        using var manager = await LoginAsync(app, "elena@example.com", "password123");
        var approveResponse = await manager.PostAsJsonAsync(
            $"/api/admin/regional/requests/contribution/{fixture.PendingContributionId}/approve",
            new RegionalRequestDecisionRequestDto
            {
                RegionId = fixture.AssignedRegionId,
                Note = "Looks good."
            });
        approveResponse.EnsureSuccessStatusCode();
        var approved = await approveResponse.Content.ReadFromJsonAsync<RegionalRequestActionResultDto>();
        Assert.NotNull(approved);
        Assert.True(approved!.Success);
        Assert.Equal("Approved", approved.Status);

        var rejectResponse = await manager.PostAsJsonAsync(
            $"/api/admin/regional/requests/contribution/{fixture.SecondPendingContributionId}/reject",
            new RegionalRequestDecisionRequestDto
            {
                RegionId = fixture.AssignedRegionId,
                Note = "Internal moderation note."
            });
        rejectResponse.EnsureSuccessStatusCode();
        var rejected = await rejectResponse.Content.ReadFromJsonAsync<RegionalRequestActionResultDto>();
        Assert.NotNull(rejected);
        Assert.True(rejected!.Success);
        Assert.Equal("Rejected", rejected.Status);

        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var approvedContribution = await db.PlaceContributions.SingleAsync(item => item.Id == fixture.PendingContributionId);
        var rejectedContribution = await db.PlaceContributions.SingleAsync(item => item.Id == fixture.SecondPendingContributionId);

        Assert.Equal(PlaceContributionStatus.Approved, approvedContribution.Status);
        Assert.NotNull(approvedContribution.PlaceId);
        Assert.Equal(PlaceContributionStatus.Rejected, rejectedContribution.Status);
        Assert.Null(rejectedContribution.PlaceId);
        Assert.Equal("Internal moderation note.", rejectedContribution.ReviewNote);

        var approvalNotification = await db.UserNotifications.SingleAsync(notification =>
            notification.UserId == fixture.ContributorUserId &&
            notification.Type == "contribution_approved" &&
            notification.RelatedEntityId == fixture.PendingContributionId);
        Assert.Equal("Your contribution has been approved and published.", approvalNotification.Message);
        Assert.Equal($"/places/{approvedContribution.PlaceId!.Value}", approvalNotification.ActionUrl);

        var rejectionNotification = await db.UserNotifications.SingleAsync(notification =>
            notification.UserId == fixture.ContributorUserId &&
            notification.Type == "contribution_rejected" &&
            notification.RelatedEntityId == fixture.SecondPendingContributionId);
        Assert.Equal("Your contribution was reviewed but was not approved.", rejectionNotification.Message);
        Assert.DoesNotContain("Internal moderation note", rejectionNotification.Message, StringComparison.OrdinalIgnoreCase);

        using var contributorClient = await LoginAsync(app, "marko.p@example.com", "password123");
        var publicDetail = await contributorClient.GetFromJsonAsync<PlaceContributionResponseDto>(
            $"/api/contributions/{fixture.SecondPendingContributionId}");
        Assert.NotNull(publicDetail);
        Assert.Null(publicDetail!.Contribution.ReviewNote);
    }

    [Fact]
    public async Task Bulk_actions_return_per_item_results_and_validate_each_request()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        var fixture = await CreateQueueFixtureAsync(app);

        using var manager = await LoginAsync(app, "elena@example.com", "password123");
        var approveResponse = await manager.PostAsJsonAsync(
            "/api/admin/regional/requests/bulk-approve",
            new RegionalRequestBulkActionRequestDto
            {
                RegionId = fixture.AssignedRegionId,
                RequestKeys = new List<string>
                {
                    $"contribution:{fixture.PendingContributionId}",
                    $"contribution:{fixture.OutOfRegionContributionId}",
                    $"contribution:{fixture.ApprovedContributionId}",
                    "invalid:1"
                },
                Note = "Bulk approval."
            });
        approveResponse.EnsureSuccessStatusCode();

        var approve = await approveResponse.Content.ReadFromJsonAsync<RegionalRequestBulkActionResponseDto>();
        Assert.NotNull(approve);
        Assert.Equal(1, approve!.SucceededCount);
        Assert.Equal(3, approve.FailedCount);
        Assert.Contains(approve.Results, result =>
            result.RequestKey == $"contribution:{fixture.PendingContributionId}" &&
            result.Success &&
            result.Status == "Approved");
        Assert.Contains(approve.Results, result =>
            result.RequestKey == $"contribution:{fixture.OutOfRegionContributionId}" &&
            !result.Success &&
            result.Message == "Request not found.");
        Assert.Contains(approve.Results, result =>
            result.RequestKey == $"contribution:{fixture.ApprovedContributionId}" &&
            !result.Success);
        Assert.Contains(approve.Results, result =>
            result.RequestKey == "invalid:1" &&
            !result.Success &&
            result.Message == "Invalid request id.");

        var rejectResponse = await manager.PostAsJsonAsync(
            "/api/admin/regional/requests/bulk-reject",
            new RegionalRequestBulkActionRequestDto
            {
                RegionId = fixture.AssignedRegionId,
                RequestKeys = new List<string>
                {
                    $"contribution:{fixture.SecondPendingContributionId}",
                    $"review-report:{fixture.PendingReportToRejectId}"
                },
                Reason = "Bulk rejection."
            });
        rejectResponse.EnsureSuccessStatusCode();

        var reject = await rejectResponse.Content.ReadFromJsonAsync<RegionalRequestBulkActionResponseDto>();
        Assert.NotNull(reject);
        Assert.Equal(1, reject!.SucceededCount);
        Assert.Equal(1, reject.FailedCount);
        Assert.Contains(reject.Results, result =>
            result.RequestKey == $"contribution:{fixture.SecondPendingContributionId}" &&
            result.Success &&
            result.Status == "Rejected");
        Assert.Contains(reject.Results, result =>
            result.RequestKey == $"review-report:{fixture.PendingReportToRejectId}" &&
            !result.Success &&
            result.Message == "Invalid request id.");

        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(
            PlaceContributionStatus.Rejected,
            (await db.PlaceContributions.SingleAsync(item => item.Id == fixture.SecondPendingContributionId)).Status);
        Assert.Equal(
            ReviewReportStatus.Pending,
            (await db.ReviewReports.SingleAsync(item => item.Id == fixture.PendingReportToRejectId)).Status);
    }

    private static async Task<RegionalRequestQueueResponseDto> GetQueueAsync(
        HttpClient client,
        int regionId,
        string type = "all",
        string status = "pending",
        string sort = "newest",
        string? search = null,
        int skip = 0,
        int take = 10)
    {
        var url = $"/api/admin/regional/requests?regionId={regionId}&type={type}&status={status}&sort={sort}&skip={skip}&take={take}";
        if (!string.IsNullOrWhiteSpace(search))
        {
            url += "&search=" + Uri.EscapeDataString(search);
        }

        var queue = await client.GetFromJsonAsync<RegionalRequestQueueResponseDto>(url);
        Assert.NotNull(queue);
        return queue!;
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

    private static async Task<QueueFixture> CreateQueueFixtureAsync(GeotrackApiFactory app)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var now = DateTime.UtcNow;
        var territory = await db.Territories.OrderBy(item => item.Id).FirstAsync();
        var destinationType = await db.DestinationTypes.OrderBy(item => item.Id).FirstAsync();
        var locationType = await db.LocationTypes.OrderBy(item => item.Id).FirstAsync();
        var placeType = await db.PlaceTypes.SingleAsync(item => item.Name == "Cafe");
        var contributor = await db.Users.SingleAsync(item => item.Email == "marko.p@example.com");
        var manager = await db.Users.SingleAsync(item => item.Email == "elena@example.com");
        var reviewAuthorToRemove = CreateUser("queue-remove-author");
        var reviewAuthorToKeep = CreateUser("queue-keep-author");
        var outReviewAuthor = CreateUser("queue-out-author");
        var actionedReviewAuthor = CreateUser("queue-actioned-author");
        var dismissedReviewAuthor = CreateUser("queue-dismissed-author");
        db.Users.AddRange(
            reviewAuthorToRemove,
            reviewAuthorToKeep,
            outReviewAuthor,
            actionedReviewAuthor,
            dismissedReviewAuthor);
        await db.SaveChangesAsync();

        var assignedRegion = new Region
        {
            Name = $"Queue Assigned {Guid.NewGuid():N}",
            TerritoryId = territory.Id,
            CreatedAt = now
        };
        var unassignedRegion = new Region
        {
            Name = $"Queue Unassigned {Guid.NewGuid():N}",
            TerritoryId = territory.Id,
            CreatedAt = now
        };
        db.Regions.AddRange(assignedRegion, unassignedRegion);
        await db.SaveChangesAsync();

        await GrantRegionalManagerCoverageAsync(db, manager, assignedRegion.Id);

        var assignedDestination = new Destination
        {
            Name = "Queue Assigned Destination",
            DestinationTypeId = destinationType.Id,
            RegionId = assignedRegion.Id,
            Description = "Assigned destination for queue tests.",
            Latitude = 42.1,
            Longitude = 18.1,
            CreatedAt = now
        };
        var unassignedDestination = new Destination
        {
            Name = "Queue Unassigned Destination",
            DestinationTypeId = destinationType.Id,
            RegionId = unassignedRegion.Id,
            Description = "Unassigned destination for queue tests.",
            Latitude = 43.1,
            Longitude = 19.1,
            CreatedAt = now
        };
        db.Destinations.AddRange(assignedDestination, unassignedDestination);
        await db.SaveChangesAsync();

        var assignedLocation = new Location
        {
            Name = "Queue Assigned Location",
            DestinationId = assignedDestination.Id,
            LocationTypeId = locationType.Id,
            Latitude = 42.1,
            Longitude = 18.1,
            CreatedAt = now
        };
        var unassignedLocation = new Location
        {
            Name = "Queue Unassigned Location",
            DestinationId = unassignedDestination.Id,
            LocationTypeId = locationType.Id,
            Latitude = 43.1,
            Longitude = 19.1,
            CreatedAt = now
        };
        db.Locations.AddRange(assignedLocation, unassignedLocation);
        await db.SaveChangesAsync();

        var assignedPlace = new Place
        {
            Name = "Queue Assigned Place",
            PlaceTypeId = placeType.Id,
            LocationId = assignedLocation.Id,
            Description = "Assigned place for reported reviews.",
            Latitude = 42.1,
            Longitude = 18.1,
            CreatedAt = now
        };
        var unassignedPlace = new Place
        {
            Name = "Queue Unassigned Place",
            PlaceTypeId = placeType.Id,
            LocationId = unassignedLocation.Id,
            Description = "Unassigned place for reported reviews.",
            Latitude = 43.1,
            Longitude = 19.1,
            CreatedAt = now
        };
        db.Places.AddRange(assignedPlace, unassignedPlace);
        await db.SaveChangesAsync();

        var pendingContribution = CreateContribution(
            contributor.Id,
            placeType.Id,
            assignedLocation.Id,
            "Queue Alpha Place",
            PlaceContributionStatus.PendingReview,
            now.AddMinutes(-10));
        var secondPendingContribution = CreateContribution(
            contributor.Id,
            placeType.Id,
            assignedLocation.Id,
            "Queue Beta Place",
            PlaceContributionStatus.PendingReview,
            now.AddMinutes(-30));
        var approvedContribution = CreateContribution(
            contributor.Id,
            placeType.Id,
            assignedLocation.Id,
            "Queue Approved Place",
            PlaceContributionStatus.Approved,
            now.AddMinutes(-40),
            manager.Id,
            now.AddMinutes(-5));
        var rejectedContribution = CreateContribution(
            contributor.Id,
            placeType.Id,
            assignedLocation.Id,
            "Queue Rejected Place",
            PlaceContributionStatus.Rejected,
            now.AddMinutes(-50),
            manager.Id,
            now.AddMinutes(-4));
        var outContribution = CreateContribution(
            contributor.Id,
            placeType.Id,
            unassignedLocation.Id,
            "Queue Out Place",
            PlaceContributionStatus.PendingReview,
            now.AddMinutes(-1));
        db.PlaceContributions.AddRange(
            pendingContribution,
            secondPendingContribution,
            approvedContribution,
            rejectedContribution,
            outContribution);
        await db.SaveChangesAsync();

        var reviewToRemove = CreatePlaceReview(reviewAuthorToRemove.Id, assignedPlace.Id, "Report comment for accepted moderation.", now.AddMinutes(-8));
        var reviewToKeep = CreatePlaceReview(reviewAuthorToKeep.Id, assignedPlace.Id, "Report comment that should stay.", now.AddMinutes(-7));
        var outReview = CreatePlaceReview(outReviewAuthor.Id, unassignedPlace.Id, "Out-of-region reported review.", now.AddMinutes(-6));
        var actionedReview = CreatePlaceReview(actionedReviewAuthor.Id, assignedPlace.Id, "Already actioned review.", now.AddMinutes(-5));
        var dismissedReview = CreatePlaceReview(dismissedReviewAuthor.Id, assignedPlace.Id, "Already dismissed review.", now.AddMinutes(-4));
        db.PlaceReviews.AddRange(reviewToRemove, reviewToKeep, outReview, actionedReview, dismissedReview);
        await db.SaveChangesAsync();

        var reportToApprove = CreateReport(
            reviewToRemove,
            contributor.Id,
            reviewAuthorToRemove,
            assignedPlace,
            assignedRegion,
            "Spam",
            ReviewReportStatus.Pending,
            now.AddMinutes(-3));
        var reportToReject = CreateReport(
            reviewToKeep,
            contributor.Id,
            reviewAuthorToKeep,
            assignedPlace,
            assignedRegion,
            "Harassment",
            ReviewReportStatus.Pending,
            now.AddMinutes(-2));
        var outReport = CreateReport(
            outReview,
            contributor.Id,
            outReviewAuthor,
            unassignedPlace,
            unassignedRegion,
            "Spam",
            ReviewReportStatus.Pending,
            now.AddMinutes(-1));
        var actionedReport = CreateReport(
            actionedReview,
            contributor.Id,
            actionedReviewAuthor,
            assignedPlace,
            assignedRegion,
            "Spam",
            ReviewReportStatus.Actioned,
            now.AddMinutes(-20),
            manager.Id,
            now.AddMinutes(-19),
            ReviewReportDecision.RemoveReview);
        var dismissedReport = CreateReport(
            dismissedReview,
            contributor.Id,
            dismissedReviewAuthor,
            assignedPlace,
            assignedRegion,
            "Other",
            ReviewReportStatus.Dismissed,
            now.AddMinutes(-21),
            manager.Id,
            now.AddMinutes(-18),
            ReviewReportDecision.KeepReview);
        db.ReviewReports.AddRange(reportToApprove, reportToReject, outReport, actionedReport, dismissedReport);
        await db.SaveChangesAsync();

        return new QueueFixture(
            assignedRegion.Id,
            unassignedRegion.Id,
            contributor.Id,
            reviewAuthorToRemove.Id,
            pendingContribution.Id,
            secondPendingContribution.Id,
            approvedContribution.Id,
            rejectedContribution.Id,
            outContribution.Id,
            reportToApprove.Id,
            reportToReject.Id,
            outReport.Id,
            reviewToRemove.Id,
            reviewToKeep.Id);
    }

    private static User CreateUser(string prefix)
    {
        var suffix = Guid.NewGuid().ToString("N");
        return new User
        {
            Username = $"{prefix}-{suffix}"[..Math.Min(prefix.Length + 1 + suffix.Length, 100)],
            Email = $"{prefix}-{suffix}@example.com",
            FirstName = "Queue",
            LastName = "Reviewer",
            DisplayName = $"{prefix}-{suffix}"[..Math.Min(prefix.Length + 1 + suffix.Length, 100)],
            PasswordHash = "test-only",
            CreatedAt = DateTime.UtcNow
        };
    }

    private static PlaceContribution CreateContribution(
        int userId,
        int placeTypeId,
        int locationId,
        string name,
        PlaceContributionStatus status,
        DateTime submittedAt,
        int? reviewedByUserId = null,
        DateTime? reviewedAt = null) =>
        new()
        {
            Type = PlaceContributionType.CreatePlace,
            Status = status,
            UserId = userId,
            ReviewedByUserId = reviewedByUserId,
            ReviewedAt = reviewedAt,
            ReviewNote = reviewedAt.HasValue ? "Already reviewed." : null,
            PlaceTypeId = placeTypeId,
            LocationId = locationId,
            Name = name,
            Address = "Queue test address",
            Description = "Queue contribution body.",
            Latitude = 42.1,
            Longitude = 18.1,
            TagsJson = "[]",
            AmenitiesJson = "[]",
            BookingPartnersJson = "[]",
            OwnershipProofDocumentUrl = "/media/uploads/contributions/queue-proof.pdf",
            OwnershipProofOriginalFileName = "queue-proof.pdf",
            OwnershipProofContentType = "application/pdf",
            OwnershipProofUploadedAt = submittedAt,
            SubmittedAt = submittedAt,
            CreatedAt = submittedAt
        };

    private static PlaceReview CreatePlaceReview(
        int authorUserId,
        int placeId,
        string comment,
        DateTime createdAt) =>
        new()
        {
            UserId = authorUserId,
            PlaceId = placeId,
            Rating = 2,
            Comment = comment,
            CreatedAt = createdAt
        };

    private static ReviewReport CreateReport(
        PlaceReview review,
        int reporterUserId,
        User reviewAuthor,
        Place place,
        Region region,
        string reason,
        ReviewReportStatus status,
        DateTime createdAt,
        int? reviewedByUserId = null,
        DateTime? reviewedAt = null,
        ReviewReportDecision? decision = null) =>
        new()
        {
            TargetType = ReviewReportTargetType.Place,
            ReviewId = review.Id,
            PlaceReviewId = review.Id,
            ReporterUserId = reporterUserId,
            Reason = Enum.Parse<ReviewReportReason>(reason),
            Note = reason + " report note.",
            Status = status,
            ReviewedByUserId = reviewedByUserId,
            ReviewedAt = reviewedAt,
            Decision = decision,
            DecisionNote = reviewedAt.HasValue ? "Already reviewed." : string.Empty,
            ReviewAuthorUserId = reviewAuthor.Id,
            ReviewAuthorName = $"{reviewAuthor.FirstName} {reviewAuthor.LastName}".Trim(),
            EntityId = place.Id,
            EntityName = place.Name,
            RegionId = region.Id,
            RegionName = region.Name,
            ReviewRating = review.Rating,
            ReviewCommentSnapshot = review.Comment,
            ReviewCreatedAt = review.CreatedAt,
            CreatedAt = createdAt
        };

    private static async Task GrantRegionalManagerCoverageAsync(AppDbContext db, User reviewer, int regionId)
    {
        var role = await db.Roles.SingleAsync(item => item.Name == "RegionalManager");

        if (!await db.UserRoles.AnyAsync(item => item.UserId == reviewer.Id && item.RoleId == role.Id))
        {
            db.UserRoles.Add(new UserRole
            {
                UserId = reviewer.Id,
                RoleId = role.Id
            });
        }

        if (!await db.RegionalManagerRegions.AnyAsync(item => item.UserId == reviewer.Id && item.RegionId == regionId))
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

    private sealed record QueueFixture(
        int AssignedRegionId,
        int UnassignedRegionId,
        int ContributorUserId,
        int ReviewAuthorUserId,
        int PendingContributionId,
        int SecondPendingContributionId,
        int ApprovedContributionId,
        int RejectedContributionId,
        int OutOfRegionContributionId,
        int PendingReportToApproveId,
        int PendingReportToRejectId,
        int OutOfRegionReportId,
        int ReviewToRemoveId,
        int ReviewToKeepId);
}
