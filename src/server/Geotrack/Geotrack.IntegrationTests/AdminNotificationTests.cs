using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Geotrack.Application.DTOs.Admin;
using Geotrack.Application.DTOs.Auth;
using Geotrack.Application.DTOs.Reviews;
using Geotrack.Application.DTOs.Users;
using Geotrack.Domain.Entities.Reviews;
using Geotrack.Domain.Entities.Users;
using Geotrack.Infrastructure;
using Geotrack.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Geotrack.IntegrationTests;

public sealed class AdminNotificationTests
{
    private const string UserPassword = "Password123!";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

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
            item.Type == "SupportTicketCreated" &&
            item.TargetType == "SupportTicket" &&
            item.TargetId == created!.Id);

        Assert.Equal("New support report", notification.Title);
        Assert.Contains("Location and maps", notification.Message, StringComparison.Ordinal);
        Assert.Equal($"/admin-support-tickets?ticketId={created.Id}", notification.TargetRoute);
    }

    [Fact]
    public async Task Review_report_creation_creates_admin_notification()
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
            item.Type == "ReviewReported" &&
            item.TargetType == "ReviewReport" &&
            item.TargetId == report.ReportId);

        Assert.Equal("New reported review", notification.Title);
        Assert.Contains("spam", notification.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal($"/admin-reviews?status=reported&highlight=Place%3A{reviewId}", notification.TargetRoute);
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
        DateTime? readAt = null)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var notification = new AdminNotification
        {
            AudienceRole = "Admin",
            Type = type,
            Title = title,
            Message = message,
            Severity = "Info",
            TargetType = targetType,
            TargetId = targetId,
            TargetRoute = "/admin-dashboard",
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
}
