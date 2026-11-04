using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Geotrack.Application.DTOs.Admin;
using Geotrack.Application.DTOs.Auth;
using Geotrack.Application.Security;
using Geotrack.Domain.Entities.Core;
using Geotrack.Domain.Entities.Reviews;
using Geotrack.Domain.Entities.Users;
using Geotrack.Infrastructure;
using Geotrack.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Coordinate = NetTopologySuite.Geometries.Coordinate;
using Geometry = NetTopologySuite.Geometries.Geometry;
using GeometryFactory = NetTopologySuite.Geometries.GeometryFactory;
using Polygon = NetTopologySuite.Geometries.Polygon;
using PrecisionModel = NetTopologySuite.Geometries.PrecisionModel;

namespace Geotrack.IntegrationTests;

public sealed class AdminUserManagementTests
{
    private const string UserPassword = "Password123!";
    private const string BoundaryGeometryPropertyName = "BoundaryGeometry";
    private const string BoundaryDisplayGeometryPropertyName = "BoundaryDisplayGeometry";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly GeometryFactory GeometryFactory = new(new PrecisionModel(), 4326);

    [Fact]
    public async Task User_management_requires_admin_and_returns_paginated_real_counts()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();

        using var anonymous = app.CreateClient();
        var anonymousResponse = await anonymous.GetAsync("/api/admin/users");
        Assert.Equal(HttpStatusCode.Unauthorized, anonymousResponse.StatusCode);

        using var normalUser = await LoginUserAsync(app, "marko.p@example.com", "password123");
        var nonAdminResponse = await normalUser.Client.GetAsync("/api/admin/users");
        Assert.Equal(HttpStatusCode.Forbidden, nonAdminResponse.StatusCode);

        using var admin = await LoginAdminAsync(app, "zika@gmail.com", "TEST-only-Fixture-9284!");
        var response = await admin.Client.GetAsync("/api/admin/users?tab=app-users&skip=0&take=1&sort=newest");
        response.EnsureSuccessStatusCode();

        var content = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("passwordHash", content, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("securityStamp", content, StringComparison.OrdinalIgnoreCase);

        var page = JsonSerializer.Deserialize<AdminUserListResponseDto>(content, JsonOptions);
        var expectedCounts = await GetExpectedCountsAsync(app);

        Assert.NotNull(page);
        Assert.Single(page!.Items);
        Assert.Equal(0, page.Skip);
        Assert.Equal(1, page.Take);
        Assert.Equal(expectedCounts.AppUsersCount, page.TotalCount);
        Assert.True(page.HasMore);
        Assert.Equal(1, page.NextSkip);
        Assert.Equal(expectedCounts.AppUsersCount, page.Counts.AppUsersCount);
        Assert.Equal(expectedCounts.TerritoryManagersCount, page.Counts.TerritoryManagersCount);
        Assert.Equal(expectedCounts.ActiveCount, page.Counts.ActiveCount);
        Assert.Equal(expectedCounts.SuspendedCount, page.Counts.SuspendedCount);
    }

    [Fact]
    public async Task User_preview_requires_admin_and_returns_bounded_safe_real_account_detail()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        var target = await RegisterUserAsync(
            app,
            $"admin-users-preview-{Guid.NewGuid():N}@example.com",
            "Preview Target User");

        await AddReviewAndReportFixtureAsync(app, target.Id);
        await AddReportsMadeFixtureAsync(app, target.Id, count: 12);

        using var anonymous = app.CreateClient();
        var anonymousResponse = await anonymous.GetAsync($"/api/admin/users/{target.Id}/preview");
        Assert.Equal(HttpStatusCode.Unauthorized, anonymousResponse.StatusCode);

        using var normalUser = await LoginUserAsync(app, target.Email, UserPassword);
        var nonAdminResponse = await normalUser.Client.GetAsync($"/api/admin/users/{target.Id}/preview");
        Assert.Equal(HttpStatusCode.Forbidden, nonAdminResponse.StatusCode);

        using var admin = await LoginAdminAsync(app, "zika@gmail.com", "TEST-only-Fixture-9284!");
        var response = await admin.Client.GetAsync($"/api/admin/users/{target.Id}/preview");
        response.EnsureSuccessStatusCode();

        var content = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("passwordHash", content, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("securityStamp", content, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("refreshToken", content, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("tokenHash", content, StringComparison.OrdinalIgnoreCase);

        var preview = JsonSerializer.Deserialize<AdminUserPreviewDto>(content, JsonOptions);
        Assert.NotNull(preview);
        Assert.Equal(target.Id, preview!.User.UserId);
        Assert.Equal(target.Email, preview.User.Email);
        Assert.Contains(RoleNames.RegisteredUser, preview.User.Roles);
        Assert.Contains(preview.Roles, role => role.RoleName == RoleNames.RegisteredUser);
        Assert.Equal("App User", preview.User.AccountType);

        Assert.Equal(2, preview.AccountSummary.ReviewsCount);
        Assert.Equal(12, preview.AccountSummary.ReportsMadeCount);
        Assert.True(preview.AccountSummary.ReportedContentCount >= 2);
        Assert.True(preview.AccountSummary.PendingReportsAgainstUserCount >= 1);
        Assert.True(preview.AccountSummary.LastKnownActivityAt.HasValue);

        Assert.Equal(10, preview.ReportsMade.Items.Count);
        Assert.Equal(12, preview.ReportsMade.TotalCount);
        Assert.True(preview.ReportsMade.HasMore);
        Assert.All(preview.ReportsMade.Items, item =>
        {
            Assert.Equal(target.Id, item.ReporterUserId);
            Assert.NotEmpty(item.ContentExcerpt);
        });

        Assert.True(preview.ReportsAgainstUser.TotalCount >= 2);
        Assert.Contains(preview.ReportsAgainstUser.Items, item => item.ReviewAuthorUserId == target.Id);
        Assert.Equal(2, preview.RecentReviews.TotalCount);
        Assert.Contains(preview.RecentReviews.Items, item => item.TargetType == "Place");
        Assert.True(preview.ModerationHistory.TotalCount > 0);
        Assert.True(preview.Permissions.CanAssignTerritoryManager);
        Assert.True(preview.Permissions.CanSuspend);
        Assert.False(preview.Permissions.CanUnsuspend);
        Assert.True(preview.Permissions.CanDelete);
    }

    [Fact]
    public async Task User_list_supports_search_status_sort_and_review_report_counts()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        var prefix = $"admin-users-{Guid.NewGuid():N}";
        var high = await RegisterUserAsync(app, $"{prefix}-high@example.com", "High Review User");
        var low = await RegisterUserAsync(app, $"{prefix}-low@example.com", "Low Review User");

        await AddReviewAndReportFixtureAsync(app, high.Id);
        await SuspendDirectlyAsync(app, low.Id);

        using var admin = await LoginAdminAsync(app, "zika@gmail.com", "TEST-only-Fixture-9284!");

        var byReviews = await admin.Client.GetFromJsonAsync<AdminUserListResponseDto>(
            $"/api/admin/users?tab=app-users&search={Uri.EscapeDataString(prefix)}&sort=reviewsDesc&skip=0&take=25");
        Assert.NotNull(byReviews);
        Assert.Equal(2, byReviews!.TotalCount);
        var highRow = byReviews.Items.First();
        Assert.Equal(high.Email, highRow.Email);
        Assert.Equal(2, highRow.ReviewsCount);
        Assert.Equal(1, highRow.ReportedCount);
        Assert.Equal("Active", highRow.Status);

        var byReported = await admin.Client.GetFromJsonAsync<AdminUserListResponseDto>(
            $"/api/admin/users?tab=app-users&search={Uri.EscapeDataString(prefix)}&sort=reportedDesc&status=all&skip=0&take=25");
        Assert.NotNull(byReported);
        Assert.Equal(high.Email, byReported!.Items.First().Email);

        var suspended = await admin.Client.GetFromJsonAsync<AdminUserListResponseDto>(
            $"/api/admin/users?tab=app-users&search={Uri.EscapeDataString(low.Email)}&status=suspended&skip=0&take=25");
        Assert.NotNull(suspended);
        var suspendedRow = Assert.Single(suspended!.Items);
        Assert.Equal(low.Email, suspendedRow.Email);
        Assert.Equal("Suspended", suspendedRow.Status);
        Assert.True(suspendedRow.CanUnsuspend);

        var activeOnly = await admin.Client.GetFromJsonAsync<AdminUserListResponseDto>(
            $"/api/admin/users?tab=app-users&search={Uri.EscapeDataString(low.Email)}&status=active&skip=0&take=25");
        Assert.NotNull(activeOnly);
        Assert.Empty(activeOnly!.Items);
    }

    [Fact]
    public async Task Admin_can_suspend_unsuspend_and_soft_delete_without_self_account_mutation()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        var target = await RegisterUserAsync(
            app,
            $"admin-users-status-{Guid.NewGuid():N}@example.com",
            "Status Target User");

        using var targetSession = await LoginUserAsync(app, target.Email, UserPassword);
        using var admin = await LoginAdminAsync(app, "zika@gmail.com", "TEST-only-Fixture-9284!");

        var selfSuspend = await admin.Client.PostAsync($"/api/admin/users/{admin.UserId}/suspend", null);
        Assert.Equal(HttpStatusCode.BadRequest, selfSuspend.StatusCode);

        var selfDelete = await admin.Client.DeleteAsync($"/api/admin/users/{admin.UserId}");
        Assert.Equal(HttpStatusCode.BadRequest, selfDelete.StatusCode);

        var suspendResponse = await admin.Client.PostAsync($"/api/admin/users/{target.Id}/suspend", null);
        suspendResponse.EnsureSuccessStatusCode();
        var suspended = await suspendResponse.Content.ReadFromJsonAsync<AdminUserListItemDto>();
        Assert.NotNull(suspended);
        Assert.Equal("Suspended", suspended!.Status);
        Assert.True(suspended.CanUnsuspend);
        Assert.False(suspended.CanSuspend);

        var protectedResponse = await targetSession.Client.GetAsync("/api/user/me");
        Assert.Equal(HttpStatusCode.Forbidden, protectedResponse.StatusCode);

        using (var suspendedLoginClient = app.CreateClient())
        {
            var suspendedLogin = await suspendedLoginClient.PostAsJsonAsync(
                "/api/auth/login",
                new LoginRequestDto { Email = target.Email, Password = UserPassword });
            Assert.Equal(HttpStatusCode.Forbidden, suspendedLogin.StatusCode);
        }

        var unsuspendResponse = await admin.Client.PostAsync($"/api/admin/users/{target.Id}/unsuspend", null);
        unsuspendResponse.EnsureSuccessStatusCode();
        var active = await unsuspendResponse.Content.ReadFromJsonAsync<AdminUserListItemDto>();
        Assert.NotNull(active);
        Assert.Equal("Active", active!.Status);
        Assert.True(active.CanSuspend);

        using (var activeSession = await LoginUserAsync(app, target.Email, UserPassword))
        {
            var meResponse = await activeSession.Client.GetAsync("/api/user/me");
            meResponse.EnsureSuccessStatusCode();
        }

        var deleteResponse = await admin.Client.DeleteAsync($"/api/admin/users/{target.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var deletedUser = await db.Users.SingleAsync(user => user.Id == target.Id);
            Assert.True(deletedUser.IsDeleted);
            Assert.NotNull(deletedUser.DeletedAt);
            Assert.Equal(admin.UserId, deletedUser.DeletedByUserId);
        }

        using (var deletedLoginClient = app.CreateClient())
        {
            var deletedLogin = await deletedLoginClient.PostAsJsonAsync(
                "/api/auth/login",
                new LoginRequestDto { Email = target.Email, Password = UserPassword });
            Assert.Equal(HttpStatusCode.Unauthorized, deletedLogin.StatusCode);
        }

        var defaultList = await admin.Client.GetFromJsonAsync<AdminUserListResponseDto>(
            $"/api/admin/users?search={Uri.EscapeDataString(target.Email)}&status=all");
        Assert.NotNull(defaultList);
        Assert.Empty(defaultList!.Items);

        var deletedList = await admin.Client.GetFromJsonAsync<AdminUserListResponseDto>(
            $"/api/admin/users?search={Uri.EscapeDataString(target.Email)}&status=deleted");
        Assert.NotNull(deletedList);
        var deletedRow = Assert.Single(deletedList!.Items);
        Assert.Equal("Deleted", deletedRow.Status);
        Assert.False(deletedRow.CanDelete);
    }

    [Fact]
    public async Task Admin_can_assign_territory_manager_only_for_valid_country_boundary()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        var target = await RegisterUserAsync(
            app,
            $"admin-users-tm-{Guid.NewGuid():N}@example.com",
            "Territory Manager Candidate");
        var validTerritory = await CreateTerritoryAsync(app, withBoundary: true);
        var invalidTerritory = await CreateTerritoryAsync(app, withBoundary: false);

        using var admin = await LoginAdminAsync(app, "zika@gmail.com", "TEST-only-Fixture-9284!");

        var invalidResponse = await admin.Client.PostAsJsonAsync(
            $"/api/admin/users/{target.Id}/assign-territory-manager",
            new AdminUserAssignTerritoryManagerRequestDto { TerritoryId = invalidTerritory.Id });
        Assert.Equal(HttpStatusCode.BadRequest, invalidResponse.StatusCode);

        var assignResponse = await admin.Client.PostAsJsonAsync(
            $"/api/admin/users/{target.Id}/assign-territory-manager",
            new AdminUserAssignTerritoryManagerRequestDto { TerritoryId = validTerritory.Id });
        assignResponse.EnsureSuccessStatusCode();

        var assigned = await assignResponse.Content.ReadFromJsonAsync<AdminUserListItemDto>();
        Assert.NotNull(assigned);
        Assert.Contains(RoleNames.TerritoryManager, assigned!.Roles);
        Assert.Equal(1, assigned.AssignedTerritoryCount);
        Assert.Equal(validTerritory.Id, Assert.Single(assigned.AssignedTerritories).TerritoryId);
        Assert.False(assigned.CanAssignTerritoryManager);

        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.True(await db.UserRoles.AnyAsync(userRole =>
                userRole.UserId == target.Id &&
                userRole.Role.Name == RoleNames.TerritoryManager));
            Assert.True(await db.TerritoryManagers.AnyAsync(manager => manager.UserId == target.Id));
            Assert.True(await db.TerritoryManagerTerritories.AnyAsync(assignment =>
                assignment.UserId == target.Id &&
                assignment.TerritoryId == validTerritory.Id));
        }

        var managersTab = await admin.Client.GetFromJsonAsync<AdminUserListResponseDto>(
            $"/api/admin/users?tab=territory-managers&search={Uri.EscapeDataString(target.Email)}");
        Assert.NotNull(managersTab);
        var managerRow = Assert.Single(managersTab!.Items);
        Assert.Equal(target.Email, managerRow.Email);
        Assert.Contains(RoleNames.TerritoryManager, managerRow.Roles);
        Assert.Equal(validTerritory.Id, Assert.Single(managerRow.AssignedTerritories).TerritoryId);
    }

    private static async Task<AuthenticatedClient> LoginAdminAsync(
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

        var session = await response.Content.ReadFromJsonAsync<AdminLoginResponseDto>();
        Assert.NotNull(session);
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", session!.AccessToken);

        return new AuthenticatedClient(client, session.User.Id);
    }

    private static async Task<AuthenticatedClient> LoginUserAsync(
        GeotrackApiFactory app,
        string email,
        string password)
    {
        var client = app.CreateClient();
        var response = await client.PostAsJsonAsync(
            "/api/auth/login",
            new LoginRequestDto
            {
                Email = email,
                Password = password,
                RememberMe = false
            });
        response.EnsureSuccessStatusCode();

        var session = await response.Content.ReadFromJsonAsync<LoginResponseDto>();
        Assert.NotNull(session);
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", session!.AccessToken);

        var userId = await GetUserIdAsync(app, email);
        return new AuthenticatedClient(client, userId);
    }

    private static async Task<RegisteredUser> RegisterUserAsync(
        GeotrackApiFactory app,
        string email,
        string fullName)
    {
        using var client = app.CreateClient();
        var response = await client.PostAsJsonAsync(
            "/api/auth/register",
            new RegisterRequestDto
            {
                Email = email,
                FullName = fullName,
                Password = UserPassword
            });
        response.EnsureSuccessStatusCode();

        return new RegisteredUser(await GetUserIdAsync(app, email), email, fullName);
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

    private static async Task<AdminUserListCountsDto> GetExpectedCountsAsync(GeotrackApiFactory app)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        return new AdminUserListCountsDto
        {
            AppUsersCount = await db.Users.AsNoTracking().CountAsync(user => !user.IsDeleted),
            TerritoryManagersCount = await db.Users
                .AsNoTracking()
                .CountAsync(user =>
                    !user.IsDeleted &&
                    user.UserRoles.Any(userRole => userRole.Role.Name == RoleNames.TerritoryManager)),
            ActiveCount = await db.Users.AsNoTracking().CountAsync(user => !user.IsDeleted && !user.IsSuspended),
            SuspendedCount = await db.Users.AsNoTracking().CountAsync(user => !user.IsDeleted && user.IsSuspended),
            DeletedCount = await db.Users.AsNoTracking().CountAsync(user => user.IsDeleted)
        };
    }

    private static async Task AddReviewAndReportFixtureAsync(GeotrackApiFactory app, int authorUserId)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = DateTime.UtcNow;
        var place = await db.Places
            .Include(existing => existing.Location)
                .ThenInclude(location => location.Destination)
                    .ThenInclude(destination => destination.Region)
            .OrderBy(existing => existing.Id)
            .FirstAsync();
        var author = await db.Users.SingleAsync(user => user.Id == authorUserId);
        var pendingReporterId = await db.Users
            .Where(user => user.Email == "elena@example.com")
            .Select(user => user.Id)
            .SingleAsync();
        var dismissedReporterId = await db.Users
            .Where(user => user.Email == "mika@gmail.com")
            .Select(user => user.Id)
            .SingleAsync();

        var placeReview = new PlaceReview
        {
            PlaceId = place.Id,
            UserId = authorUserId,
            Rating = 2,
            Comment = $"Admin user management review {Guid.NewGuid():N}",
            CreatedAt = now.AddMinutes(-10)
        };
        var destinationReview = new DestinationReview
        {
            DestinationId = place.Location.DestinationId,
            UserId = authorUserId,
            Rating = 4,
            Comment = $"Admin user management destination review {Guid.NewGuid():N}",
            CreatedAt = now.AddMinutes(-9)
        };

        db.PlaceReviews.Add(placeReview);
        db.DestinationReviews.Add(destinationReview);
        await db.SaveChangesAsync();

        db.ReviewReports.Add(new ReviewReport
        {
            TargetType = ReviewReportTargetType.Place,
            ReviewId = placeReview.Id,
            PlaceReviewId = placeReview.Id,
            ReporterUserId = pendingReporterId,
            Reason = ReviewReportReason.OffensiveContent,
            Note = "Admin user management pending report",
            Status = ReviewReportStatus.Pending,
            ReviewAuthorUserId = authorUserId,
            ReviewAuthorName = author.DisplayName ?? $"{author.FirstName} {author.LastName}".Trim(),
            EntityId = place.Id,
            EntityName = place.Name,
            RegionId = place.Location.Destination.RegionId,
            RegionName = place.Location.Destination.Region.Name,
            ReviewRating = placeReview.Rating,
            ReviewCommentSnapshot = placeReview.Comment,
            ReviewCreatedAt = placeReview.CreatedAt,
            CreatedAt = now
        });
        db.ReviewReports.Add(new ReviewReport
        {
            TargetType = ReviewReportTargetType.Place,
            ReviewId = placeReview.Id,
            PlaceReviewId = placeReview.Id,
            ReporterUserId = dismissedReporterId,
            Reason = ReviewReportReason.Spam,
            Note = "Admin user management dismissed report",
            Status = ReviewReportStatus.Dismissed,
            ReviewAuthorUserId = authorUserId,
            ReviewAuthorName = author.DisplayName ?? $"{author.FirstName} {author.LastName}".Trim(),
            EntityId = place.Id,
            EntityName = place.Name,
            RegionId = place.Location.Destination.RegionId,
            RegionName = place.Location.Destination.Region.Name,
            ReviewRating = placeReview.Rating,
            ReviewCommentSnapshot = placeReview.Comment,
            ReviewCreatedAt = placeReview.CreatedAt,
            CreatedAt = now.AddMinutes(1)
        });
        await db.SaveChangesAsync();
    }

    private static async Task AddReportsMadeFixtureAsync(
        GeotrackApiFactory app,
        int reporterUserId,
        int count)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = DateTime.UtcNow;
        var place = await db.Places
            .Include(existing => existing.Location)
                .ThenInclude(location => location.Destination)
                    .ThenInclude(destination => destination.Region)
            .OrderBy(existing => existing.Id)
            .FirstAsync();
        var reviewAuthor = await db.Users
            .Where(user => user.Id != reporterUserId && user.Email == "marko.p@example.com")
            .SingleAsync();
        var reviewerUserId = await db.Users
            .Where(user => user.UserRoles.Any(userRole => userRole.Role.Name == RoleNames.Admin))
            .Select(user => user.Id)
            .FirstAsync();

        for (var index = 0; index < count; index++)
        {
            var status = (index % 3) switch
            {
                1 => ReviewReportStatus.Dismissed,
                2 => ReviewReportStatus.Actioned,
                _ => ReviewReportStatus.Pending
            };
            var reviewedAt = status == ReviewReportStatus.Pending
                ? (DateTime?)null
                : now.AddMinutes(index + 5);

            db.ReviewReports.Add(new ReviewReport
            {
                TargetType = ReviewReportTargetType.Place,
                ReviewId = 900_000 + index,
                ReporterUserId = reporterUserId,
                Reason = index % 2 == 0 ? ReviewReportReason.Spam : ReviewReportReason.FalseInformation,
                Note = $"Preview report made fixture {index}",
                Status = status,
                ReviewedByUserId = reviewedAt.HasValue ? reviewerUserId : null,
                ReviewedAt = reviewedAt,
                Decision = status == ReviewReportStatus.Actioned
                    ? ReviewReportDecision.RemoveReview
                    : status == ReviewReportStatus.Dismissed
                        ? ReviewReportDecision.KeepReview
                        : null,
                DecisionNote = reviewedAt.HasValue ? $"Preview decision {index}" : string.Empty,
                ReviewAuthorUserId = reviewAuthor.Id,
                ReviewAuthorName = reviewAuthor.DisplayName ?? $"{reviewAuthor.FirstName} {reviewAuthor.LastName}".Trim(),
                EntityId = place.Id,
                EntityName = place.Name,
                RegionId = place.Location.Destination.RegionId,
                RegionName = place.Location.Destination.Region.Name,
                ReviewRating = 1 + (index % 5),
                ReviewCommentSnapshot = $"Reported review snapshot for preview fixture {index}.",
                ReviewCreatedAt = now.AddDays(-30).AddMinutes(index),
                CreatedAt = now.AddMinutes(index)
            });
        }

        await db.SaveChangesAsync();
    }

    private static async Task SuspendDirectlyAsync(GeotrackApiFactory app, int userId)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = await db.Users.SingleAsync(existing => existing.Id == userId);
        user.IsSuspended = true;
        user.SuspendedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
    }

    private static async Task<TestTerritory> CreateTerritoryAsync(
        GeotrackApiFactory app,
        bool withBoundary)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var (countryCode, countryIso3) = await GetUnusedCountryCodesAsync(db);
        var territory = new Territory
        {
            Name = $"Admin User Territory {Guid.NewGuid():N}",
            TerritoryKind = TerritoryKinds.Country,
            CountryCode = countryCode,
            CountryIso3 = countryIso3,
            CreatedAt = DateTime.UtcNow,
            BoundaryShapeType = withBoundary ? "Polygon" : null,
            BoundarySource = withBoundary ? "IntegrationTest" : null
        };

        db.Territories.Add(territory);
        await db.SaveChangesAsync();

        if (withBoundary)
        {
            SetBoundary(db, territory, Polygon(
                (10.0, 20.0),
                (10.0, 21.0),
                (11.0, 21.0),
                (11.0, 20.0)));
            await db.SaveChangesAsync();
        }

        return new TestTerritory(territory.Id, territory.Name);
    }

    private static async Task<(string CountryCode, string CountryIso3)> GetUnusedCountryCodesAsync(AppDbContext db)
    {
        var existingIso2 = await db.Territories
            .AsNoTracking()
            .Where(territory => territory.CountryCode != null)
            .Select(territory => territory.CountryCode!)
            .ToListAsync();
        var existingIso3 = await db.Territories
            .AsNoTracking()
            .Where(territory => territory.CountryIso3 != null)
            .Select(territory => territory.CountryIso3!)
            .ToListAsync();

        for (var first = 'Q'; first <= 'Z'; first++)
        {
            for (var second = 'A'; second <= 'Z'; second++)
            {
                var iso2 = $"{first}{second}";
                var iso3 = $"{first}{second}X";
                if (!existingIso2.Contains(iso2, StringComparer.OrdinalIgnoreCase) &&
                    !existingIso3.Contains(iso3, StringComparer.OrdinalIgnoreCase))
                {
                    return (iso2, iso3);
                }
            }
        }

        throw new InvalidOperationException("Unable to allocate a unique test country code.");
    }

    private static void SetBoundary(AppDbContext db, Territory territory, Geometry? geometry)
    {
        db.Entry(territory)
            .Property<Geometry?>(BoundaryGeometryPropertyName)
            .CurrentValue = geometry;
        db.Entry(territory)
            .Property<Geometry?>(BoundaryDisplayGeometryPropertyName)
            .CurrentValue = geometry;
    }

    private static Polygon Polygon(params (double Latitude, double Longitude)[] points)
    {
        var coordinates = points
            .Select(point => new Coordinate(point.Longitude, point.Latitude))
            .ToList();

        if (!coordinates[0].Equals2D(coordinates[^1]))
        {
            coordinates.Add(new Coordinate(coordinates[0].X, coordinates[0].Y));
        }

        var polygon = GeometryFactory.CreatePolygon(coordinates.ToArray());
        polygon.SRID = 4326;
        return polygon;
    }

    private sealed record AuthenticatedClient(HttpClient Client, int UserId) : IDisposable
    {
        public void Dispose() => Client.Dispose();
    }

    private sealed record RegisteredUser(int Id, string Email, string FullName);

    private sealed record TestTerritory(int Id, string Name);
}
