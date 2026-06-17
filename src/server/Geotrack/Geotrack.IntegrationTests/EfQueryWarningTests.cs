using Geotrack.Application.DTOs.Agent;
using Geotrack.Application.DTOs.Admin;
using Geotrack.Application.DTOs.Favorites;
using Geotrack.Application.DTOs.GeoTrackTools;
using Geotrack.Application.DTOs.Notifications;
using Geotrack.Application.Interfaces.Service;
using Geotrack.Application.Options;
using Geotrack.Application.Security;
using Geotrack.Application.Services;
using Geotrack.Domain.Entities.Core;
using Geotrack.Domain.Entities.Reviews;
using Geotrack.Domain.Entities.Users;
using Geotrack.Infrastructure;
using Geotrack.Infrastructure.Repositories;
using Geotrack.Infrastructure.Services;
using Geotrack.Infrastructure.Services.Agent;
using Geotrack.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Geotrack.IntegrationTests;

public sealed class EfQueryWarningTests
{
    [Fact]
    public async Task Public_search_queries_do_not_emit_ef_row_limiting_or_collection_warnings()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        var fixture = await CreateWarningFixtureAsync(app);
        var connectionString = await GetConnectionStringAsync(app);

        await using var strictDb = CreateStrictWarningDbContext(connectionString);

        var globalSearch = new SearchService(strictDb);
        var globalResult = await globalSearch.SearchAsync(
            "warning",
            "destinations,places,stays,events",
            take: 5);

        var discoverySearch = new DestinationDiscoverySearchService(strictDb);
        var discoveryResult = await discoverySearch.SearchAsync("warning", skip: 0, take: 5);

        Assert.True(globalResult.TotalCount >= 0);
        Assert.Contains(discoveryResult.Items, item => item.Id == fixture.DestinationId);
    }

    [Fact]
    public async Task Regional_management_queries_do_not_emit_ef_collection_warnings()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        var fixture = await CreateWarningFixtureAsync(app);
        var connectionString = await GetConnectionStringAsync(app);

        await using var strictDb = CreateStrictWarningDbContext(connectionString);
        var scopeValidation = new RegionalScopeValidationService(
            strictDb,
            Options.Create(new RegionalScopeValidationOptions()));

        var sanitizer = new NoopContentHtmlSanitizer();
        var destinationResolver = new DestinationBoundaryResolver(strictDb);
        using var httpServices = new ServiceCollection()
            .AddHttpClient()
            .BuildServiceProvider();
        var destinations = new RegionalDestinationService(
            strictDb,
            scopeValidation,
            sanitizer,
            httpServices.GetRequiredService<IHttpClientFactory>(),
            destinationResolver,
            NullLogger<RegionalDestinationService>.Instance);
        var destinationList = await destinations.GetDestinationsAsync(
            fixture.ManagerUserId,
            fixture.RegionId,
            search: null,
            status: "all",
            destinationTypeId: null,
            tagIds: null,
            issues: null,
            sort: "newest",
            skip: 0,
            take: 5);
        var destinationDetail = await destinations.GetDestinationAsync(
            fixture.ManagerUserId,
            fixture.RegionId,
            fixture.DestinationId);

        var locations = new RegionalLocationService(strictDb, scopeValidation, sanitizer, destinationResolver);
        var locationList = await locations.GetLocationsAsync(
            fixture.ManagerUserId,
            fixture.RegionId,
            search: null,
            status: "all",
            destinationId: null,
            locationTypeId: null,
            boundaryState: null,
            tagIds: null,
            issues: null,
            sort: "newest",
            skip: 0,
            take: 5);
        var locationDetail = await locations.GetLocationAsync(
            fixture.ManagerUserId,
            fixture.RegionId,
            fixture.LocationId);

        var places = new RegionalPlaceService(strictDb, scopeValidation, sanitizer, destinationResolver);
        var placeList = await places.GetPlacesAsync(
            fixture.ManagerUserId,
            fixture.RegionId,
            search: null,
            status: "all",
            placeType: null,
            destinationId: null,
            locationId: null,
            placeTypeId: null,
            tagIds: null,
            issues: null,
            sort: "newest",
            skip: 0,
            take: 5);
        var placeDetail = await places.GetPlaceAsync(
            fixture.ManagerUserId,
            fixture.RegionId,
            fixture.PlaceId);

        var events = new RegionalEventService(strictDb, scopeValidation, sanitizer);
        var eventList = await events.GetEventsAsync(
            fixture.ManagerUserId,
            fixture.RegionId,
            search: null,
            status: "all",
            eventType: null,
            destinationId: null,
            locationId: null,
            placeId: null,
            eventTypeId: null,
            dateFilter: null,
            dateFrom: null,
            dateTo: null,
            priceFilter: null,
            tagIds: null,
            sort: "newest",
            skip: 0,
            take: 5);
        var eventDetail = await events.GetEventAsync(
            fixture.ManagerUserId,
            fixture.RegionId,
            fixture.EventId);

        Assert.Contains(destinationList.Items, item => item.Id == destinationDetail.Id);
        Assert.Contains(locationList.Items, item => item.Id == locationDetail.Id);
        Assert.Contains(placeList.Items, item => item.Id == placeDetail.Id);
        Assert.Contains(eventList.Items, item => item.Id == eventDetail.Id);
    }

    [Fact]
    public async Task Public_detail_queries_do_not_emit_ef_row_limiting_or_collection_warnings()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        var fixture = await CreateWarningFixtureAsync(app);
        var connectionString = await GetConnectionStringAsync(app);

        await using var strictDb = CreateStrictWarningDbContext(connectionString);

        var destinationService = new DestinationService(new DestinationRepository(strictDb));
        var destination = await destinationService.GetDestinationDetailsAsync(fixture.DestinationId, userId: null);
        var destinationSection = await destinationService.GetDestinationSectionAsync(fixture.DestinationId, "locations");

        var locationService = new LocationService(new LocationRepository(strictDb));
        var location = await locationService.GetLocationDetailsAsync(fixture.LocationId, userId: null);
        var locationSection = await locationService.GetLocationSectionAsync(fixture.LocationId, "places");

        var sanitizer = new NoopContentHtmlSanitizer();
        var audit = new NoopModerationAuditService();

        var placeService = new PlaceService(
            strictDb,
            new NoopFavoriteService(),
            new NoopReviewContentModerationService(),
            sanitizer,
            audit);
        var place = await placeService.GetPlaceDetailAsync(fixture.PlaceId);
        var hotel = await placeService.GetHotelDetailAsync(fixture.StayPlaceId);
        var placeMaster = await placeService.GetPlaceDetailsAsync(fixture.PlaceId, userId: null);

        var eventService = new EventService(
            strictDb,
            new NoopFavoriteService(),
            new NoopReviewContentModerationService());
        var evt = await eventService.GetEventDetailAsync(fixture.EventId);
        var eventMaster = await eventService.GetEventDetailsAsync(fixture.EventId, userId: null);
        var eventSection = await eventService.GetEventSectionAsync(fixture.EventId, "tags");

        Assert.Equal(fixture.DestinationId, destination.Id);
        Assert.Equal(fixture.DestinationId, destinationSection.DestinationId);
        Assert.Equal(fixture.LocationId, location.Id);
        Assert.Equal(fixture.LocationId, locationSection.LocationId);
        Assert.Equal(fixture.PlaceId, place?.Id);
        Assert.Equal(fixture.StayPlaceId, hotel?.Id);
        Assert.Equal(fixture.PlaceId, placeMaster.Id);
        Assert.Equal(fixture.EventId, evt?.Id);
        Assert.Equal(fixture.EventId, eventMaster.Id);
        Assert.Equal(fixture.EventId, eventSection.EventId);
    }

    [Fact]
    public async Task Explore_map_and_agent_runtime_queries_do_not_emit_ef_row_limiting_or_collection_warnings()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        var fixture = await CreateWarningFixtureAsync(app);
        var connectionString = await GetConnectionStringAsync(app);

        await using var strictDb = CreateStrictWarningDbContext(connectionString);

        var favorites = new NoopFavoriteService();
        var explore = new ExploreService(strictDb, favorites);
        var explorePage = await explore.GetExplorePageForDestinationAsync(
            fixture.DestinationId,
            fixture.ManagerUserId);
        var exploreSection = await explore.GetExploreSectionPageAsync(
            "where-to-eat",
            fixture.DestinationId,
            userId: fixture.ManagerUserId,
            skip: 0,
            take: 5);

        using var cache = new MemoryCache(new MemoryCacheOptions());
        using var httpClient = new HttpClient();
        var map = new MapService(
            httpClient,
            strictDb,
            favorites,
            cache,
            NullLogger<MapService>.Instance);
        var mapSearch = await map.SearchAsync(
            "warning",
            new[] { "destination", "location", "place", "event" });
        var mapEntities = await map.GetEntitiesAsync(
            44.1,
            20.1,
            new[] { "places", "stays", "events" },
            radiusKm: 10,
            userId: fixture.ManagerUserId);
        var mapMarkers = await map.GetMarkersInViewportAsync(
            north: 45,
            south: 43,
            east: 22,
            west: 19,
            zoom: 13,
            categories: new[] { "places", "stays", "events" },
            userId: fixture.ManagerUserId);

        var tools = new GeoTrackToolService(
            strictDb,
            new StubAgentQueryPlanner(fixture.DestinationId));
        var toolRequest = new GeoTrackToolSearchRequest
        {
            DestinationId = fixture.DestinationId,
            Query = "warning",
            Limit = 5,
            UpcomingOnly = true
        };
        var agentPlaces = await tools.SearchPlacesAsync(toolRequest);
        var agentStays = await tools.SearchStaysAsync(toolRequest);
        var agentEvents = await tools.SearchEventsAsync(toolRequest);
        var agentContext = await tools.BuildAgentContextAsync(
            "warning",
            contextOptions: null,
            maxItems: 5);

        Assert.NotNull(explorePage.SelectedDestination);
        Assert.Equal(fixture.DestinationId, exploreSection.SelectedDestination?.Id);
        Assert.Contains(mapSearch, item => item.Id == fixture.DestinationId);
        Assert.Contains(mapEntities.Markers, item => item.EntityId == fixture.PlaceId);
        Assert.Contains(mapMarkers.Markers, item => item.EntityId == fixture.PlaceId);
        Assert.Contains(agentPlaces, item => item.Id == fixture.PlaceId);
        Assert.Contains(agentStays, item => item.Id == fixture.StayPlaceId);
        Assert.Contains(agentEvents, item => item.Id == fixture.EventId);
        Assert.True(agentContext.SearchAttempted);
    }

    [Fact]
    public async Task Place_contribution_queries_do_not_emit_ef_row_limiting_or_collection_warnings()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        var fixture = await CreateWarningFixtureAsync(app);
        var connectionString = await GetConnectionStringAsync(app);

        await using var strictDb = CreateStrictWarningDbContext(connectionString);

        var service = new PlaceContributionService(
            strictDb,
            new NoopContributionMediaStorageService(),
            new NoopUserNotificationService(),
            new NoopAdminNotificationService(),
            new NoopContentHtmlSanitizer(),
            new NoopModerationAuditService(),
            new DestinationBoundaryResolver(strictDb));

        var mine = await service.GetForUserAsync(fixture.ManagerUserId);
        var pending = await service.GetPendingReviewAsync();
        var pendingDetail = await service.GetPendingReviewByIdAsync(fixture.PendingContributionId);
        var approvedOwnershipDetail = await service.GetByIdForUserAsync(
            fixture.ManagerUserId,
            fixture.ApprovedOwnershipContributionId);
        var approvedUpdate = await service.ApproveAsync(
            fixture.PendingContributionId,
            fixture.ManagerUserId,
            note: null,
            internalNote: null,
            sourceArea: "EfWarningTest");

        Assert.Contains(mine.Contributions, item => item.Id == fixture.PendingContributionId);
        Assert.Contains(pending.Contributions, item => item.Id == fixture.PendingContributionId);
        Assert.Equal(fixture.PendingContributionId, pendingDetail?.Id);
        Assert.Equal(fixture.ApprovedOwnershipContributionId, approvedOwnershipDetail?.Id);
        Assert.Equal("Approved", approvedUpdate.Status);
    }

    [Fact]
    public void Production_logging_config_keeps_http_client_pipeline_logs_at_warning()
    {
        var solutionDirectory = FindSolutionDirectory();
        var configPath = Path.Combine(
            solutionDirectory.FullName,
            "Geotrack.Server",
            "appsettings.Production.json");

        using var document = System.Text.Json.JsonDocument.Parse(File.ReadAllText(configPath));
        var logLevel = document.RootElement
            .GetProperty("Logging")
            .GetProperty("LogLevel")
            .GetProperty("System.Net.Http.HttpClient")
            .GetString();

        Assert.Equal("Warning", logLevel);
    }

    private static async Task<WarningFixture> CreateWarningFixtureAsync(GeotrackApiFactory app)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = DateTime.UtcNow;

        var territory = await db.Territories.OrderBy(territory => territory.Id).FirstAsync();
        var manager = await db.Users.SingleAsync(user => user.Email == "elena@example.com");
        var destinationTypeId = await db.DestinationTypes
            .OrderBy(type => type.Id)
            .Select(type => type.Id)
            .FirstAsync();
        var locationTypeId = await db.LocationTypes
            .OrderBy(type => type.Id)
            .Select(type => type.Id)
            .FirstAsync();
        var placeTypeId = await db.PlaceTypes
            .OrderBy(type => type.Id)
            .Select(type => type.Id)
            .FirstAsync();
        var stayTypeId = await db.StayTypes
            .OrderBy(type => type.Id)
            .Select(type => type.Id)
            .FirstAsync();
        var eventTypeId = await db.EventTypes
            .OrderBy(type => type.Id)
            .Select(type => type.Id)
            .FirstAsync();

        var region = new Region
        {
            Name = $"EF Warning Region {Guid.NewGuid():N}",
            TerritoryId = territory.Id,
            CreatedAt = now
        };
        db.Regions.Add(region);
        await db.SaveChangesAsync();

        await EnsureRegionalManagerAssignmentAsync(db, manager.Id, region.Id, now);

        var destination = new Destination
        {
            Name = $"Warning Destination {Guid.NewGuid():N}",
            Summary = "Warning query destination summary",
            Description = "A warning query destination used for EF diagnostics.",
            Status = "Published",
            DestinationTypeId = destinationTypeId,
            RegionId = region.Id,
            Latitude = 44.1,
            Longitude = 20.1,
            CreatedAt = now,
            PublishedAt = now
        };
        db.Destinations.Add(destination);
        await db.SaveChangesAsync();

        var location = new Location
        {
            Name = "Warning Location",
            Summary = "Warning location summary",
            Description = "A warning query location used for EF diagnostics.",
            Status = "Published",
            DestinationId = destination.Id,
            LocationTypeId = locationTypeId,
            Latitude = 44.11,
            Longitude = 20.11,
            CreatedAt = now,
            PublishedAt = now
        };
        db.Locations.Add(location);
        await db.SaveChangesAsync();

        var place = new Place
        {
            Name = "Warning Place",
            Subtitle = "Warning place subtitle",
            Description = "A warning query place used for EF diagnostics.",
            Status = "Published",
            PlaceTypeId = placeTypeId,
            LocationId = location.Id,
            Latitude = 44.12,
            Longitude = 20.12,
            CreatedAt = now,
            PublishedAt = now
        };
        var similarPlace = new Place
        {
            Name = "Warning Similar Place",
            Description = "A similar warning query place.",
            Status = "Published",
            PlaceTypeId = placeTypeId,
            LocationId = location.Id,
            Latitude = 44.13,
            Longitude = 20.13,
            CreatedAt = now.AddMinutes(-1),
            PublishedAt = now
        };
        var stayPlace = new Place
        {
            Name = "Warning Stay",
            Description = "A warning query stay used for EF diagnostics.",
            Status = "Published",
            PlaceTypeId = placeTypeId,
            LocationId = location.Id,
            Latitude = 44.14,
            Longitude = 20.14,
            CreatedAt = now.AddMinutes(-2),
            PublishedAt = now,
            Stay = new Stay
            {
                StayTypeId = stayTypeId,
                PricePerNight = 80m
            }
        };
        db.Places.AddRange(place, similarPlace, stayPlace);
        await db.SaveChangesAsync();

        db.EntityImages.Add(new EntityImage
        {
            TargetType = EntityImageTargetType.Place,
            PlaceId = place.Id,
            Url = "/media/seed/warning-place-card.webp",
            AltText = "Warning place",
            IsMain = true,
            ImageRole = EntityImageRole.Card,
            SortOrder = 0,
            CreatedAt = now
        });

        var updateContribution = new PlaceContribution
        {
            Type = PlaceContributionType.UpdatePlace,
            Status = PlaceContributionStatus.PendingReview,
            UserId = manager.Id,
            PlaceId = place.Id,
            PlaceTypeId = placeTypeId,
            LocationId = location.Id,
            Name = "Warning Place Updated Contribution",
            Subtitle = "Updated warning place subtitle",
            Description = "A pending update contribution used for EF diagnostics.",
            Latitude = 44.125,
            Longitude = 20.125,
            TagsJson = "[]",
            AmenitiesJson = "[]",
            StayAmenitiesJson = "[]",
            BookingPartnersJson = "[]",
            OriginalSnapshotJson = "{}",
            SubmittedAt = now.AddMinutes(1),
            CreatedAt = now.AddMinutes(1),
            UpdatedAt = now.AddMinutes(1)
        };
        updateContribution.Images.Add(new PlaceContributionImage
        {
            Url = "/media/uploads/contributions/warning-update-card.webp",
            AltText = "Warning update",
            IsMain = true,
            ImageRole = EntityImageRole.Card,
            SortOrder = 0,
            CreatedAt = now.AddMinutes(1)
        });

        var approvedOwnershipContribution = new PlaceContribution
        {
            Type = PlaceContributionType.ClaimOwnership,
            Status = PlaceContributionStatus.Approved,
            UserId = manager.Id,
            PlaceId = place.Id,
            ReviewedByUserId = manager.Id,
            ReviewedAt = now,
            ReviewNote = "Approved for EF diagnostics.",
            PlaceTypeId = placeTypeId,
            LocationId = location.Id,
            Name = place.Name,
            Subtitle = place.Subtitle,
            Description = place.Description,
            Latitude = place.Latitude,
            Longitude = place.Longitude,
            TagsJson = "[]",
            AmenitiesJson = "[]",
            StayAmenitiesJson = "[]",
            BookingPartnersJson = "[]",
            OriginalSnapshotJson = "{}",
            OwnershipProofDocumentUrl = "/media/uploads/contributions/warning-proof.pdf",
            SubmittedAt = now.AddMinutes(-1),
            CreatedAt = now.AddMinutes(-1),
            UpdatedAt = now
        };
        db.PlaceContributions.AddRange(updateContribution, approvedOwnershipContribution);
        await db.SaveChangesAsync();

        var evt = new Event
        {
            Name = "Warning Event",
            Summary = "Warning event summary",
            Description = "A warning query event used for EF diagnostics.",
            Status = "Published",
            EventTypeId = eventTypeId,
            DestinationId = destination.Id,
            LocationId = location.Id,
            ParentEntityType = "Location",
            StartDate = now.AddDays(7),
            EndDate = now.AddDays(7).AddHours(2),
            CreatedAt = now,
            PublishedAt = now
        };
        var similarEvent = new Event
        {
            Name = "Warning Similar Event",
            Description = "A similar warning query event.",
            Status = "Published",
            EventTypeId = eventTypeId,
            DestinationId = destination.Id,
            LocationId = location.Id,
            ParentEntityType = "Location",
            StartDate = now.AddDays(8),
            EndDate = now.AddDays(8).AddHours(2),
            CreatedAt = now.AddMinutes(-1),
            PublishedAt = now
        };
        db.Events.AddRange(evt, similarEvent);
        await db.SaveChangesAsync();

        return new WarningFixture(
            manager.Id,
            region.Id,
            destination.Id,
            location.Id,
            place.Id,
            stayPlace.Id,
            evt.Id,
            updateContribution.Id,
            approvedOwnershipContribution.Id);
    }

    private static async Task EnsureRegionalManagerAssignmentAsync(
        AppDbContext db,
        int userId,
        int regionId,
        DateTime now)
    {
        var role = await db.Roles.SingleAsync(role => role.Name == RoleNames.RegionalManager);

        if (!await db.UserRoles.AnyAsync(userRole => userRole.UserId == userId && userRole.RoleId == role.Id))
        {
            db.UserRoles.Add(new UserRole
            {
                UserId = userId,
                RoleId = role.Id
            });
        }

        if (!await db.RegionalManagerRegions.AnyAsync(assignment =>
                assignment.UserId == userId &&
                assignment.RegionId == regionId))
        {
            db.RegionalManagerRegions.Add(new RegionalManagerRegion
            {
                UserId = userId,
                RegionId = regionId,
                IsActive = true,
                CreatedAt = now
            });
        }

        await db.SaveChangesAsync();
    }

    private static async Task<string> GetConnectionStringAsync(GeotrackApiFactory app)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return db.Database.GetConnectionString()
            ?? throw new InvalidOperationException("Test database connection string is missing.");
    }

    private static AppDbContext CreateStrictWarningDbContext(string connectionString)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(connectionString, npgsqlOptions => npgsqlOptions.UseNetTopologySuite())
            .ConfigureWarnings(warnings =>
            {
                warnings.Throw(RelationalEventId.MultipleCollectionIncludeWarning);
                warnings.Throw(CoreEventId.RowLimitingOperationWithoutOrderByWarning);
            })
            .Options;

        return new AppDbContext(options);
    }

    private static DirectoryInfo FindSolutionDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Geotrack.sln")))
        {
            directory = directory.Parent;
        }

        return directory ?? throw new InvalidOperationException("Could not locate Geotrack.sln from test output directory.");
    }

    private sealed record WarningFixture(
        int ManagerUserId,
        int RegionId,
        int DestinationId,
        int LocationId,
        int PlaceId,
        int StayPlaceId,
        int EventId,
        int PendingContributionId,
        int ApprovedOwnershipContributionId);

    private sealed class NoopContributionMediaStorageService : IContributionMediaStorageService
    {
        public Task<StoredContributionMediaFile> StoreAsync(int userId, byte[] fileBytes) =>
            Task.FromResult(new StoredContributionMediaFile { MediaUrl = "/media/uploads/contributions/noop.webp" });

        public Task<StoredContributionMediaFile> StoreOwnershipProofAsync(
            int userId,
            byte[] fileBytes,
            string? originalFileName,
            string? contentType) =>
            Task.FromResult(new StoredContributionMediaFile { MediaUrl = "/media/uploads/contributions/noop-proof.pdf" });

        public Task DeleteAsync(string mediaUrl) => Task.CompletedTask;

        public bool IsManagedContributionMediaUrl(string? mediaUrl) => false;
    }

    private sealed class NoopFavoriteService : IFavoriteService
    {
        public Task<FavoriteToggleResult> ToggleAsync(int userId, FavoriteToggleDto dto) =>
            Task.FromResult(new FavoriteToggleResult
            {
                Status = FavoriteToggleStatus.Success,
                IsSaved = false
            });

        public Task EnsureSavedAsync(int userId, string entityType, int entityId) => Task.CompletedTask;

        public Task RemoveIfUnsavedAsync(int userId, string entityType, int entityId) => Task.CompletedTask;

        public Task<HashSet<int>> GetFavoriteIdsAsync(int userId, string entityType) =>
            Task.FromResult(new HashSet<int>());

        public Task<bool> IsFavoriteAsync(int userId, string entityType, int entityId) =>
            Task.FromResult(false);
    }

    private sealed class NoopReviewContentModerationService : IReviewContentModerationService
    {
        public Task EnsureAllowedReviewTextAsync(string? text) => Task.CompletedTask;
    }

    private sealed class NoopUserNotificationService : IUserNotificationService
    {
        public Task<GetUserNotificationsResponseDto> GetMineAsync(int userId, int skip = 0, int take = 25) =>
            Task.FromResult(new GetUserNotificationsResponseDto());

        public Task<UserNotificationUnreadCountDto> GetUnreadCountAsync(int userId) =>
            Task.FromResult(new UserNotificationUnreadCountDto());

        public Task MarkReadAsync(int userId, int notificationId) => Task.CompletedTask;

        public Task MarkAllReadAsync(int userId) => Task.CompletedTask;

        public Task AddContributionDecisionNotificationAsync(PlaceContribution contribution) => Task.CompletedTask;

        public void AddReviewReportReporterDecisionNotification(ReviewReport report)
        {
        }

        public void AddReportedReviewRemovedNotification(ReviewReport report)
        {
        }

        public void AddReviewRemovedByModeratorNotification(
            int authorUserId,
            string targetType,
            int reviewId,
            DateTime createdAt)
        {
        }
    }

    private sealed class NoopAdminNotificationService : IAdminNotificationService
    {
        public Task<AdminNotificationListDto> GetNotificationsAsync(
            int currentUserId,
            AdminNotificationListRequestDto request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new AdminNotificationListDto());

        public Task<AdminNotificationUnreadCountDto> GetUnreadCountAsync(
            int currentUserId,
            string? audienceRole = null,
            int? regionId = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new AdminNotificationUnreadCountDto());

        public Task<AdminNotificationMutationDto> MarkReadAsync(
            int currentUserId,
            int notificationId,
            string? audienceRole = null,
            int? regionId = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new AdminNotificationMutationDto());

        public Task<AdminNotificationMutationDto> MarkAllReadAsync(
            int currentUserId,
            string? audienceRole = null,
            int? regionId = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new AdminNotificationMutationDto());

        public Task CreateSupportTicketCreatedNotificationAsync(
            SupportRequest ticket,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task CreateReviewReportedNotificationAsync(
            ReviewReport report,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task CreateTerritoryContributionPendingReviewNotificationAsync(
            PlaceContribution contribution,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task CreateRegionalContributionPendingReviewNotificationAsync(
            PlaceContribution contribution,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task CreateContentManagerContributionPendingReviewNotificationAsync(
            PlaceContribution contribution,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task CreateTerritoryRegionCreatedNotificationAsync(
            int territoryId,
            int regionId,
            string regionName,
            DateTime createdAt,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task CreateTerritoryRegionalManagerAssignedNotificationAsync(
            int territoryId,
            int regionId,
            int managerUserId,
            DateTime createdAt,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task CreateTerritoryRegionalManagerRemovedNotificationAsync(
            int territoryId,
            int regionId,
            int managerUserId,
            DateTime createdAt,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class NoopContentHtmlSanitizer : IContentHtmlSanitizer
    {
        public string SanitizeRichText(string? html) => html?.Trim() ?? string.Empty;
    }

    private sealed class NoopModerationAuditService : IModerationAuditService
    {
        public Task RecordAsync(
            int moderatorUserId,
            string targetType,
            int targetId,
            string action,
            string? previousStatus,
            string? newStatus,
            string? reason = null,
            string? internalNote = null,
            string? sourceArea = null,
            string? metadataJson = null,
            string? correlationId = null,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class StubAgentQueryPlanner : IAgentQueryPlanner
    {
        private readonly int _destinationId;

        public StubAgentQueryPlanner(int destinationId)
        {
            _destinationId = destinationId;
        }

        public Task<GeoTrackAgentQueryPlanDto> PlanAsync(
            string message,
            AgentContextOptionsDto? contextOptions,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new GeoTrackAgentQueryPlanDto
            {
                Intent = "recommend",
                OriginalMessage = message,
                NormalizedMessage = message,
                DestinationId = _destinationId,
                LocationUsed = "Warning destination",
                QueryText = "warning",
                SearchCatalog = true,
                EntityTypes = new List<string> { "all" }
            });
    }
}
