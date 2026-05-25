using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Geotrack.Application.DTOs.Admin;
using Geotrack.Application.DTOs.Auth;
using Geotrack.Application.Security;
using Geotrack.Domain.Entities.Reviews;
using Geotrack.Domain.Entities.Users;
using Geotrack.Infrastructure;
using Geotrack.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Geotrack.IntegrationTests;

public sealed class AdminDashboardTests
{
    [Fact]
    public async Task Admin_can_load_dashboard_with_current_user_and_links()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        using var client = await LoginAdminAsync(app, "zika@gmail.com", "Proba123!");

        var dashboard = await client.GetFromJsonAsync<AdminDashboardDto>("/api/admin/dashboard");

        Assert.NotNull(dashboard);
        Assert.Equal("zika@gmail.com", dashboard!.CurrentUser.Email);
        Assert.Equal("Administrator", dashboard.CurrentUser.RoleDisplayName);
        Assert.Equal("/admin-territories", dashboard.QuickLinks.TerritoryManagementRoute);
        Assert.Equal("/admin-users", dashboard.QuickLinks.UserManagementRoute);
        Assert.Equal("/admin-reviews", dashboard.QuickLinks.ReviewManagementRoute);
        Assert.Equal("/admin-reviews?status=reported", dashboard.QuickLinks.PendingReportsRoute);
    }

    [Fact]
    public async Task Dashboard_requires_authenticated_admin_role()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();

        using var anonymousClient = app.CreateClient();
        var anonymous = await anonymousClient.GetAsync("/api/admin/dashboard");
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);

        await GrantUserRoleAsync(app, "elena@example.com", RoleNames.RegionalManager);
        using var regionalManager = await LoginAdminAsync(app, "elena@example.com", "password123");
        var regionalManagerResponse = await regionalManager.GetAsync("/api/admin/dashboard");
        Assert.Equal(HttpStatusCode.Forbidden, regionalManagerResponse.StatusCode);

        using var territoryManager = await LoginAdminAsync(app, "mika@gmail.com", "Proba123!");
        var territoryManagerResponse = await territoryManager.GetAsync("/api/admin/dashboard");
        Assert.Equal(HttpStatusCode.Forbidden, territoryManagerResponse.StatusCode);
    }

    [Fact]
    public async Task Dashboard_stats_match_database_counts()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        await EnsureDashboardReviewReportsAsync(app);

        using var client = await LoginAdminAsync(app, "zika@gmail.com", "Proba123!");

        var dashboard = await client.GetFromJsonAsync<AdminDashboardDto>("/api/admin/dashboard");
        var expected = await GetExpectedCountsAsync(app);

        Assert.NotNull(dashboard);
        Assert.Equal(expected.TerritoriesCount, dashboard!.Stats.TerritoriesCount);
        Assert.Equal(expected.AssignedTerritoriesCount, dashboard.Stats.AssignedTerritoriesCount);
        Assert.Equal(expected.AppUsersCount, dashboard.Stats.AppUsersCount);
        Assert.Equal(0, dashboard.Stats.SuspendedUsersCount);
        Assert.Equal(expected.ReviewsCount, dashboard.Stats.ReviewsCount);
        Assert.Equal(expected.ReportedReviewsCount, dashboard.Stats.ReportedReviewsCount);
        Assert.Equal(expected.PendingReportsCount, dashboard.Stats.PendingReportsCount);
        Assert.Equal(expected.PendingReportsCount, dashboard.Badges.PendingReportsCount);
        Assert.True(dashboard.Stats.ReportedReviewsCount >= dashboard.Stats.PendingReportsCount);
        Assert.True(dashboard.Stats.PendingReportsCount > 0);
    }

    [Fact]
    public async Task Dashboard_returns_zero_counts_for_empty_report_review_and_assignment_tables()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        await ClearReportReviewAndAssignmentTablesAsync(app);

        using var client = await LoginAdminAsync(app, "zika@gmail.com", "Proba123!");
        var response = await client.GetAsync("/api/admin/dashboard");

        response.EnsureSuccessStatusCode();

        var dashboard = await response.Content.ReadFromJsonAsync<AdminDashboardDto>();
        Assert.NotNull(dashboard);
        Assert.Equal(0, dashboard!.Stats.AssignedTerritoriesCount);
        Assert.Equal(0, dashboard.Stats.ReviewsCount);
        Assert.Equal(0, dashboard.Stats.ReportedReviewsCount);
        Assert.Equal(0, dashboard.Stats.PendingReportsCount);
        Assert.Equal(0, dashboard.Badges.PendingReportsCount);
    }

    private static async Task<HttpClient> LoginAdminAsync(GeotrackApiFactory app, string email, string password)
    {
        var client = app.CreateClient();
        var response = await client.PostAsJsonAsync(
            "/api/admin/auth/login",
            new LoginRequestDto
            {
                Email = email,
                Password = password,
            });
        response.EnsureSuccessStatusCode();

        var session = await response.Content.ReadFromJsonAsync<AdminLoginResponseDto>();
        Assert.NotNull(session);
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", session!.AccessToken);

        return client;
    }

    private static async Task GrantUserRoleAsync(GeotrackApiFactory app, string email, string roleName)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = await db.Users.SingleAsync(existing => existing.Email == email);
        var role = await db.Roles.SingleAsync(existing => existing.Name == roleName);

        var hasRole = await db.UserRoles.AnyAsync(userRole =>
            userRole.UserId == user.Id &&
            userRole.RoleId == role.Id);

        if (!hasRole)
        {
            db.UserRoles.Add(new UserRole
            {
                UserId = user.Id,
                RoleId = role.Id,
            });
            await db.SaveChangesAsync();
        }
    }

    private static async Task EnsureDashboardReviewReportsAsync(GeotrackApiFactory app)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var place = await db.Places
            .Include(existing => existing.Location)
            .ThenInclude(location => location.Destination)
            .OrderBy(existing => existing.Id)
            .FirstAsync();

        var users = await db.Users
            .OrderBy(existing => existing.Id)
            .Take(4)
            .ToListAsync();

        Assert.True(users.Count >= 3);

        var reviewAuthor = users[0];
        var pendingReporter = users[1];
        var dismissedReporter = users[2];

        var review = await db.PlaceReviews
            .FirstOrDefaultAsync(existing => existing.PlaceId == place.Id && existing.UserId == reviewAuthor.Id);

        if (review is null)
        {
            review = new PlaceReview
            {
                PlaceId = place.Id,
                UserId = reviewAuthor.Id,
                Rating = 2,
                Comment = "Dashboard integration test review",
                CreatedAt = DateTime.UtcNow.AddDays(-2),
            };

            db.PlaceReviews.Add(review);
            await db.SaveChangesAsync();
        }

        await AddReportIfMissingAsync(
            db,
            review,
            pendingReporter.Id,
            place.Id,
            place.Name,
            place.Location.Destination.RegionId,
            ReviewReportStatus.Pending);

        await AddReportIfMissingAsync(
            db,
            review,
            dismissedReporter.Id,
            place.Id,
            place.Name,
            place.Location.Destination.RegionId,
            ReviewReportStatus.Dismissed);

        await db.SaveChangesAsync();
    }

    private static async Task AddReportIfMissingAsync(
        AppDbContext db,
        PlaceReview review,
        int reporterUserId,
        int entityId,
        string entityName,
        int regionId,
        ReviewReportStatus status)
    {
        var exists = await db.ReviewReports.AnyAsync(report =>
            report.ReporterUserId == reporterUserId &&
            report.TargetType == ReviewReportTargetType.Place &&
            report.ReviewId == review.Id);

        if (exists)
            return;

        db.ReviewReports.Add(new ReviewReport
        {
            TargetType = ReviewReportTargetType.Place,
            ReviewId = review.Id,
            PlaceReviewId = review.Id,
            ReporterUserId = reporterUserId,
            Reason = ReviewReportReason.OffensiveContent,
            Note = "Dashboard integration test report",
            Status = status,
            ReviewAuthorUserId = review.UserId,
            ReviewAuthorName = "Dashboard Test User",
            EntityId = entityId,
            EntityName = entityName,
            RegionId = regionId,
            RegionName = "Dashboard Test Region",
            ReviewRating = review.Rating,
            ReviewCommentSnapshot = review.Comment,
            ReviewCreatedAt = review.CreatedAt,
            CreatedAt = DateTime.UtcNow,
        });
    }

    private static async Task<DashboardCounts> GetExpectedCountsAsync(GeotrackApiFactory app)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var reviewsCount =
            await db.PlaceReviews.AsNoTracking().CountAsync() +
            await db.EventReviews.AsNoTracking().CountAsync() +
            await db.DestinationReviews.AsNoTracking().CountAsync() +
            await db.LocationReviews.AsNoTracking().CountAsync();

        return new DashboardCounts(
            TerritoriesCount: await db.Territories.AsNoTracking().CountAsync(),
            AssignedTerritoriesCount: await db.TerritoryManagerTerritories
                .AsNoTracking()
                .Select(assignment => assignment.TerritoryId)
                .Distinct()
                .CountAsync(),
            AppUsersCount: await db.Users.AsNoTracking().CountAsync(),
            ReviewsCount: reviewsCount,
            ReportedReviewsCount: await db.ReviewReports.AsNoTracking().CountAsync(),
            PendingReportsCount: await db.ReviewReports
                .AsNoTracking()
                .CountAsync(report => report.Status == ReviewReportStatus.Pending));
    }

    private static async Task ClearReportReviewAndAssignmentTablesAsync(GeotrackApiFactory app)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        await db.ReviewReports.ExecuteDeleteAsync();
        await db.PlaceReviews.ExecuteDeleteAsync();
        await db.EventReviews.ExecuteDeleteAsync();
        await db.DestinationReviews.ExecuteDeleteAsync();
        await db.LocationReviews.ExecuteDeleteAsync();
        await db.TerritoryManagerTerritories.ExecuteDeleteAsync();
    }

    private sealed record DashboardCounts(
        int TerritoriesCount,
        int AssignedTerritoriesCount,
        int AppUsersCount,
        int ReviewsCount,
        int ReportedReviewsCount,
        int PendingReportsCount);
}
