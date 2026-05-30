using Geotrack.Application.DTOs.Favorites;
using Geotrack.Application.Interfaces.Service;
using Geotrack.Application.Options;
using Geotrack.Application.Security;
using Geotrack.Application.Services;
using Geotrack.Domain.Entities.Core;
using Geotrack.Domain.Entities.Users;
using Geotrack.Infrastructure;
using Geotrack.Infrastructure.Repositories;
using Geotrack.Infrastructure.Services;
using Geotrack.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
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

        var destinations = new RegionalDestinationService(strictDb, scopeValidation);
        var destinationList = await destinations.GetDestinationsAsync(
            fixture.ManagerUserId,
            fixture.RegionId,
            search: null,
            status: "all",
            sort: "newest",
            skip: 0,
            take: 5);
        var destinationDetail = await destinations.GetDestinationAsync(
            fixture.ManagerUserId,
            fixture.RegionId,
            fixture.DestinationId);

        var locations = new RegionalLocationService(strictDb, scopeValidation);
        var locationList = await locations.GetLocationsAsync(
            fixture.ManagerUserId,
            fixture.RegionId,
            search: null,
            status: "all",
            sort: "newest",
            skip: 0,
            take: 5);
        var locationDetail = await locations.GetLocationAsync(
            fixture.ManagerUserId,
            fixture.RegionId,
            fixture.LocationId);

        var places = new RegionalPlaceService(strictDb, scopeValidation);
        var placeList = await places.GetPlacesAsync(
            fixture.ManagerUserId,
            fixture.RegionId,
            search: null,
            status: "all",
            placeType: null,
            sort: "newest",
            skip: 0,
            take: 5);
        var placeDetail = await places.GetPlaceAsync(
            fixture.ManagerUserId,
            fixture.RegionId,
            fixture.PlaceId);

        var events = new RegionalEventService(strictDb, scopeValidation);
        var eventList = await events.GetEventsAsync(
            fixture.ManagerUserId,
            fixture.RegionId,
            search: null,
            status: "all",
            eventType: null,
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

        var placeService = new PlaceService(
            strictDb,
            new NoopFavoriteService(),
            new NoopReviewContentModerationService());
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
            evt.Id);
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

    private sealed record WarningFixture(
        int ManagerUserId,
        int RegionId,
        int DestinationId,
        int LocationId,
        int PlaceId,
        int StayPlaceId,
        int EventId);

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
}
