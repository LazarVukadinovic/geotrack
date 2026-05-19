using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Geotrack.Application.DTOs.Admin;
using Geotrack.Application.DTOs.Auth;
using Geotrack.Application.Security;
using Geotrack.Domain.Entities.Core;
using Geotrack.Domain.Entities.Users;
using Geotrack.Infrastructure;
using Geotrack.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Geotrack.IntegrationTests;

public sealed class TerritoryUserAdministrationTests
{
    [Fact]
    public async Task Territory_manager_can_list_users_in_assigned_territory()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        var fixture = await CreateFixtureAsync(app);

        using var client = await LoginAdminAsync(app, "mika@gmail.com", "Proba123!");
        var users = await client.GetFromJsonAsync<TerritoryUserListResponseDto>(
            $"/api/admin/territory/users?territoryId={fixture.AssignedTerritoryId}&skip=0&take=8");

        Assert.NotNull(users);
        Assert.Equal(fixture.AssignedTerritoryId, users!.Territory!.Id);
        Assert.Equal(3, users.TotalCount);
        Assert.DoesNotContain(users.Items, item => item.UserId == fixture.OutOfTerritoryUserId);

        var manager = Assert.Single(users.Items, item => item.UserId == fixture.ActiveManagerUserId);
        Assert.Equal("RegionalManager", manager.RoleCode);
        Assert.Equal("Regional Manager", manager.RoleDisplayName);
        Assert.Equal("/media/uploads/avatars/territory-admin-test.png", manager.AvatarUrl);
        Assert.Single(manager.AssignedRegions, region => region.RegionId == fixture.AssignedRegionOneId);
        Assert.Equal("Active", manager.Status);
        Assert.True(manager.CanEdit);
        Assert.True(manager.CanAddRegion);
        Assert.True(manager.CanRemoveManagerRole);
        Assert.NotNull(manager.LastLoginAt);
        Assert.NotEqual("Never", manager.LastLoginDisplay);

        var viewer = Assert.Single(users.Items, item => item.UserId == fixture.ViewerUserId);
        Assert.Equal(RoleNames.RegisteredUser, viewer.RoleCode);
        Assert.Equal("Viewer", viewer.RoleDisplayName);
        Assert.Equal("Never", viewer.LastLoginDisplay);
    }

    [Fact]
    public async Task Territory_user_list_rejects_unassigned_regional_and_anonymous_callers()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        var fixture = await CreateFixtureAsync(app);
        await GrantRegionalManagerRoleAsync(app, "elena@example.com");

        using var anonymousClient = app.CreateClient();
        var anonymous = await anonymousClient.GetAsync(
            $"/api/admin/territory/users?territoryId={fixture.AssignedTerritoryId}");
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);

        using var regionalClient = await LoginAdminAsync(app, "elena@example.com", "password123");
        var regional = await regionalClient.GetAsync(
            $"/api/admin/territory/users?territoryId={fixture.AssignedTerritoryId}");
        Assert.Equal(HttpStatusCode.Forbidden, regional.StatusCode);

        using var territoryClient = await LoginAdminAsync(app, "mika@gmail.com", "Proba123!");
        var unassigned = await territoryClient.GetAsync(
            $"/api/admin/territory/users?territoryId={fixture.UnassignedTerritoryId}");
        Assert.Equal(HttpStatusCode.Forbidden, unassigned.StatusCode);
    }

    [Fact]
    public async Task Territory_user_list_supports_search_sort_and_pagination()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        var fixture = await CreateFixtureAsync(app);
        await AddPagedUsersAsync(app, fixture.AssignedRegionOneId, 10);

        using var client = await LoginAdminAsync(app, "mika@gmail.com", "Proba123!");

        var byName = await client.GetFromJsonAsync<TerritoryUserListResponseDto>(
            $"/api/admin/territory/users?territoryId={fixture.AssignedTerritoryId}&search=Active%20Manager&skip=0&take=8");
        Assert.NotNull(byName);
        Assert.Contains(byName!.Items, item => item.UserId == fixture.ActiveManagerUserId);

        var byEmail = await client.GetFromJsonAsync<TerritoryUserListResponseDto>(
            $"/api/admin/territory/users?territoryId={fixture.AssignedTerritoryId}&search={Uri.EscapeDataString(fixture.ViewerEmail)}&skip=0&take=8");
        Assert.NotNull(byEmail);
        var emailMatch = Assert.Single(byEmail!.Items);
        Assert.Equal(fixture.ViewerUserId, emailMatch.UserId);

        var byRegion = await client.GetFromJsonAsync<TerritoryUserListResponseDto>(
            $"/api/admin/territory/users?territoryId={fixture.AssignedTerritoryId}&search={Uri.EscapeDataString(fixture.AssignedRegionTwoName)}&skip=0&take=8");
        Assert.NotNull(byRegion);
        Assert.Contains(byRegion!.Items, item => item.UserId == fixture.ViewerUserId);

        var activeFirst = await client.GetFromJsonAsync<TerritoryUserListResponseDto>(
            $"/api/admin/territory/users?territoryId={fixture.AssignedTerritoryId}&sort=status_active_first&skip=0&take=20");
        Assert.NotNull(activeFirst);
        Assert.True(activeFirst!.Items.TakeWhile(item => item.IsActive).Any());
        Assert.True(activeFirst.Items.SkipWhile(item => item.IsActive).All(item => !item.IsActive));

        var firstPage = await client.GetFromJsonAsync<TerritoryUserListResponseDto>(
            $"/api/admin/territory/users?territoryId={fixture.AssignedTerritoryId}&skip=0&take=8");
        Assert.NotNull(firstPage);
        Assert.Equal(8, firstPage!.Items.Count);
        Assert.True(firstPage.HasMore);

        var secondPage = await client.GetFromJsonAsync<TerritoryUserListResponseDto>(
            $"/api/admin/territory/users?territoryId={fixture.AssignedTerritoryId}&skip=8&take=8");
        Assert.NotNull(secondPage);
        Assert.NotEmpty(secondPage!.Items);
        Assert.DoesNotContain(secondPage.Items, item => firstPage.Items.Any(first => first.UserId == item.UserId));
    }

    [Fact]
    public async Task Add_region_allows_only_regions_inside_assigned_territory_and_prevents_duplicates()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        var fixture = await CreateFixtureAsync(app);

        using var client = await LoginAdminAsync(app, "mika@gmail.com", "Proba123!");

        var add = await client.PostAsJsonAsync(
            $"/api/admin/territory/users/{fixture.ActiveManagerUserId}/regions",
            new TerritoryUserAssignRegionRequestDto
            {
                TerritoryId = fixture.AssignedTerritoryId,
                RegionId = fixture.AssignedRegionTwoId
            });
        add.EnsureSuccessStatusCode();

        var updated = await add.Content.ReadFromJsonAsync<TerritoryUserListItemDto>();
        Assert.NotNull(updated);
        Assert.Contains(updated!.AssignedRegions, region => region.RegionId == fixture.AssignedRegionTwoId);

        var duplicate = await client.PostAsJsonAsync(
            $"/api/admin/territory/users/{fixture.ActiveManagerUserId}/regions",
            new TerritoryUserAssignRegionRequestDto
            {
                TerritoryId = fixture.AssignedTerritoryId,
                RegionId = fixture.AssignedRegionTwoId
            });
        Assert.Equal(HttpStatusCode.BadRequest, duplicate.StatusCode);

        var outside = await client.PostAsJsonAsync(
            $"/api/admin/territory/users/{fixture.ActiveManagerUserId}/regions",
            new TerritoryUserAssignRegionRequestDto
            {
                TerritoryId = fixture.AssignedTerritoryId,
                RegionId = fixture.UnassignedRegionId
            });
        Assert.Equal(HttpStatusCode.Forbidden, outside.StatusCode);
    }

    [Fact]
    public async Task Add_region_promotes_viewer_by_role_name_and_available_regions_are_scoped()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        var fixture = await CreateFixtureAsync(app);

        using var client = await LoginAdminAsync(app, "mika@gmail.com", "Proba123!");

        var available = await client.GetFromJsonAsync<TerritoryUserAvailableRegionsResponseDto>(
            $"/api/admin/territory/users/{fixture.ViewerUserId}/available-regions?territoryId={fixture.AssignedTerritoryId}");
        Assert.NotNull(available);
        Assert.Contains(available!.Items, item => item.RegionId == fixture.AssignedRegionOneId);
        Assert.DoesNotContain(available.Items, item => item.RegionId == fixture.UnassignedRegionId);

        var add = await client.PostAsJsonAsync(
            $"/api/admin/territory/users/{fixture.ViewerUserId}/regions",
            new TerritoryUserAssignRegionRequestDto
            {
                TerritoryId = fixture.AssignedTerritoryId,
                RegionId = fixture.AssignedRegionOneId
            });
        add.EnsureSuccessStatusCode();

        var updated = await add.Content.ReadFromJsonAsync<TerritoryUserListItemDto>();
        Assert.NotNull(updated);
        Assert.Equal(RoleNames.RegionalManager, updated!.RoleCode);

        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.True(await db.UserRoles.AnyAsync(role =>
            role.UserId == fixture.ViewerUserId &&
            role.Role.Name == RoleNames.RegionalManager));
    }

    [Fact]
    public async Task Edit_user_updates_only_safe_profile_fields()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        var fixture = await CreateFixtureAsync(app);

        using var client = await LoginAdminAsync(app, "mika@gmail.com", "Proba123!");
        var response = await client.PutAsJsonAsync(
            $"/api/admin/territory/users/{fixture.ActiveManagerUserId}",
            new TerritoryUserUpdateRequestDto
            {
                TerritoryId = fixture.AssignedTerritoryId,
                FirstName = "Renamed",
                LastName = "Manager",
                DisplayName = "Renamed Manager"
            });
        response.EnsureSuccessStatusCode();

        var updated = await response.Content.ReadFromJsonAsync<TerritoryUserListItemDto>();
        Assert.NotNull(updated);
        Assert.Equal("Renamed Manager", updated!.DisplayName);
        Assert.Equal("Renamed", updated.FirstName);
        Assert.Equal(fixture.ActiveManagerEmail, updated.Email);

        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var persisted = await db.Users.SingleAsync(user => user.Id == fixture.ActiveManagerUserId);
        Assert.Equal(fixture.ActiveManagerEmail, persisted.Email);
    }

    [Fact]
    public async Task Remove_manager_role_is_territory_scoped_and_does_not_delete_user()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        var fixture = await CreateFixtureAsync(app, createCrossTerritoryManager: true);

        using var client = await LoginAdminAsync(app, "mika@gmail.com", "Proba123!");
        var response = await client.PostAsJsonAsync(
            $"/api/admin/territory/users/{fixture.CrossTerritoryManagerUserId}/remove-manager-role",
            new TerritoryUserRemoveManagerRoleRequestDto
            {
                TerritoryId = fixture.AssignedTerritoryId
            });
        response.EnsureSuccessStatusCode();

        var updated = await response.Content.ReadFromJsonAsync<TerritoryUserListItemDto>();
        Assert.NotNull(updated);
        Assert.Equal("Inactive", updated!.Status);
        Assert.Empty(updated.AssignedRegions);

        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var userStillExists = await db.Users.AnyAsync(user => user.Id == fixture.CrossTerritoryManagerUserId);
        Assert.True(userStillExists);

        var assignedAssignment = await db.RegionalManagerRegions.SingleAsync(assignment =>
            assignment.UserId == fixture.CrossTerritoryManagerUserId &&
            assignment.RegionId == fixture.AssignedRegionOneId);
        Assert.False(assignedAssignment.IsActive);

        var outsideAssignment = await db.RegionalManagerRegions.SingleAsync(assignment =>
            assignment.UserId == fixture.CrossTerritoryManagerUserId &&
            assignment.RegionId == fixture.UnassignedRegionId);
        Assert.True(outsideAssignment.IsActive);

        Assert.True(await db.UserRoles.AnyAsync(role =>
            role.UserId == fixture.CrossTerritoryManagerUserId &&
            role.Role.Name == RoleNames.RegionalManager));
    }

    [Fact]
    public async Task Remove_manager_role_rejects_out_of_scope_and_self_targets()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        var fixture = await CreateFixtureAsync(app);

        using var client = await LoginAdminAsync(app, "mika@gmail.com", "Proba123!");

        var outOfScope = await client.PostAsJsonAsync(
            $"/api/admin/territory/users/{fixture.OutOfTerritoryUserId}/remove-manager-role",
            new TerritoryUserRemoveManagerRoleRequestDto
            {
                TerritoryId = fixture.AssignedTerritoryId
            });
        Assert.Equal(HttpStatusCode.Forbidden, outOfScope.StatusCode);

        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var mika = await db.Users.SingleAsync(user => user.Email == "mika@gmail.com");
        var role = await db.Roles.SingleAsync(existing => existing.Name == RoleNames.RegionalManager);
        if (!await db.UserRoles.AnyAsync(userRole => userRole.UserId == mika.Id && userRole.RoleId == role.Id))
        {
            db.UserRoles.Add(new UserRole { UserId = mika.Id, RoleId = role.Id });
        }
        db.RegionalManagerRegions.Add(new RegionalManagerRegion
        {
            UserId = mika.Id,
            RegionId = fixture.AssignedRegionOneId,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var self = await client.PostAsJsonAsync(
            $"/api/admin/territory/users/{mika.Id}/remove-manager-role",
            new TerritoryUserRemoveManagerRoleRequestDto
            {
                TerritoryId = fixture.AssignedTerritoryId
            });
        Assert.Equal(HttpStatusCode.BadRequest, self.StatusCode);
    }

    [Fact]
    public async Task Successful_login_updates_last_login_at()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();

        using var _ = await LoginAdminAsync(app, "mika@gmail.com", "Proba123!");

        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = await db.Users.SingleAsync(existing => existing.Email == "mika@gmail.com");
        Assert.NotNull(user.LastLoginAt);
    }

    private static async Task<HttpClient> LoginAdminAsync(GeotrackApiFactory app, string email, string password)
    {
        var client = app.CreateClient();

        var loginResponse = await client.PostAsJsonAsync(
            "/api/admin/auth/login",
            new LoginRequestDto
            {
                Email = email,
                Password = password,
                RememberMe = false
            });
        loginResponse.EnsureSuccessStatusCode();

        var session = await loginResponse.Content.ReadFromJsonAsync<AdminLoginResponseDto>();
        Assert.NotNull(session);
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", session!.AccessToken);

        return client;
    }

    private static async Task GrantRegionalManagerRoleAsync(GeotrackApiFactory app, string email)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = await db.Users.SingleAsync(existing => existing.Email == email);
        var role = await db.Roles.SingleAsync(existing => existing.Name == RoleNames.RegionalManager);

        if (!await db.UserRoles.AnyAsync(userRole => userRole.UserId == user.Id && userRole.RoleId == role.Id))
        {
            db.UserRoles.Add(new UserRole
            {
                UserId = user.Id,
                RoleId = role.Id
            });
        }

        await db.SaveChangesAsync();
    }

    private static async Task<TerritoryUsersFixture> CreateFixtureAsync(
        GeotrackApiFactory app,
        bool createCrossTerritoryManager = false)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var suffix = Guid.NewGuid().ToString("N");
        var now = DateTime.UtcNow;

        var territoryManager = await db.Users.SingleAsync(user => user.Email == "mika@gmail.com");
        var regionalRole = await db.Roles.SingleAsync(role => role.Name == RoleNames.RegionalManager);
        var registeredRole = await db.Roles.SingleAsync(role => role.Name == RoleNames.RegisteredUser);

        var assignedTerritory = new Territory
        {
            Name = $"Territory Users Assigned {suffix}",
            CreatedAt = now.AddDays(-20)
        };
        var unassignedTerritory = new Territory
        {
            Name = $"Territory Users Out {suffix}",
            CreatedAt = now.AddDays(-20)
        };
        db.Territories.AddRange(assignedTerritory, unassignedTerritory);
        await db.SaveChangesAsync();

        db.TerritoryManagerTerritories.Add(new TerritoryManagerTerritory
        {
            UserId = territoryManager.Id,
            TerritoryId = assignedTerritory.Id,
            CreatedAt = now
        });

        var assignedRegionOne = new Region
        {
            Name = $"Assigned Region One {suffix}",
            TerritoryId = assignedTerritory.Id,
            CreatedAt = now.AddDays(-10)
        };
        var assignedRegionTwo = new Region
        {
            Name = $"Assigned Region Two {suffix}",
            TerritoryId = assignedTerritory.Id,
            CreatedAt = now.AddDays(-9)
        };
        var unassignedRegion = new Region
        {
            Name = $"Out Region {suffix}",
            TerritoryId = unassignedTerritory.Id,
            CreatedAt = now.AddDays(-8)
        };
        db.Regions.AddRange(assignedRegionOne, assignedRegionTwo, unassignedRegion);
        await db.SaveChangesAsync();

        var activeManager = await CreateUserAsync(
            db,
            $"active-manager-{suffix}@example.com",
            "Active",
            "Manager",
            "Active Manager",
            "/media/uploads/avatars/territory-admin-test.png",
            now.AddDays(-2));
        var viewer = await CreateUserAsync(
            db,
            $"viewer-{suffix}@example.com",
            "Viewer",
            "Person",
            "Viewer Person",
            null,
            null);
        var inactiveManager = await CreateUserAsync(
            db,
            $"inactive-manager-{suffix}@example.com",
            "Inactive",
            "Manager",
            "Inactive Manager",
            null,
            null);
        var outUser = await CreateUserAsync(
            db,
            $"out-manager-{suffix}@example.com",
            "Outside",
            "Manager",
            "Outside Manager",
            null,
            null);

        db.UserRoles.AddRange(
            new UserRole { UserId = activeManager.Id, RoleId = regionalRole.Id },
            new UserRole { UserId = viewer.Id, RoleId = registeredRole.Id },
            new UserRole { UserId = inactiveManager.Id, RoleId = regionalRole.Id },
            new UserRole { UserId = outUser.Id, RoleId = regionalRole.Id });

        db.RegionalManagerRegions.AddRange(
            new RegionalManagerRegion
            {
                UserId = activeManager.Id,
                RegionId = assignedRegionOne.Id,
                IsActive = true,
                CreatedAt = now.AddDays(-4)
            },
            new RegionalManagerRegion
            {
                UserId = viewer.Id,
                RegionId = assignedRegionTwo.Id,
                IsActive = true,
                CreatedAt = now.AddDays(-3)
            },
            new RegionalManagerRegion
            {
                UserId = inactiveManager.Id,
                RegionId = assignedRegionOne.Id,
                IsActive = false,
                CreatedAt = now.AddDays(-6),
                UpdatedAt = now.AddDays(-1)
            },
            new RegionalManagerRegion
            {
                UserId = outUser.Id,
                RegionId = unassignedRegion.Id,
                IsActive = true,
                CreatedAt = now.AddDays(-5)
            });

        User? crossTerritoryManager = null;
        if (createCrossTerritoryManager)
        {
            crossTerritoryManager = await CreateUserAsync(
                db,
                $"cross-manager-{suffix}@example.com",
                "Cross",
                "Manager",
                "Cross Manager",
                null,
                now.AddDays(-1));
            db.UserRoles.Add(new UserRole { UserId = crossTerritoryManager.Id, RoleId = regionalRole.Id });
            db.RegionalManagerRegions.AddRange(
                new RegionalManagerRegion
                {
                    UserId = crossTerritoryManager.Id,
                    RegionId = assignedRegionOne.Id,
                    IsActive = true,
                    CreatedAt = now.AddDays(-4)
                },
                new RegionalManagerRegion
                {
                    UserId = crossTerritoryManager.Id,
                    RegionId = unassignedRegion.Id,
                    IsActive = true,
                    CreatedAt = now.AddDays(-2)
                });
        }

        await db.SaveChangesAsync();

        return new TerritoryUsersFixture(
            assignedTerritory.Id,
            unassignedTerritory.Id,
            assignedRegionOne.Id,
            assignedRegionTwo.Id,
            assignedRegionTwo.Name,
            unassignedRegion.Id,
            activeManager.Id,
            activeManager.Email,
            viewer.Id,
            viewer.Email,
            inactiveManager.Id,
            outUser.Id,
            crossTerritoryManager?.Id ?? 0);
    }

    private static async Task<User> CreateUserAsync(
        AppDbContext db,
        string email,
        string firstName,
        string lastName,
        string displayName,
        string? avatarUrl,
        DateTime? lastLoginAt)
    {
        var user = new User
        {
            Username = email,
            Email = email,
            FirstName = firstName,
            LastName = lastName,
            DisplayName = displayName,
            AvatarUrl = avatarUrl,
            PasswordHash = string.Empty,
            PhoneNumber = string.Empty,
            CreatedAt = DateTime.UtcNow.AddDays(-30),
            LastLoginAt = lastLoginAt
        };

        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user;
    }

    private static async Task AddPagedUsersAsync(GeotrackApiFactory app, int regionId, int count)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var role = await db.Roles.SingleAsync(existing => existing.Name == RoleNames.RegionalManager);
        var suffix = Guid.NewGuid().ToString("N");

        for (var i = 0; i < count; i++)
        {
            var user = await CreateUserAsync(
                db,
                $"paged-user-{i:D2}-{suffix}@example.com",
                "Paged",
                $"User {i:D2}",
                $"Paged User {i:D2}",
                null,
                null);

            db.UserRoles.Add(new UserRole
            {
                UserId = user.Id,
                RoleId = role.Id
            });

            db.RegionalManagerRegions.Add(new RegionalManagerRegion
            {
                UserId = user.Id,
                RegionId = regionId,
                IsActive = true,
                CreatedAt = DateTime.UtcNow.AddMinutes(-i)
            });
        }

        await db.SaveChangesAsync();
    }

    private sealed record TerritoryUsersFixture(
        int AssignedTerritoryId,
        int UnassignedTerritoryId,
        int AssignedRegionOneId,
        int AssignedRegionTwoId,
        string AssignedRegionTwoName,
        int UnassignedRegionId,
        int ActiveManagerUserId,
        string ActiveManagerEmail,
        int ViewerUserId,
        string ViewerEmail,
        int InactiveManagerUserId,
        int OutOfTerritoryUserId,
        int CrossTerritoryManagerUserId);
}
