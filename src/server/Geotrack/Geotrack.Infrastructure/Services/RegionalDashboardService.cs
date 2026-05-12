using Geotrack.Application.DTOs.Admin;
using Geotrack.Application.Interfaces.Service;
using Geotrack.Domain.Entities.Users;
using Microsoft.EntityFrameworkCore;

namespace Geotrack.Infrastructure.Services;

public class RegionalDashboardService : IRegionalDashboardService
{
    private const int DashboardQueuePreviewTake = 4;
    private const int DashboardRecentActivityTake = 5;
    private const int ActivityMaxTake = 50;

    private readonly AppDbContext _db;

    public RegionalDashboardService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<List<RegionalAssignedRegionDto>> GetAssignedRegionsAsync(int userId)
    {
        return await _db.RegionalManagerRegions
            .AsNoTracking()
            .Where(assignment => assignment.UserId == userId)
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

        var pendingQuery = PendingContributionsForRegion(regionId);

        var stats = new RegionalDashboardStatsDto
        {
            PendingRequests = await pendingQuery.CountAsync(),
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
            RequestQueuePreview = await GetRequestQueuePreviewAsync(regionId, DashboardQueuePreviewTake),
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
        var region = await _db.RegionalManagerRegions
            .AsNoTracking()
            .Where(assignment => assignment.UserId == userId && assignment.RegionId == regionId)
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

    private IQueryable<PlaceContribution> PendingContributionsForRegion(int regionId)
    {
        return _db.PlaceContributions
            .AsNoTracking()
            .Where(contribution =>
                contribution.Status == PlaceContributionStatus.PendingReview &&
                contribution.LocationId.HasValue &&
                contribution.Location != null &&
                contribution.Location.Destination.RegionId == regionId &&
                (contribution.Type != PlaceContributionType.UpdatePlace ||
                 (contribution.PlaceId.HasValue &&
                  contribution.Place != null &&
                  contribution.Place.Location.Destination.RegionId == regionId &&
                  contribution.Place.Location.Destination.RegionId == contribution.Location.Destination.RegionId)));
    }

    private async Task<List<RegionalRequestQueuePreviewItemDto>> GetRequestQueuePreviewAsync(int regionId, int take)
    {
        var rows = await PendingContributionsForRegion(regionId)
            .OrderByDescending(contribution => contribution.SubmittedAt)
            .ThenByDescending(contribution => contribution.Id)
            .Take(take)
            .Select(contribution => new
            {
                contribution.Id,
                contribution.Name,
                contribution.Type,
                contribution.Status,
                contribution.SubmittedAt,
                UserDisplayName = contribution.User.DisplayName,
                contribution.User.FirstName,
                contribution.User.LastName,
                contribution.User.Username,
                ThumbnailImageUrl = contribution.Images
                    .OrderByDescending(image => image.IsMain)
                    .ThenBy(image => image.SortOrder)
                    .ThenBy(image => image.Id)
                    .Select(image => image.Url)
                    .FirstOrDefault()
            })
            .ToListAsync();

        return rows.Select(row => new RegionalRequestQueuePreviewItemDto
            {
                RequestId = row.Id,
                Title = row.Name,
                SubmittedByDisplayName = BuildDisplayName(row.UserDisplayName, row.FirstName, row.LastName, row.Username),
                EntityType = "Place",
                ContributionType = row.Type.ToString(),
                SubmittedAt = row.SubmittedAt,
                Status = row.Status.ToString(),
                ThumbnailImageUrl = row.ThumbnailImageUrl,
                DetailRoute = $"/request/detail/{row.Id}"
            })
            .ToList();
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
