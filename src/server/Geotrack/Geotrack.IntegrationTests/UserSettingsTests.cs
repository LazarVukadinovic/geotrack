using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Geotrack.Application.DTOs.Auth;
using Geotrack.Application.DTOs.Users;
using Geotrack.IntegrationTests.Infrastructure;
using Xunit;

namespace Geotrack.IntegrationTests;

public sealed class UserSettingsTests
{
    [Fact]
    public async Task User_settings_flow_persists_notification_preferences_and_support_requests()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        using var client = await LoginAsync(app, "password123");

        var currentNotifications = await client.GetFromJsonAsync<GetNotificationSettingsResponseDto>("/api/user/me/notifications");
        Assert.NotNull(currentNotifications);
        Assert.True(currentNotifications.GeneralNotifications);
        Assert.True(currentNotifications.TravelRecommendations);
        Assert.True(currentNotifications.NearbyEvents);
        Assert.True(currentNotifications.CollectionUpdates);
        Assert.False(currentNotifications.EmailNotifications);

        var me = await client.GetFromJsonAsync<GetMeResponseDto>("/api/user/me");
        Assert.NotNull(me);
        Assert.True(me.AnalyticsEnabled);

        var currentAnalytics = await client.GetFromJsonAsync<GetAnalyticsSettingsResponseDto>("/api/user/me/analytics");
        Assert.NotNull(currentAnalytics);
        Assert.True(currentAnalytics.AnalyticsEnabled);

        var disableAnalyticsResponse = await client.PutAsJsonAsync(
            "/api/user/me/analytics",
            new UpdateAnalyticsSettingsRequestDto
            {
                AnalyticsEnabled = false,
            });
        disableAnalyticsResponse.EnsureSuccessStatusCode();

        var disabledAnalytics = await disableAnalyticsResponse.Content.ReadFromJsonAsync<UpdateAnalyticsSettingsResponseDto>();
        Assert.NotNull(disabledAnalytics);
        Assert.False(disabledAnalytics.AnalyticsEnabled);

        var persistedDisabledAnalytics = await client.GetFromJsonAsync<GetAnalyticsSettingsResponseDto>("/api/user/me/analytics");
        Assert.NotNull(persistedDisabledAnalytics);
        Assert.False(persistedDisabledAnalytics.AnalyticsEnabled);

        var enableAnalyticsResponse = await client.PutAsJsonAsync(
            "/api/user/me/analytics",
            new UpdateAnalyticsSettingsRequestDto
            {
                AnalyticsEnabled = true,
            });
        enableAnalyticsResponse.EnsureSuccessStatusCode();

        var persistedEnabledAnalytics = await client.GetFromJsonAsync<GetAnalyticsSettingsResponseDto>("/api/user/me/analytics");
        Assert.NotNull(persistedEnabledAnalytics);
        Assert.True(persistedEnabledAnalytics.AnalyticsEnabled);

        var updateResponse = await client.PutAsJsonAsync(
            "/api/user/me/notifications",
            new UpdateNotificationSettingsRequestDto
            {
                GeneralNotifications = true,
                TravelRecommendations = false,
                NearbyEvents = true,
                CollectionUpdates = false,
                EmailNotifications = true,
            });
        updateResponse.EnsureSuccessStatusCode();

        var updatedNotifications = await updateResponse.Content.ReadFromJsonAsync<UpdateNotificationSettingsResponseDto>();
        Assert.NotNull(updatedNotifications);
        Assert.False(updatedNotifications.TravelRecommendations);
        Assert.False(updatedNotifications.CollectionUpdates);
        Assert.True(updatedNotifications.EmailNotifications);

        var persistedNotifications = await client.GetFromJsonAsync<GetNotificationSettingsResponseDto>("/api/user/me/notifications");
        Assert.NotNull(persistedNotifications);
        Assert.False(persistedNotifications.TravelRecommendations);
        Assert.False(persistedNotifications.CollectionUpdates);
        Assert.True(persistedNotifications.EmailNotifications);

        var supportResponse = await client.PostAsJsonAsync(
            "/api/user/me/support-requests",
            new CreateSupportRequestRequestDto
            {
                Category = "location-and-maps",
                Subject = "Nearby results look off",
                Message = "The nearby list stayed on the wrong city after I switched destinations in the app.",
            });
        supportResponse.EnsureSuccessStatusCode();

        var createdSupportRequest = await supportResponse.Content.ReadFromJsonAsync<CreateSupportRequestResponseDto>();
        Assert.NotNull(createdSupportRequest);
        Assert.StartsWith("GT-", createdSupportRequest.ReferenceCode);
        Assert.Equal("location-and-maps", createdSupportRequest.Category);
        Assert.Equal("Open", createdSupportRequest.Status);

        var supportRequests = await client.GetFromJsonAsync<GetSupportRequestsResponseDto>("/api/user/me/support-requests");
        Assert.NotNull(supportRequests);
        Assert.Contains(supportRequests.Requests, request => request.Id == createdSupportRequest.Id);
    }

    [Fact]
    public async Task User_settings_flow_allows_password_change_for_the_current_user()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        using var client = await LoginAsync(app, "password123");

        var changeResponse = await client.PutAsJsonAsync(
            "/api/user/me/password",
            new ChangePasswordRequestDto
            {
                CurrentPassword = "password123",
                NewPassword = "newPassword123!",
            });
        changeResponse.EnsureSuccessStatusCode();

        using var oldPasswordClient = app.CreateClient();
        var oldPasswordLogin = await oldPasswordClient.PostAsJsonAsync(
            "/api/auth/login",
            new LoginRequestDto
            {
                Email = "marko.p@example.com",
                Password = "password123",
                RememberMe = false,
            });
        Assert.Equal(HttpStatusCode.Unauthorized, oldPasswordLogin.StatusCode);

        using var newPasswordClient = await LoginAsync(app, "newPassword123!");
        var meResponse = await newPasswordClient.GetAsync("/api/user/me");
        meResponse.EnsureSuccessStatusCode();
    }

    private static async Task<HttpClient> LoginAsync(GeotrackApiFactory app, string password)
    {
        var client = app.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/auth/login",
            new LoginRequestDto
            {
                Email = "marko.p@example.com",
                Password = password,
                RememberMe = false,
            });
        response.EnsureSuccessStatusCode();

        var login = await response.Content.ReadFromJsonAsync<LoginResponseDto>();
        Assert.NotNull(login);

        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", login.AccessToken);

        return client;
    }
}
