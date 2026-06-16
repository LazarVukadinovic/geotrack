using System.Text.Json;
using System.Net;
using System.Text.RegularExpressions;
using Geotrack.Application.DTOs.Admin;
using Geotrack.Application.Interfaces.Service;
using Geotrack.Application.Security;
using Geotrack.Application.Services;
using Geotrack.Domain.Entities.Core;
using Geotrack.Domain.Entities.Reviews;
using Geotrack.Domain.Entities.Tags;
using Geotrack.Domain.Entities.Users;
using Microsoft.EntityFrameworkCore;

namespace Geotrack.Infrastructure.Services;

public class RegionalPlaceService : IRegionalPlaceService
{
    private const int MaxTake = 50;
    private const string PublishedStatus = "Published";
    private const string DraftStatus = "Draft";
    private const string ArchivedStatus = "Archived";
    private const string ManagedRegionalImagePrefix = "/media/uploads/regional/";
    private static string ExistingEditRoute(int placeId) => $"/places/edit/{placeId}";

    private readonly AppDbContext _db;
    private readonly RegionalScopeValidationService _scopeValidation;
    private readonly IContentHtmlSanitizer _htmlSanitizer;
    private readonly DestinationBoundaryResolver _destinationBoundaryResolver;

    public RegionalPlaceService(
        AppDbContext db,
        RegionalScopeValidationService scopeValidation,
        IContentHtmlSanitizer htmlSanitizer,
        DestinationBoundaryResolver destinationBoundaryResolver)
    {
        _db = db;
        _scopeValidation = scopeValidation;
        _htmlSanitizer = htmlSanitizer;
        _destinationBoundaryResolver = destinationBoundaryResolver;
    }

    public async Task<RegionalPlaceListResponseDto> GetPlacesAsync(
        int userId,
        int regionId,
        string? search,
        string? status,
        string? placeType,
        string? sort,
        int skip,
        int take)
    {
        await EnsureAssignedRegionAsync(userId, regionId);

        var normalizedSkip = NormalizeSkip(skip);
        var normalizedTake = NormalizeTake(take);
        var normalizedStatus = NormalizeStatus(status);
        var normalizedSort = NormalizeSort(sort);

        var query = _db.Places
            .AsNoTracking()
            .Where(place => place.Location.Destination.RegionId == regionId);

        if (normalizedStatus != "all")
        {
            var statusName = StatusFromToken(normalizedStatus);
            query = query.Where(place => place.Status == statusName);
        }

        query = ApplySearch(query, search);
        query = ApplyPlaceTypeFilter(query, placeType);
        query = ApplySort(query, normalizedSort);

        var totalCount = await query.CountAsync();
        var rows = await query
            .Skip(normalizedSkip)
            .Take(normalizedTake)
            .AsSplitQuery()
            .Select(place => new PlaceProjection
            {
                Id = place.Id,
                Name = place.Name,
                InternalReferenceId = place.InternalReferenceId,
                Subtitle = place.Subtitle,
                Description = place.Description,
                Status = place.Status,
                Address = place.Address,
                OpeningHours = place.OpeningHours,
                OperatingHoursEnabled = place.OperatingHoursEnabled,
                OperatingHoursJson = place.OperatingHoursJson,
                ContactEmail = place.ContactEmail,
                PhoneNumber = place.Number,
                Website = place.Website,
                AdmissionPriceRange = place.AdmissionPriceRange,
                BookingExternalLink = place.BookingExternalLink,
                BestTimeToVisit = place.BestTimeToVisit,
                VisitorTips = place.VisitorTips,
                AccessNotes = place.AccessNotes,
                AccessibilityNotes = place.AccessibilityNotes,
                PlaceTypeId = place.PlaceTypeId,
                PlaceTypeName = place.PlaceType.Name,
                IsOwnable = place.PlaceType.IsOwnable,
                Owners = place.UserPlaces
                    .OrderBy(userPlace => userPlace.User.DisplayName ?? userPlace.User.Username)
                    .ThenBy(userPlace => userPlace.User.Id)
                    .Select(userPlace => new RegionalPlaceOwnerDto
                    {
                        UserId = userPlace.UserId,
                        Username = userPlace.User.Username,
                        Email = userPlace.User.Email,
                        DisplayName = userPlace.User.DisplayName ?? userPlace.User.Username
                    })
                    .ToList(),
                StayTypeId = place.Stay != null ? place.Stay.StayTypeId : null,
                StayTypeName = place.Stay != null ? place.Stay.StayType.Name : null,
                LocationId = place.LocationId,
                LocationName = place.Location.Name,
                DestinationId = place.Location.DestinationId,
                DestinationName = place.Location.Destination.Name,
                RegionId = place.Location.Destination.RegionId,
                RegionName = place.Location.Destination.Region.Name,
                TerritoryName = place.Location.Destination.Region.Territory.Name,
                CreatedByUserId = place.CreatedByUserId,
                CreatedAt = place.CreatedAt,
                UpdatedAt = place.UpdatedAt,
                PublishedAt = place.PublishedAt,
                Latitude = place.Latitude,
                Longitude = place.Longitude,
                ImageUrl = place.Images
                    .Where(image => image.TargetType == EntityImageTargetType.Place)
                    .OrderBy(image => image.ImageRole == EntityImageRole.Card ? 0 : image.ImageRole == EntityImageRole.Cover ? 1 : 2)
                    .ThenBy(image => image.SortOrder)
                    .ThenBy(image => image.CreatedAt)
                    .ThenBy(image => image.Id)
                    .Select(image => image.Url)
                    .FirstOrDefault(),
                CardImageUrl = place.Images
                    .Where(image => image.TargetType == EntityImageTargetType.Place && image.ImageRole == EntityImageRole.Card)
                    .OrderBy(image => image.SortOrder)
                    .ThenBy(image => image.CreatedAt)
                    .ThenBy(image => image.Id)
                    .Select(image => image.Url)
                    .FirstOrDefault(),
                CoverImageUrl = place.Images
                    .Where(image => image.TargetType == EntityImageTargetType.Place && image.ImageRole == EntityImageRole.Cover)
                    .OrderBy(image => image.SortOrder)
                    .ThenBy(image => image.CreatedAt)
                    .ThenBy(image => image.Id)
                    .Select(image => image.Url)
                    .FirstOrDefault(),
                GalleryImageUrls = place.Images
                    .Where(image => image.TargetType == EntityImageTargetType.Place && image.ImageRole == EntityImageRole.Gallery)
                    .OrderBy(image => image.SortOrder)
                    .ThenBy(image => image.CreatedAt)
                    .ThenBy(image => image.Id)
                    .Select(image => image.Url)
                    .ToList(),
                Tags = place.PlaceTags
                    .Where(placeTag => placeTag.Tag.ForPlace)
                    .OrderBy(placeTag => placeTag.Tag.Name)
                    .Select(placeTag => new RegionalPlaceTagDto
                    {
                        Id = placeTag.TagId,
                        Name = placeTag.Tag.Name,
                        Slug = placeTag.Tag.Slug
                    })
                    .ToList()
            })
            .ToListAsync();

        return new RegionalPlaceListResponseDto
        {
            Items = rows.Select(MapListItem).ToList(),
            TotalCount = totalCount,
            Skip = normalizedSkip,
            Take = normalizedTake
        };
    }

    public async Task<RegionalPlaceDetailDto> GetPlaceAsync(int userId, int regionId, int placeId)
    {
        await EnsureAssignedRegionAsync(userId, regionId);

        var row = await _db.Places
            .AsNoTracking()
            .Where(place => place.Id == placeId && place.Location.Destination.RegionId == regionId)
            .AsSplitQuery()
            .Select(place => new PlaceProjection
            {
                Id = place.Id,
                Name = place.Name,
                InternalReferenceId = place.InternalReferenceId,
                Subtitle = place.Subtitle,
                Description = place.Description,
                Status = place.Status,
                Address = place.Address,
                OpeningHours = place.OpeningHours,
                OperatingHoursEnabled = place.OperatingHoursEnabled,
                OperatingHoursJson = place.OperatingHoursJson,
                ContactEmail = place.ContactEmail,
                PhoneNumber = place.Number,
                Website = place.Website,
                AdmissionPriceRange = place.AdmissionPriceRange,
                BookingExternalLink = place.BookingExternalLink,
                BestTimeToVisit = place.BestTimeToVisit,
                VisitorTips = place.VisitorTips,
                AccessNotes = place.AccessNotes,
                AccessibilityNotes = place.AccessibilityNotes,
                PlaceTypeId = place.PlaceTypeId,
                PlaceTypeName = place.PlaceType.Name,
                IsOwnable = place.PlaceType.IsOwnable,
                HasLinkedContribution = place.PlaceContributions.Any(),
                Owners = place.UserPlaces
                    .OrderBy(userPlace => userPlace.User.DisplayName ?? userPlace.User.Username)
                    .ThenBy(userPlace => userPlace.User.Id)
                    .Select(userPlace => new RegionalPlaceOwnerDto
                    {
                        UserId = userPlace.UserId,
                        Username = userPlace.User.Username,
                        Email = userPlace.User.Email,
                        DisplayName = userPlace.User.DisplayName ?? userPlace.User.Username
                    })
                    .ToList(),
                StayTypeId = place.Stay != null ? place.Stay.StayTypeId : null,
                StayTypeName = place.Stay != null ? place.Stay.StayType.Name : null,
                LocationId = place.LocationId,
                LocationName = place.Location.Name,
                DestinationId = place.Location.DestinationId,
                DestinationName = place.Location.Destination.Name,
                RegionId = place.Location.Destination.RegionId,
                RegionName = place.Location.Destination.Region.Name,
                TerritoryName = place.Location.Destination.Region.Territory.Name,
                CreatedByUserId = place.CreatedByUserId,
                CreatedAt = place.CreatedAt,
                UpdatedAt = place.UpdatedAt,
                PublishedAt = place.PublishedAt,
                Latitude = place.Latitude,
                Longitude = place.Longitude,
                ImageUrl = place.Images
                    .Where(image => image.TargetType == EntityImageTargetType.Place)
                    .OrderBy(image => image.ImageRole == EntityImageRole.Card ? 0 : image.ImageRole == EntityImageRole.Cover ? 1 : 2)
                    .ThenBy(image => image.SortOrder)
                    .ThenBy(image => image.CreatedAt)
                    .ThenBy(image => image.Id)
                    .Select(image => image.Url)
                    .FirstOrDefault(),
                CardImageUrl = place.Images
                    .Where(image => image.TargetType == EntityImageTargetType.Place && image.ImageRole == EntityImageRole.Card)
                    .OrderBy(image => image.SortOrder)
                    .ThenBy(image => image.CreatedAt)
                    .ThenBy(image => image.Id)
                    .Select(image => image.Url)
                    .FirstOrDefault(),
                CoverImageUrl = place.Images
                    .Where(image => image.TargetType == EntityImageTargetType.Place && image.ImageRole == EntityImageRole.Cover)
                    .OrderBy(image => image.SortOrder)
                    .ThenBy(image => image.CreatedAt)
                    .ThenBy(image => image.Id)
                    .Select(image => image.Url)
                    .FirstOrDefault(),
                GalleryImageUrls = place.Images
                    .Where(image => image.TargetType == EntityImageTargetType.Place && image.ImageRole == EntityImageRole.Gallery)
                    .OrderBy(image => image.SortOrder)
                    .ThenBy(image => image.CreatedAt)
                    .ThenBy(image => image.Id)
                    .Select(image => image.Url)
                    .ToList(),
                Tags = place.PlaceTags
                    .Where(placeTag => placeTag.Tag.ForPlace)
                    .OrderBy(placeTag => placeTag.Tag.Name)
                    .Select(placeTag => new RegionalPlaceTagDto
                    {
                        Id = placeTag.TagId,
                        Name = placeTag.Tag.Name,
                        Slug = placeTag.Tag.Slug
                    })
                    .ToList(),
                PlaceAmenities = place.PlaceAmenities
                    .OrderBy(placeAmenity => placeAmenity.Amenity.Name)
                    .Select(placeAmenity => new RegionalPlaceAmenityDto
                    {
                        Name = placeAmenity.Amenity.Name
                    })
                    .ToList(),
                StayAmenities = place.Stay == null
                    ? new List<RegionalPlaceAmenityDto>()
                    : place.Stay.StayAmenities
                        .OrderBy(stayAmenity => stayAmenity.StayAmenity.Name)
                        .Select(stayAmenity => new RegionalPlaceAmenityDto
                        {
                            Name = stayAmenity.StayAmenity.Name
                        })
                    .ToList()
            })
            .SingleOrDefaultAsync()
            ?? throw new KeyNotFoundException("Place not found.");

        var dto = MapDetail(row);
        dto.CanDelete = !await HasBlockingDependenciesAsync(row.Id);
        return dto;
    }

    public async Task<RegionalPlaceDetailDto> CreatePlaceAsync(
        int userId,
        RegionalPlaceCreateRequestDto request)
    {
        if (!string.IsNullOrWhiteSpace(request.InternalReferenceId))
            throw new InvalidOperationException("Internal reference id is generated by the server.");

        await EnsureAssignedRegionAsync(userId, request.RegionId);

        var coordinates = ResolveRequiredCoordinates(request.Latitude, request.Longitude);
        await _scopeValidation.EnsurePointAllowedAsync(
            userId,
            request.RegionId,
            "Place coordinates",
            coordinates.Latitude,
            coordinates.Longitude,
            "Create");

        var resolvedDestination = await _destinationBoundaryResolver.ResolveRequiredInRegionAsync(
            request.RegionId,
            coordinates.Latitude,
            coordinates.Longitude);

        var location = await _db.Locations
            .AsNoTracking()
            .Where(item =>
                item.Id == request.LocationId &&
                item.DestinationId == resolvedDestination.DestinationId &&
                item.Destination.RegionId == request.RegionId)
            .Select(item => new
            {
                item.Id,
                item.DestinationId
            })
            .SingleOrDefaultAsync()
            ?? throw new InvalidOperationException("Selected location must belong to the destination resolved from the place coordinates.");

        var placeType = await ResolvePlaceTypeAsync(request.PlaceTypeId);
        var isStayPlace = IsStayPlaceType(placeType.Name);

        if ((request.StayTypeId.HasValue || !string.IsNullOrWhiteSpace(request.StayTypeName)) && !isStayPlace)
            throw new InvalidOperationException("Stay type can only be set for accommodation places.");

        int? stayTypeId = null;
        if (isStayPlace)
        {
            if (request.StayTypeId.HasValue && request.StayTypeId.Value > 0)
                stayTypeId = await ResolveStayTypeIdAsync(request.StayTypeId);
            else if (!string.IsNullOrWhiteSpace(request.StayTypeName))
                stayTypeId = await ResolveStayTypeByNameAsync(request.StayTypeName);
            else
                stayTypeId = await ResolveStayTypeIdAsync(null);
        }

        var name = NormalizeRequiredText(request.Name, "Place name is required.", 200, "Place name is too long.");
        var summary = NormalizeRequiredText(request.Summary, "Summary is required.", 160, "Summary is too long.");
        var description = NormalizeRichDescription(request.Description, 2000, "Description is too long.");
        var status = NormalizeCreateStatus(request.Status);
        var tagIds = await ValidatePlaceTagIdsAsync(request.TagIds);
        var operatingHours = NormalizeOperatingHours(request.OperatingHoursEnabled, request.OperatingHours);
        var now = DateTime.UtcNow;

        Place? place = null;
        await _db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var transaction = await _db.Database.BeginTransactionAsync();

            place = new Place
            {
                Name = name,
                Subtitle = summary,
                Description = description,
                Status = status,
                PlaceTypeId = placeType.Id,
                LocationId = location.Id,
                Address = NormalizeOptionalText(request.StreetAddress, 500, "Street address is too long."),
                BestTimeToVisit = NormalizeOptionalText(request.BestTimeToVisit, 500, "Best time to visit is too long."),
                VisitorTips = NormalizeOptionalText(request.VisitorTips, 1000, "Visitor tips are too long."),
                AccessNotes = NormalizeOptionalText(request.AccessNotes, 1000, "Access notes are too long."),
                AccessibilityNotes = NormalizeOptionalText(request.AccessibilityNotes, 1000, "Accessibility notes are too long."),
                Website = NormalizeUrl(request.Website, "Website URL is invalid."),
                Number = NormalizePhone(request.ContactPhone),
                AdmissionPriceRange = NormalizeOptionalText(request.AdmissionPriceRange, 200, "Admission / price range is too long."),
                BookingExternalLink = isStayPlace
                    ? NormalizeUrl(request.BookingExternalLink, "Booking / external link is invalid.")
                    : null,
                OperatingHoursEnabled = operatingHours.Enabled,
                OperatingHoursJson = operatingHours.Json,
                OpeningHours = operatingHours.Enabled ? BuildOpeningHoursSummary(operatingHours.Hours) : null,
                Latitude = coordinates.Latitude,
                Longitude = coordinates.Longitude,
                CreatedByUserId = userId,
                PublishedAt = status == PublishedStatus ? now : null,
                CreatedAt = now,
                PlaceTags = tagIds
                    .Select(tagId => new PlaceTag { TagId = tagId })
                    .ToList()
            };

            if (stayTypeId.HasValue)
            {
                place.Stay = new Stay
                {
                    StayTypeId = stayTypeId.Value
                };
            }

            _db.Places.Add(place);
            await _db.SaveChangesAsync();

            _db.EntityImages.AddRange(AddRequiredCreateImages(request, place.Id, place.Name, now));

            place.InternalReferenceId = BuildInternalReferenceId(place.Id);
            await _db.SaveChangesAsync();
            await transaction.CommitAsync();
        });

        return await GetPlaceAsync(userId, request.RegionId, place!.Id);
    }

    public async Task<RegionalPlaceDetailDto> UpdatePlaceAsync(
        int userId,
        int regionId,
        int placeId,
        RegionalPlaceUpdateRequestDto request)
    {
        await EnsureAssignedRegionAsync(userId, regionId);

        var place = await _db.Places
            .Include(item => item.Location)
                .ThenInclude(location => location.Destination)
            .Include(item => item.Stay)
            .Include(item => item.PlaceTags)
            .Include(item => item.Images)
            .SingleOrDefaultAsync(item => item.Id == placeId && item.Location.Destination.RegionId == regionId)
            ?? throw new KeyNotFoundException("Place not found.");

        var requestedLocationId = request.LocationId ?? place.LocationId;
        var parentLocationChanged = request.LocationId.HasValue && request.LocationId.Value != place.LocationId;

        if (request.PlaceTypeId.HasValue &&
            !await _db.PlaceTypes.AnyAsync(item => item.Id == request.PlaceTypeId.Value))
        {
            throw new InvalidOperationException("Place type is invalid.");
        }

        string? effectivePlaceTypeName = null;
        if (request.PlaceTypeId.HasValue && request.PlaceTypeId.Value > 0)
        {
            effectivePlaceTypeName = await _db.PlaceTypes
                .AsNoTracking()
                .Where(pt => pt.Id == request.PlaceTypeId.Value)
                .Select(pt => pt.Name)
                .FirstOrDefaultAsync();
        }
        else
        {
            effectivePlaceTypeName = await _db.PlaceTypes
                .AsNoTracking()
                .Where(pt => pt.Id == place.PlaceTypeId)
                .Select(pt => pt.Name)
                .FirstOrDefaultAsync();
        }

        var becomingStay = IsStayPlaceType(effectivePlaceTypeName);

        int? resolvedStayTypeId = null;
        if (becomingStay)
        {
            if (request.StayTypeId.HasValue && request.StayTypeId.Value > 0)
            {
                if (!await _db.StayTypes.AnyAsync(item => item.Id == request.StayTypeId.Value))
                    throw new InvalidOperationException("Stay type is invalid.");

                resolvedStayTypeId = request.StayTypeId.Value;
            }
            else if (!string.IsNullOrWhiteSpace(request.StayTypeName))
            {
                resolvedStayTypeId = await ResolveStayTypeByNameAsync(request.StayTypeName);
            }
        }
        else if (request.StayTypeId.HasValue || !string.IsNullOrWhiteSpace(request.StayTypeName))
        {
            throw new InvalidOperationException("Stay type can only be set for accommodation places.");
        }

        if (resolvedStayTypeId.HasValue && place.Stay is not null)
            place.Stay.StayTypeId = resolvedStayTypeId.Value;

        if (becomingStay && place.Stay is null)
        {
            var stayTypeId = resolvedStayTypeId ?? await ResolveStayTypeIdAsync(null);
            place.Stay = new Stay { StayTypeId = stayTypeId };
        }
        else if (!becomingStay && place.Stay is not null)
        {
            _db.Stays.Remove(place.Stay);
            place.Stay = null;
        }

        ApplyUpdate(place, request);
        if (!becomingStay)
        {
            place.BookingExternalLink = null;
        }

        if (parentLocationChanged || request.Latitude.HasValue || request.Longitude.HasValue)
        {
            var coordinates = new PlaceCoordinates(place.Latitude, place.Longitude);
            await _scopeValidation.EnsurePointAllowedAsync(
                userId,
                regionId,
                "Place coordinates",
                coordinates.Latitude,
                coordinates.Longitude,
                "Update");

            var resolvedDestination = await _destinationBoundaryResolver.ResolveRequiredInRegionAsync(
                regionId,
                coordinates.Latitude,
                coordinates.Longitude);

            var locationExists = await _db.Locations
                .AsNoTracking()
                .AnyAsync(location =>
                    location.Id == requestedLocationId &&
                    location.DestinationId == resolvedDestination.DestinationId &&
                    location.Destination.RegionId == regionId);
            if (!locationExists)
                throw new InvalidOperationException("Selected location must belong to the destination resolved from the place coordinates.");

            place.LocationId = requestedLocationId;
        }

        if (request.OperatingHoursEnabled.HasValue || request.OperatingHours is not null)
        {
            var operatingHours = NormalizeOperatingHours(
                request.OperatingHoursEnabled ?? place.OperatingHoursEnabled,
                request.OperatingHours ?? DeserializeOperatingHours(place.OperatingHoursJson));
            place.OperatingHoursEnabled = operatingHours.Enabled;
            place.OperatingHoursJson = operatingHours.Json;
            place.OpeningHours = operatingHours.Enabled ? BuildOpeningHoursSummary(operatingHours.Hours) : null;
        }

        if (request.TagIds is not null)
        {
            var tagIds = await ValidatePlaceTagIdsAsync(request.TagIds);
            place.PlaceTags.Clear();
            foreach (var tagId in tagIds)
                place.PlaceTags.Add(new PlaceTag { PlaceId = place.Id, TagId = tagId });
        }

        var now = DateTime.UtcNow;
        ApplyImageUpdates(place, request, now);
        place.UpdatedAt = now;

        if (request.PlaceTypeId.HasValue && request.PlaceTypeId.Value > 0)
        {
            var linkedContribution = await _db.PlaceContributions
                .Where(c => c.PlaceId == place.Id && c.Status == PlaceContributionStatus.Approved)
                .FirstOrDefaultAsync();

            if (linkedContribution != null)
                linkedContribution.PlaceTypeId = request.PlaceTypeId.Value;
        }

        await _db.SaveChangesAsync();
        return await GetPlaceAsync(userId, regionId, placeId);
    }

    public async Task<RegionalPlaceDetailDto> AddOwnerAsync(
        int userId,
        int regionId,
        int placeId,
        RegionalPlaceAddOwnerRequestDto request)
    {
        await EnsureAssignedRegionAsync(userId, regionId);

        var place = await _db.Places
            .Include(item => item.PlaceType)
            .Include(item => item.Location)
                .ThenInclude(location => location.Destination)
            .Include(item => item.UserPlaces)
            .SingleOrDefaultAsync(item => item.Id == placeId && item.Location.Destination.RegionId == regionId)
            ?? throw new KeyNotFoundException("Place not found.");

        if (!place.PlaceType.IsOwnable)
            throw new InvalidOperationException("Only ownable place types can have owners assigned.");

        var identifier = NormalizeRequiredText(
            request.EmailOrUsername,
            "Owner email or username is required.",
            256,
            "Owner email or username is too long.");
        var normalizedIdentifier = identifier.ToLowerInvariant();

        var owner = await _db.Users
            .SingleOrDefaultAsync(candidate =>
                candidate.Email.ToLower() == normalizedIdentifier ||
                candidate.Username.ToLower() == normalizedIdentifier)
            ?? throw new KeyNotFoundException("User not found.");

        if (!place.UserPlaces.Any(userPlace => userPlace.UserId == owner.Id))
        {
            _db.UserPlaces.Add(new UserPlace
            {
                UserId = owner.Id,
                PlaceId = place.Id,
                CreatedAt = DateTime.UtcNow
            });

            place.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
        }

        return await GetPlaceAsync(userId, regionId, placeId);
    }

    public async Task DeletePlaceAsync(int userId, int regionId, int placeId)
    {
        await EnsureAssignedRegionAsync(userId, regionId);

        var place = await _db.Places
            .SingleOrDefaultAsync(item => item.Id == placeId && item.Location.Destination.RegionId == regionId)
            ?? throw new KeyNotFoundException("Place not found.");

        if (await HasBlockingDependenciesAsync(placeId))
        {
            throw new InvalidOperationException("This place cannot be deleted while it has related content.");
        }

        _db.Places.Remove(place);
        await _db.SaveChangesAsync();
    }

    public async Task<List<RegionalPlaceParentDestinationDto>> GetParentDestinationsAsync(
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
            .Select(destination => new RegionalPlaceParentDestinationDto
            {
                Id = destination.Id,
                Name = destination.Name,
                DisplayName = destination.Name,
                Status = destination.Status
            })
            .ToListAsync();
    }

    public async Task<List<RegionalPlaceParentLocationDto>> GetParentLocationsAsync(
        int userId,
        int regionId,
        int? destinationId,
        string? search,
        int take)
    {
        await EnsureAssignedRegionAsync(userId, regionId);

        var normalizedTake = Math.Clamp(take <= 0 ? MaxTake : take, 1, MaxTake);
        var query = _db.Locations
            .AsNoTracking()
            .Where(location => location.Destination.RegionId == regionId);

        if (destinationId.HasValue)
        {
            query = query.Where(location => location.DestinationId == destinationId.Value);
        }

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
            .Select(location => new RegionalPlaceParentLocationDto
            {
                Id = location.Id,
                Name = location.Name,
                DisplayName = location.Name,
                DestinationId = location.DestinationId,
                DestinationName = location.Destination.Name,
                Status = location.Status
            })
            .ToListAsync();
    }

    public async Task<List<RegionalPlaceTagDto>> GetPlaceTagsAsync(int userId, string? search)
    {
        await EnsureRegionalManagerRoleAsync(userId);

        var query = _db.Tags
            .AsNoTracking()
            .Where(tag => tag.ForPlace && (tag.TagType == null || tag.TagType.ForPlace));

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLowerInvariant();
            query = query.Where(tag =>
                tag.Name.ToLower().Contains(term) ||
                tag.Slug.ToLower().Contains(term));
        }

        return await query
            .OrderBy(tag => tag.TagType != null ? tag.TagType.FilterOrder : 1000)
            .ThenBy(tag => tag.TagType != null ? tag.TagType.Name : string.Empty)
            .ThenBy(tag => tag.Name)
            .Take(100)
            .Select(tag => new RegionalPlaceTagDto
            {
                Id = tag.Id,
                Name = tag.Name,
                Slug = tag.Slug,
                TagTypeId = tag.TagTypeId,
                TagTypeName = tag.TagType != null ? tag.TagType.Name : null,
                TagTypeSlug = tag.TagType != null ? tag.TagType.Slug : null,
                TagTypeLabel = tag.TagType != null ? tag.TagType.FilterLabel ?? tag.TagType.Name : null,
                TagTypeOrder = tag.TagType != null ? tag.TagType.FilterOrder : 1000
            })
            .ToListAsync();
    }

    public async Task<RegionalPlaceTagDto> CreatePlaceTagAsync(
        int userId,
        RegionalTagCreateRequestDto request)
    {
        await EnsureRegionalManagerRoleAsync(userId);

        var tag = await RegionalTagWriter.CreateScopedTagAsync(
            _db,
            request.Name,
            RegionalTagScope.Place);

        return new RegionalPlaceTagDto
        {
            Id = tag.Id,
            Name = tag.Name,
            Slug = tag.Slug,
            TagTypeId = tag.TagTypeId,
            TagTypeName = tag.TagType?.Name,
            TagTypeSlug = tag.TagType?.Slug,
            TagTypeLabel = tag.TagType?.FilterLabel ?? tag.TagType?.Name,
            TagTypeOrder = tag.TagType?.FilterOrder ?? 1000
        };
    }

    public async Task<List<RegionalPlaceTypeDto>> GetPlaceTypesAsync(int userId)
    {
        await EnsureRegionalManagerRoleAsync(userId);

        return await _db.PlaceTypes
            .AsNoTracking()
            .OrderBy(type => type.Name)
            .ThenBy(type => type.Id)
            .Select(type => new RegionalPlaceTypeDto
            {
                Id = type.Id,
                Name = type.Name,
                Description = type.Description,
                IsOwnable = type.IsOwnable
            })
            .ToListAsync();
    }

    public async Task<List<RegionalStayTypeDto>> GetStayTypesAsync(int userId)
    {
        await EnsureRegionalManagerRoleAsync(userId);

        return await _db.StayTypes
            .AsNoTracking()
            .OrderBy(type => type.Name == "Hotel" ? 0 : 1)
            .ThenBy(type => type.Name)
            .Select(type => new RegionalStayTypeDto
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

    private async Task<bool> HasBlockingDependenciesAsync(int placeId)
    {
        if (await _db.Events.AsNoTracking().AnyAsync(@event => @event.PlaceId == placeId))
            return true;

        if (await _db.PlaceReviews.AsNoTracking().AnyAsync(review => review.PlaceId == placeId))
            return true;

        if (await _db.ReviewReports.AsNoTracking().AnyAsync(report =>
                report.TargetType == ReviewReportTargetType.Place && report.EntityId == placeId ||
                report.PlaceReview != null && report.PlaceReview.PlaceId == placeId))
            return true;

        if (await _db.PlaceContributions.AsNoTracking().AnyAsync(contribution => contribution.PlaceId == placeId))
            return true;

        if (await _db.UserPlaces.AsNoTracking().AnyAsync(owner => owner.PlaceId == placeId))
            return true;

        if (await _db.CollectionItems.AsNoTracking().AnyAsync(item => item.PlaceId == placeId))
            return true;

        if (await _db.Favorites.AsNoTracking().AnyAsync(item =>
                item.EntityId == placeId &&
                item.EntityType.ToLower() == "place"))
            return true;

        return await _db.AnalyticsEvents.AsNoTracking().AnyAsync(item =>
            item.EntityId == placeId &&
            item.EntityType != null &&
            (item.EntityType.ToLower() == "place" ||
                item.EntityType.ToLower() == "stay" ||
                item.EntityType.ToLower() == "hotel"));
    }

    private static IQueryable<Place> ApplySearch(IQueryable<Place> query, string? search)
    {
        if (string.IsNullOrWhiteSpace(search))
            return query;

        var term = search.Trim().ToLowerInvariant();
        return query.Where(place =>
            place.Name.ToLower().Contains(term) ||
            place.Description.ToLower().Contains(term) ||
            (place.InternalReferenceId != null && place.InternalReferenceId.ToLower().Contains(term)) ||
            place.Status.ToLower().Contains(term) ||
            place.Subtitle != null && place.Subtitle.ToLower().Contains(term) ||
            place.Address != null && place.Address.ToLower().Contains(term) ||
            place.Website != null && place.Website.ToLower().Contains(term) ||
            place.PlaceType.Name.ToLower().Contains(term) ||
            place.Location.Name.ToLower().Contains(term) ||
            place.Location.Destination.Name.ToLower().Contains(term) ||
            place.Location.Destination.Region.Name.ToLower().Contains(term) ||
            place.Location.Destination.Region.Territory.Name.ToLower().Contains(term) ||
            place.Stay != null && place.Stay.StayType.Name.ToLower().Contains(term) ||
            place.Stay != null && place.Stay.StayType.Slug.ToLower().Contains(term) ||
            place.PlaceTags.Any(placeTag =>
                placeTag.Tag.Name.ToLower().Contains(term) ||
                placeTag.Tag.Slug.ToLower().Contains(term)));
    }

    private static IQueryable<Place> ApplyPlaceTypeFilter(IQueryable<Place> query, string? placeType)
    {
        if (string.IsNullOrWhiteSpace(placeType) ||
            string.Equals(placeType.Trim(), "all", StringComparison.OrdinalIgnoreCase))
        {
            return query;
        }

        var term = placeType.Trim().ToLowerInvariant();
        return query.Where(place =>
            place.PlaceType.Name.ToLower() == term ||
            place.Stay != null && place.Stay.StayType.Name.ToLower() == term ||
            place.Stay != null && place.Stay.StayType.Slug.ToLower() == term);
    }

    private static IQueryable<Place> ApplySort(IQueryable<Place> query, string sort)
    {
        return sort switch
        {
            "oldest" => query
                .OrderBy(place => place.UpdatedAt ?? place.CreatedAt)
                .ThenBy(place => place.Id),
            "nameasc" => query
                .OrderBy(place => place.Name)
                .ThenBy(place => place.Id),
            "namedesc" => query
                .OrderByDescending(place => place.Name)
                .ThenByDescending(place => place.Id),
            _ => query
                .OrderByDescending(place => place.UpdatedAt ?? place.CreatedAt)
                .ThenByDescending(place => place.Id),
        };
    }

    private void ApplyUpdate(Place place, RegionalPlaceUpdateRequestDto request)
    {
        if (request.Name is not null)
        {
            var name = request.Name.Trim();
            if (name.Length == 0)
                throw new InvalidOperationException("Place name is required.");
            if (name.Length > 200)
                throw new InvalidOperationException("Place name is too long.");

            place.Name = name;
        }

        if (request.Summary is not null || request.Subtitle is not null)
        {
            var subtitle = (request.Summary ?? request.Subtitle ?? string.Empty).Trim();
            if (subtitle.Length > 500)
                throw new InvalidOperationException("Subtitle is too long.");

            place.Subtitle = subtitle.Length == 0 ? null : subtitle;
        }

        if (request.Description is not null)
        {
            place.Description = NormalizeRichDescription(request.Description, 2000, "Description is too long.");
        }

        if (request.Address is not null)
        {
            var address = request.Address.Trim();
            if (address.Length > 500)
                throw new InvalidOperationException("Address is too long.");

            place.Address = address.Length == 0 ? null : address;
        }

        if (request.OpeningHours is not null)
        {
            var openingHours = request.OpeningHours.Trim();
            if (openingHours.Length > 200)
                throw new InvalidOperationException("Opening hours are too long.");

            place.OpeningHours = openingHours.Length == 0 ? null : openingHours;
        }

        if (request.ContactEmail is not null)
        {
            var contactEmail = request.ContactEmail.Trim();
            if (contactEmail.Length > 200)
                throw new InvalidOperationException("Contact email is too long.");

            place.ContactEmail = contactEmail.Length == 0 ? null : contactEmail;
        }

        if (request.PhoneNumber is not null)
        {
            var phoneNumber = request.PhoneNumber.Trim();
            if (phoneNumber.Length > 50)
                throw new InvalidOperationException("Phone number is too long.");

            place.Number = phoneNumber.Length == 0 ? null : phoneNumber;
        }

        if (request.Website is not null)
        {
            place.Website = NormalizeUrl(request.Website, "Website URL is invalid.");
        }

        if (request.AdmissionPriceRange is not null)
        {
            place.AdmissionPriceRange = NormalizeOptionalText(
                request.AdmissionPriceRange,
                200,
                "Admission / price range is too long.");
        }

        if (request.BookingExternalLink is not null)
        {
            place.BookingExternalLink = NormalizeUrl(
                request.BookingExternalLink,
                "Booking / external link is invalid.");
        }

        if (request.BestTimeToVisit is not null)
        {
            place.BestTimeToVisit = NormalizeOptionalText(
                request.BestTimeToVisit,
                500,
                "Best time to visit is too long.");
        }

        if (request.VisitorTips is not null)
        {
            place.VisitorTips = NormalizeOptionalText(
                request.VisitorTips,
                1000,
                "Visitor tips are too long.");
        }

        if (request.AccessNotes is not null)
        {
            place.AccessNotes = NormalizeOptionalText(
                request.AccessNotes,
                1000,
                "Access notes are too long.");
        }

        if (request.AccessibilityNotes is not null)
        {
            place.AccessibilityNotes = NormalizeOptionalText(
                request.AccessibilityNotes,
                1000,
                "Accessibility notes are too long.");
        }

        if (request.PlaceTypeId.HasValue)
        {
            if (request.PlaceTypeId.Value <= 0)
                throw new InvalidOperationException("Place type is invalid.");

            place.PlaceTypeId = request.PlaceTypeId.Value;
        }

        if (request.Latitude.HasValue)
        {
            if (request.Latitude.Value is < -90 or > 90)
                throw new InvalidOperationException("Latitude is invalid.");

            place.Latitude = request.Latitude.Value;
        }

        if (request.Longitude.HasValue)
        {
            if (request.Longitude.Value is < -180 or > 180)
                throw new InvalidOperationException("Longitude is invalid.");

            place.Longitude = request.Longitude.Value;
        }
    }

    private async Task<PlaceType> ResolvePlaceTypeAsync(int? requestedPlaceTypeId)
    {
        if (!requestedPlaceTypeId.HasValue || requestedPlaceTypeId.Value <= 0)
            throw new InvalidOperationException("Place type is required.");

        return await _db.PlaceTypes
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == requestedPlaceTypeId.Value)
            ?? throw new InvalidOperationException("Place type is invalid.");
    }

    private async Task<int> ResolveStayTypeIdAsync(int? requestedStayTypeId)
    {
        if (requestedStayTypeId.HasValue)
        {
            if (requestedStayTypeId.Value <= 0)
                throw new InvalidOperationException("Stay type is invalid.");

            var exists = await _db.StayTypes
                .AsNoTracking()
                .AnyAsync(item => item.Id == requestedStayTypeId.Value);

            if (!exists)
                throw new InvalidOperationException("Stay type is invalid.");

            return requestedStayTypeId.Value;
        }

        var defaultStayTypeId = await _db.StayTypes
            .AsNoTracking()
            .OrderBy(item => item.Name == "Hotel" ? 0 : 1)
            .ThenBy(item => item.Id)
            .Select(item => item.Id)
            .FirstOrDefaultAsync();

        if (defaultStayTypeId <= 0)
            throw new InvalidOperationException("A stay type must exist before accommodation places can be created.");

        return defaultStayTypeId;
    }

    private async Task<int> ResolveStayTypeByNameAsync(string name)
    {
        var normalized = name.Trim();
        if (string.IsNullOrEmpty(normalized))
            return await ResolveStayTypeIdAsync(null);

        var existing = await _db.StayTypes
            .Where(st => st.Name.ToLower() == normalized.ToLower())
            .Select(st => st.Id)
            .FirstOrDefaultAsync();

        if (existing > 0)
            return existing;

        var newType = new StayType
        {
            Name = normalized,
            Slug = normalized.ToLowerInvariant().Replace(" ", "-"),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        await _db.StayTypes.AddAsync(newType);
        await _db.SaveChangesAsync();
        return newType.Id;
    }

    private async Task<List<int>> ValidatePlaceTagIdsAsync(IEnumerable<int>? requestedTagIds)
    {
        var tagIds = (requestedTagIds ?? Array.Empty<int>())
            .Where(id => id > 0)
            .Distinct()
            .ToList();

        if (tagIds.Count == 0)
            return tagIds;

        return await _db.Tags
            .AsNoTracking()
            .Where(tag => tagIds.Contains(tag.Id) && tag.ForPlace)
            .Select(tag => tag.Id)
            .ToListAsync();
    }

    private static PlaceCoordinates ResolveRequiredCoordinates(double? latitude, double? longitude)
    {
        if (!latitude.HasValue || !longitude.HasValue)
            throw new InvalidOperationException("Latitude and longitude are required.");

        ValidateLatitude(latitude.Value);
        ValidateLongitude(longitude.Value);

        if (latitude.Value == 0d && longitude.Value == 0d)
            throw new InvalidOperationException("Latitude and longitude are invalid.");

        return new PlaceCoordinates(latitude.Value, longitude.Value);
    }

    private static OperatingHoursResult NormalizeOperatingHours(
        bool enabled,
        IReadOnlyCollection<RegionalPlaceOperatingHourDto>? hours)
    {
        if (!enabled)
            return new OperatingHoursResult(false, null, []);

        var normalizedHours = new List<RegionalPlaceOperatingHourDto>();
        var seenDays = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var input in hours ?? Array.Empty<RegionalPlaceOperatingHourDto>())
        {
            var day = NormalizeDayOfWeek(input.DayOfWeek);
            if (!seenDays.Add(day))
                throw new InvalidOperationException("Operating hours contain duplicate days.");

            if (!input.IsOpen)
            {
                normalizedHours.Add(new RegionalPlaceOperatingHourDto
                {
                    DayOfWeek = day,
                    IsOpen = false
                });
                continue;
            }

            var openTime = NormalizeClockTime(input.OpenTime, "Opening time is invalid.");
            var closeTime = NormalizeClockTime(input.CloseTime, "Closing time is invalid.");
            // 00:00 - 00:00 is the explicit "open 24 hours" sentinel.
            if (openTime == closeTime && openTime != "00:00")
                throw new InvalidOperationException("Opening time and closing time cannot be the same.");

            normalizedHours.Add(new RegionalPlaceOperatingHourDto
            {
                DayOfWeek = day,
                IsOpen = true,
                OpenTime = openTime,
                CloseTime = closeTime
            });
        }

        if (normalizedHours.Count == 0)
            throw new InvalidOperationException("Operating hours are required when enabled.");

        var json = JsonSerializer.Serialize(
            normalizedHours,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        return new OperatingHoursResult(true, json, normalizedHours);
    }

    private static string NormalizeDayOfWeek(string? dayOfWeek)
    {
        var normalized = NormalizeToken(dayOfWeek ?? string.Empty);
        return normalized switch
        {
            "monday" => "Monday",
            "tuesday" => "Tuesday",
            "wednesday" => "Wednesday",
            "thursday" => "Thursday",
            "friday" => "Friday",
            "saturday" => "Saturday",
            "sunday" => "Sunday",
            _ => throw new InvalidOperationException("Operating hours day is invalid.")
        };
    }

    private static string NormalizeClockTime(string? value, string errorMessage)
    {
        var normalized = value?.Trim() ?? string.Empty;
        if (!TimeSpan.TryParseExact(normalized, "hh\\:mm", null, out _))
            throw new InvalidOperationException(errorMessage);

        return normalized;
    }

    private static string? BuildOpeningHoursSummary(IReadOnlyCollection<RegionalPlaceOperatingHourDto> hours)
    {
        if (IsOpen24HoursSchedule(hours))
            return "Open 24 hours";

        var firstOpen = hours.FirstOrDefault(hour => hour.IsOpen);
        return firstOpen is null
            ? "Closed"
            : $"{firstOpen.DayOfWeek} {firstOpen.OpenTime} - {firstOpen.CloseTime}";
    }

    private static bool IsOpen24HoursSchedule(IReadOnlyCollection<RegionalPlaceOperatingHourDto> hours)
    {
        var requiredDays = Enum.GetNames<DayOfWeek>();
        return hours.Count == requiredDays.Length && requiredDays.All(day =>
            hours.Any(hour =>
                string.Equals(hour.DayOfWeek, day, StringComparison.OrdinalIgnoreCase) &&
                hour.IsOpen &&
                hour.OpenTime == "00:00" &&
                hour.CloseTime == "00:00"));
    }

    private static List<RegionalPlaceOperatingHourDto> DeserializeOperatingHours(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return [];

        try
        {
            return JsonSerializer.Deserialize<List<RegionalPlaceOperatingHourDto>>(
                    json,
                    new JsonSerializerOptions(JsonSerializerDefaults.Web))
                ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static string NormalizeCreateStatus(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return PublishedStatus;

        var normalized = NormalizeToken(value);
        return normalized switch
        {
            "published" or "publish" => PublishedStatus,
            "draft" => DraftStatus,
            _ => throw new InvalidOperationException("Place status is invalid.")
        };
    }

    private static string? NormalizeUrl(string? value, string invalidMessage)
    {
        var normalized = NormalizeOptionalText(value, 500, invalidMessage);
        if (normalized is null)
            return null;

        if (!Uri.TryCreate(normalized, UriKind.Absolute, out var uri) ||
            uri.UserInfo.Length > 0 ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new InvalidOperationException(invalidMessage);
        }

        return uri.ToString();
    }

    private static string? NormalizePhone(string? value)
    {
        var normalized = NormalizeOptionalText(value, 50, "Contact phone is too long.");
        if (normalized is null)
            return null;

        if (normalized.Length < 3 ||
            normalized.Any(ch => !(char.IsDigit(ch) || ch is '+' or '-' or '(' or ')' or ' ' or '.')))
        {
            throw new InvalidOperationException("Contact phone is invalid.");
        }

        return normalized;
    }

    private static string NormalizeRequiredText(string? value, string requiredMessage, int maxLength, string tooLongMessage)
    {
        var normalized = value?.Trim() ?? string.Empty;
        if (normalized.Length == 0)
            throw new InvalidOperationException(requiredMessage);
        if (normalized.Length > maxLength)
            throw new InvalidOperationException(tooLongMessage);

        return normalized;
    }

    private static string? NormalizeOptionalText(string? value, int maxLength, string tooLongMessage)
    {
        if (value is null)
            return null;

        var normalized = value.Trim();
        if (normalized.Length == 0)
            return null;
        if (normalized.Length > maxLength)
            throw new InvalidOperationException(tooLongMessage);

        return normalized;
    }

    private string NormalizeRichDescription(string? value, int maxLength, string tooLongMessage)
    {
        var normalized = _htmlSanitizer.SanitizeRichText(value);
        var withLineBreaks = Regex.Replace(normalized, "<br\\s*/?>", "\n", RegexOptions.IgnoreCase);
        var plainText = WebUtility.HtmlDecode(Regex.Replace(withLineBreaks, "<[^>]+>", string.Empty));
        if (plainText.Length > maxLength)
            throw new InvalidOperationException(tooLongMessage);

        return normalized;
    }

    private static void ValidateLatitude(double latitude)
    {
        if (!double.IsFinite(latitude) || latitude is < -90 or > 90)
            throw new InvalidOperationException("Latitude is invalid.");
    }

    private static void ValidateLongitude(double longitude)
    {
        if (!double.IsFinite(longitude) || longitude is < -180 or > 180)
            throw new InvalidOperationException("Longitude is invalid.");
    }

    private static bool IsStayPlaceType(string? placeTypeName)
    {
        var normalized = NormalizeToken(placeTypeName ?? string.Empty);
        return normalized is "stay" or "stays" or "hotel" or "hotels" or "accommodation" or "accommodations";
    }

    private static RegionalPlaceListItemDto MapListItem(PlaceProjection row)
    {
        return new RegionalPlaceListItemDto
        {
            Id = row.Id,
            Name = row.Name,
            InternalReferenceId = row.InternalReferenceId,
            Summary = BuildSummary(row.Subtitle, row.Description),
            Description = row.Description,
            Subtitle = row.Subtitle,
            Address = row.Address,
            PlaceType = row.PlaceTypeName,
            PlaceTypeId = row.PlaceTypeId,
            StayType = row.StayTypeName,
            StayTypeId = row.StayTypeId,
            Status = string.IsNullOrWhiteSpace(row.Status) ? PublishedStatus : row.Status,
            RegionId = row.RegionId,
            RegionDisplayName = BuildRegionDisplayName(row.RegionName, row.TerritoryName),
            DestinationId = row.DestinationId,
            DestinationName = row.DestinationName,
            LocationId = row.LocationId,
            LocationName = row.LocationName,
            CreatedByUserId = row.CreatedByUserId,
            Tags = row.Tags,
            CreatedAt = row.CreatedAt,
            PublishedAt = row.PublishedAt,
            LastUpdatedAt = row.UpdatedAt ?? row.CreatedAt,
            ImageUrl = row.ImageUrl,
            CardImageUrl = row.CardImageUrl,
            CoverImageUrl = row.CoverImageUrl,
            GalleryImageUrls = row.GalleryImageUrls,
            DetailRoute = "/places/detail/" + row.Id,
            EditRoute = ExistingEditRoute(row.Id)
        };
    }

    private static RegionalPlaceDetailDto MapDetail(PlaceProjection row)
    {
        var item = MapListItem(row);
        return new RegionalPlaceDetailDto
        {
            Id = item.Id,
            Name = item.Name,
            Summary = item.Summary,
            InternalReferenceId = item.InternalReferenceId,
            Description = item.Description,
            Subtitle = item.Subtitle,
            Address = item.Address,
            PlaceType = item.PlaceType,
            PlaceTypeId = item.PlaceTypeId,
            IsOwnable = row.IsOwnable,
            HasLinkedContribution = row.HasLinkedContribution,
            Owners = row.Owners,
            StayType = item.StayType,
            StayTypeId = item.StayTypeId,
            Status = item.Status,
            RegionId = item.RegionId,
            RegionDisplayName = item.RegionDisplayName,
            DestinationId = item.DestinationId,
            DestinationName = item.DestinationName,
            LocationId = item.LocationId,
            LocationName = item.LocationName,
            CreatedByUserId = item.CreatedByUserId,
            Tags = item.Tags,
            CreatedAt = item.CreatedAt,
            PublishedAt = item.PublishedAt,
            LastUpdatedAt = item.LastUpdatedAt,
            ImageUrl = item.ImageUrl,
            CardImageUrl = item.CardImageUrl,
            CoverImageUrl = item.CoverImageUrl,
            GalleryImageUrls = item.GalleryImageUrls,
            DetailRoute = item.DetailRoute,
            EditRoute = item.EditRoute,
            Latitude = row.Latitude,
            Longitude = row.Longitude,
            OpeningHours = row.OpeningHours,
            ContactEmail = row.ContactEmail,
            PhoneNumber = row.PhoneNumber,
            Website = row.Website,
            AdmissionPriceRange = row.AdmissionPriceRange,
            BookingExternalLink = row.BookingExternalLink,
            BestTimeToVisit = row.BestTimeToVisit,
            VisitorTips = row.VisitorTips,
            AccessNotes = row.AccessNotes,
            AccessibilityNotes = row.AccessibilityNotes,
            OperatingHoursEnabled = row.OperatingHoursEnabled,
            OperatingHours = DeserializeOperatingHours(row.OperatingHoursJson),
            Amenities = row.PlaceAmenities
                .Concat(row.StayAmenities)
                .DistinctBy(amenity => amenity.Name, StringComparer.OrdinalIgnoreCase)
                .ToList()
        };
    }

    private static string BuildRegionDisplayName(string regionName, string? territoryName)
    {
        return string.IsNullOrWhiteSpace(territoryName)
            ? regionName
            : regionName + ", " + territoryName;
    }

    private static string? BuildSummary(string? subtitle, string? description)
    {
        var source = string.IsNullOrWhiteSpace(subtitle) ? description : subtitle;
        if (string.IsNullOrWhiteSpace(source))
            return null;

        var normalized = string.Join(" ", source.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
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

    private static string StatusFromToken(string status) =>
        status switch
        {
            "published" => PublishedStatus,
            "draft" => DraftStatus,
            "archived" => ArchivedStatus,
            _ => PublishedStatus
        };

    private static string NormalizeSort(string? sort)
    {
        var normalized = NormalizeToken(sort ?? "newest");
        return normalized switch
        {
            "oldest" => "oldest",
            "nameaz" or "nameasc" => "nameasc",
            "nameza" or "namedesc" => "namedesc",
            _ => "newest"
        };
    }

    private static string NormalizeToken(string value) =>
        value.Replace(" ", string.Empty, StringComparison.Ordinal)
            .Replace("-", string.Empty, StringComparison.Ordinal)
            .Replace("_", string.Empty, StringComparison.Ordinal)
            .ToLowerInvariant();

    private static void ApplyImageUpdates(
        Place place,
        RegionalPlaceUpdateRequestDto request,
        DateTime now)
    {
        if (!string.IsNullOrWhiteSpace(request.CardImageUrl))
        {
            UpsertImage(
                place.Images,
                place.Id,
                EntityImageRole.Card,
                NormalizeManagedImageUrl(request.CardImageUrl, "Card image must be uploaded before it can be assigned."),
                place.Name,
                isMain: true,
                sortOrder: 0,
                now);
        }

        if (!string.IsNullOrWhiteSpace(request.CoverImageUrl))
        {
            UpsertImage(
                place.Images,
                place.Id,
                EntityImageRole.Cover,
                NormalizeManagedImageUrl(request.CoverImageUrl, "Cover image must be uploaded before it can be assigned."),
                place.Name,
                isMain: false,
                sortOrder: 1,
                now);
        }

        if (request.GalleryImageUrls is not null)
        {
            var existingGalleryImages = place.Images
                .Where(image => image.TargetType == EntityImageTargetType.Place &&
                                image.PlaceId == place.Id &&
                                image.ImageRole == EntityImageRole.Gallery)
                .ToList();

            foreach (var image in existingGalleryImages)
                place.Images.Remove(image);

            var galleryUrls = request.GalleryImageUrls
                .Select(NormalizeOptionalImageUrl)
                .Where(url => !string.IsNullOrWhiteSpace(url))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            for (var index = 0; index < galleryUrls.Count; index++)
            {
                place.Images.Add(new EntityImage
                {
                    TargetType = EntityImageTargetType.Place,
                    PlaceId = place.Id,
                    Url = galleryUrls[index]!,
                    AltText = place.Name,
                    IsMain = false,
                    ImageRole = EntityImageRole.Gallery,
                    SortOrder = index + 2,
                    CreatedAt = now
                });
            }
        }
    }

    private static void UpsertImage(
        ICollection<EntityImage> images,
        int placeId,
        string role,
        string url,
        string altText,
        bool isMain,
        int sortOrder,
        DateTime now)
    {
        var image = images.FirstOrDefault(item =>
            item.TargetType == EntityImageTargetType.Place &&
            item.PlaceId == placeId &&
            item.ImageRole == role);

        if (image is null)
        {
            images.Add(new EntityImage
            {
                TargetType = EntityImageTargetType.Place,
                PlaceId = placeId,
                Url = url,
                AltText = altText,
                IsMain = isMain,
                ImageRole = role,
                SortOrder = sortOrder,
                CreatedAt = now
            });
            return;
        }

        image.Url = url;
        image.AltText = altText;
        image.IsMain = isMain;
        image.SortOrder = sortOrder;
        image.UpdatedAt = now;
    }

    private static List<EntityImage> AddRequiredCreateImages(RegionalPlaceCreateRequestDto request, int entityId, string entityName, DateTime now)
    {
        var cardUrl = NormalizeRequiredImageUrl(request.CardImageUrl, "Card image is required.");
        var coverUrl = NormalizeRequiredImageUrl(request.CoverImageUrl, "Cover image is required.");
        var images = new List<EntityImage>
        {
            new()
            {
                TargetType = EntityImageTargetType.Place,
                PlaceId = entityId,
                Url = cardUrl,
                AltText = entityName,
                IsMain = true,
                ImageRole = EntityImageRole.Card,
                SortOrder = 0,
                CreatedAt = now
            },
            new()
            {
                TargetType = EntityImageTargetType.Place,
                PlaceId = entityId,
                Url = coverUrl,
                AltText = entityName,
                IsMain = false,
                ImageRole = EntityImageRole.Cover,
                SortOrder = 1,
                CreatedAt = now
            }
        };

        var galleryUrls = request.GalleryImageUrls
            .Select(url => NormalizeOptionalImageUrl(url))
            .Where(url => !string.IsNullOrWhiteSpace(url))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        for (var i = 0; i < galleryUrls.Count; i++)
        {
            images.Add(new EntityImage
            {
                TargetType = EntityImageTargetType.Place,
                PlaceId = entityId,
                Url = galleryUrls[i]!,
                AltText = entityName,
                IsMain = false,
                ImageRole = EntityImageRole.Gallery,
                SortOrder = i + 2,
                CreatedAt = now
            });
        }

        return images;
    }

    private static string NormalizeRequiredImageUrl(string? value, string message)
    {
        var normalized = NormalizeOptionalImageUrl(value);
        if (string.IsNullOrWhiteSpace(normalized))
            throw new InvalidOperationException(message);

        return normalized;
    }

    private static string? NormalizeOptionalImageUrl(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var normalized = value.Trim();
        if (normalized.Length > 1000)
            throw new InvalidOperationException("Image URL is too long.");

        return normalized;
    }

    private static string NormalizeManagedImageUrl(string? value, string message)
    {
        var normalized = NormalizeRequiredImageUrl(value, message);
        if (!normalized.StartsWith(ManagedRegionalImagePrefix, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Image must be uploaded before it can be assigned.");

        return normalized;
    }

    private static string BuildInternalReferenceId(int placeId) => $"PLC-{placeId:D6}";

    private sealed record PlaceCoordinates(double Latitude, double Longitude);

    private sealed record OperatingHoursResult(
        bool Enabled,
        string? Json,
        IReadOnlyCollection<RegionalPlaceOperatingHourDto> Hours);

    private sealed class PlaceProjection
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? InternalReferenceId { get; set; }
        public string? Subtitle { get; set; }
        public string? Description { get; set; }
        public string Status { get; set; } = PublishedStatus;
        public string? Address { get; set; }
        public string? OpeningHours { get; set; }
        public bool OperatingHoursEnabled { get; set; }
        public string? OperatingHoursJson { get; set; }
        public string? ContactEmail { get; set; }
        public string? PhoneNumber { get; set; }
        public string? Website { get; set; }
        public string? AdmissionPriceRange { get; set; }
        public string? BookingExternalLink { get; set; }
        public string? BestTimeToVisit { get; set; }
        public string? VisitorTips { get; set; }
        public string? AccessNotes { get; set; }
        public string? AccessibilityNotes { get; set; }
        public int PlaceTypeId { get; set; }
        public string? PlaceTypeName { get; set; }
        public bool IsOwnable { get; set; }
        public bool HasLinkedContribution { get; set; }
        public List<RegionalPlaceOwnerDto> Owners { get; set; } = new();
        public int? StayTypeId { get; set; }
        public string? StayTypeName { get; set; }
        public int LocationId { get; set; }
        public string LocationName { get; set; } = string.Empty;
        public int DestinationId { get; set; }
        public string DestinationName { get; set; } = string.Empty;
        public int RegionId { get; set; }
        public string RegionName { get; set; } = string.Empty;
        public string? TerritoryName { get; set; }
        public int? CreatedByUserId { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
        public DateTime? PublishedAt { get; set; }
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public string? ImageUrl { get; set; }
        public string? CardImageUrl { get; set; }
        public string? CoverImageUrl { get; set; }
        public List<string> GalleryImageUrls { get; set; } = new();
        public List<RegionalPlaceTagDto> Tags { get; set; } = new();
        public List<RegionalPlaceAmenityDto> PlaceAmenities { get; set; } = new();
        public List<RegionalPlaceAmenityDto> StayAmenities { get; set; } = new();
    }
}
