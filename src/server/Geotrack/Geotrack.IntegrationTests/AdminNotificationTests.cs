using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Geotrack.Application.DTOs.Admin;
using Geotrack.Application.DTOs.Auth;
using Geotrack.Application.DTOs.Contributions;
using Geotrack.Application.DTOs.Reviews;
using Geotrack.Application.DTOs.Users;
using Geotrack.Application.Interfaces.Service;
using Geotrack.Application.Security;
using Geotrack.Domain.Entities.Core;
using Geotrack.Domain.Entities.Reviews;
using Geotrack.Domain.Entities.Users;
using Geotrack.Infrastructure;
using Geotrack.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Geotrack.IntegrationTests;

public sealed class AdminNotificationTests
{
    private const string UserPassword = "Password123!";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task Admin_notification_scope_migration_is_registered_and_schema_contains_scope_columns()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();

        await using var scope = app.Services.CreateAsyncScope();
        var migrations = scope.ServiceProvider.GetRequiredService<IMigrationsAssembly>();

        Assert.Contains("20260527120000_AddAdminNotificationScopes", migrations.Migrations.Keys);

        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var columns = await ReadAdminNotificationColumnsAsync(db);

        Assert.Contains("ScopeType", columns);
        Assert.Contains("ScopeId", columns);
        Assert.Contains("ScopeLabel", columns);
    }

    [Fact]
    public async Task Admin_notification_list_returns_empty_page_when_no_notifications_exist()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        using var admin = await LoginAdminAsync(app);

        var response = await admin.Client.GetAsync("/api/admin/notifications?includeRead=true&skip=0&take=25");
        response.EnsureSuccessStatusCode();

        var page = await response.Content.ReadFromJsonAsync<AdminNotificationListDto>(JsonOptions);
        Assert.NotNull(page);
        Assert.Empty(page!.Items);
        Assert.Equal(0, page.UnreadCount);
        Assert.Equal(0, page.TotalCount);
        Assert.Equal(0, page.Skip);
        Assert.Equal(25, page.Take);
        Assert.Null(page.NextSkip);
        Assert.False(page.HasMore);
    }

    [Fact]
    public async Task Territory_manager_notification_list_returns_empty_page_when_no_notifications_exist()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        using var manager = await LoginAdminAppAsync(app, "mika@gmail.com", "Proba123!");

        var response = await manager.Client.GetAsync(
            "/api/admin/notifications?audienceRole=TerritoryManager&includeRead=true&skip=0&take=25");
        response.EnsureSuccessStatusCode();

        var page = await response.Content.ReadFromJsonAsync<AdminNotificationListDto>(JsonOptions);
        Assert.NotNull(page);
        Assert.Empty(page!.Items);
        Assert.Equal(0, page.UnreadCount);
        Assert.Equal(0, page.TotalCount);
        Assert.Equal(0, page.Skip);
        Assert.Equal(25, page.Take);
        Assert.Null(page.NextSkip);
        Assert.False(page.HasMore);
    }

    [Fact]
    public async Task Regional_manager_notification_list_returns_empty_page_when_no_notifications_exist()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        var fixture = await CreateRegionalManagerAssignmentFixtureAsync(app);
        using var manager = await LoginAdminAppAsync(app, "elena@example.com", "password123");

        var response = await manager.Client.GetAsync(
            $"/api/admin/notifications?audienceRole=RegionalManager&regionId={fixture.AssignedRegionId}&includeRead=true&skip=0&take=25");
        response.EnsureSuccessStatusCode();

        var page = await response.Content.ReadFromJsonAsync<AdminNotificationListDto>(JsonOptions);
        Assert.NotNull(page);
        Assert.Empty(page!.Items);
        Assert.Equal(0, page.UnreadCount);
        Assert.Equal(0, page.TotalCount);
        Assert.Equal(0, page.Skip);
        Assert.Equal(25, page.Take);
        Assert.Null(page.NextSkip);
        Assert.False(page.HasMore);
    }

    [Fact]
    public async Task Territory_manager_scope_filter_handles_null_scope_id_without_exposing_global_rows()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        using var manager = await LoginAdminAppAsync(app, "mika@gmail.com", "Proba123!");

        await AddNotificationAsync(
            app,
            "ContributionPendingReview",
            "Null scoped region",
            "A malformed scoped notification must not leak.",
            "Contribution",
            70001,
            audienceRole: RoleNames.TerritoryManager,
            scopeType: "Region",
            scopeId: null);
        await AddNotificationAsync(
            app,
            "ReviewReported",
            "Territory global row",
            "A global territory-manager row must not bypass territory scope filtering.",
            "ReviewReport",
            70002,
            audienceRole: RoleNames.TerritoryManager,
            scopeType: "Global",
            scopeId: null);

        var response = await manager.Client.GetAsync(
            "/api/admin/notifications?audienceRole=TerritoryManager&includeRead=true&skip=0&take=25");
        response.EnsureSuccessStatusCode();

        var page = await response.Content.ReadFromJsonAsync<AdminNotificationListDto>(JsonOptions);
        Assert.NotNull(page);
        Assert.Empty(page!.Items);
        Assert.Equal(0, page.UnreadCount);
        Assert.Equal(0, page.TotalCount);
    }

    [Fact]
    public async Task Regional_manager_notifications_are_scoped_and_read_state_is_per_user()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        var fixture = await CreateRegionalNotificationFixtureAsync(app);

        using var anonymous = app.CreateClient();
        var anonymousResponse = await anonymous.GetAsync(
            $"/api/admin/notifications?audienceRole=RegionalManager&regionId={fixture.AssignedRegionId}");
        Assert.Equal(HttpStatusCode.Unauthorized, anonymousResponse.StatusCode);

        using var user = await RegisterAndLoginAsync(
            app,
            $"regional-notification-user-{Guid.NewGuid():N}@example.com",
            "Regular User");
        var nonAdminResponse = await user.Client.GetAsync(
            $"/api/admin/notifications?audienceRole=RegionalManager&regionId={fixture.AssignedRegionId}");
        Assert.Equal(HttpStatusCode.Forbidden, nonAdminResponse.StatusCode);

        using var manager = await LoginAdminAppAsync(app, "elena@example.com", "password123");
        var page = await manager.Client.GetFromJsonAsync<AdminNotificationListDto>(
            $"/api/admin/notifications?audienceRole=RegionalManager&regionId={fixture.AssignedRegionId}&includeRead=true&skip=0&take=25",
            JsonOptions);

        Assert.NotNull(page);
        Assert.Equal(2, page!.UnreadCount);
        Assert.Contains(page.Items, item => item.Id == fixture.AssignedUnreadId && !item.IsRead);
        Assert.Contains(page.Items, item => item.Id == fixture.AssignedSecondUnreadId && !item.IsRead);
        Assert.Contains(page.Items, item => item.Id == fixture.AssignedRecentReadId && item.IsRead);
        Assert.DoesNotContain(page.Items, item => item.Id == fixture.AssignedOldReadId);
        Assert.DoesNotContain(page.Items, item => item.Id == fixture.OtherRegionNotificationId);
        Assert.DoesNotContain(page.Items, item => item.Id == fixture.AdminOnlyNotificationId);
        Assert.DoesNotContain(page.Items, item => item.Id == fixture.TerritoryOnlyNotificationId);
        Assert.DoesNotContain(page.Items, item => item.Id == fixture.HiddenReviewReportNotificationId);
        Assert.DoesNotContain(page.Items, item => item.Id == fixture.HiddenOtherManagerAssignmentNotificationId);

        var count = await manager.Client.GetFromJsonAsync<AdminNotificationUnreadCountDto>(
            $"/api/admin/notifications/unread-count?audienceRole=RegionalManager&regionId={fixture.AssignedRegionId}",
            JsonOptions);
        Assert.NotNull(count);
        Assert.Equal(2, count!.UnreadCount);

        var noRegionPage = await manager.Client.GetFromJsonAsync<AdminNotificationListDto>(
            "/api/admin/notifications?audienceRole=RegionalManager&includeRead=true&skip=0&take=25",
            JsonOptions);
        Assert.NotNull(noRegionPage);
        Assert.Empty(noRegionPage!.Items);

        var otherRegionPage = await manager.Client.GetFromJsonAsync<AdminNotificationListDto>(
            $"/api/admin/notifications?audienceRole=RegionalManager&regionId={fixture.OtherRegionId}&includeRead=true&skip=0&take=25",
            JsonOptions);
        Assert.NotNull(otherRegionPage);
        Assert.Empty(otherRegionPage!.Items);

        var forbiddenOtherRegionRead = await manager.Client.PostAsJsonAsync(
            $"/api/admin/notifications/{fixture.OtherRegionNotificationId}/read?audienceRole=RegionalManager&regionId={fixture.AssignedRegionId}",
            new { });
        Assert.Equal(HttpStatusCode.NotFound, forbiddenOtherRegionRead.StatusCode);

        var markOne = await manager.Client.PostAsJsonAsync(
            $"/api/admin/notifications/{fixture.AssignedUnreadId}/read?audienceRole=RegionalManager&regionId={fixture.AssignedRegionId}",
            new { });
        markOne.EnsureSuccessStatusCode();
        var markOneResult = await markOne.Content.ReadFromJsonAsync<AdminNotificationMutationDto>(JsonOptions);
        Assert.NotNull(markOneResult);
        Assert.Equal(1, markOneResult!.UnreadCount);

        var markAll = await manager.Client.PostAsJsonAsync(
            $"/api/admin/notifications/mark-all-read?audienceRole=RegionalManager&regionId={fixture.AssignedRegionId}",
            new { });
        markAll.EnsureSuccessStatusCode();
        var markAllResult = await markAll.Content.ReadFromJsonAsync<AdminNotificationMutationDto>(JsonOptions);
        Assert.NotNull(markAllResult);
        Assert.Equal(0, markAllResult!.UnreadCount);

        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.False(await db.AdminNotificationReadStates.AnyAsync(state =>
            state.UserId == manager.UserId &&
            state.AdminNotificationId == fixture.OtherRegionNotificationId));
    }

    [Fact]
    public async Task Admin_notification_api_requires_admin_and_hides_sensitive_metadata()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();

        using var anonymous = app.CreateClient();
        var anonymousResponse = await anonymous.GetAsync("/api/admin/notifications");
        Assert.Equal(HttpStatusCode.Unauthorized, anonymousResponse.StatusCode);

        using var user = await RegisterAndLoginAsync(app, $"notification-user-{Guid.NewGuid():N}@example.com", "Regular User");
        var nonAdminResponse = await user.Client.GetAsync("/api/admin/notifications");
        Assert.Equal(HttpStatusCode.Forbidden, nonAdminResponse.StatusCode);

        using var admin = await LoginAdminAsync(app);
        await AddNotificationAsync(
            app,
            type: "System",
            title: "System notice",
            message: "Safe administrator notification.",
            targetType: "System",
            targetId: 9001,
            metadataJson: """{"summary":"Safe summary","passwordHash":"do-not-return","refreshToken":"do-not-return"}""");

        var response = await admin.Client.GetAsync("/api/admin/notifications?includeRead=true&skip=0&take=25");
        response.EnsureSuccessStatusCode();

        var content = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("passwordHash", content, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("refreshToken", content, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("do-not-return", content, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Safe summary", content, StringComparison.Ordinal);

        var page = JsonSerializer.Deserialize<AdminNotificationListDto>(content, JsonOptions);
        Assert.NotNull(page);
        Assert.Equal(1, page!.UnreadCount);
        Assert.Contains(page.Items, item => item.Title == "System notice" && item.MetadataSummary == "Safe summary");
    }

    [Fact]
    public async Task Admin_notifications_apply_read_retention_and_mark_read_actions_per_admin()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        using var admin = await LoginAdminAsync(app);

        var unreadOldId = await AddNotificationAsync(
            app,
            "System",
            "Old unread",
            "Unread notifications stay visible.",
            "System",
            9101,
            createdAt: DateTime.UtcNow.AddMonths(-4));
        var readOldId = await AddNotificationAsync(
            app,
            "System",
            "Old read",
            "Old read notifications are hidden.",
            "System",
            9102,
            createdAt: DateTime.UtcNow.AddMonths(-4),
            readByUserId: admin.UserId,
            readAt: DateTime.UtcNow.AddMonths(-3));
        var readRecentId = await AddNotificationAsync(
            app,
            "System",
            "Recent read",
            "Recent read notifications stay visible.",
            "System",
            9103,
            createdAt: DateTime.UtcNow.AddMonths(-3),
            readByUserId: admin.UserId,
            readAt: DateTime.UtcNow.AddMonths(-1));
        var unreadRecentId = await AddNotificationAsync(
            app,
            "System",
            "Recent unread",
            "Recent unread notification.",
            "System",
            9104,
            createdAt: DateTime.UtcNow);
        _ = readOldId;

        var page = await admin.Client.GetFromJsonAsync<AdminNotificationListDto>(
            "/api/admin/notifications?includeRead=true&skip=0&take=25",
            JsonOptions);

        Assert.NotNull(page);
        Assert.Equal(2, page!.UnreadCount);
        Assert.Contains(page.Items, item => item.Id == unreadOldId && !item.IsRead);
        Assert.Contains(page.Items, item => item.Id == unreadRecentId && !item.IsRead);
        Assert.Contains(page.Items, item => item.Id == readRecentId && item.IsRead);
        Assert.DoesNotContain(page.Items, item => item.Title == "Old read");

        var count = await admin.Client.GetFromJsonAsync<AdminNotificationUnreadCountDto>(
            "/api/admin/notifications/unread-count",
            JsonOptions);
        Assert.NotNull(count);
        Assert.Equal(2, count!.UnreadCount);

        var markOne = await admin.Client.PostAsJsonAsync(
            $"/api/admin/notifications/{unreadRecentId}/read",
            new { });
        markOne.EnsureSuccessStatusCode();
        var markOneResult = await markOne.Content.ReadFromJsonAsync<AdminNotificationMutationDto>(JsonOptions);
        Assert.NotNull(markOneResult);
        Assert.Equal(1, markOneResult!.UnreadCount);
        Assert.NotNull(markOneResult.ReadAt);

        var markAll = await admin.Client.PostAsJsonAsync("/api/admin/notifications/mark-all-read", new { });
        markAll.EnsureSuccessStatusCode();
        var markAllResult = await markAll.Content.ReadFromJsonAsync<AdminNotificationMutationDto>(JsonOptions);
        Assert.NotNull(markAllResult);
        Assert.Equal(0, markAllResult!.UnreadCount);
    }

    [Fact]
    public async Task Support_ticket_creation_creates_admin_notification()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        var token = $"support-notification-{Guid.NewGuid():N}";
        using var user = await RegisterAndLoginAsync(app, $"{token}@example.com", "Support Reporter");

        var createResponse = await user.Client.PostAsJsonAsync(
            "/api/user/me/support-requests",
            new CreateSupportRequestRequestDto
            {
                Category = "location-and-maps",
                Subject = $"{token} map issue",
                Message = "The map stayed on the wrong city."
            });
        createResponse.EnsureSuccessStatusCode();

        var created = await createResponse.Content.ReadFromJsonAsync<CreateSupportRequestResponseDto>(JsonOptions);
        Assert.NotNull(created);

        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var notification = await db.AdminNotifications.SingleAsync(item =>
            item.AudienceRole == RoleNames.Admin &&
            item.Type == "SupportTicketCreated" &&
            item.TargetType == "SupportTicket" &&
            item.TargetId == created!.Id);

        Assert.Equal("New support report", notification.Title);
        Assert.Contains("Location and maps", notification.Message, StringComparison.Ordinal);
        Assert.Equal($"/admin-support-tickets?ticketId={created.Id}", notification.TargetRoute);
        Assert.False(await db.AdminNotifications.AnyAsync(item =>
            item.AudienceRole == RoleNames.TerritoryManager &&
            item.TargetType == "SupportTicket" &&
            item.TargetId == created.Id));
    }

    [Fact]
    public async Task Review_report_creation_creates_content_moderator_notification()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        var token = $"review-notification-{Guid.NewGuid():N}";
        using var author = await RegisterAndLoginAsync(app, $"{token}-author@example.com", "Review Author");
        using var reporter = await RegisterAndLoginAsync(app, $"{token}-reporter@example.com", "Review Reporter");
        var reviewId = await AddPlaceReviewAsync(app, author.UserId, $"{token} reported review body");

        var reportResponse = await reporter.Client.PostAsJsonAsync(
            $"/api/reviews/reports/place/{reviewId}",
            new ReportReviewRequestDto
            {
                Reason = "spam",
                Note = "Looks automated."
            });
        reportResponse.EnsureSuccessStatusCode();

        var report = await reportResponse.Content.ReadFromJsonAsync<ReportReviewResponseDto>(JsonOptions);
        Assert.NotNull(report);
        Assert.False(report!.AlreadyReported);

        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var notification = await db.AdminNotifications.SingleAsync(item =>
            item.AudienceRole == RoleNames.ContentModerator &&
            item.Type == "ReviewReported" &&
            item.TargetType == "ReviewReport" &&
            item.TargetId == report.ReportId);

        Assert.Equal("New reported review", notification.Title);
        Assert.Contains("spam", notification.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal($"/content-moderator/review-management?status=reported&highlight=Place%3A{reviewId}", notification.TargetRoute);

        Assert.False(await db.AdminNotifications.AnyAsync(item =>
            item.AudienceRole == RoleNames.Admin &&
            item.Type == "ReviewReported" &&
            item.TargetType == "ReviewReport" &&
            item.TargetId == report.ReportId));

        Assert.False(await db.AdminNotifications.AnyAsync(item =>
            item.AudienceRole == RoleNames.TerritoryManager &&
            item.Type == "ReviewReported" &&
            item.TargetType == "ReviewReport" &&
            item.TargetId == report.ReportId));
        Assert.False(await db.AdminNotifications.AnyAsync(item =>
            item.AudienceRole == RoleNames.RegionalManager &&
            item.Type == "ReviewReported" &&
            item.TargetType == "ReviewReport" &&
            item.TargetId == report.ReportId));

        using var moderator = await LoginAdminAppAsync(app, "moderator@gmail.com", "Proba123!");
        var page = await moderator.Client.GetFromJsonAsync<AdminNotificationListDto>(
            "/api/admin/notifications?audienceRole=ContentModerator&includeRead=true&skip=0&take=25",
            JsonOptions);

        Assert.NotNull(page);
        Assert.Contains(page!.Items, item => item.Id == notification.Id && !item.IsRead);
        Assert.Equal(1, page.UnreadCount);
    }

    [Fact]
    public async Task Territory_manager_notifications_are_scoped_and_read_state_is_per_user()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        var fixture = await CreateTerritoryNotificationFixtureAsync(app);

        using var anonymous = app.CreateClient();
        var anonymousResponse = await anonymous.GetAsync("/api/admin/notifications?audienceRole=TerritoryManager");
        Assert.Equal(HttpStatusCode.Unauthorized, anonymousResponse.StatusCode);

        using var regional = await LoginAdminAppAsync(app, "elena@example.com", "password123");
        var regionalResponse = await regional.Client.GetAsync("/api/admin/notifications?audienceRole=TerritoryManager");
        Assert.Equal(HttpStatusCode.Forbidden, regionalResponse.StatusCode);

        using var manager = await LoginAdminAppAsync(app, "mika@gmail.com", "Proba123!");
        var page = await manager.Client.GetFromJsonAsync<AdminNotificationListDto>(
            "/api/admin/notifications?audienceRole=TerritoryManager&includeRead=true&skip=0&take=25",
            JsonOptions);

        Assert.NotNull(page);
        Assert.Equal(2, page!.UnreadCount);
        Assert.Contains(page.Items, item => item.Id == fixture.AssignedUnreadId && !item.IsRead);
        Assert.Contains(page.Items, item => item.Id == fixture.AssignedSecondUnreadId && !item.IsRead);
        Assert.Contains(page.Items, item => item.Id == fixture.AssignedRecentReadId && item.IsRead);
        Assert.DoesNotContain(page.Items, item => item.Id == fixture.HiddenReviewReportNotificationId);
        Assert.DoesNotContain(page.Items, item => item.Id == fixture.AssignedOldReadId);
        Assert.DoesNotContain(page.Items, item => item.Id == fixture.OtherTerritoryNotificationId);
        Assert.DoesNotContain(page.Items, item => item.Id == fixture.AdminOnlyNotificationId);

        var count = await manager.Client.GetFromJsonAsync<AdminNotificationUnreadCountDto>(
            "/api/admin/notifications/unread-count?audienceRole=TerritoryManager",
            JsonOptions);
        Assert.NotNull(count);
        Assert.Equal(2, count!.UnreadCount);

        var forbiddenOtherTerritoryRead = await manager.Client.PostAsJsonAsync(
            $"/api/admin/notifications/{fixture.OtherTerritoryNotificationId}/read?audienceRole=TerritoryManager",
            new { });
        Assert.Equal(HttpStatusCode.NotFound, forbiddenOtherTerritoryRead.StatusCode);

        var markOne = await manager.Client.PostAsJsonAsync(
            $"/api/admin/notifications/{fixture.AssignedUnreadId}/read?audienceRole=TerritoryManager",
            new { });
        markOne.EnsureSuccessStatusCode();
        var markOneResult = await markOne.Content.ReadFromJsonAsync<AdminNotificationMutationDto>(JsonOptions);
        Assert.NotNull(markOneResult);
        Assert.Equal(1, markOneResult!.UnreadCount);

        var markAll = await manager.Client.PostAsJsonAsync(
            "/api/admin/notifications/mark-all-read?audienceRole=TerritoryManager",
            new { });
        markAll.EnsureSuccessStatusCode();
        var markAllResult = await markAll.Content.ReadFromJsonAsync<AdminNotificationMutationDto>(JsonOptions);
        Assert.NotNull(markAllResult);
        Assert.Equal(0, markAllResult!.UnreadCount);

        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.False(await db.AdminNotificationReadStates.AnyAsync(state =>
            state.UserId == manager.UserId &&
            state.AdminNotificationId == fixture.OtherTerritoryNotificationId));
    }

    [Fact]
    public async Task Territory_scoped_contribution_submission_creates_notification()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        var fixture = await CreateContributionNotificationFixtureAsync(app);

        PlaceContributionDetailDto created;
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var contributions = scope.ServiceProvider.GetRequiredService<IPlaceContributionService>();
            created = await contributions.CreateAsync(
                fixture.ContributorUserId,
                new UpsertPlaceContributionDto
                {
                    Type = "CreatePlace",
                    PlaceTypeId = fixture.PlaceTypeId,
                    LocationId = fixture.LocationId,
                    Name = "Territory notification cafe-free landmark",
                    Description = "A real contribution created through the contribution service for territory notification coverage.",
                    Latitude = fixture.Latitude,
                    Longitude = fixture.Longitude
                },
                cardImageBytes: null,
                coverImageBytes: null,
                galleryImageBytes: null,
                ownershipProofBytes: null,
                ownershipProofOriginalFileName: null,
                ownershipProofContentType: null);
        }

        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var notification = await db.AdminNotifications.SingleAsync(item =>
                item.AudienceRole == RoleNames.TerritoryManager &&
                item.Type == "ContributionPendingReview" &&
                item.TargetType == "Contribution" &&
                item.TargetId == created.Id);

            Assert.Equal("Region", notification.ScopeType);
            Assert.Equal(fixture.RegionId, notification.ScopeId);
            Assert.StartsWith($"/territory/regions/{fixture.RegionId}", notification.TargetRoute, StringComparison.Ordinal);

            var regionalNotification = await db.AdminNotifications.SingleAsync(item =>
                item.AudienceRole == RoleNames.RegionalManager &&
                item.Type == "ContributionPendingReview" &&
                item.TargetType == "Contribution" &&
                item.TargetId == created.Id);

            Assert.Equal("Region", regionalNotification.ScopeType);
            Assert.Equal(fixture.RegionId, regionalNotification.ScopeId);
            Assert.Equal(
                $"/request/detail/contribution/{created.Id}?regionId={fixture.RegionId}",
                regionalNotification.TargetRoute);
        }

        using var manager = await LoginAdminAppAsync(app, "mika@gmail.com", "Proba123!");
        var page = await manager.Client.GetFromJsonAsync<AdminNotificationListDto>(
            "/api/admin/notifications?audienceRole=TerritoryManager&includeRead=true&skip=0&take=25",
            JsonOptions);

        Assert.NotNull(page);
        Assert.Contains(page!.Items, item =>
            item.Type == "ContributionPendingReview" &&
            item.TargetId == created.Id &&
            item.ScopeId == fixture.RegionId);

        using var regional = await LoginAdminAppAsync(app, "elena@example.com", "password123");
        var regionalPage = await regional.Client.GetFromJsonAsync<AdminNotificationListDto>(
            $"/api/admin/notifications?audienceRole=RegionalManager&regionId={fixture.RegionId}&includeRead=true&skip=0&take=25",
            JsonOptions);

        Assert.NotNull(regionalPage);
        Assert.Contains(regionalPage!.Items, item =>
            item.Type == "ContributionPendingReview" &&
            item.TargetId == created.Id &&
            item.ScopeId == fixture.RegionId);
    }

    private static async Task<int> AddNotificationAsync(
        GeotrackApiFactory app,
        string type,
        string title,
        string message,
        string targetType,
        int targetId,
        string? metadataJson = null,
        DateTime? createdAt = null,
        int? readByUserId = null,
        DateTime? readAt = null,
        string audienceRole = RoleNames.Admin,
        string scopeType = "Global",
        int? scopeId = null,
        string? scopeLabel = null,
        string targetRoute = "/admin-dashboard")
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var notification = new AdminNotification
        {
            AudienceRole = audienceRole,
            Type = type,
            Title = title,
            Message = message,
            Severity = "Info",
            TargetType = targetType,
            TargetId = targetId,
            TargetRoute = targetRoute,
            ScopeType = scopeType,
            ScopeId = scopeId,
            ScopeLabel = scopeLabel,
            MetadataJson = metadataJson,
            CreatedAt = createdAt ?? DateTime.UtcNow
        };
        db.AdminNotifications.Add(notification);
        await db.SaveChangesAsync();

        if (readByUserId.HasValue)
        {
            db.AdminNotificationReadStates.Add(new AdminNotificationReadState
            {
                AdminNotificationId = notification.Id,
                UserId = readByUserId.Value,
                ReadAt = readAt ?? DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        return notification.Id;
    }

    private static async Task<int> AddPlaceReviewAsync(
        GeotrackApiFactory app,
        int authorUserId,
        string comment)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var placeId = await db.Places
            .OrderBy(place => place.Id)
            .Select(place => place.Id)
            .FirstAsync();

        var review = new PlaceReview
        {
            PlaceId = placeId,
            UserId = authorUserId,
            Rating = 2,
            Comment = comment,
            CreatedAt = DateTime.UtcNow
        };

        db.PlaceReviews.Add(review);
        await db.SaveChangesAsync();
        return review.Id;
    }

    private static async Task<AuthenticatedClient> LoginAdminAsync(GeotrackApiFactory app)
    {
        return await LoginAdminAppAsync(app, "zika@gmail.com", "Proba123!");
    }

    private static async Task<AuthenticatedClient> LoginAdminAppAsync(
        GeotrackApiFactory app,
        string email,
        string password)
    {
        var client = app.CreateClient();
        var response = await client.PostAsJsonAsync(
            "/api/admin/auth/login",
            new LoginRequestDto
            {
                Email = email,
                Password = password,
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

    private static async Task<RegionalManagerAssignmentFixture> CreateRegionalManagerAssignmentFixtureAsync(
        GeotrackApiFactory app)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var now = DateTime.UtcNow;
        var manager = await db.Users.SingleAsync(user => user.Email == "elena@example.com");
        var otherManager = await db.Users.SingleAsync(user => user.Email == "marko.p@example.com");
        var assignedTerritory = new Territory
        {
            Name = $"Regional Notification Assigned Territory {Guid.NewGuid():N}",
            CreatedAt = now.AddDays(-10)
        };
        var otherTerritory = new Territory
        {
            Name = $"Regional Notification Other Territory {Guid.NewGuid():N}",
            CreatedAt = now.AddDays(-10)
        };
        db.Territories.AddRange(assignedTerritory, otherTerritory);
        await db.SaveChangesAsync();

        var assignedRegion = new Region
        {
            Name = "Regional Notification Assigned Region",
            TerritoryId = assignedTerritory.Id,
            CreatedAt = now.AddDays(-9)
        };
        var otherRegion = new Region
        {
            Name = "Regional Notification Other Region",
            TerritoryId = otherTerritory.Id,
            CreatedAt = now.AddDays(-9)
        };
        db.Regions.AddRange(assignedRegion, otherRegion);
        await db.SaveChangesAsync();

        await GrantRegionalManagerCoverageAsync(db, manager, assignedRegion.Id);

        return new RegionalManagerAssignmentFixture(
            manager.Id,
            otherManager.Id,
            assignedRegion.Id,
            otherRegion.Id,
            assignedRegion.Name,
            otherRegion.Name);
    }

    private static async Task<RegionalNotificationFixture> CreateRegionalNotificationFixtureAsync(
        GeotrackApiFactory app)
    {
        var assignment = await CreateRegionalManagerAssignmentFixtureAsync(app);
        var now = DateTime.UtcNow;

        var assignedUnreadId = await AddNotificationAsync(
            app,
            "ContributionPendingReview",
            "Assigned regional contribution",
            "A request is pending in the assigned region.",
            "Contribution",
            72001,
            audienceRole: RoleNames.RegionalManager,
            scopeType: "Region",
            scopeId: assignment.AssignedRegionId,
            scopeLabel: assignment.AssignedRegionName,
            targetRoute: $"/request/detail/contribution/72001?regionId={assignment.AssignedRegionId}");
        var assignedSecondUnreadId = await AddNotificationAsync(
            app,
            "ContributionPendingReview",
            "Assigned second regional contribution",
            "Another request is pending in the assigned region.",
            "Contribution",
            72002,
            audienceRole: RoleNames.RegionalManager,
            scopeType: "Region",
            scopeId: assignment.AssignedRegionId,
            scopeLabel: assignment.AssignedRegionName,
            targetRoute: $"/request/detail/contribution/72002?regionId={assignment.AssignedRegionId}");
        var assignedRecentReadId = await AddNotificationAsync(
            app,
            "RegionCreated",
            "Recent read regional update",
            "Read notifications younger than two months remain visible.",
            "Region",
            assignment.AssignedRegionId,
            createdAt: now.AddMonths(-3),
            readByUserId: assignment.ManagerUserId,
            readAt: now.AddMonths(-1),
            audienceRole: RoleNames.RegionalManager,
            scopeType: "Region",
            scopeId: assignment.AssignedRegionId,
            scopeLabel: assignment.AssignedRegionName,
            targetRoute: $"/dashboard?regionId={assignment.AssignedRegionId}");
        var assignedOldReadId = await AddNotificationAsync(
            app,
            "RegionalManagerAssigned",
            "Old read regional assignment",
            "Read notifications older than two months are hidden.",
            "User",
            assignment.ManagerUserId,
            createdAt: now.AddMonths(-5),
            readByUserId: assignment.ManagerUserId,
            readAt: now.AddMonths(-3),
            audienceRole: RoleNames.RegionalManager,
            scopeType: "Region",
            scopeId: assignment.AssignedRegionId,
            scopeLabel: assignment.AssignedRegionName,
            targetRoute: $"/dashboard?regionId={assignment.AssignedRegionId}");
        var otherRegionNotificationId = await AddNotificationAsync(
            app,
            "ContributionPendingReview",
            "Other regional contribution",
            "This request belongs to another region.",
            "Contribution",
            72003,
            audienceRole: RoleNames.RegionalManager,
            scopeType: "Region",
            scopeId: assignment.OtherRegionId,
            scopeLabel: assignment.OtherRegionName,
            targetRoute: $"/request/detail/contribution/72003?regionId={assignment.OtherRegionId}");
        var adminOnlyNotificationId = await AddNotificationAsync(
            app,
            "System",
            "Administrator regional system row",
            "This administrator notification must not appear for regional managers.",
            "System",
            72004);
        var territoryOnlyNotificationId = await AddNotificationAsync(
            app,
            "ContributionPendingReview",
            "Territory-only contribution",
            "This territory notification must not appear for regional managers.",
            "Contribution",
            72005,
            audienceRole: RoleNames.TerritoryManager,
            scopeType: "Region",
            scopeId: assignment.AssignedRegionId,
            scopeLabel: assignment.AssignedRegionName);
        var hiddenReviewReportNotificationId = await AddNotificationAsync(
            app,
            "ReviewReported",
            "Assigned regional review report",
            "Reported review notifications are administrator-only.",
            "ReviewReport",
            72006,
            audienceRole: RoleNames.RegionalManager,
            scopeType: "Region",
            scopeId: assignment.AssignedRegionId,
            scopeLabel: assignment.AssignedRegionName);
        var hiddenOtherManagerAssignmentNotificationId = await AddNotificationAsync(
            app,
            "RegionalManagerAssigned",
            "Other manager assignment",
            "A different regional manager was assigned to this region.",
            "User",
            assignment.OtherManagerUserId,
            audienceRole: RoleNames.RegionalManager,
            scopeType: "Region",
            scopeId: assignment.AssignedRegionId,
            scopeLabel: assignment.AssignedRegionName,
            targetRoute: $"/dashboard?regionId={assignment.AssignedRegionId}");

        return new RegionalNotificationFixture(
            assignment.AssignedRegionId,
            assignment.OtherRegionId,
            assignedUnreadId,
            assignedSecondUnreadId,
            assignedRecentReadId,
            assignedOldReadId,
            otherRegionNotificationId,
            adminOnlyNotificationId,
            territoryOnlyNotificationId,
            hiddenReviewReportNotificationId,
            hiddenOtherManagerAssignmentNotificationId);
    }

    private static async Task<TerritoryNotificationFixture> CreateTerritoryNotificationFixtureAsync(
        GeotrackApiFactory app)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var now = DateTime.UtcNow;
        var manager = await db.Users.SingleAsync(user => user.Email == "mika@gmail.com");
        var assignedTerritory = new Territory
        {
            Name = $"Notification Assigned {Guid.NewGuid():N}",
            CreatedAt = now.AddDays(-10)
        };
        var otherTerritory = new Territory
        {
            Name = $"Notification Other {Guid.NewGuid():N}",
            CreatedAt = now.AddDays(-10)
        };

        db.Territories.AddRange(assignedTerritory, otherTerritory);
        await db.SaveChangesAsync();

        db.TerritoryManagerTerritories.Add(new TerritoryManagerTerritory
        {
            UserId = manager.Id,
            TerritoryId = assignedTerritory.Id,
            CreatedAt = now
        });

        var assignedRegion = new Region
        {
            Name = "Notification Assigned Region",
            TerritoryId = assignedTerritory.Id,
            CreatedAt = now.AddDays(-9)
        };
        var otherRegion = new Region
        {
            Name = "Notification Other Region",
            TerritoryId = otherTerritory.Id,
            CreatedAt = now.AddDays(-9)
        };
        db.Regions.AddRange(assignedRegion, otherRegion);
        await db.SaveChangesAsync();

        var assignedUnreadId = await AddNotificationAsync(
            app,
            "ContributionPendingReview",
            "Assigned contribution",
            "A request is pending in the assigned region.",
            "Contribution",
            71001,
            audienceRole: RoleNames.TerritoryManager,
            scopeType: "Region",
            scopeId: assignedRegion.Id,
            scopeLabel: assignedRegion.Name);
        var assignedSecondUnreadId = await AddNotificationAsync(
            app,
            "ContributionPendingReview",
            "Assigned second contribution",
            "Another content request is pending in the assigned region.",
            "Contribution",
            71002,
            audienceRole: RoleNames.TerritoryManager,
            scopeType: "Region",
            scopeId: assignedRegion.Id,
            scopeLabel: assignedRegion.Name);
        var hiddenReviewReportNotificationId = await AddNotificationAsync(
            app,
            "ReviewReported",
            "Assigned review report",
            "A reported review should not appear in territory notifications.",
            "ReviewReport",
            71006,
            audienceRole: RoleNames.TerritoryManager,
            scopeType: "Region",
            scopeId: assignedRegion.Id,
            scopeLabel: assignedRegion.Name);
        var assignedRecentReadId = await AddNotificationAsync(
            app,
            "RegionCreated",
            "Recent read region",
            "Read notifications younger than two months remain visible.",
            "Region",
            assignedRegion.Id,
            createdAt: now.AddMonths(-3),
            readByUserId: manager.Id,
            readAt: now.AddMonths(-1),
            audienceRole: RoleNames.TerritoryManager,
            scopeType: "Region",
            scopeId: assignedRegion.Id,
            scopeLabel: assignedRegion.Name);
        var assignedOldReadId = await AddNotificationAsync(
            app,
            "RegionalManagerAssigned",
            "Old read manager assignment",
            "Read notifications older than two months are hidden.",
            "User",
            71003,
            createdAt: now.AddMonths(-5),
            readByUserId: manager.Id,
            readAt: now.AddMonths(-3),
            audienceRole: RoleNames.TerritoryManager,
            scopeType: "Region",
            scopeId: assignedRegion.Id,
            scopeLabel: assignedRegion.Name);
        var otherTerritoryNotificationId = await AddNotificationAsync(
            app,
            "ContributionPendingReview",
            "Other territory contribution",
            "This request belongs to another territory.",
            "Contribution",
            71004,
            audienceRole: RoleNames.TerritoryManager,
            scopeType: "Region",
            scopeId: otherRegion.Id,
            scopeLabel: otherRegion.Name);
        var adminOnlyNotificationId = await AddNotificationAsync(
            app,
            "System",
            "Administrator only",
            "This global notification must stay Administrator-only.",
            "System",
            71005);

        return new TerritoryNotificationFixture(
            assignedUnreadId,
            assignedSecondUnreadId,
            hiddenReviewReportNotificationId,
            assignedRecentReadId,
            assignedOldReadId,
            otherTerritoryNotificationId,
            adminOnlyNotificationId);
    }

    private static async Task<ContributionNotificationFixture> CreateContributionNotificationFixtureAsync(
        GeotrackApiFactory app)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var now = DateTime.UtcNow;
        var manager = await db.Users.SingleAsync(user => user.Email == "mika@gmail.com");
        var regionalManager = await db.Users.SingleAsync(user => user.Email == "elena@example.com");
        var contributor = await db.Users.SingleAsync(user => user.Email == "marko.p@example.com");
        var destinationType = await db.DestinationTypes.OrderBy(item => item.Id).FirstAsync();
        var locationType = await db.LocationTypes.OrderBy(item => item.Id).FirstAsync();
        var placeType = await db.PlaceTypes
            .Where(item => !item.IsOwnable)
            .OrderBy(item => item.Id)
            .FirstAsync();

        var territory = new Territory
        {
            Name = $"Contribution Notification Territory {Guid.NewGuid():N}",
            CreatedAt = now.AddDays(-2)
        };
        db.Territories.Add(territory);
        await db.SaveChangesAsync();

        db.TerritoryManagerTerritories.Add(new TerritoryManagerTerritory
        {
            UserId = manager.Id,
            TerritoryId = territory.Id,
            CreatedAt = now
        });

        var region = new Region
        {
            Name = "Contribution Notification Region",
            TerritoryId = territory.Id,
            CreatedAt = now.AddDays(-2)
        };
        db.Regions.Add(region);
        await db.SaveChangesAsync();

        await GrantRegionalManagerCoverageAsync(db, regionalManager, region.Id);

        var destination = new Destination
        {
            Name = "Contribution Notification Destination",
            RegionId = region.Id,
            DestinationTypeId = destinationType.Id,
            Description = "Destination for contribution notification tests.",
            Latitude = 44.1,
            Longitude = 16.1,
            CreatedAt = now.AddDays(-1)
        };
        db.Destinations.Add(destination);
        await db.SaveChangesAsync();

        var location = new Location
        {
            Name = "Contribution Notification Location",
            DestinationId = destination.Id,
            LocationTypeId = locationType.Id,
            Latitude = 44.15,
            Longitude = 16.15,
            CreatedAt = now.AddDays(-1)
        };
        db.Locations.Add(location);
        await db.SaveChangesAsync();

        return new ContributionNotificationFixture(
            contributor.Id,
            placeType.Id,
            location.Id,
            region.Id,
            location.Latitude,
            location.Longitude);
    }

    private static async Task GrantRegionalManagerCoverageAsync(
        AppDbContext db,
        User manager,
        int regionId)
    {
        var regionalManagerRole = await db.Roles.SingleAsync(role => role.Name == RoleNames.RegionalManager);

        var hasRole = await db.UserRoles.AnyAsync(userRole =>
            userRole.UserId == manager.Id &&
            userRole.RoleId == regionalManagerRole.Id);

        if (!hasRole)
        {
            db.UserRoles.Add(new UserRole
            {
                UserId = manager.Id,
                RoleId = regionalManagerRole.Id
            });
        }

        var assignment = await db.RegionalManagerRegions.SingleOrDefaultAsync(existing =>
            existing.UserId == manager.Id &&
            existing.RegionId == regionId);

        if (assignment is null)
        {
            db.RegionalManagerRegions.Add(new RegionalManagerRegion
            {
                UserId = manager.Id,
                RegionId = regionId,
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            });
        }
        else
        {
            assignment.IsActive = true;
            assignment.UpdatedAt = DateTime.UtcNow;
        }

        await db.SaveChangesAsync();
    }

    private static async Task<HashSet<string>> ReadAdminNotificationColumnsAsync(AppDbContext db)
    {
        var columns = new HashSet<string>(StringComparer.Ordinal);

        await db.Database.OpenConnectionAsync();
        try
        {
            var connection = db.Database.GetDbConnection();
            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT column_name
                FROM information_schema.columns
                WHERE table_schema = 'public'
                  AND table_name = 'AdminNotifications';
                """;

            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                columns.Add(reader.GetString(0));
            }
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }

        return columns;
    }

    private sealed record AuthenticatedClient(HttpClient Client, int UserId) : IDisposable
    {
        public void Dispose() => Client.Dispose();
    }

    private sealed record TerritoryNotificationFixture(
        int AssignedUnreadId,
        int AssignedSecondUnreadId,
        int HiddenReviewReportNotificationId,
        int AssignedRecentReadId,
        int AssignedOldReadId,
        int OtherTerritoryNotificationId,
        int AdminOnlyNotificationId);

    private sealed record RegionalManagerAssignmentFixture(
        int ManagerUserId,
        int OtherManagerUserId,
        int AssignedRegionId,
        int OtherRegionId,
        string AssignedRegionName,
        string OtherRegionName);

    private sealed record RegionalNotificationFixture(
        int AssignedRegionId,
        int OtherRegionId,
        int AssignedUnreadId,
        int AssignedSecondUnreadId,
        int AssignedRecentReadId,
        int AssignedOldReadId,
        int OtherRegionNotificationId,
        int AdminOnlyNotificationId,
        int TerritoryOnlyNotificationId,
        int HiddenReviewReportNotificationId,
        int HiddenOtherManagerAssignmentNotificationId);

    private sealed record ContributionNotificationFixture(
        int ContributorUserId,
        int PlaceTypeId,
        int LocationId,
        int RegionId,
        double Latitude,
        double Longitude);
}
