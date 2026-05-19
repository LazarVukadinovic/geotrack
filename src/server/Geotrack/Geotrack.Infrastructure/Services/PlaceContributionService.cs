using System.Text.Json;
using System.Text.RegularExpressions;
using Geotrack.Application.DTOs.Contributions;
using Geotrack.Application.Interfaces.Service;
using Geotrack.Application.Security;
using Geotrack.Domain.Entities.Core;
using Geotrack.Domain.Entities.Tags;
using Geotrack.Domain.Entities.Users;
using Microsoft.EntityFrameworkCore;

namespace Geotrack.Infrastructure.Services;

public class PlaceContributionService : IPlaceContributionService
{
    private const string CardImageCaption = "__contribution_card__";
    private const string CoverImageCaption = "__contribution_cover__";
    private const int MaxContributionImages = 20;
    private const int MaxWebsiteUrlLength = 500;
    private const int MaxBookingPartnerUrlLength = 500;
    private const int MaxImageUrlLength = 1000;
    private const int MaxBookingPartnerIdLength = 80;
    private const double MaxContributionLocationMatchDistanceKm = 50d;
    private const double MaxContributionDestinationMatchDistanceKm = 75d;
    private const string ManagedContributionMediaPrefix = "/media/uploads/contributions/";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly HashSet<string> LegacyOwnershipProofRequiredPlaceTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "stay",
        "restaurant",
        "cafe"
    };
    private static readonly Regex ManagedContributionMediaFileNameRegex = new(
        @"^[a-z0-9][a-z0-9._-]{0,199}\.(png|jpe?g|gif|webp)$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

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

    public async Task<List<ContributionPlaceTypeDto>> GetPlaceTypesAsync()
    {
        return await _db.PlaceTypes
            .AsNoTracking()
            .OrderBy(type => type.Name)
            .ThenBy(type => type.Id)
            .Select(type => new ContributionPlaceTypeDto
            {
                Id = type.Id,
                Name = type.Name,
                Description = type.Description,
                IsOwnable = type.IsOwnable
            })
            .ToListAsync();
    }

    public async Task<PlaceContributionDetailDto> CreateAsync(
        int userId,
        UpsertPlaceContributionDto request,
        byte[]? cardImageBytes,
        byte[]? coverImageBytes,
        IReadOnlyList<byte[]>? galleryImageBytes,
        byte[]? ownershipProofBytes,
        string? ownershipProofOriginalFileName,
        string? ownershipProofContentType)
    {
        var now = DateTime.UtcNow;
        var type = ParseType(request.Type);

        Place? targetPlace = null;
        ContributionReferenceResolution? references = null;

        var requiresOwnershipProof = await RequiresOwnershipProofAsync(type, request);
        if (requiresOwnershipProof &&
            ownershipProofBytes is null or { Length: 0 })
        {
            throw new InvalidOperationException("Ownership proof document is required.");
        }

        if (type == PlaceContributionType.ClaimOwnership)
        {
            if (!request.PlaceId.HasValue)
                throw new InvalidOperationException("PlaceId is required for ownership claims.");

            targetPlace = await _db.Places
                .AsNoTracking()
                .Include(p => p.Stay)
                .SingleOrDefaultAsync(p => p.Id == request.PlaceId.Value);

            if (targetPlace is null)
                throw new KeyNotFoundException("Target place not found.");

            var alreadyOwner = await UserCanManagePlaceAsync(userId, request.PlaceId.Value);
            if (alreadyOwner)
                throw new InvalidOperationException("You already own this place.");
        }
        else
        {
            Validate(request);
            references = await ResolveContributionReferencesAsync(request);
        }

        string? storedCardImageUrl = null;
        string? storedCoverImageUrl = null;
        var storedGalleryImageUrls = new List<string>();
        string? storedOwnershipProofUrl = null;

        if (cardImageBytes is { Length: > 0 })
        {
            storedCardImageUrl = (await _mediaStorage.StoreAsync(userId, cardImageBytes)).MediaUrl;
        }

        if (coverImageBytes is { Length: > 0 })
        {
            storedCoverImageUrl = (await _mediaStorage.StoreAsync(userId, coverImageBytes)).MediaUrl;
        }

        if (galleryImageBytes is not null)
        {
            foreach (var galleryBytes in galleryImageBytes.Where(bytes => bytes is { Length: > 0 }))
            {
                storedGalleryImageUrls.Add((await _mediaStorage.StoreAsync(userId, galleryBytes)).MediaUrl);
            }
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
            PlaceId = type is PlaceContributionType.UpdatePlace or PlaceContributionType.ClaimOwnership
                ? request.PlaceId
                : null,
            OriginalSnapshotJson = type is PlaceContributionType.UpdatePlace or PlaceContributionType.ClaimOwnership && request.PlaceId.HasValue
                ? await BuildOriginalSnapshotAsync(request.PlaceId.Value)
                : "{}",
            PlaceTypeId = type == PlaceContributionType.ClaimOwnership ? targetPlace!.PlaceTypeId : references!.PlaceTypeId,
            LocationId = type == PlaceContributionType.ClaimOwnership ? targetPlace!.LocationId : references!.LocationId,
            RestaurantTypeId = request.RestaurantTypeId,
            StayTypeId = type == PlaceContributionType.ClaimOwnership ? targetPlace!.Stay?.StayTypeId : references!.StayTypeId,
            Name = type == PlaceContributionType.ClaimOwnership
                ? targetPlace!.Name
                : NormalizeRequired(request.Name, "Name"),
            Subtitle = type == PlaceContributionType.ClaimOwnership
                ? targetPlace!.Subtitle
                : NormalizeOptional(request.Subtitle),
            Address = type == PlaceContributionType.ClaimOwnership
                ? targetPlace!.Address
                : NormalizeOptional(request.Address),
            OpeningHours = type == PlaceContributionType.ClaimOwnership
                ? targetPlace!.OpeningHours
                : NormalizeOptional(request.OpeningHours),
            Description = type == PlaceContributionType.ClaimOwnership
                ? targetPlace!.Description
                : NormalizeDescription(request.Description),
            Latitude = type == PlaceContributionType.ClaimOwnership ? targetPlace!.Latitude : references!.Latitude,
            Longitude = type == PlaceContributionType.ClaimOwnership ? targetPlace!.Longitude : references!.Longitude,
            ContactEmail = type == PlaceContributionType.ClaimOwnership
                ? targetPlace!.ContactEmail
                : NormalizeContactEmail(request.ContactEmail),
            PhoneNumber = type == PlaceContributionType.ClaimOwnership
                ? targetPlace!.Number
                : NormalizeOptional(request.PhoneNumber ?? request.Phone),
            Website = NormalizeWebsite(request.Website),
            PricePerNight = type == PlaceContributionType.ClaimOwnership
                ? targetPlace!.Stay?.PricePerNight
                : NormalizePricePerNight(request.PricePerNight),
            CheckInTime = type == PlaceContributionType.ClaimOwnership
                ? targetPlace!.Stay?.CheckInTime
                : NormalizeOptional(request.CheckInTime),
            CheckOutTime = type == PlaceContributionType.ClaimOwnership
                ? targetPlace!.Stay?.CheckOutTime
                : NormalizeOptional(request.CheckOutTime),
            TagsJson = Serialize(NormalizeStringList(request.Tags, 80, 30)),
            AmenitiesJson = Serialize(type == PlaceContributionType.ClaimOwnership ? new List<string>() : references!.PlaceAmenities),
            StayAmenitiesJson = Serialize(type == PlaceContributionType.ClaimOwnership ? new List<string>() : references!.StayAmenities),
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

        SyncContributionImages(contribution, request.Images, storedCardImageUrl, storedCoverImageUrl, storedGalleryImageUrls, now, type != PlaceContributionType.ClaimOwnership);

        _db.PlaceContributions.Add(contribution);
        await _db.SaveChangesAsync();

        var saved = await BaseContributionQuery().SingleAsync(c => c.Id == contribution.Id);
        return MapDetail(saved);
    }

    public async Task<PlaceContributionDetailDto> UpdateAsync(
        int userId,
        int contributionId,
        UpsertPlaceContributionDto request,
        byte[]? cardImageBytes,
        byte[]? coverImageBytes,
        IReadOnlyList<byte[]>? galleryImageBytes,
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

        Place? targetPlace = null;
        ContributionReferenceResolution? references = null;

        var requiresOwnershipProof = await RequiresOwnershipProofAsync(type, request);
        if (requiresOwnershipProof &&
            string.IsNullOrWhiteSpace(contribution.OwnershipProofDocumentUrl) &&
            ownershipProofBytes is null or { Length: 0 })
        {
            throw new InvalidOperationException("Ownership proof document is required.");
        }

        if (type == PlaceContributionType.ClaimOwnership)
        {
            if (!request.PlaceId.HasValue)
                throw new InvalidOperationException("PlaceId is required for ownership claims.");

            targetPlace = await _db.Places
                .AsNoTracking()
                .Include(p => p.Stay)
                .SingleOrDefaultAsync(p => p.Id == request.PlaceId.Value);

            if (targetPlace is null)
                throw new KeyNotFoundException("Target place not found.");

            var alreadyOwner = await UserCanManagePlaceAsync(userId, request.PlaceId.Value);
            if (alreadyOwner)
                throw new InvalidOperationException("You already own this place.");
        }
        else
        {
            Validate(request);
            references = await ResolveContributionReferencesAsync(request);
        }

        string? storedCardImageUrl = null;
        string? storedCoverImageUrl = null;
        var storedGalleryImageUrls = new List<string>();

        if (cardImageBytes is { Length: > 0 })
        {
            storedCardImageUrl = (await _mediaStorage.StoreAsync(userId, cardImageBytes)).MediaUrl;
        }

        if (coverImageBytes is { Length: > 0 })
        {
            storedCoverImageUrl = (await _mediaStorage.StoreAsync(userId, coverImageBytes)).MediaUrl;
        }

        if (galleryImageBytes is not null)
        {
            foreach (var galleryBytes in galleryImageBytes.Where(bytes => bytes is { Length: > 0 }))
            {
                storedGalleryImageUrls.Add((await _mediaStorage.StoreAsync(userId, galleryBytes)).MediaUrl);
            }
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
        else if (!requiresOwnershipProof && !string.IsNullOrWhiteSpace(contribution.OwnershipProofDocumentUrl))
        {
            await _mediaStorage.DeleteAsync(contribution.OwnershipProofDocumentUrl);
            contribution.OwnershipProofDocumentUrl = null;
            contribution.OwnershipProofOriginalFileName = null;
            contribution.OwnershipProofContentType = null;
            contribution.OwnershipProofUploadedAt = null;
        }

        contribution.Type = type;
        contribution.PlaceId = type is PlaceContributionType.UpdatePlace or PlaceContributionType.ClaimOwnership
            ? request.PlaceId
            : null;
        contribution.OriginalSnapshotJson = type is PlaceContributionType.UpdatePlace or PlaceContributionType.ClaimOwnership && request.PlaceId.HasValue
            ? await BuildOriginalSnapshotAsync(request.PlaceId.Value)
            : "{}";
        contribution.PlaceTypeId = type == PlaceContributionType.ClaimOwnership ? targetPlace!.PlaceTypeId : references!.PlaceTypeId;
        contribution.LocationId = type == PlaceContributionType.ClaimOwnership ? targetPlace!.LocationId : references!.LocationId;
        contribution.RestaurantTypeId = request.RestaurantTypeId;
        contribution.StayTypeId = type == PlaceContributionType.ClaimOwnership ? targetPlace!.Stay?.StayTypeId : references!.StayTypeId;
        contribution.Name = type == PlaceContributionType.ClaimOwnership
            ? targetPlace!.Name
            : NormalizeRequired(request.Name, "Name");
        contribution.Subtitle = type == PlaceContributionType.ClaimOwnership
            ? targetPlace!.Subtitle
            : NormalizeOptional(request.Subtitle);
        contribution.Address = type == PlaceContributionType.ClaimOwnership
            ? targetPlace!.Address
            : NormalizeOptional(request.Address);
        contribution.OpeningHours = type == PlaceContributionType.ClaimOwnership
            ? targetPlace!.OpeningHours
            : NormalizeOptional(request.OpeningHours);
        contribution.Description = type == PlaceContributionType.ClaimOwnership
            ? targetPlace!.Description
            : NormalizeDescription(request.Description);
        contribution.Latitude = type == PlaceContributionType.ClaimOwnership ? targetPlace!.Latitude : references!.Latitude;
        contribution.Longitude = type == PlaceContributionType.ClaimOwnership ? targetPlace!.Longitude : references!.Longitude;
        contribution.ContactEmail = type == PlaceContributionType.ClaimOwnership
            ? targetPlace!.ContactEmail
            : NormalizeContactEmail(request.ContactEmail);
        contribution.PhoneNumber = type == PlaceContributionType.ClaimOwnership
            ? targetPlace!.Number
            : NormalizeOptional(request.PhoneNumber ?? request.Phone);
        contribution.Website = NormalizeWebsite(request.Website);
        contribution.PricePerNight = type == PlaceContributionType.ClaimOwnership
            ? targetPlace!.Stay?.PricePerNight
            : NormalizePricePerNight(request.PricePerNight);
        contribution.CheckInTime = type == PlaceContributionType.ClaimOwnership
            ? targetPlace!.Stay?.CheckInTime
            : NormalizeOptional(request.CheckInTime);
        contribution.CheckOutTime = type == PlaceContributionType.ClaimOwnership
            ? targetPlace!.Stay?.CheckOutTime
            : NormalizeOptional(request.CheckOutTime);
        contribution.TagsJson = Serialize(NormalizeStringList(request.Tags, 80, 30));
        contribution.AmenitiesJson = Serialize(type == PlaceContributionType.ClaimOwnership ? new List<string>() : references!.PlaceAmenities);
        contribution.StayAmenitiesJson = Serialize(type == PlaceContributionType.ClaimOwnership ? new List<string>() : references!.StayAmenities);
        contribution.BookingPartnersJson = Serialize(NormalizeBookingPartners(request.BookingPartners));
        contribution.UpdatedAt = now;

        SyncContributionImages(contribution, request.Images, storedCardImageUrl, storedCoverImageUrl, storedGalleryImageUrls, now, type != PlaceContributionType.ClaimOwnership);

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

        await EnsureReviewerCanReviewAsync(reviewerUserId, contribution);

        var now = DateTime.UtcNow;

        if (contribution.Type == PlaceContributionType.CreatePlace)
        {
            var publishedPlace = await PublishNewPlaceAsync(contribution, now);
            contribution.PlaceId = publishedPlace.Id;

            await EnsureUserPlaceAsync(contribution.UserId, publishedPlace.Id, now);
        }
        else if (contribution.Type == PlaceContributionType.UpdatePlace)
        {
            if (!contribution.PlaceId.HasValue)
                throw new InvalidOperationException("Update contribution is missing target place.");

            await ApplyUpdateToExistingPlaceAsync(contribution, contribution.PlaceId.Value, now);
        }
        else if (contribution.Type == PlaceContributionType.ClaimOwnership)
        {
            if (!contribution.PlaceId.HasValue)
                throw new InvalidOperationException("Ownership claim is missing target place.");

            var placeExists = await _db.Places.AnyAsync(place => place.Id == contribution.PlaceId.Value);
            if (!placeExists)
                throw new InvalidOperationException("The place you are trying to claim does not exist.");

            await EnsureUserPlaceAsync(contribution.UserId, contribution.PlaceId.Value, now);
        }
        else
        {
            throw new InvalidOperationException("Unsupported contribution type.");
        }

        contribution.Status = PlaceContributionStatus.Approved;
        contribution.ReviewedByUserId = reviewerUserId;
        contribution.ReviewedAt = now;
        contribution.ReviewNote = NormalizeOptional(note);
        contribution.UpdatedAt = now;

        _notificationService.AddContributionDecisionNotification(contribution);

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

        await EnsureReviewerCanReviewAsync(reviewerUserId, contribution);

        var now = DateTime.UtcNow;

        contribution.Status = PlaceContributionStatus.Rejected;
        contribution.ReviewedByUserId = reviewerUserId;
        contribution.ReviewedAt = now;
        contribution.ReviewNote = NormalizeOptional(note);
        contribution.UpdatedAt = now;

        _notificationService.AddContributionDecisionNotification(contribution);

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
            .Include(c => c.Place)
                .ThenInclude(p => p!.Location)
                    .ThenInclude(l => l.Destination);
    }

    private async Task<IQueryable<PlaceContribution>> ApplyReviewerScopeAsync(
        IQueryable<PlaceContribution> query,
        int? reviewerUserId)
    {
        if (!reviewerUserId.HasValue)
            return query;

        var managesAll = await ReviewerManagesAllAsync(reviewerUserId.Value);

        if (managesAll)
            return query;

        var regionIds = await GetReviewerRegionIdsAsync(reviewerUserId.Value);

        if (regionIds.Count == 0)
            return query.Where(c => false);

        return query.Where(c =>
            c.LocationId.HasValue &&
            c.Location != null &&
            c.Location.Destination != null &&
            regionIds.Contains(c.Location.Destination.RegionId) &&
            (c.Type != PlaceContributionType.UpdatePlace ||
             (c.PlaceId.HasValue &&
              c.Place != null &&
              regionIds.Contains(c.Place.Location.Destination.RegionId) &&
              c.Place.Location.Destination.RegionId == c.Location.Destination.RegionId)));
    }

    private async Task EnsureReviewerCanReviewAsync(int reviewerUserId, PlaceContribution contribution)
    {
        var managesAll = await ReviewerManagesAllAsync(reviewerUserId);

        if (managesAll)
        {
            if (contribution.Type == PlaceContributionType.UpdatePlace)
                _ = await ResolveTargetPlaceRegionIdAsync(contribution);

            return;
        }

        var regionIds = await GetReviewerRegionIdsAsync(reviewerUserId);
        if (regionIds.Count == 0)
            throw new UnauthorizedAccessException("Regional manager has no assigned regions.");

        var contributionRegionId = await ResolveContributionRegionIdAsync(contribution);

        if (!regionIds.Contains(contributionRegionId))
            throw new UnauthorizedAccessException("You are not assigned to this contribution's region.");

        if (contribution.Type != PlaceContributionType.UpdatePlace)
            return;

        var targetRegionId = await ResolveTargetPlaceRegionIdAsync(contribution);
        if (!regionIds.Contains(targetRegionId))
            throw new UnauthorizedAccessException("You are not assigned to this contribution's target place region.");

        if (targetRegionId != contributionRegionId)
            throw new UnauthorizedAccessException("Cross-region update contributions require admin review.");
    }

    private async Task<bool> ReviewerManagesAllAsync(int reviewerUserId)
    {
        return await _db.Users
            .AsNoTracking()
            .Where(u => u.Id == reviewerUserId)
            .Select(u => u.UserRoles.Any(ur =>
                ur.Role.Name == RoleNames.Admin ||
                ur.Role.Name == "SuperAdmin"))
            .SingleOrDefaultAsync();
    }

    private async Task<List<int>> GetReviewerRegionIdsAsync(int reviewerUserId)
    {
        return await _db.RegionalManagerRegions
            .AsNoTracking()
            .Where(r => r.UserId == reviewerUserId && r.IsActive)
            .Select(r => r.RegionId)
            .ToListAsync();
    }

    private async Task<int> ResolveContributionRegionIdAsync(PlaceContribution contribution)
    {
        if (!contribution.LocationId.HasValue)
            throw new InvalidOperationException("Contribution location is required.");

        var regionId = await _db.Locations
            .AsNoTracking()
            .Where(location => location.Id == contribution.LocationId.Value)
            .Select(location => (int?)location.Destination.RegionId)
            .FirstOrDefaultAsync();

        return regionId ?? throw new KeyNotFoundException("Contribution location not found.");
    }

    private async Task<int> ResolveTargetPlaceRegionIdAsync(PlaceContribution contribution)
    {
        if (!contribution.PlaceId.HasValue)
            throw new InvalidOperationException("Target place is required for update contributions.");

        var regionId = await _db.Places
            .AsNoTracking()
            .Where(place => place.Id == contribution.PlaceId.Value)
            .Select(place => (int?)place.Location.Destination.RegionId)
            .FirstOrDefaultAsync();

        return regionId ?? throw new KeyNotFoundException("Target place not found.");
    }

    private async Task<bool> RequiresOwnershipProofAsync(
        PlaceContributionType contributionType,
        UpsertPlaceContributionDto request)
    {
        if (contributionType == PlaceContributionType.ClaimOwnership)
            return true;

        if (request.StayTypeId.HasValue || !string.IsNullOrWhiteSpace(request.StayType))
            return true;

        return await ResolveRequestedPlaceTypeOwnableAsync(request);
    }

    private async Task<string?> ResolveRequestedPlaceTypeNameAsync(UpsertPlaceContributionDto request)
    {
        if (!string.IsNullOrWhiteSpace(request.PlaceType))
            return request.PlaceType;

        if (!request.PlaceTypeId.HasValue)
            return null;

        var placeTypeName = await _db.PlaceTypes
            .AsNoTracking()
            .Where(placeType => placeType.Id == request.PlaceTypeId.Value)
            .Select(placeType => placeType.Name)
            .SingleOrDefaultAsync();

        return placeTypeName ?? throw new KeyNotFoundException("Place type not found.");
    }

    private async Task<bool> ResolveRequestedPlaceTypeOwnableAsync(UpsertPlaceContributionDto request)
    {
        if (request.PlaceTypeId.HasValue)
        {
            var isOwnable = await _db.PlaceTypes
                .AsNoTracking()
                .Where(placeType => placeType.Id == request.PlaceTypeId.Value)
                .Select(placeType => (bool?)placeType.IsOwnable)
                .SingleOrDefaultAsync();

            return isOwnable ?? throw new KeyNotFoundException("Place type not found.");
        }

        var placeTypeName = NormalizeOptional(request.PlaceType);
        if (placeTypeName is null)
            return false;

        var dbIsOwnable = await _db.PlaceTypes
            .AsNoTracking()
            .Where(placeType => placeType.Name.ToLower() == placeTypeName)
            .Select(placeType => (bool?)placeType.IsOwnable)
            .SingleOrDefaultAsync();

        return dbIsOwnable ?? LegacyOwnershipProofRequiredPlaceTypes.Contains(placeTypeName);
    }

    private async Task<bool> UserCanManagePlaceAsync(int userId, int placeId)
    {
        return await _db.UserPlaces
            .AsNoTracking()
            .AnyAsync(userPlace => userPlace.UserId == userId && userPlace.PlaceId == placeId);
    }

    private async Task EnsureUserPlaceAsync(int userId, int placeId, DateTime now)
    {
        var exists = await _db.UserPlaces
            .AnyAsync(userPlace => userPlace.UserId == userId && userPlace.PlaceId == placeId);

        if (exists)
            return;

        _db.UserPlaces.Add(new UserPlace
        {
            UserId = userId,
            PlaceId = placeId,
            CreatedAt = now,
            UpdatedAt = now
        });
    }

    private async Task<string> BuildOriginalSnapshotAsync(int placeId)
    {
        var place = await _db.Places
            .AsNoTracking()
            .Include(p => p.Stay)
            .Include(p => p.Images)
            .Include(p => p.PlaceTags)
                .ThenInclude(pt => pt.Tag)
                    .ThenInclude(tag => tag.TagType)
                        .ThenInclude(tagType => tagType!.PlaceTypes)
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

    private async Task<ContributionReferenceResolution> ResolveContributionReferencesAsync(UpsertPlaceContributionDto request)
    {
        var contributionType = ParseType(request.Type);
        if (contributionType == PlaceContributionType.UpdatePlace)
        {
            if (!request.PlaceId.HasValue)
                throw new InvalidOperationException("PlaceId is required for update contributions.");

            var targetExists = await _db.Places
                .AsNoTracking()
                .AnyAsync(place => place.Id == request.PlaceId.Value);

            if (!targetExists)
                throw new KeyNotFoundException("Target place not found.");
        }

        var stayTypeId = request.StayTypeId;
        if (!stayTypeId.HasValue && !string.IsNullOrWhiteSpace(request.StayType))
            stayTypeId = await ResolveStayTypeIdAsync(request.StayType);

        var placeTypeId = request.PlaceTypeId;
        if (!placeTypeId.HasValue && !string.IsNullOrWhiteSpace(request.PlaceType))
            placeTypeId = await ResolvePlaceTypeIdAsync(request.PlaceType);

        if (!placeTypeId.HasValue && stayTypeId.HasValue)
            placeTypeId = await ResolvePlaceTypeIdAsync("Stay");

        if (!placeTypeId.HasValue)
            placeTypeId = await ResolvePlaceTypeIdAsync("Place");

        var location = await ResolveContributionLocationAsync(request);
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
            location.LocationId,
            stayTypeId,
            requestedPlaceAmenities,
            requestedStayAmenities,
            location.Latitude,
            location.Longitude);
    }

    private async Task<ContributionLocationResolution> ResolveContributionLocationAsync(UpsertPlaceContributionDto request)
    {
        if (request.LocationId.HasValue)
        {
            var location = await _db.Locations
                .AsNoTracking()
                .Where(existingLocation => existingLocation.Id == request.LocationId.Value)
                .Select(existingLocation => new { existingLocation.Id, existingLocation.Latitude, existingLocation.Longitude })
                .FirstOrDefaultAsync()
                ?? throw new KeyNotFoundException("Location not found.");

            var latitude = request.Latitude ?? location.Latitude;
            var longitude = request.Longitude ?? location.Longitude;

            if (!HasUsableCoordinates(latitude, longitude))
                throw new InvalidOperationException("Please choose a valid location before submitting.");

            return new ContributionLocationResolution(location.Id, latitude, longitude);
        }

        var locationId = await ResolveLegacyLocationIdAsync(request);
        if (!locationId.HasValue || !HasUsableCoordinates(request.Latitude, request.Longitude))
            throw new InvalidOperationException("Please choose a valid location before submitting.");

        return new ContributionLocationResolution(locationId.Value, request.Latitude!.Value, request.Longitude!.Value);
    }

    private async Task<int?> ResolvePlaceTypeIdAsync(string name)
    {
        var displayName = NormalizeRequiredName(name, "Place type", 100);
        var normalized = displayName.ToLowerInvariant();
        var placeType = await _db.PlaceTypes
            .FirstOrDefaultAsync(existingType => existingType.Name.ToLower() == normalized);

        if (placeType is not null)
            return placeType.Id;

        placeType = new PlaceType
        {
            Name = ToTitle(displayName),
            Description = normalized == "stay" ? "Accommodation and stay listings" : $"{ToTitle(displayName)} listings",
            IsOwnable = LegacyOwnershipProofRequiredPlaceTypes.Contains(normalized),
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
            .FirstOrDefaultAsync(existingType =>
                existingType.Slug == slug ||
                existingType.Name.ToLower() == normalized.ToLowerInvariant());

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
        if (!HasUsableCoordinates(request.Latitude, request.Longitude))
            return null;

        var latitude = request.Latitude!.Value;
        var longitude = request.Longitude!.Value;

        var nearestLocationId = await ResolveNearestKnownLocationIdAsync(latitude, longitude);
        if (nearestLocationId.HasValue)
            return nearestLocationId.Value;

        var nearestDestination = await ResolveNearestKnownDestinationAsync(latitude, longitude);
        if (nearestDestination is null)
            return null;

        var locationType = await ResolveLocationTypeAsync();
        var locationName = NormalizeOptional(request.City, 200) ?? nearestDestination.Name;
        var location = new Location
        {
            Name = locationName,
            DestinationId = nearestDestination.Id,
            LocationTypeId = locationType.Id,
            Latitude = latitude,
            Longitude = longitude,
            CreatedAt = DateTime.UtcNow
        };

        await _db.Locations.AddAsync(location);
        await _db.SaveChangesAsync();
        return location.Id;
    }

    private async Task<int?> ResolveNearestKnownLocationIdAsync(double latitude, double longitude)
    {
        var locations = await _db.Locations
            .AsNoTracking()
            .Select(location => new CoordinateCandidate(location.Id, location.Latitude, location.Longitude))
            .ToListAsync();

        return locations
            .Select(location => new
            {
                location.Id,
                DistanceKm = HaversineKilometers(latitude, longitude, location.Latitude, location.Longitude)
            })
            .Where(location => location.DistanceKm <= MaxContributionLocationMatchDistanceKm)
            .OrderBy(location => location.DistanceKm)
            .ThenBy(location => location.Id)
            .Select(location => (int?)location.Id)
            .FirstOrDefault();
    }

    private async Task<DestinationCandidate?> ResolveNearestKnownDestinationAsync(double latitude, double longitude)
    {
        var destinations = await _db.Destinations
            .AsNoTracking()
            .Select(destination => new DestinationCandidate(
                destination.Id,
                destination.Name,
                destination.Latitude,
                destination.Longitude))
            .ToListAsync();

        return destinations
            .Select(destination => new
            {
                Destination = destination,
                DistanceKm = HaversineKilometers(latitude, longitude, destination.Latitude, destination.Longitude)
            })
            .Where(destination => destination.DistanceKm <= MaxContributionDestinationMatchDistanceKm)
            .OrderBy(destination => destination.DistanceKm)
            .ThenBy(destination => destination.Destination.Id)
            .Select(destination => destination.Destination)
            .FirstOrDefault();
    }

    private async Task<DestinationType> ResolveDestinationTypeAsync()
    {
        var destinationType = await _db.DestinationTypes
            .FirstOrDefaultAsync(existingType => existingType.Name == "City")
            ?? await _db.DestinationTypes.OrderBy(existingType => existingType.Id).FirstOrDefaultAsync();

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
            .FirstOrDefaultAsync(existingType => existingType.Name == "Neighborhood")
            ?? await _db.LocationTypes.OrderBy(existingType => existingType.Id).FirstOrDefaultAsync();

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
                .OrderBy(location => Math.Pow(location.Latitude - latitude.Value, 2) + Math.Pow(location.Longitude - longitude.Value, 2))
                .First();
        }

        return candidates.OrderBy(location => location.Id).First();
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
                };
            }

            place.Stay.StayTypeId = contribution.StayTypeId.Value;
            place.Stay.PricePerNight = contribution.PricePerNight;
            place.Stay.CheckInTime = contribution.CheckInTime;
            place.Stay.CheckOutTime = contribution.CheckOutTime;

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
                IsMain = image.ImageRole == EntityImageRole.Cover,
                ImageRole = EntityImageRole.Normalize(image.ImageRole),
                SortOrder = image.SortOrder,
                CreatedAt = now,
                UpdatedAt = now
            });
        }
    }

    private void SyncContributionImages(
        PlaceContribution contribution,
        List<UpsertPlaceContributionImageDto>? requestImages,
        string? storedCardImageUrl,
        string? storedCoverImageUrl,
        IReadOnlyList<string> storedGalleryImageUrls,
        DateTime now,
        bool requireCardAndCover)
    {
        var oldManagedUrls = contribution.Images
            .Where(image => _mediaStorage.IsManagedContributionMediaUrl(image.Url))
            .Select(image => image.Url)
            .ToList();

        contribution.Images.Clear();

        var imageDtos = new List<UpsertPlaceContributionImageDto>();

        if (requestImages is not null)
        {
            imageDtos.AddRange(NormalizeImages(requestImages));
        }

        if (!string.IsNullOrWhiteSpace(storedCoverImageUrl))
        {
            imageDtos.Add(new UpsertPlaceContributionImageDto
            {
                Url = storedCoverImageUrl,
                Caption = CoverImageCaption,
                IsMain = true,
                ImageRole = EntityImageRole.Cover,
                SortOrder = -10,
                ImagePosition = "50% 50%",
                ImageSize = "cover"
            });
        }

        if (!string.IsNullOrWhiteSpace(storedCardImageUrl))
        {
            imageDtos.Add(new UpsertPlaceContributionImageDto
            {
                Url = storedCardImageUrl,
                Caption = CardImageCaption,
                IsMain = false,
                ImageRole = EntityImageRole.Card,
                SortOrder = -9,
                ImagePosition = "50% 50%",
                ImageSize = "cover"
            });
        }

        imageDtos.AddRange(storedGalleryImageUrls.Select((url, index) => new UpsertPlaceContributionImageDto
        {
            Url = url,
            ImageRole = EntityImageRole.Gallery,
            IsMain = false,
            SortOrder = index,
            ImagePosition = "50% 50%",
            ImageSize = "cover"
        }));

        if (requireCardAndCover)
            RequireCardAndCoverImages(imageDtos);

        if (imageDtos.Count == 0)
            return;

        if (imageDtos.Count > MaxContributionImages)
            throw new InvalidOperationException($"A contribution can include at most {MaxContributionImages} images.");

        var coverSeen = false;
        var cardSeen = false;
        foreach (var image in imageDtos
                     .OrderBy(image => ImageRoleSort(EntityImageRole.Normalize(image.ImageRole)))
                     .ThenBy(image => image.SortOrder)
                     .ThenBy(image => image.Url, StringComparer.OrdinalIgnoreCase))
        {
            var imageRole = EntityImageRole.Normalize(image.ImageRole);
            if (imageRole == EntityImageRole.Cover)
            {
                if (coverSeen) imageRole = EntityImageRole.Gallery;
                coverSeen = true;
            }
            else if (imageRole == EntityImageRole.Card)
            {
                if (cardSeen) imageRole = EntityImageRole.Gallery;
                cardSeen = true;
            }

            contribution.Images.Add(new PlaceContributionImage
            {
                Url = NormalizeImageUrl(image.Url),
                AltText = NormalizeOptional(image.AltText),
                Caption = NormalizeOptional(image.Caption),
                IsMain = imageRole == EntityImageRole.Cover,
                ImageRole = imageRole,
                SortOrder = image.SortOrder,
                ImagePosition = NormalizeImagePosition(image.ImagePosition),
                ImageSize = NormalizeImageSize(image.ImageSize),
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


    private static void RequireCardAndCoverImages(IEnumerable<UpsertPlaceContributionImageDto> images)
    {
        var hasCard = false;
        var hasCover = false;

        foreach (var image in images)
        {
            var role = EntityImageRole.Normalize(image.ImageRole);
            if (role == EntityImageRole.Card && !string.IsNullOrWhiteSpace(image.Url))
            {
                hasCard = true;
            }
            else if (role == EntityImageRole.Cover && !string.IsNullOrWhiteSpace(image.Url))
            {
                hasCover = true;
            }
        }

        if (!hasCard || !hasCover)
        {
            throw new InvalidOperationException("Card and cover images are required.");
        }
    }

    private static int ImageRoleSort(string? imageRole)
    {
        return EntityImageRole.Normalize(imageRole) switch
        {
            EntityImageRole.Cover => 0,
            EntityImageRole.Card => 1,
            _ => 2
        };
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

        var resolvedPartners = new List<ResolvedBookingPartner>();

        foreach (var partner in bookingPartners)
        {
            var normalizedId = NormalizeOptional(partner.Id)?.ToLowerInvariant();
            if (normalizedId is null)
                continue;

            BookingPartner? resolved = null;
            if (int.TryParse(normalizedId, out var numericId))
            {
                resolved = await _db.BookingPartners.FirstOrDefaultAsync(existing => existing.Id == numericId);
            }

            if (resolved is null)
            {
                var partnerName = PartnerName(normalizedId);
                resolved = await _db.BookingPartners.FirstOrDefaultAsync(existing =>
                    existing.Name.ToLower() == partnerName.ToLowerInvariant() ||
                    existing.Name.ToLower() == normalizedId);

                if (resolved is null)
                {
                    resolved = new BookingPartner
                    {
                        Name = partnerName,
                        Variant = PartnerVariant(normalizedId)
                    };
                    await _db.BookingPartners.AddAsync(resolved);
                    await _db.SaveChangesAsync();
                }
            }

            resolvedPartners.Add(new ResolvedBookingPartner(resolved.Id, NormalizeOptional(partner.Url)));
        }

        return resolvedPartners
            .DistinctBy(partner => partner.BookingPartnerId)
            .ToList();
    }

    private static PlaceContributionListItemDto MapListItem(PlaceContribution contribution)
    {
        var cardImage = GetCardImage(contribution);
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
            CardImageUrl = cardImage?.Url,
            MainImageUrl = cardImage?.Url,
            CoverImageUrl = coverImage?.Url,
            GalleryImageUrls = contribution.Images
                .Where(image => image.ImageRole == EntityImageRole.Gallery)
                .OrderBy(image => image.SortOrder)
                .ThenBy(image => image.Id)
                .Select(image => image.Url)
                .ToList(),
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
            CardImageUrl = listItem.CardImageUrl,
            MainImageUrl = listItem.MainImageUrl,
            CoverImageUrl = listItem.CoverImageUrl,
            GalleryImageUrls = listItem.GalleryImageUrls,
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
            ImagePosition = GetCoverImage(contribution)?.ImagePosition ?? "50% 50%",
            ImageSize = GetCoverImage(contribution)?.ImageSize ?? "cover",
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
                    ImageRole = EntityImageRole.Normalize(image.ImageRole),
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

    private static PlaceContributionImage? GetCardImage(PlaceContribution contribution)
    {
        return contribution.Images
            .OrderBy(image => image.SortOrder)
            .ThenBy(image => image.Id)
            .FirstOrDefault(image => image.ImageRole == EntityImageRole.Card);
    }

    private static PlaceContributionImage? GetCoverImage(PlaceContribution contribution)
    {
        return contribution.Images
            .OrderBy(image => image.SortOrder)
            .ThenBy(image => image.Id)
            .FirstOrDefault(image => image.ImageRole == EntityImageRole.Cover);
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
        var normalized = (value ?? string.Empty)
            .Trim()
            .Replace(" ", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("-", string.Empty, StringComparison.OrdinalIgnoreCase)
            .ToLowerInvariant();

        return normalized switch
        {
            "" or "create" or "createplace" => PlaceContributionType.CreatePlace,
            "update" or "updateplace" => PlaceContributionType.UpdatePlace,
            "claim" or "claimownership" => PlaceContributionType.ClaimOwnership,
            _ when Enum.TryParse<PlaceContributionType>(value, ignoreCase: true, out var parsed) => parsed,
            _ => throw new InvalidOperationException("Unsupported contribution type.")
        };
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

    private static string? NormalizeOptional(string? value, int maxLength)
    {
        var trimmed = NormalizeOptional(value);
        if (trimmed is null)
            return null;

        return trimmed.Length > maxLength ? trimmed[..maxLength] : trimmed;
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
            .Select(value => NormalizeOptional(value))
            .Where(value => value is not null)
            .Select(value => value!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static List<string> NormalizeStringList(IEnumerable<string>? values, int maxItemLength, int maxCount) =>
        (values ?? Enumerable.Empty<string>())
            .Select(value => value.Trim())
            .Where(value => !string.IsNullOrWhiteSpace(value) && value.Length <= maxItemLength)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(maxCount)
            .ToList();

    private static List<ContributionBookingPartnerDto> NormalizeBookingPartners(
        IEnumerable<ContributionBookingPartnerDto>? values)
    {
        var normalized = (values ?? Enumerable.Empty<ContributionBookingPartnerDto>())
            .Select(value => new
            {
                Id = value.Id?.Trim().ToLowerInvariant() ?? string.Empty,
                value.Url
            })
            .Where(value => !string.IsNullOrWhiteSpace(value.Id))
            .Select(value => new ContributionBookingPartnerDto
            {
                Id = NormalizeBookingPartnerId(value.Id),
                Url = NormalizeBookingPartnerUrl(value.Url)
            })
            .DistinctBy(value => value.Id, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (normalized.Count > 10)
            throw new InvalidOperationException("A contribution can include at most 10 booking partners.");

        return normalized;
    }

    private static void Validate(UpsertPlaceContributionDto request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            throw new InvalidOperationException("Name is required.");

        if (request.Name.Trim().Length > 200)
            throw new InvalidOperationException("Name is too long.");

        if (NormalizeDescription(request.Description).Length > 2000)
            throw new InvalidOperationException("Description is too long.");

        if (request is { Latitude: null, Longitude: not null } or { Latitude: not null, Longitude: null })
            throw new InvalidOperationException("Both latitude and longitude are required when setting coordinates.");

        if (request.Latitude.HasValue && !IsFinite(request.Latitude.Value))
            throw new InvalidOperationException("Latitude must be a finite value.");

        if (request.Longitude.HasValue && !IsFinite(request.Longitude.Value))
            throw new InvalidOperationException("Longitude must be a finite value.");

        if (request.Latitude is < -90 or > 90)
            throw new InvalidOperationException("Latitude must be between -90 and 90.");

        if (request.Longitude is < -180 or > 180)
            throw new InvalidOperationException("Longitude must be between -180 and 180.");

        if (request is { Latitude: 0d, Longitude: 0d })
            throw new InvalidOperationException("Please choose a valid location before submitting.");

        if (request.LocationId is <= 0)
            throw new InvalidOperationException("Please choose a valid location before submitting.");

        if (!request.LocationId.HasValue &&
            (!HasUsableCoordinates(request.Latitude, request.Longitude) ||
             string.IsNullOrWhiteSpace(request.City) ||
             string.IsNullOrWhiteSpace(request.Country)))
        {
            throw new InvalidOperationException("Please choose a valid location before submitting.");
        }

        _ = NormalizeContactEmail(request.ContactEmail);
        _ = NormalizeWebsite(request.Website);
        _ = NormalizePricePerNight(request.PricePerNight);
        _ = NormalizeBookingPartners(request.BookingPartners);
        _ = NormalizeImages(request.Images).ToList();

        if (ParseType(request.Type) == PlaceContributionType.UpdatePlace && !request.PlaceId.HasValue)
            throw new InvalidOperationException("PlaceId is required for update contributions.");
    }

    private static string NormalizeDescription(string? description) =>
        description?.Trim() ?? string.Empty;

    private static string NormalizeRequiredName(string? value, string fieldName, int maxLength)
    {
        var trimmed = value?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
            throw new InvalidOperationException($"{fieldName} is required.");

        if (trimmed.Length > maxLength)
            throw new InvalidOperationException($"{fieldName} must be {maxLength} characters or fewer.");

        return trimmed;
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

        return NormalizeExternalHttpsUrl(trimmed, MaxWebsiteUrlLength, "Website URL");
    }

    private static string NormalizeBookingPartnerUrl(string? url)
    {
        var trimmed = url?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
            throw new InvalidOperationException("Booking partner URL is required.");

        return NormalizeExternalHttpsUrl(trimmed, MaxBookingPartnerUrlLength, "Booking partner URL");
    }

    private static string NormalizeImageUrl(string? imageUrl)
    {
        var trimmed = imageUrl?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
            throw new InvalidOperationException("Image URL is required.");

        if (trimmed.Length > MaxImageUrlLength)
            throw new InvalidOperationException($"Image URL must be {MaxImageUrlLength} characters or fewer.");

        if (IsManagedContributionMediaPath(trimmed))
            return trimmed;

        return NormalizeExternalHttpsUrl(trimmed, MaxImageUrlLength, "Image URL");
    }

    private static string NormalizeExternalHttpsUrl(string trimmed, int maxLength, string fieldName)
    {
        if (trimmed.Length > maxLength)
            throw new InvalidOperationException($"{fieldName} must be {maxLength} characters or fewer.");

        if (trimmed.StartsWith("//", StringComparison.Ordinal))
            throw new InvalidOperationException($"{fieldName} must be a valid https URL.");

        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) ||
            !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(uri.Host) ||
            uri.HostNameType == UriHostNameType.Unknown ||
            !string.IsNullOrEmpty(uri.UserInfo))
        {
            throw new InvalidOperationException($"{fieldName} must be a valid https URL.");
        }

        var normalized = uri.AbsoluteUri;
        if (normalized.Length > maxLength)
            throw new InvalidOperationException($"{fieldName} must be {maxLength} characters or fewer.");

        return normalized;
    }

    private static bool IsManagedContributionMediaPath(string value)
    {
        if (!value.StartsWith(ManagedContributionMediaPrefix, StringComparison.Ordinal))
            return false;

        if (value.Contains('\\') ||
            value.Contains("//", StringComparison.Ordinal) ||
            value.Contains('%') ||
            value.Contains('?') ||
            value.Contains('#'))
        {
            throw new InvalidOperationException("Image URL must be a valid https URL or managed media path.");
        }

        var fileName = value[ManagedContributionMediaPrefix.Length..];
        if (string.IsNullOrWhiteSpace(fileName) ||
            fileName.Contains('/') ||
            fileName.Contains('\\') ||
            fileName.Contains("..", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Image URL must be a valid https URL or managed media path.");
        }

        if (!ManagedContributionMediaFileNameRegex.IsMatch(fileName))
            throw new InvalidOperationException("Image URL must be a valid https URL or managed media path.");

        return true;
    }

    private static IEnumerable<UpsertPlaceContributionImageDto> NormalizeImages(IEnumerable<UpsertPlaceContributionImageDto>? images)
    {
        var normalized = (images ?? Enumerable.Empty<UpsertPlaceContributionImageDto>())
            .Where(image => !string.IsNullOrWhiteSpace(image.Url))
            .Select((image, index) => new UpsertPlaceContributionImageDto
            {
                Url = NormalizeImageUrl(image.Url),
                AltText = image.AltText,
                Caption = image.Caption,
                IsMain = image.IsMain,
                ImageRole = EntityImageRole.Normalize(image.ImageRole),
                SortOrder = image.SortOrder,
                ImagePosition = image.ImagePosition,
                ImageSize = image.ImageSize
            })
            .DistinctBy(image => image.Url, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (normalized.Count > MaxContributionImages)
            throw new InvalidOperationException($"A contribution can include at most {MaxContributionImages} images.");

        return normalized;
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

    private static bool HasUsableCoordinates(double? latitude, double? longitude) =>
        latitude.HasValue &&
        longitude.HasValue &&
        IsFinite(latitude.Value) &&
        IsFinite(longitude.Value) &&
        latitude.Value >= -90 &&
        latitude.Value <= 90 &&
        longitude.Value >= -180 &&
        longitude.Value <= 180 &&
        (latitude.Value != 0d || longitude.Value != 0d);

    private static bool IsFinite(double value) =>
        !double.IsNaN(value) && !double.IsInfinity(value);

    private static double HaversineKilometers(
        double latitude1,
        double longitude1,
        double latitude2,
        double longitude2)
    {
        const double earthRadiusKm = 6371.0088d;
        var dLat = DegreesToRadians(latitude2 - latitude1);
        var dLon = DegreesToRadians(longitude2 - longitude1);
        var lat1 = DegreesToRadians(latitude1);
        var lat2 = DegreesToRadians(latitude2);

        var a =
            Math.Pow(Math.Sin(dLat / 2d), 2d) +
            Math.Cos(lat1) * Math.Cos(lat2) * Math.Pow(Math.Sin(dLon / 2d), 2d);

        return 2d * earthRadiusKm * Math.Asin(Math.Min(1d, Math.Sqrt(a)));
    }

    private static double DegreesToRadians(double value) => value * Math.PI / 180d;

    private static List<string> NormalizeStayAmenityValues(IEnumerable<string> values)
    {
        return values
            .Select(StayAmenitySlug)
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

    private static string NormalizeBookingPartnerId(string partnerId)
    {
        if (partnerId.Length > MaxBookingPartnerIdLength)
            throw new InvalidOperationException($"Booking partner name must be {MaxBookingPartnerIdLength} characters or fewer.");

        var partnerName = PartnerName(partnerId);
        if (partnerName.Length > 100)
            throw new InvalidOperationException("Booking partner name is too long.");

        return partnerId;
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
            .Select(part => char.ToUpperInvariant(part[0]) + part[1..]));

    private sealed record ResolvedBookingPartner(int BookingPartnerId, string? Url);

    private sealed record CoordinateCandidate(int Id, double Latitude, double Longitude);

    private sealed record DestinationCandidate(int Id, string Name, double Latitude, double Longitude);

    private sealed record ContributionReferenceResolution(
        int? PlaceTypeId,
        int LocationId,
        int? StayTypeId,
        List<string> PlaceAmenities,
        List<string> StayAmenities,
        double Latitude,
        double Longitude);

    private sealed record ContributionLocationResolution(
        int LocationId,
        double Latitude,
        double Longitude);
}
