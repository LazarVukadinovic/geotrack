using Geotrack.Application.DTOs.Admin;
using Geotrack.Application.Interfaces.Service;
using Geotrack.Application.Security;
using Geotrack.Domain.Entities.Core;
using Geotrack.Domain.Entities.Reviews;
using Microsoft.EntityFrameworkCore;

namespace Geotrack.Infrastructure.Services;

public class RegionalEventService : IRegionalEventService
{
    private const int MaxTake = 50;
    private const string PublishedStatus = "Published";
    private const string ExistingEditRoute = "/events/new";

    private readonly AppDbContext _db;

    public RegionalEventService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<RegionalEventListResponseDto> GetEventsAsync(
        int userId,
        int regionId,
        string? search,
        string? status,
        string? eventType,
        string? sort,
        int skip,
        int take)
    {
        await EnsureAssignedRegionAsync(userId, regionId);

        var normalizedSkip = NormalizeSkip(skip);
        var normalizedTake = NormalizeTake(take);
        var normalizedStatus = NormalizeStatus(status);
        var normalizedSort = NormalizeSort(sort);

        var query = _db.Events
            .AsNoTracking()
            .Where(@event => @event.Location.Destination.RegionId == regionId);

        if (normalizedStatus is "draft" or "archived")
        {
            query = query.Where(_ => false);
        }

        query = ApplySearch(query, search);
        query = ApplyEventTypeFilter(query, eventType);
        query = ApplySort(query, normalizedSort);

        var totalCount = await query.CountAsync();
        var rows = await query
            .Skip(normalizedSkip)
            .Take(normalizedTake)
            .Select(@event => new EventProjection
            {
                Id = @event.Id,
                Name = @event.Name,
                Description = @event.Description,
                EventTypeId = @event.EventTypeId,
                EventTypeName = @event.EventType.Name,
                LocationId = @event.LocationId,
                LocationName = @event.Location.Name,
                DestinationId = @event.Location.DestinationId,
                DestinationName = @event.Location.Destination.Name,
                RegionId = @event.Location.Destination.RegionId,
                RegionName = @event.Location.Destination.Region.Name,
                TerritoryName = @event.Location.Destination.Region.Territory.Name,
                PlaceId = @event.PlaceId,
                PlaceName = @event.Place != null ? @event.Place.Name : null,
                Address = @event.Place != null ? @event.Place.Address : null,
                Latitude = @event.Place != null ? @event.Place.Latitude : @event.Location.Latitude,
                Longitude = @event.Place != null ? @event.Place.Longitude : @event.Location.Longitude,
                StartsAt = @event.StartDate,
                EndsAt = @event.EndDate,
                CreatedAt = @event.CreatedAt,
                UpdatedAt = @event.UpdatedAt,
                TicketPrice = @event.TicketPrice,
                Duration = @event.Duration,
                HostedBy = @event.HostedBy,
                VenueDescription = @event.VenueDescription,
                ArrivalTips = @event.ArrivalTips,
                Accessibility = @event.Accessibility,
                ImageUrl = @event.Images
                    .Where(image => image.TargetType == EntityImageTargetType.Event)
                    .OrderByDescending(image => image.IsMain)
                    .ThenBy(image => image.SortOrder)
                    .ThenBy(image => image.CreatedAt)
                    .ThenBy(image => image.Id)
                    .Select(image => image.Url)
                    .FirstOrDefault()
            })
            .ToListAsync();

        return new RegionalEventListResponseDto
        {
            Items = rows.Select(MapListItem).ToList(),
            TotalCount = totalCount,
            Skip = normalizedSkip,
            Take = normalizedTake
        };
    }

    public async Task<RegionalEventDetailDto> GetEventAsync(int userId, int regionId, int eventId)
    {
        await EnsureAssignedRegionAsync(userId, regionId);

        var row = await _db.Events
            .AsNoTracking()
            .Where(@event => @event.Id == eventId && @event.Location.Destination.RegionId == regionId)
            .Select(@event => new EventProjection
            {
                Id = @event.Id,
                Name = @event.Name,
                Description = @event.Description,
                EventTypeId = @event.EventTypeId,
                EventTypeName = @event.EventType.Name,
                LocationId = @event.LocationId,
                LocationName = @event.Location.Name,
                DestinationId = @event.Location.DestinationId,
                DestinationName = @event.Location.Destination.Name,
                RegionId = @event.Location.Destination.RegionId,
                RegionName = @event.Location.Destination.Region.Name,
                TerritoryName = @event.Location.Destination.Region.Territory.Name,
                PlaceId = @event.PlaceId,
                PlaceName = @event.Place != null ? @event.Place.Name : null,
                Address = @event.Place != null ? @event.Place.Address : null,
                Latitude = @event.Place != null ? @event.Place.Latitude : @event.Location.Latitude,
                Longitude = @event.Place != null ? @event.Place.Longitude : @event.Location.Longitude,
                StartsAt = @event.StartDate,
                EndsAt = @event.EndDate,
                CreatedAt = @event.CreatedAt,
                UpdatedAt = @event.UpdatedAt,
                TicketPrice = @event.TicketPrice,
                Duration = @event.Duration,
                HostedBy = @event.HostedBy,
                VenueDescription = @event.VenueDescription,
                ArrivalTips = @event.ArrivalTips,
                Accessibility = @event.Accessibility,
                ImageUrl = @event.Images
                    .Where(image => image.TargetType == EntityImageTargetType.Event)
                    .OrderByDescending(image => image.IsMain)
                    .ThenBy(image => image.SortOrder)
                    .ThenBy(image => image.CreatedAt)
                    .ThenBy(image => image.Id)
                    .Select(image => image.Url)
                    .FirstOrDefault()
            })
            .SingleOrDefaultAsync()
            ?? throw new KeyNotFoundException("Event not found.");

        return MapDetail(row);
    }

    public async Task<RegionalEventDetailDto> UpdateEventAsync(
        int userId,
        int regionId,
        int eventId,
        RegionalEventUpdateRequestDto request)
    {
        await EnsureAssignedRegionAsync(userId, regionId);

        var @event = await _db.Events
            .Include(item => item.Location)
                .ThenInclude(location => location.Destination)
            .SingleOrDefaultAsync(item => item.Id == eventId && item.Location.Destination.RegionId == regionId)
            ?? throw new KeyNotFoundException("Event not found.");

        if (request.LocationId.HasValue)
        {
            var location = await _db.Locations
                .SingleOrDefaultAsync(item =>
                    item.Id == request.LocationId.Value &&
                    item.Destination.RegionId == regionId)
                ?? throw new InvalidOperationException("Location is invalid for the selected region.");

            @event.LocationId = location.Id;
        }

        if (request.PlaceId.HasValue)
        {
            if (request.PlaceId.Value <= 0)
                throw new InvalidOperationException("Place is invalid.");

            var place = await _db.Places
                .SingleOrDefaultAsync(item =>
                    item.Id == request.PlaceId.Value &&
                    item.Location.Destination.RegionId == regionId)
                ?? throw new InvalidOperationException("Place is invalid for the selected region.");

            @event.PlaceId = place.Id;
        }

        if (request.EventTypeId.HasValue &&
            !await _db.EventTypes.AnyAsync(item => item.Id == request.EventTypeId.Value))
        {
            throw new InvalidOperationException("Event type is invalid.");
        }

        ApplyUpdate(@event, request);
        @event.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();
        return await GetEventAsync(userId, regionId, eventId);
    }

    public async Task DeleteEventAsync(int userId, int regionId, int eventId)
    {
        await EnsureAssignedRegionAsync(userId, regionId);

        var @event = await _db.Events
            .SingleOrDefaultAsync(item => item.Id == eventId && item.Location.Destination.RegionId == regionId)
            ?? throw new KeyNotFoundException("Event not found.");

        if (await HasBlockingDependenciesAsync(eventId))
        {
            throw new InvalidOperationException("This event cannot be deleted while it has related content.");
        }

        _db.Events.Remove(@event);
        await _db.SaveChangesAsync();
    }

    private async Task EnsureAssignedRegionAsync(int userId, int regionId)
    {
        if (regionId <= 0)
            throw new InvalidOperationException("regionId is required.");

        await EnsureRegionalManagerRoleAsync(userId);

        var assigned = await _db.RegionalManagerRegions
            .AsNoTracking()
            .AnyAsync(assignment =>
                assignment.UserId == userId &&
                assignment.RegionId == regionId &&
                assignment.IsActive);

        if (!assigned)
            throw new UnauthorizedAccessException("Region is not assigned to the current regional manager.");
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

    private async Task<bool> HasBlockingDependenciesAsync(int eventId)
    {
        if (await _db.EventReviews.AsNoTracking().AnyAsync(review => review.EventId == eventId))
            return true;

        if (await _db.ReviewReports.AsNoTracking().AnyAsync(report =>
                report.TargetType == ReviewReportTargetType.Event && report.EntityId == eventId ||
                report.EventReview != null && report.EventReview.EventId == eventId))
            return true;

        if (await _db.CollectionItems.AsNoTracking().AnyAsync(item => item.EventId == eventId))
            return true;

        if (await _db.Favorites.AsNoTracking().AnyAsync(item =>
                item.EntityId == eventId &&
                item.EntityType.ToLower() == "event"))
            return true;

        return await _db.AnalyticsEvents.AsNoTracking().AnyAsync(item =>
            item.EntityId == eventId &&
            item.EntityType != null &&
            item.EntityType.ToLower() == "event");
    }

    private static IQueryable<Event> ApplySearch(IQueryable<Event> query, string? search)
    {
        if (string.IsNullOrWhiteSpace(search))
            return query;

        var term = search.Trim().ToLowerInvariant();
        return query.Where(@event =>
            @event.Name.ToLower().Contains(term) ||
            @event.Description.ToLower().Contains(term) ||
            @event.EventType.Name.ToLower().Contains(term) ||
            @event.Location.Name.ToLower().Contains(term) ||
            @event.Location.Destination.Name.ToLower().Contains(term) ||
            @event.Location.Destination.Region.Name.ToLower().Contains(term) ||
            @event.Location.Destination.Region.Territory.Name.ToLower().Contains(term) ||
            @event.Place != null && @event.Place.Name.ToLower().Contains(term) ||
            @event.EventTags.Any(eventTag =>
                eventTag.Tag.Name.ToLower().Contains(term) ||
                eventTag.Tag.Slug.ToLower().Contains(term)));
    }

    private static IQueryable<Event> ApplyEventTypeFilter(IQueryable<Event> query, string? eventType)
    {
        if (string.IsNullOrWhiteSpace(eventType) ||
            string.Equals(eventType.Trim(), "all", StringComparison.OrdinalIgnoreCase))
        {
            return query;
        }

        var term = eventType.Trim().ToLowerInvariant();
        return query.Where(@event => @event.EventType.Name.ToLower() == term);
    }

    private static IQueryable<Event> ApplySort(IQueryable<Event> query, string sort)
    {
        return sort switch
        {
            "oldest" => query
                .OrderBy(@event => @event.UpdatedAt ?? @event.CreatedAt)
                .ThenBy(@event => @event.Id),
            "nameasc" => query
                .OrderBy(@event => @event.Name)
                .ThenBy(@event => @event.Id),
            "namedesc" => query
                .OrderByDescending(@event => @event.Name)
                .ThenByDescending(@event => @event.Id),
            "eventdateasc" => query
                .OrderBy(@event => @event.StartDate)
                .ThenBy(@event => @event.Id),
            "eventdatedesc" => query
                .OrderByDescending(@event => @event.StartDate)
                .ThenByDescending(@event => @event.Id),
            _ => query
                .OrderByDescending(@event => @event.UpdatedAt ?? @event.CreatedAt)
                .ThenByDescending(@event => @event.Id),
        };
    }

    private static void ApplyUpdate(Event @event, RegionalEventUpdateRequestDto request)
    {
        if (request.Name is not null)
        {
            var name = request.Name.Trim();
            if (name.Length == 0)
                throw new InvalidOperationException("Event name is required.");
            if (name.Length > 200)
                throw new InvalidOperationException("Event name is too long.");

            @event.Name = name;
        }

        if (request.Description is not null)
        {
            var description = request.Description.Trim();
            if (description.Length > 2000)
                throw new InvalidOperationException("Description is too long.");

            @event.Description = description;
        }

        if (request.EventTypeId.HasValue)
        {
            if (request.EventTypeId.Value <= 0)
                throw new InvalidOperationException("Event type is invalid.");

            @event.EventTypeId = request.EventTypeId.Value;
        }

        var startsAt = request.StartsAt ?? @event.StartDate;
        var endsAt = request.EndsAt ?? @event.EndDate;
        if (endsAt < startsAt)
            throw new InvalidOperationException("Event end date must be after the start date.");

        if (request.StartsAt.HasValue)
            @event.StartDate = request.StartsAt.Value;

        if (request.EndsAt.HasValue)
            @event.EndDate = request.EndsAt.Value;

        @event.TicketPrice = NormalizeOptionalText(request.TicketPrice, 100, "Ticket price is too long.", @event.TicketPrice);
        @event.Duration = NormalizeOptionalText(request.Duration, 100, "Duration is too long.", @event.Duration);
        @event.HostedBy = NormalizeOptionalText(request.HostedBy, 200, "Host name is too long.", @event.HostedBy);
        @event.VenueDescription = NormalizeOptionalText(request.VenueDescription, 2000, "Venue description is too long.", @event.VenueDescription);
        @event.ArrivalTips = NormalizeOptionalText(request.ArrivalTips, 2000, "Arrival tips are too long.", @event.ArrivalTips);
        @event.Accessibility = NormalizeOptionalText(request.Accessibility, 1000, "Accessibility notes are too long.", @event.Accessibility);
    }

    private static string? NormalizeOptionalText(string? value, int maxLength, string errorMessage, string? currentValue)
    {
        if (value is null)
            return currentValue;

        var normalized = value.Trim();
        if (normalized.Length > maxLength)
            throw new InvalidOperationException(errorMessage);

        return normalized.Length == 0 ? null : normalized;
    }

    private static RegionalEventListItemDto MapListItem(EventProjection row)
    {
        return new RegionalEventListItemDto
        {
            Id = row.Id,
            Name = row.Name,
            Summary = BuildSummary(row.Description),
            Description = row.Description,
            EventType = row.EventTypeName,
            EventTypeId = row.EventTypeId,
            Category = row.EventTypeName,
            Status = PublishedStatus,
            RegionId = row.RegionId,
            RegionDisplayName = BuildRegionDisplayName(row.RegionName, row.TerritoryName),
            DestinationId = row.DestinationId,
            DestinationName = row.DestinationName,
            LocationId = row.LocationId,
            LocationName = row.LocationName,
            PlaceId = row.PlaceId,
            PlaceName = row.PlaceName,
            Address = row.Address,
            StartsAt = row.StartsAt,
            EndsAt = row.EndsAt,
            CreatedAt = row.CreatedAt,
            LastUpdatedAt = row.UpdatedAt ?? row.CreatedAt,
            TicketPrice = row.TicketPrice,
            Duration = row.Duration,
            HostedBy = row.HostedBy,
            VenueDescription = row.VenueDescription,
            ArrivalTips = row.ArrivalTips,
            Accessibility = row.Accessibility,
            ImageUrl = row.ImageUrl,
            DetailRoute = "/events/detail/" + row.Id,
            EditRoute = ExistingEditRoute,
            Latitude = row.Latitude,
            Longitude = row.Longitude
        };
    }

    private static RegionalEventDetailDto MapDetail(EventProjection row)
    {
        var item = MapListItem(row);
        return new RegionalEventDetailDto
        {
            Id = item.Id,
            Name = item.Name,
            Summary = item.Summary,
            Description = item.Description,
            EventType = item.EventType,
            EventTypeId = item.EventTypeId,
            Category = item.Category,
            Status = item.Status,
            RegionId = item.RegionId,
            RegionDisplayName = item.RegionDisplayName,
            DestinationId = item.DestinationId,
            DestinationName = item.DestinationName,
            LocationId = item.LocationId,
            LocationName = item.LocationName,
            PlaceId = item.PlaceId,
            PlaceName = item.PlaceName,
            Address = item.Address,
            StartsAt = item.StartsAt,
            EndsAt = item.EndsAt,
            CreatedAt = item.CreatedAt,
            LastUpdatedAt = item.LastUpdatedAt,
            TicketPrice = item.TicketPrice,
            Duration = item.Duration,
            HostedBy = item.HostedBy,
            VenueDescription = item.VenueDescription,
            ArrivalTips = item.ArrivalTips,
            Accessibility = item.Accessibility,
            ImageUrl = item.ImageUrl,
            DetailRoute = item.DetailRoute,
            EditRoute = item.EditRoute,
            Latitude = item.Latitude,
            Longitude = item.Longitude
        };
    }

    private static string BuildRegionDisplayName(string regionName, string? territoryName)
    {
        return string.IsNullOrWhiteSpace(territoryName)
            ? regionName
            : regionName + ", " + territoryName;
    }

    private static string? BuildSummary(string? description)
    {
        if (string.IsNullOrWhiteSpace(description))
            return null;

        var normalized = string.Join(" ", description.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return normalized.Length <= 160 ? normalized : normalized[..157] + "...";
    }

    private static int NormalizeSkip(int skip)
    {
        if (skip < 0)
            throw new InvalidOperationException("skip must be zero or greater.");

        return skip;
    }

    private static int NormalizeTake(int take)
    {
        if (take <= 0)
            throw new InvalidOperationException("take must be greater than zero.");

        return Math.Clamp(take, 1, MaxTake);
    }

    private static string NormalizeStatus(string? status)
    {
        var normalized = NormalizeToken(status ?? "all");
        return normalized switch
        {
            "all" or "published" or "draft" or "archived" => normalized,
            _ => throw new InvalidOperationException("Invalid status filter.")
        };
    }

    private static string NormalizeSort(string? sort)
    {
        var normalized = NormalizeToken(sort ?? "newest");
        return normalized switch
        {
            "oldest" => "oldest",
            "nameaz" or "nameasc" => "nameasc",
            "nameza" or "namedesc" => "namedesc",
            "eventdateasc" or "datesoonest" or "soonest" => "eventdateasc",
            "eventdatedesc" or "datelatest" or "latest" => "eventdatedesc",
            _ => "newest"
        };
    }

    private static string NormalizeToken(string value) =>
        value.Replace(" ", string.Empty, StringComparison.Ordinal)
            .Replace("-", string.Empty, StringComparison.Ordinal)
            .Replace("_", string.Empty, StringComparison.Ordinal)
            .ToLowerInvariant();

    private sealed class EventProjection
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public int EventTypeId { get; set; }
        public string? EventTypeName { get; set; }
        public int DestinationId { get; set; }
        public string DestinationName { get; set; } = string.Empty;
        public int LocationId { get; set; }
        public string LocationName { get; set; } = string.Empty;
        public int RegionId { get; set; }
        public string RegionName { get; set; } = string.Empty;
        public string? TerritoryName { get; set; }
        public int? PlaceId { get; set; }
        public string? PlaceName { get; set; }
        public string? Address { get; set; }
        public DateTime StartsAt { get; set; }
        public DateTime EndsAt { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
        public string? TicketPrice { get; set; }
        public string? Duration { get; set; }
        public string? HostedBy { get; set; }
        public string? VenueDescription { get; set; }
        public string? ArrivalTips { get; set; }
        public string? Accessibility { get; set; }
        public string? ImageUrl { get; set; }
        public double? Latitude { get; set; }
        public double? Longitude { get; set; }
    }
}
