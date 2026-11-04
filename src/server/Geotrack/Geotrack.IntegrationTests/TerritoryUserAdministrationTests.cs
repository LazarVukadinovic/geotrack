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

        using var client = await LoginAdminAsync(app, "mika@gmail.com", "TEST-only-Fixture-9284!");
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
        Assert.True(manager.CanRemoveRegion);
        Assert.True(manager.CanRemoveManagerRole);
        Assert.NotNull(manager.LastLoginAt);
        Assert.NotEqual("Never", manager.LastLoginDisplay);
        var assignedRegion = Assert.Single(manager.AssignedRegions);
        Assert.Equal(fixture.AssignedRegionOneId, assignedRegion.RegionId);
        Assert.Equal(fixture.AssignedTerritoryId, assignedRegion.TerritoryId);
        Assert.False(string.IsNullOrWhiteSpace(assignedRegion.TerritoryName));
        Assert.True(assignedRegion.IsActive);
        Assert.True(assignedRegion.CanRemove);
        Assert.True(assignedRegion.AssignedAt <= DateTime.UtcNow);

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

        using var territoryClient = await LoginAdminAsync(app, "mika@gmail.com", "TEST-only-Fixture-9284!");
        var unassigned = await territoryClient.GetAsync(
            $"/api/admin/territory/users?territoryId={fixture.UnassignedTerritoryId}");
        Assert.Equal(HttpStatusCode.Forbidden, unassigned.StatusCode);

        var outOfScopeDetails = await territoryClient.GetAsync(
            $"/api/admin/territory/users/{fixture.OutOfTerritoryUserId}?territoryId={fixture.AssignedTerritoryId}");
        Assert.Equal(HttpStatusCode.Forbidden, outOfScopeDetails.StatusCode);
    }

    [Fact]
    public async Task Territory_user_list_supports_search_sort_and_pagination()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        var fixture = await CreateFixtureAsync(app);
        await AddPagedUsersAsync(app, fixture.AssignedRegionOneId, 10);

        using var client = await LoginAdminAsync(app, "mika@gmail.com", "TEST-only-Fixture-9284!");

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

        using var client = await LoginAdminAsync(app, "mika@gmail.com", "TEST-only-Fixture-9284!");

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
    public async Task Territory_manager_can_get_user_details_and_remove_one_region_assignment()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        var fixture = await CreateFixtureAsync(app);

        using var client = await LoginAdminAsync(app, "mika@gmail.com", "TEST-only-Fixture-9284!");

        var add = await client.PostAsJsonAsync(
            $"/api/admin/territory/users/{fixture.ActiveManagerUserId}/regions",
            new TerritoryUserAssignRegionRequestDto
            {
                TerritoryId = fixture.AssignedTerritoryId,
                RegionId = fixture.AssignedRegionTwoId
            });
        add.EnsureSuccessStatusCode();

        var details = await client.GetFromJsonAsync<TerritoryUserListItemDto>(
            $"/api/admin/territory/users/{fixture.ActiveManagerUserId}?territoryId={fixture.AssignedTerritoryId}");
        Assert.NotNull(details);
        Assert.Equal(
            new[] { fixture.AssignedRegionOneId, fixture.AssignedRegionTwoId },
            details!.AssignedRegions.Select(region => region.RegionId).OrderBy(id => id).ToArray());
        Assert.All(details.AssignedRegions, region =>
        {
            Assert.Equal(fixture.AssignedTerritoryId, region.TerritoryId);
            Assert.True(region.IsActive);
            Assert.True(region.CanRemove);
        });

        var removeOne = await client.DeleteAsync(
            $"/api/admin/territory/users/{fixture.ActiveManagerUserId}/regions/{fixture.AssignedRegionOneId}?territoryId={fixture.AssignedTerritoryId}");
        removeOne.EnsureSuccessStatusCode();

        var afterOne = await removeOne.Content.ReadFromJsonAsync<TerritoryUserListItemDto>();
        Assert.NotNull(afterOne);
        Assert.DoesNotContain(afterOne!.AssignedRegions, region => region.RegionId == fixture.AssignedRegionOneId);
        Assert.Contains(afterOne.AssignedRegions, region => region.RegionId == fixture.AssignedRegionTwoId);
        Assert.Contains(RoleNames.RegionalManager, afterOne.Roles);

        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var removedAssignment = await db.RegionalManagerRegions.SingleAsync(assignment =>
                assignment.UserId == fixture.ActiveManagerUserId &&
                assignment.RegionId == fixture.AssignedRegionOneId);
            Assert.False(removedAssignment.IsActive);
            Assert.NotNull(removedAssignment.UpdatedAt);

            Assert.True(await db.RegionalManagerRegions.AnyAsync(assignment =>
                assignment.UserId == fixture.ActiveManagerUserId &&
                assignment.RegionId == fixture.AssignedRegionTwoId &&
                assignment.IsActive));
            Assert.True(await db.UserRoles.AnyAsync(role =>
                role.UserId == fixture.ActiveManagerUserId &&
                role.Role.Name == RoleNames.RegionalManager));
        }

        var removeTwo = await client.DeleteAsync(
            $"/api/admin/territory/users/{fixture.ActiveManagerUserId}/regions/{fixture.AssignedRegionTwoId}?territoryId={fixture.AssignedTerritoryId}");
        removeTwo.EnsureSuccessStatusCode();

        var afterTwo = await removeTwo.Content.ReadFromJsonAsync<TerritoryUserListItemDto>();
        Assert.NotNull(afterTwo);
        Assert.Empty(afterTwo!.AssignedRegions);
        Assert.DoesNotContain(RoleNames.RegionalManager, afterTwo.Roles);

        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.True(await db.Users.AnyAsync(user => user.Id == fixture.ActiveManagerUserId));
            Assert.False(await db.UserRoles.AnyAsync(role =>
                role.UserId == fixture.ActiveManagerUserId &&
                role.Role.Name == RoleNames.RegionalManager));
        }
    }

    [Fact]
    public async Task Add_region_promotes_viewer_by_role_name_and_available_regions_are_scoped()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        var fixture = await CreateFixtureAsync(app);

        using var client = await LoginAdminAsync(app, "mika@gmail.com", "TEST-only-Fixture-9284!");

        var available = await client.GetFromJsonAsync<TerritoryUserAvailableRegionsResponseDto>(
            $"/api/admin/territory/users/{fixture.ViewerUserId}/available-regions?territoryId={fixture.AssignedTerritoryId}");
        Assert.NotNull(available);
        Assert.Contains(available!.Items, item => item.RegionId == fixture.AssignedRegionOneId);
        Assert.DoesNotContain(available.Items, item => item.RegionId == fixture.AssignedRegionTwoId);
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
    public async Task Add_regional_manager_assigns_role_regions_and_prevents_duplicates()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        var fixture = await CreateFixtureAsync(app);

        using var client = await LoginAdminAsync(app, "mika@gmail.com", "TEST-only-Fixture-9284!");

        var response = await client.PostAsJsonAsync(
            $"/api/admin/territory/users/{fixture.PlainUserId}/regional-manager",
            new TerritoryUserAddRegionalManagerRequestDto
            {
                TerritoryId = fixture.AssignedTerritoryId,
                RegionIds = new List<int> { fixture.AssignedRegionOneId }
            });
        response.EnsureSuccessStatusCode();

        var updated = await response.Content.ReadFromJsonAsync<TerritoryUserListItemDto>();
        Assert.NotNull(updated);
        Assert.Equal(RoleNames.RegionalManager, updated!.RoleCode);
        Assert.Contains(RoleNames.RegionalManager, updated.Roles);
        Assert.Contains(updated.AssignedRegions, region => region.RegionId == fixture.AssignedRegionOneId);

        var duplicate = await client.PostAsJsonAsync(
            $"/api/admin/territory/users/{fixture.PlainUserId}/regional-manager",
            new TerritoryUserAddRegionalManagerRequestDto
            {
                TerritoryId = fixture.AssignedTerritoryId,
                RegionIds = new List<int> { fixture.AssignedRegionOneId }
            });
        duplicate.EnsureSuccessStatusCode();

        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(1, await db.UserRoles.CountAsync(role =>
            role.UserId == fixture.PlainUserId &&
            role.Role.Name == RoleNames.RegionalManager));
        Assert.Equal(1, await db.RegionalManagerRegions.CountAsync(assignment =>
            assignment.UserId == fixture.PlainUserId &&
            assignment.RegionId == fixture.AssignedRegionOneId &&
            assignment.IsActive));
    }

    [Fact]
    public async Task Add_regional_manager_rejects_regions_outside_assigned_territory()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        var fixture = await CreateFixtureAsync(app);

        using var client = await LoginAdminAsync(app, "mika@gmail.com", "TEST-only-Fixture-9284!");

        var response = await client.PostAsJsonAsync(
            $"/api/admin/territory/users/{fixture.PlainUserId}/regional-manager",
            new TerritoryUserAddRegionalManagerRequestDto
            {
                TerritoryId = fixture.AssignedTerritoryId,
                RegionIds = new List<int> { fixture.UnassignedRegionId }
            });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.False(await db.UserRoles.AnyAsync(role =>
            role.UserId == fixture.PlainUserId &&
            role.Role.Name == RoleNames.RegionalManager));
    }

    [Fact]
    public async Task Territory_manager_can_add_territory_associate_to_existing_user_without_duplicates()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        var fixture = await CreateFixtureAsync(app);

        using var client = await LoginAdminAsync(app, "mika@gmail.com", "TEST-only-Fixture-9284!");

        var response = await client.PostAsJsonAsync(
            $"/api/admin/territory/users/{fixture.PlainUserId}/territory-associate",
            new TerritoryUserAddTerritoryAssociateRequestDto
            {
                TerritoryId = fixture.AssignedTerritoryId
            });
        response.EnsureSuccessStatusCode();

        var updated = await response.Content.ReadFromJsonAsync<TerritoryUserListItemDto>();
        Assert.NotNull(updated);
        Assert.Equal(RoleNames.TerritoryAssociate, updated!.RoleCode);
        Assert.Contains(RoleNames.TerritoryAssociate, updated.Roles);
        Assert.Contains(updated.AssignedTerritories, territory => territory.TerritoryId == fixture.AssignedTerritoryId);

        var duplicate = await client.PostAsJsonAsync(
            $"/api/admin/territory/users/{fixture.PlainUserId}/territory-associate",
            new TerritoryUserAddTerritoryAssociateRequestDto
            {
                TerritoryId = fixture.AssignedTerritoryId
            });
        duplicate.EnsureSuccessStatusCode();

        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(1, await db.UserRoles.CountAsync(role =>
            role.UserId == fixture.PlainUserId &&
            role.Role.Name == RoleNames.TerritoryAssociate));
        Assert.Equal(1, await db.TerritoryManagerTerritories.CountAsync(assignment =>
            assignment.UserId == fixture.PlainUserId &&
            assignment.TerritoryId == fixture.AssignedTerritoryId));
    }

    [Fact]
    public async Task Add_territory_associate_preserves_existing_regional_manager_role()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        var fixture = await CreateFixtureAsync(app);

        using var client = await LoginAdminAsync(app, "mika@gmail.com", "TEST-only-Fixture-9284!");

        var response = await client.PostAsJsonAsync(
            $"/api/admin/territory/users/{fixture.ActiveManagerUserId}/territory-associate",
            new TerritoryUserAddTerritoryAssociateRequestDto
            {
                TerritoryId = fixture.AssignedTerritoryId
            });
        response.EnsureSuccessStatusCode();

        var updated = await response.Content.ReadFromJsonAsync<TerritoryUserListItemDto>();
        Assert.NotNull(updated);
        Assert.Contains(RoleNames.RegionalManager, updated!.Roles);
        Assert.Contains(RoleNames.TerritoryAssociate, updated.Roles);
        Assert.Contains(updated.AssignedRegions, region => region.RegionId == fixture.AssignedRegionOneId);

        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.True(await db.UserRoles.AnyAsync(role =>
            role.UserId == fixture.ActiveManagerUserId &&
            role.Role.Name == RoleNames.RegionalManager));
        Assert.True(await db.RegionalManagerRegions.AnyAsync(assignment =>
            assignment.UserId == fixture.ActiveManagerUserId &&
            assignment.RegionId == fixture.AssignedRegionOneId &&
            assignment.IsActive));
    }

    [Fact]
    public async Task Territory_associate_cannot_add_territory_associate()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        var fixture = await CreateFixtureAsync(app);
        await GrantTerritoryAssociateRoleAsync(app, "elena@example.com", fixture.AssignedTerritoryId);

        using var associateClient = await LoginAdminAsync(app, "elena@example.com", "password123");
        var response = await associateClient.PostAsJsonAsync(
            $"/api/admin/territory/users/{fixture.PlainUserId}/territory-associate",
            new TerritoryUserAddTerritoryAssociateRequestDto
            {
                TerritoryId = fixture.AssignedTerritoryId
            });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Territory_manager_can_remove_territory_associate_assignment_without_deleting_user()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        var fixture = await CreateFixtureAsync(app);

        using var client = await LoginAdminAsync(app, "mika@gmail.com", "TEST-only-Fixture-9284!");
        var add = await client.PostAsJsonAsync(
            $"/api/admin/territory/users/{fixture.PlainUserId}/territory-associate",
            new TerritoryUserAddTerritoryAssociateRequestDto
            {
                TerritoryId = fixture.AssignedTerritoryId
            });
        add.EnsureSuccessStatusCode();

        var response = await client.PostAsJsonAsync(
            $"/api/admin/territory/users/{fixture.PlainUserId}/territory-associate/remove",
            new TerritoryUserRemoveTerritoryAssociateRequestDto
            {
                TerritoryId = fixture.AssignedTerritoryId
            });
        response.EnsureSuccessStatusCode();

        var updated = await response.Content.ReadFromJsonAsync<TerritoryUserListItemDto>();
        Assert.NotNull(updated);
        Assert.False(updated!.IsInTerritoryScope);
        Assert.Equal("Inactive", updated.Status);

        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.True(await db.Users.AnyAsync(user => user.Id == fixture.PlainUserId));
        Assert.False(await db.UserRoles.AnyAsync(role =>
            role.UserId == fixture.PlainUserId &&
            role.Role.Name == RoleNames.TerritoryAssociate));
        Assert.False(await db.TerritoryManagerTerritories.AnyAsync(assignment =>
            assignment.UserId == fixture.PlainUserId &&
            assignment.TerritoryId == fixture.AssignedTerritoryId));
        Assert.True(await db.UserRoles.AnyAsync(role =>
            role.UserId == fixture.PlainUserId &&
            role.Role.Name == RoleNames.RegisteredUser));
    }

    [Fact]
    public async Task Territory_associate_cannot_remove_regional_territory_or_associate_roles()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        var fixture = await CreateFixtureAsync(app);
        await GrantTerritoryAssociateRoleAsync(app, "elena@example.com", fixture.AssignedTerritoryId);

        using var managerClient = await LoginAdminAsync(app, "mika@gmail.com", "TEST-only-Fixture-9284!");
        var add = await managerClient.PostAsJsonAsync(
            $"/api/admin/territory/users/{fixture.PlainUserId}/territory-associate",
            new TerritoryUserAddTerritoryAssociateRequestDto
            {
                TerritoryId = fixture.AssignedTerritoryId
            });
        add.EnsureSuccessStatusCode();

        using var associateClient = await LoginAdminAsync(app, "elena@example.com", "password123");
        var removeAssociate = await associateClient.PostAsJsonAsync(
            $"/api/admin/territory/users/{fixture.PlainUserId}/territory-associate/remove",
            new TerritoryUserRemoveTerritoryAssociateRequestDto
            {
                TerritoryId = fixture.AssignedTerritoryId
            });
        Assert.Equal(HttpStatusCode.Forbidden, removeAssociate.StatusCode);

        var removeRegionalManager = await associateClient.PostAsJsonAsync(
            $"/api/admin/territory/users/{fixture.ActiveManagerUserId}/remove-manager-role",
            new TerritoryUserRemoveManagerRoleRequestDto
            {
                TerritoryId = fixture.AssignedTerritoryId
            });
        Assert.Equal(HttpStatusCode.Forbidden, removeRegionalManager.StatusCode);

        var removeRegion = await associateClient.DeleteAsync(
            $"/api/admin/territory/users/{fixture.ActiveManagerUserId}/regions/{fixture.AssignedRegionOneId}?territoryId={fixture.AssignedTerritoryId}");
        Assert.Equal(HttpStatusCode.Forbidden, removeRegion.StatusCode);

        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var territoryManagerId = await db.Users
            .Where(user => user.Email == "mika@gmail.com")
            .Select(user => user.Id)
            .SingleAsync();

        var removeTerritoryManager = await associateClient.PostAsJsonAsync(
            $"/api/admin/territory/users/{territoryManagerId}/remove-manager-role",
            new TerritoryUserRemoveManagerRoleRequestDto
            {
                TerritoryId = fixture.AssignedTerritoryId
            });
        Assert.NotEqual(HttpStatusCode.OK, removeTerritoryManager.StatusCode);
    }

    [Fact]
    public async Task List_action_flags_are_correct_for_territory_manager_and_associate()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        var fixture = await CreateFixtureAsync(app);
        await GrantTerritoryAssociateRoleAsync(app, "elena@example.com", fixture.AssignedTerritoryId);

        using var managerClient = await LoginAdminAsync(app, "mika@gmail.com", "TEST-only-Fixture-9284!");
        var add = await managerClient.PostAsJsonAsync(
            $"/api/admin/territory/users/{fixture.PlainUserId}/territory-associate",
            new TerritoryUserAddTerritoryAssociateRequestDto
            {
                TerritoryId = fixture.AssignedTerritoryId
            });
        add.EnsureSuccessStatusCode();

        var managerView = await managerClient.GetFromJsonAsync<TerritoryUserListResponseDto>(
            $"/api/admin/territory/users?territoryId={fixture.AssignedTerritoryId}&skip=0&take=20");
        Assert.NotNull(managerView);
        var associateRowForManager = Assert.Single(managerView!.Items, item => item.UserId == fixture.PlainUserId);
        Assert.False(associateRowForManager.CanAddTerritoryAssociate);
        Assert.False(associateRowForManager.CanAddTerritoryAssociateRole);
        Assert.True(associateRowForManager.CanAddRegionalManagerRole);
        Assert.True(associateRowForManager.CanRemoveTerritoryAssociateRole);

        var regionalRowForManager = Assert.Single(managerView.Items, item => item.UserId == fixture.ActiveManagerUserId);
        Assert.True(regionalRowForManager.CanAddTerritoryAssociate);
        Assert.True(regionalRowForManager.CanAddTerritoryAssociateRole);
        Assert.False(regionalRowForManager.CanAddRegionalManagerRole);
        Assert.True(regionalRowForManager.CanRemoveRegionalManagerRole);

        using var associateClient = await LoginAdminAsync(app, "elena@example.com", "password123");
        var associateView = await associateClient.GetFromJsonAsync<TerritoryUserListResponseDto>(
            $"/api/admin/territory/users?territoryId={fixture.AssignedTerritoryId}&skip=0&take=20");
        Assert.NotNull(associateView);
        Assert.All(associateView!.Items, item =>
        {
            Assert.False(item.CanAddTerritoryAssociate);
            Assert.False(item.CanAddTerritoryAssociateRole);
            Assert.False(item.CanRemoveTerritoryAssociateRole);
        });

        var regionalRowForAssociate = Assert.Single(associateView.Items, item => item.UserId == fixture.ActiveManagerUserId);
        Assert.True(regionalRowForAssociate.CanAddRegion);
        Assert.False(regionalRowForAssociate.CanRemoveRegionalManagerRole);
    }

    [Fact]
    public async Task Edit_user_updates_only_safe_profile_fields()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        var fixture = await CreateFixtureAsync(app);

        using var client = await LoginAdminAsync(app, "mika@gmail.com", "TEST-only-Fixture-9284!");
        var response = await client.PutAsJsonAsync(
            $"/api/admin/territory/users/{fixture.ActiveManagerUserId}",
            new TerritoryUserUpdateRequestDto
            {
                TerritoryId = fixture.AssignedTerritoryId,
                FirstName = "Renamed",
                LastName = "Manager",
                RegionIdsToAdd = new List<int> { fixture.AssignedRegionTwoId }
            });
        response.EnsureSuccessStatusCode();

        var updated = await response.Content.ReadFromJsonAsync<TerritoryUserListItemDto>();
        Assert.NotNull(updated);
        Assert.Equal("Renamed Manager", updated!.DisplayName);
        Assert.Equal("Renamed", updated.FirstName);
        Assert.Equal(fixture.ActiveManagerEmail, updated.Email);
        Assert.Contains(updated.AssignedRegions, region => region.RegionId == fixture.AssignedRegionTwoId);

        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var persisted = await db.Users.SingleAsync(user => user.Id == fixture.ActiveManagerUserId);
        Assert.Equal(fixture.ActiveManagerEmail, persisted.Email);
        Assert.Equal("Active Manager", persisted.DisplayName);
        Assert.Equal(1, await db.RegionalManagerRegions.CountAsync(assignment =>
            assignment.UserId == fixture.ActiveManagerUserId &&
            assignment.RegionId == fixture.AssignedRegionTwoId &&
            assignment.IsActive));
    }

    [Fact]
    public async Task Edit_user_rejects_out_of_scope_users_and_regions_without_duplicates()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        var fixture = await CreateFixtureAsync(app);

        using var client = await LoginAdminAsync(app, "mika@gmail.com", "TEST-only-Fixture-9284!");

        var outsideUser = await client.PutAsJsonAsync(
            $"/api/admin/territory/users/{fixture.OutOfTerritoryUserId}",
            new TerritoryUserUpdateRequestDto
            {
                TerritoryId = fixture.AssignedTerritoryId,
                FirstName = "Blocked",
                LastName = "User"
            });
        Assert.Equal(HttpStatusCode.Forbidden, outsideUser.StatusCode);

        var outsideRegion = await client.PutAsJsonAsync(
            $"/api/admin/territory/users/{fixture.ActiveManagerUserId}",
            new TerritoryUserUpdateRequestDto
            {
                TerritoryId = fixture.AssignedTerritoryId,
                FirstName = "Blocked",
                LastName = "Region",
                RegionIdsToAdd = new List<int> { fixture.UnassignedRegionId }
            });
        Assert.Equal(HttpStatusCode.Forbidden, outsideRegion.StatusCode);

        var duplicate = await client.PutAsJsonAsync(
            $"/api/admin/territory/users/{fixture.ActiveManagerUserId}",
            new TerritoryUserUpdateRequestDto
            {
                TerritoryId = fixture.AssignedTerritoryId,
                FirstName = "Duplicate",
                LastName = "Region",
                RegionIdsToAdd = new List<int> { fixture.AssignedRegionOneId }
            });
        Assert.Equal(HttpStatusCode.BadRequest, duplicate.StatusCode);

        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(1, await db.RegionalManagerRegions.CountAsync(assignment =>
            assignment.UserId == fixture.ActiveManagerUserId &&
            assignment.RegionId == fixture.AssignedRegionOneId));
    }

    [Fact]
    public async Task Territory_user_mutations_reject_regional_and_anonymous_callers()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        var fixture = await CreateFixtureAsync(app);
        await GrantRegionalManagerRoleAsync(app, "elena@example.com");

        using var anonymousClient = app.CreateClient();
        var anonymous = await anonymousClient.PutAsJsonAsync(
            $"/api/admin/territory/users/{fixture.ActiveManagerUserId}",
            new TerritoryUserUpdateRequestDto
            {
                TerritoryId = fixture.AssignedTerritoryId,
                FirstName = "Anonymous",
                LastName = "Caller"
            });
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);

        using var regionalClient = await LoginAdminAsync(app, "elena@example.com", "password123");
        var regional = await regionalClient.PutAsJsonAsync(
            $"/api/admin/territory/users/{fixture.ActiveManagerUserId}",
            new TerritoryUserUpdateRequestDto
            {
                TerritoryId = fixture.AssignedTerritoryId,
                FirstName = "Regional",
                LastName = "Caller"
            });
        Assert.Equal(HttpStatusCode.Forbidden, regional.StatusCode);

        var anonymousAddRegionalManager = await anonymousClient.PostAsJsonAsync(
            $"/api/admin/territory/users/{fixture.PlainUserId}/regional-manager",
            new TerritoryUserAddRegionalManagerRequestDto
            {
                TerritoryId = fixture.AssignedTerritoryId,
                RegionIds = new List<int> { fixture.AssignedRegionOneId }
            });
        Assert.Equal(HttpStatusCode.Unauthorized, anonymousAddRegionalManager.StatusCode);

        var anonymousAddTerritoryAssociate = await anonymousClient.PostAsJsonAsync(
            $"/api/admin/territory/users/{fixture.PlainUserId}/territory-associate",
            new TerritoryUserAddTerritoryAssociateRequestDto
            {
                TerritoryId = fixture.AssignedTerritoryId
            });
        Assert.Equal(HttpStatusCode.Unauthorized, anonymousAddTerritoryAssociate.StatusCode);

        var regionalAddTerritoryAssociate = await regionalClient.PostAsJsonAsync(
            $"/api/admin/territory/users/{fixture.PlainUserId}/territory-associate",
            new TerritoryUserAddTerritoryAssociateRequestDto
            {
                TerritoryId = fixture.AssignedTerritoryId
            });
        Assert.Equal(HttpStatusCode.Forbidden, regionalAddTerritoryAssociate.StatusCode);

        var regionalAddRegionalManager = await regionalClient.PostAsJsonAsync(
            $"/api/admin/territory/users/{fixture.PlainUserId}/regional-manager",
            new TerritoryUserAddRegionalManagerRequestDto
            {
                TerritoryId = fixture.AssignedTerritoryId,
                RegionIds = new List<int> { fixture.AssignedRegionOneId }
            });
        Assert.Equal(HttpStatusCode.Forbidden, regionalAddRegionalManager.StatusCode);
    }

    [Fact]
    public async Task Remove_manager_role_is_territory_scoped_and_does_not_delete_user()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        var fixture = await CreateFixtureAsync(app, createCrossTerritoryManager: true);

        using var client = await LoginAdminAsync(app, "mika@gmail.com", "TEST-only-Fixture-9284!");
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

        using var client = await LoginAdminAsync(app, "mika@gmail.com", "TEST-only-Fixture-9284!");

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

        using var _ = await LoginAdminAsync(app, "mika@gmail.com", "TEST-only-Fixture-9284!");

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

    private static async Task GrantTerritoryAssociateRoleAsync(GeotrackApiFactory app, string email, int territoryId)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = await db.Users
            .Include(existing => existing.TerritoryManager)
                .ThenInclude(manager => manager!.TerritoryManagerTerritories)
            .SingleAsync(existing => existing.Email == email);
        var role = await db.Roles.SingleAsync(existing => existing.Name == RoleNames.TerritoryAssociate);

        if (!await db.UserRoles.AnyAsync(userRole => userRole.UserId == user.Id && userRole.RoleId == role.Id))
        {
            db.UserRoles.Add(new UserRole
            {
                UserId = user.Id,
                RoleId = role.Id
            });
        }

        if (user.TerritoryManager is null)
        {
            user.TerritoryManager = new TerritoryManager
            {
                UserId = user.Id
            };
            db.TerritoryManagers.Add(user.TerritoryManager);
        }

        if (!user.TerritoryManager.TerritoryManagerTerritories.Any(assignment => assignment.TerritoryId == territoryId))
        {
            user.TerritoryManager.TerritoryManagerTerritories.Add(new TerritoryManagerTerritory
            {
                UserId = user.Id,
                TerritoryId = territoryId,
                CreatedAt = DateTime.UtcNow
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
        var plainUser = await CreateUserAsync(
            db,
            $"plain-user-{suffix}@example.com",
            "Plain",
            "User",
            "Plain User",
            null,
            null);

        db.UserRoles.AddRange(
            new UserRole { UserId = activeManager.Id, RoleId = regionalRole.Id },
            new UserRole { UserId = viewer.Id, RoleId = registeredRole.Id },
            new UserRole { UserId = inactiveManager.Id, RoleId = regionalRole.Id },
            new UserRole { UserId = outUser.Id, RoleId = regionalRole.Id },
            new UserRole { UserId = plainUser.Id, RoleId = registeredRole.Id });

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
            plainUser.Id,
            plainUser.Email,
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
        int PlainUserId,
        string PlainUserEmail,
        int InactiveManagerUserId,
        int OutOfTerritoryUserId,
        int CrossTerritoryManagerUserId);
}
