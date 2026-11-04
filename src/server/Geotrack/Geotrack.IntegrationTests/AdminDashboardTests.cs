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
        using var client = await LoginAdminAsync(app, "zika@gmail.com", "TEST-only-Fixture-9284!");

        var dashboard = await client.GetFromJsonAsync<AdminDashboardDto>("/api/admin/dashboard");

        Assert.NotNull(dashboard);
        Assert.Equal("zika@gmail.com", dashboard!.CurrentUser.Email);
        Assert.Equal("Administrator", dashboard.CurrentUser.RoleDisplayName);
        Assert.Equal("/admin-territories", dashboard.QuickLinks.TerritoryManagementRoute);
        Assert.Equal("/admin-users", dashboard.QuickLinks.UserManagementRoute);
        Assert.Equal(string.Empty, dashboard.QuickLinks.ReviewManagementRoute);
        Assert.Equal("/admin-support-tickets", dashboard.QuickLinks.SupportTicketsRoute);
        Assert.Equal(string.Empty, dashboard.QuickLinks.PendingReportsRoute);
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

        using var territoryManager = await LoginAdminAsync(app, "mika@gmail.com", "TEST-only-Fixture-9284!");
        var territoryManagerResponse = await territoryManager.GetAsync("/api/admin/dashboard");
        Assert.Equal(HttpStatusCode.Forbidden, territoryManagerResponse.StatusCode);

        using var contentModerator = await LoginAdminAsync(app, "moderator@gmail.com", "TEST-only-Fixture-9284!");
        var contentModeratorResponse = await contentModerator.GetAsync("/api/admin/dashboard");
        Assert.Equal(HttpStatusCode.Forbidden, contentModeratorResponse.StatusCode);
    }

    [Fact]
    public async Task Content_moderator_dashboard_requires_content_moderator_and_reports_entity_counts()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        await EnsureDashboardReviewReportsAsync(app);

        using var anonymousClient = app.CreateClient();
        var anonymous = await anonymousClient.GetAsync("/api/admin/content-moderator/dashboard");
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);

        using var admin = await LoginAdminAsync(app, "zika@gmail.com", "TEST-only-Fixture-9284!");
        var adminResponse = await admin.GetAsync("/api/admin/content-moderator/dashboard");
        Assert.Equal(HttpStatusCode.Forbidden, adminResponse.StatusCode);

        await GrantUserRoleAsync(app, "elena@example.com", RoleNames.RegionalManager);
        using var regionalManager = await LoginAdminAsync(app, "elena@example.com", "password123");
        var regionalManagerResponse = await regionalManager.GetAsync("/api/admin/content-moderator/dashboard");
        Assert.Equal(HttpStatusCode.Forbidden, regionalManagerResponse.StatusCode);

        using var territoryManager = await LoginAdminAsync(app, "mika@gmail.com", "TEST-only-Fixture-9284!");
        var territoryManagerResponse = await territoryManager.GetAsync("/api/admin/content-moderator/dashboard");
        Assert.Equal(HttpStatusCode.Forbidden, territoryManagerResponse.StatusCode);

        using var moderator = await LoginAdminAsync(app, "moderator@gmail.com", "TEST-only-Fixture-9284!");
        var dashboard = await moderator.GetFromJsonAsync<ContentModeratorDashboardDto>("/api/admin/content-moderator/dashboard");
        var expected = await GetExpectedCountsAsync(app);

        Assert.NotNull(dashboard);
        Assert.Equal("moderator@gmail.com", dashboard!.CurrentUser.Email);
        Assert.Equal("Content Moderator", dashboard.CurrentUser.RoleDisplayName);
        Assert.Equal("/content-moderator/destinations", dashboard.QuickLinks.DestinationsReviewRoute);
        Assert.Equal("/content-moderator/locations", dashboard.QuickLinks.LocationsReviewRoute);
        Assert.Equal("/content-moderator/places", dashboard.QuickLinks.PlacesReviewRoute);
        Assert.Equal("/content-moderator/events", dashboard.QuickLinks.EventsReviewRoute);
        Assert.Equal("/content-moderator/review-management", dashboard.QuickLinks.ReviewManagementRoute);
        Assert.Equal("/content-moderator/review-management?status=reported", dashboard.QuickLinks.PendingReportsRoute);
        Assert.Equal(expected.DestinationsCount, dashboard.Stats.DestinationsCount);
        Assert.Equal(expected.LocationsCount, dashboard.Stats.LocationsCount);
        Assert.Equal(expected.PlacesCount, dashboard.Stats.PlacesCount);
        Assert.Equal(expected.EventsCount, dashboard.Stats.EventsCount);
        Assert.Equal(expected.SuspendedDestinationsCount, dashboard.Stats.SuspendedDestinationsCount);
        Assert.Equal(expected.SuspendedLocationsCount, dashboard.Stats.SuspendedLocationsCount);
        Assert.Equal(expected.SuspendedPlacesCount, dashboard.Stats.SuspendedPlacesCount);
        Assert.Equal(expected.SuspendedEventsCount, dashboard.Stats.SuspendedEventsCount);
        Assert.Equal(expected.DeletedDestinationsCount, dashboard.Stats.DeletedDestinationsCount);
        Assert.Equal(expected.DeletedLocationsCount, dashboard.Stats.DeletedLocationsCount);
        Assert.Equal(expected.DeletedPlacesCount, dashboard.Stats.DeletedPlacesCount);
        Assert.Equal(expected.DeletedEventsCount, dashboard.Stats.DeletedEventsCount);
        Assert.Equal(expected.PendingReportsCount, dashboard.Stats.PendingReviewReportsCount);
        Assert.Equal(expected.PendingReportsCount, dashboard.Badges.PendingReviewReportsCount);
        Assert.True(dashboard.Stats.ReportedReviewsCount > 0);
        Assert.True(dashboard.Stats.ActiveReviewsCount >= 0);
        Assert.Equal(expected.ReportedReviewsCount - expected.PendingReportsCount, dashboard.Stats.ResolvedReportsCount);
    }

    [Fact]
    public async Task Dashboard_stats_match_database_counts()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        await EnsureDashboardReviewReportsAsync(app);
        await EnsureDashboardSupportTicketsAsync(app);

        using var client = await LoginAdminAsync(app, "zika@gmail.com", "TEST-only-Fixture-9284!");

        var dashboard = await client.GetFromJsonAsync<AdminDashboardDto>("/api/admin/dashboard");
        var expected = await GetExpectedCountsAsync(app);

        Assert.NotNull(dashboard);
        Assert.Equal(expected.TerritoriesCount, dashboard!.Stats.TerritoriesCount);
        Assert.Equal(expected.AssignedTerritoriesCount, dashboard.Stats.AssignedTerritoriesCount);
        Assert.Equal(expected.AppUsersCount, dashboard.Stats.AppUsersCount);
        Assert.Equal(expected.SuspendedUsersCount, dashboard.Stats.SuspendedUsersCount);
        Assert.Equal(expected.ReviewsCount, dashboard.Stats.ReviewsCount);
        Assert.Equal(0, dashboard.Stats.ReportedReviewsCount);
        Assert.Equal(0, dashboard.Stats.PendingReportsCount);
        Assert.Equal(expected.SupportTicketsCount, dashboard.Stats.SupportTicketsCount);
        Assert.Equal(expected.OpenSupportTicketsCount, dashboard.Stats.OpenSupportTicketsCount);
        Assert.Equal(expected.ResolvedSupportTicketsCount, dashboard.Stats.ResolvedSupportTicketsCount);
        Assert.Equal(expected.DismissedSupportTicketsCount, dashboard.Stats.DismissedSupportTicketsCount);
        Assert.Equal(expected.UnresolvedSupportTicketsCount, dashboard.Stats.UnresolvedSupportTicketsCount);
        Assert.Equal(expected.HighPrioritySupportTicketsCount, dashboard.Stats.HighPrioritySupportTicketsCount);
        Assert.Equal(0, dashboard.Badges.PendingReportsCount);
        Assert.Equal(expected.UnresolvedSupportTicketsCount, dashboard.Badges.SupportReportsCount);
        Assert.True(expected.PendingReportsCount > 0);
        Assert.True(dashboard.Stats.UnresolvedSupportTicketsCount > 0);
    }

    [Fact]
    public async Task Dashboard_returns_zero_counts_for_empty_report_review_and_assignment_tables()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        await ClearReportReviewAndAssignmentTablesAsync(app);

        using var client = await LoginAdminAsync(app, "zika@gmail.com", "TEST-only-Fixture-9284!");
        var response = await client.GetAsync("/api/admin/dashboard");

        response.EnsureSuccessStatusCode();

        var dashboard = await response.Content.ReadFromJsonAsync<AdminDashboardDto>();
        Assert.NotNull(dashboard);
        Assert.Equal(0, dashboard!.Stats.AssignedTerritoriesCount);
        Assert.Equal(0, dashboard.Stats.ReviewsCount);
        Assert.Equal(0, dashboard.Stats.ReportedReviewsCount);
        Assert.Equal(0, dashboard.Stats.PendingReportsCount);
        Assert.Equal(0, dashboard.Stats.SupportTicketsCount);
        Assert.Equal(0, dashboard.Stats.UnresolvedSupportTicketsCount);
        Assert.Equal(0, dashboard.Badges.PendingReportsCount);
        Assert.Equal(0, dashboard.Badges.SupportReportsCount);
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

    private static async Task EnsureDashboardSupportTicketsAsync(GeotrackApiFactory app)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var userId = await db.Users
            .OrderBy(user => user.Id)
            .Select(user => user.Id)
            .FirstAsync();

        var token = $"dashboard-support-{Guid.NewGuid():N}";
        var now = DateTime.UtcNow;
        db.SupportRequests.AddRange(
            new SupportRequest
            {
                UserId = userId,
                Category = "account-access",
                Subject = $"{token} open",
                Message = "Dashboard support open ticket.",
                Status = SupportRequestStatuses.Open,
                Priority = SupportRequestPriorities.High,
                CreatedAt = now.AddMinutes(-3)
            },
            new SupportRequest
            {
                UserId = userId,
                Category = "location-and-maps",
                Subject = $"{token} in progress",
                Message = "Dashboard support in-progress ticket.",
                Status = "InProgress",
                Priority = SupportRequestPriorities.Normal,
                CreatedAt = now.AddMinutes(-2),
                UpdatedAt = now.AddMinutes(-1)
            },
            new SupportRequest
            {
                UserId = userId,
                Category = "privacy-and-data",
                Subject = $"{token} resolved",
                Message = "Dashboard support resolved ticket.",
                Status = SupportRequestStatuses.Resolved,
                Priority = SupportRequestPriorities.Critical,
                CreatedAt = now.AddMinutes(-1),
                UpdatedAt = now,
                ResolvedAt = now
            });

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
        var openSupportTicketsCount = await db.SupportRequests
            .AsNoTracking()
            .CountAsync(ticket =>
                ticket.Status == SupportRequestStatuses.Open ||
                ticket.Status == "Received" ||
                ticket.Status == "InProgress");

        return new DashboardCounts(
            TerritoriesCount: await db.Territories.AsNoTracking().CountAsync(),
            AssignedTerritoriesCount: await db.TerritoryManagerTerritories
                .AsNoTracking()
                .Select(assignment => assignment.TerritoryId)
                .Distinct()
                .CountAsync(),
            AppUsersCount: await db.Users.AsNoTracking().CountAsync(user => !user.IsDeleted),
            SuspendedUsersCount: await db.Users
                .AsNoTracking()
                .CountAsync(user => !user.IsDeleted && user.IsSuspended),
            ReviewsCount: reviewsCount,
            ReportedReviewsCount: await db.ReviewReports.AsNoTracking().CountAsync(),
            PendingReportsCount: await db.ReviewReports
                .AsNoTracking()
                .CountAsync(report => report.Status == ReviewReportStatus.Pending),
            SupportTicketsCount: await db.SupportRequests.AsNoTracking().CountAsync(),
            OpenSupportTicketsCount: openSupportTicketsCount,
            ResolvedSupportTicketsCount: await db.SupportRequests
                .AsNoTracking()
                .CountAsync(ticket => ticket.Status == SupportRequestStatuses.Resolved),
            DismissedSupportTicketsCount: await db.SupportRequests
                .AsNoTracking()
                .CountAsync(ticket => ticket.Status == SupportRequestStatuses.Dismissed),
            UnresolvedSupportTicketsCount: openSupportTicketsCount,
            HighPrioritySupportTicketsCount: await db.SupportRequests
                .AsNoTracking()
                .CountAsync(ticket =>
                    (
                        ticket.Status == SupportRequestStatuses.Open ||
                        ticket.Status == "Received" ||
                        ticket.Status == "InProgress"
                    ) &&
                    (
                        ticket.Priority == SupportRequestPriorities.High ||
                        ticket.Priority == SupportRequestPriorities.Critical
                    )),
            DestinationsCount: await db.Destinations.AsNoTracking().CountAsync(entity => entity.Status != "Archived"),
            LocationsCount: await db.Locations.AsNoTracking().CountAsync(entity => entity.Status != "Archived"),
            PlacesCount: await db.Places.AsNoTracking().CountAsync(entity => entity.Status != "Archived"),
            EventsCount: await db.Events.AsNoTracking().CountAsync(entity => entity.Status != "Archived"),
            SuspendedDestinationsCount: await db.Destinations.AsNoTracking().CountAsync(entity => entity.Status == "Draft"),
            SuspendedLocationsCount: await db.Locations.AsNoTracking().CountAsync(entity => entity.Status == "Draft"),
            SuspendedPlacesCount: await db.Places.AsNoTracking().CountAsync(entity => entity.Status == "Draft"),
            SuspendedEventsCount: await db.Events.AsNoTracking().CountAsync(entity => entity.Status == "Draft"),
            DeletedDestinationsCount: await db.Destinations.AsNoTracking().CountAsync(entity => entity.Status == "Archived"),
            DeletedLocationsCount: await db.Locations.AsNoTracking().CountAsync(entity => entity.Status == "Archived"),
            DeletedPlacesCount: await db.Places.AsNoTracking().CountAsync(entity => entity.Status == "Archived"),
            DeletedEventsCount: await db.Events.AsNoTracking().CountAsync(entity => entity.Status == "Archived"));
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
        await db.SupportRequests.ExecuteDeleteAsync();
        await db.TerritoryManagerTerritories.ExecuteDeleteAsync();
    }

    private sealed record DashboardCounts(
        int TerritoriesCount,
        int AssignedTerritoriesCount,
        int AppUsersCount,
        int SuspendedUsersCount,
        int ReviewsCount,
        int ReportedReviewsCount,
        int PendingReportsCount,
        int SupportTicketsCount,
        int OpenSupportTicketsCount,
        int ResolvedSupportTicketsCount,
        int DismissedSupportTicketsCount,
        int UnresolvedSupportTicketsCount,
        int HighPrioritySupportTicketsCount,
        int DestinationsCount,
        int LocationsCount,
        int PlacesCount,
        int EventsCount,
        int SuspendedDestinationsCount,
        int SuspendedLocationsCount,
        int SuspendedPlacesCount,
        int SuspendedEventsCount,
        int DeletedDestinationsCount,
        int DeletedLocationsCount,
        int DeletedPlacesCount,
        int DeletedEventsCount);
}
