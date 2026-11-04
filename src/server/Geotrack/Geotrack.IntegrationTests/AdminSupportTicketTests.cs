using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Geotrack.Application.DTOs.Admin;
using Geotrack.Application.DTOs.Auth;
using Geotrack.Application.DTOs.Users;
using Geotrack.Domain.Entities.Users;
using Geotrack.Infrastructure;
using Geotrack.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Geotrack.IntegrationTests;

public sealed class AdminSupportTicketTests
{
    private const string UserPassword = "Password123!";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task User_support_submission_is_persisted_and_visible_to_admin_only()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        var token = $"support-submit-{Guid.NewGuid():N}";

        using var anonymous = app.CreateClient();
        var anonymousCreate = await anonymous.PostAsJsonAsync(
            "/api/user/me/support-requests",
            new CreateSupportRequestRequestDto
            {
                Category = "location-and-maps",
                Subject = $"{token} anonymous attempt",
                Message = "Anonymous support creation should follow product auth rules."
            });
        Assert.Equal(HttpStatusCode.Unauthorized, anonymousCreate.StatusCode);

        using var user = await RegisterAndLoginAsync(app, $"{token}@example.com", "Support Reporter");
        var createResponse = await user.Client.PostAsJsonAsync(
            "/api/user/me/support-requests",
            new CreateSupportRequestRequestDto
            {
                Category = "location-and-maps",
                Subject = $"{token} map issue",
                Message = "The map stayed centered on the wrong city after I changed destination.",
                PageUrl = "/settings/contact-support",
                UserAgent = "GeoTrack integration test browser",
                Browser = "Chrome",
                Platform = "Windows"
            });
        createResponse.EnsureSuccessStatusCode();

        var created = await createResponse.Content.ReadFromJsonAsync<CreateSupportRequestResponseDto>(JsonOptions);
        Assert.NotNull(created);
        Assert.StartsWith("GT-", created!.ReferenceCode);
        Assert.Equal("location-and-maps", created.Category);
        Assert.Equal("Open", created.Status);

        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var stored = await db.SupportRequests.SingleAsync(ticket => ticket.Id == created.Id);
            Assert.Equal(user.UserId, stored.UserId);
            Assert.Equal(SupportRequestStatuses.Open, stored.Status);
            Assert.Equal(SupportRequestPriorities.Normal, stored.Priority);
            Assert.Equal("/settings/contact-support", stored.PageUrl);
            Assert.Equal("GeoTrack integration test browser", stored.UserAgent);
        }

        var nonAdminList = await user.Client.GetAsync("/api/admin/support-tickets");
        Assert.Equal(HttpStatusCode.Forbidden, nonAdminList.StatusCode);

        var anonymousAdminList = await anonymous.GetAsync("/api/admin/support-tickets");
        Assert.Equal(HttpStatusCode.Unauthorized, anonymousAdminList.StatusCode);

        using var admin = await LoginAdminAsync(app);
        var listResponse = await admin.Client.GetAsync(
            $"/api/admin/support-tickets?status=open&category=location-and-maps&search={Uri.EscapeDataString(token)}&sort=newest&skip=0&take=25");
        listResponse.EnsureSuccessStatusCode();

        var listContent = await listResponse.Content.ReadAsStringAsync();
        Assert.DoesNotContain("passwordHash", listContent, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("securityStamp", listContent, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("refreshToken", listContent, StringComparison.OrdinalIgnoreCase);

        var page = JsonSerializer.Deserialize<AdminSupportTicketListResponseDto>(listContent, JsonOptions);
        Assert.NotNull(page);
        var item = Assert.Single(page!.Items);
        Assert.Equal(created.Id, item.TicketId);
        Assert.Equal("Support Reporter", item.Reporter.DisplayName);
        Assert.Equal("Open", item.Status);
        Assert.True(item.Permissions.CanView);

        var detail = await admin.Client.GetFromJsonAsync<AdminSupportTicketDetailDto>(
            $"/api/admin/support-tickets/{created.Id}",
            JsonOptions);
        Assert.NotNull(detail);
        Assert.Equal(created.Id, detail!.TicketId);
        Assert.Equal("The map stayed centered on the wrong city after I changed destination.", detail.Message);
        Assert.Equal("GeoTrack integration test browser", detail.UserAgent);
        Assert.Null(detail.InternalNote);

        var userSupportHistoryContent = await user.Client.GetStringAsync("/api/user/me/support-requests");
        Assert.DoesNotContain("internalNote", userSupportHistoryContent, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("resolvedByUserId", userSupportHistoryContent, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("assignedAdminId", userSupportHistoryContent, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Admin_support_ticket_filters_search_sort_and_pagination_use_real_data()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        var token = $"support-page-{Guid.NewGuid():N}";
        await AddSupportTicketsAsync(app, token, 26, "privacy-and-data", SupportRequestStatuses.Open);
        var resolvedId = await AddSupportTicketAsync(
            app,
            $"{token} resolved account issue",
            "account-access",
            SupportRequestStatuses.Resolved,
            SupportRequestPriorities.High,
            resolvedAt: DateTime.UtcNow.AddMinutes(-2));
        _ = resolvedId;

        using var admin = await LoginAdminAsync(app);
        var firstPage = await admin.Client.GetFromJsonAsync<AdminSupportTicketListResponseDto>(
            $"/api/admin/support-tickets?status=open&category=privacy-and-data&search={Uri.EscapeDataString(token)}&sort=newest&skip=0&take=25",
            JsonOptions);

        Assert.NotNull(firstPage);
        Assert.Equal(26, firstPage!.TotalCount);
        Assert.Equal(25, firstPage.Items.Count);
        Assert.True(firstPage.HasMore);
        Assert.Equal(25, firstPage.NextSkip);
        Assert.All(firstPage.Items, ticket =>
        {
            Assert.Equal("Open", ticket.Status);
            Assert.Equal("privacy-and-data", ticket.Category);
            Assert.Contains(token, ticket.Subject, StringComparison.OrdinalIgnoreCase);
        });

        var secondPage = await admin.Client.GetFromJsonAsync<AdminSupportTicketListResponseDto>(
            $"/api/admin/support-tickets?status=open&category=privacy-and-data&search={Uri.EscapeDataString(token)}&sort=newest&skip=25&take=25",
            JsonOptions);

        Assert.NotNull(secondPage);
        Assert.Single(secondPage!.Items);
        Assert.False(secondPage.HasMore);
        Assert.Null(secondPage.NextSkip);

        var resolvedPage = await admin.Client.GetFromJsonAsync<AdminSupportTicketListResponseDto>(
            $"/api/admin/support-tickets?status=resolved&category=account-access&search={Uri.EscapeDataString(token)}&sort=updatedDesc&skip=0&take=25",
            JsonOptions);

        Assert.NotNull(resolvedPage);
        var resolved = Assert.Single(resolvedPage!.Items);
        Assert.Equal("Resolved", resolved.Status);
        Assert.Equal("account-access", resolved.Category);
        Assert.Equal("High", resolved.Priority);

        var priorityPage = await admin.Client.GetFromJsonAsync<AdminSupportTicketListResponseDto>(
            $"/api/admin/support-tickets?status=all&priority=high&search={Uri.EscapeDataString(token)}&sort=priorityDesc&skip=0&take=25",
            JsonOptions);

        Assert.NotNull(priorityPage);
        Assert.Contains(priorityPage!.Items, ticket => ticket.Priority == "High");
    }

    [Fact]
    public async Task Admin_status_actions_notes_and_dashboard_counts_are_persisted()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        var token = $"support-actions-{Guid.NewGuid():N}";
        var firstId = await AddSupportTicketAsync(app, $"{token} first", "account-access", SupportRequestStatuses.Open);
        var secondId = await AddSupportTicketAsync(app, $"{token} second", "location-and-maps", SupportRequestStatuses.Open);

        using var admin = await LoginAdminAsync(app);

        var invalidInProgressResponse = await admin.Client.PostAsJsonAsync(
            $"/api/admin/support-tickets/{firstId}/status",
            new AdminSupportTicketStatusUpdateRequestDto { Status = "InProgress" });
        Assert.Equal(HttpStatusCode.BadRequest, invalidInProgressResponse.StatusCode);

        var resolveResponse = await admin.Client.PostAsJsonAsync(
            $"/api/admin/support-tickets/{firstId}/resolve",
            new AdminSupportTicketStatusUpdateRequestDto { InternalNote = "Resolved after checking the account state." });
        resolveResponse.EnsureSuccessStatusCode();

        var resolved = await resolveResponse.Content.ReadFromJsonAsync<AdminSupportTicketDetailDto>(JsonOptions);
        Assert.NotNull(resolved);
        Assert.Equal("Resolved", resolved!.Status);
        Assert.NotNull(resolved.ResolvedAt);
        Assert.Equal(admin.UserId, resolved.ResolvedBy?.UserId);
        Assert.Equal("Resolved after checking the account state.", resolved.InternalNote);

        var dismissResponse = await admin.Client.PostAsJsonAsync(
            $"/api/admin/support-tickets/{secondId}/dismiss",
            new AdminSupportTicketStatusUpdateRequestDto { InternalNote = "Duplicate report." });
        dismissResponse.EnsureSuccessStatusCode();

        var dismissed = await dismissResponse.Content.ReadFromJsonAsync<AdminSupportTicketDetailDto>(JsonOptions);
        Assert.NotNull(dismissed);
        Assert.Equal("Dismissed", dismissed!.Status);
        Assert.NotNull(dismissed.ResolvedAt);
        Assert.Equal(admin.UserId, dismissed.ResolvedBy?.UserId);

        var reopenResponse = await admin.Client.PostAsJsonAsync(
            $"/api/admin/support-tickets/{secondId}/reopen",
            new AdminSupportTicketStatusUpdateRequestDto { InternalNote = "Reopened for another look." });
        reopenResponse.EnsureSuccessStatusCode();

        var reopened = await reopenResponse.Content.ReadFromJsonAsync<AdminSupportTicketDetailDto>(JsonOptions);
        Assert.NotNull(reopened);
        Assert.Equal("Open", reopened!.Status);
        Assert.Null(reopened.ResolvedAt);
        Assert.Null(reopened.ResolvedBy);
        Assert.Equal("Reopened for another look.", reopened.InternalNote);

        var noteResponse = await admin.Client.PostAsJsonAsync(
            $"/api/admin/support-tickets/{secondId}/note",
            new AdminSupportTicketNoteRequestDto { InternalNote = "Final internal note." });
        noteResponse.EnsureSuccessStatusCode();

        var noted = await noteResponse.Content.ReadFromJsonAsync<AdminSupportTicketDetailDto>(JsonOptions);
        Assert.NotNull(noted);
        Assert.Equal("Final internal note.", noted!.InternalNote);

        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var first = await db.SupportRequests.SingleAsync(ticket => ticket.Id == firstId);
            var second = await db.SupportRequests.SingleAsync(ticket => ticket.Id == secondId);

            Assert.Equal(SupportRequestStatuses.Resolved, first.Status);
            Assert.Equal(admin.UserId, first.ResolvedByUserId);
            Assert.NotNull(first.ResolvedAt);
            Assert.Equal(SupportRequestStatuses.Open, second.Status);
            Assert.Null(second.ResolvedAt);
            Assert.Null(second.ResolvedByUserId);
            Assert.Equal("Final internal note.", second.InternalNote);
        }

        var dashboard = await admin.Client.GetFromJsonAsync<AdminDashboardDto>("/api/admin/dashboard", JsonOptions);
        var allTickets = await admin.Client.GetFromJsonAsync<AdminSupportTicketListResponseDto>(
            "/api/admin/support-tickets?status=all&skip=0&take=25",
            JsonOptions);

        Assert.NotNull(dashboard);
        Assert.NotNull(allTickets);
        Assert.Equal(allTickets!.Counts.OpenCount, dashboard!.Stats.OpenSupportTicketsCount);
        Assert.Equal(allTickets.Counts.ResolvedCount, dashboard.Stats.ResolvedSupportTicketsCount);
        Assert.Equal(allTickets.Counts.DismissedCount, dashboard.Stats.DismissedSupportTicketsCount);
        Assert.Equal(allTickets.Counts.OpenCount, dashboard.Stats.UnresolvedSupportTicketsCount);
        Assert.Equal(dashboard.Stats.UnresolvedSupportTicketsCount, dashboard.Badges.SupportReportsCount);
    }

    private static async Task<IReadOnlyList<int>> AddSupportTicketsAsync(
        GeotrackApiFactory app,
        string token,
        int count,
        string category,
        string status)
    {
        var ids = new List<int>(count);

        for (var index = 0; index < count; index++)
        {
            var priority = index == 0 ? SupportRequestPriorities.Critical : SupportRequestPriorities.Normal;
            var id = await AddSupportTicketAsync(
                app,
                $"{token} ticket {index:D2}",
                category,
                status,
                priority,
                createdAt: DateTime.UtcNow.AddMinutes(index));
            ids.Add(id);
        }

        return ids;
    }

    private static async Task<int> AddSupportTicketAsync(
        GeotrackApiFactory app,
        string subject,
        string category,
        string status,
        string priority = SupportRequestPriorities.Normal,
        DateTime? createdAt = null,
        DateTime? resolvedAt = null)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var userId = await db.Users
            .OrderBy(user => user.Id)
            .Select(user => user.Id)
            .FirstAsync();

        var now = createdAt ?? DateTime.UtcNow;
        var ticket = new SupportRequest
        {
            UserId = userId,
            Category = category,
            Subject = subject,
            Message = $"{subject} message body",
            Status = status,
            Priority = priority,
            PageUrl = "/settings/contact-support",
            CreatedAt = now,
            UpdatedAt = resolvedAt,
            ResolvedAt = resolvedAt
        };

        db.SupportRequests.Add(ticket);
        await db.SaveChangesAsync();
        return ticket.Id;
    }

    private static async Task<AuthenticatedClient> LoginAdminAsync(GeotrackApiFactory app)
    {
        var client = app.CreateClient();
        var response = await client.PostAsJsonAsync(
            "/api/admin/auth/login",
            new LoginRequestDto
            {
                Email = "zika@gmail.com",
                Password = "TEST-only-Fixture-9284!",
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
