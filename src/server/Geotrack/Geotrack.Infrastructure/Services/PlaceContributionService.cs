using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Globalization;
using Geotrack.Application.DTOs.Contributions;
using Geotrack.Application.Interfaces.Service;
using Geotrack.Application.Security;
using Geotrack.Application.Services;
using Geotrack.Domain.Entities.Core;
using Geotrack.Domain.Entities.Tags;
using Geotrack.Domain.Entities.Users;
using Microsoft.EntityFrameworkCore;
using Coordinate = NetTopologySuite.Geometries.Coordinate;
using Geometry = NetTopologySuite.Geometries.Geometry;
using GeometryFactory = NetTopologySuite.Geometries.GeometryFactory;
using PrecisionModel = NetTopologySuite.Geometries.PrecisionModel;

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
    private const string ManagedContributionMediaPrefix = "/media/uploads/contributions/";
    private const string BoundaryGeometryPropertyName = "BoundaryGeometry";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly GeometryFactory Wgs84GeometryFactory = new(new PrecisionModel(), 4326);
    private static readonly HashSet<string> LegacyOwnablePlaceTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "stay",
        "restaurant",
        "cafe",
        "bar"
    };
    private static readonly HashSet<string> OpeningHoursRequiredPlaceTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "bar",
        "cafe",
        "restaurant",
        "museum",
        "gallery"
    };
    private static readonly HashSet<string> ContributionIneligiblePlaceTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "beach",
        "nature",
        "shopping"
    };
    private static readonly Regex ManagedContributionMediaFileNameRegex = new(
        @"^[a-z0-9][a-z0-9._-]{0,199}\.(png|jpe?g|gif|webp)$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex LocalImagePathRegex = new(
        @"^/(media|images)/[a-z0-9][a-z0-9._/-]{0,499}\.(png|jpe?g|gif|webp|svg)$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private readonly AppDbContext _db;
    private readonly IContributionMediaStorageService _mediaStorage;
    private readonly IUserNotificationService _notificationService;
    private readonly IAdminNotificationService _adminNotificationService;
    private readonly IContentHtmlSanitizer _htmlSanitizer;
    private readonly IModerationAuditService _auditService;
    private readonly DestinationBoundaryResolver _destinationBoundaryResolver;

    public PlaceContributionService(
        AppDbContext db,
        IContributionMediaStorageService mediaStorage,
        IUserNotificationService notificationService,
        IAdminNotificationService adminNotificationService,
        IContentHtmlSanitizer htmlSanitizer,
        IModerationAuditService auditService,
        DestinationBoundaryResolver destinationBoundaryResolver)
    {
        _db = db;
        _mediaStorage = mediaStorage;
        _notificationService = notificationService;
        _adminNotificationService = adminNotificationService;
        _htmlSanitizer = htmlSanitizer;
        _auditService = auditService;
        _destinationBoundaryResolver = destinationBoundaryResolver;
    }

    public async Task<GetPlaceContributionsResponseDto> GetForUserAsync(int userId)
    {
        var contributions = await BaseContributionQuery()
            .Where(c => c.UserId == userId)
            .OrderByDescending(c => c.SubmittedAt)
            .ToListAsync();

        return new GetPlaceContributionsResponseDto
        {
            Contributions = contributions
                .Where(c => !(c.Status == PlaceContributionStatus.Approved &&
                              c.Place != null &&
                              c.Place.Status == "Archived"))
                .Select(MapListItem)
                .ToList()
        };
    }

    public async Task<PlaceContributionDetailDto?> GetByIdForUserAsync(int userId, int contributionId)
    {
        var contribution = await BaseContributionQuery()
            .SingleOrDefaultAsync(c => c.Id == contributionId && c.UserId == userId);

        if (contribution is null)
            return null;

        var includeReviewNote = HasCompletedReview(contribution.Status);

        if (contribution.Status == PlaceContributionStatus.Approved &&
            contribution.PlaceId.HasValue)
        {
            return await MapApprovedPublishedPlaceDetailAsync(contribution, includeReviewNote);
        }

        var hasPendingOwnershipClaim = contribution.PlaceId.HasValue &&
            await _db.PlaceContributions.AsNoTracking().AnyAsync(c =>
                c.PlaceId == contribution.PlaceId &&
                c.UserId == contribution.UserId &&
                c.Type == PlaceContributionType.ClaimOwnership &&
                (c.Status == PlaceContributionStatus.PendingReview || c.Status == PlaceContributionStatus.Approved));

        if (contribution.Type == PlaceContributionType.ClaimOwnership)
            return await MapOwnershipDetailWithPlaceFallbacksAsync(contribution, includeReviewNote, hasPendingOwnershipClaim);

        return MapDetail(contribution, includeReviewNote, hasPendingOwnershipClaim);
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

        if (contribution is null)
            return null;

        return contribution.Type == PlaceContributionType.ClaimOwnership
            ? await MapOwnershipDetailWithPlaceFallbacksAsync(contribution, includeReviewNote: true)
            : MapDetail(contribution, includeReviewNote: true);
    }

    public async Task<GetPlaceContributionsResponseDto> GetPendingForContentManagerAsync()
    {
        var regionsWithManagers = await _db.RegionalManagerRegions
            .AsNoTracking()
            .Where(r => r.IsActive)
            .Select(r => r.RegionId)
            .Distinct()
            .ToListAsync();

        var contributions = await BaseContributionQuery()
            .Where(c => !(c.Status == PlaceContributionStatus.Approved &&
                          c.Place != null &&
                          c.Place.Status == "Archived") &&
                        (!c.LocationId.HasValue ||
                         c.Location == null ||
                         !regionsWithManagers.Contains(c.Location.Destination.RegionId)))
            .OrderBy(c => c.SubmittedAt)
            .ToListAsync();

        return new GetPlaceContributionsResponseDto
        {
            Contributions = contributions.Select(MapListItem).ToList()
        };
    }

    public async Task<PlaceContributionDetailDto?> GetPendingForContentManagerByIdAsync(int contributionId)
    {
        var regionsWithManagers = await _db.RegionalManagerRegions
            .AsNoTracking()
            .Where(r => r.IsActive)
            .Select(r => r.RegionId)
            .Distinct()
            .ToListAsync();

        var contribution = await BaseContributionQuery()
            .SingleOrDefaultAsync(c =>
                c.Id == contributionId &&
                (!c.LocationId.HasValue ||
                 c.Location == null ||
                 !regionsWithManagers.Contains(c.Location.Destination.RegionId)));

        if (contribution is null)
            return null;

        if (contribution.Type == PlaceContributionType.ClaimOwnership)
        {
            var ownershipDetail = await MapOwnershipDetailWithPlaceFallbacksAsync(contribution, includeReviewNote: true);
            await ApplyContentManagerSuggestionsAsync(ownershipDetail, contribution);
            return ownershipDetail;
        }

        return await MapContentManagerDetailAsync(contribution);
    }

    public async Task<List<ContributionPlaceTypeDto>> GetPlaceTypesAsync()
    {
        return await _db.PlaceTypes
            .AsNoTracking()
            .Where(type => !ContributionIneligiblePlaceTypes.Contains(type.Name))
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

    public async Task<List<ContributionStayTypeDto>> GetStayTypesAsync()
    {
        return await _db.StayTypes
            .AsNoTracking()
            .OrderBy(st => st.Name == "Hotel" ? 0 : 1)
            .ThenBy(st => st.Name)
            .Select(st => new ContributionStayTypeDto
            {
                Id = st.Id,
                Name = st.Name,
                Slug = st.Slug
            })
            .ToListAsync();
    }

    public async Task<ContributionFeatureOptionsDto> GetFeatureOptionsAsync()
    {
        var characteristics = await _db.Tags
            .AsNoTracking()
            .Where(tag => tag.ForPlace && (tag.TagType == null || tag.TagType.ForPlace))
            .OrderBy(tag => tag.TagType != null ? tag.TagType.FilterOrder : 1000)
            .ThenBy(tag => tag.Name)
            .Select(tag => new ContributionFeatureOptionDto
            {
                Id = tag.Slug,
                Label = tag.Name,
                TagTypeId = tag.TagTypeId,
                TagTypeName = tag.TagType != null ? tag.TagType.Name : null,
                TagTypeSlug = tag.TagType != null ? tag.TagType.Slug : null,
                TagTypeLabel = tag.TagType != null ? tag.TagType.FilterLabel ?? tag.TagType.Name : null,
                TagTypeOrder = tag.TagType != null ? tag.TagType.FilterOrder : 1000
            })
            .ToListAsync();

        var amenities = await _db.Amenities
            .AsNoTracking()
            .OrderBy(amenity => amenity.Name)
            .Select(amenity => new ContributionFeatureOptionDto
            {
                Id = amenity.Icon != "" ? amenity.Icon : amenity.Name,
                Label = amenity.Name
            })
            .ToListAsync();

        var stayAmenities = await _db.StayAmenities
            .AsNoTracking()
            .OrderBy(amenity => amenity.Name)
            .Select(amenity => new ContributionFeatureOptionDto
            {
                Id = amenity.Slug,
                Label = amenity.Name
            })
            .ToListAsync();

        characteristics = characteristics.Count > 0
            ? characteristics
            : DefaultContributionCharacteristics();
        amenities = ExcludeAmenityFeatureOptions(
            MergeFeatureOptions(amenities, DefaultContributionAmenities()),
            "family",
            "family-friendly");
        stayAmenities = ExcludeAmenityFeatureOptions(
            MergeFeatureOptions(stayAmenities, DefaultContributionStayAmenities()),
            "family",
            "family-friendly");

        var allAmenityIds = amenities.Select(option => option.Id)
            .Concat(stayAmenities.Select(option => option.Id))
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var characteristicIdsByPlaceType = await BuildContributionCharacteristicIdsByPlaceTypeAsync(characteristics);

        return new ContributionFeatureOptionsDto
        {
            Characteristics = characteristics,
            Amenities = amenities,
            StayAmenities = stayAmenities,
            CharacteristicIdsByPlaceType = characteristicIdsByPlaceType,
            AmenityIdsByPlaceType = BuildContributionAmenityIdsByPlaceType(allAmenityIds)
        };
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

        if (type == PlaceContributionType.ClaimOwnership)
        {
            if (!request.PlaceId.HasValue)
                throw new InvalidOperationException("PlaceId is required for ownership claims.");

            targetPlace = await LoadOwnershipTargetPlaceAsync(request.PlaceId.Value);

            if (targetPlace is null)
                throw new KeyNotFoundException("Target place not found.");

            if (targetPlace.PlaceType is null || !targetPlace.PlaceType.IsOwnable)
                throw new InvalidOperationException("Ownership can only be requested for ownable place types.");

            var alreadyOwner = await HasProofBackedOwnershipAsync(userId, request.PlaceId.Value);
            if (alreadyOwner)
                throw new InvalidOperationException("You already own this place.");

            var activeClaim = await _db.PlaceContributions
                .Include(c => c.Images)
                .Where(c =>
                    c.UserId == userId &&
                    c.PlaceId == request.PlaceId.Value &&
                    c.Type == PlaceContributionType.ClaimOwnership &&
                    c.Status == PlaceContributionStatus.PendingReview)
                .OrderByDescending(c => c.SubmittedAt)
                .ThenByDescending(c => c.Id)
                .FirstOrDefaultAsync();

            if (activeClaim is not null)
            {
                await BackfillMissingOwnershipSnapshotAsync(activeClaim, targetPlace, now);
                await _db.SaveChangesAsync();

                var existing = await BaseContributionQuery().SingleAsync(c => c.Id == activeClaim.Id);
                return MapDetail(existing, includeReviewNote: false);
            }
        }
        else
        {
            Validate(request);
            await ValidateRequiredOpeningHoursAsync(type, request);
            references = await ResolveContributionReferencesAsync(request);
        }

        var requiresOwnershipProof = RequiresOwnershipProof(type);
        if (requiresOwnershipProof &&
            ownershipProofBytes is null or { Length: 0 })
        {
            throw new InvalidOperationException("Ownership proof document is required.");
        }

        var allowsOwnershipProof = await AllowsOwnershipProofAsync(type, request);
        if (!allowsOwnershipProof)
        {
            ownershipProofBytes = null;
            ownershipProofOriginalFileName = null;
            ownershipProofContentType = null;
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

        var operatingHours = type == PlaceContributionType.ClaimOwnership
            ? BuildOperatingHoursFromPlace(targetPlace!)
            : NormalizeOperatingHours(request.OperatingHoursEnabled, request.OperatingHours);

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
            City = NormalizeOptional(type == PlaceContributionType.ClaimOwnership ? null : request.City),
            Region = NormalizeOptional(type == PlaceContributionType.ClaimOwnership ? null : request.Region),
            Country = NormalizeOptional(type == PlaceContributionType.ClaimOwnership ? null : request.Country),
            OpeningHours = operatingHours.Enabled
                ? BuildOpeningHoursSummary(operatingHours.Hours)
                : null,
            OperatingHoursEnabled = operatingHours.Enabled,
            OperatingHoursJson = operatingHours.Json,
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
            Website = type == PlaceContributionType.ClaimOwnership
                ? NormalizeWebsite(targetPlace!.Website)
                : NormalizeWebsite(request.Website),
            PricePerNight = type == PlaceContributionType.ClaimOwnership
                ? targetPlace!.Stay?.PricePerNight
                : NormalizePricePerNight(request.PricePerNight),
            CheckInTime = type == PlaceContributionType.ClaimOwnership
                ? targetPlace!.Stay?.CheckInTime
                : NormalizeOptional(request.CheckInTime),
            CheckOutTime = type == PlaceContributionType.ClaimOwnership
                ? targetPlace!.Stay?.CheckOutTime
                : NormalizeOptional(request.CheckOutTime),
            TagsJson = Serialize(type == PlaceContributionType.ClaimOwnership ? BuildOwnershipTags(targetPlace!) : NormalizeStringList(request.Tags, 80, 30)),
            AmenitiesJson = Serialize(type == PlaceContributionType.ClaimOwnership ? BuildOwnershipPlaceAmenities(targetPlace!) : references!.PlaceAmenities),
            StayAmenitiesJson = Serialize(type == PlaceContributionType.ClaimOwnership ? BuildOwnershipStayAmenities(targetPlace!) : references!.StayAmenities),
            BookingPartnersJson = Serialize(type == PlaceContributionType.ClaimOwnership ? BuildOwnershipBookingPartners(targetPlace!) : NormalizeBookingPartners(request.BookingPartners)),
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

        var initialImages = type == PlaceContributionType.ClaimOwnership
            ? BuildOwnershipImages(targetPlace!)
            : request.Images;

        SyncContributionImages(contribution, initialImages, storedCardImageUrl, storedCoverImageUrl, storedGalleryImageUrls, now, type != PlaceContributionType.ClaimOwnership);

        _db.PlaceContributions.Add(contribution);
        await _db.SaveChangesAsync();
        await _adminNotificationService.CreateTerritoryContributionPendingReviewNotificationAsync(contribution);
        await _adminNotificationService.CreateRegionalContributionPendingReviewNotificationAsync(contribution);
        await _adminNotificationService.CreateContentManagerContributionPendingReviewNotificationAsync(contribution);

        var saved = await BaseContributionQuery().SingleAsync(c => c.Id == contribution.Id);
        return MapDetail(saved, includeReviewNote: false);
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

        var now = DateTime.UtcNow;
        var type = ParseType(request.Type);
        var isApprovedPublishedEdit =
            contribution.Status == PlaceContributionStatus.Approved &&
            type == PlaceContributionType.UpdatePlace &&
            contribution.PlaceId.HasValue &&
            request.PlaceId == contribution.PlaceId;
        var isRejectedResubmission = contribution.Status == PlaceContributionStatus.Rejected;

        if (contribution.Status != PlaceContributionStatus.PendingReview &&
            !isApprovedPublishedEdit &&
            !isRejectedResubmission)
        {
            throw new InvalidOperationException("Only pending, rejected, and owned approved places can be edited.");
        }

        Place? targetPlace = null;
        ContributionReferenceResolution? references = null;

        var requiresOwnershipProof = RequiresOwnershipProof(type);
        var allowsOwnershipProof = await AllowsOwnershipProofAsync(type, request);
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

            targetPlace = await LoadOwnershipTargetPlaceAsync(request.PlaceId.Value);

            if (targetPlace is null)
                throw new KeyNotFoundException("Target place not found.");

            if (targetPlace.PlaceType is null || !targetPlace.PlaceType.IsOwnable)
                throw new InvalidOperationException("Ownership can only be requested for ownable place types.");

            var alreadyOwner = await HasProofBackedOwnershipAsync(userId, request.PlaceId.Value);
            if (alreadyOwner)
                throw new InvalidOperationException("You already own this place.");

            Validate(request);
            references = await ResolveContributionReferencesAsync(request);
        }
        else
        {
            Validate(request);
            await ValidateRequiredOpeningHoursAsync(type, request);
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
        else if (!allowsOwnershipProof && !string.IsNullOrWhiteSpace(contribution.OwnershipProofDocumentUrl))
        {
            await _mediaStorage.DeleteAsync(contribution.OwnershipProofDocumentUrl);
            contribution.OwnershipProofDocumentUrl = null;
            contribution.OwnershipProofOriginalFileName = null;
            contribution.OwnershipProofContentType = null;
            contribution.OwnershipProofUploadedAt = null;
        }

        contribution.Type = isApprovedPublishedEdit ? contribution.Type : type;
        contribution.PlaceId = type is PlaceContributionType.UpdatePlace or PlaceContributionType.ClaimOwnership
            ? request.PlaceId
            : null;
        contribution.OriginalSnapshotJson = type is PlaceContributionType.UpdatePlace or PlaceContributionType.ClaimOwnership && request.PlaceId.HasValue
            ? await BuildOriginalSnapshotAsync(request.PlaceId.Value)
            : "{}";
        contribution.PlaceTypeId = references!.PlaceTypeId;
        contribution.LocationId = references!.LocationId;
        contribution.RestaurantTypeId = request.RestaurantTypeId;
        contribution.StayTypeId = references!.StayTypeId;
        contribution.Name = NormalizeRequired(request.Name, "Name");
        contribution.Subtitle = NormalizeOptional(request.Subtitle);
        contribution.Address = NormalizeOptional(request.Address);
        contribution.City = NormalizeOptional(request.City);
        contribution.Region = NormalizeOptional(request.Region);
        contribution.Country = NormalizeOptional(request.Country);
        var operatingHours = NormalizeOperatingHours(request.OperatingHoursEnabled, request.OperatingHours);
        contribution.OpeningHours = operatingHours.Enabled
            ? BuildOpeningHoursSummary(operatingHours.Hours)
            : null;
        contribution.OperatingHoursEnabled = operatingHours.Enabled;
        contribution.OperatingHoursJson = operatingHours.Json;
        contribution.Description = NormalizeDescription(request.Description);
        contribution.Latitude = references!.Latitude;
        contribution.Longitude = references!.Longitude;
        contribution.ContactEmail = NormalizeContactEmail(request.ContactEmail);
        contribution.PhoneNumber = NormalizeOptional(request.PhoneNumber ?? request.Phone);
        contribution.Website = NormalizeWebsite(request.Website);
        contribution.PricePerNight = NormalizePricePerNight(request.PricePerNight);
        contribution.CheckInTime = NormalizeOptional(request.CheckInTime);
        contribution.CheckOutTime = NormalizeOptional(request.CheckOutTime);
        contribution.TagsJson = Serialize(NormalizeStringList(request.Tags, 80, 30));
        contribution.AmenitiesJson = Serialize(references!.PlaceAmenities);
        contribution.StayAmenitiesJson = Serialize(references!.StayAmenities);
        contribution.BookingPartnersJson = Serialize(NormalizeBookingPartners(request.BookingPartners));
        contribution.UpdatedAt = now;

        if (isRejectedResubmission)
        {
            contribution.Status = PlaceContributionStatus.PendingReview;
            contribution.ReviewedByUserId = null;
            contribution.ReviewedAt = null;
            contribution.ReviewNote = null;
            contribution.SubmittedAt = now;
        }

        var effectiveRequestImages = isApprovedPublishedEdit
            ? await EnsurePublishedPlaceImageFallbacksAsync(
                contribution.PlaceId!.Value,
                request.Images,
                !string.IsNullOrWhiteSpace(storedCardImageUrl),
                !string.IsNullOrWhiteSpace(storedCoverImageUrl))
            : type == PlaceContributionType.ClaimOwnership && request.Images is null && targetPlace is not null
                ? BuildOwnershipImages(targetPlace)
            : request.Images;

        SyncContributionImages(contribution, effectiveRequestImages, storedCardImageUrl, storedCoverImageUrl, storedGalleryImageUrls, now, type != PlaceContributionType.ClaimOwnership);

        if (isApprovedPublishedEdit)
            await ApplyUpdateToExistingPlaceAsync(contribution, contribution.PlaceId!.Value, now);

        await _db.SaveChangesAsync();

        if (isRejectedResubmission)
        {
            await _adminNotificationService.CreateTerritoryContributionPendingReviewNotificationAsync(contribution);
            await _adminNotificationService.CreateRegionalContributionPendingReviewNotificationAsync(contribution);
            await _adminNotificationService.CreateContentManagerContributionPendingReviewNotificationAsync(contribution);
        }

        var saved = await BaseContributionQuery().SingleAsync(c => c.Id == contribution.Id);
        return MapDetail(saved, includeReviewNote: false);
    }

    public async Task DeleteAsync(int userId, int contributionId)
    {
        var contribution = await _db.PlaceContributions
            .Include(c => c.Images)
            .SingleOrDefaultAsync(c => c.Id == contributionId && c.UserId == userId);

        if (contribution is null)
            throw new InvalidOperationException("Contribution was not found.");

        if (contribution.Status is not (PlaceContributionStatus.PendingReview or PlaceContributionStatus.Rejected))
            throw new InvalidOperationException("Only pending or rejected contributions can be deleted.");

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

    public async Task<PlaceContributionDetailDto> ApproveAsync(
        int contributionId,
        int reviewerUserId,
        string? note,
        string? internalNote = null,
        string? sourceArea = null)
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
        var previousStatus = contribution.Status.ToString();
        var publicNote = NormalizeOptional(note);
        var normalizedInternalNote = NormalizeOptional(internalNote);

        if (contribution.Type == PlaceContributionType.CreatePlace)
        {
            var publishedPlace = await PublishNewPlaceAsync(contribution, now);
            contribution.PlaceId = publishedPlace.Id;

            // Only grant ownership if an ownership proof document was provided.
            // Without a proof doc the user submitted the place but did not claim ownership.
            if (!string.IsNullOrWhiteSpace(contribution.OwnershipProofDocumentUrl))
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

            var targetPlace = await LoadOwnershipTargetPlaceAsync(contribution.PlaceId.Value)
                ?? throw new InvalidOperationException("The place you are trying to claim does not exist.");

            await BackfillMissingOwnershipSnapshotAsync(contribution, targetPlace, now);
            await ApplyUpdateToExistingPlaceAsync(contribution, contribution.PlaceId.Value, now);
            await EnsureUserPlaceAsync(contribution.UserId, contribution.PlaceId.Value, now);
        }
        else
        {
            throw new InvalidOperationException("Unsupported contribution type.");
        }

        contribution.Status = PlaceContributionStatus.Approved;
        contribution.ReviewedByUserId = reviewerUserId;
        contribution.ReviewedAt = now;
        contribution.ReviewNote = publicNote;
        contribution.UpdatedAt = now;

        await _auditService.RecordAsync(
            reviewerUserId,
            "PlaceContribution",
            contribution.Id,
            "ApproveContribution",
            previousStatus,
            contribution.Status.ToString(),
            publicNote,
            normalizedInternalNote,
            sourceArea ?? "PlaceContributionReview");

        await _notificationService.AddContributionDecisionNotificationAsync(contribution);

        await _db.SaveChangesAsync();

        var saved = await BaseContributionQuery().SingleAsync(c => c.Id == contribution.Id);
        return MapDetail(saved, includeReviewNote: true);
    }

    public async Task<ContributionRegionOptionsDto> GetRegionOptionsAsync()
    {
        var regions = await _db.Regions
            .AsNoTracking()
            .OrderBy(r => r.Name)
            .Select(r => new ContributionRegionOptionDto
            {
                Id = r.Id,
                Name = r.Name,
                TerritoryName = r.Territory != null ? r.Territory.Name : null
            })
            .ToListAsync();

        return new ContributionRegionOptionsDto { Regions = regions };
    }

    public async Task<ContentManagerDestinationCenterValidationResultDto> ValidateDestinationCenterAsync(
        int regionId,
        double latitude,
        double longitude)
    {
        if (!HasUsableCoordinates(latitude, longitude))
        {
            return new ContentManagerDestinationCenterValidationResultDto
            {
                IsAllowed = false,
                Message = "Select a valid destination centre on the map."
            };
        }

        var boundary = await _db.Regions
            .AsNoTracking()
            .Where(region => region.Id == regionId)
            .Select(region => EF.Property<Geometry?>(region, BoundaryGeometryPropertyName))
            .SingleOrDefaultAsync();

        if (boundary is null || boundary.IsEmpty || !boundary.IsValid)
        {
            return new ContentManagerDestinationCenterValidationResultDto
            {
                IsAllowed = false,
                Message = "The selected region does not have a valid boundary. Contact an administrator."
            };
        }

        var point = Wgs84GeometryFactory.CreatePoint(new Coordinate(longitude, latitude));
        point.SRID = 4326;
        var isAllowed = boundary.Covers(point);

        return new ContentManagerDestinationCenterValidationResultDto
        {
            IsAllowed = isAllowed,
            Message = isAllowed
                ? "Destination centre is inside the selected region."
                : "Destination centre must be inside the selected region."
        };
    }

    public async Task<ContributionLocationOptionsDto> GetDestinationLocationOptionsAsync(int destinationId)
    {
        var destination = await _db.Destinations
            .AsNoTracking()
            .Where(d => d.Id == destinationId)
            .Select(d => new { d.Id, d.Name })
            .FirstOrDefaultAsync()
            ?? throw new KeyNotFoundException("Destination not found.");

        var locations = await _db.Locations
            .AsNoTracking()
            .Where(location => location.DestinationId == destinationId && location.Status == "Published")
            .OrderBy(location => location.Name)
            .Select(location => new ContributionLocationOptionDto
            {
                Id = location.Id,
                Name = location.Name,
                DestinationId = destination.Id,
                DestinationName = destination.Name,
                Status = location.Status
            })
            .ToListAsync();

        return new ContributionLocationOptionsDto { Locations = locations };
    }

    private async Task<PlaceContributionDetailDto> MapContentManagerDetailAsync(PlaceContribution contribution)
    {
        var detail = MapDetail(contribution, includeReviewNote: true);
        await ApplyContentManagerSuggestionsAsync(detail, contribution);
        return detail;
    }

    private async Task ApplyContentManagerSuggestionsAsync(
        PlaceContributionDetailDto detail,
        PlaceContribution contribution)
    {
        detail.SuggestedDestinationName =
            NormalizeOptional(contribution.City) ??
            NormalizeOptional(contribution.Location?.Destination?.Name);
        detail.SuggestedLocationName = SuggestedLocationName(contribution);

        var contributedRegionName =
            NormalizeOptional(contribution.Region) ??
            NormalizeOptional(contribution.Location?.Destination?.Region?.Name);

        if (string.IsNullOrWhiteSpace(contributedRegionName))
            return;

        var regions = await _db.Regions
            .AsNoTracking()
            .Select(region => new ContributionRegionOptionDto
            {
                Id = region.Id,
                Name = region.Name,
                TerritoryName = region.Territory != null ? region.Territory.Name : null
            })
            .ToListAsync();

        var suggestedRegion = FindBestRegionMatch(regions, contributedRegionName);
        if (suggestedRegion is null)
            return;

        detail.SuggestedRegionId = suggestedRegion.Id;
        detail.SuggestedRegionName = suggestedRegion.Name;
        detail.RegionDisplayName ??= BuildContributionRegionDisplayName(suggestedRegion.Name, suggestedRegion.TerritoryName);
    }

    public async Task<PlaceContributionDetailDto> ApproveForContentManagerAsync(
        int contributionId,
        int reviewerUserId,
        int regionId,
        int? existingDestinationId,
        string? newDestinationName,
        double? newDestinationLatitude,
        double? newDestinationLongitude,
        string? newLocationName,
        string? note)
    {
        var contribution = await _db.PlaceContributions
            .Include(c => c.Images)
            .SingleOrDefaultAsync(c => c.Id == contributionId);

        if (contribution is null)
            throw new InvalidOperationException("Contribution was not found.");

        if (contribution.Status != PlaceContributionStatus.PendingReview)
            throw new InvalidOperationException("Only pending contributions can be approved.");

        var isContentManager = await IsContentManagerAsync(reviewerUserId);
        var managesAll = await ReviewerManagesAllAsync(reviewerUserId);
        if (!isContentManager && !managesAll)
            throw new UnauthorizedAccessException("Only content managers can use this approval method.");

        // Claim-ownership and update contributions reference an existing place that already has
        // a location assigned, so they bypass the region/destination/location assignment flow.
        if (contribution.Type is PlaceContributionType.ClaimOwnership or PlaceContributionType.UpdatePlace)
            return await ApproveExistingPlaceForContentManagerAsync(contribution, reviewerUserId, note, DateTime.UtcNow);

        var region = await _db.Regions
            .AsNoTracking()
            .Where(r => r.Id == regionId)
            .Select(r => new { r.Id, r.Name })
            .FirstOrDefaultAsync()
            ?? throw new KeyNotFoundException("Region not found.");

        if (!contribution.Latitude.HasValue || !contribution.Longitude.HasValue)
            throw new InvalidOperationException("Contribution is missing coordinates.");

        if (!contribution.PlaceTypeId.HasValue)
            throw new InvalidOperationException("Contribution is missing place type.");

        var now = DateTime.UtcNow;

        if (!existingDestinationId.HasValue || existingDestinationId.Value <= 0)
        {
            throw new InvalidOperationException(
                "An existing destination with a valid boundary is required. Create the destination boundary before approving this legacy contribution.");
        }

        var resolvedDestination = await _destinationBoundaryResolver.ResolvePublicAsync(
            contribution.Latitude.Value,
            contribution.Longitude.Value);
        if (!resolvedDestination.IsResolved || !resolvedDestination.DestinationId.HasValue)
            throw new InvalidOperationException(resolvedDestination.Message);

        if (resolvedDestination.RegionId != regionId || resolvedDestination.DestinationId != existingDestinationId.Value)
        {
            throw new InvalidOperationException(
                "The selected destination does not contain the contribution coordinates.");
        }

        var destination = await _db.Destinations
            .SingleOrDefaultAsync(d =>
                d.Id == existingDestinationId.Value &&
                d.RegionId == regionId &&
                d.Status == "Published")
            ?? throw new KeyNotFoundException("Published destination not found in the selected region.");

        var locationName = NormalizeOptional(newLocationName)
            ?? throw new InvalidOperationException("Location name is required.");
        var location = await _db.Locations
            .Where(l => l.DestinationId == destination.Id && l.Name.ToLower() == locationName.ToLowerInvariant())
            .OrderBy(l => l.Id)
            .FirstOrDefaultAsync();

        bool locationIsNew = location is null;
        if (locationIsNew)
        {
            var locationType = await ResolveLocationTypeAsync();
            location = new Location
            {
                Name = locationName,
                SearchText = locationName.ToLowerInvariant(),
                Description = string.Empty,
                Latitude = contribution.Latitude.Value,
                Longitude = contribution.Longitude.Value,
                DestinationId = destination.Id,
                LocationTypeId = locationType.Id,
                Status = "Published",
                CreatedAt = now,
                UpdatedAt = now
            };
            _db.Locations.Add(location);
            await _db.SaveChangesAsync();
        }

        contribution.LocationId = location.Id;

        if (contribution.Type == PlaceContributionType.CreatePlace)
        {
            var publishedPlace = await PublishNewPlaceAsync(contribution, now);
            contribution.PlaceId = publishedPlace.Id;

            if (!string.IsNullOrWhiteSpace(contribution.OwnershipProofDocumentUrl))
                await EnsureUserPlaceAsync(contribution.UserId, publishedPlace.Id, now);
        }
        else
        {
            throw new InvalidOperationException("Only CreatePlace contributions can be approved via this method.");
        }

        contribution.Status = PlaceContributionStatus.Approved;
        contribution.ReviewedByUserId = reviewerUserId;
        contribution.ReviewedAt = now;
        contribution.ReviewNote = NormalizeOptional(note);
        contribution.UpdatedAt = now;

        await _notificationService.AddContributionDecisionNotificationAsync(contribution);
        await _db.SaveChangesAsync();

        var saved = await BaseContributionQuery().SingleAsync(c => c.Id == contribution.Id);
        var result = MapDetail(saved, includeReviewNote: true);
        if (locationIsNew)
            result.CreatedLocationId = location.Id;
        return result;
    }

    /// <summary>
    /// Approves a contribution that targets an existing place (claim-ownership or update) on behalf
    /// of a content manager. These contributions already carry a place and location, so the region/
    /// destination/location assignment used for new places does not apply here.
    /// </summary>
    private async Task<PlaceContributionDetailDto> ApproveExistingPlaceForContentManagerAsync(
        PlaceContribution contribution,
        int reviewerUserId,
        string? note,
        DateTime now)
    {
        if (contribution.Type == PlaceContributionType.UpdatePlace)
        {
            if (!contribution.PlaceId.HasValue)
                throw new InvalidOperationException("Update contribution is missing target place.");

            await ApplyUpdateToExistingPlaceAsync(contribution, contribution.PlaceId.Value, now);
        }
        else if (contribution.Type == PlaceContributionType.ClaimOwnership)
        {
            if (!contribution.PlaceId.HasValue)
                throw new InvalidOperationException("Ownership claim is missing target place.");

            var targetPlace = await LoadOwnershipTargetPlaceAsync(contribution.PlaceId.Value)
                ?? throw new InvalidOperationException("The place you are trying to claim does not exist.");

            await BackfillMissingOwnershipSnapshotAsync(contribution, targetPlace, now);
            await ApplyUpdateToExistingPlaceAsync(contribution, contribution.PlaceId.Value, now);
            await EnsureUserPlaceAsync(contribution.UserId, contribution.PlaceId.Value, now);
        }
        else
        {
            throw new InvalidOperationException("Only claim-ownership and update contributions are supported here.");
        }

        contribution.Status = PlaceContributionStatus.Approved;
        contribution.ReviewedByUserId = reviewerUserId;
        contribution.ReviewedAt = now;
        contribution.ReviewNote = NormalizeOptional(note);
        contribution.UpdatedAt = now;

        await _notificationService.AddContributionDecisionNotificationAsync(contribution);
        await _db.SaveChangesAsync();

        var saved = await BaseContributionQuery().SingleAsync(c => c.Id == contribution.Id);
        return MapDetail(saved, includeReviewNote: true);
    }

    public async Task AddDestinationCardImageAsync(int destinationId, byte[] imageBytes)
    {
        var destination = await _db.Destinations.FindAsync(destinationId)
            ?? throw new KeyNotFoundException("Destination not found.");

        var stored = await _mediaStorage.StoreAsync(0, imageBytes);
        var now = DateTime.UtcNow;

        var existing = await _db.EntityImages.FirstOrDefaultAsync(i =>
            i.DestinationId == destinationId && i.ImageRole == EntityImageRole.Card);

        if (existing is not null)
        {
            existing.Url = stored.MediaUrl;
            existing.UpdatedAt = now;
        }
        else
        {
            _db.EntityImages.Add(new EntityImage
            {
                TargetType = EntityImageTargetType.Destination,
                DestinationId = destinationId,
                Url = stored.MediaUrl,
                AltText = destination.Name,
                IsMain = true,
                ImageRole = EntityImageRole.Card,
                SortOrder = 0,
                CreatedAt = now
            });
        }

        destination.UpdatedAt = now;
        await _db.SaveChangesAsync();
    }

    public async Task AddLocationCardImageAsync(int locationId, byte[] imageBytes)
    {
        var location = await _db.Locations.FindAsync(locationId)
            ?? throw new KeyNotFoundException("Location not found.");

        var stored = await _mediaStorage.StoreAsync(0, imageBytes);
        var now = DateTime.UtcNow;

        var existing = await _db.EntityImages.FirstOrDefaultAsync(i =>
            i.LocationId == locationId && i.ImageRole == EntityImageRole.Card);

        if (existing is not null)
        {
            existing.Url = stored.MediaUrl;
            existing.UpdatedAt = now;
        }
        else
        {
            _db.EntityImages.Add(new EntityImage
            {
                TargetType = EntityImageTargetType.Location,
                LocationId = locationId,
                Url = stored.MediaUrl,
                AltText = location.Name,
                IsMain = true,
                ImageRole = EntityImageRole.Card,
                SortOrder = 0,
                CreatedAt = now
            });
        }

        location.UpdatedAt = now;
        await _db.SaveChangesAsync();
    }

    public async Task<PlaceContributionDetailDto> RejectAsync(
        int contributionId,
        int reviewerUserId,
        string? note,
        string? internalNote = null,
        string? sourceArea = null)
    {
        var contribution = await _db.PlaceContributions
            .SingleOrDefaultAsync(c => c.Id == contributionId);

        if (contribution is null)
            throw new InvalidOperationException("Contribution was not found.");

        if (contribution.Status != PlaceContributionStatus.PendingReview)
            throw new InvalidOperationException("Only pending contributions can be rejected.");

        await EnsureReviewerCanReviewAsync(reviewerUserId, contribution);

        var now = DateTime.UtcNow;
        var previousStatus = contribution.Status.ToString();
        var publicNote = NormalizeOptional(note);
        var normalizedInternalNote = NormalizeOptional(internalNote);

        contribution.Status = PlaceContributionStatus.Rejected;
        contribution.ReviewedByUserId = reviewerUserId;
        contribution.ReviewedAt = now;
        contribution.ReviewNote = publicNote;
        contribution.UpdatedAt = now;

        await _auditService.RecordAsync(
            reviewerUserId,
            "PlaceContribution",
            contribution.Id,
            "RejectContribution",
            previousStatus,
            contribution.Status.ToString(),
            publicNote,
            normalizedInternalNote,
            sourceArea ?? "PlaceContributionReview");

        await _notificationService.AddContributionDecisionNotificationAsync(contribution);

        await _db.SaveChangesAsync();

        var saved = await BaseContributionQuery().SingleAsync(c => c.Id == contribution.Id);
        return MapDetail(saved, includeReviewNote: true);
    }

    private IQueryable<PlaceContribution> BaseContributionQuery()
    {
        return _db.PlaceContributions
            .AsNoTracking()
            .Include(c => c.PlaceType)
            .Include(c => c.Location)
                .ThenInclude(l => l!.Destination)
                    .ThenInclude(d => d.Region)
                        .ThenInclude(r => r.Territory)
            .Include(c => c.StayType)
            .Include(c => c.Images)
            .Include(c => c.User)
            .Include(c => c.Place)
                .ThenInclude(p => p!.Location)
                    .ThenInclude(l => l.Destination)
                        .ThenInclude(d => d.Region)
                            .ThenInclude(r => r.Territory)
            .Include(c => c.Place)
                .ThenInclude(p => p!.Images)
            .AsSplitQuery();
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
            ((c.Type != PlaceContributionType.UpdatePlace && c.Type != PlaceContributionType.ClaimOwnership) ||
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
            if (RequiresTargetPlaceReviewScope(contribution.Type))
                _ = await ResolveTargetPlaceRegionIdAsync(contribution);

            return;
        }

        var isContentManager = await IsContentManagerAsync(reviewerUserId);
        if (isContentManager)
        {
            if (RequiresTargetPlaceReviewScope(contribution.Type))
                _ = await ResolveTargetPlaceRegionIdAsync(contribution);

            return;
        }

        if (!contribution.LocationId.HasValue)
        {
            throw new UnauthorizedAccessException("This contribution has no assigned region and can only be reviewed by a content manager.");
        }

        var contributionRegionIdForCheck = await ResolveContributionRegionIdAsync(contribution);
        var regionHasManager = await _db.RegionalManagerRegions
            .AsNoTracking()
            .AnyAsync(r => r.RegionId == contributionRegionIdForCheck && r.IsActive);

        if (!regionHasManager)
        {
            throw new UnauthorizedAccessException("This contribution's region has no assigned manager and can only be reviewed by a content manager.");
        }

        var regionIds = await GetReviewerRegionIdsAsync(reviewerUserId);
        if (regionIds.Count == 0)
            throw new UnauthorizedAccessException("Regional manager has no assigned regions.");

        var contributionRegionId = await ResolveContributionRegionIdAsync(contribution);

        if (!regionIds.Contains(contributionRegionId))
            throw new UnauthorizedAccessException("You are not assigned to this contribution's region.");

        if (!RequiresTargetPlaceReviewScope(contribution.Type))
            return;

        var targetRegionId = await ResolveTargetPlaceRegionIdAsync(contribution);
        if (!regionIds.Contains(targetRegionId))
            throw new UnauthorizedAccessException("You are not assigned to this contribution's target place region.");

        if (targetRegionId != contributionRegionId)
            throw new UnauthorizedAccessException("Cross-region update contributions require admin review.");
    }

    private async Task<bool> IsContentManagerAsync(int reviewerUserId)
    {
        return await _db.Users
            .AsNoTracking()
            .Where(u => u.Id == reviewerUserId)
            .Select(u => u.UserRoles.Any(ur => ur.Role.Name == RoleNames.ContentModerator))
            .SingleOrDefaultAsync();
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
            throw new InvalidOperationException("Target place is required for this contribution.");

        var regionId = await _db.Places
            .AsNoTracking()
            .Where(place => place.Id == contribution.PlaceId.Value)
            .Select(place => (int?)place.Location.Destination.RegionId)
            .FirstOrDefaultAsync();

        return regionId ?? throw new KeyNotFoundException("Target place not found.");
    }

    private static bool RequiresTargetPlaceReviewScope(PlaceContributionType type) =>
        type is PlaceContributionType.UpdatePlace or PlaceContributionType.ClaimOwnership;

    private static bool RequiresOwnershipProof(PlaceContributionType contributionType) =>
        contributionType == PlaceContributionType.ClaimOwnership;

    private async Task ValidateRequiredOpeningHoursAsync(
        PlaceContributionType contributionType,
        UpsertPlaceContributionDto request)
    {
        if (contributionType == PlaceContributionType.ClaimOwnership)
            return;

        var placeTypeName = await ResolveRequestedPlaceTypeNameAsync(request);
        if (placeTypeName is null || !OpeningHoursRequiredPlaceTypes.Contains(placeTypeName))
            return;

        if (!request.OperatingHoursEnabled)
            throw new InvalidOperationException("Opening hours are required for this place type.");
    }

    private async Task<bool> AllowsOwnershipProofAsync(
        PlaceContributionType contributionType,
        UpsertPlaceContributionDto request)
    {
        return contributionType == PlaceContributionType.ClaimOwnership ||
            await ResolveRequestedPlaceTypeOwnableAsync(request);
    }

    private async Task<string?> ResolveRequestedPlaceTypeNameAsync(UpsertPlaceContributionDto request)
    {
        var requestedPlaceType = NormalizeOptional(request.PlaceType);
        if (requestedPlaceType is not null)
            return requestedPlaceType;

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
            var placeType = await _db.PlaceTypes
                .AsNoTracking()
                .Where(placeType => placeType.Id == request.PlaceTypeId.Value)
                .Select(placeType => new { placeType.Name, placeType.IsOwnable })
                .SingleOrDefaultAsync();

            if (placeType is null)
                throw new KeyNotFoundException("Place type not found.");

            return placeType.IsOwnable || LegacyOwnablePlaceTypes.Contains(placeType.Name);
        }

        var placeTypeName = NormalizeOptional(request.PlaceType);
        if (placeTypeName is null && !string.IsNullOrWhiteSpace(request.StayType))
        {
            placeTypeName = "stay";
        }

        if (placeTypeName is null)
            return false;

        var dbIsOwnable = await _db.PlaceTypes
            .AsNoTracking()
            .Where(placeType => placeType.Name.ToLower() == placeTypeName)
            .Select(placeType => (bool?)placeType.IsOwnable)
            .SingleOrDefaultAsync();

        return dbIsOwnable ?? LegacyOwnablePlaceTypes.Contains(placeTypeName);
    }

    private async Task<bool> UserCanManagePlaceAsync(int userId, int placeId)
    {
        return await _db.UserPlaces
            .AsNoTracking()
            .AnyAsync(userPlace => userPlace.UserId == userId && userPlace.PlaceId == placeId);
    }

    /// <summary>
    /// Returns true only when the user has an approved contribution with an ownership proof
    /// document for this place. A contribution approved without a proof doc does NOT count
    /// as real ownership — the user may still submit a ClaimOwnership request.
    /// </summary>
    private async Task<bool> HasProofBackedOwnershipAsync(int userId, int placeId)
    {
        return await _db.PlaceContributions
            .AsNoTracking()
            .AnyAsync(c =>
                c.PlaceId == placeId &&
                c.UserId == userId &&
                c.Status == PlaceContributionStatus.Approved &&
                c.OwnershipProofDocumentUrl != null);
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

    private async Task<Place?> LoadOwnershipTargetPlaceAsync(int placeId)
    {
        return await _db.Places
            .AsNoTracking()
            .Include(p => p.PlaceType)
            .Include(p => p.Location)
                .ThenInclude(location => location.Destination)
                    .ThenInclude(destination => destination.Region)
            .Include(p => p.Stay)
                .ThenInclude(stay => stay!.StayType)
            .Include(p => p.Stay)
                .ThenInclude(stay => stay!.StayAmenities)
                    .ThenInclude(stayAmenity => stayAmenity.StayAmenity)
            .Include(p => p.Images)
            .Include(p => p.PlaceTags)
                .ThenInclude(placeTag => placeTag.Tag)
            .Include(p => p.PlaceAmenities)
                .ThenInclude(placeAmenity => placeAmenity.Amenity)
            .Include(p => p.PlaceBookingPartners)
                .ThenInclude(placeBookingPartner => placeBookingPartner.BookingPartner)
            .AsSplitQuery()
            .SingleOrDefaultAsync(p => p.Id == placeId);
    }

    private async Task BackfillMissingOwnershipSnapshotAsync(
        PlaceContribution contribution,
        Place targetPlace,
        DateTime now)
    {
        if (contribution.Type != PlaceContributionType.ClaimOwnership)
            return;

        contribution.PlaceTypeId ??= targetPlace.PlaceTypeId;
        contribution.LocationId ??= targetPlace.LocationId;
        contribution.StayTypeId ??= targetPlace.Stay?.StayTypeId;

        if (string.IsNullOrWhiteSpace(contribution.Name))
            contribution.Name = targetPlace.Name;
        if (string.IsNullOrWhiteSpace(contribution.Subtitle))
            contribution.Subtitle = targetPlace.Subtitle;
        if (string.IsNullOrWhiteSpace(contribution.Address))
            contribution.Address = targetPlace.Address;
        if (string.IsNullOrWhiteSpace(contribution.OpeningHours))
            contribution.OpeningHours = targetPlace.OpeningHours;
        if (!contribution.OperatingHoursEnabled && targetPlace.OperatingHoursEnabled)
        {
            contribution.OperatingHoursEnabled = true;
            contribution.OperatingHoursJson = targetPlace.OperatingHoursJson;
            contribution.OpeningHours = targetPlace.OpeningHours;
        }
        if (string.IsNullOrWhiteSpace(contribution.Description))
            contribution.Description = targetPlace.Description;
        if (!contribution.Latitude.HasValue)
            contribution.Latitude = targetPlace.Latitude;
        if (!contribution.Longitude.HasValue)
            contribution.Longitude = targetPlace.Longitude;
        if (string.IsNullOrWhiteSpace(contribution.ContactEmail))
            contribution.ContactEmail = targetPlace.ContactEmail;
        if (string.IsNullOrWhiteSpace(contribution.PhoneNumber))
            contribution.PhoneNumber = targetPlace.Number;
        if (string.IsNullOrWhiteSpace(contribution.Website))
            contribution.Website = NormalizeWebsite(targetPlace.Website);
        if (!contribution.PricePerNight.HasValue)
            contribution.PricePerNight = targetPlace.Stay?.PricePerNight;
        if (string.IsNullOrWhiteSpace(contribution.CheckInTime))
            contribution.CheckInTime = targetPlace.Stay?.CheckInTime;
        if (string.IsNullOrWhiteSpace(contribution.CheckOutTime))
            contribution.CheckOutTime = targetPlace.Stay?.CheckOutTime;

        if (DeserializeStringList(contribution.TagsJson).Count == 0)
            contribution.TagsJson = Serialize(BuildOwnershipTags(targetPlace));
        if (DeserializeStringList(contribution.AmenitiesJson).Count == 0)
            contribution.AmenitiesJson = Serialize(BuildOwnershipPlaceAmenities(targetPlace));
        if (DeserializeStringList(contribution.StayAmenitiesJson).Count == 0)
            contribution.StayAmenitiesJson = Serialize(BuildOwnershipStayAmenities(targetPlace));
        if (DeserializeBookingPartners(contribution.BookingPartnersJson).Count == 0)
            contribution.BookingPartnersJson = Serialize(BuildOwnershipBookingPartners(targetPlace));

        if (contribution.Images.Count == 0)
            SyncContributionImages(contribution, BuildOwnershipImages(targetPlace), null, null, Array.Empty<string>(), now, requireCardAndCover: false);

        contribution.UpdatedAt = now;
    }

    private async Task<string> BuildOriginalSnapshotAsync(int placeId)
    {
        var place = await _db.Places
            .AsNoTracking()
            .Include(p => p.Stay)
                .ThenInclude(stay => stay!.StayAmenities)
                    .ThenInclude(stayAmenity => stayAmenity.StayAmenity)
            .Include(p => p.Images)
            .Include(p => p.PlaceTags)
                .ThenInclude(pt => pt.Tag)
                    .ThenInclude(tag => tag.TagType)
                        .ThenInclude(tagType => tagType!.PlaceTypes)
            .Include(p => p.PlaceAmenities)
                .ThenInclude(pa => pa.Amenity)
            .Include(p => p.PlaceBookingPartners)
                .ThenInclude(pb => pb.BookingPartner)
            .AsSplitQuery()
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
            place.OperatingHoursEnabled,
            place.OperatingHoursJson,
            place.Description,
            place.Latitude,
            place.Longitude,
            place.ContactEmail,
            PhoneNumber = place.Number,
            place.Website,
            place.PlaceTypeId,
            place.LocationId,
            StayTypeId = place.Stay?.StayTypeId,
            place.Stay?.PricePerNight,
            place.Stay?.CheckInTime,
            place.Stay?.CheckOutTime,
            Tags = place.PlaceTags.Select(pt => pt.Tag.Name).ToList(),
            Amenities = place.PlaceAmenities.Select(pa => pa.Amenity.Name).ToList(),
            StayAmenities = place.Stay?.StayAmenities.Select(stayAmenity => stayAmenity.StayAmenity.Slug).ToList() ?? new List<string>(),
            BookingPartners = place.PlaceBookingPartners.Select(pb => new
            {
                pb.BookingPartnerId,
                pb.BookingPartner.Name,
                pb.Url
            }).ToList(),
            Images = place.Images.Select(image => new
            {
                image.Url,
                image.AltText,
                image.Caption,
                image.IsMain,
                image.ImageRole,
                image.SortOrder,
                image.ImagePosition,
                image.ImageSize
            }).ToList()
        };

        return JsonSerializer.Serialize(snapshot);
    }

    private static List<string> BuildOwnershipTags(Place place)
    {
        return place.PlaceTags
            .Select(placeTag => placeTag.Tag.Name)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(30)
            .ToList();
    }

    private static List<string> BuildOwnershipPlaceAmenities(Place place)
    {
        return place.PlaceAmenities
            .Select(placeAmenity => placeAmenity.Amenity.Name)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(30)
            .ToList();
    }

    private static List<string> BuildOwnershipStayAmenities(Place place)
    {
        return place.Stay?.StayAmenities
            .Select(stayAmenity => stayAmenity.StayAmenity.Slug)
            .Where(slug => !string.IsNullOrWhiteSpace(slug))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(30)
            .ToList() ?? new List<string>();
    }

    private static List<ContributionBookingPartnerDto> BuildOwnershipBookingPartners(Place place)
    {
        return place.PlaceBookingPartners
            .Where(partner => partner.BookingPartner is not null)
            .Select(partner => new ContributionBookingPartnerDto
            {
                Id = BookingPartnerContributionId(partner.BookingPartner.Name),
                Url = partner.Url ?? string.Empty
            })
            .Where(partner => !string.IsNullOrWhiteSpace(partner.Id) && !string.IsNullOrWhiteSpace(partner.Url))
            .DistinctBy(partner => partner.Id, StringComparer.OrdinalIgnoreCase)
            .Take(10)
            .ToList();
    }

    private static List<UpsertPlaceContributionImageDto> BuildOwnershipImages(Place place)
    {
        return OrderPlaceImagesForSnapshot(place.Images)
            .Select(image => new UpsertPlaceContributionImageDto
            {
                Url = image.Url,
                AltText = image.AltText,
                Caption = image.Caption,
                IsMain = image.IsMain,
                ImageRole = EntityImageRole.Normalize(image.ImageRole),
                SortOrder = image.SortOrder,
                ImagePosition = NormalizeImagePosition(image.ImagePosition),
                ImageSize = NormalizeImageSize(image.ImageSize)
            })
            .ToList();
    }

    private static IOrderedEnumerable<EntityImage> OrderPlaceImagesForSnapshot(IEnumerable<EntityImage> images)
    {
        return images
            .OrderByDescending(image => image.IsMain)
            .ThenBy(image => image.SortOrder)
            .ThenBy(image => image.CreatedAt)
            .ThenBy(image => image.Id);
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

        if (placeTypeId.HasValue)
        {
            var resolvedTypeName = await _db.PlaceTypes
                .AsNoTracking()
                .Where(pt => pt.Id == placeTypeId.Value)
                .Select(pt => pt.Name)
                .FirstOrDefaultAsync();

            if (resolvedTypeName != null && ContributionIneligiblePlaceTypes.Contains(resolvedTypeName))
                throw new InvalidOperationException($"Place type '{resolvedTypeName}' is not available for contributions.");
        }

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
        if (!HasUsableCoordinates(request.Latitude, request.Longitude))
            throw new InvalidOperationException("Please choose valid coordinates before submitting.");
        if (!request.LocationId.HasValue || request.LocationId.Value <= 0)
            throw new InvalidOperationException("Please select a location from the destination location list.");

        var latitude = request.Latitude!.Value;
        var longitude = request.Longitude!.Value;
        var destination = await _destinationBoundaryResolver.ResolvePublicAsync(latitude, longitude);
        if (!destination.IsResolved || !destination.DestinationId.HasValue)
            throw new InvalidOperationException(destination.Message);

        var location = await _db.Locations
            .AsNoTracking()
            .Where(existingLocation =>
                existingLocation.Id == request.LocationId.Value &&
                existingLocation.DestinationId == destination.DestinationId.Value &&
                existingLocation.Status == "Published")
            .Select(existingLocation => new { existingLocation.Id })
            .SingleOrDefaultAsync()
            ?? throw new InvalidOperationException("Selected location must belong to the destination resolved from the coordinates.");

        return new ContributionLocationResolution(location.Id, latitude, longitude);
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
            IsOwnable = LegacyOwnablePlaceTypes.Contains(normalized),
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

    public async Task<ResolveContributionLocationDto> ResolveDestinationLocationsAsync(double latitude, double longitude)
    {
        if (!HasUsableCoordinates(latitude, longitude))
        {
            return new ResolveContributionLocationDto
            {
                IsResolved = false,
                Message = "Please choose valid coordinates."
            };
        }

        return await _destinationBoundaryResolver.ResolveContributionAsync(latitude, longitude);
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

    private async Task EnsureContributionLocationMatchesResolvedDestinationAsync(PlaceContribution contribution)
    {
        if (!contribution.LocationId.HasValue ||
            !contribution.Latitude.HasValue ||
            !contribution.Longitude.HasValue)
        {
            throw new InvalidOperationException("Contribution location and coordinates are required.");
        }

        var resolution = await _destinationBoundaryResolver.ResolvePublicAsync(
            contribution.Latitude.Value,
            contribution.Longitude.Value);
        if (!resolution.IsResolved || !resolution.DestinationId.HasValue)
            throw new InvalidOperationException(resolution.Message);

        var locationMatches = await _db.Locations
            .AsNoTracking()
            .AnyAsync(location =>
                location.Id == contribution.LocationId.Value &&
                location.DestinationId == resolution.DestinationId.Value &&
                location.Status == "Published");

        if (!locationMatches)
        {
            throw new InvalidOperationException(
                "The selected location no longer belongs to the destination resolved from the contribution coordinates.");
        }
    }

    private async Task<Place> PublishNewPlaceAsync(PlaceContribution contribution, DateTime now)
    {
        if (!contribution.PlaceTypeId.HasValue)
            throw new InvalidOperationException("Place type is required.");

        if (!contribution.LocationId.HasValue)
            throw new InvalidOperationException("Location is required.");

        if (!contribution.Latitude.HasValue || !contribution.Longitude.HasValue)
            throw new InvalidOperationException("Coordinates are required.");

        await EnsureContributionLocationMatchesResolvedDestinationAsync(contribution);

        var place = new Place
        {
            Name = contribution.Name,
            SearchText = contribution.Name.ToLowerInvariant(),
            Subtitle = contribution.Subtitle,
            Address = contribution.Address,
            OpeningHours = contribution.OpeningHours,
            OperatingHoursEnabled = contribution.OperatingHoursEnabled,
            OperatingHoursJson = contribution.OperatingHoursJson,
            Description = DisplayTextSafety.CleanOptionalRichMultiline(contribution.Description) ?? string.Empty,
            Latitude = contribution.Latitude.Value,
            Longitude = contribution.Longitude.Value,
            ContactEmail = contribution.ContactEmail,
            Number = contribution.PhoneNumber,
            Website = contribution.Website,
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
            .AsSplitQuery()
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
        place.SearchText = contribution.Name.ToLowerInvariant();
        place.Subtitle = contribution.Subtitle;
        place.Address = contribution.Address;
        place.OpeningHours = contribution.OpeningHours;
        place.OperatingHoursEnabled = contribution.OperatingHoursEnabled;
        place.OperatingHoursJson = contribution.OperatingHoursJson;
        place.Description = DisplayTextSafety.CleanOptionalRichMultiline(contribution.Description) ?? string.Empty;
        place.Latitude = contribution.Latitude.Value;
        place.Longitude = contribution.Longitude.Value;
        place.ContactEmail = contribution.ContactEmail;
        place.Number = contribution.PhoneNumber;
        place.Website = contribution.Website;
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
                Tag = tag
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

        var mainSeen = false;
        foreach (var image in contributionImages.OrderBy(i => i.SortOrder).ThenBy(i => i.Id))
        {
            var imageRole = EntityImageRole.Normalize(image.ImageRole);
            var isMain = !mainSeen && (image.IsMain || imageRole == EntityImageRole.Cover);
            if (isMain)
                mainSeen = true;

            _db.EntityImages.Add(new EntityImage
            {
                TargetType = EntityImageTargetType.Place,
                PlaceId = place.Id,
                Url = image.Url,
                AltText = image.AltText,
                Caption = image.Caption,
                IsMain = isMain,
                ImageRole = imageRole,
                SortOrder = image.SortOrder,
                ImagePosition = NormalizeImagePosition(image.ImagePosition),
                ImageSize = NormalizeImageSize(image.ImageSize),
                CreatedAt = now,
                UpdatedAt = now
            });
        }
    }

    private async Task<List<UpsertPlaceContributionImageDto>?> EnsurePublishedPlaceImageFallbacksAsync(
        int placeId,
        List<UpsertPlaceContributionImageDto>? requestImages,
        bool hasStoredCardImage,
        bool hasStoredCoverImage)
    {
        if ((hasStoredCardImage || HasImageRole(requestImages, EntityImageRole.Card)) &&
            (hasStoredCoverImage || HasImageRole(requestImages, EntityImageRole.Cover)))
            return requestImages;

        var publishedImages = await _db.EntityImages
            .AsNoTracking()
            .Where(image => image.PlaceId == placeId)
            .OrderBy(image => image.ImageRole == EntityImageRole.Cover ? 0 : image.ImageRole == EntityImageRole.Card ? 1 : 2)
            .ThenBy(image => image.SortOrder)
            .ThenBy(image => image.Id)
            .ToListAsync();

        if (publishedImages.Count == 0)
            return requestImages;

        var merged = requestImages?.ToList() ?? new List<UpsertPlaceContributionImageDto>();
        if (merged.All(image => string.IsNullOrWhiteSpace(image.Url)))
            merged = publishedImages.Select(ToUpsertImageDto).ToList();

        if (hasStoredCardImage)
            merged.RemoveAll(image => EntityImageRole.Normalize(image.ImageRole) == EntityImageRole.Card);

        if (hasStoredCoverImage)
            merged.RemoveAll(image => EntityImageRole.Normalize(image.ImageRole) == EntityImageRole.Cover);

        var hasCover = hasStoredCoverImage || HasImageRole(merged, EntityImageRole.Cover);
        var hasCard = hasStoredCardImage || HasImageRole(merged, EntityImageRole.Card);

        if (!hasCover)
        {
            var cover = publishedImages.FirstOrDefault(image => EntityImageRole.Normalize(image.ImageRole) == EntityImageRole.Cover)
                ?? publishedImages.FirstOrDefault();
            if (cover is not null)
                merged.Add(ToUpsertImageDto(cover, EntityImageRole.Cover, -10));
        }

        if (!hasCard)
        {
            var card = publishedImages.FirstOrDefault(image => EntityImageRole.Normalize(image.ImageRole) == EntityImageRole.Card)
                ?? publishedImages.FirstOrDefault(image => EntityImageRole.Normalize(image.ImageRole) == EntityImageRole.Cover)
                ?? publishedImages.FirstOrDefault();
            if (card is not null)
                merged.Add(ToUpsertImageDto(card, EntityImageRole.Card, -9));
        }

        return merged;
    }

    private static bool HasImageRole(IEnumerable<UpsertPlaceContributionImageDto>? images, string role)
    {
        return (images ?? Enumerable.Empty<UpsertPlaceContributionImageDto>())
            .Any(image => EntityImageRole.Normalize(image.ImageRole) == role && !string.IsNullOrWhiteSpace(image.Url));
    }

    private static UpsertPlaceContributionImageDto ToUpsertImageDto(EntityImage image)
    {
        return ToUpsertImageDto(image, EntityImageRole.Normalize(image.ImageRole), image.SortOrder);
    }

    private static UpsertPlaceContributionImageDto ToUpsertImageDto(EntityImage image, string imageRole, int sortOrder)
    {
        return new UpsertPlaceContributionImageDto
        {
            Url = image.Url,
            AltText = image.AltText,
            Caption = image.Caption,
            IsMain = image.IsMain || imageRole == EntityImageRole.Cover,
            ImageRole = imageRole,
            SortOrder = sortOrder,
            ImagePosition = NormalizeImagePosition(image.ImagePosition),
            ImageSize = NormalizeImageSize(image.ImageSize)
        };
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
            var coverMetadata = ImageMetadataForRole(requestImages, EntityImageRole.Cover);
            imageDtos.Add(new UpsertPlaceContributionImageDto
            {
                Url = storedCoverImageUrl,
                Caption = CoverImageCaption,
                IsMain = true,
                ImageRole = EntityImageRole.Cover,
                SortOrder = -10,
                ImagePosition = coverMetadata.ImagePosition,
                ImageSize = coverMetadata.ImageSize
            });
        }

        if (!string.IsNullOrWhiteSpace(storedCardImageUrl))
        {
            var cardMetadata = ImageMetadataForRole(requestImages, EntityImageRole.Card);
            imageDtos.Add(new UpsertPlaceContributionImageDto
            {
                Url = storedCardImageUrl,
                Caption = CardImageCaption,
                IsMain = false,
                ImageRole = EntityImageRole.Card,
                SortOrder = -9,
                ImagePosition = cardMetadata.ImagePosition,
                ImageSize = cardMetadata.ImageSize
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
        var mainSeen = false;
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

            var isMain = !mainSeen && (image.IsMain || imageRole == EntityImageRole.Cover);
            if (isMain)
                mainSeen = true;

            contribution.Images.Add(new PlaceContributionImage
            {
                Url = NormalizeImageUrl(image.Url),
                AltText = NormalizeOptional(image.AltText),
                Caption = NormalizeOptional(image.Caption),
                IsMain = isMain,
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

    private static (string ImagePosition, string ImageSize) ImageMetadataForRole(
        IEnumerable<UpsertPlaceContributionImageDto>? images,
        string role)
    {
        var image = (images ?? Enumerable.Empty<UpsertPlaceContributionImageDto>())
            .FirstOrDefault(item => EntityImageRole.Normalize(item.ImageRole) == role);

        return (
            NormalizeImagePosition(image?.ImagePosition),
            NormalizeImageSize(image?.ImageSize)
        );
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

        var requested = tagNames
            .Select(name => new
            {
                Name = NormalizeOptional(name, 80),
                Slug = Slugify(name)
            })
            .Where(tag => !string.IsNullOrWhiteSpace(tag.Name) && !string.IsNullOrWhiteSpace(tag.Slug))
            .DistinctBy(tag => tag.Slug, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (requested.Count == 0)
            return new List<Tag>();

        var slugs = requested.Select(tag => tag.Slug).ToList();
        var names = requested.Select(tag => tag.Name!.ToLowerInvariant()).ToList();
        var existing = await _db.Tags
            .Where(tag =>
                (slugs.Contains(tag.Slug.ToLower()) || names.Contains(tag.Name.ToLower())))
            .ToListAsync();

        var existingSlugs = existing
            .Select(tag => tag.Slug)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var now = DateTime.UtcNow;
        foreach (var tag in existing.Where(tag => !tag.ForPlace))
        {
            tag.ForPlace = true;
            tag.UpdatedAt = now;
        }

        foreach (var tag in requested.Where(tag => !existingSlugs.Contains(tag.Slug)))
        {
            var created = new Tag
            {
                Name = ToTitle(tag.Name!),
                Slug = tag.Slug,
                Description = $"{ToTitle(tag.Name!)} places",
                ForPlace = true,
                CreatedAt = now
            };

            _db.Tags.Add(created);
            existing.Add(created);
            existingSlugs.Add(created.Slug);
        }

        return existing;
    }

    private async Task<List<Amenity>> ResolveAmenitiesAsync(List<string> amenityNames)
    {
        if (amenityNames.Count == 0)
            return new List<Amenity>();

        var normalized = amenityNames.Select(NormalizeSlug).ToList();

        return await _db.Amenities
            .Where(amenity => normalized.Contains(amenity.Name.ToLower()) ||
                              normalized.Contains(amenity.Icon.ToLower()))
            .ToListAsync();
    }

    private async Task<List<StayAmenity>> ResolveStayAmenitiesAsync(List<string> stayAmenityNames)
    {
        if (stayAmenityNames.Count == 0)
            return new List<StayAmenity>();

        var normalized = stayAmenityNames.Select(NormalizeSlug).ToList();
        var knownDefinitions = new Dictionary<string, (string Name, string Icon, string Description)>(StringComparer.OrdinalIgnoreCase)
        {
            ["outdoor-seating"] = ("Outdoor Seating", "outdoor", "Outdoor seating or open-air areas"),
            ["sea-view"] = ("Sea View", "sea", "Sea view available"),
            ["family-friendly"] = ("Family Friendly", "family", "Family-friendly facilities or setting")
        };

        var existing = await _db.StayAmenities
            .Where(amenity => normalized.Contains(amenity.Slug.ToLower()) ||
                              normalized.Contains(amenity.Name.ToLower()))
            .ToListAsync();

        var existingSlugs = existing.Select(amenity => amenity.Slug).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var slug in normalized.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (existingSlugs.Contains(slug) || !knownDefinitions.TryGetValue(slug, out var definition))
                continue;

            var amenity = new StayAmenity
            {
                Name = definition.Name,
                Slug = slug,
                IconKey = definition.Icon,
                Description = definition.Description
            };
            _db.StayAmenities.Add(amenity);
            existing.Add(amenity);
        }

        if (existing.Any(amenity => amenity.Id == 0))
            await _db.SaveChangesAsync();

        return existing;
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

    private static bool HasCompletedReview(PlaceContributionStatus status) =>
        status is PlaceContributionStatus.Approved or PlaceContributionStatus.Rejected;

    private static PlaceContributionListItemDto MapListItem(PlaceContribution contribution)
    {
        var cardImageUrl = GetCardImage(contribution)?.Url;
        var coverImageUrl = GetCoverImage(contribution)?.Url;
        var galleryImageUrls = contribution.Images
            .Where(image => image.ImageRole == EntityImageRole.Gallery)
            .OrderBy(image => image.SortOrder)
            .ThenBy(image => image.Id)
            .Select(image => image.Url)
            .ToList();

        if (contribution.Status == PlaceContributionStatus.Approved &&
            contribution.Place is not null)
        {
            var publishedCardImage = GetPublishedCardImage(contribution.Place);
            var publishedCoverImage = GetPublishedCoverImage(contribution.Place);
            var publishedFirstImage = OrderPlaceImagesForSnapshot(contribution.Place.Images)
                .FirstOrDefault();

            cardImageUrl = cardImageUrl ?? publishedCardImage?.Url ?? publishedCoverImage?.Url ?? publishedFirstImage?.Url;
            coverImageUrl = coverImageUrl ?? publishedCoverImage?.Url ?? publishedFirstImage?.Url ?? publishedCardImage?.Url;

            if (galleryImageUrls.Count == 0)
            {
                galleryImageUrls = contribution.Place.Images
                    .Where(image => EntityImageRole.Normalize(image.ImageRole) == EntityImageRole.Gallery)
                    .OrderBy(image => image.SortOrder)
                    .ThenBy(image => image.Id)
                    .Select(image => image.Url)
                    .ToList();
            }
        }

        var firstContributionImage = contribution.Images
            .OrderBy(image => ImageRoleSort(image.ImageRole))
            .ThenBy(image => image.SortOrder)
            .ThenBy(image => image.Id)
            .FirstOrDefault();

        cardImageUrl ??= firstContributionImage?.Url;
        coverImageUrl ??= firstContributionImage?.Url;

        var usesPublishedPlace = contribution.Status == PlaceContributionStatus.Approved &&
            contribution.Place is not null;

        return new PlaceContributionListItemDto
        {
            Id = contribution.Id,
            Type = contribution.Type.ToString(),
            EntityType = "Place",
            Name = usesPublishedPlace ? contribution.Place!.Name : contribution.Name,
            Subtitle = usesPublishedPlace ? contribution.Place!.Subtitle : contribution.Subtitle,
            PlaceType = contribution.PlaceType?.Name,
            StayType = contribution.StayType?.Name,
            Location = usesPublishedPlace ? BuildLocationLabelFromPlace(contribution.Place!) : BuildLocationLabel(contribution),
            CardImageUrl = cardImageUrl,
            MainImageUrl = cardImageUrl,
            CoverImageUrl = coverImageUrl,
            GalleryImageUrls = galleryImageUrls,
            Status = contribution.Status.ToString(),
            SubmittedAt = contribution.SubmittedAt,
            UpdatedAt = usesPublishedPlace ? contribution.Place!.UpdatedAt : contribution.UpdatedAt,
            PlaceId = contribution.PlaceId,
            PublishedPlaceId = contribution.PlaceId,
            HasOwnershipProofDocument = !string.IsNullOrWhiteSpace(contribution.OwnershipProofDocumentUrl),
            SubmittedByDisplayName = BuildUserDisplayName(contribution.User),
            SubmittedByInitials = BuildUserInitials(contribution.User)
        };
    }

    private static EntityImage? GetPublishedCardImage(Place place)
    {
        return place.Images
            .OrderBy(image => image.SortOrder)
            .ThenBy(image => image.Id)
            .FirstOrDefault(image => EntityImageRole.Normalize(image.ImageRole) == EntityImageRole.Card);
    }

    private static EntityImage? GetPublishedCoverImage(Place place)
    {
        return place.Images
            .OrderBy(image => image.SortOrder)
            .ThenBy(image => image.Id)
            .FirstOrDefault(image => EntityImageRole.Normalize(image.ImageRole) == EntityImageRole.Cover);
    }

    private static PlaceContributionDetailDto MapDetail(
        PlaceContribution contribution,
        bool includeReviewNote,
        bool hasPendingOwnershipClaim = false)
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
            DestinationId = contribution.Location?.DestinationId,
            DestinationName = contribution.Location?.Destination?.Name,
            RestaurantTypeId = contribution.RestaurantTypeId,
            StayTypeId = contribution.StayTypeId,
            Address = contribution.Address,
            OpeningHours = contribution.OpeningHours,
            OperatingHoursEnabled = contribution.OperatingHoursEnabled,
            OperatingHours = DeserializeOperatingHours(contribution.OperatingHoursJson),
            Latitude = contribution.Latitude,
            Longitude = contribution.Longitude,
            Description = DisplayTextSafety.CleanOptionalRichMultiline(contribution.Description) ?? string.Empty,
            ContactEmail = contribution.ContactEmail,
            PhoneNumber = contribution.PhoneNumber,
            Phone = contribution.PhoneNumber,
            Website = contribution.Website,
            PricePerNight = contribution.PricePerNight,
            CheckInTime = contribution.CheckInTime,
            CheckOutTime = contribution.CheckOutTime,
            City = contribution.City ?? contribution.Location?.Name,
            Region = contribution.Region ?? contribution.Location?.Destination?.Region?.Name,
            Country = contribution.Country ?? contribution.Location?.Destination?.Region?.Territory?.Name,
            RegionDisplayName = BuildPlaceRegionDisplayName(contribution),
            ImagePosition = GetCoverImage(contribution)?.ImagePosition ?? "50% 50%",
            ImageSize = GetCoverImage(contribution)?.ImageSize ?? "cover",
            CardImagePosition = GetCardImage(contribution)?.ImagePosition,
            CardImageSize = GetCardImage(contribution)?.ImageSize,
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
            ReviewNote = includeReviewNote ? contribution.ReviewNote : null,
            ReviewedAt = contribution.ReviewedAt,
            OwnershipProofDocumentUrl = contribution.OwnershipProofDocumentUrl,
            OwnershipProofOriginalFileName = contribution.OwnershipProofOriginalFileName,
            OwnershipProofContentType = contribution.OwnershipProofContentType,
            OwnershipProofUploadedAt = contribution.OwnershipProofUploadedAt,
            HasPendingOwnershipClaim = hasPendingOwnershipClaim,
            SubmittedByDisplayName = BuildUserDisplayName(contribution.User),
            SubmittedByInitials = BuildUserInitials(contribution.User),
        };
    }

    private static string? BuildUserDisplayName(User? user)
    {
        if (user is null) return null;
        if (!string.IsNullOrWhiteSpace(user.DisplayName)) return user.DisplayName;
        var fullName = $"{user.FirstName} {user.LastName}".Trim();
        return string.IsNullOrWhiteSpace(fullName) ? user.Username : fullName;
    }

    private static string? BuildUserInitials(User? user)
    {
        if (user is null) return null;
        if (!string.IsNullOrWhiteSpace(user.FirstName) || !string.IsNullOrWhiteSpace(user.LastName))
            return $"{(user.FirstName?.Length > 0 ? user.FirstName[0] : ' ')}{(user.LastName?.Length > 0 ? user.LastName[0] : ' ')}".Trim();
        return user.Username.Length > 0 ? user.Username[0].ToString().ToUpper() : "U";
    }

    private async Task<PlaceContributionDetailDto> MapOwnershipDetailWithPlaceFallbacksAsync(
        PlaceContribution contribution,
        bool includeReviewNote,
        bool hasPendingOwnershipClaim = false)
    {
        var detail = MapDetail(contribution, includeReviewNote, hasPendingOwnershipClaim);
        if (!contribution.PlaceId.HasValue)
            return detail;

        if (!NeedsOwnershipPlaceFallback(detail))
            return detail;

        var place = await LoadOwnershipTargetPlaceAsync(contribution.PlaceId.Value);
        if (place is null)
            return detail;

        ApplyOwnershipPlaceFallbacks(detail, place);
        return detail;
    }

    private static bool NeedsOwnershipPlaceFallback(PlaceContributionDetailDto detail)
    {
        return string.IsNullOrWhiteSpace(detail.CardImageUrl) ||
               string.IsNullOrWhiteSpace(detail.CoverImageUrl) ||
               detail.Images.Count == 0 ||
               detail.Tags.Count == 0 ||
               (detail.Amenities.Count == 0 && detail.StayAmenities.Count == 0) ||
               string.IsNullOrWhiteSpace(detail.Website);
    }

    private static void ApplyOwnershipPlaceFallbacks(PlaceContributionDetailDto detail, Place place)
    {
        var orderedImages = OrderPlaceImagesForSnapshot(place.Images).ToList();
        var cardImage = orderedImages.FirstOrDefault(image => EntityImageRole.Normalize(image.ImageRole) == EntityImageRole.Card);
        var coverImage = orderedImages.FirstOrDefault(image => EntityImageRole.Normalize(image.ImageRole) == EntityImageRole.Cover);
        var firstImage = orderedImages.FirstOrDefault();
        var cardImageUrl = cardImage?.Url ?? coverImage?.Url ?? firstImage?.Url;
        var coverImageUrl = coverImage?.Url ?? firstImage?.Url ?? cardImage?.Url;

        if (string.IsNullOrWhiteSpace(detail.CardImageUrl))
            detail.CardImageUrl = cardImageUrl;
        if (string.IsNullOrWhiteSpace(detail.MainImageUrl))
            detail.MainImageUrl = detail.CardImageUrl;
        if (string.IsNullOrWhiteSpace(detail.CoverImageUrl))
            detail.CoverImageUrl = coverImageUrl;
        if (detail.GalleryImageUrls.Count == 0)
        {
            detail.GalleryImageUrls = orderedImages
                .Where(image => EntityImageRole.Normalize(image.ImageRole) == EntityImageRole.Gallery)
                .Select(image => image.Url)
                .ToList();
        }
        if (detail.Images.Count == 0)
        {
            detail.Images = orderedImages
                .Select(image => new PlaceContributionImageDto
                {
                    Id = image.Id,
                    Url = image.Url,
                    AltText = image.AltText,
                    Caption = image.Caption,
                    IsMain = image.IsMain,
                    ImageRole = EntityImageRole.Normalize(image.ImageRole),
                    SortOrder = image.SortOrder,
                    ImagePosition = string.IsNullOrWhiteSpace(image.ImagePosition) ? "50% 50%" : image.ImagePosition,
                    ImageSize = string.IsNullOrWhiteSpace(image.ImageSize) ? "cover" : image.ImageSize
                })
                .ToList();
        }

        if (string.IsNullOrWhiteSpace(detail.Subtitle))
            detail.Subtitle = place.Subtitle;
        if (string.IsNullOrWhiteSpace(detail.Address))
            detail.Address = place.Address;
        if (string.IsNullOrWhiteSpace(detail.OpeningHours))
            detail.OpeningHours = place.OpeningHours;
        if (!detail.OperatingHoursEnabled && place.OperatingHoursEnabled)
        {
            detail.OperatingHoursEnabled = true;
            detail.OperatingHours = DeserializeOperatingHours(place.OperatingHoursJson);
            detail.OpeningHours = place.OpeningHours;
        }
        if (string.IsNullOrWhiteSpace(detail.Description))
            detail.Description = DisplayTextSafety.CleanOptionalRichMultiline(place.Description) ?? string.Empty;
        if (!detail.Latitude.HasValue)
            detail.Latitude = place.Latitude;
        if (!detail.Longitude.HasValue)
            detail.Longitude = place.Longitude;
        if (string.IsNullOrWhiteSpace(detail.ContactEmail))
            detail.ContactEmail = place.ContactEmail;
        if (string.IsNullOrWhiteSpace(detail.PhoneNumber))
            detail.PhoneNumber = place.Number;
        if (string.IsNullOrWhiteSpace(detail.Phone))
            detail.Phone = place.Number;
        if (string.IsNullOrWhiteSpace(detail.Website))
            detail.Website = place.Website;
        if (!detail.PricePerNight.HasValue)
            detail.PricePerNight = place.Stay?.PricePerNight;
        if (string.IsNullOrWhiteSpace(detail.CheckInTime))
            detail.CheckInTime = place.Stay?.CheckInTime;
        if (string.IsNullOrWhiteSpace(detail.CheckOutTime))
            detail.CheckOutTime = place.Stay?.CheckOutTime;
        if (detail.Tags.Count == 0)
            detail.Tags = BuildOwnershipTags(place);
        if (detail.Amenities.Count == 0)
            detail.Amenities = BuildOwnershipPlaceAmenities(place);
        if (detail.StayAmenities.Count == 0)
            detail.StayAmenities = BuildOwnershipStayAmenities(place);
        if (detail.BookingPartners.Count == 0)
            detail.BookingPartners = BuildOwnershipBookingPartners(place);
        if (string.IsNullOrWhiteSpace(detail.ImagePosition))
            detail.ImagePosition = coverImage?.ImagePosition ?? "50% 50%";
        if (string.IsNullOrWhiteSpace(detail.ImageSize))
            detail.ImageSize = coverImage?.ImageSize ?? "cover";
    }

    private async Task<PlaceContributionDetailDto> MapApprovedPublishedPlaceDetailAsync(
        PlaceContribution contribution,
        bool includeReviewNote)
    {
        var detail = MapDetail(contribution, includeReviewNote);
        if (!contribution.PlaceId.HasValue)
            return detail;

        var place = await _db.Places
            .AsNoTracking()
            .Include(p => p.PlaceType)
            .Include(p => p.Location)
                .ThenInclude(location => location.Destination)
                    .ThenInclude(destination => destination.Region)
            .Include(p => p.Images)
            .Include(p => p.PlaceTags)
                .ThenInclude(placeTag => placeTag.Tag)
            .Include(p => p.PlaceAmenities)
                .ThenInclude(placeAmenity => placeAmenity.Amenity)
            .Include(p => p.PlaceBookingPartners)
                .ThenInclude(placeBookingPartner => placeBookingPartner.BookingPartner)
            .Include(p => p.Stay)
                .ThenInclude(stay => stay!.StayType)
            .Include(p => p.Stay)
                .ThenInclude(stay => stay!.StayAmenities)
                    .ThenInclude(stayStayAmenity => stayStayAmenity.StayAmenity)
            .AsSplitQuery()
            .SingleOrDefaultAsync(p => p.Id == contribution.PlaceId.Value);

        if (place is null)
            return detail;

        var cardImage = place.Images
            .OrderBy(image => image.SortOrder)
            .ThenBy(image => image.Id)
            .FirstOrDefault(image => EntityImageRole.Normalize(image.ImageRole) == EntityImageRole.Card);
        var coverImage = place.Images
            .OrderBy(image => image.SortOrder)
            .ThenBy(image => image.Id)
            .FirstOrDefault(image => EntityImageRole.Normalize(image.ImageRole) == EntityImageRole.Cover);
        var firstImage = place.Images
            .OrderBy(image => image.SortOrder)
            .ThenBy(image => image.Id)
            .FirstOrDefault();
        var cardImageUrl = cardImage?.Url ?? coverImage?.Url ?? firstImage?.Url;
        var coverImageUrl = coverImage?.Url ?? firstImage?.Url ?? cardImage?.Url;

        detail.Name = place.Name;
        detail.PlaceType = place.PlaceType?.Name;
        detail.StayType = place.Stay?.StayType?.Name;
        detail.Location = BuildLocationLabelFromPlace(place);
        detail.CardImageUrl = cardImageUrl;
        detail.MainImageUrl = cardImageUrl;
        detail.CoverImageUrl = coverImageUrl;
        detail.GalleryImageUrls = place.Images
            .Where(image => EntityImageRole.Normalize(image.ImageRole) == EntityImageRole.Gallery)
            .OrderBy(image => image.SortOrder)
            .ThenBy(image => image.Id)
            .Select(image => image.Url)
            .ToList();
        detail.PlaceTypeId = place.PlaceTypeId;
        detail.LocationId = place.LocationId;
        detail.StayTypeId = place.Stay?.StayTypeId;
        detail.Address = place.Address;
        detail.OpeningHours = place.OpeningHours;
        detail.OperatingHoursEnabled = place.OperatingHoursEnabled;
        detail.OperatingHours = DeserializeOperatingHours(place.OperatingHoursJson);
        detail.Latitude = place.Latitude;
        detail.Longitude = place.Longitude;
        detail.Description = DisplayTextSafety.CleanOptionalRichMultiline(place.Description) ?? string.Empty;
        detail.ContactEmail = place.ContactEmail;
        detail.PhoneNumber = place.Number;
        detail.Phone = place.Number;
        detail.Website = place.Website;
        detail.PricePerNight = place.Stay?.PricePerNight;
        detail.CheckInTime = place.Stay?.CheckInTime;
        detail.CheckOutTime = place.Stay?.CheckOutTime;
        detail.City = place.Location?.Name ?? detail.City;
        detail.Region = place.Location?.Destination?.Region?.Name ?? detail.Region;
        detail.Country = place.Location?.Destination?.Region?.Territory?.Name ?? detail.Country;
        detail.ImagePosition = coverImage?.ImagePosition ?? "50% 50%";
        detail.ImageSize = coverImage?.ImageSize ?? "cover";
        detail.CardImagePosition = cardImage?.ImagePosition;
        detail.CardImageSize = cardImage?.ImageSize;
        detail.Tags = place.PlaceTags
            .Select(placeTag => placeTag.Tag.Name)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        detail.Amenities = place.PlaceAmenities
            .Select(placeAmenity => placeAmenity.Amenity.Name)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        detail.StayAmenities = place.Stay?.StayAmenities
            .Select(stayStayAmenity => stayStayAmenity.StayAmenity.Slug)
            .Where(slug => !string.IsNullOrWhiteSpace(slug))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList() ?? new List<string>();
        detail.BookingPartners = place.PlaceBookingPartners
            .Select(placeBookingPartner => new ContributionBookingPartnerDto
            {
                Id = BookingPartnerContributionId(placeBookingPartner.BookingPartner.Name),
                Url = placeBookingPartner.Url ?? string.Empty
            })
            .ToList();
        detail.Images = place.Images
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
                ImagePosition = string.IsNullOrWhiteSpace(image.ImagePosition) ? "50% 50%" : image.ImagePosition,
                ImageSize = string.IsNullOrWhiteSpace(image.ImageSize) ? "cover" : image.ImageSize
            })
            .ToList();

        return detail;
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

    private static string? BuildLocationLabelFromPlace(Place place)
    {
        if (place.Location is null)
            return null;

        var parts = new[]
        {
            place.Location.Name,
            place.Location.Destination?.Name,
            place.Location.Destination?.Region?.Name
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

    private static List<ContributionFeatureOptionDto> DefaultContributionCharacteristics() => new()
    {
        FeatureOption("luxury", "Luxury"),
        FeatureOption("budget-friendly", "Budget-Friendly"),
        FeatureOption("family-friendly", "Family Friendly"),
        FeatureOption("adults-only", "Adults Only"),
        FeatureOption("pet-friendly", "Pet Friendly"),
        FeatureOption("beachfront", "Beachfront"),
        FeatureOption("romantic", "Romantic"),
        FeatureOption("mountain-view", "Mountain View"),
        FeatureOption("city-center", "City Center"),
        FeatureOption("countryside", "Countryside"),
        FeatureOption("eco-friendly", "Eco-Friendly"),
        FeatureOption("business", "Business"),
        FeatureOption("lakefront", "Lakefront"),
        FeatureOption("historic", "Historic"),
        FeatureOption("quiet-area", "Quiet Area"),
        FeatureOption("wellness", "Wellness"),
        FeatureOption("spa", "Spa"),
        FeatureOption("viewpoint", "Viewpoint"),
        FeatureOption("local-cuisine", "Local Cuisine"),
        FeatureOption("fine-dining", "Fine Dining"),
        FeatureOption("outdoor-dining", "Outdoor Dining"),
        FeatureOption("traditional", "Traditional"),
        FeatureOption("breakfast", "Breakfast"),
        FeatureOption("dessert", "Dessert"),
        FeatureOption("workspace", "Workspace"),
        FeatureOption("live-music", "Live Music"),
        FeatureOption("nightlife", "Nightlife"),
        FeatureOption("cocktails", "Cocktails"),
        FeatureOption("rooftop", "Rooftop"),
        FeatureOption("walking-route", "Walking Route"),
        FeatureOption("architecture", "Architecture"),
        FeatureOption("unesco", "UNESCO"),
        FeatureOption("art", "Art"),
        FeatureOption("interactive", "Interactive"),
        FeatureOption("educational", "Educational"),
        FeatureOption("modern-art", "Modern Art"),
        FeatureOption("local-art", "Local Art"),
        FeatureOption("garden", "Garden"),
        FeatureOption("botanical-garden", "Botanical Garden")
    };

    private static List<ContributionFeatureOptionDto> DefaultContributionAmenities() => new()
    {
        FeatureOption("outdoor", "OUTDOOR SEATING"),
        FeatureOption("wifi", "WIFI"),
        FeatureOption("parking", "PARKING"),
        FeatureOption("ac", "A/C"),
        FeatureOption("breakfast", "BREAKFAST"),
        FeatureOption("sea", "SEA VIEW"),
        FeatureOption("pool", "POOL"),
        FeatureOption("spa", "SPA"),
        FeatureOption("gym", "GYM"),
        FeatureOption("dining", "DINING")
    };

    private static List<ContributionFeatureOptionDto> DefaultContributionStayAmenities() => new()
    {
        FeatureOption("free-wifi", "Free WiFi"),
        FeatureOption("swimming-pool", "Swimming Pool"),
        FeatureOption("breakfast-included", "Breakfast Included"),
        FeatureOption("parking", "Parking"),
        FeatureOption("spa", "Spa"),
        FeatureOption("fitness-center", "Fitness Center"),
        FeatureOption("restaurant", "Restaurant"),
        FeatureOption("air-conditioning", "Air Conditioning"),
        FeatureOption("outdoor-seating", "Outdoor Seating"),
        FeatureOption("sea-view", "Sea View")
    };

    private async Task<Dictionary<string, List<string>>> BuildContributionCharacteristicIdsByPlaceTypeAsync(
        IReadOnlyCollection<ContributionFeatureOptionDto> options)
    {
        var placeTypes = await _db.PlaceTypes
            .AsNoTracking()
            .Select(placeType => new { placeType.Id, placeType.Name })
            .ToListAsync();
        var restrictions = await _db.TagTypePlaceTypes
            .AsNoTracking()
            .Select(link => new { link.PlaceTypeId, link.TagTypeId })
            .ToListAsync();

        if (placeTypes.Count == 0 || restrictions.Count == 0)
        {
            return BuildContributionCharacteristicIdsByPlaceType(
                options.Select(option => option.Id).ToList());
        }

        var globallyRestrictedTagTypeIds = restrictions
            .Select(link => link.TagTypeId)
            .ToHashSet();
        var result = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var placeType in placeTypes)
        {
            var allowedTagTypeIds = restrictions
                .Where(link => link.PlaceTypeId == placeType.Id)
                .Select(link => link.TagTypeId)
                .ToHashSet();

            result[Slugify(placeType.Name)] = options
                .Where(option =>
                    option.TagTypeId is null ||
                    !globallyRestrictedTagTypeIds.Contains(option.TagTypeId.Value) ||
                    allowedTagTypeIds.Contains(option.TagTypeId.Value))
                .Select(option => option.Id)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        return result;
    }

    private static Dictionary<string, List<string>> BuildContributionCharacteristicIdsByPlaceType(IReadOnlyCollection<string> availableIds)
    {
        string[] Pick(params string[] ids) =>
            ids.Where(id => availableIds.Contains(id, StringComparer.OrdinalIgnoreCase)).ToArray();

        return new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["stay"] = Pick("luxury", "budget-friendly", "family-friendly", "adults-only", "pet-friendly", "romantic", "mountain-view", "city-center", "countryside", "eco-friendly", "business", "lakefront", "historic", "quiet-area", "wellness", "spa", "viewpoint").ToList(),
            ["restaurant"] = Pick("luxury", "budget-friendly", "family-friendly", "pet-friendly", "romantic", "city-center", "countryside", "eco-friendly", "lakefront", "historic", "local-cuisine", "fine-dining", "outdoor-dining", "traditional").ToList(),
            ["cafe"] = Pick("budget-friendly", "family-friendly", "pet-friendly", "romantic", "city-center", "countryside", "eco-friendly", "lakefront", "historic", "quiet-area", "breakfast", "dessert", "workspace").ToList(),
            ["bar"] = Pick("luxury", "budget-friendly", "adults-only", "romantic", "beachfront", "mountain-view", "city-center", "countryside", "lakefront", "historic", "live-music", "nightlife", "cocktails", "rooftop").ToList(),
            ["landmark"] = Pick("family-friendly", "mountain-view", "city-center", "countryside", "lakefront", "historic", "viewpoint", "architecture", "walking-route", "unesco").ToList(),
            ["museum"] = Pick("family-friendly", "city-center", "historic", "art", "interactive", "educational", "architecture").ToList(),
            ["gallery"] = Pick("romantic", "city-center", "historic", "art", "modern-art", "local-art", "quiet-area").ToList(),
            ["park"] = Pick("family-friendly", "pet-friendly", "eco-friendly", "walking-route", "quiet-area", "garden", "botanical-garden", "lakefront", "historic").ToList(),
            ["beach"] = Pick("family-friendly", "beachfront", "romantic", "eco-friendly").ToList(),
            ["nature"] = Pick("family-friendly", "pet-friendly", "mountain-view", "countryside", "eco-friendly", "lakefront").ToList()
        };
    }

    private static Dictionary<string, List<string>> BuildContributionAmenityIdsByPlaceType(IReadOnlyCollection<string> availableIds)
    {
        string[] Pick(params string[] ids) =>
            ids.Where(id => availableIds.Contains(id, StringComparer.OrdinalIgnoreCase)).ToArray();

        return new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["stay"] = Pick("free-wifi", "swimming-pool", "breakfast-included", "parking", "spa", "fitness-center", "restaurant", "air-conditioning", "outdoor-seating", "sea-view").ToList(),
            ["restaurant"] = Pick("outdoor", "wifi", "parking", "ac", "sea").ToList(),
            ["cafe"] = Pick("outdoor", "wifi", "breakfast", "parking", "ac", "sea").ToList(),
            ["bar"] = Pick("outdoor", "wifi", "parking", "ac", "sea").ToList(),
            ["beach"] = Pick("parking", "outdoor", "wifi", "sea").ToList(),
            ["landmark"] = Pick("parking", "wifi", "ac").ToList(),
            ["nature"] = Pick("parking", "outdoor").ToList(),
            ["museum"] = Pick("parking", "wifi", "ac").ToList(),
            ["gallery"] = Pick("parking", "wifi", "ac").ToList(),
            ["park"] = Pick("parking", "outdoor").ToList()
        };
    }

    private static ContributionFeatureOptionDto FeatureOption(string id, string label) => new()
    {
        Id = id,
        Label = label
    };

    private static List<ContributionFeatureOptionDto> MergeFeatureOptions(
        IEnumerable<ContributionFeatureOptionDto> primary,
        IEnumerable<ContributionFeatureOptionDto> fallback)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<ContributionFeatureOptionDto>();

        foreach (var option in primary.Concat(fallback))
        {
            var id = option.Id?.Trim();
            var label = option.Label?.Trim();
            var key = Slugify(label ?? id ?? string.Empty);
            if (string.IsNullOrWhiteSpace(id) ||
                string.IsNullOrWhiteSpace(label) ||
                string.IsNullOrWhiteSpace(key) ||
                !seen.Add(key))
            {
                continue;
            }

            result.Add(new ContributionFeatureOptionDto
            {
                Id = id,
                Label = label,
                TagTypeId = option.TagTypeId,
                TagTypeName = option.TagTypeName,
                TagTypeSlug = option.TagTypeSlug,
                TagTypeLabel = option.TagTypeLabel,
                TagTypeOrder = option.TagTypeOrder
            });
        }

        return result;
    }

    private static List<ContributionFeatureOptionDto> ExcludeAmenityFeatureOptions(
        IEnumerable<ContributionFeatureOptionDto> options,
        params string[] excludedIds)
    {
        var excluded = excludedIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return options
            .Where(option => !excluded.Contains(option.Id) &&
                             !string.Equals(option.Label, "Family Friendly", StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    private static string? BuildPlaceRegionDisplayName(PlaceContribution contribution)
    {
        var region = contribution.Place?.Location?.Destination?.Region;
        if (region != null)
            return BuildContributionRegionDisplayName(region.Name, region.Territory?.Name);

        var locationRegion = contribution.Location?.Destination?.Region;
        if (locationRegion != null)
            return BuildContributionRegionDisplayName(locationRegion.Name, locationRegion.Territory?.Name);

        return null;
    }

    private static string BuildContributionRegionDisplayName(string regionName, string? territoryName)
    {
        return string.IsNullOrWhiteSpace(territoryName)
            ? regionName
            : regionName + ", " + territoryName;
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

    private void Validate(UpsertPlaceContributionDto request)
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


        _ = NormalizeContactEmail(request.ContactEmail);
        _ = NormalizeWebsite(request.Website);
        _ = NormalizePricePerNight(request.PricePerNight);
        _ = NormalizeBookingPartners(request.BookingPartners);
        _ = NormalizeImages(request.Images).ToList();
        _ = NormalizeOperatingHours(request.OperatingHoursEnabled, request.OperatingHours);

        if (ParseType(request.Type) == PlaceContributionType.UpdatePlace && !request.PlaceId.HasValue)
            throw new InvalidOperationException("PlaceId is required for update contributions.");
    }

    private static OperatingHoursResult NormalizeOperatingHours(
        bool enabled,
        IReadOnlyCollection<ContributionOperatingHourDto>? hours)
    {
        if (!enabled)
            return new OperatingHoursResult(false, null, []);

        var normalizedHours = new List<ContributionOperatingHourDto>();
        var seenDays = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var input in hours ?? Array.Empty<ContributionOperatingHourDto>())
        {
            var day = NormalizeDayOfWeek(input.DayOfWeek);
            if (!seenDays.Add(day))
                throw new InvalidOperationException("Operating hours contain duplicate days.");

            if (!input.IsOpen)
            {
                normalizedHours.Add(new ContributionOperatingHourDto
                {
                    DayOfWeek = day,
                    IsOpen = false
                });
                continue;
            }

            var openTime = NormalizeClockTime(input.OpenTime, "Opening time is invalid.");
            var closeTime = NormalizeClockTime(input.CloseTime, "Closing time is invalid.");
            // 00:00 - 00:00 is the "open 24 hours" sentinel; any other equal pair is ambiguous.
            if (openTime == closeTime && openTime != "00:00")
                throw new InvalidOperationException("Opening time and closing time cannot be the same.");

            normalizedHours.Add(new ContributionOperatingHourDto
            {
                DayOfWeek = day,
                IsOpen = true,
                OpenTime = openTime,
                CloseTime = closeTime
            });
        }

        if (normalizedHours.Count == 0)
            throw new InvalidOperationException("Operating hours are required when enabled.");

        return new OperatingHoursResult(
            true,
            JsonSerializer.Serialize(normalizedHours, JsonOptions),
            normalizedHours);
    }

    private static OperatingHoursResult BuildOperatingHoursFromPlace(Place place)
    {
        if (!place.OperatingHoursEnabled)
            return new OperatingHoursResult(false, null, []);

        var hours = DeserializeOperatingHours(place.OperatingHoursJson);
        return new OperatingHoursResult(
            hours.Count > 0,
            hours.Count > 0 ? JsonSerializer.Serialize(hours, JsonOptions) : null,
            hours);
    }

    private static string NormalizeDayOfWeek(string? dayOfWeek)
    {
        var normalized = (dayOfWeek ?? string.Empty).Trim().ToLowerInvariant();
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
        if (!System.Text.RegularExpressions.Regex.IsMatch(normalized, @"^([01]\d|2[0-3]):[0-5]\d$"))
            throw new InvalidOperationException(errorMessage);

        return normalized;
    }

    private static string? BuildOpeningHoursSummary(IReadOnlyCollection<ContributionOperatingHourDto> hours)
    {
        if (hours.Count == 7 && hours.All(h =>
                h.IsOpen && h.OpenTime == "00:00" && h.CloseTime == "00:00"))
            return "Open 24 hours";

        if (!hours.Any(h => h.IsOpen))
            return null;

        return string.Join(", ", hours.Select(h =>
            h.IsOpen ? $"{h.DayOfWeek}: {h.OpenTime}–{h.CloseTime}" : $"{h.DayOfWeek}: Closed"));
    }

    private static List<ContributionOperatingHourDto> DeserializeOperatingHours(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return [];

        try
        {
            return JsonSerializer.Deserialize<List<ContributionOperatingHourDto>>(json, JsonOptions) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private string NormalizeDescription(string? description) =>
        _htmlSanitizer.SanitizeRichText(description);

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

        if (IsManagedContributionMediaPath(trimmed) || IsSafeLocalImagePath(trimmed))
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

    private static bool IsSafeLocalImagePath(string value)
    {
        if (!value.StartsWith("/media/", StringComparison.Ordinal) &&
            !value.StartsWith("/images/", StringComparison.Ordinal))
        {
            return false;
        }

        if (value.Contains('\\') ||
            value.Contains("//", StringComparison.Ordinal) ||
            value.Contains('%') ||
            value.Contains('?') ||
            value.Contains('#') ||
            value.Contains("..", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Image URL must be a valid https URL or local image path.");
        }

        if (!LocalImagePathRegex.IsMatch(value))
            throw new InvalidOperationException("Image URL must be a valid https URL or local image path.");

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
            .GroupBy(image => $"{image.ImageRole}\n{image.Url}", StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
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

    private static string? SuggestedLocationName(PlaceContribution contribution)
    {
        var address = NormalizeOptional(contribution.Address);
        var city = NormalizeOptional(contribution.City);

        if (!string.IsNullOrWhiteSpace(address) &&
            !string.Equals(address, city, StringComparison.OrdinalIgnoreCase) &&
            !Regex.IsMatch(address, @"\d", RegexOptions.CultureInvariant))
        {
            return address;
        }

        return NormalizeOptional(contribution.Location?.Name);
    }

    private static ContributionRegionOptionDto? FindBestRegionMatch(
        IReadOnlyCollection<ContributionRegionOptionDto> regions,
        string contributedRegionName)
    {
        var target = NormalizeGeoName(contributedRegionName);
        if (target.Length == 0)
            return null;

        return regions
            .Select(region => new
            {
                Region = region,
                Score = RegionMatchScore(target, NormalizeGeoName(region.Name))
            })
            .Where(candidate => candidate.Score > 0)
            .OrderByDescending(candidate => candidate.Score)
            .ThenBy(candidate => candidate.Region.Name)
            .Select(candidate => candidate.Region)
            .FirstOrDefault();
    }

    private static int RegionMatchScore(string target, string region)
    {
        if (target == region)
            return 100;

        if (target.Contains(region, StringComparison.Ordinal) ||
            region.Contains(target, StringComparison.Ordinal))
        {
            return 80;
        }

        var targetTokens = target.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var regionTokens = region.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var score = 0;

        foreach (var targetToken in targetTokens)
        {
            foreach (var regionToken in regionTokens)
            {
                if (targetToken == regionToken)
                {
                    score = Math.Max(score, 70);
                    continue;
                }

                if (StemRegionToken(targetToken) == StemRegionToken(regionToken))
                {
                    score = Math.Max(score, 75);
                    continue;
                }

                var commonPrefix = CommonPrefixLength(targetToken, regionToken);
                if (commonPrefix >= Math.Min(6, Math.Min(targetToken.Length, regionToken.Length)))
                    score = Math.Max(score, 60 + commonPrefix);
            }
        }

        return score;
    }

    private static int CommonPrefixLength(string left, string right)
    {
        var max = Math.Min(left.Length, right.Length);
        var index = 0;
        while (index < max && left[index] == right[index])
            index++;

        return index;
    }

    private static string NormalizeGeoName(string value)
    {
        var decomposed = value.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var chars = decomposed
            .Where(ch => CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
            .Select(ch => char.IsLetterOrDigit(ch) ? ch : ' ')
            .ToArray();

        var normalized = new string(chars).Normalize(NormalizationForm.FormC);
        var tokensToIgnore = new HashSet<string>(StringComparer.Ordinal)
        {
            "administrative",
            "area",
            "county",
            "district",
            "grad",
            "okrug",
            "oblast",
            "region",
            "regija"
        };

        return string.Join(' ', normalized
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(token => !tokensToIgnore.Contains(token)));
    }

    private static string StemRegionToken(string token)
    {
        foreach (var suffix in new[] { "ski", "ska", "sko", "cki", "cka", "cko", " district", " region" })
        {
            if (token.EndsWith(suffix, StringComparison.Ordinal) &&
                token.Length > suffix.Length + 3)
            {
                return token[..^suffix.Length];
            }
        }

        return token.EndsWith('a') && token.Length > 5
            ? token[..^1]
            : token;
    }

    private static string PartnerName(string id) => id.ToLowerInvariant() switch
    {
        "booking" => "Booking.com",
        "airbnb" => "Airbnb",
        "hotel" => "Official Website",
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

    private static string BookingPartnerContributionId(string name)
    {
        var slug = Slugify(name);
        return slug switch
        {
            "booking-com" => "booking",
            "hotel-website" or "official-website" => "hotel",
            _ => slug
        };
    }

    private static string Slugify(string value)
    {
        var slug = Regex.Replace(value.Trim().ToLowerInvariant(), @"[^a-z0-9]+", "-").Trim('-');
        return string.IsNullOrWhiteSpace(slug) ? $"tag-{Guid.NewGuid():N}" : slug;
    }

    private static string ToTitle(string value) =>
        string.Join(" ", value.Split('-', '_', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => char.ToUpperInvariant(part[0]) + part[1..]));

    private sealed record ResolvedBookingPartner(int BookingPartnerId, string? Url);

    private sealed record ContributionReferenceResolution(
        int? PlaceTypeId,
        int? LocationId,
        int? StayTypeId,
        List<string> PlaceAmenities,
        List<string> StayAmenities,
        double Latitude,
        double Longitude);

    private sealed record ContributionLocationResolution(
        int? LocationId,
        double Latitude,
        double Longitude);

    private sealed record OperatingHoursResult(
        bool Enabled,
        string? Json,
        IReadOnlyList<ContributionOperatingHourDto> Hours);
}
