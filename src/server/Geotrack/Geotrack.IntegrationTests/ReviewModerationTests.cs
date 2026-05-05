using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Geotrack.Application.DTOs.Auth;
using Geotrack.Application.DTOs.Moderation;
using Geotrack.Application.DTOs.Places;
using Geotrack.Application.DTOs.Reviews;
using Geotrack.Domain.Entities.Reviews;
using Geotrack.Domain.Entities.Users;
using Geotrack.Infrastructure;
using Geotrack.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Geotrack.IntegrationTests;

public sealed class ReviewModerationTests
{
    [Fact]
    public async Task Review_creation_rejects_english_and_serbian_offensive_text()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        var placeId = await GetFirstPlaceIdAsync(app);
        using var client = await RegisterAndLoginAsync(app, "moderation-author");

        var englishResponse = await client.PostAsJsonAsync(
            $"/api/places/{placeId}/reviews",
            new CreatePlaceReviewRequestDto
            {
                Rating = 2,
                Comment = "This place was sh1t."
            });

        Assert.Equal(HttpStatusCode.BadRequest, englishResponse.StatusCode);
        var englishError = await englishResponse.Content.ReadFromJsonAsync<Dictionary<string, string>>();
        Assert.NotNull(englishError);
        Assert.Contains("content rules", englishError["message"], StringComparison.OrdinalIgnoreCase);

        var serbianResponse = await client.PostAsJsonAsync(
            $"/api/places/{placeId}/reviews",
            new CreatePlaceReviewRequestDto
            {
                Rating = 2,
                Comment = "Ovo je j e b e n o lose iskustvo."
            });

        Assert.Equal(HttpStatusCode.BadRequest, serbianResponse.StatusCode);
        var serbianError = await serbianResponse.Content.ReadFromJsonAsync<Dictionary<string, string>>();
        Assert.NotNull(serbianError);
        Assert.Contains("content rules", serbianError["message"], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Admin_managed_database_terms_drive_review_validation_and_cache_invalidation()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        var placeId = await GetFirstPlaceIdAsync(app);
        using var adminClient = await RegisterAdminAndLoginAsync(app, "moderation-admin");
        var term = $"blockedterm{Guid.NewGuid():N}"[..28];

        var createTermResponse = await adminClient.PostAsJsonAsync(
            "/api/moderation/terms",
            new CreateModerationTermRequestDto
            {
                Term = term,
                LanguageCode = "en",
                Category = "Profanity",
                MatchType = "Exact",
                Severity = 2,
                IsActive = true
            });
        createTermResponse.EnsureSuccessStatusCode();

        var moderationTerm = await createTermResponse.Content.ReadFromJsonAsync<ModerationTermDto>();
        Assert.NotNull(moderationTerm);
        Assert.Equal(term, moderationTerm.Term);

        using var firstAuthor = await RegisterAndLoginAsync(app, "db-term-blocked-author");
        var blockedReviewResponse = await firstAuthor.PostAsJsonAsync(
            $"/api/places/{placeId}/reviews",
            new CreatePlaceReviewRequestDto
            {
                Rating = 4,
                Comment = $"This review includes {term}."
            });

        Assert.Equal(HttpStatusCode.BadRequest, blockedReviewResponse.StatusCode);
        var blockedError = await blockedReviewResponse.Content.ReadFromJsonAsync<Dictionary<string, string>>();
        Assert.NotNull(blockedError);
        Assert.Contains("content rules", blockedError["message"], StringComparison.OrdinalIgnoreCase);

        var deactivateResponse = await adminClient.PostAsync(
            $"/api/moderation/terms/{moderationTerm.Id}/deactivate",
            content: null);
        deactivateResponse.EnsureSuccessStatusCode();

        using var secondAuthor = await RegisterAndLoginAsync(app, "db-term-allowed-author");
        var allowedReviewResponse = await secondAuthor.PostAsJsonAsync(
            $"/api/places/{placeId}/reviews",
            new CreatePlaceReviewRequestDto
            {
                Rating = 4,
                Comment = $"This review includes {term} after deactivation."
            });

        allowedReviewResponse.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Reporting_a_review_persists_signal_without_hiding_and_blocks_duplicates()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        var placeId = await GetFirstPlaceIdAsync(app);

        using var authorClient = await RegisterAndLoginAsync(app, "review-author");
        var createdReview = await CreatePlaceReviewAsync(authorClient, placeId, "Great shade near the entrance.");

        using var reporterClient = await RegisterAndLoginAsync(app, "review-reporter");
        var reportResponse = await reporterClient.PostAsJsonAsync(
            $"/api/reviews/reports/place/{createdReview.Id}",
            new ReportReviewRequestDto { Reason = "offensive_content" });
        reportResponse.EnsureSuccessStatusCode();

        var report = await reportResponse.Content.ReadFromJsonAsync<ReportReviewResponseDto>();
        Assert.NotNull(report);
        Assert.False(report.AlreadyReported);
        Assert.Equal("Pending", report.Status);

        var reviewsAfterReport = await reporterClient.GetFromJsonAsync<PagedReviewsResponseDto>($"/api/reviews/places/{placeId}");
        Assert.NotNull(reviewsAfterReport);
        var visibleReview = Assert.Single(reviewsAfterReport.Items, review => review.Id == createdReview.Id);
        Assert.True(visibleReview.ReportedByCurrentUser);

        var duplicateResponse = await reporterClient.PostAsJsonAsync(
            $"/api/reviews/reports/place/{createdReview.Id}",
            new ReportReviewRequestDto { Reason = "spam" });
        duplicateResponse.EnsureSuccessStatusCode();

        var duplicate = await duplicateResponse.Content.ReadFromJsonAsync<ReportReviewResponseDto>();
        Assert.NotNull(duplicate);
        Assert.True(duplicate.AlreadyReported);

        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var savedReports = await db.ReviewReports
            .Where(savedReport =>
                savedReport.TargetType == ReviewReportTargetType.Place &&
                savedReport.ReviewId == createdReview.Id)
            .ToListAsync();

        var savedReport = Assert.Single(savedReports);
        Assert.Equal(ReviewReportStatus.Pending, savedReport.Status);
    }

    [Fact]
    public async Task Regional_manager_can_query_keep_and_remove_reported_reviews_in_scope()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        var placeId = await GetFirstPlaceIdAsync(app);
        await GrantRegionalManagerCoverageAsync(app, "elena@example.com", "Dalmatia");

        using var firstAuthor = await RegisterAndLoginAsync(app, "keep-author");
        var reviewToKeep = await CreatePlaceReviewAsync(firstAuthor, placeId, "Useful stop with clear signage.");

        using var firstReporter = await RegisterAndLoginAsync(app, "keep-reporter");
        var keepReport = await ReportReviewAsync(firstReporter, "place", reviewToKeep.Id);

        using var managerClient = await LoginAsync(app, "elena@example.com", "password123");
        var queue = await managerClient.GetFromJsonAsync<GetReviewReportsResponseDto>("/api/reviews/reports");
        Assert.NotNull(queue);
        Assert.Contains(queue.Reports, report => report.Id == keepReport.ReportId);

        var dismissResponse = await managerClient.PostAsJsonAsync(
            $"/api/reviews/reports/{keepReport.ReportId}/dismiss",
            new ModerateReviewReportRequestDto { Note = "Review stays visible." });
        dismissResponse.EnsureSuccessStatusCode();

        var dismissed = await dismissResponse.Content.ReadFromJsonAsync<ReviewReportItemDto>();
        Assert.NotNull(dismissed);
        Assert.Equal("Dismissed", dismissed.Status);
        Assert.Equal("KeepReview", dismissed.Decision);

        using var secondAuthor = await RegisterAndLoginAsync(app, "remove-author");
        var reviewToRemove = await CreatePlaceReviewAsync(secondAuthor, placeId, "Legitimate review to remove by moderation.");

        using var secondReporter = await RegisterAndLoginAsync(app, "remove-reporter");
        var removeReport = await ReportReviewAsync(secondReporter, "place", reviewToRemove.Id);

        var removeResponse = await managerClient.PostAsJsonAsync(
            $"/api/reviews/reports/{removeReport.ReportId}/remove-review",
            new ModerateReviewReportRequestDto { Note = "Regional manager removal." });
        removeResponse.EnsureSuccessStatusCode();

        var reviewsAfterRemoval = await managerClient.GetFromJsonAsync<PagedReviewsResponseDto>($"/api/reviews/places/{placeId}");
        Assert.NotNull(reviewsAfterRemoval);
        Assert.DoesNotContain(reviewsAfterRemoval.Items, review => review.Id == reviewToRemove.Id);

        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var actionedReport = await db.ReviewReports.SingleAsync(report => report.Id == removeReport.ReportId);
        Assert.Equal(ReviewReportStatus.Actioned, actionedReport.Status);
        Assert.Equal(ReviewReportDecision.RemoveReview, actionedReport.Decision);
    }

    private static async Task<CreatePlaceReviewResponseDto> CreatePlaceReviewAsync(
        HttpClient client,
        int placeId,
        string comment)
    {
        var response = await client.PostAsJsonAsync(
            $"/api/places/{placeId}/reviews",
            new CreatePlaceReviewRequestDto
            {
                Rating = 4,
                Comment = comment
            });
        response.EnsureSuccessStatusCode();

        var created = await response.Content.ReadFromJsonAsync<CreatePlaceReviewResponseDto>();
        Assert.NotNull(created);
        return created;
    }

    private static async Task<ReportReviewResponseDto> ReportReviewAsync(
        HttpClient client,
        string targetType,
        int reviewId)
    {
        var response = await client.PostAsJsonAsync(
            $"/api/reviews/reports/{targetType}/{reviewId}",
            new ReportReviewRequestDto { Reason = "offensive_content" });
        response.EnsureSuccessStatusCode();

        var report = await response.Content.ReadFromJsonAsync<ReportReviewResponseDto>();
        Assert.NotNull(report);
        return report;
    }

    private static async Task<int> GetFirstPlaceIdAsync(GeotrackApiFactory app)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Places
            .OrderBy(place => place.Id)
            .Select(place => place.Id)
            .FirstAsync();
    }

    private static async Task<HttpClient> RegisterAndLoginAsync(GeotrackApiFactory app, string prefix)
    {
        var client = app.CreateClient();
        var email = $"{prefix}-{Guid.NewGuid():N}@example.com";

        var registerResponse = await client.PostAsJsonAsync(
            "/api/auth/register",
            new RegisterRequestDto
            {
                FullName = "Review Tester",
                Email = email,
                Password = "Password123!",
            });
        registerResponse.EnsureSuccessStatusCode();

        var session = await registerResponse.Content.ReadFromJsonAsync<LoginResponseDto>();
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
                FullName = "Moderation Admin",
                Email = email,
                Password = "Password123!",
            });
        registerResponse.EnsureSuccessStatusCode();

        await GrantRoleAsync(app, email, "Admin");

        return await LoginAsync(app, email, "Password123!");
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
}
