using Geotrack.Application.DTOs.Admin;
using Geotrack.Application.Interfaces.Service;
using Geotrack.Application.Security;
using Geotrack.Domain.Entities.Users;
using Microsoft.EntityFrameworkCore;

namespace Geotrack.Infrastructure.Services;

public class RegionalDashboardService : IRegionalDashboardService
{
    private const int DashboardQueuePreviewTake = 4;
    private const int DashboardRecentActivityTake = 5;
    private const int ActivityMaxTake = 50;

    private readonly AppDbContext _db;
    private readonly IRegionalRequestQueueService _requestQueue;

    public RegionalDashboardService(AppDbContext db, IRegionalRequestQueueService requestQueue)
    {
        _db = db;
        _requestQueue = requestQueue;
    }

    public async Task<List<RegionalAssignedRegionDto>> GetAssignedRegionsAsync(int userId)
    {
        await EnsureRegionalManagerRoleAsync(userId);

        return await _db.RegionalManagerRegions
            .AsNoTracking()
            .Where(assignment => assignment.UserId == userId && assignment.IsActive)
            .OrderBy(assignment => assignment.Region.Name)
            .ThenBy(assignment => assignment.RegionId)
            .Select(assignment => new RegionalAssignedRegionDto
            {
                Id = assignment.RegionId,
                Name = assignment.Region.Name,
                TerritoryName = assignment.Region.Territory.Name,
                DisplayName = assignment.Region.Territory.Name == string.Empty
                    ? assignment.Region.Name
                    : assignment.Region.Name + ", " + assignment.Region.Territory.Name
            })
            .ToListAsync();
    }

    public async Task<RegionalDashboardDto> GetDashboardAsync(int userId, int regionId)
    {
        var activeRegion = await GetAssignedRegionAsync(userId, regionId);

        // Dashboard monthly deltas use a rolling 30-day UTC window.
        var lastMonthCutoff = DateTime.UtcNow.AddDays(-30);

        var queuePreview = await _requestQueue.GetQueueAsync(
            userId,
            regionId,
            type: "all",
            status: "pending",
            search: null,
            sort: "newest",
            skip: 0,
            take: DashboardQueuePreviewTake);

        var stats = new RegionalDashboardStatsDto
        {
            PendingRequests = queuePreview.PendingCount,
            TotalDestinations = await _db.Destinations
                .AsNoTracking()
                .CountAsync(destination => destination.RegionId == regionId),
            DestinationsAddedLastMonth = await _db.Destinations
                .AsNoTracking()
                .CountAsync(destination => destination.RegionId == regionId && destination.CreatedAt >= lastMonthCutoff),
            TotalPlaces = await _db.Places
                .AsNoTracking()
                .CountAsync(place => place.Location.Destination.RegionId == regionId),
            PlacesAddedLastMonth = await _db.Places
                .AsNoTracking()
                .CountAsync(place => place.Location.Destination.RegionId == regionId && place.CreatedAt >= lastMonthCutoff),
            TotalEvents = await _db.Events
                .AsNoTracking()
                .CountAsync(evt => evt.Location.Destination.RegionId == regionId),
            EventsAddedLastMonth = await _db.Events
                .AsNoTracking()
                .CountAsync(evt => evt.Location.Destination.RegionId == regionId && evt.CreatedAt >= lastMonthCutoff)
        };

        return new RegionalDashboardDto
        {
            ActiveRegion = activeRegion,
            Stats = stats,
            RequestQueuePreview = queuePreview.Items.Select(item => new RegionalRequestQueuePreviewItemDto
            {
                RequestKey = item.RequestKey,
                RequestId = item.RequestId,
                Source = item.Source,
                Title = item.Title,
                SubmittedByDisplayName = item.SubmittedByDisplayName,
                EntityType = item.EntityType,
                ContributionType = item.ContributionType ?? string.Empty,
                SubmittedAt = item.SubmittedAt,
                Status = item.Status,
                ThumbnailImageUrl = item.ThumbnailImageUrl,
                DetailRoute = item.DetailRoute ?? string.Empty
            }).ToList(),
            RecentActivity = await BuildActivityAsync(regionId, 0, DashboardRecentActivityTake)
        };
    }

    public async Task<RegionalDashboardActivityResponseDto> GetActivityAsync(int userId, int regionId, int skip, int take)
    {
        _ = await GetAssignedRegionAsync(userId, regionId);

        skip = Math.Max(0, skip);
        take = Math.Clamp(take, 1, ActivityMaxTake);

        var items = await BuildActivityAsync(regionId, skip, take + 1);

        return new RegionalDashboardActivityResponseDto
        {
            Items = items.Take(take).ToList(),
            Skip = skip,
            Take = take,
            HasMore = items.Count > take
        };
    }

    private async Task<RegionalDashboardRegionDto> GetAssignedRegionAsync(int userId, int regionId)
    {
        await EnsureRegionalManagerRoleAsync(userId);

        var region = await _db.RegionalManagerRegions
            .AsNoTracking()
            .Where(assignment => assignment.UserId == userId && assignment.RegionId == regionId && assignment.IsActive)
            .Select(assignment => new RegionalDashboardRegionDto
            {
                Id = assignment.RegionId,
                Name = assignment.Region.Name,
                TerritoryName = assignment.Region.Territory.Name,
                DisplayName = assignment.Region.Territory.Name == string.Empty
                    ? assignment.Region.Name
                    : assignment.Region.Name + ", " + assignment.Region.Territory.Name
            })
            .SingleOrDefaultAsync();

        return region ?? throw new UnauthorizedAccessException("Region is not assigned to the current regional manager.");
    }

    private async Task EnsureRegionalManagerRoleAsync(int userId)
    {
        var roles = await _db.UserRoles
            .AsNoTracking()
            .Where(userRole => userRole.UserId == userId)
            .Select(userRole => userRole.Role.Name)
            .ToListAsync();

        if (!roles.Any(role => RoleNames.Normalize(role) == RoleNames.RegionalManager))
            throw new UnauthorizedAccessException("Regional manager role is required.");
    }

    private async Task<List<RegionalDashboardActivityItemDto>> BuildActivityAsync(int regionId, int skip, int take)
    {
        var reviewedContributions = await _db.PlaceContributions
            .AsNoTracking()
            .Where(contribution =>
                contribution.ReviewedAt.HasValue &&
                (contribution.Status == PlaceContributionStatus.Approved ||
                 contribution.Status == PlaceContributionStatus.Rejected) &&
                contribution.LocationId.HasValue &&
                contribution.Location != null &&
                contribution.Location.Destination.RegionId == regionId &&
                (contribution.Type != PlaceContributionType.UpdatePlace ||
                 (contribution.PlaceId.HasValue &&
                  contribution.Place != null &&
                  contribution.Place.Location.Destination.RegionId == regionId &&
                  contribution.Place.Location.Destination.RegionId == contribution.Location.Destination.RegionId)))
            .OrderByDescending(contribution => contribution.ReviewedAt)
            .ThenByDescending(contribution => contribution.Id)
            .Take(skip + take)
            .Select(contribution => new
            {
                contribution.Id,
                contribution.Name,
                contribution.Status,
                contribution.ReviewedAt,
                ReviewerDisplayName = contribution.ReviewedByUser == null ? null : contribution.ReviewedByUser.DisplayName,
                ReviewerFirstName = contribution.ReviewedByUser == null ? null : contribution.ReviewedByUser.FirstName,
                ReviewerLastName = contribution.ReviewedByUser == null ? null : contribution.ReviewedByUser.LastName,
                ReviewerUsername = contribution.ReviewedByUser == null ? null : contribution.ReviewedByUser.Username
            })
            .ToListAsync();

        var activities = reviewedContributions.Select(row =>
        {
            var actor = BuildDisplayName(row.ReviewerDisplayName, row.ReviewerFirstName, row.ReviewerLastName, row.ReviewerUsername);
            var action = row.Status == PlaceContributionStatus.Approved ? "Approved contribution" : "Rejected contribution";

            return new RegionalDashboardActivityItemDto
            {
                Id = $"contribution-{row.Id}-{row.Status.ToString().ToLowerInvariant()}",
                EntityType = "request",
                EntityId = row.Id,
                Action = action,
                Description = string.IsNullOrWhiteSpace(actor)
                    ? $"{action}: {row.Name}"
                    : $"{actor} {action.ToLowerInvariant()}: {row.Name}",
                ActorDisplayName = string.IsNullOrWhiteSpace(actor) ? null : actor,
                ActorInitials = BuildInitials(actor),
                OccurredAt = row.ReviewedAt!.Value,
                Status = row.Status.ToString(),
                DetailRoute = $"/request/detail/{row.Id}"
            };
        }).ToList();

        activities.AddRange(await GetCreatedDestinationActivitiesAsync(regionId, skip + take));
        activities.AddRange(await GetCreatedLocationActivitiesAsync(regionId, skip + take));
        activities.AddRange(await GetCreatedPlaceActivitiesAsync(regionId, skip + take));
        activities.AddRange(await GetCreatedEventActivitiesAsync(regionId, skip + take));

        return activities
            .OrderByDescending(activity => activity.OccurredAt)
            .ThenByDescending(activity => activity.Id, StringComparer.Ordinal)
            .Skip(skip)
            .Take(take)
            .ToList();
    }

    private async Task<List<RegionalDashboardActivityItemDto>> GetCreatedDestinationActivitiesAsync(int regionId, int take)
    {
        return await _db.Destinations
            .AsNoTracking()
            .Where(destination => destination.RegionId == regionId)
            .OrderByDescending(destination => destination.CreatedAt)
            .ThenByDescending(destination => destination.Id)
            .Take(take)
            .Select(destination => new RegionalDashboardActivityItemDto
            {
                Id = "destination-" + destination.Id,
                EntityType = "destination",
                EntityId = destination.Id,
                Action = "Created destination",
                Description = "Destination added: " + destination.Name,
                OccurredAt = destination.CreatedAt,
                Status = "Created",
                DetailRoute = "/destinations/detail/" + destination.Id
            })
            .ToListAsync();
    }

    private async Task<List<RegionalDashboardActivityItemDto>> GetCreatedLocationActivitiesAsync(int regionId, int take)
    {
        return await _db.Locations
            .AsNoTracking()
            .Where(location => location.Destination.RegionId == regionId)
            .OrderByDescending(location => location.CreatedAt)
            .ThenByDescending(location => location.Id)
            .Take(take)
            .Select(location => new RegionalDashboardActivityItemDto
            {
                Id = "location-" + location.Id,
                EntityType = "location",
                EntityId = location.Id,
                Action = "Created location",
                Description = "Location added: " + location.Name,
                OccurredAt = location.CreatedAt,
                Status = "Created",
                DetailRoute = "/locations/detail/" + location.Id
            })
            .ToListAsync();
    }

    private async Task<List<RegionalDashboardActivityItemDto>> GetCreatedPlaceActivitiesAsync(int regionId, int take)
    {
        return await _db.Places
            .AsNoTracking()
            .Where(place => place.Location.Destination.RegionId == regionId)
            .OrderByDescending(place => place.CreatedAt)
            .ThenByDescending(place => place.Id)
            .Take(take)
            .Select(place => new RegionalDashboardActivityItemDto
            {
                Id = "place-" + place.Id,
                EntityType = "place",
                EntityId = place.Id,
                Action = "Created place",
                Description = "Place added: " + place.Name,
                OccurredAt = place.CreatedAt,
                Status = "Created",
                DetailRoute = "/places/detail/" + place.Id
            })
            .ToListAsync();
    }

    private async Task<List<RegionalDashboardActivityItemDto>> GetCreatedEventActivitiesAsync(int regionId, int take)
    {
        return await _db.Events
            .AsNoTracking()
            .Where(evt => evt.Location.Destination.RegionId == regionId)
            .OrderByDescending(evt => evt.CreatedAt)
            .ThenByDescending(evt => evt.Id)
            .Take(take)
            .Select(evt => new RegionalDashboardActivityItemDto
            {
                Id = "event-" + evt.Id,
                EntityType = "event",
                EntityId = evt.Id,
                Action = "Created event",
                Description = "Event scheduled: " + evt.Name,
                OccurredAt = evt.CreatedAt,
                Status = "Created",
                DetailRoute = "/events/detail/" + evt.Id
            })
            .ToListAsync();
    }

    private static string BuildDisplayName(string? displayName, string? firstName, string? lastName, string? username)
    {
        if (!string.IsNullOrWhiteSpace(displayName))
            return displayName.Trim();

        var fullName = string.Join(" ", new[] { firstName, lastName }
            .Where(part => !string.IsNullOrWhiteSpace(part))
            .Select(part => part!.Trim()));

        if (!string.IsNullOrWhiteSpace(fullName))
            return fullName;

        return username?.Trim() ?? string.Empty;
    }

    private static string? BuildInitials(string displayName)
    {
        var parts = displayName.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
            return null;

        return string.Concat(parts.Take(2).Select(part => char.ToUpperInvariant(part[0])));
    }
}
