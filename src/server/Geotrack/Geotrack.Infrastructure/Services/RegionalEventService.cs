using Geotrack.Application.DTOs.Admin;
using Geotrack.Application.Interfaces.Service;
using Geotrack.Application.Security;
using Geotrack.Domain.Entities.Core;
using Geotrack.Domain.Entities.Reviews;
using Geotrack.Domain.Entities.Tags;
using Microsoft.EntityFrameworkCore;
using System.Globalization;
using System.Net.Mail;

namespace Geotrack.Infrastructure.Services;

public class RegionalEventService : IRegionalEventService
{
    private const int MaxTake = 50;
    private const string PublishedStatus = "Published";
    private const string DraftStatus = "Draft";
    private const string ArchivedStatus = "Archived";

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
            .Where(@event => @event.Destination.RegionId == regionId);

        if (normalizedStatus != "all")
        {
            query = query.Where(@event => @event.Status.ToLower() == normalizedStatus);
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
                Summary = @event.Summary,
                InternalReferenceId = @event.InternalReferenceId,
                Description = @event.Description,
                EventTypeId = @event.EventTypeId,
                EventTypeName = @event.EventType.Name,
                LocationId = @event.LocationId,
                LocationName = @event.Location != null ? @event.Location.Name : null,
                DestinationId = @event.DestinationId,
                DestinationName = @event.Destination.Name,
                RegionId = @event.Destination.RegionId,
                RegionName = @event.Destination.Region.Name,
                TerritoryName = @event.Destination.Region.Territory.Name,
                PlaceId = @event.PlaceId,
                PlaceName = @event.Place != null ? @event.Place.Name : null,
                Address = @event.Place != null ? @event.Place.Address : null,
                Latitude = @event.Place != null ? @event.Place.Latitude : @event.Location != null ? @event.Location.Latitude : @event.Destination.Latitude,
                Longitude = @event.Place != null ? @event.Place.Longitude : @event.Location != null ? @event.Location.Longitude : @event.Destination.Longitude,
                Status = @event.Status,
                ParentEntityType = @event.ParentEntityType,
                StartsAt = @event.StartDate,
                EndsAt = @event.EndDate,
                IsAllDay = @event.IsAllDay,
                IsRecurring = @event.IsRecurring,
                CreatedAt = @event.CreatedAt,
                UpdatedAt = @event.UpdatedAt,
                CreatedByUserId = @event.CreatedByUserId,
                PublishedAt = @event.PublishedAt,
                VenueName = @event.VenueName,
                TicketPrice = @event.TicketPrice,
                IsFree = @event.IsFree,
                ExternalTicketLink = @event.ExternalTicketLink,
                Duration = @event.Duration,
                HostedBy = @event.HostedBy,
                ContactEmail = @event.ContactEmail,
                AttendanceLimit = @event.AttendanceLimit,
                AgeRestriction = @event.AgeRestriction,
                VenueDescription = @event.VenueDescription,
                ArrivalTips = @event.ArrivalTips,
                Accessibility = @event.Accessibility,
                Tags = @event.EventTags
                    .OrderBy(eventTag => eventTag.Tag.Name)
                    .Select(eventTag => new RegionalEventTagDto
                    {
                        Id = eventTag.TagId,
                        Name = eventTag.Tag.Name,
                        Slug = eventTag.Tag.Slug
                    })
                    .ToList(),
                ImageUrl = @event.Images
                    .Where(image => image.TargetType == EntityImageTargetType.Event)
                    .OrderByDescending(image => image.IsMain)
                    .ThenBy(image => image.SortOrder)
                    .ThenBy(image => image.CreatedAt)
                    .ThenBy(image => image.Id)
                    .Select(image => image.Url)
                    .FirstOrDefault(),
                Images = @event.Images
                    .Where(image => image.TargetType == EntityImageTargetType.Event)
                    .OrderByDescending(image => image.IsMain)
                    .ThenBy(image => image.SortOrder)
                    .ThenBy(image => image.CreatedAt)
                    .ThenBy(image => image.Id)
                    .Select(image => new RegionalEntityImageDto
                    {
                        Id = image.Id,
                        Url = image.Url,
                        AltText = image.AltText,
                        Caption = image.Caption,
                        IsMain = image.IsMain,
                        SortOrder = image.SortOrder
                    })
                    .ToList()
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
            .Where(@event => @event.Id == eventId && @event.Destination.RegionId == regionId)
            .Select(@event => new EventProjection
            {
                Id = @event.Id,
                Name = @event.Name,
                Summary = @event.Summary,
                InternalReferenceId = @event.InternalReferenceId,
                Description = @event.Description,
                EventTypeId = @event.EventTypeId,
                EventTypeName = @event.EventType.Name,
                LocationId = @event.LocationId,
                LocationName = @event.Location != null ? @event.Location.Name : null,
                DestinationId = @event.DestinationId,
                DestinationName = @event.Destination.Name,
                RegionId = @event.Destination.RegionId,
                RegionName = @event.Destination.Region.Name,
                TerritoryName = @event.Destination.Region.Territory.Name,
                PlaceId = @event.PlaceId,
                PlaceName = @event.Place != null ? @event.Place.Name : null,
                Address = @event.Place != null ? @event.Place.Address : null,
                Latitude = @event.Place != null ? @event.Place.Latitude : @event.Location != null ? @event.Location.Latitude : @event.Destination.Latitude,
                Longitude = @event.Place != null ? @event.Place.Longitude : @event.Location != null ? @event.Location.Longitude : @event.Destination.Longitude,
                Status = @event.Status,
                ParentEntityType = @event.ParentEntityType,
                StartsAt = @event.StartDate,
                EndsAt = @event.EndDate,
                IsAllDay = @event.IsAllDay,
                IsRecurring = @event.IsRecurring,
                CreatedAt = @event.CreatedAt,
                UpdatedAt = @event.UpdatedAt,
                CreatedByUserId = @event.CreatedByUserId,
                PublishedAt = @event.PublishedAt,
                VenueName = @event.VenueName,
                TicketPrice = @event.TicketPrice,
                IsFree = @event.IsFree,
                ExternalTicketLink = @event.ExternalTicketLink,
                Duration = @event.Duration,
                HostedBy = @event.HostedBy,
                ContactEmail = @event.ContactEmail,
                AttendanceLimit = @event.AttendanceLimit,
                AgeRestriction = @event.AgeRestriction,
                VenueDescription = @event.VenueDescription,
                ArrivalTips = @event.ArrivalTips,
                Accessibility = @event.Accessibility,
                Tags = @event.EventTags
                    .OrderBy(eventTag => eventTag.Tag.Name)
                    .Select(eventTag => new RegionalEventTagDto
                    {
                        Id = eventTag.TagId,
                        Name = eventTag.Tag.Name,
                        Slug = eventTag.Tag.Slug
                    })
                    .ToList(),
                ImageUrl = @event.Images
                    .Where(image => image.TargetType == EntityImageTargetType.Event)
                    .OrderByDescending(image => image.IsMain)
                    .ThenBy(image => image.SortOrder)
                    .ThenBy(image => image.CreatedAt)
                    .ThenBy(image => image.Id)
                    .Select(image => image.Url)
                    .FirstOrDefault(),
                Images = @event.Images
                    .Where(image => image.TargetType == EntityImageTargetType.Event)
                    .OrderByDescending(image => image.IsMain)
                    .ThenBy(image => image.SortOrder)
                    .ThenBy(image => image.CreatedAt)
                    .ThenBy(image => image.Id)
                    .Select(image => new RegionalEntityImageDto
                    {
                        Id = image.Id,
                        Url = image.Url,
                        AltText = image.AltText,
                        Caption = image.Caption,
                        IsMain = image.IsMain,
                        SortOrder = image.SortOrder
                    })
                    .ToList()
            })
            .SingleOrDefaultAsync()
            ?? throw new KeyNotFoundException("Event not found.");

        return MapDetail(row);
    }

    public async Task<RegionalEventDetailDto> CreateEventAsync(
        int userId,
        RegionalEventCreateRequestDto request)
    {
        if (!string.IsNullOrWhiteSpace(request.InternalReferenceId))
            throw new InvalidOperationException("Internal reference id is generated by the server.");

        await EnsureAssignedRegionAsync(userId, request.RegionId);

        var name = NormalizeRequiredText(request.Name, "Event title is required.", 200, "Event title is too long.");
        var summary = NormalizeRequiredText(request.Summary, "Summary is required.", 160, "Summary is too long.");
        var description = NormalizeOptionalText(request.Description, 8000, "Description is too long.") ?? string.Empty;
        var status = NormalizeCreateStatus(request.Status);
        var eventTypeId = await ResolveEventTypeIdAsync(request.EventTypeId);
        var parent = await ResolveParentAsync(request.RegionId, request);
        var schedule = NormalizeSchedule(request.StartsAt, request.EndsAt, request.IsAllDay);
        var tagIds = await ValidateEventTagIdsAsync(request.TagIds);
        var ticketPrice = request.IsFree
            ? null
            : NormalizeTicketPrice(request.TicketPrice);
        var images = RegionalEntityImageRequestNormalizer.Normalize(request.Images, name);
        var now = DateTime.UtcNow;

        await using var transaction = await _db.Database.BeginTransactionAsync();

        var @event = new Event
        {
            Name = name,
            Summary = summary,
            Description = description,
            Status = status,
            ParentEntityType = parent.ParentEntityType,
            DestinationId = parent.DestinationId,
            Destination = parent.Destination,
            LocationId = parent.LocationId,
            Location = parent.Location,
            PlaceId = parent.PlaceId,
            Place = parent.Place,
            EventTypeId = eventTypeId,
            VenueName = NormalizeOptionalText(request.VenueName, 200, "Venue name is too long."),
            IsAllDay = request.IsAllDay,
            StartDate = schedule.StartsAt,
            EndDate = schedule.EndsAt,
            IsRecurring = request.IsRecurring,
            IsFree = request.IsFree,
            TicketPrice = ticketPrice,
            ExternalTicketLink = NormalizeUrl(request.ExternalTicketLink, "External ticket link is invalid."),
            HostedBy = NormalizeOptionalText(request.OrganizerName, 200, "Organizer name is too long."),
            ContactEmail = NormalizeEmail(request.ContactEmail),
            AttendanceLimit = NormalizeAttendanceLimit(request.AttendanceLimit),
            AgeRestriction = NormalizeOptionalText(request.AgeRestriction, 100, "Age restriction is too long."),
            CreatedByUserId = userId,
            PublishedAt = status == PublishedStatus ? now : null,
            CreatedAt = now,
            Images = images
                .Select(image => new EntityImage
                {
                    TargetType = EntityImageTargetType.Event,
                    Url = image.Url,
                    AltText = image.AltText,
                    Caption = image.Caption,
                    IsMain = image.IsMain,
                    SortOrder = image.SortOrder,
                    CreatedAt = now
                })
                .ToList(),
            EventTags = tagIds
                .Select(tagId => new EventTag { TagId = tagId })
                .ToList()
        };

        _db.Events.Add(@event);
        await _db.SaveChangesAsync();

        @event.InternalReferenceId = BuildInternalReferenceId(@event.Id);
        await _db.SaveChangesAsync();
        await transaction.CommitAsync();

        return await GetEventAsync(userId, request.RegionId, @event.Id);
    }

    public async Task<RegionalEventDetailDto> UpdateEventAsync(
        int userId,
        int regionId,
        int eventId,
        RegionalEventUpdateRequestDto request)
    {
        await EnsureAssignedRegionAsync(userId, regionId);

        var @event = await _db.Events
            .Include(item => item.Destination)
            .Include(item => item.EventTags)
            .Include(item => item.Images)
            .SingleOrDefaultAsync(item => item.Id == eventId && item.Destination.RegionId == regionId)
            ?? throw new KeyNotFoundException("Event not found.");

        if (!string.IsNullOrWhiteSpace(request.ParentEntityType))
        {
            await ApplyParentUpdateAsync(@event, regionId, request);
        }
        else if (request.LocationId.HasValue)
        {
            var location = await _db.Locations
                .SingleOrDefaultAsync(item =>
                    item.Id == request.LocationId.Value &&
                    item.Destination.RegionId == regionId)
                ?? throw new InvalidOperationException("Location is invalid for the selected region.");

            @event.LocationId = location.Id;
            @event.DestinationId = location.DestinationId;
            @event.PlaceId = null;
            @event.ParentEntityType = "Location";
        }

        if (request.PlaceId.HasValue)
        {
            if (request.PlaceId.Value <= 0)
                throw new InvalidOperationException("Place is invalid.");

            var place = await _db.Places
                .Include(item => item.Location)
                .SingleOrDefaultAsync(item =>
                    item.Id == request.PlaceId.Value &&
                    item.Location.Destination.RegionId == regionId)
                ?? throw new InvalidOperationException("Place is invalid for the selected region.");

            @event.PlaceId = place.Id;
            @event.LocationId = place.LocationId;
            @event.DestinationId = place.Location.DestinationId;
            @event.ParentEntityType = "Place";
        }

        if (request.EventTypeId.HasValue &&
            !await _db.EventTypes.AnyAsync(item => item.Id == request.EventTypeId.Value))
        {
            throw new InvalidOperationException("Event type is invalid.");
        }

        if (request.TagIds is not null)
        {
            var tagIds = await ValidateEventTagIdsAsync(request.TagIds);
            ReplaceTags(@event, tagIds);
        }

        if (request.Images is not null)
        {
            var fallbackAltText = request.Name ?? @event.Name;
            ReplaceImages(@event, request.Images, fallbackAltText);
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
            .SingleOrDefaultAsync(item => item.Id == eventId && item.Destination.RegionId == regionId)
            ?? throw new KeyNotFoundException("Event not found.");

        if (await HasBlockingDependenciesAsync(eventId))
        {
            throw new InvalidOperationException("This event cannot be deleted while it has related content.");
        }

        _db.Events.Remove(@event);
        await _db.SaveChangesAsync();
    }

    public async Task<List<RegionalEventParentDestinationDto>> GetParentDestinationsAsync(
        int userId,
        int regionId,
        string? search,
        int take)
    {
        await EnsureAssignedRegionAsync(userId, regionId);

        var normalizedTake = Math.Clamp(take <= 0 ? MaxTake : take, 1, MaxTake);
        var query = _db.Destinations
            .AsNoTracking()
            .Where(destination => destination.RegionId == regionId);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLowerInvariant();
            query = query.Where(destination =>
                destination.Name.ToLower().Contains(term) ||
                (destination.InternalReferenceId != null && destination.InternalReferenceId.ToLower().Contains(term)));
        }

        return await query
            .OrderBy(destination => destination.Name)
            .ThenBy(destination => destination.Id)
            .Take(normalizedTake)
            .Select(destination => new RegionalEventParentDestinationDto
            {
                Id = destination.Id,
                Name = destination.Name,
                DisplayName = destination.Name,
                Status = destination.Status,
                Latitude = destination.Latitude,
                Longitude = destination.Longitude
            })
            .ToListAsync();
    }

    public async Task<List<RegionalEventParentLocationDto>> GetParentLocationsAsync(
        int userId,
        int regionId,
        string? search,
        int take)
    {
        await EnsureAssignedRegionAsync(userId, regionId);

        var normalizedTake = Math.Clamp(take <= 0 ? MaxTake : take, 1, MaxTake);
        var query = _db.Locations
            .AsNoTracking()
            .Where(location => location.Destination.RegionId == regionId);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLowerInvariant();
            query = query.Where(location =>
                location.Name.ToLower().Contains(term) ||
                (location.InternalReferenceId != null && location.InternalReferenceId.ToLower().Contains(term)) ||
                location.Destination.Name.ToLower().Contains(term));
        }

        return await query
            .OrderBy(location => location.Name)
            .ThenBy(location => location.Id)
            .Take(normalizedTake)
            .Select(location => new RegionalEventParentLocationDto
            {
                Id = location.Id,
                Name = location.Name,
                DisplayName = location.Name,
                DestinationId = location.DestinationId,
                DestinationName = location.Destination.Name,
                Status = location.Status,
                Latitude = location.Latitude,
                Longitude = location.Longitude
            })
            .ToListAsync();
    }

    public async Task<List<RegionalEventParentPlaceDto>> GetParentPlacesAsync(
        int userId,
        int regionId,
        string? search,
        int take)
    {
        await EnsureAssignedRegionAsync(userId, regionId);

        var normalizedTake = Math.Clamp(take <= 0 ? MaxTake : take, 1, MaxTake);
        var query = _db.Places
            .AsNoTracking()
            .Where(place => place.Location.Destination.RegionId == regionId);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLowerInvariant();
            query = query.Where(place =>
                place.Name.ToLower().Contains(term) ||
                (place.InternalReferenceId != null && place.InternalReferenceId.ToLower().Contains(term)) ||
                place.Location.Name.ToLower().Contains(term) ||
                place.Location.Destination.Name.ToLower().Contains(term));
        }

        return await query
            .OrderBy(place => place.Name)
            .ThenBy(place => place.Id)
            .Take(normalizedTake)
            .Select(place => new RegionalEventParentPlaceDto
            {
                Id = place.Id,
                Name = place.Name,
                DisplayName = place.Name,
                LocationId = place.LocationId,
                LocationName = place.Location.Name,
                DestinationId = place.Location.DestinationId,
                DestinationName = place.Location.Destination.Name,
                Address = place.Address,
                Status = place.Status,
                Latitude = place.Latitude,
                Longitude = place.Longitude
            })
            .ToListAsync();
    }

    public async Task<List<RegionalEventTagDto>> GetEventTagsAsync(int userId, string? search)
    {
        await EnsureRegionalManagerRoleAsync(userId);

        var query = _db.Tags
            .AsNoTracking()
            .Where(tag => tag.ForEvent);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLowerInvariant();
            query = query.Where(tag =>
                tag.Name.ToLower().Contains(term) ||
                tag.Slug.ToLower().Contains(term));
        }

        return await query
            .OrderBy(tag => tag.Name)
            .Take(50)
            .Select(tag => new RegionalEventTagDto
            {
                Id = tag.Id,
                Name = tag.Name,
                Slug = tag.Slug
            })
            .ToListAsync();
    }

    public async Task<List<RegionalEventTypeDto>> GetEventTypesAsync(int userId)
    {
        await EnsureRegionalManagerRoleAsync(userId);

        return await _db.EventTypes
            .AsNoTracking()
            .OrderBy(type => type.Name)
            .ThenBy(type => type.Id)
            .Select(type => new RegionalEventTypeDto
            {
                Id = type.Id,
                Name = type.Name,
                Description = type.Description
            })
            .ToListAsync();
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
            (@event.Summary != null && @event.Summary.ToLower().Contains(term)) ||
            (@event.InternalReferenceId != null && @event.InternalReferenceId.ToLower().Contains(term)) ||
            @event.Description.ToLower().Contains(term) ||
            @event.EventType.Name.ToLower().Contains(term) ||
            (@event.VenueName != null && @event.VenueName.ToLower().Contains(term)) ||
            (@event.Location != null && @event.Location.Name.ToLower().Contains(term)) ||
            @event.Destination.Name.ToLower().Contains(term) ||
            @event.Destination.Region.Name.ToLower().Contains(term) ||
            @event.Destination.Region.Territory.Name.ToLower().Contains(term) ||
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
            if (description.Length > 8000)
                throw new InvalidOperationException("Description is too long.");

            @event.Description = description;
        }

        if (request.Summary is not null)
        {
            @event.Summary = NormalizeOptionalText(request.Summary, 160, "Summary is too long.");
        }

        if (request.Status is not null)
        {
            @event.Status = NormalizeUpdateStatus(request.Status);
        }

        if (request.EventTypeId.HasValue)
        {
            if (request.EventTypeId.Value <= 0)
                throw new InvalidOperationException("Event type is invalid.");

            @event.EventTypeId = request.EventTypeId.Value;
        }

        var isAllDay = request.IsAllDay ?? @event.IsAllDay;
        var schedule = NormalizeSchedule(request.StartsAt ?? @event.StartDate, request.EndsAt ?? @event.EndDate, isAllDay);

        if (request.IsAllDay.HasValue)
            @event.IsAllDay = request.IsAllDay.Value;

        if (request.StartsAt.HasValue || request.EndsAt.HasValue || request.IsAllDay.HasValue)
        {
            @event.StartDate = schedule.StartsAt;
            @event.EndDate = schedule.EndsAt;
        }

        if (request.VenueName is not null)
        {
            @event.VenueName = NormalizeOptionalText(request.VenueName, 200, "Venue name is too long.");
        }

        if (request.IsRecurring.HasValue)
        {
            @event.IsRecurring = request.IsRecurring.Value;
        }

        if (request.IsFree.HasValue)
        {
            @event.IsFree = request.IsFree.Value;
        }

        if (request.IsFree == true)
        {
            @event.TicketPrice = null;
        }
        else
        {
            @event.TicketPrice = NormalizeOptionalText(request.TicketPrice, 100, "Ticket price is too long.", @event.TicketPrice);
        }

        if (request.ExternalTicketLink is not null)
        {
            @event.ExternalTicketLink = NormalizeUrl(request.ExternalTicketLink, "External ticket link is invalid.");
        }

        @event.Duration = NormalizeOptionalText(request.Duration, 100, "Duration is too long.", @event.Duration);
        @event.HostedBy = NormalizeOptionalText(request.OrganizerName ?? request.HostedBy, 200, "Host name is too long.", @event.HostedBy);

        if (request.ContactEmail is not null)
        {
            @event.ContactEmail = NormalizeEmail(request.ContactEmail);
        }

        if (request.AttendanceLimit.HasValue)
        {
            @event.AttendanceLimit = NormalizeAttendanceLimit(request.AttendanceLimit);
        }

        if (request.AgeRestriction is not null)
        {
            @event.AgeRestriction = NormalizeOptionalText(request.AgeRestriction, 100, "Age restriction is too long.");
        }

        @event.VenueDescription = NormalizeOptionalText(request.VenueDescription, 2000, "Venue description is too long.", @event.VenueDescription);
        @event.ArrivalTips = NormalizeOptionalText(request.ArrivalTips, 2000, "Arrival tips are too long.", @event.ArrivalTips);
        @event.Accessibility = NormalizeOptionalText(request.Accessibility, 1000, "Accessibility notes are too long.", @event.Accessibility);
    }

    private async Task ApplyParentUpdateAsync(Event @event, int regionId, RegionalEventUpdateRequestDto request)
    {
        var parentType = NormalizeToken(request.ParentEntityType ?? string.Empty);
        switch (parentType)
        {
            case "destination":
            {
                if (!request.DestinationId.HasValue || request.DestinationId.Value <= 0)
                    throw new InvalidOperationException("Parent destination is required.");

                var destination = await _db.Destinations
                    .SingleOrDefaultAsync(item => item.Id == request.DestinationId.Value && item.RegionId == regionId)
                    ?? throw new InvalidOperationException("Destination is invalid for the selected region.");

                @event.DestinationId = destination.Id;
                @event.LocationId = null;
                @event.PlaceId = null;
                @event.ParentEntityType = "Destination";
                break;
            }

            case "location":
            {
                if (!request.LocationId.HasValue || request.LocationId.Value <= 0)
                    throw new InvalidOperationException("Parent location is required.");

                var location = await _db.Locations
                    .SingleOrDefaultAsync(item => item.Id == request.LocationId.Value && item.Destination.RegionId == regionId)
                    ?? throw new InvalidOperationException("Location is invalid for the selected region.");

                @event.DestinationId = location.DestinationId;
                @event.LocationId = location.Id;
                @event.PlaceId = null;
                @event.ParentEntityType = "Location";
                break;
            }

            case "place":
            {
                if (!request.PlaceId.HasValue || request.PlaceId.Value <= 0)
                    throw new InvalidOperationException("Parent place is required.");

                var place = await _db.Places
                    .Include(item => item.Location)
                    .SingleOrDefaultAsync(item => item.Id == request.PlaceId.Value && item.Location.Destination.RegionId == regionId)
                    ?? throw new InvalidOperationException("Place is invalid for the selected region.");

                @event.DestinationId = place.Location.DestinationId;
                @event.LocationId = place.LocationId;
                @event.PlaceId = place.Id;
                @event.ParentEntityType = "Place";
                break;
            }

            default:
                throw new InvalidOperationException("Event parent type is required.");
        }
    }

    private void ReplaceImages(
        Event @event,
        IEnumerable<RegionalEntityImageInputDto> imageRequests,
        string fallbackAltText)
    {
        var images = RegionalEntityImageRequestNormalizer.Normalize(imageRequests, fallbackAltText);
        var now = DateTime.UtcNow;

        _db.EntityImages.RemoveRange(@event.Images.Where(image => image.TargetType == EntityImageTargetType.Event));

        foreach (var image in images)
        {
            @event.Images.Add(new EntityImage
            {
                TargetType = EntityImageTargetType.Event,
                EventId = @event.Id,
                Url = image.Url,
                AltText = image.AltText,
                Caption = image.Caption,
                IsMain = image.IsMain,
                SortOrder = image.SortOrder,
                CreatedAt = now
            });
        }
    }

    private static void ReplaceTags(Event @event, IEnumerable<int> tagIds)
    {
        @event.EventTags.Clear();
        foreach (var tagId in tagIds)
        {
            @event.EventTags.Add(new EventTag
            {
                EventId = @event.Id,
                TagId = tagId
            });
        }
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

    private static string? NormalizeOptionalText(string? value, int maxLength, string errorMessage)
    {
        if (value is null)
            return null;

        var normalized = value.Trim();
        if (normalized.Length > maxLength)
            throw new InvalidOperationException(errorMessage);

        return normalized.Length == 0 ? null : normalized;
    }

    private static string NormalizeRequiredText(string? value, string requiredMessage, int maxLength, string lengthMessage)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
            throw new InvalidOperationException(requiredMessage);

        if (normalized.Length > maxLength)
            throw new InvalidOperationException(lengthMessage);

        return normalized;
    }

    private static string NormalizeCreateStatus(string? status)
    {
        if (string.IsNullOrWhiteSpace(status))
            return PublishedStatus;

        var normalized = NormalizeToken(status);
        return normalized switch
        {
            "published" or "publish" => PublishedStatus,
            "draft" => DraftStatus,
            _ => throw new InvalidOperationException("Event status is invalid.")
        };
    }

    private static string NormalizeUpdateStatus(string status)
    {
        var normalized = NormalizeToken(status);
        if (normalized is "published" or "publish")
            return PublishedStatus;
        if (normalized == "draft")
            return DraftStatus;
        if (normalized == "archived")
            return ArchivedStatus;

        throw new InvalidOperationException("Event status is invalid.");
    }

    private async Task<int> ResolveEventTypeIdAsync(int? eventTypeId)
    {
        if (!eventTypeId.HasValue || eventTypeId.Value <= 0)
            throw new InvalidOperationException("Event category/type is required.");

        var exists = await _db.EventTypes
            .AsNoTracking()
            .AnyAsync(type => type.Id == eventTypeId.Value);

        if (!exists)
            throw new InvalidOperationException("Event type is invalid.");

        return eventTypeId.Value;
    }

    private async Task<ResolvedEventParent> ResolveParentAsync(
        int regionId,
        RegionalEventCreateRequestDto request)
    {
        var parentType = NormalizeToken(request.ParentEntityType ?? string.Empty);
        return parentType switch
        {
            "destination" => await ResolveDestinationParentAsync(regionId, request),
            "location" => await ResolveLocationParentAsync(regionId, request),
            "place" => await ResolvePlaceParentAsync(regionId, request),
            _ => throw new InvalidOperationException("Event parent type is required.")
        };
    }

    private async Task<ResolvedEventParent> ResolveDestinationParentAsync(
        int regionId,
        RegionalEventCreateRequestDto request)
    {
        if (!request.DestinationId.HasValue || request.DestinationId.Value <= 0)
            throw new InvalidOperationException("Parent destination is required.");

        var destination = await _db.Destinations
            .SingleOrDefaultAsync(item =>
                item.Id == request.DestinationId.Value &&
                item.RegionId == regionId)
            ?? throw new InvalidOperationException("Destination is invalid for the selected region.");

        if (request.LocationId.HasValue || request.PlaceId.HasValue)
            throw new InvalidOperationException("Destination events cannot include location or place ids.");

        return new ResolvedEventParent(
            "Destination",
            destination.Id,
            destination,
            null,
            null,
            null,
            null);
    }

    private async Task<ResolvedEventParent> ResolveLocationParentAsync(
        int regionId,
        RegionalEventCreateRequestDto request)
    {
        if (!request.LocationId.HasValue || request.LocationId.Value <= 0)
            throw new InvalidOperationException("Parent location is required.");

        var location = await _db.Locations
            .Include(item => item.Destination)
            .SingleOrDefaultAsync(item =>
                item.Id == request.LocationId.Value &&
                item.Destination.RegionId == regionId)
            ?? throw new InvalidOperationException("Location is invalid for the selected region.");

        if (request.PlaceId.HasValue)
            throw new InvalidOperationException("Location events cannot include a place id.");

        if (request.DestinationId.HasValue && request.DestinationId.Value != location.DestinationId)
            throw new InvalidOperationException("Parent destination does not match the selected location.");

        return new ResolvedEventParent(
            "Location",
            location.DestinationId,
            location.Destination,
            location.Id,
            location,
            null,
            null);
    }

    private async Task<ResolvedEventParent> ResolvePlaceParentAsync(
        int regionId,
        RegionalEventCreateRequestDto request)
    {
        if (!request.PlaceId.HasValue || request.PlaceId.Value <= 0)
            throw new InvalidOperationException("Parent place is required.");

        var place = await _db.Places
            .Include(item => item.Location)
                .ThenInclude(location => location.Destination)
            .SingleOrDefaultAsync(item =>
                item.Id == request.PlaceId.Value &&
                item.Location.Destination.RegionId == regionId)
            ?? throw new InvalidOperationException("Place is invalid for the selected region.");

        if (request.LocationId.HasValue && request.LocationId.Value != place.LocationId)
            throw new InvalidOperationException("Parent location does not match the selected place.");

        if (request.DestinationId.HasValue && request.DestinationId.Value != place.Location.DestinationId)
            throw new InvalidOperationException("Parent destination does not match the selected place.");

        return new ResolvedEventParent(
            "Place",
            place.Location.DestinationId,
            place.Location.Destination,
            place.LocationId,
            place.Location,
            place.Id,
            place);
    }

    private static NormalizedSchedule NormalizeSchedule(
        DateTime? startsAt,
        DateTime? endsAt,
        bool isAllDay)
    {
        if (!startsAt.HasValue)
            throw new InvalidOperationException("Start date is required.");

        if (!endsAt.HasValue)
            throw new InvalidOperationException("End date is required.");

        if (isAllDay)
        {
            var startDate = startsAt.Value.Date;
            var endDate = endsAt.Value.Date;
            if (endDate < startDate)
                throw new InvalidOperationException("Event end date must be after the start date.");

            return new NormalizedSchedule(startDate, endDate.AddDays(1).AddTicks(-1));
        }

        if (endsAt.Value <= startsAt.Value)
            throw new InvalidOperationException("Event end time must be after the start time.");

        return new NormalizedSchedule(startsAt.Value, endsAt.Value);
    }

    private async Task<List<int>> ValidateEventTagIdsAsync(IEnumerable<int>? tagIds)
    {
        var normalizedIds = (tagIds ?? Enumerable.Empty<int>())
            .Where(tagId => tagId > 0)
            .Distinct()
            .ToList();

        if (normalizedIds.Count == 0)
            return normalizedIds;

        var validIds = await _db.Tags
            .AsNoTracking()
            .Where(tag => normalizedIds.Contains(tag.Id) && tag.ForEvent)
            .Select(tag => tag.Id)
            .ToListAsync();

        if (validIds.Count != normalizedIds.Count)
            throw new InvalidOperationException("One or more selected tags are not compatible with events.");

        return normalizedIds;
    }

    private static string? NormalizeTicketPrice(string? value)
    {
        var normalized = NormalizeOptionalText(value, 100, "Ticket price is too long.");
        if (normalized is null)
            return null;

        var numericText = normalized
            .Replace("$", string.Empty, StringComparison.Ordinal)
            .Replace("€", string.Empty, StringComparison.Ordinal)
            .Replace("£", string.Empty, StringComparison.Ordinal)
            .Replace(",", string.Empty, StringComparison.Ordinal)
            .Trim();

        if (numericText.StartsWith("-", StringComparison.Ordinal) ||
            (decimal.TryParse(numericText, NumberStyles.Number, CultureInfo.InvariantCulture, out var amount) && amount < 0))
        {
            throw new InvalidOperationException("Ticket price must be non-negative.");
        }

        return normalized;
    }

    private static string? NormalizeUrl(string? value, string errorMessage)
    {
        var normalized = NormalizeOptionalText(value, 500, "External ticket link is too long.");
        if (normalized is null)
            return null;

        if (!Uri.TryCreate(normalized, UriKind.Absolute, out var uri) ||
            uri.UserInfo.Length > 0 ||
            uri.Scheme is not ("http" or "https"))
        {
            throw new InvalidOperationException(errorMessage);
        }

        return normalized;
    }

    private static string? NormalizeEmail(string? value)
    {
        var normalized = NormalizeOptionalText(value, 200, "Contact email is too long.");
        if (normalized is null)
            return null;

        try
        {
            var address = new MailAddress(normalized);
            if (!string.Equals(address.Address, normalized, StringComparison.OrdinalIgnoreCase))
                throw new FormatException();
        }
        catch (FormatException)
        {
            throw new InvalidOperationException("Contact email is invalid.");
        }

        return normalized;
    }

    private static int? NormalizeAttendanceLimit(int? attendanceLimit)
    {
        if (!attendanceLimit.HasValue)
            return null;

        if (attendanceLimit.Value <= 0)
            throw new InvalidOperationException("Attendance limit must be greater than zero.");

        return attendanceLimit.Value;
    }

    private static string BuildInternalReferenceId(int eventId) => $"EVT-{eventId:D6}";

    private static RegionalEventListItemDto MapListItem(EventProjection row)
    {
        return new RegionalEventListItemDto
        {
            Id = row.Id,
            Name = row.Name,
            Summary = row.Summary ?? BuildSummary(row.Description),
            Description = row.Description,
            InternalReferenceId = row.InternalReferenceId,
            EventType = row.EventTypeName,
            EventTypeId = row.EventTypeId,
            Category = row.EventTypeName,
            Status = row.Status,
            ParentEntityType = row.ParentEntityType,
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
            IsAllDay = row.IsAllDay,
            IsRecurring = row.IsRecurring,
            CreatedAt = row.CreatedAt,
            LastUpdatedAt = row.UpdatedAt ?? row.CreatedAt,
            CreatedByUserId = row.CreatedByUserId,
            PublishedAt = row.PublishedAt,
            VenueName = row.VenueName,
            TicketPrice = row.TicketPrice,
            IsFree = row.IsFree,
            ExternalTicketLink = row.ExternalTicketLink,
            Duration = row.Duration,
            HostedBy = row.HostedBy,
            ContactEmail = row.ContactEmail,
            AttendanceLimit = row.AttendanceLimit,
            AgeRestriction = row.AgeRestriction,
            VenueDescription = row.VenueDescription,
            ArrivalTips = row.ArrivalTips,
            Accessibility = row.Accessibility,
            Tags = row.Tags,
            ImageUrl = row.ImageUrl,
            Images = row.Images,
            DetailRoute = "/events/detail/" + row.Id,
            EditRoute = "/events/edit/" + row.Id,
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
            InternalReferenceId = item.InternalReferenceId,
            EventType = item.EventType,
            EventTypeId = item.EventTypeId,
            Category = item.Category,
            Status = item.Status,
            ParentEntityType = item.ParentEntityType,
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
            IsAllDay = item.IsAllDay,
            IsRecurring = item.IsRecurring,
            CreatedAt = item.CreatedAt,
            LastUpdatedAt = item.LastUpdatedAt,
            CreatedByUserId = item.CreatedByUserId,
            PublishedAt = item.PublishedAt,
            VenueName = item.VenueName,
            TicketPrice = item.TicketPrice,
            IsFree = item.IsFree,
            ExternalTicketLink = item.ExternalTicketLink,
            Duration = item.Duration,
            HostedBy = item.HostedBy,
            ContactEmail = item.ContactEmail,
            AttendanceLimit = item.AttendanceLimit,
            AgeRestriction = item.AgeRestriction,
            VenueDescription = item.VenueDescription,
            ArrivalTips = item.ArrivalTips,
            Accessibility = item.Accessibility,
            Tags = item.Tags,
            ImageUrl = item.ImageUrl,
            Images = item.Images,
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
        public string? Summary { get; set; }
        public string? InternalReferenceId { get; set; }
        public string? Description { get; set; }
        public int EventTypeId { get; set; }
        public string? EventTypeName { get; set; }
        public string Status { get; set; } = PublishedStatus;
        public string ParentEntityType { get; set; } = "Location";
        public int DestinationId { get; set; }
        public string DestinationName { get; set; } = string.Empty;
        public int? LocationId { get; set; }
        public string? LocationName { get; set; }
        public int RegionId { get; set; }
        public string RegionName { get; set; } = string.Empty;
        public string? TerritoryName { get; set; }
        public int? PlaceId { get; set; }
        public string? PlaceName { get; set; }
        public string? Address { get; set; }
        public DateTime StartsAt { get; set; }
        public DateTime EndsAt { get; set; }
        public bool IsAllDay { get; set; }
        public bool IsRecurring { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
        public int? CreatedByUserId { get; set; }
        public DateTime? PublishedAt { get; set; }
        public string? VenueName { get; set; }
        public string? TicketPrice { get; set; }
        public bool IsFree { get; set; }
        public string? ExternalTicketLink { get; set; }
        public string? Duration { get; set; }
        public string? HostedBy { get; set; }
        public string? ContactEmail { get; set; }
        public int? AttendanceLimit { get; set; }
        public string? AgeRestriction { get; set; }
        public string? VenueDescription { get; set; }
        public string? ArrivalTips { get; set; }
        public string? Accessibility { get; set; }
        public List<RegionalEventTagDto> Tags { get; set; } = new();
        public string? ImageUrl { get; set; }
        public List<RegionalEntityImageDto> Images { get; set; } = new();
        public double? Latitude { get; set; }
        public double? Longitude { get; set; }
    }

    private sealed record ResolvedEventParent(
        string ParentEntityType,
        int DestinationId,
        Destination Destination,
        int? LocationId,
        Location? Location,
        int? PlaceId,
        Place? Place);

    private sealed record NormalizedSchedule(DateTime StartsAt, DateTime EndsAt);
}
