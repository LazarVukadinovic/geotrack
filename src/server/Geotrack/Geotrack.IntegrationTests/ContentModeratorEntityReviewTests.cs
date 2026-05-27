using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
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
using CoreEvent = Geotrack.Domain.Entities.Core.Event;

namespace Geotrack.IntegrationTests;

public sealed class ContentModeratorEntityReviewTests
{
    private const string PublishedStatus = "Published";
    private const string SuspendedStatus = "Draft";
    private const string DeletedStatus = "Archived";
    private const string UserPassword = "Password123!";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task Entity_review_endpoints_require_content_moderator_and_reject_invalid_type()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();

        using var anonymous = app.CreateClient();
        var anonymousResponse = await anonymous.GetAsync("/api/admin/content-moderator/entities?type=destination");
        Assert.Equal(HttpStatusCode.Unauthorized, anonymousResponse.StatusCode);

        using var admin = await LoginAdminAsync(app);
        var adminResponse = await admin.Client.GetAsync("/api/admin/content-moderator/entities?type=destination");
        Assert.Equal(HttpStatusCode.Forbidden, adminResponse.StatusCode);

        await GrantUserRoleAsync(app, "elena@example.com", RoleNames.RegionalManager);
        using var regionalManager = await LoginAdminAppAsync(app, "elena@example.com", "password123");
        var regionalResponse = await regionalManager.Client.GetAsync("/api/admin/content-moderator/entities?type=destination");
        Assert.Equal(HttpStatusCode.Forbidden, regionalResponse.StatusCode);

        using var territoryManager = await LoginAdminAppAsync(app, "mika@gmail.com", "Proba123!");
        var territoryResponse = await territoryManager.Client.GetAsync("/api/admin/content-moderator/entities?type=destination");
        Assert.Equal(HttpStatusCode.Forbidden, territoryResponse.StatusCode);

        using var normalUser = await RegisterAndLoginAsync(app, $"normal-{Guid.NewGuid():N}@example.com", "Normal User");
        var normalResponse = await normalUser.Client.GetAsync("/api/admin/content-moderator/entities?type=destination");
        Assert.Equal(HttpStatusCode.Forbidden, normalResponse.StatusCode);

        using var moderator = await LoginModeratorAsync(app);
        var invalidTypeResponse = await moderator.Client.GetAsync("/api/admin/content-moderator/entities?type=country");
        Assert.Equal(HttpStatusCode.BadRequest, invalidTypeResponse.StatusCode);
    }

    [Fact]
    public async Task Entity_review_lists_real_entities_with_pagination_search_status_and_newest_sort()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        var prefix = $"cm-entity-list-{Guid.NewGuid():N}";
        await AddEntityReviewFixturesAsync(app, prefix, publishedCount: 26);

        using var moderator = await LoginModeratorAsync(app);

        foreach (var entityType in new[] { "destination", "location", "place", "event" })
        {
            var page = await moderator.Client.GetFromJsonAsync<ContentModeratorEntityReviewListResponseDto>(
                $"/api/admin/content-moderator/entities?type={entityType}&search={Uri.EscapeDataString(prefix)}&status=all&sort=newest&skip=0&take=25",
                JsonOptions);

            Assert.NotNull(page);
            Assert.Equal(27, page!.TotalCount);
            Assert.Equal(25, page.Items.Count);
            Assert.True(page.HasMore);
            Assert.Equal(25, page.NextSkip);
            Assert.True(page.Counts.AllCount >= 27);
            Assert.True(page.Counts.ActiveCount >= 26);
            Assert.True(page.Counts.SuspendedCount >= 1);
            Assert.True(page.Counts.DeletedCount >= 1);
            Assert.All(page.Items, item =>
            {
                Assert.Equal(entityType, item.EntityType);
                Assert.Contains(prefix, item.Name, StringComparison.OrdinalIgnoreCase);
                Assert.True(item.Permissions.CanView);
            });

            var createdAtValues = page.Items.Select(item => item.CreatedAt).ToList();
            Assert.Equal(createdAtValues.OrderByDescending(value => value).ToList(), createdAtValues);

            var searchPage = await moderator.Client.GetFromJsonAsync<ContentModeratorEntityReviewListResponseDto>(
                $"/api/admin/content-moderator/entities?type={entityType}&search={Uri.EscapeDataString($"{prefix}-{entityType}-published-25")}&status=all&sort=newest&skip=0&take=25",
                JsonOptions);
            Assert.NotNull(searchPage);
            var searchItem = Assert.Single(searchPage!.Items);
            Assert.Contains("published-25", searchItem.Name, StringComparison.OrdinalIgnoreCase);

            var suspendedPage = await moderator.Client.GetFromJsonAsync<ContentModeratorEntityReviewListResponseDto>(
                $"/api/admin/content-moderator/entities?type={entityType}&search={Uri.EscapeDataString(prefix)}&status=suspended&skip=0&take=25",
                JsonOptions);
            Assert.NotNull(suspendedPage);
            var suspendedItem = Assert.Single(suspendedPage!.Items);
            Assert.Equal("Suspended", suspendedItem.Status);
            Assert.Equal(SuspendedStatus, suspendedItem.RawStatus);

            var deletedPage = await moderator.Client.GetFromJsonAsync<ContentModeratorEntityReviewListResponseDto>(
                $"/api/admin/content-moderator/entities?type={entityType}&search={Uri.EscapeDataString(prefix)}&status=deleted&skip=0&take=25",
                JsonOptions);
            Assert.NotNull(deletedPage);
            var deletedItem = Assert.Single(deletedPage!.Items);
            Assert.Equal("Deleted", deletedItem.Status);
            Assert.Equal(DeletedStatus, deletedItem.RawStatus);
        }
    }

    [Fact]
    public async Task Content_moderator_can_suspend_unsuspend_and_soft_delete_each_entity_type()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        var prefix = $"cm-entity-action-{Guid.NewGuid():N}";
        var fixture = await AddEntityReviewFixturesAsync(app, prefix, publishedCount: 1);

        using var moderator = await LoginModeratorAsync(app);

        foreach (var entityType in new[] { "destination", "location", "place", "event" })
        {
            var entityId = fixture.PublishedIds[entityType].Single();

            var suspendResponse = await moderator.Client.PostAsJsonAsync(
                $"/api/admin/content-moderator/entities/{entityType}/{entityId}/suspend",
                new ContentModeratorModerationActionRequestDto { Reason = "Integration test suspension." });
            suspendResponse.EnsureSuccessStatusCode();

            var suspended = await suspendResponse.Content.ReadFromJsonAsync<ContentModeratorEntityReviewDetailDto>(JsonOptions);
            Assert.NotNull(suspended);
            Assert.Equal(SuspendedStatus, suspended!.RawStatus);
            Assert.False(suspended.CanOpenInUserApp);
            Assert.Null(suspended.PublicDetailRoute);
            Assert.True(suspended.Permissions.CanUnsuspend);
            await AssertEntityStatusAsync(app, entityType, entityId, SuspendedStatus);

            var publicSuspended = await moderator.Client.GetAsync(PublicDetailRoute(entityType, entityId));
            Assert.Equal(HttpStatusCode.NotFound, publicSuspended.StatusCode);

            var unsuspendResponse = await moderator.Client.PostAsJsonAsync(
                $"/api/admin/content-moderator/entities/{entityType}/{entityId}/unsuspend",
                new ContentModeratorModerationActionRequestDto { Reason = "Integration test restore." });
            unsuspendResponse.EnsureSuccessStatusCode();

            var unsuspended = await unsuspendResponse.Content.ReadFromJsonAsync<ContentModeratorEntityReviewDetailDto>(JsonOptions);
            Assert.NotNull(unsuspended);
            Assert.Equal(PublishedStatus, unsuspended!.RawStatus);
            Assert.True(unsuspended.CanOpenInUserApp);
            Assert.Equal(UserAppDetailRoute(entityType, entityId), unsuspended.PublicDetailRoute);
            Assert.True(unsuspended.Permissions.CanSuspend);
            await AssertEntityStatusAsync(app, entityType, entityId, PublishedStatus);

            var deleteResponse = await moderator.Client.PostAsJsonAsync(
                $"/api/admin/content-moderator/entities/{entityType}/{entityId}/delete",
                new ContentModeratorModerationActionRequestDto { Reason = "Integration test delete." });
            deleteResponse.EnsureSuccessStatusCode();

            var deleted = await deleteResponse.Content.ReadFromJsonAsync<ContentModeratorEntityReviewDetailDto>(JsonOptions);
            Assert.NotNull(deleted);
            Assert.Equal(DeletedStatus, deleted!.RawStatus);
            Assert.False(deleted.CanOpenInUserApp);
            Assert.Null(deleted.PublicDetailRoute);
            Assert.False(deleted.Permissions.CanDelete);
            await AssertEntityStatusAsync(app, entityType, entityId, DeletedStatus);

            var publicDeleted = await moderator.Client.GetAsync(PublicDetailRoute(entityType, entityId));
            Assert.Equal(HttpStatusCode.NotFound, publicDeleted.StatusCode);
        }
    }

    [Fact]
    public async Task Entity_review_uses_stays_user_app_route_for_stay_places()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        var placeId = await AddPublishedStayPlaceAsync(app, $"cm-stay-place-{Guid.NewGuid():N}");

        using var moderator = await LoginModeratorAsync(app);

        var detail = await moderator.Client.GetFromJsonAsync<ContentModeratorEntityReviewDetailDto>(
            $"/api/admin/content-moderator/entities/place/{placeId}",
            JsonOptions);

        Assert.NotNull(detail);
        Assert.True(detail!.CanOpenInUserApp);
        Assert.Equal($"/stays/{placeId}", detail.PublicDetailRoute);
    }

    [Fact]
    public async Task Dashboard_entity_counts_match_content_moderator_list_count_rules()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        var prefix = $"cm-dashboard-count-{Guid.NewGuid():N}";
        await AddEntityReviewFixturesAsync(app, prefix, publishedCount: 2);

        using var moderator = await LoginModeratorAsync(app);
        var dashboard = await moderator.Client.GetFromJsonAsync<ContentModeratorDashboardDto>(
            "/api/admin/content-moderator/dashboard",
            JsonOptions);

        Assert.NotNull(dashboard);

        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(await db.Destinations.CountAsync(entity => entity.Status != DeletedStatus), dashboard!.Stats.DestinationsCount);
        Assert.Equal(await db.Locations.CountAsync(entity => entity.Status != DeletedStatus), dashboard.Stats.LocationsCount);
        Assert.Equal(await db.Places.CountAsync(entity => entity.Status != DeletedStatus), dashboard.Stats.PlacesCount);
        Assert.Equal(await db.Events.CountAsync(entity => entity.Status != DeletedStatus), dashboard.Stats.EventsCount);
    }

    private static async Task<EntityReviewFixture> AddEntityReviewFixturesAsync(
        GeotrackApiFactory app,
        string prefix,
        int publishedCount)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var regionId = await db.Regions.OrderBy(region => region.Id).Select(region => region.Id).FirstAsync();
        var destinationTypeId = await db.DestinationTypes.OrderBy(type => type.Id).Select(type => type.Id).FirstAsync();
        var locationTypeId = await db.LocationTypes.OrderBy(type => type.Id).Select(type => type.Id).FirstAsync();
        var placeTypeId = await db.PlaceTypes.OrderBy(type => type.Id).Select(type => type.Id).FirstAsync();
        var eventTypeId = await db.EventTypes.OrderBy(type => type.Id).Select(type => type.Id).FirstAsync();
        var now = DateTime.UtcNow.AddDays(-1);

        var parentDestination = new Destination
        {
            Name = $"Entity review parent destination {Guid.NewGuid():N}",
            Summary = "Parent destination for content moderator tests.",
            Description = "Parent destination for content moderator tests.",
            Status = PublishedStatus,
            DestinationTypeId = destinationTypeId,
            RegionId = regionId,
            Latitude = 44.1,
            Longitude = 20.1,
            CreatedAt = now.AddHours(-2),
            PublishedAt = now.AddHours(-2)
        };
        db.Destinations.Add(parentDestination);
        await db.SaveChangesAsync();

        var parentLocation = new Location
        {
            Name = $"Entity review parent location {Guid.NewGuid():N}",
            Summary = "Parent location for content moderator tests.",
            Description = "Parent location for content moderator tests.",
            Status = PublishedStatus,
            DestinationId = parentDestination.Id,
            LocationTypeId = locationTypeId,
            Latitude = 44.2,
            Longitude = 20.2,
            CreatedAt = now.AddHours(-1),
            PublishedAt = now.AddHours(-1)
        };
        db.Locations.Add(parentLocation);
        await db.SaveChangesAsync();

        var publishedIds = new Dictionary<string, List<int>>
        {
            ["destination"] = new(),
            ["location"] = new(),
            ["place"] = new(),
            ["event"] = new(),
        };

        for (var index = 0; index < publishedCount; index++)
        {
            var destination = new Destination
            {
                Name = $"{prefix}-destination-published-{index:00}",
                Summary = $"{prefix} destination summary {index}",
                Description = $"{prefix} destination description {index}",
                Status = PublishedStatus,
                DestinationTypeId = destinationTypeId,
                RegionId = regionId,
                Latitude = 43 + index * 0.01,
                Longitude = 20 + index * 0.01,
                CreatedAt = now.AddMinutes(index),
                PublishedAt = now.AddMinutes(index)
            };
            db.Destinations.Add(destination);

            var location = new Location
            {
                Name = $"{prefix}-location-published-{index:00}",
                Summary = $"{prefix} location summary {index}",
                Description = $"{prefix} location description {index}",
                Status = PublishedStatus,
                DestinationId = parentDestination.Id,
                LocationTypeId = locationTypeId,
                Latitude = 44 + index * 0.01,
                Longitude = 21 + index * 0.01,
                CreatedAt = now.AddMinutes(index),
                PublishedAt = now.AddMinutes(index)
            };
            db.Locations.Add(location);

            var place = new Place
            {
                Name = $"{prefix}-place-published-{index:00}",
                Subtitle = $"{prefix} place subtitle {index}",
                Description = $"{prefix} place description {index}",
                Status = PublishedStatus,
                LocationId = parentLocation.Id,
                PlaceTypeId = placeTypeId,
                Latitude = 45 + index * 0.01,
                Longitude = 22 + index * 0.01,
                CreatedAt = now.AddMinutes(index),
                PublishedAt = now.AddMinutes(index)
            };
            db.Places.Add(place);

            var @event = new CoreEvent
            {
                Name = $"{prefix}-event-published-{index:00}",
                Summary = $"{prefix} event summary {index}",
                Description = $"{prefix} event description {index}",
                Status = PublishedStatus,
                EventTypeId = eventTypeId,
                DestinationId = parentDestination.Id,
                LocationId = parentLocation.Id,
                ParentEntityType = "Location",
                VenueName = $"{prefix} venue {index}",
                StartDate = now.AddDays(index + 1),
                EndDate = now.AddDays(index + 1).AddHours(2),
                CreatedAt = now.AddMinutes(index),
                PublishedAt = now.AddMinutes(index)
            };
            db.Events.Add(@event);
            await db.SaveChangesAsync();

            publishedIds["destination"].Add(destination.Id);
            publishedIds["location"].Add(location.Id);
            publishedIds["place"].Add(place.Id);
            publishedIds["event"].Add(@event.Id);
        }

        db.Destinations.AddRange(
            BuildDestination($"{prefix}-destination-suspended", SuspendedStatus, destinationTypeId, regionId, now.AddMinutes(-10)),
            BuildDestination($"{prefix}-destination-deleted", DeletedStatus, destinationTypeId, regionId, now.AddMinutes(-20)));
        db.Locations.AddRange(
            BuildLocation($"{prefix}-location-suspended", SuspendedStatus, parentDestination.Id, locationTypeId, now.AddMinutes(-10)),
            BuildLocation($"{prefix}-location-deleted", DeletedStatus, parentDestination.Id, locationTypeId, now.AddMinutes(-20)));
        db.Places.AddRange(
            BuildPlace($"{prefix}-place-suspended", SuspendedStatus, parentLocation.Id, placeTypeId, now.AddMinutes(-10)),
            BuildPlace($"{prefix}-place-deleted", DeletedStatus, parentLocation.Id, placeTypeId, now.AddMinutes(-20)));
        db.Events.AddRange(
            BuildEvent($"{prefix}-event-suspended", SuspendedStatus, parentDestination.Id, parentLocation.Id, eventTypeId, now.AddMinutes(-10)),
            BuildEvent($"{prefix}-event-deleted", DeletedStatus, parentDestination.Id, parentLocation.Id, eventTypeId, now.AddMinutes(-20)));

        await db.SaveChangesAsync();

        return new EntityReviewFixture(publishedIds);
    }

    private static Destination BuildDestination(
        string name,
        string status,
        int destinationTypeId,
        int regionId,
        DateTime createdAt)
    {
        return new Destination
        {
            Name = name,
            Summary = $"{name} summary",
            Description = $"{name} description",
            Status = status,
            DestinationTypeId = destinationTypeId,
            RegionId = regionId,
            Latitude = 43.2,
            Longitude = 20.3,
            CreatedAt = createdAt,
            PublishedAt = status == PublishedStatus ? createdAt : null
        };
    }

    private static Location BuildLocation(
        string name,
        string status,
        int destinationId,
        int locationTypeId,
        DateTime createdAt)
    {
        return new Location
        {
            Name = name,
            Summary = $"{name} summary",
            Description = $"{name} description",
            Status = status,
            DestinationId = destinationId,
            LocationTypeId = locationTypeId,
            Latitude = 44.2,
            Longitude = 20.4,
            CreatedAt = createdAt,
            PublishedAt = status == PublishedStatus ? createdAt : null
        };
    }

    private static Place BuildPlace(
        string name,
        string status,
        int locationId,
        int placeTypeId,
        DateTime createdAt)
    {
        return new Place
        {
            Name = name,
            Subtitle = $"{name} subtitle",
            Description = $"{name} description",
            Status = status,
            LocationId = locationId,
            PlaceTypeId = placeTypeId,
            Latitude = 45.2,
            Longitude = 21.3,
            CreatedAt = createdAt,
            PublishedAt = status == PublishedStatus ? createdAt : null
        };
    }

    private static CoreEvent BuildEvent(
        string name,
        string status,
        int destinationId,
        int locationId,
        int eventTypeId,
        DateTime createdAt)
    {
        return new CoreEvent
        {
            Name = name,
            Summary = $"{name} summary",
            Description = $"{name} description",
            Status = status,
            EventTypeId = eventTypeId,
            DestinationId = destinationId,
            LocationId = locationId,
            ParentEntityType = "Location",
            VenueName = $"{name} venue",
            StartDate = createdAt.AddDays(2),
            EndDate = createdAt.AddDays(2).AddHours(2),
            CreatedAt = createdAt,
            PublishedAt = status == PublishedStatus ? createdAt : null
        };
    }

    private static async Task AssertEntityStatusAsync(
        GeotrackApiFactory app,
        string entityType,
        int entityId,
        string expectedStatus)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var actual = entityType switch
        {
            "destination" => await db.Destinations.Where(entity => entity.Id == entityId).Select(entity => entity.Status).SingleAsync(),
            "location" => await db.Locations.Where(entity => entity.Id == entityId).Select(entity => entity.Status).SingleAsync(),
            "place" => await db.Places.Where(entity => entity.Id == entityId).Select(entity => entity.Status).SingleAsync(),
            "event" => await db.Events.Where(entity => entity.Id == entityId).Select(entity => entity.Status).SingleAsync(),
            _ => throw new InvalidOperationException("Unknown entity type.")
        };

        Assert.Equal(expectedStatus, actual);
    }

    private static string PublicDetailRoute(string entityType, int entityId)
    {
        return entityType switch
        {
            "destination" => $"/api/destinations/{entityId}",
            "location" => $"/api/locations/{entityId}",
            "place" => $"/api/places/{entityId}",
            "event" => $"/api/events/{entityId}",
            _ => throw new InvalidOperationException("Unknown entity type.")
        };
    }

    private static string UserAppDetailRoute(string entityType, int entityId)
    {
        return entityType switch
        {
            "destination" => $"/destinations/{entityId}",
            "location" => $"/locations/{entityId}",
            "place" => $"/places/{entityId}",
            "event" => $"/events/{entityId}",
            _ => throw new InvalidOperationException("Unknown entity type.")
        };
    }

    private static async Task<int> AddPublishedStayPlaceAsync(GeotrackApiFactory app, string name)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var regionId = await db.Regions.OrderBy(region => region.Id).Select(region => region.Id).FirstAsync();
        var destinationTypeId = await db.DestinationTypes.OrderBy(type => type.Id).Select(type => type.Id).FirstAsync();
        var locationTypeId = await db.LocationTypes.OrderBy(type => type.Id).Select(type => type.Id).FirstAsync();
        var placeTypeId = await db.PlaceTypes.OrderBy(type => type.Id).Select(type => type.Id).FirstAsync();
        var stayTypeId = await db.StayTypes.OrderBy(type => type.Id).Select(type => type.Id).FirstAsync();
        var now = DateTime.UtcNow.AddDays(-1);

        var destination = new Destination
        {
            Name = $"{name}-destination",
            Summary = "Stay parent destination.",
            Description = "Stay parent destination.",
            Status = PublishedStatus,
            DestinationTypeId = destinationTypeId,
            RegionId = regionId,
            Latitude = 44.1,
            Longitude = 20.1,
            CreatedAt = now,
            PublishedAt = now
        };
        db.Destinations.Add(destination);
        await db.SaveChangesAsync();

        var location = new Location
        {
            Name = $"{name}-location",
            Summary = "Stay parent location.",
            Description = "Stay parent location.",
            Status = PublishedStatus,
            DestinationId = destination.Id,
            LocationTypeId = locationTypeId,
            Latitude = 44.2,
            Longitude = 20.2,
            CreatedAt = now,
            PublishedAt = now
        };
        db.Locations.Add(location);
        await db.SaveChangesAsync();

        var place = new Place
        {
            Name = name,
            Subtitle = "Stay route place.",
            Description = "Stay route place.",
            Status = PublishedStatus,
            LocationId = location.Id,
            PlaceTypeId = placeTypeId,
            Latitude = 44.3,
            Longitude = 20.3,
            CreatedAt = now,
            PublishedAt = now
        };
        db.Places.Add(place);
        await db.SaveChangesAsync();

        db.Stays.Add(new Stay
        {
            PlaceId = place.Id,
            StayTypeId = stayTypeId
        });
        await db.SaveChangesAsync();

        return place.Id;
    }

    private static async Task<AuthenticatedClient> LoginAdminAsync(GeotrackApiFactory app)
    {
        return await LoginAdminAppAsync(app, "zika@gmail.com", "Proba123!");
    }

    private static async Task<AuthenticatedClient> LoginModeratorAsync(GeotrackApiFactory app)
    {
        return await LoginAdminAppAsync(app, "moderator@gmail.com", "Proba123!");
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

    private static async Task GrantUserRoleAsync(GeotrackApiFactory app, string email, string roleName)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = await db.Users.SingleAsync(existing => existing.Email == email);
        var role = await db.Roles.SingleAsync(existing => existing.Name == roleName);

        var hasRole = await db.UserRoles.AnyAsync(userRole =>
            userRole.UserId == user.Id &&
            userRole.RoleId == role.Id);

        if (!hasRole)
        {
            db.UserRoles.Add(new UserRole
            {
                UserId = user.Id,
                RoleId = role.Id
            });

            await db.SaveChangesAsync();
        }
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

    private sealed record EntityReviewFixture(IReadOnlyDictionary<string, List<int>> PublishedIds);

    private sealed record AuthenticatedClient(HttpClient Client, int UserId) : IDisposable
    {
        public void Dispose() => Client.Dispose();
    }
}
