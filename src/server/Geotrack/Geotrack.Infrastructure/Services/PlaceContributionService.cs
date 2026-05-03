using System.Text.Json;
using System.Text.RegularExpressions;
using Geotrack.Application.DTOs.Contributions;
using Geotrack.Application.Interfaces.Service;
using Geotrack.Domain.Entities.Core;
using Geotrack.Domain.Entities.Tags;
using Geotrack.Domain.Entities.Users;
using Microsoft.EntityFrameworkCore;

namespace Geotrack.Infrastructure.Services;

public class PlaceContributionService : IPlaceContributionService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly AppDbContext _db;
    private readonly IContributionMediaStorageService _mediaStorage;
    private readonly IUserNotificationService _notificationService;

    public PlaceContributionService(
        AppDbContext db,
        IContributionMediaStorageService mediaStorage,
        IUserNotificationService notificationService)
    {
        _db = db;
        _mediaStorage = mediaStorage;
        _notificationService = notificationService;
    }

    public async Task<GetPlaceContributionsResponseDto> GetMineAsync(int userId)
    {
        var contributions = await _db.PlaceContributions
            .AsNoTracking()
            .Include(x => x.PlaceType)
            .Include(x => x.Location)
                .ThenInclude(x => x!.Destination)
                    .ThenInclude(x => x.Region)
                        .ThenInclude(x => x.Territory)
            .Include(x => x.StayType)
            .Include(x => x.Images)
            .Where(x => x.UserId == userId)
            .OrderByDescending(x => x.UpdatedAt ?? x.SubmittedAt)
            .ToListAsync();

        return new GetPlaceContributionsResponseDto
        {
            Contributions = contributions.Select(MapListItem).ToList()
        };
    }

    public async Task<PlaceContributionDetailDto> GetMineDetailAsync(int userId, int contributionId)
    {
        var contribution = await LoadContributionDetails()
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == contributionId && x.UserId == userId)
            ?? throw new KeyNotFoundException("Contribution not found");

        return MapDetail(contribution);
    }

    public async Task<PlaceContributionDetailDto> CreateAsync(int userId, UpsertPlaceContributionDto request, byte[]? mainImageBytes)
    {
        Validate(request, isUpdateMethod: false);

        var now = DateTime.UtcNow;
        string? storedMediaUrl = null;

        if (mainImageBytes is { Length: > 0 })
        {
            storedMediaUrl = (await _mediaStorage.StoreAsync(userId, mainImageBytes)).MediaUrl;
        }

        await using var transaction = await _db.Database.BeginTransactionAsync();

        try
        {
            var references = await ResolveContributionReferencesAsync(request);

            var contribution = new PlaceContribution
            {
                Type = ParseType(request.Type),
                Status = PlaceContributionStatus.PendingReview,
                UserId = userId,
                PlaceId = request.PlaceId,
                PlaceTypeId = references.PlaceTypeId,
                LocationId = references.LocationId,
                RestaurantTypeId = request.RestaurantTypeId,
                StayTypeId = references.StayTypeId,
                Name = request.Name.Trim(),
                Subtitle = NormalizeOptional(request.Subtitle, 500),
                Address = NormalizeOptional(request.Address, 500),
                OpeningHours = NormalizeOptional(request.OpeningHours, 200),
                Description = request.Description.Trim(),
                Latitude = request.Latitude,
                Longitude = request.Longitude,
                ContactEmail = NormalizeContactEmail(request.ContactEmail),
                PhoneNumber = NormalizeOptional(request.PhoneNumber ?? request.Phone, 50),
                Website = NormalizeWebsite(request.Website),
                PricePerNight = NormalizePricePerNight(request.PricePerNight),
                CheckInTime = NormalizeOptional(request.CheckInTime, 20),
                CheckOutTime = NormalizeOptional(request.CheckOutTime, 20),
                TagsJson = Serialize(NormalizeStringList(request.Tags, 80, 30)),
                AmenitiesJson = Serialize(references.PlaceAmenities),
                StayAmenitiesJson = Serialize(references.StayAmenities),
                BookingPartnersJson = Serialize(NormalizeBookingPartners(request.BookingPartners)),
                SubmittedAt = now,
                CreatedAt = now,
                UpdatedAt = now
            };

            ApplyImages(contribution, request.Images, storedMediaUrl, now, request.ImagePosition, request.ImageSize);

            await _db.PlaceContributions.AddAsync(contribution);
            await _db.SaveChangesAsync();
            await transaction.CommitAsync();

            return MapDetail(contribution);
        }
        catch
        {
            if (storedMediaUrl is not null)
                await _mediaStorage.DeleteAsync(storedMediaUrl);

            throw;
        }
    }

    public async Task<PlaceContributionDetailDto> UpdateAsync(
        int userId,
        int contributionId,
        UpsertPlaceContributionDto request,
        byte[]? mainImageBytes)
    {
        Validate(request, isUpdateMethod: true);

        var contribution = await _db.PlaceContributions
            .Include(x => x.Images)
            .FirstOrDefaultAsync(x => x.Id == contributionId && x.UserId == userId)
            ?? throw new KeyNotFoundException("Contribution not found");

        var oldManagedImages = contribution.Images
            .Where(x => _mediaStorage.IsManagedContributionMediaUrl(x.Url))
            .Select(x => x.Url)
            .ToList();

        string? storedMediaUrl = null;
        if (mainImageBytes is { Length: > 0 })
        {
            storedMediaUrl = (await _mediaStorage.StoreAsync(userId, mainImageBytes)).MediaUrl;
        }

        var references = await ResolveContributionReferencesAsync(request);

        contribution.Type = ParseType(request.Type);
        contribution.PlaceId = request.PlaceId;
        contribution.PlaceTypeId = references.PlaceTypeId;
        contribution.LocationId = references.LocationId;
        contribution.RestaurantTypeId = request.RestaurantTypeId;
        contribution.StayTypeId = references.StayTypeId;
        contribution.Name = request.Name.Trim();
        contribution.Subtitle = NormalizeOptional(request.Subtitle, 500);
        contribution.Address = NormalizeOptional(request.Address, 500);
        contribution.OpeningHours = NormalizeOptional(request.OpeningHours, 200);
        contribution.Description = request.Description.Trim();
        contribution.Latitude = request.Latitude;
        contribution.Longitude = request.Longitude;
        contribution.ContactEmail = NormalizeContactEmail(request.ContactEmail);
        contribution.PhoneNumber = NormalizeOptional(request.PhoneNumber ?? request.Phone, 50);
        contribution.Website = NormalizeWebsite(request.Website);
        contribution.PricePerNight = NormalizePricePerNight(request.PricePerNight);
        contribution.CheckInTime = NormalizeOptional(request.CheckInTime, 20);
        contribution.CheckOutTime = NormalizeOptional(request.CheckOutTime, 20);
        contribution.TagsJson = Serialize(NormalizeStringList(request.Tags, 80, 30));
        contribution.AmenitiesJson = Serialize(references.PlaceAmenities);
        contribution.StayAmenitiesJson = Serialize(references.StayAmenities);
        contribution.BookingPartnersJson = Serialize(NormalizeBookingPartners(request.BookingPartners));
        contribution.Status = PlaceContributionStatus.PendingReview;
        contribution.ReviewNote = null;
        contribution.ReviewedAt = null;
        contribution.ReviewedByUserId = null;
        contribution.UpdatedAt = DateTime.UtcNow;

        var shouldReplaceImages = request.Images is not null || storedMediaUrl is not null;
        if (shouldReplaceImages)
        {
            _db.PlaceContributionImages.RemoveRange(contribution.Images);
            contribution.Images.Clear();
            ApplyImages(contribution, request.Images, storedMediaUrl, DateTime.UtcNow, request.ImagePosition, request.ImageSize);
        }

        try
        {
            await _db.SaveChangesAsync();
        }
        catch
        {
            if (storedMediaUrl is not null)
                await _mediaStorage.DeleteAsync(storedMediaUrl);

            throw;
        }

        if (shouldReplaceImages)
        {
            var currentUrls = contribution.Images.Select(x => x.Url).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var oldUrl in oldManagedImages.Where(url => !currentUrls.Contains(url)))
            {
                await _mediaStorage.DeleteAsync(oldUrl);
            }
        }

        return MapDetail(contribution);
    }

    public async Task DeleteAsync(int userId, int contributionId)
    {
        var contribution = await _db.PlaceContributions
            .Include(x => x.Images)
            .FirstOrDefaultAsync(x => x.Id == contributionId && x.UserId == userId)
            ?? throw new KeyNotFoundException("Contribution not found");

        var mediaUrls = contribution.Images
            .Where(x => _mediaStorage.IsManagedContributionMediaUrl(x.Url))
            .Select(x => x.Url)
            .ToList();

        _db.PlaceContributions.Remove(contribution);
        await _db.SaveChangesAsync();

        foreach (var url in mediaUrls)
        {
            await _mediaStorage.DeleteAsync(url);
        }
    }

    public async Task<GetPlaceContributionsResponseDto> GetReviewQueueAsync(int reviewerUserId, string? status)
    {
        var reviewer = await EnsureRegionalManagerAsync(reviewerUserId);
        var assignedRegionIds = reviewer.RegionalManagerRegions.Select(x => x.RegionId).ToHashSet();

        if (assignedRegionIds.Count == 0)
            return new GetPlaceContributionsResponseDto();

        var query = _db.PlaceContributions
            .AsNoTracking()
            .Include(x => x.PlaceType)
            .Include(x => x.Location)
                .ThenInclude(x => x!.Destination)
                    .ThenInclude(x => x.Region)
                        .ThenInclude(x => x.Territory)
            .Include(x => x.StayType)
            .Include(x => x.Images)
            .Where(x => x.Location != null && assignedRegionIds.Contains(x.Location.Destination.RegionId));

        if (!string.IsNullOrWhiteSpace(status))
            query = query.Where(x => x.Status == ParseStatus(status));

        var contributions = await query
            .OrderByDescending(x => x.UpdatedAt ?? x.SubmittedAt)
            .ToListAsync();

        return new GetPlaceContributionsResponseDto
        {
            Contributions = contributions.Select(MapListItem).ToList()
        };
    }

    public async Task<PlaceContributionDetailDto> ApproveAsync(
        int reviewerUserId,
        int contributionId,
        ReviewPlaceContributionRequestDto request)
    {
        var contribution = await LoadContributionDetails()
            .FirstOrDefaultAsync(x => x.Id == contributionId)
            ?? throw new KeyNotFoundException("Contribution not found");

        await EnsureRegionalManagerCanReviewAsync(reviewerUserId, contribution);

        await using var transaction = await _db.Database.BeginTransactionAsync();

        var place = await PublishPlaceAsync(contribution);
        var now = DateTime.UtcNow;

        contribution.PlaceId = place.Id;
        contribution.Status = PlaceContributionStatus.Approved;
        contribution.ReviewedByUserId = reviewerUserId;
        contribution.ReviewedAt = now;
        contribution.ReviewNote = NormalizeReviewNote(request.Note);
        contribution.UpdatedAt = now;

        _notificationService.AddContributionDecisionNotification(contribution);

        await _db.SaveChangesAsync();
        await transaction.CommitAsync();

        return MapDetail(contribution);
    }

    public async Task<PlaceContributionDetailDto> RejectAsync(
        int reviewerUserId,
        int contributionId,
        ReviewPlaceContributionRequestDto request)
    {
        var contribution = await LoadContributionDetails()
            .FirstOrDefaultAsync(x => x.Id == contributionId)
            ?? throw new KeyNotFoundException("Contribution not found");

        await EnsureRegionalManagerCanReviewAsync(reviewerUserId, contribution);

        var now = DateTime.UtcNow;
        contribution.Status = PlaceContributionStatus.Rejected;
        contribution.ReviewedByUserId = reviewerUserId;
        contribution.ReviewedAt = now;
        contribution.ReviewNote = NormalizeReviewNote(request.Note);
        contribution.UpdatedAt = now;

        _notificationService.AddContributionDecisionNotification(contribution);

        await _db.SaveChangesAsync();

        return MapDetail(contribution);
    }

    private IQueryable<PlaceContribution> LoadContributionDetails()
    {
        return _db.PlaceContributions
            .Include(x => x.User)
            .Include(x => x.ReviewedByUser)
            .Include(x => x.Place)
                .ThenInclude(x => x!.Images)
            .Include(x => x.Place)
                .ThenInclude(x => x!.Stay)
                    .ThenInclude(x => x!.StayType)
            .Include(x => x.PlaceType)
            .Include(x => x.Location)
                .ThenInclude(x => x!.Destination)
                    .ThenInclude(x => x.Region)
                        .ThenInclude(x => x.Territory)
            .Include(x => x.RestaurantType)
            .Include(x => x.StayType)
            .Include(x => x.Images);
    }

    private async Task<Place> PublishPlaceAsync(PlaceContribution contribution)
    {
        var now = DateTime.UtcNow;

        if (!contribution.PlaceTypeId.HasValue)
            throw new InvalidOperationException("Place type is required before approval.");

        if (!contribution.LocationId.HasValue)
            throw new InvalidOperationException("Location is required before approval.");

        var place = contribution.Type == PlaceContributionType.UpdatePlace && contribution.PlaceId.HasValue
            ? await _db.Places
                .Include(x => x.PlaceTags)
                .Include(x => x.PlaceAmenities)
                .Include(x => x.PlaceBookingPartners)
                .Include(x => x.Images)
                .Include(x => x.Stay)
                    .ThenInclude(x => x!.StayAmenities)
                .FirstOrDefaultAsync(x => x.Id == contribution.PlaceId.Value)
            : null;

        if (place is null)
        {
            place = new Place { CreatedAt = now };
            await _db.Places.AddAsync(place);
        }

        place.Name = contribution.Name.Trim();
        place.Subtitle = contribution.Subtitle;
        place.PlaceTypeId = contribution.PlaceTypeId.Value;
        place.LocationId = contribution.LocationId.Value;
        place.Address = contribution.Address;
        place.OpeningHours = contribution.OpeningHours;
        place.Description = contribution.Description.Trim();
        place.Latitude = contribution.Latitude ?? 0;
        place.Longitude = contribution.Longitude ?? 0;
        place.ContactEmail = contribution.ContactEmail;
        place.Number = contribution.PhoneNumber;
        place.UpdatedAt = now;

        await _db.SaveChangesAsync();

        await SyncPlaceTagsAsync(place, DeserializeStringList(contribution.TagsJson), now);
        await SyncPlaceAmenitiesAsync(place, DeserializeStringList(contribution.AmenitiesJson));
        await SyncBookingPartnersAsync(place, DeserializeBookingPartners(contribution.BookingPartnersJson));
        await SyncStayAsync(place, contribution, DeserializeStringList(contribution.StayAmenitiesJson), now);
        SyncEntityImages(place, contribution.Images, now);
        await EnsureUserPlaceAsync(contribution.UserId, place.Id, now);

        return place;
    }

    private async Task SyncStayAsync(Place place, PlaceContribution contribution, List<string> stayAmenitySlugs, DateTime now)
    {
        if (!contribution.StayTypeId.HasValue)
        {
            if (place.Stay is not null)
                _db.Stays.Remove(place.Stay);

            return;
        }

        var stay = place.Stay;
        if (stay is null)
        {
            stay = new Stay
            {
                PlaceId = place.Id,
                CreatedAt = now
            };
            await _db.Stays.AddAsync(stay);
        }

        stay.StayTypeId = contribution.StayTypeId.Value;
        stay.PricePerNight = contribution.PricePerNight;
        stay.CheckInTime = contribution.CheckInTime;
        stay.CheckOutTime = contribution.CheckOutTime;
        stay.UpdatedAt = now;

        if (place.Stay is null)
            place.Stay = stay;

        _db.StayStayAmenities.RemoveRange(stay.StayAmenities);

        if (stayAmenitySlugs.Count == 0)
            return;

        var amenities = await _db.StayAmenities
            .Where(x => stayAmenitySlugs.Contains(x.Slug))
            .ToListAsync();

        foreach (var amenity in amenities)
        {
            stay.StayAmenities.Add(new StayStayAmenity
            {
                StayPlaceId = place.Id,
                StayAmenityId = amenity.Id
            });
        }
    }

    private async Task SyncPlaceTagsAsync(Place place, List<string> tagSlugsOrNames, DateTime now)
    {
        _db.PlaceTags.RemoveRange(place.PlaceTags);

        foreach (var value in tagSlugsOrNames.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var slug = Slugify(value);
            var tag = await _db.Tags.FirstOrDefaultAsync(x => x.Slug == slug || x.Name.ToLower() == value.ToLower());
            if (tag is null)
            {
                tag = new Tag
                {
                    Name = ToTitle(value),
                    Slug = slug,
                    ForPlace = true,
                    CreatedAt = now
                };
                await _db.Tags.AddAsync(tag);
                await _db.SaveChangesAsync();
            }

            place.PlaceTags.Add(new PlaceTag
            {
                PlaceId = place.Id,
                TagId = tag.Id
            });
        }
    }

    private async Task SyncPlaceAmenitiesAsync(Place place, List<string> amenityNamesOrIcons)
    {
        _db.PlaceAmenities.RemoveRange(place.PlaceAmenities);

        foreach (var value in amenityNamesOrIcons.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var normalized = value.Trim().ToLowerInvariant();
            var amenity = await _db.Amenities.FirstOrDefaultAsync(x => x.Icon == normalized || x.Name.ToLower() == normalized);
            if (amenity is not null)
            {
                place.PlaceAmenities.Add(new PlaceAmenity
                {
                    PlaceId = place.Id,
                    AmenityId = amenity.Id
                });
            }
        }
    }

    private async Task SyncBookingPartnersAsync(Place place, List<ContributionBookingPartnerDto> partners)
    {
        _db.PlaceBookingPartners.RemoveRange(place.PlaceBookingPartners);

        foreach (var partnerDto in partners)
        {
            var partnerName = PartnerName(partnerDto.Id);
            var partner = await _db.BookingPartners.FirstOrDefaultAsync(x => x.Name == partnerName);
            if (partner is null)
            {
                partner = new BookingPartner
                {
                    Name = partnerName,
                    Variant = PartnerVariant(partnerDto.Id)
                };
                await _db.BookingPartners.AddAsync(partner);
                await _db.SaveChangesAsync();
            }

            place.PlaceBookingPartners.Add(new PlaceBookingPartner
            {
                PlaceId = place.Id,
                BookingPartnerId = partner.Id,
                Url = partnerDto.Url
            });
        }
    }

    private void SyncEntityImages(Place place, IEnumerable<PlaceContributionImage> contributionImages, DateTime now)
    {
        _db.EntityImages.RemoveRange(place.Images);

        var orderedImages = contributionImages
            .OrderByDescending(x => x.IsMain)
            .ThenBy(x => x.SortOrder)
            .ToList();

        var hasMain = orderedImages.Any(x => x.IsMain);
        var mainSeen = false;
        for (var index = 0; index < orderedImages.Count; index++)
        {
            var image = orderedImages[index];
            var isMain = hasMain
                ? image.IsMain && !mainSeen
                : index == 0;

            if (isMain)
                mainSeen = true;

            place.Images.Add(new EntityImage
            {
                TargetType = EntityImageTargetType.Place,
                PlaceId = place.Id,
                Url = image.Url,
                AltText = image.AltText,
                Caption = image.Caption,
                IsMain = isMain,
                SortOrder = image.SortOrder,
                CreatedAt = now
            });
        }
    }

    private async Task EnsureUserPlaceAsync(int userId, int placeId, DateTime now)
    {
        var exists = await _db.UserPlaces.AnyAsync(x => x.UserId == userId && x.PlaceId == placeId);
        if (exists)
            return;

        await _db.UserPlaces.AddAsync(new UserPlace
        {
            UserId = userId,
            PlaceId = placeId,
            CreatedAt = now
        });
    }

    private async Task<RegionalManager> EnsureRegionalManagerAsync(int userId)
    {
        return await _db.RegionalManagers
            .Include(x => x.RegionalManagerRegions)
                .ThenInclude(x => x.Region)
            .FirstOrDefaultAsync(x => x.UserId == userId)
            ?? throw new UnauthorizedAccessException("Only regional managers can review contributions.");
    }

    private async Task EnsureRegionalManagerCanReviewAsync(int reviewerUserId, PlaceContribution contribution)
    {
        var reviewer = await EnsureRegionalManagerAsync(reviewerUserId);
        var assignedRegionIds = reviewer.RegionalManagerRegions.Select(x => x.RegionId).ToHashSet();

        if (!contribution.LocationId.HasValue)
            throw new UnauthorizedAccessException("Contribution has no location assigned.");

        var canReview = await _db.Locations
            .AnyAsync(x => x.Id == contribution.LocationId.Value && assignedRegionIds.Contains(x.Destination.RegionId));

        if (!canReview)
            throw new UnauthorizedAccessException("You are not assigned to this contribution's region.");
    }

    private async Task<ContributionReferenceResolution> ResolveContributionReferencesAsync(UpsertPlaceContributionDto request)
    {
        var stayTypeId = request.StayTypeId;
        if (!stayTypeId.HasValue && !string.IsNullOrWhiteSpace(request.StayType))
        {
            stayTypeId = await ResolveStayTypeIdAsync(request.StayType);
        }

        var placeTypeId = request.PlaceTypeId;
        if (!placeTypeId.HasValue && stayTypeId.HasValue)
        {
            placeTypeId = await ResolvePlaceTypeIdAsync("Stay");
        }

        var locationId = request.LocationId ?? await ResolveLegacyLocationIdAsync(request);
        var requestedPlaceAmenities = NormalizeStringList(request.Amenities, 80, 30);
        var requestedStayAmenities = NormalizeStringList(request.StayAmenities, 80, 30);

        if (stayTypeId.HasValue && requestedStayAmenities.Count == 0 && requestedPlaceAmenities.Count > 0)
        {
            requestedStayAmenities = NormalizeStayAmenityValues(requestedPlaceAmenities);
            requestedPlaceAmenities = new List<string>();
        }
        else if (stayTypeId.HasValue)
        {
            requestedStayAmenities = NormalizeStayAmenityValues(requestedStayAmenities);
        }

        return new ContributionReferenceResolution(
            placeTypeId,
            locationId,
            stayTypeId,
            requestedPlaceAmenities,
            requestedStayAmenities);
    }

    private async Task<int?> ResolvePlaceTypeIdAsync(string name)
    {
        var normalized = name.Trim().ToLowerInvariant();
        var placeType = await _db.PlaceTypes
            .FirstOrDefaultAsync(x => x.Name.ToLower() == normalized);

        if (placeType is not null)
            return placeType.Id;

        placeType = new PlaceType
        {
            Name = ToTitle(name),
            Description = "Accommodation and stay listings",
            CreatedAt = DateTime.UtcNow
        };

        await _db.PlaceTypes.AddAsync(placeType);
        await _db.SaveChangesAsync();
        return placeType.Id;
    }

    private async Task<int?> ResolveStayTypeIdAsync(string stayTypeName)
    {
        var normalized = stayTypeName.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
            return null;

        var slug = Slugify(normalized);
        var stayType = await _db.StayTypes
            .FirstOrDefaultAsync(x => x.Slug == slug || x.Name.ToLower() == normalized.ToLowerInvariant());

        if (stayType is not null)
            return stayType.Id;

        stayType = new StayType
        {
            Name = ToTitle(normalized),
            Slug = slug,
            Description = $"{ToTitle(normalized)} accommodation",
            CreatedAt = DateTime.UtcNow
        };

        await _db.StayTypes.AddAsync(stayType);
        await _db.SaveChangesAsync();
        return stayType.Id;
    }

    private async Task<int?> ResolveLegacyLocationIdAsync(UpsertPlaceContributionDto request)
    {
        var city = NormalizeOptional(request.City, 200);
        var regionName = NormalizeOptional(request.Region, 200);
        var countryName = NormalizeOptional(request.Country, 200);

        if (city is null && regionName is null && countryName is null)
            return null;

        if (city is not null)
        {
            var directLocation = await _db.Locations
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.Name.ToLower() == city.ToLowerInvariant());

            if (directLocation is not null)
                return directLocation.Id;

            var matchingDestination = await _db.Destinations
                .Include(x => x.Locations)
                .Include(x => x.Region)
                    .ThenInclude(x => x.Territory)
                .FirstOrDefaultAsync(x => x.Name.ToLower() == city.ToLowerInvariant());

            if (matchingDestination is not null)
            {
                var existing = SelectBestLocation(matchingDestination.Locations, request.Latitude, request.Longitude);
                if (existing is not null)
                    return existing.Id;
            }
        }

        var territory = countryName is not null
            ? await _db.Territories.FirstOrDefaultAsync(x => x.Name.ToLower() == countryName.ToLowerInvariant())
            : await _db.Territories.OrderBy(x => x.Id).FirstOrDefaultAsync();

        if (territory is null)
        {
            territory = new Territory
            {
                Name = countryName ?? "Unknown",
                CreatedAt = DateTime.UtcNow
            };
            await _db.Territories.AddAsync(territory);
            await _db.SaveChangesAsync();
        }

        var resolvedRegionName = regionName ?? territory.Name;
        var region = await _db.Regions
            .FirstOrDefaultAsync(x => x.TerritoryId == territory.Id && x.Name.ToLower() == resolvedRegionName.ToLowerInvariant());

        if (region is null)
        {
            region = new Region
            {
                Name = resolvedRegionName,
                TerritoryId = territory.Id,
                CreatedAt = DateTime.UtcNow
            };
            await _db.Regions.AddAsync(region);
            await _db.SaveChangesAsync();
        }

        var destinationName = city ?? region.Name;
        var destination = await _db.Destinations
            .Include(x => x.Locations)
            .FirstOrDefaultAsync(x => x.RegionId == region.Id && x.Name.ToLower() == destinationName.ToLowerInvariant());

        if (destination is null)
        {
            var destinationType = await ResolveDestinationTypeAsync();
            destination = new Destination
            {
                Name = destinationName,
                DestinationTypeId = destinationType.Id,
                RegionId = region.Id,
                Description = string.Empty,
                Latitude = request.Latitude ?? 0,
                Longitude = request.Longitude ?? 0,
                CreatedAt = DateTime.UtcNow
            };
            await _db.Destinations.AddAsync(destination);
            await _db.SaveChangesAsync();
        }
        else
        {
            var existing = SelectBestLocation(destination.Locations, request.Latitude, request.Longitude);
            if (existing is not null)
                return existing.Id;
        }

        var locationType = await ResolveLocationTypeAsync();
        var location = new Location
        {
            Name = destinationName,
            DestinationId = destination.Id,
            LocationTypeId = locationType.Id,
            Latitude = request.Latitude ?? destination.Latitude,
            Longitude = request.Longitude ?? destination.Longitude,
            CreatedAt = DateTime.UtcNow
        };

        await _db.Locations.AddAsync(location);
        await _db.SaveChangesAsync();
        return location.Id;
    }

    private async Task<DestinationType> ResolveDestinationTypeAsync()
    {
        var destinationType = await _db.DestinationTypes
            .FirstOrDefaultAsync(x => x.Name == "City")
            ?? await _db.DestinationTypes.OrderBy(x => x.Id).FirstOrDefaultAsync();

        if (destinationType is not null)
            return destinationType;

        destinationType = new DestinationType
        {
            Name = "City",
            Description = "City destination",
            CreatedAt = DateTime.UtcNow
        };
        await _db.DestinationTypes.AddAsync(destinationType);
        await _db.SaveChangesAsync();
        return destinationType;
    }

    private async Task<LocationType> ResolveLocationTypeAsync()
    {
        var locationType = await _db.LocationTypes
            .FirstOrDefaultAsync(x => x.Name == "Neighborhood")
            ?? await _db.LocationTypes.OrderBy(x => x.Id).FirstOrDefaultAsync();

        if (locationType is not null)
            return locationType;

        locationType = new LocationType
        {
            Name = "Area",
            Description = "Contribution-created area",
            CreatedAt = DateTime.UtcNow
        };
        await _db.LocationTypes.AddAsync(locationType);
        await _db.SaveChangesAsync();
        return locationType;
    }

    private static Location? SelectBestLocation(IEnumerable<Location> locations, double? latitude, double? longitude)
    {
        var candidates = locations.ToList();
        if (candidates.Count == 0)
            return null;

        if (latitude.HasValue && longitude.HasValue)
        {
            return candidates
                .OrderBy(x => Math.Pow(x.Latitude - latitude.Value, 2) + Math.Pow(x.Longitude - longitude.Value, 2))
                .First();
        }

        return candidates.OrderBy(x => x.Id).First();
    }

    private static void ApplyImages(
        PlaceContribution contribution,
        IEnumerable<UpsertPlaceContributionImageDto>? requestImages,
        string? storedMainImageUrl,
        DateTime now,
        string? imagePosition = null,
        string? imageSize = null)
    {
        var images = NormalizeImages(requestImages).ToList();

        if (!string.IsNullOrWhiteSpace(storedMainImageUrl))
        {
            images.Insert(0, new UpsertPlaceContributionImageDto
            {
                Url = storedMainImageUrl,
                IsMain = true,
                SortOrder = 0,
                ImagePosition = imagePosition ?? "50% 50%",
                ImageSize = imageSize ?? "cover"
            });
        }

        if (images.Count == 0)
            return;

        if (!images.Any(x => x.IsMain))
            images[0].IsMain = true;

        var mainSeen = false;
        foreach (var image in images.OrderBy(x => x.SortOrder))
        {
            var isMain = image.IsMain && !mainSeen;
            if (isMain)
                mainSeen = true;

            contribution.Images.Add(new PlaceContributionImage
            {
                Url = image.Url.Trim(),
                AltText = NormalizeOptional(image.AltText, 300),
                Caption = NormalizeOptional(image.Caption, 500),
                IsMain = isMain,
                SortOrder = image.SortOrder,
                ImagePosition = NormalizeImagePosition(image.ImagePosition),
                ImageSize = NormalizeImageSize(image.ImageSize),
                CreatedAt = now
            });
        }
    }

    private static IEnumerable<UpsertPlaceContributionImageDto> NormalizeImages(IEnumerable<UpsertPlaceContributionImageDto>? images)
    {
        return (images ?? Enumerable.Empty<UpsertPlaceContributionImageDto>())
            .Where(x => !string.IsNullOrWhiteSpace(x.Url))
            .Select((x, index) => new UpsertPlaceContributionImageDto
            {
                Url = x.Url.Trim(),
                AltText = x.AltText,
                Caption = x.Caption,
                IsMain = x.IsMain,
                SortOrder = x.SortOrder < 0 ? index : x.SortOrder,
                ImagePosition = x.ImagePosition,
                ImageSize = x.ImageSize
            })
            .DistinctBy(x => x.Url, StringComparer.OrdinalIgnoreCase)
            .Take(20);
    }

    private static void Validate(UpsertPlaceContributionDto request, bool isUpdateMethod)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            throw new InvalidOperationException("Name is required.");

        if (request.Name.Trim().Length > 200)
            throw new InvalidOperationException("Name is too long.");

        if (request.Description.Trim().Length > 2000)
            throw new InvalidOperationException("Description is too long.");

        if (request is { Latitude: null, Longitude: not null } or { Latitude: not null, Longitude: null })
            throw new InvalidOperationException("Both latitude and longitude are required when setting coordinates.");

        if (request.Latitude is < -90 or > 90)
            throw new InvalidOperationException("Latitude must be between -90 and 90.");

        if (request.Longitude is < -180 or > 180)
            throw new InvalidOperationException("Longitude must be between -180 and 180.");

        _ = NormalizeContactEmail(request.ContactEmail);
        _ = NormalizeWebsite(request.Website);
        _ = NormalizePricePerNight(request.PricePerNight);

        if (ParseType(request.Type) == PlaceContributionType.UpdatePlace && !request.PlaceId.HasValue)
            throw new InvalidOperationException("PlaceId is required for update contributions.");
    }

    private static PlaceContributionType ParseType(string? type)
    {
        var normalized = (type ?? string.Empty).Trim().Replace(" ", "", StringComparison.OrdinalIgnoreCase).Replace("-", "", StringComparison.OrdinalIgnoreCase).ToLowerInvariant();
        return normalized switch
        {
            "" or "create" or "createplace" => PlaceContributionType.CreatePlace,
            "update" or "updateplace" => PlaceContributionType.UpdatePlace,
            _ => throw new InvalidOperationException("Invalid contribution type.")
        };
    }

    private static PlaceContributionStatus ParseStatus(string status)
    {
        var normalized = status.Trim().Replace(" ", "", StringComparison.OrdinalIgnoreCase).Replace("-", "", StringComparison.OrdinalIgnoreCase).ToLowerInvariant();
        return normalized switch
        {
            "pending" or "underreview" or "pendingreview" => PlaceContributionStatus.PendingReview,
            "approved" => PlaceContributionStatus.Approved,
            "rejected" => PlaceContributionStatus.Rejected,
            _ => throw new InvalidOperationException("Invalid contribution status.")
        };
    }

    private static string? NormalizeOptional(string? value, int maxLength)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
            return null;

        return trimmed.Length > maxLength ? trimmed[..maxLength] : trimmed;
    }

    private static string NormalizeContactEmail(string? email)
    {
        var trimmed = email?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
            return string.Empty;

        if (trimmed.Length > 200)
            throw new InvalidOperationException("Contact email is too long.");

        if (!Regex.IsMatch(trimmed, @"^[^@\s]+@[^@\s]+\.[^@\s]+$"))
            throw new InvalidOperationException("Contact email is invalid.");

        return trimmed.ToLowerInvariant();
    }

    private static decimal? NormalizePricePerNight(decimal? pricePerNight)
    {
        if (!pricePerNight.HasValue)
            return null;

        if (pricePerNight.Value < 0m)
            throw new InvalidOperationException("Price per night cannot be negative.");

        if (pricePerNight.Value > 100000m)
            throw new InvalidOperationException("Price per night is too large.");

        return decimal.Round(pricePerNight.Value, 2, MidpointRounding.AwayFromZero);
    }

    private static string NormalizeWebsite(string? website)
    {
        var trimmed = website?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
            return string.Empty;

        return trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            ? trimmed
            : $"https://{trimmed}";
    }

    private static string NormalizeImagePosition(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrWhiteSpace(trimmed) || trimmed.Length > 40 ? "50% 50%" : trimmed;
    }

    private static string NormalizeImageSize(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrWhiteSpace(trimmed) || trimmed.Length > 40 ? "cover" : trimmed;
    }

    private static string NormalizeReviewNote(string? value)
    {
        var trimmed = value?.Trim() ?? string.Empty;
        return trimmed.Length > 1000 ? trimmed[..1000] : trimmed;
    }

    private static List<string> NormalizeStringList(IEnumerable<string>? values, int maxItemLength, int maxCount) =>
        (values ?? Enumerable.Empty<string>())
            .Select(x => x.Trim())
            .Where(x => !string.IsNullOrWhiteSpace(x) && x.Length <= maxItemLength)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(maxCount)
            .ToList();

    private static List<string> NormalizeStayAmenityValues(IEnumerable<string> values)
    {
        return values
            .Select(value => StayAmenitySlug(value))
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string StayAmenitySlug(string value)
    {
        var slug = Slugify(value);
        return slug switch
        {
            "wifi" or "free-wifi" => "free-wifi",
            "pool" or "swimming-pool" => "swimming-pool",
            "breakfast" or "breakfast-included" => "breakfast-included",
            "parking" => "parking",
            "spa" => "spa",
            "gym" or "fitness" or "fitness-center" => "fitness-center",
            "dining" or "restaurant" => "restaurant",
            "ac" or "a-c" or "air-conditioning" => "air-conditioning",
            "sea" or "sea-view" => "sea-view",
            _ => slug
        };
    }

    private static List<ContributionBookingPartnerDto> NormalizeBookingPartners(IEnumerable<ContributionBookingPartnerDto>? partners)
    {
        return (partners ?? Enumerable.Empty<ContributionBookingPartnerDto>())
            .Where(x => !string.IsNullOrWhiteSpace(x.Id))
            .Select(x => new ContributionBookingPartnerDto
            {
                Id = x.Id.Trim().ToLowerInvariant(),
                Url = NormalizeWebsite(x.Url)
            })
            .DistinctBy(x => x.Id, StringComparer.OrdinalIgnoreCase)
            .Take(10)
            .ToList();
    }

    private static PlaceContributionListItemDto MapListItem(PlaceContribution contribution)
    {
        var mainImageUrl = MainImageUrl(contribution.Images);
        return new PlaceContributionListItemDto
        {
            Id = contribution.Id,
            Type = contribution.Type.ToString(),
            EntityType = IsAccommodationContribution(contribution) ? "Stay" : "Place",
            Name = contribution.Name,
            Subtitle = contribution.Subtitle,
            PlaceType = contribution.PlaceType?.Name,
            StayType = contribution.StayType?.Name ?? contribution.Place?.Stay?.StayType?.Name,
            Location = contribution.Location?.Name ?? contribution.Location?.Destination?.Name,
            MainImageUrl = mainImageUrl,
            CoverImageUrl = mainImageUrl,
            Status = contribution.Status.ToString(),
            SubmittedAt = contribution.SubmittedAt,
            UpdatedAt = contribution.UpdatedAt,
            PlaceId = contribution.PlaceId,
            PublishedPlaceId = contribution.PlaceId
        };
    }

    private static PlaceContributionDetailDto MapDetail(PlaceContribution contribution)
    {
        var listItem = MapListItem(contribution);
        var mainImage = MainImage(contribution.Images);
        var placeAmenities = DeserializeStringList(contribution.AmenitiesJson);
        var stayAmenities = DeserializeStringList(contribution.StayAmenitiesJson);

        return new PlaceContributionDetailDto
        {
            Id = listItem.Id,
            Type = listItem.Type,
            EntityType = listItem.EntityType,
            Name = listItem.Name,
            Subtitle = listItem.Subtitle,
            PlaceType = listItem.PlaceType,
            StayType = listItem.StayType,
            Location = listItem.Location,
            MainImageUrl = listItem.MainImageUrl,
            CoverImageUrl = listItem.CoverImageUrl,
            Status = listItem.Status,
            SubmittedAt = listItem.SubmittedAt,
            UpdatedAt = listItem.UpdatedAt,
            PlaceId = listItem.PlaceId,
            PublishedPlaceId = listItem.PublishedPlaceId,
            PlaceTypeId = contribution.PlaceTypeId,
            LocationId = contribution.LocationId,
            RestaurantTypeId = contribution.RestaurantTypeId,
            StayTypeId = contribution.StayTypeId,
            Address = contribution.Address,
            OpeningHours = contribution.OpeningHours,
            Latitude = contribution.Latitude,
            Longitude = contribution.Longitude,
            Description = contribution.Description,
            ContactEmail = contribution.ContactEmail,
            PhoneNumber = contribution.PhoneNumber,
            Phone = contribution.PhoneNumber,
            Website = contribution.Website,
            PricePerNight = contribution.PricePerNight,
            CheckInTime = contribution.CheckInTime,
            CheckOutTime = contribution.CheckOutTime,
            City = contribution.Location?.Destination?.Name,
            Region = contribution.Location?.Destination?.Region?.Name,
            Country = contribution.Location?.Destination?.Region?.Territory?.Name,
            ImagePosition = mainImage?.ImagePosition ?? "50% 50%",
            ImageSize = mainImage?.ImageSize ?? "cover",
            Tags = DeserializeStringList(contribution.TagsJson),
            Amenities = stayAmenities.Count > 0 || IsAccommodationContribution(contribution) ? stayAmenities : placeAmenities,
            StayAmenities = stayAmenities,
            BookingPartners = DeserializeBookingPartners(contribution.BookingPartnersJson),
            Images = contribution.Images
                .OrderByDescending(x => x.IsMain)
                .ThenBy(x => x.SortOrder)
                .Select(x => new PlaceContributionImageDto
                {
                    Id = x.Id,
                    Url = x.Url,
                    AltText = x.AltText,
                    Caption = x.Caption,
                    IsMain = x.IsMain,
                    SortOrder = x.SortOrder,
                    ImagePosition = x.ImagePosition,
                    ImageSize = x.ImageSize
                })
                .ToList(),
            ReviewNote = contribution.ReviewNote,
            ReviewedAt = contribution.ReviewedAt
        };
    }

    private static string? MainImageUrl(IEnumerable<PlaceContributionImage> images) =>
        images.OrderByDescending(x => x.IsMain).ThenBy(x => x.SortOrder).Select(x => x.Url).FirstOrDefault();

    private static PlaceContributionImage? MainImage(IEnumerable<PlaceContributionImage> images) =>
        images.OrderByDescending(x => x.IsMain).ThenBy(x => x.SortOrder).FirstOrDefault();

    private static bool IsAccommodationContribution(PlaceContribution contribution) =>
        contribution.StayTypeId.HasValue || contribution.StayType is not null || contribution.Place?.Stay is not null;

    private static string Serialize<T>(T value) => JsonSerializer.Serialize(value, JsonOptions);

    private static List<string> DeserializeStringList(string json)
    {
        try { return JsonSerializer.Deserialize<List<string>>(json, JsonOptions) ?? new List<string>(); }
        catch (JsonException) { return new List<string>(); }
    }

    private static List<ContributionBookingPartnerDto> DeserializeBookingPartners(string json)
    {
        try { return JsonSerializer.Deserialize<List<ContributionBookingPartnerDto>>(json, JsonOptions) ?? new List<ContributionBookingPartnerDto>(); }
        catch (JsonException) { return new List<ContributionBookingPartnerDto>(); }
    }

    private static string PartnerName(string id) => id.ToLowerInvariant() switch
    {
        "booking" => "Booking.com",
        "airbnb" => "Airbnb",
        "hotel" => "Hotel Website",
        "expedia" => "Expedia",
        _ => ToTitle(id)
    };

    private static string PartnerVariant(string id) => id.ToLowerInvariant() switch
    {
        "airbnb" => "brand",
        "hotel" => "muted",
        "expedia" => "muted",
        _ => "dark"
    };

    private static string Slugify(string value)
    {
        var slug = Regex.Replace(value.Trim().ToLowerInvariant(), @"[^a-z0-9]+", "-").Trim('-');
        return string.IsNullOrWhiteSpace(slug) ? $"tag-{Guid.NewGuid():N}" : slug;
    }

    private static string ToTitle(string value) =>
        string.Join(" ", value.Split('-', '_', StringSplitOptions.RemoveEmptyEntries)
            .Select(x => char.ToUpperInvariant(x[0]) + x[1..]));

    private sealed record ContributionReferenceResolution(
        int? PlaceTypeId,
        int? LocationId,
        int? StayTypeId,
        List<string> PlaceAmenities,
        List<string> StayAmenities);
}
