using System.Text.Json;
using Geotrack.Application.DTOs.Contributions;
using Geotrack.Application.Interfaces.Service;
using Geotrack.Domain.Entities.Core;
using Geotrack.Domain.Entities.Tags;
using Geotrack.Domain.Entities.Users;
using Microsoft.EntityFrameworkCore;

namespace Geotrack.Infrastructure.Services;

public class PlaceContributionService : IPlaceContributionService
{
    private const string MainImageCaption = "__contribution_main__";
    private const string CoverImageCaption = "__contribution_cover__";

    private readonly AppDbContext _db;
    private readonly IContributionMediaStorageService _mediaStorage;

    public PlaceContributionService(
        AppDbContext db,
        IContributionMediaStorageService mediaStorage)
    {
        _db = db;
        _mediaStorage = mediaStorage;
    }

    public async Task<GetPlaceContributionsResponseDto> GetForUserAsync(int userId)
    {
        var contributions = await BaseContributionQuery()
            .Where(c => c.UserId == userId)
            .OrderByDescending(c => c.SubmittedAt)
            .ToListAsync();

        return new GetPlaceContributionsResponseDto
        {
            Contributions = contributions.Select(MapListItem).ToList()
        };
    }

    public async Task<PlaceContributionDetailDto?> GetByIdForUserAsync(int userId, int contributionId)
    {
        var contribution = await BaseContributionQuery()
            .SingleOrDefaultAsync(c => c.Id == contributionId && c.UserId == userId);

        return contribution is null ? null : MapDetail(contribution);
    }

    public async Task<GetPlaceContributionsResponseDto> GetPendingReviewAsync(int? reviewerUserId = null)
    {
        var query = BaseContributionQuery()
            .Where(c => c.Status == PlaceContributionStatus.PendingReview);

        query = await ApplyReviewerScopeAsync(query, reviewerUserId);

        var contributions = await query
            .OrderBy(c => c.SubmittedAt)
            .ToListAsync();

        return new GetPlaceContributionsResponseDto
        {
            Contributions = contributions.Select(MapListItem).ToList()
        };
    }

    public async Task<PlaceContributionDetailDto?> GetPendingReviewByIdAsync(int contributionId, int? reviewerUserId = null)
    {
        var query = BaseContributionQuery()
            .Where(c => c.Id == contributionId && c.Status == PlaceContributionStatus.PendingReview);

        query = await ApplyReviewerScopeAsync(query, reviewerUserId);

        var contribution = await query.SingleOrDefaultAsync();

        return contribution is null ? null : MapDetail(contribution);
    }

    public async Task<PlaceContributionDetailDto> CreateAsync(
        int userId,
        UpsertPlaceContributionDto request,
        byte[]? mainImageBytes,
        byte[]? coverImageBytes,
        byte[]? ownershipProofBytes,
        string? ownershipProofOriginalFileName,
        string? ownershipProofContentType)
    {
        var now = DateTime.UtcNow;
        var type = ParseType(request.Type);

        if (type == PlaceContributionType.CreatePlace &&
            ownershipProofBytes is null or { Length: 0 })
        {
            throw new InvalidOperationException("Ownership proof document is required when creating a place contribution.");
        }

        if (type == PlaceContributionType.UpdatePlace)
        {
            if (!request.PlaceId.HasValue)
                throw new InvalidOperationException("PlaceId is required for update contributions.");

            var placeExists = await _db.Places.AnyAsync(p => p.Id == request.PlaceId.Value);
            if (!placeExists)
                throw new InvalidOperationException("The place you are trying to update does not exist.");
        }

        string? storedMainImageUrl = null;
        string? storedCoverImageUrl = null;
        string? storedOwnershipProofUrl = null;

        if (mainImageBytes is { Length: > 0 })
        {
            storedMainImageUrl = (await _mediaStorage.StoreAsync(userId, mainImageBytes)).MediaUrl;
        }

        if (coverImageBytes is { Length: > 0 })
        {
            storedCoverImageUrl = (await _mediaStorage.StoreAsync(userId, coverImageBytes)).MediaUrl;
        }

        if (ownershipProofBytes is { Length: > 0 })
        {
            storedOwnershipProofUrl = (await _mediaStorage.StoreOwnershipProofAsync(
                userId,
                ownershipProofBytes,
                ownershipProofOriginalFileName,
                ownershipProofContentType)).MediaUrl;
        }

        var contribution = new PlaceContribution
        {
            Type = type,
            Status = PlaceContributionStatus.PendingReview,
            UserId = userId,
            PlaceId = type == PlaceContributionType.UpdatePlace ? request.PlaceId : null,
            OriginalSnapshotJson = type == PlaceContributionType.UpdatePlace && request.PlaceId.HasValue
                ? await BuildOriginalSnapshotAsync(request.PlaceId.Value)
                : "{}",
            PlaceTypeId = request.PlaceTypeId,
            LocationId = request.LocationId,
            RestaurantTypeId = request.RestaurantTypeId,
            StayTypeId = request.StayTypeId,
            Name = NormalizeRequired(request.Name, "Name"),
            Subtitle = NormalizeOptional(request.Subtitle),
            Address = NormalizeOptional(request.Address),
            OpeningHours = NormalizeOptional(request.OpeningHours),
            Description = NormalizeRequired(request.Description, "Description"),
            Latitude = request.Latitude,
            Longitude = request.Longitude,
            ContactEmail = NormalizeOptional(request.ContactEmail),
            PhoneNumber = NormalizeOptional(request.PhoneNumber ?? request.Phone),
            Website = NormalizeOptional(request.Website),
            PricePerNight = request.PricePerNight,
            CheckInTime = NormalizeOptional(request.CheckInTime),
            CheckOutTime = NormalizeOptional(request.CheckOutTime),
            TagsJson = Serialize(NormalizeStringList(request.Tags)),
            AmenitiesJson = Serialize(NormalizeStringList(request.Amenities)),
            StayAmenitiesJson = Serialize(NormalizeStringList(request.StayAmenities)),
            BookingPartnersJson = Serialize(NormalizeBookingPartners(request.BookingPartners)),
            OwnershipProofDocumentUrl = storedOwnershipProofUrl,
            OwnershipProofOriginalFileName = storedOwnershipProofUrl is null
                ? null
                : NormalizeFileName(ownershipProofOriginalFileName),
            OwnershipProofContentType = storedOwnershipProofUrl is null
                ? null
                : NormalizeOptional(ownershipProofContentType),
            OwnershipProofUploadedAt = storedOwnershipProofUrl is null ? null : now,
            SubmittedAt = now,
            CreatedAt = now,
            UpdatedAt = now
        };

        SyncContributionImages(contribution, request.Images, storedMainImageUrl, storedCoverImageUrl, now);

        _db.PlaceContributions.Add(contribution);
        await _db.SaveChangesAsync();

        var saved = await BaseContributionQuery().SingleAsync(c => c.Id == contribution.Id);
        return MapDetail(saved);
    }

    public async Task<PlaceContributionDetailDto> UpdateAsync(
        int userId,
        int contributionId,
        UpsertPlaceContributionDto request,
        byte[]? mainImageBytes,
        byte[]? coverImageBytes,
        byte[]? ownershipProofBytes,
        string? ownershipProofOriginalFileName,
        string? ownershipProofContentType)
    {
        var contribution = await _db.PlaceContributions
            .Include(c => c.Images)
            .SingleOrDefaultAsync(c => c.Id == contributionId && c.UserId == userId);

        if (contribution is null)
            throw new InvalidOperationException("Contribution was not found.");

        if (contribution.Status != PlaceContributionStatus.PendingReview)
            throw new InvalidOperationException("Only pending contributions can be edited.");

        var now = DateTime.UtcNow;
        var type = ParseType(request.Type);

        if (type == PlaceContributionType.UpdatePlace)
        {
            if (!request.PlaceId.HasValue)
                throw new InvalidOperationException("PlaceId is required for update contributions.");

            var placeExists = await _db.Places.AnyAsync(p => p.Id == request.PlaceId.Value);
            if (!placeExists)
                throw new InvalidOperationException("The place you are trying to update does not exist.");
        }

        string? storedMainImageUrl = null;
        string? storedCoverImageUrl = null;

        if (mainImageBytes is { Length: > 0 })
        {
            storedMainImageUrl = (await _mediaStorage.StoreAsync(userId, mainImageBytes)).MediaUrl;
        }

        if (coverImageBytes is { Length: > 0 })
        {
            storedCoverImageUrl = (await _mediaStorage.StoreAsync(userId, coverImageBytes)).MediaUrl;
        }

        if (ownershipProofBytes is { Length: > 0 })
        {
            if (!string.IsNullOrWhiteSpace(contribution.OwnershipProofDocumentUrl))
                await _mediaStorage.DeleteAsync(contribution.OwnershipProofDocumentUrl);

            var storedOwnershipProof = await _mediaStorage.StoreOwnershipProofAsync(
                userId,
                ownershipProofBytes,
                ownershipProofOriginalFileName,
                ownershipProofContentType);

            contribution.OwnershipProofDocumentUrl = storedOwnershipProof.MediaUrl;
            contribution.OwnershipProofOriginalFileName = NormalizeFileName(ownershipProofOriginalFileName);
            contribution.OwnershipProofContentType = NormalizeOptional(ownershipProofContentType);
            contribution.OwnershipProofUploadedAt = now;
        }

        contribution.Type = type;
        contribution.PlaceId = type == PlaceContributionType.UpdatePlace ? request.PlaceId : null;
        contribution.OriginalSnapshotJson = type == PlaceContributionType.UpdatePlace && request.PlaceId.HasValue
            ? await BuildOriginalSnapshotAsync(request.PlaceId.Value)
            : "{}";
        contribution.PlaceTypeId = request.PlaceTypeId;
        contribution.LocationId = request.LocationId;
        contribution.RestaurantTypeId = request.RestaurantTypeId;
        contribution.StayTypeId = request.StayTypeId;
        contribution.Name = NormalizeRequired(request.Name, "Name");
        contribution.Subtitle = NormalizeOptional(request.Subtitle);
        contribution.Address = NormalizeOptional(request.Address);
        contribution.OpeningHours = NormalizeOptional(request.OpeningHours);
        contribution.Description = NormalizeRequired(request.Description, "Description");
        contribution.Latitude = request.Latitude;
        contribution.Longitude = request.Longitude;
        contribution.ContactEmail = NormalizeOptional(request.ContactEmail);
        contribution.PhoneNumber = NormalizeOptional(request.PhoneNumber ?? request.Phone);
        contribution.Website = NormalizeOptional(request.Website);
        contribution.PricePerNight = request.PricePerNight;
        contribution.CheckInTime = NormalizeOptional(request.CheckInTime);
        contribution.CheckOutTime = NormalizeOptional(request.CheckOutTime);
        contribution.TagsJson = Serialize(NormalizeStringList(request.Tags));
        contribution.AmenitiesJson = Serialize(NormalizeStringList(request.Amenities));
        contribution.StayAmenitiesJson = Serialize(NormalizeStringList(request.StayAmenities));
        contribution.BookingPartnersJson = Serialize(NormalizeBookingPartners(request.BookingPartners));
        contribution.UpdatedAt = now;

        SyncContributionImages(contribution, request.Images, storedMainImageUrl, storedCoverImageUrl, now);

        await _db.SaveChangesAsync();

        var saved = await BaseContributionQuery().SingleAsync(c => c.Id == contribution.Id);
        return MapDetail(saved);
    }

    public async Task DeleteAsync(int userId, int contributionId)
    {
        var contribution = await _db.PlaceContributions
            .Include(c => c.Images)
            .SingleOrDefaultAsync(c => c.Id == contributionId && c.UserId == userId);

        if (contribution is null)
            throw new InvalidOperationException("Contribution was not found.");

        if (contribution.Status != PlaceContributionStatus.PendingReview)
            throw new InvalidOperationException("Only pending contributions can be deleted.");

        foreach (var image in contribution.Images)
        {
            if (_mediaStorage.IsManagedContributionMediaUrl(image.Url))
                await _mediaStorage.DeleteAsync(image.Url);
        }

        if (!string.IsNullOrWhiteSpace(contribution.OwnershipProofDocumentUrl))
            await _mediaStorage.DeleteAsync(contribution.OwnershipProofDocumentUrl);

        _db.PlaceContributions.Remove(contribution);
        await _db.SaveChangesAsync();
    }

    public async Task<PlaceContributionDetailDto> ApproveAsync(int contributionId, int reviewerUserId, string? note)
    {
        var contribution = await _db.PlaceContributions
            .Include(c => c.Images)
            .SingleOrDefaultAsync(c => c.Id == contributionId);

        if (contribution is null)
            throw new InvalidOperationException("Contribution was not found.");

        if (contribution.Status != PlaceContributionStatus.PendingReview)
            throw new InvalidOperationException("Only pending contributions can be approved.");

        var now = DateTime.UtcNow;

        Place publishedPlace;

        if (contribution.Type == PlaceContributionType.CreatePlace)
        {
            publishedPlace = await PublishNewPlaceAsync(contribution, now);
            contribution.PlaceId = publishedPlace.Id;
        }
        else
        {
            if (!contribution.PlaceId.HasValue)
                throw new InvalidOperationException("Update contribution is missing target place.");

            publishedPlace = await ApplyUpdateToExistingPlaceAsync(contribution, contribution.PlaceId.Value, now);
        }

        contribution.Status = PlaceContributionStatus.Approved;
        contribution.ReviewedByUserId = reviewerUserId;
        contribution.ReviewedAt = now;
        contribution.ReviewNote = NormalizeOptional(note);
        contribution.UpdatedAt = now;

        await _db.SaveChangesAsync();

        var saved = await BaseContributionQuery().SingleAsync(c => c.Id == contribution.Id);
        return MapDetail(saved);
    }

    public async Task<PlaceContributionDetailDto> RejectAsync(int contributionId, int reviewerUserId, string? note)
    {
        var contribution = await _db.PlaceContributions
            .SingleOrDefaultAsync(c => c.Id == contributionId);

        if (contribution is null)
            throw new InvalidOperationException("Contribution was not found.");

        if (contribution.Status != PlaceContributionStatus.PendingReview)
            throw new InvalidOperationException("Only pending contributions can be rejected.");

        var now = DateTime.UtcNow;

        contribution.Status = PlaceContributionStatus.Rejected;
        contribution.ReviewedByUserId = reviewerUserId;
        contribution.ReviewedAt = now;
        contribution.ReviewNote = NormalizeOptional(note);
        contribution.UpdatedAt = now;

        await _db.SaveChangesAsync();

        var saved = await BaseContributionQuery().SingleAsync(c => c.Id == contribution.Id);
        return MapDetail(saved);
    }

    private IQueryable<PlaceContribution> BaseContributionQuery()
    {
        return _db.PlaceContributions
            .AsNoTracking()
            .Include(c => c.PlaceType)
            .Include(c => c.Location)
                .ThenInclude(l => l!.Destination)
                    .ThenInclude(d => d.Region)
            .Include(c => c.StayType)
            .Include(c => c.Images)
            .Include(c => c.Place);
    }

    private async Task<IQueryable<PlaceContribution>> ApplyReviewerScopeAsync(
        IQueryable<PlaceContribution> query,
        int? reviewerUserId)
    {
        if (!reviewerUserId.HasValue)
            return query;

        var managesAll = await _db.Users
            .AsNoTracking()
            .Where(u => u.Id == reviewerUserId.Value)
            .Select(u => u.UserRoles.Any(ur =>
                ur.Role.Name == "Admin" ||
                ur.Role.Name == "SuperAdmin"))
            .SingleOrDefaultAsync();

        if (managesAll)
            return query;

        var regionIds = await _db.RegionalManagerRegions
            .AsNoTracking()
            .Where(r => r.UserId == reviewerUserId.Value)
            .Select(r => r.RegionId)
            .ToListAsync();

        if (regionIds.Count == 0)
            return query.Where(c => false);

        return query.Where(c =>
            c.Location != null &&
            c.Location.Destination != null &&
            regionIds.Contains(c.Location.Destination.RegionId));
    }

    private async Task<string> BuildOriginalSnapshotAsync(int placeId)
    {
        var place = await _db.Places
            .AsNoTracking()
            .Include(p => p.Stay)
            .Include(p => p.Images)
            .Include(p => p.PlaceTags)
                .ThenInclude(pt => pt.Tag)
            .Include(p => p.PlaceAmenities)
                .ThenInclude(pa => pa.Amenity)
            .Include(p => p.PlaceBookingPartners)
                .ThenInclude(pb => pb.BookingPartner)
            .SingleOrDefaultAsync(p => p.Id == placeId);

        if (place is null)
            return "{}";

        var snapshot = new
        {
            place.Id,
            place.Name,
            place.Subtitle,
            place.Address,
            place.OpeningHours,
            place.Description,
            place.Latitude,
            place.Longitude,
            place.ContactEmail,
            PhoneNumber = place.Number,
            place.PlaceTypeId,
            place.LocationId,
            StayTypeId = place.Stay?.StayTypeId,
            place.Stay?.PricePerNight,
            place.Stay?.CheckInTime,
            place.Stay?.CheckOutTime,
            Tags = place.PlaceTags.Select(pt => pt.Tag.Name).ToList(),
            Amenities = place.PlaceAmenities.Select(pa => pa.Amenity.Name).ToList(),
            BookingPartners = place.PlaceBookingPartners.Select(pb => new
            {
                pb.BookingPartnerId,
                pb.Url
            }).ToList(),
            Images = place.Images.Select(image => new
            {
                image.Url,
                image.AltText,
                image.Caption,
                image.IsMain,
                image.SortOrder
            }).ToList()
        };

        return JsonSerializer.Serialize(snapshot);
    }

    private async Task<Place> PublishNewPlaceAsync(PlaceContribution contribution, DateTime now)
    {
        if (!contribution.PlaceTypeId.HasValue)
            throw new InvalidOperationException("Place type is required.");

        if (!contribution.LocationId.HasValue)
            throw new InvalidOperationException("Location is required.");

        if (!contribution.Latitude.HasValue || !contribution.Longitude.HasValue)
            throw new InvalidOperationException("Coordinates are required.");

        var place = new Place
        {
            Name = contribution.Name,
            Subtitle = contribution.Subtitle,
            Address = contribution.Address,
            OpeningHours = contribution.OpeningHours,
            Description = contribution.Description,
            Latitude = contribution.Latitude.Value,
            Longitude = contribution.Longitude.Value,
            ContactEmail = contribution.ContactEmail,
            Number = contribution.PhoneNumber,
            PlaceTypeId = contribution.PlaceTypeId.Value,
            LocationId = contribution.LocationId.Value,
            CreatedAt = now,
            UpdatedAt = now
        };

        _db.Places.Add(place);
        await _db.SaveChangesAsync();

        await SyncPublishedPlaceRelationsAsync(place, contribution, now);
        await SyncPublishedPlaceImagesAsync(place, contribution.Images, now);

        return place;
    }

    private async Task<Place> ApplyUpdateToExistingPlaceAsync(PlaceContribution contribution, int placeId, DateTime now)
    {
        var place = await _db.Places
            .Include(p => p.Stay)
            .Include(p => p.Images)
            .Include(p => p.PlaceTags)
            .Include(p => p.PlaceAmenities)
            .Include(p => p.PlaceBookingPartners)
            .SingleOrDefaultAsync(p => p.Id == placeId);

        if (place is null)
            throw new InvalidOperationException("The place you are trying to update does not exist.");

        if (!contribution.PlaceTypeId.HasValue)
            throw new InvalidOperationException("Place type is required.");

        if (!contribution.LocationId.HasValue)
            throw new InvalidOperationException("Location is required.");

        if (!contribution.Latitude.HasValue || !contribution.Longitude.HasValue)
            throw new InvalidOperationException("Coordinates are required.");

        place.Name = contribution.Name;
        place.Subtitle = contribution.Subtitle;
        place.Address = contribution.Address;
        place.OpeningHours = contribution.OpeningHours;
        place.Description = contribution.Description;
        place.Latitude = contribution.Latitude.Value;
        place.Longitude = contribution.Longitude.Value;
        place.ContactEmail = contribution.ContactEmail;
        place.Number = contribution.PhoneNumber;
        place.PlaceTypeId = contribution.PlaceTypeId.Value;
        place.LocationId = contribution.LocationId.Value;
        place.UpdatedAt = now;

        await SyncPublishedPlaceRelationsAsync(place, contribution, now);
        await SyncPublishedPlaceImagesAsync(place, contribution.Images, now);

        return place;
    }

    private async Task SyncPublishedPlaceRelationsAsync(Place place, PlaceContribution contribution, DateTime now)
    {
        var tagNames = DeserializeStringList(contribution.TagsJson);
        var amenityNames = DeserializeStringList(contribution.AmenitiesJson);
        var stayAmenityNames = DeserializeStringList(contribution.StayAmenitiesJson);
        var bookingPartners = DeserializeBookingPartners(contribution.BookingPartnersJson);

        var existingPlaceTags = await _db.PlaceTags
            .Where(pt => pt.PlaceId == place.Id)
            .ToListAsync();

        _db.PlaceTags.RemoveRange(existingPlaceTags);

        var tags = await ResolveTagsAsync(tagNames);
        foreach (var tag in tags)
        {
            _db.PlaceTags.Add(new PlaceTag
            {
                PlaceId = place.Id,
                TagId = tag.Id
            });
        }

        var existingAmenities = await _db.PlaceAmenities
            .Where(pa => pa.PlaceId == place.Id)
            .ToListAsync();

        _db.PlaceAmenities.RemoveRange(existingAmenities);

        var amenities = await ResolveAmenitiesAsync(amenityNames);
        foreach (var amenity in amenities)
        {
            _db.PlaceAmenities.Add(new PlaceAmenity
            {
                PlaceId = place.Id,
                AmenityId = amenity.Id
            });
        }

        var existingBookingPartners = await _db.PlaceBookingPartners
            .Where(pb => pb.PlaceId == place.Id)
            .ToListAsync();

        _db.PlaceBookingPartners.RemoveRange(existingBookingPartners);

        var resolvedBookingPartners = await ResolveBookingPartnersAsync(bookingPartners);
        foreach (var bookingPartner in resolvedBookingPartners)
        {
            _db.PlaceBookingPartners.Add(new PlaceBookingPartner
            {
                PlaceId = place.Id,
                BookingPartnerId = bookingPartner.BookingPartnerId,
                Url = bookingPartner.Url
            });
        }

        if (contribution.StayTypeId.HasValue)
        {
            if (place.Stay is null)
            {
                place.Stay = new Stay
                {
                    PlaceId = place.Id,
                    CreatedAt = now
                };
            }

            place.Stay.StayTypeId = contribution.StayTypeId.Value;
            place.Stay.PricePerNight = contribution.PricePerNight;
            place.Stay.CheckInTime = contribution.CheckInTime;
            place.Stay.CheckOutTime = contribution.CheckOutTime;
            place.Stay.UpdatedAt = now;

            await SyncStayAmenitiesAsync(place.Id, stayAmenityNames);
        }
        else if (place.Stay is not null)
        {
            var existingStayAmenities = await _db.StayStayAmenities
                .Where(sa => sa.StayPlaceId == place.Id)
                .ToListAsync();

            _db.StayStayAmenities.RemoveRange(existingStayAmenities);
            _db.Stays.Remove(place.Stay);
        }
    }

    private async Task SyncStayAmenitiesAsync(int stayPlaceId, List<string> stayAmenityNames)
    {
        var existingStayAmenities = await _db.StayStayAmenities
            .Where(sa => sa.StayPlaceId == stayPlaceId)
            .ToListAsync();

        _db.StayStayAmenities.RemoveRange(existingStayAmenities);

        var stayAmenities = await ResolveStayAmenitiesAsync(stayAmenityNames);
        foreach (var stayAmenity in stayAmenities)
        {
            _db.StayStayAmenities.Add(new StayStayAmenity
            {
                StayPlaceId = stayPlaceId,
                StayAmenityId = stayAmenity.Id
            });
        }
    }

    private async Task SyncPublishedPlaceImagesAsync(
        Place place,
        IEnumerable<PlaceContributionImage> contributionImages,
        DateTime now)
    {
        var existingImages = await _db.EntityImages
            .Where(image => image.PlaceId == place.Id)
            .ToListAsync();

        _db.EntityImages.RemoveRange(existingImages);

        foreach (var image in contributionImages.OrderBy(i => i.SortOrder).ThenBy(i => i.Id))
        {
            _db.EntityImages.Add(new EntityImage
            {
                TargetType = EntityImageTargetType.Place,
                PlaceId = place.Id,
                Url = image.Url,
                AltText = image.AltText,
                Caption = image.Caption,
                IsMain = image.IsMain,
                SortOrder = image.SortOrder,
                CreatedAt = now,
                UpdatedAt = now
            });
        }
    }

    private void SyncContributionImages(
        PlaceContribution contribution,
        List<UpsertPlaceContributionImageDto>? requestImages,
        string? storedMainImageUrl,
        string? storedCoverImageUrl,
        DateTime now)
    {
        var oldManagedUrls = contribution.Images
            .Where(image => _mediaStorage.IsManagedContributionMediaUrl(image.Url))
            .Select(image => image.Url)
            .ToList();

        contribution.Images.Clear();

        var imageDtos = new List<UpsertPlaceContributionImageDto>();

        if (requestImages is not null)
        {
            imageDtos.AddRange(requestImages.Where(image => !string.IsNullOrWhiteSpace(image.Url)));
        }

        if (!string.IsNullOrWhiteSpace(storedCoverImageUrl))
        {
            imageDtos.Add(new UpsertPlaceContributionImageDto
            {
                Url = storedCoverImageUrl,
                Caption = CoverImageCaption,
                IsMain = false,
                SortOrder = -1,
                ImagePosition = "50% 50%",
                ImageSize = "cover"
            });
        }

        if (!string.IsNullOrWhiteSpace(storedMainImageUrl))
        {
            imageDtos.Add(new UpsertPlaceContributionImageDto
            {
                Url = storedMainImageUrl,
                Caption = MainImageCaption,
                IsMain = true,
                SortOrder = 0,
                ImagePosition = "50% 50%",
                ImageSize = "cover"
            });
        }

        if (imageDtos.Count == 0)
            return;

        var hasMain = imageDtos.Any(image => image.IsMain);
        if (!hasMain)
        {
            imageDtos[0].IsMain = true;
        }

        foreach (var image in imageDtos
                     .OrderBy(image => image.SortOrder)
                     .ThenBy(image => image.Url, StringComparer.OrdinalIgnoreCase))
        {
            contribution.Images.Add(new PlaceContributionImage
            {
                Url = NormalizeRequired(image.Url, "Image url"),
                AltText = NormalizeOptional(image.AltText),
                Caption = NormalizeOptional(image.Caption),
                IsMain = image.IsMain,
                SortOrder = image.SortOrder,
                ImagePosition = NormalizeOptional(image.ImagePosition) ?? "50% 50%",
                ImageSize = NormalizeOptional(image.ImageSize) ?? "cover",
                CreatedAt = now,
                UpdatedAt = now
            });
        }

        foreach (var oldManagedUrl in oldManagedUrls)
        {
            if (contribution.Images.All(image => !string.Equals(image.Url, oldManagedUrl, StringComparison.OrdinalIgnoreCase)))
            {
                _ = _mediaStorage.DeleteAsync(oldManagedUrl);
            }
        }
    }

    private async Task<List<Tag>> ResolveTagsAsync(List<string> tagNames)
    {
        if (tagNames.Count == 0)
            return new List<Tag>();

        var normalized = tagNames.Select(NormalizeSlug).ToList();

        return await _db.Tags
            .Where(tag => normalized.Contains(tag.Slug.ToLower()))
            .ToListAsync();
    }

    private async Task<List<Amenity>> ResolveAmenitiesAsync(List<string> amenityNames)
    {
        if (amenityNames.Count == 0)
            return new List<Amenity>();

        var normalized = amenityNames.Select(NormalizeSlug).ToList();

        return await _db.Amenities
            .Where(amenity => normalized.Contains(amenity.Name.ToLower()))
            .ToListAsync();
    }

    private async Task<List<StayAmenity>> ResolveStayAmenitiesAsync(List<string> stayAmenityNames)
    {
        if (stayAmenityNames.Count == 0)
            return new List<StayAmenity>();

        var normalized = stayAmenityNames.Select(NormalizeSlug).ToList();

        return await _db.StayAmenities
            .Where(amenity => normalized.Contains(amenity.Slug.ToLower()) ||
                              normalized.Contains(amenity.Name.ToLower()))
            .ToListAsync();
    }

    private async Task<List<ResolvedBookingPartner>> ResolveBookingPartnersAsync(
        List<ContributionBookingPartnerDto> bookingPartners)
    {
        if (bookingPartners.Count == 0)
            return new List<ResolvedBookingPartner>();

        var normalizedIds = bookingPartners
            .Select(partner => NormalizeOptional(partner.Id))
            .Where(value => value is not null)
            .Select(value => value!)
            .ToList();

        var knownPartners = await _db.BookingPartners
            .Where(partner => normalizedIds.Contains(partner.Id.ToString()) ||
                              normalizedIds.Contains(partner.Name.ToLower()))
            .ToListAsync();

        return bookingPartners
            .Select(partner =>
            {
                var normalizedId = NormalizeOptional(partner.Id);
                if (normalizedId is null)
                    return null;

                var resolved = knownPartners.FirstOrDefault(known =>
                    string.Equals(known.Id.ToString(), normalizedId, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(known.Name, normalizedId, StringComparison.OrdinalIgnoreCase));

                return resolved is null
                    ? null
                    : new ResolvedBookingPartner(resolved.Id, NormalizeOptional(partner.Url));
            })
            .Where(value => value is not null)
            .Select(value => value!)
            .ToList();
    }

    private static PlaceContributionListItemDto MapListItem(PlaceContribution contribution)
    {
        var mainImage = GetMainImage(contribution);
        var coverImage = GetCoverImage(contribution);

        return new PlaceContributionListItemDto
        {
            Id = contribution.Id,
            Type = contribution.Type.ToString(),
            EntityType = "Place",
            Name = contribution.Name,
            Subtitle = contribution.Subtitle,
            PlaceType = contribution.PlaceType?.Name,
            StayType = contribution.StayType?.Name,
            Location = BuildLocationLabel(contribution),
            MainImageUrl = mainImage?.Url,
            CoverImageUrl = coverImage?.Url,
            Status = contribution.Status.ToString(),
            SubmittedAt = contribution.SubmittedAt,
            UpdatedAt = contribution.UpdatedAt,
            PlaceId = contribution.PlaceId,
            PublishedPlaceId = contribution.PlaceId,
            HasOwnershipProofDocument = !string.IsNullOrWhiteSpace(contribution.OwnershipProofDocumentUrl)
        };
    }

    private static PlaceContributionDetailDto MapDetail(PlaceContribution contribution)
    {
        var listItem = MapListItem(contribution);

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
            HasOwnershipProofDocument = listItem.HasOwnershipProofDocument,
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
            City = contribution.Location?.Name,
            Region = contribution.Location?.Destination?.Region?.Name,
            Country = contribution.Location?.Destination?.Name,
            ImagePosition = GetMainImage(contribution)?.ImagePosition ?? "50% 50%",
            ImageSize = GetMainImage(contribution)?.ImageSize ?? "cover",
            Tags = DeserializeStringList(contribution.TagsJson),
            Amenities = DeserializeStringList(contribution.AmenitiesJson),
            StayAmenities = DeserializeStringList(contribution.StayAmenitiesJson),
            BookingPartners = DeserializeBookingPartners(contribution.BookingPartnersJson),
            Images = contribution.Images
                .OrderBy(image => image.SortOrder)
                .ThenBy(image => image.Id)
                .Select(image => new PlaceContributionImageDto
                {
                    Id = image.Id,
                    Url = image.Url,
                    AltText = image.AltText,
                    Caption = image.Caption,
                    IsMain = image.IsMain,
                    SortOrder = image.SortOrder,
                    ImagePosition = image.ImagePosition,
                    ImageSize = image.ImageSize
                })
                .ToList(),
            ReviewNote = contribution.ReviewNote,
            ReviewedAt = contribution.ReviewedAt,
            OwnershipProofDocumentUrl = contribution.OwnershipProofDocumentUrl,
            OwnershipProofOriginalFileName = contribution.OwnershipProofOriginalFileName,
            OwnershipProofContentType = contribution.OwnershipProofContentType,
            OwnershipProofUploadedAt = contribution.OwnershipProofUploadedAt
        };
    }

    private static PlaceContributionImage? GetMainImage(PlaceContribution contribution)
    {
        return contribution.Images
            .OrderByDescending(image => image.IsMain)
            .ThenBy(image => image.SortOrder)
            .ThenBy(image => image.Id)
            .FirstOrDefault(image => !string.Equals(image.Caption, CoverImageCaption, StringComparison.Ordinal));
    }

    private static PlaceContributionImage? GetCoverImage(PlaceContribution contribution)
    {
        return contribution.Images
            .OrderBy(image => image.SortOrder)
            .ThenBy(image => image.Id)
            .FirstOrDefault(image => string.Equals(image.Caption, CoverImageCaption, StringComparison.Ordinal));
    }

    private static string? BuildLocationLabel(PlaceContribution contribution)
    {
        if (contribution.Location is null)
            return null;

        var parts = new[]
        {
            contribution.Location.Name,
            contribution.Location.Destination?.Name,
            contribution.Location.Destination?.Region?.Name
        };

        return string.Join(", ", parts.Where(part => !string.IsNullOrWhiteSpace(part)));
    }

    private static PlaceContributionType ParseType(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return PlaceContributionType.CreatePlace;

        if (Enum.TryParse<PlaceContributionType>(value, ignoreCase: true, out var parsed))
            return parsed;

        throw new InvalidOperationException("Unsupported contribution type.");
    }

    private static string NormalizeRequired(string? value, string fieldName)
    {
        var normalized = NormalizeOptional(value);
        if (string.IsNullOrWhiteSpace(normalized))
            throw new InvalidOperationException($"{fieldName} is required.");

        return normalized;
    }

    private static string? NormalizeOptional(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
    }

    private static string? NormalizeFileName(string? value)
    {
        var normalized = NormalizeOptional(value);
        if (normalized is null)
            return null;

        var fileName = Path.GetFileName(normalized);
        return fileName.Length > 255 ? fileName[..255] : fileName;
    }

    private static List<string> NormalizeStringList(IEnumerable<string>? values)
    {
        if (values is null)
            return new List<string>();

        return values
            .Select(NormalizeOptional)
            .Where(value => value is not null)
            .Select(value => value!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static List<ContributionBookingPartnerDto> NormalizeBookingPartners(
        IEnumerable<ContributionBookingPartnerDto>? values)
    {
        if (values is null)
            return new List<ContributionBookingPartnerDto>();

        return values
            .Select(value => new ContributionBookingPartnerDto
            {
                Id = NormalizeOptional(value.Id) ?? string.Empty,
                Url = NormalizeOptional(value.Url) ?? string.Empty
            })
            .Where(value => !string.IsNullOrWhiteSpace(value.Id))
            .ToList();
    }

    private static string Serialize<T>(T value)
    {
        return JsonSerializer.Serialize(value);
    }

    private static List<string> DeserializeStringList(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return new List<string>();

        try
        {
            return JsonSerializer.Deserialize<List<string>>(json) ?? new List<string>();
        }
        catch (JsonException)
        {
            return new List<string>();
        }
    }

    private static List<ContributionBookingPartnerDto> DeserializeBookingPartners(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return new List<ContributionBookingPartnerDto>();

        try
        {
            return JsonSerializer.Deserialize<List<ContributionBookingPartnerDto>>(json)
                   ?? new List<ContributionBookingPartnerDto>();
        }
        catch (JsonException)
        {
            return new List<ContributionBookingPartnerDto>();
        }
    }

    private static string NormalizeSlug(string value)
    {
        return value.Trim().ToLowerInvariant();
    }

    private sealed record ResolvedBookingPartner(int BookingPartnerId, string? Url);
}