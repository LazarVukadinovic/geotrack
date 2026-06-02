using Geotrack.Application.DTOs.Admin;
using Geotrack.Application.Interfaces.Service;
using Geotrack.Application.Security;
using Geotrack.Domain.Entities.Core;
using Microsoft.EntityFrameworkCore;

namespace Geotrack.Infrastructure.Services;

public sealed class ContentModeratorEntityReviewService : IContentModeratorEntityReviewService
{
    private const int DefaultTake = 25;
    private const int MaxTake = 50;

    private const string DestinationEntityType = "destination";
    private const string LocationEntityType = "location";
    private const string PlaceEntityType = "place";
    private const string EventEntityType = "event";

    private const string PublishedStatus = "Published";
    private const string SuspendedStatus = "Draft";
    private const string DeletedStatus = "Archived";

    private readonly AppDbContext _db;
    private readonly IModerationAuditService _auditService;

    public ContentModeratorEntityReviewService(
        AppDbContext db,
        IModerationAuditService auditService)
    {
        _db = db;
        _auditService = auditService;
    }

    public async Task<ContentModeratorEntityReviewListResponseDto> GetEntitiesAsync(
        int currentUserId,
        string? type,
        string? search,
        string? status,
        string? sort,
        int skip,
        int take,
        CancellationToken cancellationToken = default)
    {
        await ValidateContentModeratorAsync(currentUserId, cancellationToken);

        var entityType = NormalizeEntityType(type);
        var (normalizedSkip, normalizedTake) = NormalizePagination(skip, take);

        return entityType switch
        {
            DestinationEntityType => await GetDestinationsAsync(search, status, sort, normalizedSkip, normalizedTake, cancellationToken),
            LocationEntityType => await GetLocationsAsync(search, status, sort, normalizedSkip, normalizedTake, cancellationToken),
            PlaceEntityType => await GetPlacesAsync(search, status, sort, normalizedSkip, normalizedTake, cancellationToken),
            EventEntityType => await GetEventsAsync(search, status, sort, normalizedSkip, normalizedTake, cancellationToken),
            _ => throw new InvalidOperationException("Unsupported entity type.")
        };
    }

    public async Task<ContentModeratorEntityReviewDetailDto> GetEntityAsync(
        int currentUserId,
        string type,
        int entityId,
        CancellationToken cancellationToken = default)
    {
        await ValidateContentModeratorAsync(currentUserId, cancellationToken);

        var entityType = NormalizeEntityType(type);

        return entityType switch
        {
            DestinationEntityType => await GetDestinationDetailAsync(entityId, cancellationToken),
            LocationEntityType => await GetLocationDetailAsync(entityId, cancellationToken),
            PlaceEntityType => await GetPlaceDetailAsync(entityId, cancellationToken),
            EventEntityType => await GetEventDetailAsync(entityId, cancellationToken),
            _ => throw new InvalidOperationException("Unsupported entity type.")
        };
    }

    public async Task<ContentModeratorEntityReviewDetailDto> SuspendEntityAsync(
        int currentUserId,
        string type,
        int entityId,
        ContentModeratorModerationActionRequestDto? request,
        CancellationToken cancellationToken = default)
    {
        await ValidateContentModeratorAsync(currentUserId, cancellationToken);
        ValidateModerationActionRequest(request);

        var entityType = NormalizeEntityType(type);
        var reason = NormalizeModerationText(request?.Reason);
        var internalNote = NormalizeModerationText(request?.InternalNote);
        var now = DateTime.UtcNow;
        var previousStatus = string.Empty;
        var newStatus = string.Empty;

        switch (entityType)
        {
            case DestinationEntityType:
                var destination = await _db.Destinations.SingleOrDefaultAsync(entity => entity.Id == entityId, cancellationToken)
                    ?? throw new KeyNotFoundException("Destination was not found.");
                previousStatus = destination.Status;
                EnsureCanSuspend(destination.Status);
                destination.Status = SuspendedStatus;
                newStatus = destination.Status;
                destination.PublishedAt = null;
                destination.UpdatedAt = now;
                break;

            case LocationEntityType:
                var location = await _db.Locations.SingleOrDefaultAsync(entity => entity.Id == entityId, cancellationToken)
                    ?? throw new KeyNotFoundException("Location was not found.");
                previousStatus = location.Status;
                EnsureCanSuspend(location.Status);
                location.Status = SuspendedStatus;
                newStatus = location.Status;
                location.PublishedAt = null;
                location.UpdatedAt = now;
                break;

            case PlaceEntityType:
                var place = await _db.Places.SingleOrDefaultAsync(entity => entity.Id == entityId, cancellationToken)
                    ?? throw new KeyNotFoundException("Place was not found.");
                previousStatus = place.Status;
                EnsureCanSuspend(place.Status);
                place.Status = SuspendedStatus;
                newStatus = place.Status;
                place.PublishedAt = null;
                place.UpdatedAt = now;
                break;

            case EventEntityType:
                var @event = await _db.Events.SingleOrDefaultAsync(entity => entity.Id == entityId, cancellationToken)
                    ?? throw new KeyNotFoundException("Event was not found.");
                previousStatus = @event.Status;
                EnsureCanSuspend(@event.Status);
                @event.Status = SuspendedStatus;
                newStatus = @event.Status;
                @event.PublishedAt = null;
                @event.UpdatedAt = now;
                break;

            default:
                throw new InvalidOperationException("Unsupported entity type.");
        }

        await _auditService.RecordAsync(
            currentUserId,
            ToAuditTargetType(entityType),
            entityId,
            "SuspendEntity",
            previousStatus,
            newStatus,
            reason,
            internalNote,
            "ContentModerator",
            cancellationToken: cancellationToken);

        await _db.SaveChangesAsync(cancellationToken);
        return await GetEntityAsync(currentUserId, entityType, entityId, cancellationToken);
    }

    public async Task<ContentModeratorEntityReviewDetailDto> UnsuspendEntityAsync(
        int currentUserId,
        string type,
        int entityId,
        ContentModeratorModerationActionRequestDto? request,
        CancellationToken cancellationToken = default)
    {
        await ValidateContentModeratorAsync(currentUserId, cancellationToken);
        ValidateModerationActionRequest(request);

        var entityType = NormalizeEntityType(type);
        var reason = NormalizeModerationText(request?.Reason);
        var internalNote = NormalizeModerationText(request?.InternalNote);
        var now = DateTime.UtcNow;
        var previousStatus = string.Empty;
        var newStatus = string.Empty;

        switch (entityType)
        {
            case DestinationEntityType:
                var destination = await _db.Destinations.SingleOrDefaultAsync(entity => entity.Id == entityId, cancellationToken)
                    ?? throw new KeyNotFoundException("Destination was not found.");
                previousStatus = destination.Status;
                EnsureCanUnsuspend(destination.Status);
                destination.Status = PublishedStatus;
                newStatus = destination.Status;
                destination.PublishedAt ??= now;
                destination.UpdatedAt = now;
                break;

            case LocationEntityType:
                var location = await _db.Locations.SingleOrDefaultAsync(entity => entity.Id == entityId, cancellationToken)
                    ?? throw new KeyNotFoundException("Location was not found.");
                previousStatus = location.Status;
                EnsureCanUnsuspend(location.Status);
                location.Status = PublishedStatus;
                newStatus = location.Status;
                location.PublishedAt ??= now;
                location.UpdatedAt = now;
                break;

            case PlaceEntityType:
                var place = await _db.Places.SingleOrDefaultAsync(entity => entity.Id == entityId, cancellationToken)
                    ?? throw new KeyNotFoundException("Place was not found.");
                previousStatus = place.Status;
                EnsureCanUnsuspend(place.Status);
                place.Status = PublishedStatus;
                newStatus = place.Status;
                place.PublishedAt ??= now;
                place.UpdatedAt = now;
                break;

            case EventEntityType:
                var @event = await _db.Events.SingleOrDefaultAsync(entity => entity.Id == entityId, cancellationToken)
                    ?? throw new KeyNotFoundException("Event was not found.");
                previousStatus = @event.Status;
                EnsureCanUnsuspend(@event.Status);
                @event.Status = PublishedStatus;
                newStatus = @event.Status;
                @event.PublishedAt ??= now;
                @event.UpdatedAt = now;
                break;

            default:
                throw new InvalidOperationException("Unsupported entity type.");
        }

        await _auditService.RecordAsync(
            currentUserId,
            ToAuditTargetType(entityType),
            entityId,
            "UnsuspendEntity",
            previousStatus,
            newStatus,
            reason,
            internalNote,
            "ContentModerator",
            cancellationToken: cancellationToken);

        await _db.SaveChangesAsync(cancellationToken);
        return await GetEntityAsync(currentUserId, entityType, entityId, cancellationToken);
    }

    public async Task<ContentModeratorEntityReviewDetailDto> DeleteEntityAsync(
        int currentUserId,
        string type,
        int entityId,
        ContentModeratorModerationActionRequestDto? request,
        CancellationToken cancellationToken = default)
    {
        await ValidateContentModeratorAsync(currentUserId, cancellationToken);
        ValidateModerationActionRequest(request);

        var entityType = NormalizeEntityType(type);
        var reason = NormalizeModerationText(request?.Reason);
        var internalNote = NormalizeModerationText(request?.InternalNote);
        var now = DateTime.UtcNow;
        var previousStatus = string.Empty;
        var newStatus = string.Empty;

        switch (entityType)
        {
            case DestinationEntityType:
                var destination = await _db.Destinations.SingleOrDefaultAsync(entity => entity.Id == entityId, cancellationToken)
                    ?? throw new KeyNotFoundException("Destination was not found.");
                previousStatus = destination.Status;
                EnsureCanDelete(destination.Status);
                destination.Status = DeletedStatus;
                newStatus = destination.Status;
                destination.PublishedAt = null;
                destination.UpdatedAt = now;
                break;

            case LocationEntityType:
                var location = await _db.Locations.SingleOrDefaultAsync(entity => entity.Id == entityId, cancellationToken)
                    ?? throw new KeyNotFoundException("Location was not found.");
                previousStatus = location.Status;
                EnsureCanDelete(location.Status);
                location.Status = DeletedStatus;
                newStatus = location.Status;
                location.PublishedAt = null;
                location.UpdatedAt = now;
                break;

            case PlaceEntityType:
                var place = await _db.Places.SingleOrDefaultAsync(entity => entity.Id == entityId, cancellationToken)
                    ?? throw new KeyNotFoundException("Place was not found.");
                previousStatus = place.Status;
                EnsureCanDelete(place.Status);
                place.Status = DeletedStatus;
                newStatus = place.Status;
                place.PublishedAt = null;
                place.UpdatedAt = now;
                break;

            case EventEntityType:
                var @event = await _db.Events.SingleOrDefaultAsync(entity => entity.Id == entityId, cancellationToken)
                    ?? throw new KeyNotFoundException("Event was not found.");
                previousStatus = @event.Status;
                EnsureCanDelete(@event.Status);
                @event.Status = DeletedStatus;
                newStatus = @event.Status;
                @event.PublishedAt = null;
                @event.UpdatedAt = now;
                break;

            default:
                throw new InvalidOperationException("Unsupported entity type.");
        }

        await _auditService.RecordAsync(
            currentUserId,
            ToAuditTargetType(entityType),
            entityId,
            "DeleteEntity",
            previousStatus,
            newStatus,
            reason,
            internalNote,
            "ContentModerator",
            cancellationToken: cancellationToken);

        await _db.SaveChangesAsync(cancellationToken);
        return await GetEntityAsync(currentUserId, entityType, entityId, cancellationToken);
    }

    private async Task<ContentModeratorEntityReviewListResponseDto> GetDestinationsAsync(
        string? search,
        string? status,
        string? sort,
        int skip,
        int take,
        CancellationToken cancellationToken)
    {
        var query = _db.Destinations.AsNoTracking();
        query = ApplyDestinationSearch(ApplyDestinationStatusFilter(query, status), search);

        var totalCount = await query.CountAsync(cancellationToken);
        var counts = await GetDestinationCountsAsync(cancellationToken);
        var rows = await ApplyDestinationSort(query, sort)
            .Include(destination => destination.Region)
                .ThenInclude(region => region.Territory)
            .Include(destination => destination.DestinationType)
            .Include(destination => destination.Images)
            .AsSplitQuery()
            .Skip(skip)
            .Take(take)
            .ToListAsync(cancellationToken);

        return BuildListResponse(
            rows.Select(MapDestinationListItem).ToList(),
            totalCount,
            skip,
            take,
            counts);
    }

    private async Task<ContentModeratorEntityReviewListResponseDto> GetLocationsAsync(
        string? search,
        string? status,
        string? sort,
        int skip,
        int take,
        CancellationToken cancellationToken)
    {
        var query = _db.Locations.AsNoTracking();
        query = ApplyLocationSearch(ApplyLocationStatusFilter(query, status), search);

        var totalCount = await query.CountAsync(cancellationToken);
        var counts = await GetLocationCountsAsync(cancellationToken);
        var rows = await ApplyLocationSort(query, sort)
            .Include(location => location.Destination)
                .ThenInclude(destination => destination.Region)
                    .ThenInclude(region => region.Territory)
            .Include(location => location.LocationType)
            .Include(location => location.Images)
            .AsSplitQuery()
            .Skip(skip)
            .Take(take)
            .ToListAsync(cancellationToken);

        return BuildListResponse(
            rows.Select(MapLocationListItem).ToList(),
            totalCount,
            skip,
            take,
            counts);
    }

    private async Task<ContentModeratorEntityReviewListResponseDto> GetPlacesAsync(
        string? search,
        string? status,
        string? sort,
        int skip,
        int take,
        CancellationToken cancellationToken)
    {
        var query = _db.Places.AsNoTracking();
        query = ApplyPlaceSearch(ApplyPlaceStatusFilter(query, status), search);

        var totalCount = await query.CountAsync(cancellationToken);
        var counts = await GetPlaceCountsAsync(cancellationToken);
        var rows = await ApplyPlaceSort(query, sort)
            .Include(place => place.Location)
                .ThenInclude(location => location.Destination)
                    .ThenInclude(destination => destination.Region)
                        .ThenInclude(region => region.Territory)
            .Include(place => place.PlaceType)
            .Include(place => place.Stay)
            .Include(place => place.Images)
            .AsSplitQuery()
            .Skip(skip)
            .Take(take)
            .ToListAsync(cancellationToken);

        return BuildListResponse(
            rows.Select(MapPlaceListItem).ToList(),
            totalCount,
            skip,
            take,
            counts);
    }

    private async Task<ContentModeratorEntityReviewListResponseDto> GetEventsAsync(
        string? search,
        string? status,
        string? sort,
        int skip,
        int take,
        CancellationToken cancellationToken)
    {
        var query = _db.Events.AsNoTracking();
        query = ApplyEventSearch(ApplyEventStatusFilter(query, status), search);

        var totalCount = await query.CountAsync(cancellationToken);
        var counts = await GetEventCountsAsync(cancellationToken);
        var rows = await ApplyEventSort(query, sort)
            .Include(@event => @event.Destination)
                .ThenInclude(destination => destination.Region)
                    .ThenInclude(region => region.Territory)
            .Include(@event => @event.Location)
            .Include(@event => @event.Place)
            .Include(@event => @event.EventType)
            .Include(@event => @event.Images)
            .AsSplitQuery()
            .Skip(skip)
            .Take(take)
            .ToListAsync(cancellationToken);

        return BuildListResponse(
            rows.Select(MapEventListItem).ToList(),
            totalCount,
            skip,
            take,
            counts);
    }

    private async Task<ContentModeratorEntityReviewDetailDto> GetDestinationDetailAsync(
        int destinationId,
        CancellationToken cancellationToken)
    {
        var destination = await _db.Destinations
            .AsNoTracking()
            .Include(entity => entity.Region)
                .ThenInclude(region => region.Territory)
            .Include(entity => entity.DestinationType)
            .Include(entity => entity.Images)
            .Include(entity => entity.DestinationTags)
                .ThenInclude(tag => tag.Tag)
            .AsSplitQuery()
            .SingleOrDefaultAsync(entity => entity.Id == destinationId, cancellationToken)
            ?? throw new KeyNotFoundException("Destination was not found.");

        var dto = MapDestinationDetail(destination);
        dto.Metadata.ContentCounts = new Dictionary<string, int>
        {
            ["locations"] = await _db.Locations.AsNoTracking().CountAsync(location => location.DestinationId == destinationId && location.Status != DeletedStatus, cancellationToken),
            ["events"] = await _db.Events.AsNoTracking().CountAsync(@event => @event.DestinationId == destinationId && @event.Status != DeletedStatus, cancellationToken),
            ["reviews"] = await _db.DestinationReviews.AsNoTracking().CountAsync(review => review.DestinationId == destinationId, cancellationToken)
        };

        return dto;
    }

    private async Task<ContentModeratorEntityReviewDetailDto> GetLocationDetailAsync(
        int locationId,
        CancellationToken cancellationToken)
    {
        var location = await _db.Locations
            .AsNoTracking()
            .Include(entity => entity.Destination)
                .ThenInclude(destination => destination.Region)
                    .ThenInclude(region => region.Territory)
            .Include(entity => entity.LocationType)
            .Include(entity => entity.Images)
            .Include(entity => entity.LocationTags)
                .ThenInclude(tag => tag.Tag)
            .AsSplitQuery()
            .SingleOrDefaultAsync(entity => entity.Id == locationId, cancellationToken)
            ?? throw new KeyNotFoundException("Location was not found.");

        var dto = MapLocationDetail(location);
        dto.Metadata.ContentCounts = new Dictionary<string, int>
        {
            ["places"] = await _db.Places.AsNoTracking().CountAsync(place => place.LocationId == locationId && place.Status != DeletedStatus, cancellationToken),
            ["events"] = await _db.Events.AsNoTracking().CountAsync(@event => @event.LocationId == locationId && @event.Status != DeletedStatus, cancellationToken),
            ["reviews"] = await _db.LocationReviews.AsNoTracking().CountAsync(review => review.LocationId == locationId, cancellationToken)
        };

        return dto;
    }

    private async Task<ContentModeratorEntityReviewDetailDto> GetPlaceDetailAsync(
        int placeId,
        CancellationToken cancellationToken)
    {
        var place = await _db.Places
            .AsNoTracking()
            .Include(entity => entity.Location)
                .ThenInclude(location => location.Destination)
                    .ThenInclude(destination => destination.Region)
                        .ThenInclude(region => region.Territory)
            .Include(entity => entity.PlaceType)
            .Include(entity => entity.Stay)
            .Include(entity => entity.Images)
            .Include(entity => entity.PlaceTags)
                .ThenInclude(tag => tag.Tag)
            .AsSplitQuery()
            .SingleOrDefaultAsync(entity => entity.Id == placeId, cancellationToken)
            ?? throw new KeyNotFoundException("Place was not found.");

        var dto = MapPlaceDetail(place);
        dto.Metadata.ContentCounts = new Dictionary<string, int>
        {
            ["events"] = await _db.Events.AsNoTracking().CountAsync(@event => @event.PlaceId == placeId && @event.Status != DeletedStatus, cancellationToken),
            ["reviews"] = await _db.PlaceReviews.AsNoTracking().CountAsync(review => review.PlaceId == placeId, cancellationToken)
        };

        return dto;
    }

    private async Task<ContentModeratorEntityReviewDetailDto> GetEventDetailAsync(
        int eventId,
        CancellationToken cancellationToken)
    {
        var @event = await _db.Events
            .AsNoTracking()
            .Include(entity => entity.Destination)
                .ThenInclude(destination => destination.Region)
                    .ThenInclude(region => region.Territory)
            .Include(entity => entity.Location)
            .Include(entity => entity.Place)
            .Include(entity => entity.EventType)
            .Include(entity => entity.Images)
            .Include(entity => entity.EventTags)
                .ThenInclude(tag => tag.Tag)
            .AsSplitQuery()
            .SingleOrDefaultAsync(entity => entity.Id == eventId, cancellationToken)
            ?? throw new KeyNotFoundException("Event was not found.");

        var dto = MapEventDetail(@event);
        dto.Metadata.ContentCounts = new Dictionary<string, int>
        {
            ["reviews"] = await _db.EventReviews.AsNoTracking().CountAsync(review => review.EventId == eventId, cancellationToken)
        };

        return dto;
    }

    private async Task ValidateContentModeratorAsync(int currentUserId, CancellationToken cancellationToken)
    {
        var currentUser = await _db.Users
            .AsNoTracking()
            .Where(user => user.Id == currentUserId)
            .Select(user => new
            {
                user.IsDeleted,
                user.IsSuspended,
                Roles = user.UserRoles.Select(userRole => userRole.Role.Name).ToList()
            })
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new KeyNotFoundException("Content Moderator user was not found.");

        var hasContentModeratorRole = currentUser.Roles
            .Select(RoleNames.Normalize)
            .Contains(RoleNames.ContentModerator, StringComparer.Ordinal);

        if (!hasContentModeratorRole)
            throw new UnauthorizedAccessException("Content Moderator role is required.");

        if (currentUser.IsDeleted || currentUser.IsSuspended)
            throw new UnauthorizedAccessException("Content Moderator account is not active.");
    }

    private static ContentModeratorEntityReviewListResponseDto BuildListResponse(
        List<ContentModeratorEntityReviewListItemDto> items,
        int totalCount,
        int skip,
        int take,
        ContentModeratorEntityReviewCountsDto counts)
    {
        var nextSkip = skip + items.Count;
        var hasMore = nextSkip < totalCount;

        return new ContentModeratorEntityReviewListResponseDto
        {
            Items = items,
            TotalCount = totalCount,
            Skip = skip,
            Take = take,
            NextSkip = hasMore ? nextSkip : null,
            HasMore = hasMore,
            Counts = counts
        };
    }

    private async Task<ContentModeratorEntityReviewCountsDto> GetDestinationCountsAsync(CancellationToken cancellationToken)
    {
        return new ContentModeratorEntityReviewCountsDto
        {
            AllCount = await _db.Destinations.AsNoTracking().CountAsync(entity => entity.Status != DeletedStatus, cancellationToken),
            ActiveCount = await _db.Destinations.AsNoTracking().CountAsync(entity => entity.Status == PublishedStatus, cancellationToken),
            SuspendedCount = await _db.Destinations.AsNoTracking().CountAsync(entity => entity.Status == SuspendedStatus, cancellationToken),
            DeletedCount = await _db.Destinations.AsNoTracking().CountAsync(entity => entity.Status == DeletedStatus, cancellationToken)
        };
    }

    private async Task<ContentModeratorEntityReviewCountsDto> GetLocationCountsAsync(CancellationToken cancellationToken)
    {
        return new ContentModeratorEntityReviewCountsDto
        {
            AllCount = await _db.Locations.AsNoTracking().CountAsync(entity => entity.Status != DeletedStatus, cancellationToken),
            ActiveCount = await _db.Locations.AsNoTracking().CountAsync(entity => entity.Status == PublishedStatus, cancellationToken),
            SuspendedCount = await _db.Locations.AsNoTracking().CountAsync(entity => entity.Status == SuspendedStatus, cancellationToken),
            DeletedCount = await _db.Locations.AsNoTracking().CountAsync(entity => entity.Status == DeletedStatus, cancellationToken)
        };
    }

    private async Task<ContentModeratorEntityReviewCountsDto> GetPlaceCountsAsync(CancellationToken cancellationToken)
    {
        return new ContentModeratorEntityReviewCountsDto
        {
            AllCount = await _db.Places.AsNoTracking().CountAsync(entity => entity.Status != DeletedStatus, cancellationToken),
            ActiveCount = await _db.Places.AsNoTracking().CountAsync(entity => entity.Status == PublishedStatus, cancellationToken),
            SuspendedCount = await _db.Places.AsNoTracking().CountAsync(entity => entity.Status == SuspendedStatus, cancellationToken),
            DeletedCount = await _db.Places.AsNoTracking().CountAsync(entity => entity.Status == DeletedStatus, cancellationToken)
        };
    }

    private async Task<ContentModeratorEntityReviewCountsDto> GetEventCountsAsync(CancellationToken cancellationToken)
    {
        return new ContentModeratorEntityReviewCountsDto
        {
            AllCount = await _db.Events.AsNoTracking().CountAsync(entity => entity.Status != DeletedStatus, cancellationToken),
            ActiveCount = await _db.Events.AsNoTracking().CountAsync(entity => entity.Status == PublishedStatus, cancellationToken),
            SuspendedCount = await _db.Events.AsNoTracking().CountAsync(entity => entity.Status == SuspendedStatus, cancellationToken),
            DeletedCount = await _db.Events.AsNoTracking().CountAsync(entity => entity.Status == DeletedStatus, cancellationToken)
        };
    }

    private static IQueryable<Destination> ApplyDestinationSearch(IQueryable<Destination> query, string? search)
    {
        var term = NormalizeSearch(search);
        if (term is null)
            return query;

        return query.Where(entity =>
            entity.Name.ToLower().Contains(term) ||
            (entity.Summary ?? string.Empty).ToLower().Contains(term) ||
            entity.Description.ToLower().Contains(term) ||
            entity.Region.Name.ToLower().Contains(term) ||
            entity.Region.Territory.Name.ToLower().Contains(term) ||
            entity.DestinationType.Name.ToLower().Contains(term));
    }

    private static IQueryable<Location> ApplyLocationSearch(IQueryable<Location> query, string? search)
    {
        var term = NormalizeSearch(search);
        if (term is null)
            return query;

        return query.Where(entity =>
            entity.Name.ToLower().Contains(term) ||
            (entity.Summary ?? string.Empty).ToLower().Contains(term) ||
            entity.Description.ToLower().Contains(term) ||
            entity.Destination.Name.ToLower().Contains(term) ||
            entity.Destination.Region.Name.ToLower().Contains(term) ||
            entity.Destination.Region.Territory.Name.ToLower().Contains(term) ||
            entity.LocationType.Name.ToLower().Contains(term));
    }

    private static IQueryable<Place> ApplyPlaceSearch(IQueryable<Place> query, string? search)
    {
        var term = NormalizeSearch(search);
        if (term is null)
            return query;

        return query.Where(entity =>
            entity.Name.ToLower().Contains(term) ||
            (entity.Subtitle ?? string.Empty).ToLower().Contains(term) ||
            entity.Description.ToLower().Contains(term) ||
            (entity.Address ?? string.Empty).ToLower().Contains(term) ||
            entity.Location.Name.ToLower().Contains(term) ||
            entity.Location.Destination.Name.ToLower().Contains(term) ||
            entity.Location.Destination.Region.Name.ToLower().Contains(term) ||
            entity.Location.Destination.Region.Territory.Name.ToLower().Contains(term) ||
            entity.PlaceType.Name.ToLower().Contains(term));
    }

    private static IQueryable<Event> ApplyEventSearch(IQueryable<Event> query, string? search)
    {
        var term = NormalizeSearch(search);
        if (term is null)
            return query;

        return query.Where(entity =>
            entity.Name.ToLower().Contains(term) ||
            (entity.Summary ?? string.Empty).ToLower().Contains(term) ||
            entity.Description.ToLower().Contains(term) ||
            (entity.VenueName ?? string.Empty).ToLower().Contains(term) ||
            entity.Destination.Name.ToLower().Contains(term) ||
            entity.Destination.Region.Name.ToLower().Contains(term) ||
            entity.Destination.Region.Territory.Name.ToLower().Contains(term) ||
            ((entity.Location != null ? entity.Location.Name : string.Empty) ?? string.Empty).ToLower().Contains(term) ||
            ((entity.Place != null ? entity.Place.Name : string.Empty) ?? string.Empty).ToLower().Contains(term) ||
            entity.EventType.Name.ToLower().Contains(term));
    }

    private static IQueryable<Destination> ApplyDestinationStatusFilter(IQueryable<Destination> query, string? status)
    {
        return NormalizeStatusFilter(status) switch
        {
            "all" => query.Where(entity => entity.Status != DeletedStatus),
            "active" => query.Where(entity => entity.Status == PublishedStatus),
            "suspended" => query.Where(entity => entity.Status == SuspendedStatus),
            "deleted" => query.Where(entity => entity.Status == DeletedStatus),
            _ => throw new InvalidOperationException("Unsupported status filter.")
        };
    }

    private static IQueryable<Location> ApplyLocationStatusFilter(IQueryable<Location> query, string? status)
    {
        return NormalizeStatusFilter(status) switch
        {
            "all" => query.Where(entity => entity.Status != DeletedStatus),
            "active" => query.Where(entity => entity.Status == PublishedStatus),
            "suspended" => query.Where(entity => entity.Status == SuspendedStatus),
            "deleted" => query.Where(entity => entity.Status == DeletedStatus),
            _ => throw new InvalidOperationException("Unsupported status filter.")
        };
    }

    private static IQueryable<Place> ApplyPlaceStatusFilter(IQueryable<Place> query, string? status)
    {
        return NormalizeStatusFilter(status) switch
        {
            "all" => query.Where(entity => entity.Status != DeletedStatus),
            "active" => query.Where(entity => entity.Status == PublishedStatus),
            "suspended" => query.Where(entity => entity.Status == SuspendedStatus),
            "deleted" => query.Where(entity => entity.Status == DeletedStatus),
            _ => throw new InvalidOperationException("Unsupported status filter.")
        };
    }

    private static IQueryable<Event> ApplyEventStatusFilter(IQueryable<Event> query, string? status)
    {
        return NormalizeStatusFilter(status) switch
        {
            "all" => query.Where(entity => entity.Status != DeletedStatus),
            "active" => query.Where(entity => entity.Status == PublishedStatus),
            "suspended" => query.Where(entity => entity.Status == SuspendedStatus),
            "deleted" => query.Where(entity => entity.Status == DeletedStatus),
            _ => throw new InvalidOperationException("Unsupported status filter.")
        };
    }

    private static IQueryable<Destination> ApplyDestinationSort(IQueryable<Destination> query, string? sort)
    {
        return NormalizeSort(sort) switch
        {
            "oldest" => query.OrderBy(entity => entity.CreatedAt).ThenBy(entity => entity.Id),
            "nameasc" => query.OrderBy(entity => entity.Name).ThenByDescending(entity => entity.CreatedAt).ThenByDescending(entity => entity.Id),
            "namedesc" => query.OrderByDescending(entity => entity.Name).ThenByDescending(entity => entity.CreatedAt).ThenByDescending(entity => entity.Id),
            "status" => query.OrderBy(entity => entity.Status).ThenByDescending(entity => entity.CreatedAt).ThenByDescending(entity => entity.Id),
            _ => query.OrderByDescending(entity => entity.CreatedAt).ThenByDescending(entity => entity.Id)
        };
    }

    private static IQueryable<Location> ApplyLocationSort(IQueryable<Location> query, string? sort)
    {
        return NormalizeSort(sort) switch
        {
            "oldest" => query.OrderBy(entity => entity.CreatedAt).ThenBy(entity => entity.Id),
            "nameasc" => query.OrderBy(entity => entity.Name).ThenByDescending(entity => entity.CreatedAt).ThenByDescending(entity => entity.Id),
            "namedesc" => query.OrderByDescending(entity => entity.Name).ThenByDescending(entity => entity.CreatedAt).ThenByDescending(entity => entity.Id),
            "status" => query.OrderBy(entity => entity.Status).ThenByDescending(entity => entity.CreatedAt).ThenByDescending(entity => entity.Id),
            _ => query.OrderByDescending(entity => entity.CreatedAt).ThenByDescending(entity => entity.Id)
        };
    }

    private static IQueryable<Place> ApplyPlaceSort(IQueryable<Place> query, string? sort)
    {
        return NormalizeSort(sort) switch
        {
            "oldest" => query.OrderBy(entity => entity.CreatedAt).ThenBy(entity => entity.Id),
            "nameasc" => query.OrderBy(entity => entity.Name).ThenByDescending(entity => entity.CreatedAt).ThenByDescending(entity => entity.Id),
            "namedesc" => query.OrderByDescending(entity => entity.Name).ThenByDescending(entity => entity.CreatedAt).ThenByDescending(entity => entity.Id),
            "status" => query.OrderBy(entity => entity.Status).ThenByDescending(entity => entity.CreatedAt).ThenByDescending(entity => entity.Id),
            _ => query.OrderByDescending(entity => entity.CreatedAt).ThenByDescending(entity => entity.Id)
        };
    }

    private static IQueryable<Event> ApplyEventSort(IQueryable<Event> query, string? sort)
    {
        return NormalizeSort(sort) switch
        {
            "oldest" => query.OrderBy(entity => entity.CreatedAt).ThenBy(entity => entity.Id),
            "nameasc" => query.OrderBy(entity => entity.Name).ThenByDescending(entity => entity.CreatedAt).ThenByDescending(entity => entity.Id),
            "namedesc" => query.OrderByDescending(entity => entity.Name).ThenByDescending(entity => entity.CreatedAt).ThenByDescending(entity => entity.Id),
            "status" => query.OrderBy(entity => entity.Status).ThenByDescending(entity => entity.CreatedAt).ThenByDescending(entity => entity.Id),
            _ => query.OrderByDescending(entity => entity.CreatedAt).ThenByDescending(entity => entity.Id)
        };
    }

    private static ContentModeratorEntityReviewListItemDto MapDestinationListItem(Destination destination)
    {
        return new ContentModeratorEntityReviewListItemDto
        {
            EntityId = destination.Id,
            EntityType = DestinationEntityType,
            Name = destination.Name,
            Summary = destination.Summary,
            DescriptionPreview = BuildPreview(destination.Description),
            ThumbnailUrl = GetThumbnailUrl(destination.Images),
            Status = MapModerationStatus(destination.Status),
            RawStatus = destination.Status,
            IsPublished = IsPublished(destination.Status),
            IsSuspended = IsSuspended(destination.Status),
            IsDeleted = IsDeleted(destination.Status),
            CreatedAt = destination.CreatedAt,
            UpdatedAt = destination.UpdatedAt,
            PublishedAt = destination.PublishedAt,
            RegionId = destination.RegionId,
            RegionName = destination.Region.Name,
            TerritoryName = destination.Region.Territory.Name,
            TypeName = destination.DestinationType.Name,
            CreatedByUserId = destination.CreatedByUserId,
            PublicDetailRoute = BuildDestinationPublicDetailRoute(destination),
            CanOpenInUserApp = BuildDestinationPublicDetailRoute(destination) is not null,
            Permissions = BuildPermissions(destination.Status)
        };
    }

    private static ContentModeratorEntityReviewListItemDto MapLocationListItem(Location location)
    {
        return new ContentModeratorEntityReviewListItemDto
        {
            EntityId = location.Id,
            EntityType = LocationEntityType,
            Name = location.Name,
            Summary = location.Summary,
            DescriptionPreview = BuildPreview(location.Description),
            ThumbnailUrl = GetThumbnailUrl(location.Images),
            Status = MapModerationStatus(location.Status),
            RawStatus = location.Status,
            IsPublished = IsPublished(location.Status),
            IsSuspended = IsSuspended(location.Status),
            IsDeleted = IsDeleted(location.Status),
            CreatedAt = location.CreatedAt,
            UpdatedAt = location.UpdatedAt,
            PublishedAt = location.PublishedAt,
            RegionId = location.Destination.RegionId,
            RegionName = location.Destination.Region.Name,
            TerritoryName = location.Destination.Region.Territory.Name,
            DestinationId = location.DestinationId,
            DestinationName = location.Destination.Name,
            TypeName = location.LocationType.Name,
            CreatedByUserId = location.CreatedByUserId,
            PublicDetailRoute = BuildLocationPublicDetailRoute(location),
            CanOpenInUserApp = BuildLocationPublicDetailRoute(location) is not null,
            Permissions = BuildPermissions(location.Status)
        };
    }

    private static ContentModeratorEntityReviewListItemDto MapPlaceListItem(Place place)
    {
        return new ContentModeratorEntityReviewListItemDto
        {
            EntityId = place.Id,
            EntityType = PlaceEntityType,
            Name = place.Name,
            Summary = place.Subtitle,
            DescriptionPreview = BuildPreview(place.Description),
            ThumbnailUrl = GetThumbnailUrl(place.Images),
            Status = MapModerationStatus(place.Status),
            RawStatus = place.Status,
            IsPublished = IsPublished(place.Status),
            IsSuspended = IsSuspended(place.Status),
            IsDeleted = IsDeleted(place.Status),
            CreatedAt = place.CreatedAt,
            UpdatedAt = place.UpdatedAt,
            PublishedAt = place.PublishedAt,
            RegionId = place.Location.Destination.RegionId,
            RegionName = place.Location.Destination.Region.Name,
            TerritoryName = place.Location.Destination.Region.Territory.Name,
            DestinationId = place.Location.DestinationId,
            DestinationName = place.Location.Destination.Name,
            LocationId = place.LocationId,
            LocationName = place.Location.Name,
            TypeName = place.PlaceType.Name,
            CreatedByUserId = place.CreatedByUserId,
            PublicDetailRoute = BuildPlacePublicDetailRoute(place),
            CanOpenInUserApp = BuildPlacePublicDetailRoute(place) is not null,
            Permissions = BuildPermissions(place.Status)
        };
    }

    private static ContentModeratorEntityReviewListItemDto MapEventListItem(Event @event)
    {
        return new ContentModeratorEntityReviewListItemDto
        {
            EntityId = @event.Id,
            EntityType = EventEntityType,
            Name = @event.Name,
            Summary = @event.Summary,
            DescriptionPreview = BuildPreview(@event.Description),
            ThumbnailUrl = GetThumbnailUrl(@event.Images),
            Status = MapModerationStatus(@event.Status),
            RawStatus = @event.Status,
            IsPublished = IsPublished(@event.Status),
            IsSuspended = IsSuspended(@event.Status),
            IsDeleted = IsDeleted(@event.Status),
            CreatedAt = @event.CreatedAt,
            UpdatedAt = @event.UpdatedAt,
            PublishedAt = @event.PublishedAt,
            RegionId = @event.Destination.RegionId,
            RegionName = @event.Destination.Region.Name,
            TerritoryName = @event.Destination.Region.Territory.Name,
            DestinationId = @event.DestinationId,
            DestinationName = @event.Destination.Name,
            LocationId = @event.LocationId,
            LocationName = @event.Location?.Name,
            PlaceId = @event.PlaceId,
            PlaceName = @event.Place?.Name,
            TypeName = @event.EventType.Name,
            CreatedByUserId = @event.CreatedByUserId,
            StartsAt = @event.StartDate,
            EndsAt = @event.EndDate,
            PublicDetailRoute = BuildEventPublicDetailRoute(@event),
            CanOpenInUserApp = BuildEventPublicDetailRoute(@event) is not null,
            Permissions = BuildPermissions(@event.Status)
        };
    }

    private static ContentModeratorEntityReviewDetailDto MapDestinationDetail(Destination destination)
    {
        var item = MapDestinationListItem(destination);
        return new ContentModeratorEntityReviewDetailDto
        {
            EntityId = item.EntityId,
            EntityType = item.EntityType,
            Name = item.Name,
            Summary = item.Summary,
            DescriptionPreview = item.DescriptionPreview,
            ThumbnailUrl = item.ThumbnailUrl,
            Status = item.Status,
            RawStatus = item.RawStatus,
            IsPublished = item.IsPublished,
            IsSuspended = item.IsSuspended,
            IsDeleted = item.IsDeleted,
            CreatedAt = item.CreatedAt,
            UpdatedAt = item.UpdatedAt,
            PublishedAt = item.PublishedAt,
            RegionId = item.RegionId,
            RegionName = item.RegionName,
            TerritoryName = item.TerritoryName,
            TypeName = item.TypeName,
            CreatedByUserId = item.CreatedByUserId,
            PublicDetailRoute = item.PublicDetailRoute,
            CanOpenInUserApp = item.CanOpenInUserApp,
            Permissions = item.Permissions,
            Description = destination.Description,
            Images = BuildImageDtos(destination.Images),
            Tags = destination.DestinationTags
                .Select(tag => new ContentModeratorEntityTagDto
                {
                    Id = tag.Tag.Id,
                    Name = tag.Tag.Name,
                    Slug = tag.Tag.Slug
                })
                .OrderBy(tag => tag.Name)
                .ToList(),
            Metadata = new ContentModeratorEntityMetadataDto
            {
                Latitude = destination.Latitude,
                Longitude = destination.Longitude,
                BoundaryNote = destination.BoundaryNote,
                BoundaryShapeType = destination.BoundaryShapeType
            }
        };
    }

    private static ContentModeratorEntityReviewDetailDto MapLocationDetail(Location location)
    {
        var item = MapLocationListItem(location);
        return new ContentModeratorEntityReviewDetailDto
        {
            EntityId = item.EntityId,
            EntityType = item.EntityType,
            Name = item.Name,
            Summary = item.Summary,
            DescriptionPreview = item.DescriptionPreview,
            ThumbnailUrl = item.ThumbnailUrl,
            Status = item.Status,
            RawStatus = item.RawStatus,
            IsPublished = item.IsPublished,
            IsSuspended = item.IsSuspended,
            IsDeleted = item.IsDeleted,
            CreatedAt = item.CreatedAt,
            UpdatedAt = item.UpdatedAt,
            PublishedAt = item.PublishedAt,
            RegionId = item.RegionId,
            RegionName = item.RegionName,
            TerritoryName = item.TerritoryName,
            DestinationId = item.DestinationId,
            DestinationName = item.DestinationName,
            TypeName = item.TypeName,
            CreatedByUserId = item.CreatedByUserId,
            PublicDetailRoute = item.PublicDetailRoute,
            CanOpenInUserApp = item.CanOpenInUserApp,
            Permissions = item.Permissions,
            Description = location.Description,
            Images = BuildImageDtos(location.Images),
            Tags = location.LocationTags
                .Select(tag => new ContentModeratorEntityTagDto
                {
                    Id = tag.Tag.Id,
                    Name = tag.Tag.Name,
                    Slug = tag.Tag.Slug
                })
                .OrderBy(tag => tag.Name)
                .ToList(),
            Metadata = new ContentModeratorEntityMetadataDto
            {
                Latitude = location.Latitude,
                Longitude = location.Longitude,
                BoundaryNote = location.BoundaryNote,
                BoundaryShapeType = location.BoundaryShapeType
            }
        };
    }

    private static ContentModeratorEntityReviewDetailDto MapPlaceDetail(Place place)
    {
        var item = MapPlaceListItem(place);
        return new ContentModeratorEntityReviewDetailDto
        {
            EntityId = item.EntityId,
            EntityType = item.EntityType,
            Name = item.Name,
            Summary = item.Summary,
            DescriptionPreview = item.DescriptionPreview,
            ThumbnailUrl = item.ThumbnailUrl,
            Status = item.Status,
            RawStatus = item.RawStatus,
            IsPublished = item.IsPublished,
            IsSuspended = item.IsSuspended,
            IsDeleted = item.IsDeleted,
            CreatedAt = item.CreatedAt,
            UpdatedAt = item.UpdatedAt,
            PublishedAt = item.PublishedAt,
            RegionId = item.RegionId,
            RegionName = item.RegionName,
            TerritoryName = item.TerritoryName,
            DestinationId = item.DestinationId,
            DestinationName = item.DestinationName,
            LocationId = item.LocationId,
            LocationName = item.LocationName,
            TypeName = item.TypeName,
            CreatedByUserId = item.CreatedByUserId,
            PublicDetailRoute = item.PublicDetailRoute,
            CanOpenInUserApp = item.CanOpenInUserApp,
            Permissions = item.Permissions,
            Description = place.Description,
            Images = BuildImageDtos(place.Images),
            Tags = place.PlaceTags
                .Select(tag => new ContentModeratorEntityTagDto
                {
                    Id = tag.Tag.Id,
                    Name = tag.Tag.Name,
                    Slug = tag.Tag.Slug
                })
                .OrderBy(tag => tag.Name)
                .ToList(),
            Metadata = new ContentModeratorEntityMetadataDto
            {
                Latitude = place.Latitude,
                Longitude = place.Longitude,
                Address = place.Address,
                ContactEmail = place.ContactEmail,
                PhoneNumber = place.Number,
                Website = place.Website
            }
        };
    }

    private static ContentModeratorEntityReviewDetailDto MapEventDetail(Event @event)
    {
        var item = MapEventListItem(@event);
        return new ContentModeratorEntityReviewDetailDto
        {
            EntityId = item.EntityId,
            EntityType = item.EntityType,
            Name = item.Name,
            Summary = item.Summary,
            DescriptionPreview = item.DescriptionPreview,
            ThumbnailUrl = item.ThumbnailUrl,
            Status = item.Status,
            RawStatus = item.RawStatus,
            IsPublished = item.IsPublished,
            IsSuspended = item.IsSuspended,
            IsDeleted = item.IsDeleted,
            CreatedAt = item.CreatedAt,
            UpdatedAt = item.UpdatedAt,
            PublishedAt = item.PublishedAt,
            RegionId = item.RegionId,
            RegionName = item.RegionName,
            TerritoryName = item.TerritoryName,
            DestinationId = item.DestinationId,
            DestinationName = item.DestinationName,
            LocationId = item.LocationId,
            LocationName = item.LocationName,
            PlaceId = item.PlaceId,
            PlaceName = item.PlaceName,
            TypeName = item.TypeName,
            CreatedByUserId = item.CreatedByUserId,
            StartsAt = item.StartsAt,
            EndsAt = item.EndsAt,
            PublicDetailRoute = item.PublicDetailRoute,
            CanOpenInUserApp = item.CanOpenInUserApp,
            Permissions = item.Permissions,
            Description = @event.Description,
            Images = BuildImageDtos(@event.Images),
            Tags = @event.EventTags
                .Select(tag => new ContentModeratorEntityTagDto
                {
                    Id = tag.Tag.Id,
                    Name = tag.Tag.Name,
                    Slug = tag.Tag.Slug
                })
                .OrderBy(tag => tag.Name)
                .ToList(),
            Metadata = new ContentModeratorEntityMetadataDto
            {
                ParentEntityType = @event.ParentEntityType,
                VenueName = @event.VenueName,
                TicketPrice = @event.TicketPrice,
                IsFree = @event.IsFree,
                IsAllDay = @event.IsAllDay,
                IsRecurring = @event.IsRecurring,
                ContactEmail = @event.ContactEmail
            }
        };
    }

    private static List<ContentModeratorEntityImageDto> BuildImageDtos(IEnumerable<EntityImage> images)
    {
        return images
            .OrderBy(ImageSortPriority)
            .ThenBy(image => image.SortOrder)
            .ThenBy(image => image.CreatedAt)
            .ThenBy(image => image.Id)
            .Select(image => new ContentModeratorEntityImageDto
            {
                Id = image.Id,
                Url = image.Url,
                AltText = image.AltText,
                Caption = image.Caption,
                ImageRole = EntityImageRole.Normalize(image.ImageRole),
                IsMain = image.IsMain,
                SortOrder = image.SortOrder
            })
            .ToList();
    }

    private static string? GetThumbnailUrl(IEnumerable<EntityImage> images)
    {
        return images
            .OrderBy(ImageSortPriority)
            .ThenBy(image => image.SortOrder)
            .ThenBy(image => image.CreatedAt)
            .ThenBy(image => image.Id)
            .Select(image => image.Url)
            .FirstOrDefault();
    }

    private static int ImageSortPriority(EntityImage image)
    {
        var role = EntityImageRole.Normalize(image.ImageRole);
        return role switch
        {
            EntityImageRole.Card => 0,
            EntityImageRole.Cover => 1,
            _ when image.IsMain => 2,
            _ => 3
        };
    }

    private static ContentModeratorEntityReviewPermissionsDto BuildPermissions(string status)
    {
        return new ContentModeratorEntityReviewPermissionsDto
        {
            CanView = true,
            CanSuspend = !IsDeleted(status) && !IsSuspended(status),
            CanUnsuspend = IsSuspended(status),
            CanDelete = !IsDeleted(status),
            CanRestore = false
        };
    }

    private static string? BuildDestinationPublicDetailRoute(Destination destination) =>
        IsPublished(destination.Status)
            ? $"/destinations/{destination.Id}"
            : null;

    private static string? BuildLocationPublicDetailRoute(Location location) =>
        IsPublished(location.Status) && IsPublished(location.Destination.Status)
            ? $"/locations/{location.Id}"
            : null;

    private static string? BuildPlacePublicDetailRoute(Place place)
    {
        if (!IsPublished(place.Status) ||
            !IsPublished(place.Location.Status) ||
            !IsPublished(place.Location.Destination.Status))
        {
            return null;
        }

        return place.Stay is not null
            ? $"/stays/{place.Id}"
            : $"/places/{place.Id}";
    }

    private static string? BuildEventPublicDetailRoute(Event @event)
    {
        if (!IsPublished(@event.Status) ||
            !IsPublished(@event.Destination.Status) ||
            (@event.Location is not null && !IsPublished(@event.Location.Status)) ||
            (@event.Place is not null && !IsPublished(@event.Place.Status)))
        {
            return null;
        }

        return $"/events/{@event.Id}";
    }

    private static string MapModerationStatus(string status)
    {
        return status switch
        {
            PublishedStatus => "Active",
            SuspendedStatus => "Suspended",
            DeletedStatus => "Deleted",
            _ => status
        };
    }

    private static bool IsPublished(string status) =>
        string.Equals(status, PublishedStatus, StringComparison.Ordinal);

    private static bool IsSuspended(string status) =>
        string.Equals(status, SuspendedStatus, StringComparison.Ordinal);

    private static bool IsDeleted(string status) =>
        string.Equals(status, DeletedStatus, StringComparison.Ordinal);

    private static void EnsureCanSuspend(string status)
    {
        if (IsDeleted(status))
            throw new InvalidOperationException("Deleted entities cannot be suspended.");

        if (IsSuspended(status))
            throw new InvalidOperationException("Entity is already suspended.");
    }

    private static void EnsureCanUnsuspend(string status)
    {
        if (IsDeleted(status))
            throw new InvalidOperationException("Deleted entities cannot be restored from suspension.");

        if (!IsSuspended(status))
            throw new InvalidOperationException("Entity is not suspended.");
    }

    private static void EnsureCanDelete(string status)
    {
        if (IsDeleted(status))
            throw new InvalidOperationException("Entity is already deleted.");
    }

    private static string NormalizeEntityType(string? type)
    {
        var normalized = type?.Trim().ToLowerInvariant();

        return normalized switch
        {
            "destination" or "destinations" => DestinationEntityType,
            "location" or "locations" => LocationEntityType,
            "place" or "places" => PlaceEntityType,
            "event" or "events" => EventEntityType,
            _ => throw new InvalidOperationException("Unsupported entity type.")
        };
    }

    private static string ToAuditTargetType(string entityType)
    {
        return entityType switch
        {
            DestinationEntityType => "Destination",
            LocationEntityType => "Location",
            PlaceEntityType => "Place",
            EventEntityType => "Event",
            _ => throw new InvalidOperationException("Unsupported entity type.")
        };
    }

    private static string NormalizeStatusFilter(string? status)
    {
        var normalized = status?.Trim().ToLowerInvariant();

        return normalized switch
        {
            null or "" or "all" => "all",
            "active" or "published" => "active",
            "suspended" or "draft" => "suspended",
            "deleted" or "archived" => "deleted",
            _ => throw new InvalidOperationException("Unsupported status filter.")
        };
    }

    private static string NormalizeSort(string? sort)
    {
        var normalized = sort?.Trim().ToLowerInvariant();

        return normalized switch
        {
            null or "" or "newest" => "newest",
            "oldest" => "oldest",
            "nameasc" or "name_asc" or "name-asc" => "nameasc",
            "namedesc" or "name_desc" or "name-desc" => "namedesc",
            "status" => "status",
            _ => throw new InvalidOperationException("Unsupported sort option.")
        };
    }

    private static string? NormalizeSearch(string? search)
    {
        var trimmed = search?.Trim();
        return string.IsNullOrWhiteSpace(trimmed)
            ? null
            : trimmed.ToLowerInvariant();
    }

    private static (int Skip, int Take) NormalizePagination(int skip, int take)
    {
        if (skip < 0)
            throw new InvalidOperationException("Skip must be zero or greater.");

        var normalizedTake = take <= 0 ? DefaultTake : Math.Min(take, MaxTake);
        return (skip, normalizedTake);
    }

    private static string? BuildPreview(string? value)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
            return null;

        const int maxLength = 180;
        return trimmed.Length <= maxLength
            ? trimmed
            : $"{trimmed[..maxLength].TrimEnd()}...";
    }

    private static void ValidateModerationActionRequest(ContentModeratorModerationActionRequestDto? request)
    {
        if (request is null)
            return;

        if (request.Reason?.Length > 1000)
            throw new InvalidOperationException("Reason must be 1000 characters or fewer.");

        if (request.InternalNote?.Length > 2000)
            throw new InvalidOperationException("Internal note must be 2000 characters or fewer.");
    }

    private static string? NormalizeModerationText(string? value)
    {
        var normalized = value?.Trim();
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }
}
