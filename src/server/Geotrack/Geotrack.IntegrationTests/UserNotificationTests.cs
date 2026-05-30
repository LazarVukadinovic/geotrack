using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Geotrack.Application.DTOs.Auth;
using Geotrack.Application.DTOs.Notifications;
using Geotrack.Domain.Entities.Users;
using Geotrack.Infrastructure;
using Geotrack.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Geotrack.IntegrationTests;

public sealed class UserNotificationTests
{
    [Fact]
    public async Task User_notifications_are_scoped_paged_and_marked_read_for_current_user_only()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        var fixture = await CreateNotificationFixtureAsync(app);

        using var client = await LoginAsync(app, "marko.p@example.com", "password123");
        var page = await client.GetFromJsonAsync<GetUserNotificationsResponseDto>(
            "/api/notifications?skip=0&take=1");

        Assert.NotNull(page);
        Assert.Single(page!.Items);
        Assert.Equal(page.Items.Select(item => item.Id), page.Notifications.Select(item => item.Id));
        Assert.Equal(fixture.RecentOwnNotificationId, page.Items[0].Id);
        Assert.Equal(2, page.UnreadCount);
        Assert.Equal(2, page.TotalCount);
        Assert.Equal(0, page.Skip);
        Assert.Equal(1, page.Take);
        Assert.Equal(1, page.NextSkip);
        Assert.True(page.HasMore);
        Assert.DoesNotContain(page.Items, item => item.Id == fixture.OtherUserNotificationId);

        var count = await client.GetFromJsonAsync<UserNotificationUnreadCountDto>(
            "/api/notifications/unread-count");
        Assert.NotNull(count);
        Assert.Equal(2, count!.UnreadCount);

        var otherUserRead = await client.PostAsJsonAsync(
            $"/api/notifications/{fixture.OtherUserNotificationId}/read",
            new { });
        Assert.Equal(HttpStatusCode.NotFound, otherUserRead.StatusCode);

        var markOne = await client.PostAsJsonAsync(
            $"/api/notifications/{fixture.RecentOwnNotificationId}/read",
            new { });
        Assert.Equal(HttpStatusCode.NoContent, markOne.StatusCode);

        var afterOne = await client.GetFromJsonAsync<UserNotificationUnreadCountDto>(
            "/api/notifications/unread-count");
        Assert.NotNull(afterOne);
        Assert.Equal(1, afterOne!.UnreadCount);

        var markAll = await client.PostAsJsonAsync("/api/notifications/mark-all-read", new { });
        Assert.Equal(HttpStatusCode.NoContent, markAll.StatusCode);

        var afterAll = await client.GetFromJsonAsync<UserNotificationUnreadCountDto>(
            "/api/notifications/unread-count");
        Assert.NotNull(afterAll);
        Assert.Equal(0, afterAll!.UnreadCount);

        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.NotNull((await db.UserNotifications.FindAsync(fixture.OlderOwnNotificationId))!.ReadAt);
        Assert.NotNull((await db.UserNotifications.FindAsync(fixture.RecentOwnNotificationId))!.ReadAt);
        Assert.Null((await db.UserNotifications.FindAsync(fixture.OtherUserNotificationId))!.ReadAt);
    }

    private static async Task<UserNotificationFixture> CreateNotificationFixtureAsync(GeotrackApiFactory app)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var marko = await db.Users.SingleAsync(user => user.Email == "marko.p@example.com");
        var elena = await db.Users.SingleAsync(user => user.Email == "elena@example.com");
        var now = DateTime.UtcNow;

        var olderOwnNotification = new UserNotification
        {
            UserId = marko.Id,
            Type = "contribution_approved",
            Title = "Contribution approved",
            Message = "Your contribution has been approved and published.",
            RelatedEntityType = "contribution",
            RelatedEntityId = 1001,
            ActionUrl = "/places/1001",
            CreatedAt = now.AddMinutes(-10)
        };

        var recentOwnNotification = new UserNotification
        {
            UserId = marko.Id,
            Type = "contribution_approved",
            Title = "Contribution approved",
            Message = "Your contribution has been approved and published.",
            RelatedEntityType = "contribution",
            RelatedEntityId = 1002,
            ActionUrl = "/places/1002",
            CreatedAt = now.AddMinutes(-1)
        };

        var otherUserNotification = new UserNotification
        {
            UserId = elena.Id,
            Type = "contribution_approved",
            Title = "Contribution approved",
            Message = "Your contribution has been approved and published.",
            RelatedEntityType = "contribution",
            RelatedEntityId = 2001,
            ActionUrl = "/places/2001",
            CreatedAt = now
        };

        db.UserNotifications.AddRange(olderOwnNotification, recentOwnNotification, otherUserNotification);
        await db.SaveChangesAsync();

        return new UserNotificationFixture(
            olderOwnNotification.Id,
            recentOwnNotification.Id,
            otherUserNotification.Id);
    }

    private static async Task<HttpClient> LoginAsync(
        GeotrackApiFactory app,
        string email,
        string password)
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

    private sealed record UserNotificationFixture(
        int OlderOwnNotificationId,
        int RecentOwnNotificationId,
        int OtherUserNotificationId);
}
