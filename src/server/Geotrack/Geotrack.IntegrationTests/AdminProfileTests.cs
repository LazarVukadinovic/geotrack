using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Geotrack.Application.DTOs.Admin;
using Geotrack.Application.DTOs.Auth;
using Geotrack.Application.DTOs.Users;
using Geotrack.Application.Security;
using Geotrack.Domain.Entities.Core;
using Geotrack.Domain.Entities.Users;
using Geotrack.Infrastructure;
using Geotrack.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Geotrack.IntegrationTests;

public sealed class AdminProfileTests
{
    private static readonly DateTime FirstAssignedAt = new(2024, 1, 5, 9, 30, 0, DateTimeKind.Utc);
    private static readonly DateTime SecondAssignedAt = new(2024, 2, 10, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime TerritoryAssignedAt = new(2024, 3, 15, 8, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime AdminCreatedAt = new(2023, 11, 20, 10, 15, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Admin_profile_requires_authentication_and_admin_app_role()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        await PrepareRegionalManagerAsync(app);

        using var anonymousClient = app.CreateClient();
        var anonymous = await anonymousClient.GetAsync("/api/admin/profile");
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);

        using var nonRegionalClient = app.CreateClient();
        var loginResponse = await nonRegionalClient.PostAsJsonAsync(
            "/api/auth/login",
            new LoginRequestDto
            {
                Email = "marko.p@example.com",
                Password = "password123",
            });
        loginResponse.EnsureSuccessStatusCode();

        var session = await loginResponse.Content.ReadFromJsonAsync<LoginResponseDto>();
        Assert.NotNull(session);
        nonRegionalClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", session!.AccessToken);

        var forbidden = await nonRegionalClient.GetAsync("/api/admin/profile");
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
    }

    [Fact]
    public async Task Admin_profile_returns_shared_user_fields_roles_regions_and_member_since()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        var setup = await PrepareRegionalManagerAsync(app);

        using var client = app.CreateClient();
        await LoginRegionalManagerAsync(client);

        var profile = await client.GetFromJsonAsync<AdminProfileResponseDto>("/api/admin/profile");

        Assert.NotNull(profile);
        Assert.Equal(setup.UserId, profile!.User.Id);
        Assert.Equal("Elena", profile.User.FirstName);
        Assert.Equal("Rossi", profile.User.LastName);
        Assert.Equal("elena@example.com", profile.User.Email);
        Assert.Equal("Existing admin bio", profile.User.ShortBio);
        Assert.Contains(RoleNames.RegionalManager, profile.Roles);
        Assert.Equal(RoleNames.RegionalManager, profile.Role.RoleCode);
        Assert.Equal("Regional Manager", profile.Role.RoleDisplayName);
        Assert.Equal("Region", profile.Assignment.AssignmentType);
        Assert.Equal("Regional Manager Assignment", profile.Assignment.SectionTitle);
        Assert.Equal("Assigned Region", profile.Assignment.Label);
        Assert.Equal(2, profile.RegionalManager.AssignedRegions.Count);
        Assert.Equal(FirstAssignedAt, profile.RegionalManager.MemberSince);
        Assert.Equal(FirstAssignedAt, profile.Assignment.MemberSince);
        Assert.Equal(2, profile.Assignment.AssignedItems.Count);
        AssertEmailNotificationsUnavailable(profile.Preferences);

        var expectedRegionNames = setup.AssignedRegions.Select(region => region.Name).OrderBy(name => name).ToList();
        var actualRegionNames = profile.RegionalManager.AssignedRegions.Select(region => region.Name).OrderBy(name => name).ToList();
        Assert.Equal(expectedRegionNames, actualRegionNames);
    }

    [Fact]
    public async Task Admin_profile_returns_territory_manager_role_and_assigned_territory()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        var setup = await PrepareTerritoryManagerAsync(app);

        using var client = app.CreateClient();
        await LoginTerritoryManagerAsync(client);

        var profile = await client.GetFromJsonAsync<AdminProfileResponseDto>("/api/admin/profile");

        Assert.NotNull(profile);
        Assert.Equal(setup.UserId, profile!.User.Id);
        Assert.Equal("Mika", profile.User.FirstName);
        Assert.Equal("Mikic", profile.User.LastName);
        Assert.Equal("mika@gmail.com", profile.User.Email);
        Assert.Equal("Territory profile bio", profile.User.ShortBio);
        Assert.Contains(RoleNames.TerritoryManager, profile.Roles);
        Assert.Equal(RoleNames.TerritoryManager, profile.Role.RoleCode);
        Assert.Equal("Territory Manager", profile.Role.RoleDisplayName);
        Assert.Equal("Territory", profile.Assignment.AssignmentType);
        Assert.Equal("Territory Manager Assignment", profile.Assignment.SectionTitle);
        Assert.Equal("Assigned Territory", profile.Assignment.Label);
        Assert.NotEqual("Assigned Region", profile.Assignment.Label);
        Assert.Equal(setup.AssignedTerritory.Name, profile.Assignment.DisplayValue);
        Assert.Equal(TerritoryAssignedAt, profile.Assignment.MemberSince);

        var assignedItem = Assert.Single(profile.Assignment.AssignedItems);
        Assert.Equal(setup.AssignedTerritory.Id, assignedItem.Id);
        Assert.Equal("Croatia", assignedItem.Name);
        Assert.Equal("Croatia", assignedItem.DisplayName);
        Assert.Empty(profile.RegionalManager.AssignedRegions);
        AssertEmailNotificationsUnavailable(profile.Preferences);
    }

    [Fact]
    public async Task Admin_profile_returns_administrator_system_access_scope()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        var userId = await PrepareAdministratorAsync(app);

        using var client = app.CreateClient();
        await LoginAdministratorAsync(client);

        var profile = await client.GetFromJsonAsync<AdminProfileResponseDto>("/api/admin/profile");

        Assert.NotNull(profile);
        Assert.Equal(userId, profile!.User.Id);
        Assert.Equal("Zika", profile.User.FirstName);
        Assert.Equal("Admin", profile.User.LastName);
        Assert.Equal("zika@gmail.com", profile.User.Email);
        Assert.Equal("Administrator profile bio", profile.User.ShortBio);
        Assert.Contains(RoleNames.Admin, profile.Roles);
        Assert.Equal(RoleNames.Admin, profile.Role.RoleCode);
        Assert.Equal("Administrator", profile.Role.RoleDisplayName);
        Assert.Equal("System", profile.Assignment.AssignmentType);
        Assert.Equal("System Access", profile.Assignment.SectionTitle);
        Assert.Equal("Scope", profile.Assignment.Label);
        Assert.Equal("Platform-wide", profile.Assignment.DisplayValue);
        Assert.Equal("Full access", profile.Assignment.AccessLevel);
        Assert.Equal(AdminCreatedAt, profile.Assignment.MemberSince);
        Assert.Empty(profile.Assignment.AssignedItems);
        Assert.Empty(profile.RegionalManager.AssignedRegions);
        AssertEmailNotificationsUnavailable(profile.Preferences);
    }

    [Fact]
    public async Task Admin_profile_returns_content_moderator_content_moderation_scope()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        var userId = await PrepareContentModeratorAsync(app);

        using var client = app.CreateClient();
        await LoginContentModeratorAsync(client);

        var profile = await client.GetFromJsonAsync<AdminProfileResponseDto>("/api/admin/profile");

        Assert.NotNull(profile);
        Assert.Equal(userId, profile!.User.Id);
        Assert.Equal("Content", profile.User.FirstName);
        Assert.Equal("Moderator", profile.User.LastName);
        Assert.Equal("moderator@gmail.com", profile.User.Email);
        Assert.Contains(RoleNames.ContentModerator, profile.Roles);
        Assert.Equal(RoleNames.ContentModerator, profile.Role.RoleCode);
        Assert.Equal("Content Moderator", profile.Role.RoleDisplayName);
        Assert.Equal("ContentModeration", profile.Assignment.AssignmentType);
        Assert.Equal("Content Moderation Access", profile.Assignment.SectionTitle);
        Assert.Equal("Scope", profile.Assignment.Label);
        Assert.Equal("Content moderation", profile.Assignment.DisplayValue);
        Assert.Equal("Review moderation", profile.Assignment.AccessLevel);
        Assert.Equal(AdminCreatedAt, profile.Assignment.MemberSince);
        Assert.Empty(profile.Assignment.AssignedItems);
        Assert.Empty(profile.RegionalManager.AssignedRegions);
        AssertEmailNotificationsUnavailable(profile.Preferences);
    }

    [Fact]
    public async Task Admin_profile_update_only_changes_editable_profile_and_preference_fields()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        var setup = await PrepareRegionalManagerAsync(app);

        using var client = app.CreateClient();
        await LoginRegionalManagerAsync(client);

        var updateResponse = await client.PutAsJsonAsync(
            "/api/admin/profile",
            new
            {
                firstName = "Updated",
                lastName = "Manager",
                shortBio = "Updated bio",
                interfaceLanguage = "sr",
                timezone = "Europe/Belgrade",
                emailRequestsEnabled = false,
                emailNewReviewEnabled = true,
                email = "attacker@example.com",
                roles = new[] { "Admin" },
                assignedRegions = Array.Empty<object>(),
            });
        updateResponse.EnsureSuccessStatusCode();

        var updated = await updateResponse.Content.ReadFromJsonAsync<AdminProfileResponseDto>();
        Assert.NotNull(updated);
        Assert.Equal("Updated", updated!.User.FirstName);
        Assert.Equal("Manager", updated.User.LastName);
        Assert.Equal("Updated bio", updated.User.ShortBio);
        Assert.Equal("elena@example.com", updated.User.Email);
        Assert.Equal("sr", updated.Preferences.InterfaceLanguage);
        Assert.Equal("Europe/Belgrade", updated.Preferences.Timezone);
        AssertEmailNotificationsUnavailable(updated.Preferences);
        Assert.Contains(RoleNames.RegionalManager, updated.Roles);
        Assert.Equal("Assigned Region", updated.Assignment.Label);
        Assert.Equal(2, updated.RegionalManager.AssignedRegions.Count);

        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = await db.Users
            .Include(existing => existing.UserRoles)
                .ThenInclude(userRole => userRole.Role)
            .Include(existing => existing.RegionalManagerRegions)
            .SingleAsync(existing => existing.Id == setup.UserId);

        Assert.Equal("elena@example.com", user.Email);
        Assert.False(user.EmailNotificationsEnabled);
        Assert.False(user.EmailRequestsEnabled);
        Assert.False(user.EmailNewReviewEnabled);
        Assert.Contains(user.UserRoles, userRole => userRole.Role.Name == RoleNames.RegionalManager);
        Assert.Equal(setup.AssignedRegions.Select(region => region.Id).OrderBy(id => id), user.RegionalManagerRegions.Select(assignment => assignment.RegionId).OrderBy(id => id));

        var reloaded = await client.GetFromJsonAsync<AdminProfileResponseDto>("/api/admin/profile");
        Assert.NotNull(reloaded);
        Assert.Equal("Updated bio", reloaded!.User.ShortBio);
        AssertEmailNotificationsUnavailable(reloaded.Preferences);
    }

    [Fact]
    public async Task Administrator_profile_update_keeps_email_role_and_system_access_read_only()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        var userId = await PrepareAdministratorAsync(app);

        using var client = app.CreateClient();
        await LoginAdministratorAsync(client);

        var updateResponse = await client.PutAsJsonAsync(
            "/api/admin/profile",
            new
            {
                firstName = "Updated",
                lastName = "Administrator",
                shortBio = "Updated admin bio",
                interfaceLanguage = "sr",
                timezone = "Europe/Belgrade",
                emailRequestsEnabled = false,
                emailNewReviewEnabled = true,
                email = "attacker@example.com",
                role = new { roleCode = "RegionalManager", roleDisplayName = "Regional Manager" },
                assignment = new { label = "Assigned Region", displayValue = "Tampered" },
            });
        updateResponse.EnsureSuccessStatusCode();

        var updated = await updateResponse.Content.ReadFromJsonAsync<AdminProfileResponseDto>();
        Assert.NotNull(updated);
        Assert.Equal("Updated", updated!.User.FirstName);
        Assert.Equal("Administrator", updated.User.LastName);
        Assert.Equal("Updated admin bio", updated.User.ShortBio);
        Assert.Equal("zika@gmail.com", updated.User.Email);
        Assert.Equal("sr", updated.Preferences.InterfaceLanguage);
        Assert.Equal("Europe/Belgrade", updated.Preferences.Timezone);
        AssertEmailNotificationsUnavailable(updated.Preferences);
        Assert.Contains(RoleNames.Admin, updated.Roles);
        Assert.Equal(RoleNames.Admin, updated.Role.RoleCode);
        Assert.Equal("Administrator", updated.Role.RoleDisplayName);
        Assert.Equal("Scope", updated.Assignment.Label);
        Assert.Equal("Platform-wide", updated.Assignment.DisplayValue);
        Assert.Equal("Full access", updated.Assignment.AccessLevel);

        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = await db.Users
            .Include(existing => existing.UserRoles)
                .ThenInclude(userRole => userRole.Role)
            .SingleAsync(existing => existing.Id == userId);

        Assert.Equal("zika@gmail.com", user.Email);
        Assert.False(user.EmailNotificationsEnabled);
        Assert.False(user.EmailRequestsEnabled);
        Assert.False(user.EmailNewReviewEnabled);
        Assert.Contains(user.UserRoles, userRole => userRole.Role.Name == RoleNames.Admin);
        Assert.Equal("Updated", user.FirstName);
        Assert.Equal("Administrator", user.LastName);
        Assert.Equal("Updated admin bio", user.ShortBio);
    }

    [Fact]
    public async Task Territory_admin_profile_update_does_not_change_role_or_assignment()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        var setup = await PrepareTerritoryManagerAsync(app);

        using var client = app.CreateClient();
        await LoginTerritoryManagerAsync(client);

        var updateResponse = await client.PutAsJsonAsync(
            "/api/admin/profile",
            new
            {
                firstName = "Updated",
                lastName = "Territory",
                shortBio = "Updated territory bio",
                interfaceLanguage = "sr",
                timezone = "Europe/Belgrade",
                emailRequestsEnabled = false,
                emailNewReviewEnabled = true,
                role = new { roleCode = "Admin", roleDisplayName = "Admin" },
                assignment = new { label = "Assigned Region", displayValue = "Tampered" },
            });
        updateResponse.EnsureSuccessStatusCode();

        var updated = await updateResponse.Content.ReadFromJsonAsync<AdminProfileResponseDto>();
        Assert.NotNull(updated);
        Assert.Equal("Updated", updated!.User.FirstName);
        Assert.Equal("Territory", updated.User.LastName);
        Assert.Equal("Updated territory bio", updated.User.ShortBio);
        Assert.Equal("mika@gmail.com", updated.User.Email);
        Assert.Contains(RoleNames.TerritoryManager, updated.Roles);
        Assert.Equal(RoleNames.TerritoryManager, updated.Role.RoleCode);
        Assert.Equal("Territory Manager", updated.Role.RoleDisplayName);
        Assert.Equal("Assigned Territory", updated.Assignment.Label);
        Assert.Equal(setup.AssignedTerritory.Name, updated.Assignment.DisplayValue);
        Assert.Equal(TerritoryAssignedAt, updated.Assignment.MemberSince);
        AssertEmailNotificationsUnavailable(updated.Preferences);

        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var assignment = await db.TerritoryManagerTerritories.SingleAsync(existing =>
            existing.UserId == setup.UserId &&
            existing.TerritoryId == setup.AssignedTerritory.Id);
        Assert.Equal(TerritoryAssignedAt, assignment.CreatedAt);
    }

    [Fact]
    public async Task Admin_profile_avatar_upload_validates_and_updates_shared_user_avatar()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        var setup = await PrepareRegionalManagerAsync(app);

        using var client = app.CreateClient();
        await LoginRegionalManagerAsync(client);

        var invalidType = await client.PostAsync(
            "/api/admin/profile/avatar",
            BuildMultipart(new byte[] { 0x47, 0x49, 0x46, 0x38, 0x39, 0x61 }, "image/gif", "avatar.gif"));
        Assert.Equal(HttpStatusCode.BadRequest, invalidType.StatusCode);

        var tooLarge = await client.PostAsync(
            "/api/admin/profile/avatar",
            BuildMultipart(new byte[(2 * 1024 * 1024) + 1], "image/png", "avatar.png"));
        Assert.Equal(HttpStatusCode.BadRequest, tooLarge.StatusCode);

        var upload = await client.PostAsync(
            "/api/admin/profile/avatar",
            BuildMultipart(ValidPngBytes(), "image/png", "avatar.png"));
        upload.EnsureSuccessStatusCode();

        var avatar = await upload.Content.ReadFromJsonAsync<UpdateAvatarResponseDto>();
        Assert.NotNull(avatar);
        Assert.StartsWith("/media/uploads/avatars/", avatar!.AvatarUrl);
        Assert.NotNull(avatar.AvatarVersion);
        Assert.NotNull(avatar.UpdatedAt);

        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var userAvatar = await db.Users
                .Where(user => user.Id == setup.UserId)
                .Select(user => user.AvatarUrl)
                .SingleAsync();
            Assert.Equal(avatar.AvatarUrl, userAvatar);
        }

        var sharedUserProfile = await client.GetFromJsonAsync<GetMeResponseDto>("/api/user/me");
        Assert.NotNull(sharedUserProfile);
        Assert.Equal(avatar.AvatarUrl, sharedUserProfile!.AvatarUrl);
        Assert.Equal(avatar.AvatarVersion, sharedUserProfile.AvatarVersion);

        var remove = await client.DeleteAsync("/api/admin/profile/avatar");
        remove.EnsureSuccessStatusCode();

        var removedAvatar = await remove.Content.ReadFromJsonAsync<UpdateAvatarResponseDto>();
        Assert.NotNull(removedAvatar);
        Assert.Equal(string.Empty, removedAvatar!.AvatarUrl);
        Assert.Null(removedAvatar.AvatarVersion);

        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var userAvatar = await db.Users
                .Where(user => user.Id == setup.UserId)
                .Select(user => user.AvatarUrl)
                .SingleAsync();
            Assert.Null(userAvatar);
        }
    }

    [Fact]
    public async Task Administrator_profile_avatar_upload_and_remove_updates_shared_user_avatar()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        var userId = await PrepareAdministratorAsync(app);

        using var client = app.CreateClient();
        await LoginAdministratorAsync(client);

        var upload = await client.PostAsync(
            "/api/admin/profile/avatar",
            BuildMultipart(ValidPngBytes(), "image/png", "admin-avatar.png"));
        upload.EnsureSuccessStatusCode();

        var avatar = await upload.Content.ReadFromJsonAsync<UpdateAvatarResponseDto>();
        Assert.NotNull(avatar);
        Assert.StartsWith("/media/uploads/avatars/", avatar!.AvatarUrl);
        Assert.NotNull(avatar.AvatarVersion);

        var sharedUserProfile = await client.GetFromJsonAsync<GetMeResponseDto>("/api/user/me");
        Assert.NotNull(sharedUserProfile);
        Assert.Equal(avatar.AvatarUrl, sharedUserProfile!.AvatarUrl);
        Assert.Equal(avatar.AvatarVersion, sharedUserProfile.AvatarVersion);

        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var storedAvatar = await db.Users
                .Where(user => user.Id == userId)
                .Select(user => user.AvatarUrl)
                .SingleAsync();
            Assert.Equal(avatar.AvatarUrl, storedAvatar);
        }

        var remove = await client.DeleteAsync("/api/admin/profile/avatar");
        remove.EnsureSuccessStatusCode();

        var removedAvatar = await remove.Content.ReadFromJsonAsync<UpdateAvatarResponseDto>();
        Assert.NotNull(removedAvatar);
        Assert.Equal(string.Empty, removedAvatar!.AvatarUrl);
        Assert.Null(removedAvatar.AvatarVersion);
    }

    [Fact]
    public async Task User_avatar_upload_is_visible_to_admin_profile_and_auth_current_user()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        var setup = await PrepareRegionalManagerAsync(app);

        using var client = app.CreateClient();
        await LoginRegionalManagerAsync(client);

        var upload = await client.PutAsync(
            "/api/user/me/avatar",
            BuildMultipart(ValidPngBytes(), "image/png", "phone-avatar.png"));
        upload.EnsureSuccessStatusCode();

        var uploaded = await upload.Content.ReadFromJsonAsync<UpdateAvatarResponseDto>();
        Assert.NotNull(uploaded);
        Assert.StartsWith("/media/uploads/avatars/", uploaded!.AvatarUrl);
        Assert.NotNull(uploaded.AvatarVersion);

        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var storedAvatar = await db.Users
                .Where(user => user.Id == setup.UserId)
                .Select(user => user.AvatarUrl)
                .SingleAsync();
            Assert.Equal(uploaded.AvatarUrl, storedAvatar);
        }

        var adminProfile = await client.GetFromJsonAsync<AdminProfileResponseDto>("/api/admin/profile");
        Assert.NotNull(adminProfile);
        Assert.Equal(uploaded.AvatarUrl, adminProfile!.User.AvatarUrl);
        Assert.Equal(uploaded.AvatarVersion, adminProfile.User.AvatarVersion);

        var adminCurrentUser = await client.GetFromJsonAsync<AdminAuthUserDto>("/api/admin/auth/me");
        Assert.NotNull(adminCurrentUser);
        Assert.Equal(uploaded.AvatarUrl, adminCurrentUser!.AvatarUrl);
        Assert.Equal(uploaded.AvatarVersion, adminCurrentUser.AvatarVersion);
    }

    private static async Task<AdminLoginResponseDto> LoginRegionalManagerAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync(
            "/api/admin/auth/login",
            new LoginRequestDto
            {
                Email = "elena@example.com",
                Password = "password123",
            });
        response.EnsureSuccessStatusCode();

        var session = await response.Content.ReadFromJsonAsync<AdminLoginResponseDto>();
        Assert.NotNull(session);
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", session!.AccessToken);

        return session;
    }

    private static async Task<AdminLoginResponseDto> LoginAdministratorAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync(
            "/api/admin/auth/login",
            new LoginRequestDto
            {
                Email = "zika@gmail.com",
                Password = "Proba123!",
            });
        response.EnsureSuccessStatusCode();

        var session = await response.Content.ReadFromJsonAsync<AdminLoginResponseDto>();
        Assert.NotNull(session);
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", session!.AccessToken);

        return session;
    }

    private static async Task<AdminLoginResponseDto> LoginContentModeratorAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync(
            "/api/admin/auth/login",
            new LoginRequestDto
            {
                Email = "moderator@gmail.com",
                Password = "Proba123!",
            });
        response.EnsureSuccessStatusCode();

        var session = await response.Content.ReadFromJsonAsync<AdminLoginResponseDto>();
        Assert.NotNull(session);
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", session!.AccessToken);

        return session;
    }

    private static async Task<AdminLoginResponseDto> LoginTerritoryManagerAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync(
            "/api/admin/auth/login",
            new LoginRequestDto
            {
                Email = "mika@gmail.com",
                Password = "Proba123!",
            });
        response.EnsureSuccessStatusCode();

        var session = await response.Content.ReadFromJsonAsync<AdminLoginResponseDto>();
        Assert.NotNull(session);
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", session!.AccessToken);

        return session;
    }

    private static void AssertEmailNotificationsUnavailable(AdminProfilePreferencesDto preferences)
    {
        Assert.False(preferences.EmailNotificationsAvailable);
        Assert.False(preferences.EmailRequestsEnabled);
        Assert.False(preferences.EmailNewReviewEnabled);
    }

    private static async Task<(int UserId, List<Region> AssignedRegions)> PrepareRegionalManagerAsync(
        GeotrackApiFactory app)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var user = await db.Users
            .Include(existing => existing.UserRoles)
            .SingleAsync(existing => existing.Email == "elena@example.com");
        var role = await db.Roles.SingleAsync(existing => existing.Name == RoleNames.RegionalManager);

        if (!user.UserRoles.Any(userRole => userRole.RoleId == role.Id))
        {
            user.UserRoles.Add(new UserRole
            {
                UserId = user.Id,
                RoleId = role.Id,
            });
        }

        user.FirstName = "Elena";
        user.LastName = "Rossi";
        user.ShortBio = "Existing admin bio";
        user.InterfaceLanguage = "en-US";
        user.Timezone = "UTC";
        user.EmailRequestsEnabled = true;
        user.EmailNewReviewEnabled = false;

        var regions = await EnsureTwoRegionsAsync(db);
        var existingAssignments = await db.RegionalManagerRegions
            .Where(assignment => assignment.UserId == user.Id)
            .ToListAsync();
        db.RegionalManagerRegions.RemoveRange(existingAssignments);

        db.RegionalManagerRegions.AddRange(
            new RegionalManagerRegion
            {
                UserId = user.Id,
                RegionId = regions[0].Id,
                IsActive = true,
                CreatedAt = FirstAssignedAt,
            },
            new RegionalManagerRegion
            {
                UserId = user.Id,
                RegionId = regions[1].Id,
                IsActive = true,
                CreatedAt = SecondAssignedAt,
            });

        await db.SaveChangesAsync();

        return (user.Id, regions);
    }

    private static async Task<(int UserId, Territory AssignedTerritory)> PrepareTerritoryManagerAsync(
        GeotrackApiFactory app)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var user = await db.Users
            .Include(existing => existing.UserRoles)
            .SingleAsync(existing => existing.Email == "mika@gmail.com");
        var role = await db.Roles.SingleAsync(existing => existing.Name == RoleNames.TerritoryManager);

        if (!user.UserRoles.Any(userRole => userRole.RoleId == role.Id))
        {
            user.UserRoles.Add(new UserRole
            {
                UserId = user.Id,
                RoleId = role.Id,
            });
        }

        if (!await db.TerritoryManagers.AnyAsync(existing => existing.UserId == user.Id))
        {
            db.TerritoryManagers.Add(new TerritoryManager
            {
                UserId = user.Id,
            });
        }

        user.FirstName = "Mika";
        user.LastName = "Mikic";
        user.ShortBio = "Territory profile bio";
        user.InterfaceLanguage = "en-US";
        user.Timezone = "UTC";
        user.EmailRequestsEnabled = true;
        user.EmailNewReviewEnabled = false;

        var territory = await db.Territories.SingleAsync(existing => existing.Name == "Croatia");
        var existingAssignments = await db.TerritoryManagerTerritories
            .Where(assignment => assignment.UserId == user.Id)
            .ToListAsync();
        db.TerritoryManagerTerritories.RemoveRange(existingAssignments);

        db.TerritoryManagerTerritories.Add(new TerritoryManagerTerritory
        {
            UserId = user.Id,
            TerritoryId = territory.Id,
            CreatedAt = TerritoryAssignedAt,
        });

        await db.SaveChangesAsync();

        return (user.Id, territory);
    }

    private static async Task<int> PrepareAdministratorAsync(GeotrackApiFactory app)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var user = await db.Users
            .Include(existing => existing.UserRoles)
            .SingleAsync(existing => existing.Email == "zika@gmail.com");
        var role = await db.Roles.SingleAsync(existing => existing.Name == RoleNames.Admin);

        if (!user.UserRoles.Any(userRole => userRole.RoleId == role.Id))
        {
            user.UserRoles.Add(new UserRole
            {
                UserId = user.Id,
                RoleId = role.Id,
            });
        }

        user.FirstName = "Zika";
        user.LastName = "Admin";
        user.ShortBio = "Administrator profile bio";
        user.InterfaceLanguage = "en-US";
        user.Timezone = "UTC";
        user.EmailRequestsEnabled = true;
        user.EmailNewReviewEnabled = false;
        user.CreatedAt = AdminCreatedAt;

        await db.SaveChangesAsync();

        return user.Id;
    }

    private static async Task<int> PrepareContentModeratorAsync(GeotrackApiFactory app)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var user = await db.Users
            .Include(existing => existing.UserRoles)
            .SingleAsync(existing => existing.Email == "moderator@gmail.com");
        var role = await db.Roles.SingleAsync(existing => existing.Name == RoleNames.ContentModerator);

        if (!user.UserRoles.Any(userRole => userRole.RoleId == role.Id))
        {
            user.UserRoles.Add(new UserRole
            {
                UserId = user.Id,
                RoleId = role.Id,
            });
        }

        user.FirstName = "Content";
        user.LastName = "Moderator";
        user.ShortBio = "Content moderator profile bio";
        user.InterfaceLanguage = "en-US";
        user.Timezone = "UTC";
        user.EmailRequestsEnabled = true;
        user.EmailNewReviewEnabled = true;
        user.CreatedAt = AdminCreatedAt;

        await db.SaveChangesAsync();

        return user.Id;
    }

    private static async Task<List<Region>> EnsureTwoRegionsAsync(AppDbContext db)
    {
        var regions = await db.Regions
            .OrderBy(region => region.Id)
            .Take(2)
            .ToListAsync();

        if (regions.Count >= 2)
            return regions;

        var territory = await db.Territories.OrderBy(existing => existing.Id).FirstAsync();
        while (regions.Count < 2)
        {
            var region = new Region
            {
                Name = "Profile Test Region " + Guid.NewGuid().ToString("N")[..8],
                TerritoryId = territory.Id,
                CreatedAt = DateTime.UtcNow,
            };
            db.Regions.Add(region);
            await db.SaveChangesAsync();
            regions.Add(region);
        }

        return regions;
    }

    private static MultipartFormDataContent BuildMultipart(byte[] bytes, string contentType, string fileName)
    {
        var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(bytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        content.Add(fileContent, "avatar", fileName);
        return content;
    }

    private static byte[] ValidPngBytes()
    {
        return new byte[]
        {
            0x89, 0x50, 0x4E, 0x47,
            0x0D, 0x0A, 0x1A, 0x0A,
            0x00, 0x00, 0x00, 0x0D,
        };
    }
}
