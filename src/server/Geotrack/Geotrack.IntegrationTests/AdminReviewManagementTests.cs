using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Geotrack.Application.DTOs.Admin;
using Geotrack.Application.DTOs.Auth;
using Geotrack.Domain.Entities.Reviews;
using Geotrack.Domain.Entities.Users;
using Geotrack.Infrastructure;
using Geotrack.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Geotrack.IntegrationTests;

public sealed class AdminReviewManagementTests
{
    private const string UserPassword = "Password123!";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task Review_management_requires_admin_and_lists_real_paginated_reviews()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        var prefix = $"admin-review-page-{Guid.NewGuid():N}";
        await AddActivePlaceReviewsAsync(app, prefix, 26);

        using var anonymous = app.CreateClient();
        var anonymousResponse = await anonymous.GetAsync("/api/admin/reviews");
        Assert.Equal(HttpStatusCode.Unauthorized, anonymousResponse.StatusCode);

        using var normalUser = await RegisterAndLoginAsync(app, $"normal-{Guid.NewGuid():N}@example.com", "Normal User");
        var nonAdminResponse = await normalUser.Client.GetAsync("/api/admin/reviews");
        Assert.Equal(HttpStatusCode.Forbidden, nonAdminResponse.StatusCode);

        using var admin = await LoginAdminAsync(app);
        var response = await admin.Client.GetAsync(
            $"/api/admin/reviews?status=active&type=place&search={Uri.EscapeDataString(prefix)}&sort=newest&skip=0&take=25");
        response.EnsureSuccessStatusCode();

        var content = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("passwordHash", content, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("securityStamp", content, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("refreshToken", content, StringComparison.OrdinalIgnoreCase);

        var page = JsonSerializer.Deserialize<AdminReviewListResponseDto>(content, JsonOptions);
        Assert.NotNull(page);
        Assert.Equal(26, page!.TotalCount);
        Assert.Equal(25, page.Items.Count);
        Assert.True(page.HasMore);
        Assert.Equal(25, page.NextSkip);
        Assert.All(page.Items, item =>
        {
            Assert.Equal("Active", item.Status);
            Assert.Equal("Place", item.ReviewedEntity.EntityType);
            Assert.Contains(prefix, item.Content, StringComparison.OrdinalIgnoreCase);
            Assert.True(item.Permissions.CanDeleteReview);
            Assert.True(item.Author.UserId > 0);
            Assert.False(string.IsNullOrWhiteSpace(item.ReviewKey));
        });
    }

    [Fact]
    public async Task Reported_tab_search_type_filter_and_detail_return_real_report_data()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        var fixture = await AddReportedPlaceReviewAsync(app, reportCount: 2);

        using var admin = await LoginAdminAsync(app);
        var page = await admin.Client.GetFromJsonAsync<AdminReviewListResponseDto>(
            $"/api/admin/reviews?status=reported&type=place&search={Uri.EscapeDataString(fixture.AuthorEmail)}&skip=0&take=25",
            JsonOptions);

        Assert.NotNull(page);
        var item = Assert.Single(page!.Items, review => review.ReviewKey == fixture.ReviewKey);
        Assert.Equal("Reported", item.Status);
        Assert.Equal(2, item.Reports.ReportCount);
        Assert.Equal(2, item.Reports.PendingReportCount);
        Assert.Equal("Place", item.ReviewedEntity.EntityType);
        Assert.NotNull(item.Reports.LatestReportReason);

        var detail = await admin.Client.GetFromJsonAsync<AdminReviewDetailDto>(
            $"/api/admin/reviews/{Uri.EscapeDataString(fixture.ReviewKey)}",
            JsonOptions);

        Assert.NotNull(detail);
        Assert.Equal(fixture.ReviewKey, detail!.ReviewKey);
        Assert.Equal(fixture.AuthorEmail, detail.Author.Email);
        Assert.Equal(2, detail.AllReports.Count);
        Assert.Equal(2, detail.ReportSummary.TotalReports);
        Assert.Equal(2, detail.ReportSummary.PendingReports);
        Assert.All(detail.AllReports, report =>
        {
            Assert.True(report.ReportId > 0);
            Assert.NotEmpty(report.Reporter.DisplayName);
            Assert.Equal("Pending", report.Status);
        });
    }

    [Fact]
    public async Task Delete_review_removes_review_resolves_pending_reports_and_keeps_dashboard_count_consistent()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        var fixture = await AddReportedPlaceReviewAsync(app, reportCount: 2);

        using var admin = await LoginAdminAsync(app);
        var deleteResponse = await admin.Client.PostAsJsonAsync(
            $"/api/admin/reviews/{Uri.EscapeDataString(fixture.ReviewKey)}/delete",
            new AdminReviewModerationRequestDto { Note = "Integration test removal." });
        deleteResponse.EnsureSuccessStatusCode();

        var result = await deleteResponse.Content.ReadFromJsonAsync<AdminReviewActionResultDto>(JsonOptions);
        Assert.NotNull(result);
        Assert.True(result!.Success);

        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.False(await db.PlaceReviews.AnyAsync(review => review.Id == fixture.ReviewId));

            var reports = await db.ReviewReports
                .Where(report => fixture.ReportIds.Contains(report.Id))
                .ToListAsync();
            Assert.All(reports, report =>
            {
                Assert.Equal(ReviewReportStatus.Actioned, report.Status);
                Assert.Equal(ReviewReportDecision.RemoveReview, report.Decision);
                Assert.Equal(admin.UserId, report.ReviewedByUserId);
                Assert.NotNull(report.ReviewedAt);
            });
        }

        var dashboard = await admin.Client.GetFromJsonAsync<AdminDashboardDto>("/api/admin/dashboard", JsonOptions);
        var reported = await admin.Client.GetFromJsonAsync<AdminReviewListResponseDto>(
            "/api/admin/reviews?status=reported&skip=0&take=25",
            JsonOptions);

        Assert.NotNull(dashboard);
        Assert.NotNull(reported);
        Assert.Equal(dashboard!.Badges.PendingReportsCount, reported!.Counts.ReportedCount);
    }

    [Fact]
    public async Task Combined_actions_remove_review_and_safely_mutate_author_account()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        var suspendFixture = await AddReportedPlaceReviewAsync(app, reportCount: 1);
        var deleteFixture = await AddReportedPlaceReviewAsync(app, reportCount: 1);

        using var admin = await LoginAdminAsync(app);

        var suspendResponse = await admin.Client.PostAsJsonAsync(
            $"/api/admin/reviews/{Uri.EscapeDataString(suspendFixture.ReviewKey)}/remove-and-suspend-author",
            new AdminReviewModerationRequestDto { Note = "Suspend author." });
        suspendResponse.EnsureSuccessStatusCode();

        var deleteResponse = await admin.Client.PostAsJsonAsync(
            $"/api/admin/reviews/{Uri.EscapeDataString(deleteFixture.ReviewKey)}/remove-and-delete-author",
            new AdminReviewModerationRequestDto { Note = "Delete author." });
        deleteResponse.EnsureSuccessStatusCode();

        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var suspendedAuthor = await db.Users.SingleAsync(user => user.Id == suspendFixture.AuthorUserId);
        Assert.True(suspendedAuthor.IsSuspended);
        Assert.False(suspendedAuthor.IsDeleted);
        Assert.Equal(admin.UserId, suspendedAuthor.SuspendedByUserId);
        Assert.False(await db.PlaceReviews.AnyAsync(review => review.Id == suspendFixture.ReviewId));

        var deletedAuthor = await db.Users.SingleAsync(user => user.Id == deleteFixture.AuthorUserId);
        Assert.True(deletedAuthor.IsDeleted);
        Assert.False(deletedAuthor.IsSuspended);
        Assert.Equal(admin.UserId, deletedAuthor.DeletedByUserId);
        Assert.False(await db.PlaceReviews.AnyAsync(review => review.Id == deleteFixture.ReviewId));
    }

    [Fact]
    public async Task Current_admin_cannot_remove_review_and_suspend_self()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        using var admin = await LoginAdminAsync(app);
        var reviewKey = await AddDestinationReviewForUserAsync(app, admin.UserId);

        var response = await admin.Client.PostAsJsonAsync(
            $"/api/admin/reviews/{Uri.EscapeDataString(reviewKey)}/remove-and-suspend-author",
            new AdminReviewModerationRequestDto { Note = "Should be blocked." });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var reviewId = int.Parse(reviewKey.Split(':')[1]);
        Assert.True(await db.DestinationReviews.AnyAsync(review => review.Id == reviewId));
    }

    private static async Task AddActivePlaceReviewsAsync(
        GeotrackApiFactory app,
        string prefix,
        int count)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var placeId = await db.Places.OrderBy(place => place.Id).Select(place => place.Id).FirstAsync();
        var now = DateTime.UtcNow;

        for (var index = 0; index < count; index++)
        {
            var user = BuildUser($"{prefix}-{index}@example.com", $"Review Author {index}", now.AddMinutes(index));
            db.Users.Add(user);
            await db.SaveChangesAsync();

            db.PlaceReviews.Add(new PlaceReview
            {
                PlaceId = placeId,
                UserId = user.Id,
                Rating = 4,
                Comment = $"{prefix} active review {index}",
                CreatedAt = now.AddMinutes(index)
            });
        }

        await db.SaveChangesAsync();
    }

    private static async Task<ReportedReviewFixture> AddReportedPlaceReviewAsync(
        GeotrackApiFactory app,
        int reportCount)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = DateTime.UtcNow;
        var token = $"admin-review-{Guid.NewGuid():N}";
        var place = await db.Places
            .Include(existing => existing.Location)
                .ThenInclude(location => location.Destination)
                    .ThenInclude(destination => destination.Region)
            .OrderBy(existing => existing.Id)
            .FirstAsync();

        var author = BuildUser($"{token}-author@example.com", "Reported Review Author", now);
        db.Users.Add(author);

        var reporters = Enumerable.Range(0, reportCount)
            .Select(index => BuildUser($"{token}-reporter-{index}@example.com", $"Reporter {index}", now.AddMinutes(index + 1)))
            .ToList();
        db.Users.AddRange(reporters);
        await db.SaveChangesAsync();

        var review = new PlaceReview
        {
            PlaceId = place.Id,
            UserId = author.Id,
            Rating = 2,
            Comment = $"{token} reported content fixture",
            CreatedAt = now.AddMinutes(10)
        };
        db.PlaceReviews.Add(review);
        await db.SaveChangesAsync();

        var reportIds = new List<int>();
        for (var index = 0; index < reporters.Count; index++)
        {
            var report = new ReviewReport
            {
                TargetType = ReviewReportTargetType.Place,
                ReviewId = review.Id,
                PlaceReviewId = review.Id,
                ReporterUserId = reporters[index].Id,
                Reason = index % 2 == 0 ? ReviewReportReason.OffensiveContent : ReviewReportReason.Spam,
                Note = $"Report note {index}",
                Status = ReviewReportStatus.Pending,
                ReviewAuthorUserId = author.Id,
                ReviewAuthorName = author.DisplayName ?? $"{author.FirstName} {author.LastName}".Trim(),
                EntityId = place.Id,
                EntityName = place.Name,
                RegionId = place.Location.Destination.RegionId,
                RegionName = place.Location.Destination.Region.Name,
                ReviewRating = review.Rating,
                ReviewCommentSnapshot = review.Comment,
                ReviewCreatedAt = review.CreatedAt,
                CreatedAt = now.AddMinutes(20 + index)
            };
            db.ReviewReports.Add(report);
            await db.SaveChangesAsync();
            reportIds.Add(report.Id);
        }

        return new ReportedReviewFixture(
            $"Place:{review.Id}",
            review.Id,
            author.Id,
            author.Email,
            reportIds);
    }

    private static async Task<string> AddDestinationReviewForUserAsync(
        GeotrackApiFactory app,
        int userId)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var destinationId = await db.Destinations
            .OrderBy(destination => destination.Id)
            .Select(destination => destination.Id)
            .FirstAsync();
        var review = new DestinationReview
        {
            DestinationId = destinationId,
            UserId = userId,
            Rating = 3,
            Comment = $"Admin self review {Guid.NewGuid():N}",
            CreatedAt = DateTime.UtcNow
        };

        db.DestinationReviews.Add(review);
        await db.SaveChangesAsync();

        return $"Destination:{review.Id}";
    }

    private static User BuildUser(string email, string fullName, DateTime createdAt)
    {
        var parts = fullName.Split(' ', 2, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        return new User
        {
            Username = email,
            Email = email,
            DisplayName = fullName,
            FirstName = parts.ElementAtOrDefault(0) ?? fullName,
            LastName = parts.ElementAtOrDefault(1) ?? string.Empty,
            PasswordHash = "integration-test-only",
            CreatedAt = createdAt,
            UpdatedAt = createdAt
        };
    }

    private static async Task<AuthenticatedClient> LoginAdminAsync(GeotrackApiFactory app)
    {
        var client = app.CreateClient();
        var response = await client.PostAsJsonAsync(
            "/api/admin/auth/login",
            new LoginRequestDto
            {
                Email = "zika@gmail.com",
                Password = "Proba123!",
                RememberMe = false
            });
        response.EnsureSuccessStatusCode();

        var session = await response.Content.ReadFromJsonAsync<AdminLoginResponseDto>(JsonOptions);
        Assert.NotNull(session);
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", session!.AccessToken);

        return new AuthenticatedClient(client, session.User.Id);
    }

    private static async Task<AuthenticatedClient> RegisterAndLoginAsync(
        GeotrackApiFactory app,
        string email,
        string fullName)
    {
        var client = app.CreateClient();
        var response = await client.PostAsJsonAsync(
            "/api/auth/register",
            new RegisterRequestDto
            {
                Email = email,
                FullName = fullName,
                Password = UserPassword
            });
        response.EnsureSuccessStatusCode();

        var session = await response.Content.ReadFromJsonAsync<LoginResponseDto>(JsonOptions);
        Assert.NotNull(session);
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", session!.AccessToken);

        return new AuthenticatedClient(client, await GetUserIdAsync(app, email));
    }

    private static async Task<int> GetUserIdAsync(GeotrackApiFactory app, string email)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Users
            .Where(user => user.Email == email)
            .Select(user => user.Id)
            .SingleAsync();
    }

    private sealed record AuthenticatedClient(HttpClient Client, int UserId) : IDisposable
    {
        public void Dispose() => Client.Dispose();
    }

    private sealed record ReportedReviewFixture(
        string ReviewKey,
        int ReviewId,
        int AuthorUserId,
        string AuthorEmail,
        IReadOnlyCollection<int> ReportIds);
}
