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

    [Fact]
    public async Task Admin_profile_requires_authentication_and_regional_manager_role()
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
        Assert.Equal(2, profile.RegionalManager.AssignedRegions.Count);
        Assert.Equal(FirstAssignedAt, profile.RegionalManager.MemberSince);

        var expectedRegionNames = setup.AssignedRegions.Select(region => region.Name).OrderBy(name => name).ToList();
        var actualRegionNames = profile.RegionalManager.AssignedRegions.Select(region => region.Name).OrderBy(name => name).ToList();
        Assert.Equal(expectedRegionNames, actualRegionNames);
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
        Assert.False(updated.Preferences.EmailRequestsEnabled);
        Assert.True(updated.Preferences.EmailNewReviewEnabled);
        Assert.Contains(RoleNames.RegionalManager, updated.Roles);
        Assert.Equal(2, updated.RegionalManager.AssignedRegions.Count);

        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = await db.Users
            .Include(existing => existing.UserRoles)
                .ThenInclude(userRole => userRole.Role)
            .Include(existing => existing.RegionalManagerRegions)
            .SingleAsync(existing => existing.Id == setup.UserId);

        Assert.Equal("elena@example.com", user.Email);
        Assert.Contains(user.UserRoles, userRole => userRole.Role.Name == RoleNames.RegionalManager);
        Assert.Equal(setup.AssignedRegions.Select(region => region.Id).OrderBy(id => id), user.RegionalManagerRegions.Select(assignment => assignment.RegionId).OrderBy(id => id));

        var reloaded = await client.GetFromJsonAsync<AdminProfileResponseDto>("/api/admin/profile");
        Assert.NotNull(reloaded);
        Assert.Equal("Updated bio", reloaded!.User.ShortBio);
        Assert.False(reloaded.Preferences.EmailRequestsEnabled);
        Assert.True(reloaded.Preferences.EmailNewReviewEnabled);
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

        var remove = await client.DeleteAsync("/api/admin/profile/avatar");
        remove.EnsureSuccessStatusCode();

        var removedAvatar = await remove.Content.ReadFromJsonAsync<UpdateAvatarResponseDto>();
        Assert.NotNull(removedAvatar);
        Assert.Equal(string.Empty, removedAvatar!.AvatarUrl);

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
