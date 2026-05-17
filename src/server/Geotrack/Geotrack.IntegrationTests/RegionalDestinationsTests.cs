using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Geotrack.Application.DTOs.Admin;
using Geotrack.Application.DTOs.Auth;
using Geotrack.Domain.Entities.Core;
using Geotrack.Domain.Entities.Users;
using Geotrack.Infrastructure;
using Geotrack.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Geometry = NetTopologySuite.Geometries.Geometry;

namespace Geotrack.IntegrationTests;

public sealed class RegionalDestinationsTests
{
    [Fact]
    public async Task Regional_manager_creates_destination_in_assigned_region_with_tags_reference_and_boundary()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        var fixture = await CreateDestinationsFixtureAsync(app);

        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var destinationTags = await db.Tags
            .Where(tag => tag.ForDestination)
            .OrderBy(tag => tag.Id)
            .Take(2)
            .ToListAsync();
        Assert.Equal(2, destinationTags.Count);
        var defaultTypeId = await db.DestinationTypes
            .OrderBy(type => type.Name == "City" ? 0 : 1)
            .ThenBy(type => type.Id)
            .Select(type => type.Id)
            .FirstAsync();

        using var manager = await LoginAsync(app, "elena@example.com", "password123");
        var response = await manager.PostAsJsonAsync(
            "/api/admin/regional/destinations",
            BuildCreateRequest(fixture.AssignedRegionId, destinationTags.Select(tag => tag.Id).ToArray()));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<RegionalDestinationDetailDto>();
        Assert.NotNull(created);
        Assert.Equal(fixture.AssignedRegionId, created!.RegionId);
        Assert.Equal("Published", created.Status);
        Assert.Equal("New Regional Destination", created.Name);
        Assert.Equal("Short destination summary.", created.Summary);
        Assert.StartsWith("DEST-", created.InternalReferenceId);
        Assert.Equal(defaultTypeId, created.DestinationTypeId);
        Assert.Equal(destinationTags.Select(tag => tag.Id).OrderBy(id => id), created.Tags.Select(tag => tag.Id).OrderBy(id => id));
        Assert.NotNull(created.PublishedAt);

        var stored = await db.Destinations
            .Include(destination => destination.DestinationTags)
            .SingleAsync(destination => destination.Id == created.Id);

        Assert.Equal(fixture.AssignedRegionId, stored.RegionId);
        Assert.Equal(fixture.ManagerUserId, stored.CreatedByUserId);
        Assert.Equal(created.InternalReferenceId, stored.InternalReferenceId);
        Assert.Equal("North boundary note.", stored.BoundaryNote);
        Assert.Equal("Polygon", stored.BoundaryShapeType);
        Assert.Equal(2, stored.DestinationTags.Count);

        var boundary = db.Entry(stored)
            .Property<Geometry?>("BoundaryGeometry")
            .CurrentValue;
        Assert.NotNull(boundary);
        Assert.Equal(4326, boundary!.SRID);
        Assert.False(boundary.IsEmpty);

        var list = await GetDestinationsAsync(manager, fixture.AssignedRegionId, search: created.InternalReferenceId);
        Assert.Contains(list.Items, item => item.Id == created.Id);
    }

    [Fact]
    public async Task Create_destination_requires_regional_manager_and_assigned_region()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        var fixture = await CreateDestinationsFixtureAsync(app);

        using var anonymous = app.CreateClient();
        var anonymousResponse = await anonymous.PostAsJsonAsync(
            "/api/admin/regional/destinations",
            BuildCreateRequest(fixture.AssignedRegionId));
        Assert.Equal(HttpStatusCode.Unauthorized, anonymousResponse.StatusCode);

        using var regularUser = await LoginAsync(app, "marko.p@example.com", "password123");
        var regularUserResponse = await regularUser.PostAsJsonAsync(
            "/api/admin/regional/destinations",
            BuildCreateRequest(fixture.AssignedRegionId));
        Assert.Equal(HttpStatusCode.Forbidden, regularUserResponse.StatusCode);

        using var manager = await LoginAsync(app, "elena@example.com", "password123");
        var unassignedRegionResponse = await manager.PostAsJsonAsync(
            "/api/admin/regional/destinations",
            BuildCreateRequest(fixture.UnassignedRegionId));
        Assert.Equal(HttpStatusCode.Forbidden, unassignedRegionResponse.StatusCode);
    }

    [Fact]
    public async Task Create_destination_validates_reference_required_fields_summary_tags_and_geometry()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        var fixture = await CreateDestinationsFixtureAsync(app);

        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var nonDestinationTag = await db.Tags.FirstAsync(tag => !tag.ForDestination);

        using var manager = await LoginAsync(app, "elena@example.com", "password123");

        var tamperedReference = BuildCreateRequest(fixture.AssignedRegionId);
        tamperedReference.InternalReferenceId = "CLIENT-123";
        var tamperedResponse = await manager.PostAsJsonAsync("/api/admin/regional/destinations", tamperedReference);
        Assert.Equal(HttpStatusCode.BadRequest, tamperedResponse.StatusCode);

        var missingName = BuildCreateRequest(fixture.AssignedRegionId);
        missingName.Name = "";
        var missingNameResponse = await manager.PostAsJsonAsync("/api/admin/regional/destinations", missingName);
        Assert.Equal(HttpStatusCode.BadRequest, missingNameResponse.StatusCode);

        var longSummary = BuildCreateRequest(fixture.AssignedRegionId);
        longSummary.Summary = new string('x', 161);
        var longSummaryResponse = await manager.PostAsJsonAsync("/api/admin/regional/destinations", longSummary);
        Assert.Equal(HttpStatusCode.BadRequest, longSummaryResponse.StatusCode);

        var invalidTag = BuildCreateRequest(fixture.AssignedRegionId, [nonDestinationTag.Id]);
        var invalidTagResponse = await manager.PostAsJsonAsync("/api/admin/regional/destinations", invalidTag);
        Assert.Equal(HttpStatusCode.BadRequest, invalidTagResponse.StatusCode);

        var invalidGeometry = BuildCreateRequest(fixture.AssignedRegionId);
        invalidGeometry.Boundary = new RegionalDestinationBoundaryDto
        {
            ShapeType = "Polygon",
            Coordinates =
            [
                new() { Latitude = 95, Longitude = 19 },
                new() { Latitude = 43, Longitude = 19.2 },
                new() { Latitude = 43.2, Longitude = 19.1 }
            ]
        };
        var invalidGeometryResponse = await manager.PostAsJsonAsync("/api/admin/regional/destinations", invalidGeometry);
        Assert.Equal(HttpStatusCode.BadRequest, invalidGeometryResponse.StatusCode);
    }

    [Fact]
    public async Task Destination_tag_endpoint_returns_only_destination_compatible_tags_and_supports_search()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        _ = await CreateDestinationsFixtureAsync(app);

        using var anonymous = app.CreateClient();
        var anonymousResponse = await anonymous.GetAsync("/api/admin/regional/destination-tags");
        Assert.Equal(HttpStatusCode.Unauthorized, anonymousResponse.StatusCode);

        using var regularUser = await LoginAsync(app, "marko.p@example.com", "password123");
        var regularUserResponse = await regularUser.GetAsync("/api/admin/regional/destination-tags");
        Assert.Equal(HttpStatusCode.Forbidden, regularUserResponse.StatusCode);

        using var manager = await LoginAsync(app, "elena@example.com", "password123");
        var allTags = await manager.GetFromJsonAsync<List<RegionalDestinationTagDto>>(
            "/api/admin/regional/destination-tags");
        Assert.NotNull(allTags);
        Assert.NotEmpty(allTags!);

        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var returnedIds = allTags.Select(tag => tag.Id).ToList();
        var returnedTagRows = await db.Tags
            .Where(tag => returnedIds.Contains(tag.Id))
            .Select(tag => new { tag.Id, tag.ForDestination })
            .ToListAsync();
        Assert.Equal(returnedIds.Count, returnedTagRows.Count);
        Assert.All(returnedTagRows, tag => Assert.True(tag.ForDestination));

        var nonDestinationTag = await db.Tags.FirstAsync(tag => !tag.ForDestination);
        Assert.DoesNotContain(allTags, tag => tag.Id == nonDestinationTag.Id);

        var searchableTag = allTags.First();
        var searchResponse = await manager.GetFromJsonAsync<List<RegionalDestinationTagDto>>(
            "/api/admin/regional/destination-tags?search=" + Uri.EscapeDataString(searchableTag.Name));
        Assert.NotNull(searchResponse);
        Assert.Contains(searchResponse!, tag => tag.Id == searchableTag.Id);

        var searchIds = searchResponse!.Select(tag => tag.Id).ToList();
        var searchedTagRows = await db.Tags
            .Where(tag => searchIds.Contains(tag.Id))
            .Select(tag => new { tag.Id, tag.ForDestination })
            .ToListAsync();
        Assert.All(searchedTagRows, tag => Assert.True(tag.ForDestination));
    }

    [Fact]
    public async Task Generated_internal_reference_ids_are_unique()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        var fixture = await CreateDestinationsFixtureAsync(app);
        using var manager = await LoginAsync(app, "elena@example.com", "password123");

        var firstResponse = await manager.PostAsJsonAsync(
            "/api/admin/regional/destinations",
            BuildCreateRequest(fixture.AssignedRegionId, name: "Unique Destination One"));
        firstResponse.EnsureSuccessStatusCode();

        var secondResponse = await manager.PostAsJsonAsync(
            "/api/admin/regional/destinations",
            BuildCreateRequest(fixture.AssignedRegionId, name: "Unique Destination Two"));
        secondResponse.EnsureSuccessStatusCode();

        var first = await firstResponse.Content.ReadFromJsonAsync<RegionalDestinationDetailDto>();
        var second = await secondResponse.Content.ReadFromJsonAsync<RegionalDestinationDetailDto>();
        Assert.NotNull(first?.InternalReferenceId);
        Assert.NotNull(second?.InternalReferenceId);
        Assert.NotEqual(first!.InternalReferenceId, second!.InternalReferenceId);
    }

    [Fact]
    public async Task Destination_image_upload_and_create_persist_entity_images()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        var fixture = await CreateDestinationsFixtureAsync(app);

        using var manager = await LoginAsync(app, "elena@example.com", "password123");
        var upload = await UploadOneImageAsync(manager, fixture.AssignedRegionId);

        var request = BuildCreateRequest(fixture.AssignedRegionId, name: "Destination With Image");
        request.Images =
        [
            new()
            {
                Url = upload.Url,
                AltText = "Destination cover",
                IsMain = true,
                SortOrder = 0
            }
        ];

        var response = await manager.PostAsJsonAsync("/api/admin/regional/destinations", request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var created = await response.Content.ReadFromJsonAsync<RegionalDestinationDetailDto>();
        Assert.NotNull(created);
        Assert.Equal(upload.Url, created!.ImageUrl);
        Assert.Single(created.Images);

        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var storedImages = await db.EntityImages
            .Where(image => image.DestinationId == created.Id)
            .OrderBy(image => image.Id)
            .ToListAsync();
        var image = Assert.Single(storedImages);
        Assert.Equal(EntityImageTargetType.Destination, image.TargetType);
        Assert.Equal(upload.Url, image.Url);
        Assert.True(image.IsMain);
    }

    [Fact]
    public async Task Destination_main_image_selection_is_deterministic()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        var fixture = await CreateDestinationsFixtureAsync(app);

        using var manager = await LoginAsync(app, "elena@example.com", "password123");
        var firstUpload = await UploadOneImageAsync(manager, fixture.AssignedRegionId);
        var secondUpload = await UploadOneImageAsync(manager, fixture.AssignedRegionId);

        var request = BuildCreateRequest(fixture.AssignedRegionId, name: "Destination With Ordered Images");
        request.Images =
        [
            new() { Url = firstUpload.Url, IsMain = true, SortOrder = 20 },
            new() { Url = secondUpload.Url, IsMain = true, SortOrder = 10 }
        ];

        var response = await manager.PostAsJsonAsync("/api/admin/regional/destinations", request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var created = await response.Content.ReadFromJsonAsync<RegionalDestinationDetailDto>();
        Assert.NotNull(created);
        Assert.Equal(firstUpload.Url, created!.ImageUrl);
        Assert.Equal(firstUpload.Url, Assert.Single(created.Images, image => image.IsMain).Url);
    }

    [Fact]
    public async Task Regional_manager_lists_only_assigned_region_destinations_with_default_25_item_page()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        var fixture = await CreateDestinationsFixtureAsync(app);

        using var anonymous = app.CreateClient();
        var anonymousResponse = await anonymous.GetAsync(
            $"/api/admin/regional/destinations?regionId={fixture.AssignedRegionId}");
        Assert.Equal(HttpStatusCode.Unauthorized, anonymousResponse.StatusCode);

        using var regularUser = await LoginAsync(app, "marko.p@example.com", "password123");
        var nonRegionalResponse = await regularUser.GetAsync(
            $"/api/admin/regional/destinations?regionId={fixture.AssignedRegionId}");
        Assert.Equal(HttpStatusCode.Forbidden, nonRegionalResponse.StatusCode);

        using var manager = await LoginAsync(app, "elena@example.com", "password123");
        var unassignedRegionResponse = await manager.GetAsync(
            $"/api/admin/regional/destinations?regionId={fixture.UnassignedRegionId}");
        Assert.Equal(HttpStatusCode.Forbidden, unassignedRegionResponse.StatusCode);

        var page = await GetDestinationsAsync(manager, fixture.AssignedRegionId);

        Assert.Equal(30, page.TotalCount);
        Assert.Equal(0, page.Skip);
        Assert.Equal(25, page.Take);
        Assert.Equal(25, page.Items.Count);
        Assert.All(page.Items, item => Assert.Equal(fixture.AssignedRegionId, item.RegionId));
        Assert.DoesNotContain(page.Items, item => item.Id == fixture.UnassignedDestinationId);
    }

    [Fact]
    public async Task Pagination_search_and_sort_use_backend_query_parameters()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        var fixture = await CreateDestinationsFixtureAsync(app);

        using var manager = await LoginAsync(app, "elena@example.com", "password123");

        var nextPage = await GetDestinationsAsync(manager, fixture.AssignedRegionId, skip: 25, take: 10);
        Assert.Equal(30, nextPage.TotalCount);
        Assert.Equal(25, nextPage.Skip);
        Assert.Equal(10, nextPage.Take);
        Assert.Equal(5, nextPage.Items.Count);

        var searchedByName = await GetDestinationsAsync(
            manager,
            fixture.AssignedRegionId,
            search: "Regional Destination 07");
        Assert.Single(searchedByName.Items);
        Assert.Equal(fixture.SearchDestinationId, searchedByName.Items[0].Id);

        var searchedBySummary = await GetDestinationsAsync(
            manager,
            fixture.AssignedRegionId,
            search: "sunlit orchards");
        Assert.Single(searchedBySummary.Items);
        Assert.Equal(fixture.SearchDestinationId, searchedBySummary.Items[0].Id);

        var newest = await GetDestinationsAsync(manager, fixture.AssignedRegionId, sort: "newest", take: 3);
        Assert.Equal("Regional Destination 00", newest.Items[0].Name);

        var oldest = await GetDestinationsAsync(manager, fixture.AssignedRegionId, sort: "oldest", take: 3);
        Assert.Equal("Regional Destination 29", oldest.Items[0].Name);

        var nameAsc = await GetDestinationsAsync(manager, fixture.AssignedRegionId, sort: "nameAsc", take: 3);
        Assert.Equal("Regional Destination 00", nameAsc.Items[0].Name);

        var nameDesc = await GetDestinationsAsync(manager, fixture.AssignedRegionId, sort: "nameDesc", take: 3);
        Assert.Equal("Regional Destination 29", nameDesc.Items[0].Name);
    }

    [Fact]
    public async Task Delete_destination_is_region_scoped_and_blocks_related_content()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        var fixture = await CreateDestinationsFixtureAsync(app);

        using var manager = await LoginAsync(app, "elena@example.com", "password123");

        var outOfRegionResponse = await manager.DeleteAsync(
            $"/api/admin/regional/destinations/{fixture.UnassignedDestinationId}?regionId={fixture.AssignedRegionId}");
        Assert.Equal(HttpStatusCode.NotFound, outOfRegionResponse.StatusCode);

        var blockedResponse = await manager.DeleteAsync(
            $"/api/admin/regional/destinations/{fixture.DestinationWithContentId}?regionId={fixture.AssignedRegionId}");
        Assert.Equal(HttpStatusCode.Conflict, blockedResponse.StatusCode);
        var blockedBody = await blockedResponse.Content.ReadFromJsonAsync<Dictionary<string, string>>();
        Assert.Equal("This destination cannot be deleted while it has related content.", blockedBody?["message"]);

        var deleteResponse = await manager.DeleteAsync(
            $"/api/admin/regional/destinations/{fixture.BareDestinationId}?regionId={fixture.AssignedRegionId}");
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.False(await db.Destinations.AnyAsync(item => item.Id == fixture.BareDestinationId));
    }

    [Fact]
    public async Task Detail_and_update_are_restricted_to_assigned_region()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        var fixture = await CreateDestinationsFixtureAsync(app);

        using var manager = await LoginAsync(app, "elena@example.com", "password123");

        var outDetail = await manager.GetAsync(
            $"/api/admin/regional/destinations/{fixture.UnassignedDestinationId}?regionId={fixture.AssignedRegionId}");
        Assert.Equal(HttpStatusCode.NotFound, outDetail.StatusCode);

        var updateResponse = await manager.PutAsJsonAsync(
            $"/api/admin/regional/destinations/{fixture.SearchDestinationId}?regionId={fixture.AssignedRegionId}",
            new RegionalDestinationUpdateRequestDto
            {
                Name = "Regional Destination 07 Updated",
                Description = "Updated destination description."
            });
        updateResponse.EnsureSuccessStatusCode();

        var updated = await updateResponse.Content.ReadFromJsonAsync<RegionalDestinationDetailDto>();
        Assert.NotNull(updated);
        Assert.Equal(fixture.AssignedRegionId, updated!.RegionId);
        Assert.Equal("Regional Destination 07 Updated", updated.Name);
        Assert.Equal("Updated destination description.", updated.Description);
    }

    private static async Task<RegionalDestinationListResponseDto> GetDestinationsAsync(
        HttpClient client,
        int regionId,
        string? search = null,
        string sort = "newest",
        int skip = 0,
        int? take = null)
    {
        var url = $"/api/admin/regional/destinations?regionId={regionId}&sort={sort}&skip={skip}";
        if (take.HasValue)
        {
            url += $"&take={take.Value}";
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            url += "&search=" + Uri.EscapeDataString(search);
        }

        var page = await client.GetFromJsonAsync<RegionalDestinationListResponseDto>(url);
        Assert.NotNull(page);
        return page!;
    }

    private static async Task<HttpClient> LoginAsync(GeotrackApiFactory app, string email, string password)
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

    private static RegionalDestinationCreateRequestDto BuildCreateRequest(
        int regionId,
        int[]? tagIds = null,
        string name = "New Regional Destination")
    {
        return new RegionalDestinationCreateRequestDto
        {
            RegionId = regionId,
            Name = name,
            Summary = "Short destination summary.",
            Description = "Full destination description.",
            Status = "Published",
            BoundaryNote = "North boundary note.",
            Boundary = new RegionalDestinationBoundaryDto
            {
                ShapeType = "Polygon",
                Coordinates =
                [
                    new() { Latitude = 43.0, Longitude = 19.0 },
                    new() { Latitude = 43.0, Longitude = 19.2 },
                    new() { Latitude = 43.2, Longitude = 19.2 },
                    new() { Latitude = 43.2, Longitude = 19.0 }
                ]
            },
            TagIds = tagIds?.ToList() ?? []
        };
    }

    private static async Task<DestinationsFixture> CreateDestinationsFixtureAsync(GeotrackApiFactory app)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var now = DateTime.UtcNow;
        var territory = await db.Territories.OrderBy(item => item.Id).FirstAsync();
        var destinationType = await db.DestinationTypes.OrderBy(item => item.Id).FirstAsync();
        var locationType = await db.LocationTypes.OrderBy(item => item.Id).FirstAsync();
        var manager = await db.Users.SingleAsync(item => item.Email == "elena@example.com");

        var assignedRegion = new Region
        {
            Name = $"Destinations Assigned {Guid.NewGuid():N}",
            TerritoryId = territory.Id,
            CreatedAt = now
        };
        var unassignedRegion = new Region
        {
            Name = $"Destinations Unassigned {Guid.NewGuid():N}",
            TerritoryId = territory.Id,
            CreatedAt = now
        };
        db.Regions.AddRange(assignedRegion, unassignedRegion);
        await db.SaveChangesAsync();

        await GrantRegionalManagerCoverageAsync(db, manager, assignedRegion.Id);

        var destinations = Enumerable.Range(0, 30)
            .Select(index => new Destination
            {
                Name = $"Regional Destination {index:D2}",
                DestinationTypeId = destinationType.Id,
                RegionId = assignedRegion.Id,
                Description = index == 7
                    ? "Searchable destination summary with sunlit orchards."
                    : $"Destination description {index:D2}.",
                Latitude = 42 + index / 100d,
                Longitude = 18 + index / 100d,
                CreatedAt = now.AddMinutes(-index)
            })
            .ToList();

        var unassignedDestination = new Destination
        {
            Name = "Unassigned Regional Destination",
            DestinationTypeId = destinationType.Id,
            RegionId = unassignedRegion.Id,
            Description = "This destination must never appear in assigned-region results.",
            Latitude = 45,
            Longitude = 19,
            CreatedAt = now
        };

        db.Destinations.AddRange(destinations);
        db.Destinations.Add(unassignedDestination);
        await db.SaveChangesAsync();

        db.Locations.Add(new Location
        {
            Name = "Destination Related Location",
            DestinationId = destinations[29].Id,
            LocationTypeId = locationType.Id,
            Latitude = 42.9,
            Longitude = 18.9,
            CreatedAt = now
        });
        await db.SaveChangesAsync();

        return new DestinationsFixture(
            assignedRegion.Id,
            unassignedRegion.Id,
            manager.Id,
            destinations[7].Id,
            destinations[28].Id,
            destinations[29].Id,
            unassignedDestination.Id);
    }

    private static async Task GrantRegionalManagerCoverageAsync(AppDbContext db, User manager, int regionId)
    {
        var role = await db.Roles.SingleAsync(item => item.Name == "RegionalManager");

        if (!await db.UserRoles.AnyAsync(item => item.UserId == manager.Id && item.RoleId == role.Id))
        {
            db.UserRoles.Add(new UserRole
            {
                UserId = manager.Id,
                RoleId = role.Id
            });
        }

        if (!await db.RegionalManagerRegions.AnyAsync(item => item.UserId == manager.Id && item.RegionId == regionId))
        {
            db.RegionalManagerRegions.Add(new RegionalManagerRegion
            {
                UserId = manager.Id,
                RegionId = regionId,
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            });
        }

        await db.SaveChangesAsync();
    }

    private static async Task<RegionalImageUploadResultDto> UploadOneImageAsync(HttpClient client, int regionId)
    {
        using var form = new MultipartFormDataContent();
        using var content = new ByteArrayContent(TestPngBytes());
        content.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        form.Add(content, "files", "cover.png");

        var response = await client.PostAsync($"/api/admin/regional/media/images?regionId={regionId}", form);
        response.EnsureSuccessStatusCode();

        var uploaded = await response.Content.ReadFromJsonAsync<RegionalImageUploadResponseDto>();
        Assert.NotNull(uploaded);
        var image = Assert.Single(uploaded!.Images);
        Assert.StartsWith("/media/uploads/regional/", image.Url);
        return image;
    }

    private static byte[] TestPngBytes() =>
        Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+/p9sAAAAASUVORK5CYII=");

    private sealed record DestinationsFixture(
        int AssignedRegionId,
        int UnassignedRegionId,
        int ManagerUserId,
        int SearchDestinationId,
        int BareDestinationId,
        int DestinationWithContentId,
        int UnassignedDestinationId);
}
