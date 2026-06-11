using Geotrack.Application.DTOs.Destinations;
using Geotrack.Application.DTOs.Images;
using Geotrack.Application.DTOs.Places;
using Geotrack.Application.DTOs.Reviews;
using Geotrack.Application.Interfaces.Service;
using Geotrack.Application.Services;
using Geotrack.Domain.Entities.Core;
using Geotrack.Domain.Entities.Reviews;
using Geotrack.Domain.Entities.Tags;
using Geotrack.Domain.Entities.Users;
using Geotrack.Infrastructure.Spatial;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Geotrack.Infrastructure.Services;

public class PlaceService : IPlaceService
{
    private const int ListingMaxTake = 50;
    private const string PublishedStatus = "Published";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly AppDbContext _db;
    private readonly IFavoriteService _favoriteService;
    private readonly IReviewContentModerationService _contentModeration;
    private readonly IContentHtmlSanitizer _htmlSanitizer;
    private readonly IModerationAuditService _auditService;

    public PlaceService(
        AppDbContext db,
        IFavoriteService favoriteService,
        IReviewContentModerationService contentModeration,
        IContentHtmlSanitizer htmlSanitizer,
        IModerationAuditService auditService)
    {
        _db = db;
        _favoriteService = favoriteService;
        _contentModeration = contentModeration;
        _htmlSanitizer = htmlSanitizer;
        _auditService = auditService;
    }

    public async Task<DestinationScopedResult<PlaceListingResponseDto>> GetPlacesAsync(
        int? destinationId,
        string? type = null,
        string? tags = null,
        string? category = null,
        int? userId = null,
        double? latitude = null,
        double? longitude = null,
        int skip = 0,
        int? take = null)
    {
        if (!destinationId.HasValue || destinationId.Value <= 0)
        {
            return new DestinationScopedResult<PlaceListingResponseDto>
            {
                Status = DestinationScopedStatus.InvalidDestination
            };
        }

        var destination = await _db.Destinations
            .AsNoTracking()
            .Where(d => d.Id == destinationId.Value && d.Status == PublishedStatus)
            .Select(d => new { d.Id, d.Latitude, d.Longitude })
            .SingleOrDefaultAsync();

        if (destination is null)
        {
            return new DestinationScopedResult<PlaceListingResponseDto>
            {
                Status = DestinationScopedStatus.DestinationNotFound
            };
        }

        var userPoint = BuildUserPoint(latitude, longitude)
            ?? BuildUserPoint(destination.Latitude, destination.Longitude);

        var query = _db.Places
            .AsNoTracking()
            .Where(p => p.Status == PublishedStatus)
            .Where(p => p.Location.Status == PublishedStatus && p.Location.Destination.Status == PublishedStatus)
            .Where(p => p.Location.DestinationId == destination.Id);

        var hasTypeFilter = !string.IsNullOrWhiteSpace(type) && !IsAllFilter(type);
        if (hasTypeFilter)
        {
            var typeTerms = BuildFilterTerms(type);

            if (IsStayFilter(typeTerms))
            {
                query = query.Where(p => p.Stay != null);
            }
            else
            {
                query = query.Where(p => typeTerms.Contains(p.PlaceType.Name.ToLower()));
            }
        }
        else
        {
            query = query.Where(p => p.Stay == null);
        }

        if (!string.IsNullOrWhiteSpace(tags) && !IsAllFilter(tags))
        {
            var tagTerms = BuildFilterTerms(tags);
            query = query.Where(p =>
                p.PlaceTags.Any(pt =>
                    tagTerms.Contains(pt.Tag.Slug.ToLower()) ||
                    tagTerms.Contains(pt.Tag.Name.ToLower())) ||
                tagTerms.Contains(p.PlaceType.Name.ToLower()));
        }

        if (!string.IsNullOrWhiteSpace(category) && !IsAllFilter(category))
        {
            var categoryTerms = BuildFilterTerms(category);
            query = query.Where(p =>
                p.PlaceTags.Any(pt =>
                    categoryTerms.Contains(pt.Tag.Slug.ToLower()) ||
                    categoryTerms.Contains(pt.Tag.Name.ToLower())) ||
                categoryTerms.Contains(p.PlaceType.Name.ToLower()));
        }

        var items = await BuildPlaceListItemsAsync(
            query,
            destination.Id,
            userId,
            userPoint,
            skip,
            take);

        return new DestinationScopedResult<PlaceListingResponseDto>
        {
            Status = DestinationScopedStatus.Success,
            Data = items
        };
    }

    public async Task<PlaceDetailDto?> GetPlaceDetailAsync(
        int id,
        int? userId = null,
        double? latitude = null,
        double? longitude = null,
        bool useCurrentLocationDistance = false)
    {
        var place = await _db.Places
            .AsNoTracking()
            .Include(p => p.PlaceType)
            .Include(p => p.Location)
                .ThenInclude(l => l.Destination)
            .Include(p => p.Images)
            .Include(p => p.PlaceAmenities)
                .ThenInclude(pa => pa.Amenity)
            .Include(p => p.PlaceTags)
                .ThenInclude(pt => pt.Tag)
                    .ThenInclude(tag => tag.TagType)
                        .ThenInclude(tagType => tagType!.PlaceTypes)
            .Include(p => p.PlaceBookingPartners)
                .ThenInclude(placeBookingPartner => placeBookingPartner.BookingPartner)
            .AsSplitQuery()
            .WherePublicPlaces()
            .FirstOrDefaultAsync(p => p.Id == id);

        if (place is null)
        {
            return null;
        }

        var isSaved = userId.HasValue &&
            await _favoriteService.IsFavoriteAsync(userId.Value, "place", id);
        var isOwnedByCurrentUser = userId.HasValue &&
            await _db.UserPlaces.AsNoTracking().AnyAsync(item => item.PlaceId == id && item.UserId == userId.Value);
        var approvedContributionId = userId.HasValue
            ? await GetApprovedEditableContributionIdAsync(id, userId.Value)
            : null;
        var hasPendingOwnershipClaim = userId.HasValue &&
            await _db.PlaceContributions.AsNoTracking().AnyAsync(item =>
                item.PlaceId == id &&
                item.UserId == userId.Value &&
                item.Type == PlaceContributionType.ClaimOwnership &&
                item.Status == PlaceContributionStatus.PendingReview);
        var similar = await _db.Places
            .AsNoTracking()
            .Include(p => p.PlaceReviews)
            .Include(p => p.Images)
            .WherePublicPlaces()
            .Where(p =>
                p.Id != id &&
                p.Stay == null &&
                p.PlaceTypeId == place.PlaceTypeId &&
                p.Location.DestinationId == place.Location.DestinationId)
            .AsSplitQuery()
            .OrderByDescending(p => p.PlaceReviews.Select(review => (double?)review.Rating).Average() ?? 0d)
            .ThenByDescending(p => p.PlaceReviews.Count)
            .ThenBy(p => p.Name)
            .ThenBy(p => p.Id)
            .Take(5)
            .ToListAsync();

        var relatedEvents = await _db.Events
            .AsNoTracking()
            .Include(e => e.Images)
            .WherePublicEvents()
            .Where(e => e.PlaceId == id)
            .OrderBy(e => e.StartDate)
            .Take(5)
            .ToListAsync();

        var favoriteIds = userId.HasValue
            ? await _favoriteService.GetFavoriteIdsAsync(userId.Value, "place")
            : new HashSet<int>();

        var reviewStats = await GetPlaceReviewStatsAsync(id);
        var previewReviews = await GetPlaceReviewPreviewAsync(id);
        var reviewCount = reviewStats.Count;
        var averageRating = reviewStats.AverageRating;

        var reportedReviewIds = await GetReportedReviewIdsAsync(
            userId,
            ReviewReportTargetType.Place,
            previewReviews.Select(r => r.Id));

        var (originLat, originLng) = ResolveDistanceOrigin(
            useCurrentLocationDistance, latitude, longitude,
            place.Location?.Destination?.Latitude, place.Location?.Destination?.Longitude);

        var detailDistance = SpatialDistanceFormatter.FormatBetween(
            originLat, originLng,
            place.Latitude, place.Longitude,
            useCurrentLocationDistance);
        var images = ToImageDtos(place.Images);
        var amenities = place.PlaceAmenities
            .Select(pa => new AmenityDto
            {
                Id = pa.Amenity.Id,
                Label = pa.Amenity.Name,
                Icon = pa.Amenity.Icon ?? string.Empty
            })
            .GroupBy(a => new { a.Label, a.Icon })
            .Select(g => g.First())
            .ToList();

        return new PlaceDetailDto
        {
            Id = place.Id,
            Title = DisplayTextSafety.CleanRequired(place.Name, "Place"),
            Location = DisplayTextSafety.CleanOptional(place.Location?.Name),
            LocationId = place.LocationId,
            LocationName = DisplayTextSafety.CleanOptional(place.Location?.Name),
            Latitude = HasUsableCoordinates(place.Latitude, place.Longitude) ? place.Latitude : null,
            Longitude = HasUsableCoordinates(place.Latitude, place.Longitude) ? place.Longitude : null,
            HeroImage = GetCoverImageUrl(place.Images) ?? images.FirstOrDefault()?.Url,
            Images = images,
            Tags = DisplayTextSafety.CleanLabels(place.PlaceTags.Select(pt => pt.Tag.Name))
                .Select(label => new TagLabelDto { Label = label })
                .ToList(),
            Rating = averageRating,
            ReviewCount = FormatReviewCountLabel(reviewCount),
            Distance = detailDistance,
            OpenStatus = DeriveOpenStatus(place.OperatingHoursEnabled, place.OperatingHoursJson),
            OpeningHours = DisplayTextSafety.CleanOptional(place.OpeningHours),
            OperatingHoursEnabled = place.OperatingHoursEnabled,
            OperatingHours = ParseStructuredOperatingHours(place.OperatingHoursJson),
            OpeningHoursSchedule = ParseOpeningHoursSchedule(place.OperatingHoursJson),
            Website = DisplayTextSafety.CleanOptional(place.Website),
            About = DisplayTextSafety.CleanOptionalRichMultiline(place.Description),
            Reviews = previewReviews
                .Select(r => new ReviewDto
                {
                    Id = r.Id,
                    AuthorUserId = r.UserId,
                    AuthorName = GetAuthorName(r.User),
                    AvatarUrl = r.User.AvatarUrl,
                    Rating = (int)r.Rating,
                    TimeAgo = FormatTimeAgo(r.CreatedAt),
                    Body = r.Comment,
                    HelpfulCount = 0,
                    ReportedByCurrentUser = reportedReviewIds.Contains(r.Id),
                    IsOwnReview = userId.HasValue && r.UserId == userId.Value
                })
                .ToList(),
            RelatedEvents = relatedEvents
                .Select(e => new PlaceRelatedEventDto
                {
                    Id = e.Id,
                    Title = DisplayTextSafety.CleanRequired(e.Name, "Event"),
                    Subtitle = DisplayTextSafety.CleanOptional(e.Summary ?? e.Description),
                    Image = GetCoverImageUrl(e.Images),
                })
                .ToList(),
            Similar = similar
                .Select(s => new SimilarPlaceDto
                {
                    Id = s.Id,
                    Title = DisplayTextSafety.CleanRequired(s.Name, "Similar place"),
                    Image = GetMainImageUrl(s.Images),
                    Rating = CalculateAverageRating(s.PlaceReviews),
                    Distance = SpatialDistanceFormatter.FormatBetween(
                        originLat, originLng,
                        s.Latitude, s.Longitude,
                        useCurrentLocationDistance),
                    IsSaved = favoriteIds.Contains(s.Id)
                })
                .ToList(),
            IsSaved = isSaved,
            IsOwnable = place.PlaceType?.IsOwnable ?? false,
            IsOwnedByCurrentUser = isOwnedByCurrentUser,
            HasPendingOwnershipClaim = hasPendingOwnershipClaim,
            OwnerContributionId = approvedContributionId,
            ContentType = "place",
            Category = DisplayTextSafety.CleanOptional(place.PlaceType?.Name)?.ToLowerInvariant(),
            ContactEmail = DisplayTextSafety.CleanOptional(place.ContactEmail),
            PhoneNumber = DisplayTextSafety.CleanOptional(place.Number),
            AdmissionPriceRange = DisplayTextSafety.CleanOptional(place.AdmissionPriceRange),
            BookingExternalLink = DisplayTextSafety.CleanOptional(place.BookingExternalLink),
            Amenities = amenities,
            BestTimeToVisit = DisplayTextSafety.CleanOptional(place.BestTimeToVisit),
            VisitorTips = DisplayTextSafety.CleanOptional(place.VisitorTips),
            AccessNotes = DisplayTextSafety.CleanOptional(place.AccessNotes),
            AccessibilityNotes = DisplayTextSafety.CleanOptional(place.AccessibilityNotes),
        };
    }

    public async Task<HotelDetailDto?> GetHotelDetailAsync(
        int id,
        int? userId = null,
        double? latitude = null,
        double? longitude = null,
        bool useCurrentLocationDistance = false)
    {
        var place = await _db.Places
            .AsNoTracking()
            .Include(p => p.PlaceType)
            .Include(p => p.Location)
                .ThenInclude(l => l.Destination)
            .Include(p => p.Images)
            .Include(p => p.Stay)
                .ThenInclude(s => s!.StayType)
            .Include(p => p.Stay)
                .ThenInclude(s => s!.StayAmenities)
                    .ThenInclude(ssa => ssa.StayAmenity)
            .Include(p => p.PlaceTags)
                .ThenInclude(pt => pt.Tag)
                    .ThenInclude(tag => tag.TagType)
                        .ThenInclude(tagType => tagType!.PlaceTypes)
            .Include(p => p.PlaceBookingPartners)
                .ThenInclude(pb => pb.BookingPartner)
            .AsSplitQuery()
            .FirstOrDefaultAsync(p =>
                p.Id == id &&
                p.Status == PublishedStatus &&
                p.Location.Status == PublishedStatus &&
                p.Location.Destination.Status == PublishedStatus &&
                p.Stay != null);

        if (place is null)
        {
            return null;
        }

        var isSaved = userId.HasValue &&
            await _favoriteService.IsFavoriteAsync(userId.Value, "place", id);
        var isOwnedByCurrentUser = userId.HasValue &&
            await _db.UserPlaces.AsNoTracking().AnyAsync(item => item.PlaceId == id && item.UserId == userId.Value);
        var approvedContributionId = userId.HasValue
            ? await GetApprovedEditableContributionIdAsync(id, userId.Value)
            : null;
        var hasPendingOwnershipClaim = userId.HasValue &&
            await _db.PlaceContributions.AsNoTracking().AnyAsync(item =>
                item.PlaceId == id &&
                item.UserId == userId.Value &&
                item.Type == PlaceContributionType.ClaimOwnership &&
                item.Status == PlaceContributionStatus.PendingReview);
        var similar = await _db.Places
            .AsNoTracking()
            .Include(p => p.PlaceReviews)
            .Include(p => p.Images)
            .Where(p =>
                p.Id != id &&
                p.Status == PublishedStatus &&
                p.Location.Status == PublishedStatus &&
                p.Location.Destination.Status == PublishedStatus &&
                p.Stay != null &&
                p.Location.DestinationId == place.Location.DestinationId)
            .AsSplitQuery()
            .OrderByDescending(p => p.PlaceReviews.Select(review => (double?)review.Rating).Average() ?? 0d)
            .ThenByDescending(p => p.PlaceReviews.Count)
            .ThenBy(p => p.Name)
            .ThenBy(p => p.Id)
            .Take(5)
            .ToListAsync();

        var favoriteIds = userId.HasValue
            ? await _favoriteService.GetFavoriteIdsAsync(userId.Value, "place")
            : new HashSet<int>();

        var reviewStats = await GetPlaceReviewStatsAsync(id);
        var previewReviews = await GetPlaceReviewPreviewAsync(id);
        var reviewCount = reviewStats.Count;
        var averageRating = reviewStats.AverageRating;

        var reportedReviewIds = await GetReportedReviewIdsAsync(
            userId,
            ReviewReportTargetType.Place,
            previewReviews.Select(r => r.Id));

        var (originLat, originLng) = ResolveDistanceOrigin(
            useCurrentLocationDistance, latitude, longitude,
            place.Location?.Destination?.Latitude, place.Location?.Destination?.Longitude);

        var detailDistance = SpatialDistanceFormatter.FormatBetween(
            originLat, originLng,
            place.Latitude, place.Longitude,
            useCurrentLocationDistance);

        var stayAmenities = place.Stay?.StayAmenities
            .Select(sa => new AmenityDto
            {
                Id = sa.StayAmenity.Id,
                Label = sa.StayAmenity.Name,
                Icon = sa.StayAmenity.IconKey ?? string.Empty
            }) ?? Enumerable.Empty<AmenityDto>();
        var images = ToImageDtos(place.Images);
        var bookingPartners = BuildHotelBookingPartners(place);

        return new HotelDetailDto
        {
            Id = place.Id,
            Title = DisplayTextSafety.CleanRequired(place.Name, "Stay"),
            Location = DisplayTextSafety.CleanOptional(place.Location?.Name),
            LocationId = place.LocationId,
            LocationName = DisplayTextSafety.CleanOptional(place.Location?.Name),
            Latitude = HasUsableCoordinates(place.Latitude, place.Longitude) ? place.Latitude : null,
            Longitude = HasUsableCoordinates(place.Latitude, place.Longitude) ? place.Longitude : null,
            HeroImage = GetCoverImageUrl(place.Images) ?? images.FirstOrDefault()?.Url,
            Images = images,
            Tags = DisplayTextSafety.CleanLabels(place.PlaceTags.Select(pt => pt.Tag.Name))
                .Select(label => new TagLabelDto { Label = label })
                .ToList(),
            Rating = averageRating,
            ReviewCount = FormatReviewCountLabel(reviewCount),
            Distance = detailDistance,
            PricePerNight = place.Stay?.PricePerNight,
            About = DisplayTextSafety.CleanOptionalRichMultiline(place.Description),
            Website = null,
            ContactEmail = place.ContactEmail,
            PhoneNumber = place.Number,
            BookingPartners = bookingPartners,
            Amenities = stayAmenities
                .GroupBy(a => new { a.Label, a.Icon })
                .Select(g => g.First())
                .ToList(),
            Reviews = previewReviews
                .Select(r => new ReviewDto
                {
                    Id = r.Id,
                    AuthorUserId = r.UserId,
                    AuthorName = GetAuthorName(r.User),
                    AvatarUrl = r.User.AvatarUrl,
                    Rating = (int)r.Rating,
                    TimeAgo = FormatTimeAgo(r.CreatedAt),
                    Body = r.Comment,
                    HelpfulCount = 0,
                    ReportedByCurrentUser = reportedReviewIds.Contains(r.Id),
                    IsOwnReview = userId.HasValue && r.UserId == userId.Value
                })
                .ToList(),
            Similar = similar
                .Select(s => new SimilarPlaceDto
                {
                    Id = s.Id,
                    Title = DisplayTextSafety.CleanRequired(s.Name, "Similar stay"),
                    Image = GetMainImageUrl(s.Images),
                    Rating = CalculateAverageRating(s.PlaceReviews),
                    Distance = SpatialDistanceFormatter.FormatBetween(
                        originLat, originLng,
                        s.Latitude, s.Longitude,
                        useCurrentLocationDistance),
                    IsSaved = favoriteIds.Contains(s.Id)
                })
                .ToList(),
            IsSaved = isSaved,
            IsOwnable = place.PlaceType?.IsOwnable ?? false,
            IsOwnedByCurrentUser = isOwnedByCurrentUser,
            HasPendingOwnershipClaim = hasPendingOwnershipClaim,
            OwnerContributionId = approvedContributionId,
        };
    }

    public async Task<DestinationScopedResult<PlaceListingResponseDto>> GetHotelsAsync(
        int? destinationId,
        string? tag = null,
        int? userId = null,
        double? latitude = null,
        double? longitude = null,
        int skip = 0,
        int? take = null)
    {
        if (!destinationId.HasValue || destinationId.Value <= 0)
        {
            return new DestinationScopedResult<PlaceListingResponseDto>
            {
                Status = DestinationScopedStatus.InvalidDestination
            };
        }

        var destination = await _db.Destinations
            .AsNoTracking()
            .Where(d => d.Id == destinationId.Value && d.Status == PublishedStatus)
            .Select(d => new { d.Id, d.Latitude, d.Longitude })
            .SingleOrDefaultAsync();

        if (destination is null)
        {
            return new DestinationScopedResult<PlaceListingResponseDto>
            {
                Status = DestinationScopedStatus.DestinationNotFound
            };
        }

        var userPoint = BuildUserPoint(latitude, longitude)
            ?? BuildUserPoint(destination.Latitude, destination.Longitude);

        var query = _db.Places
            .AsNoTracking()
            .Where(p =>
                p.Status == PublishedStatus &&
                p.Location.Status == PublishedStatus &&
                p.Location.Destination.Status == PublishedStatus &&
                p.Stay != null &&
                p.Location.DestinationId == destination.Id);

        if (!string.IsNullOrWhiteSpace(tag) && !IsAllFilter(tag))
        {
            var tagTerms = BuildFilterTerms(tag);
            query = query.Where(p =>
                p.PlaceTags.Any(pt =>
                    tagTerms.Contains(pt.Tag.Slug.ToLower()) ||
                    tagTerms.Contains(pt.Tag.Name.ToLower())) ||
                tagTerms.Contains(p.PlaceType.Name.ToLower()) ||
                (p.Stay != null && tagTerms.Contains(p.Stay.StayType.Name.ToLower())));
        }

        var items = await BuildPlaceListItemsAsync(
            query,
            destination.Id,
            userId,
            userPoint,
            skip,
            take);

        return new DestinationScopedResult<PlaceListingResponseDto>
        {
            Status = DestinationScopedStatus.Success,
            Data = items
        };
    }

    public async Task<GetPlaceDetailsResponseDto> GetPlaceDetailsAsync(int placeId, int? userId)
    {
        var place = await _db.Places
            .AsNoTracking()
            .Include(p => p.PlaceReviews)
            .Include(p => p.Events)
            .AsSplitQuery()
            .FirstOrDefaultAsync(p =>
                p.Id == placeId &&
                p.Status == PublishedStatus &&
                p.Location.Status == PublishedStatus &&
                p.Location.Destination.Status == PublishedStatus)
            ?? throw new KeyNotFoundException("Place not found");

        var reviews = place.PlaceReviews.ToList();
        var averageRating = reviews.Count > 0 ? Math.Round(reviews.Average(r => r.Rating), 2) : 0;

        var isSaved = false;
        var savedCollectionIds = new List<int>();

        if (userId.HasValue)
        {
            isSaved = await _favoriteService.IsFavoriteAsync(userId.Value, "place", placeId);
            savedCollectionIds = await _db.CollectionItems
                .Where(ci => ci.PlaceId == placeId && ci.Collection.UserId == userId.Value)
                .Select(ci => ci.CollectionId)
                .ToListAsync();
        }

        return new GetPlaceDetailsResponseDto
        {
            Id = place.Id,
            Name = place.Name,
            PlaceTypeId = place.PlaceTypeId,
            LocationId = place.LocationId,
            Description = place.Description,
            Latitude = place.Latitude,
            Longitude = place.Longitude,
            ContactEmail = place.ContactEmail,
            Number = place.Number,
            CreatedAt = place.CreatedAt,
            UpdatedAt = place.UpdatedAt,
            AverageRating = averageRating,
            ReviewsCount = reviews.Count,
            IsSaved = isSaved,
            SavedCollectionIds = savedCollectionIds,
            Events = place.Events
                .Where(e => e.Status == PublishedStatus)
                .OrderBy(e => e.StartDate)
                .Select(e => new PlaceEventItemDto
                {
                    Id = e.Id,
                    Name = e.Name,
                    LocationId = e.LocationId ?? place.LocationId,
                    PlaceId = e.PlaceId ?? 0,
                    Description = e.Description,
                    StartDate = e.StartDate,
                    EndDate = e.EndDate,
                    CreatedAt = e.CreatedAt,
                    UpdatedAt = e.UpdatedAt
                })
                .ToList()
        };
    }

    public async Task<GetPlaceEventsResponseDto> GetPlaceEventsAsync(int placeId)
    {
        var exists = await _db.Places
            .WherePublicPlaces()
            .AnyAsync(p => p.Id == placeId);
        if (!exists)
        {
            throw new KeyNotFoundException("Place not found");
        }

        var events = await _db.Events
            .WherePublicEvents()
            .Where(e => e.PlaceId == placeId)
            .OrderBy(e => e.StartDate)
            .ToListAsync();

        return new GetPlaceEventsResponseDto
        {
            PlaceId = placeId,
            Events = events
                .Select(e => new PlaceEventItemDto
                {
                    Id = e.Id,
                    Name = e.Name,
                    LocationId = e.LocationId ?? 0,
                    PlaceId = e.PlaceId ?? 0,
                    Description = e.Description,
                    StartDate = e.StartDate,
                    EndDate = e.EndDate,
                    CreatedAt = e.CreatedAt,
                    UpdatedAt = e.UpdatedAt
                })
                .ToList()
        };
    }

    public async Task<GetPlaceReviewsResponseDto> GetPlaceReviewsAsync(
        int placeId,
        int skip = 0,
        int take = ReviewPaging.DefaultTake)
    {
        var exists = await _db.Places.AnyAsync(p =>
            p.Id == placeId &&
            p.Status == PublishedStatus &&
            p.Location.Status == PublishedStatus &&
            p.Location.Destination.Status == PublishedStatus);
        if (!exists)
        {
            throw new KeyNotFoundException("Place not found");
        }

        var normalizedSkip = ReviewPaging.NormalizeSkip(skip);
        var normalizedTake = ReviewPaging.NormalizeTake(take);
        var reviewStats = await GetPlaceReviewStatsAsync(placeId);
        var reviews = await _db.PlaceReviews
            .AsNoTracking()
            .Where(r => r.PlaceId == placeId)
            .OrderByDescending(r => r.CreatedAt)
            .ThenByDescending(r => r.Id)
            .Skip(normalizedSkip)
            .Take(normalizedTake)
            .ToListAsync();

        return new GetPlaceReviewsResponseDto
        {
            PlaceId = placeId,
            AverageRating = reviewStats.AverageRating ?? 0,
            ReviewsCount = reviewStats.Count,
            TotalCount = reviewStats.Count,
            Skip = normalizedSkip,
            Take = normalizedTake,
            NextSkip = normalizedSkip + reviews.Count,
            HasMore = reviewStats.Count > normalizedSkip + reviews.Count,
            Reviews = reviews
                .Select(r => new PlaceReviewItemDto
                {
                    Id = r.Id,
                    UserId = r.UserId,
                    Rating = r.Rating,
                    Comment = r.Comment,
                    CreatedAt = r.CreatedAt,
                    UpdatedAt = r.UpdatedAt
                })
                .ToList()
        };
    }

    public async Task<CreatePlaceReviewResponseDto> CreatePlaceReviewAsync(
        int placeId,
        int userId,
        CreatePlaceReviewRequestDto request)
    {
        if (request.Rating < 1 || request.Rating > 5)
        {
            throw new InvalidOperationException("Rating must be between 1 and 5");
        }

        if ((request.Comment ?? string.Empty).Length > 1000)
        {
            throw new InvalidOperationException("Comment is too long");
        }

        await _contentModeration.EnsureAllowedReviewTextAsync(request.Comment);

        var exists = await _db.Places.AnyAsync(p =>
            p.Id == placeId &&
            p.Status == PublishedStatus &&
            p.Location.Status == PublishedStatus &&
            p.Location.Destination.Status == PublishedStatus);
        if (!exists)
        {
            throw new KeyNotFoundException("Place not found");
        }

        var alreadyReviewed = await _db.PlaceReviews
            .AnyAsync(r => r.PlaceId == placeId && r.UserId == userId);

        if (alreadyReviewed)
        {
            throw new InvalidOperationException("User has already reviewed this place");
        }

        var review = new PlaceReview
        {
            UserId = userId,
            PlaceId = placeId,
            Rating = request.Rating,
            Comment = request.Comment?.Trim() ?? string.Empty,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        await _db.PlaceReviews.AddAsync(review);
        await _db.SaveChangesAsync();

        return new CreatePlaceReviewResponseDto
        {
            Id = review.Id,
            UserId = review.UserId,
            PlaceId = review.PlaceId,
            Rating = review.Rating,
            Comment = review.Comment,
            CreatedAt = review.CreatedAt,
            UpdatedAt = review.UpdatedAt
        };
    }

    private async Task<(int Count, double? AverageRating)> GetPlaceReviewStatsAsync(int placeId)
    {
        var stats = await _db.PlaceReviews
            .AsNoTracking()
            .Where(r => r.PlaceId == placeId)
            .GroupBy(r => r.PlaceId)
            .Select(group => new
            {
                Count = group.Count(),
                AverageRating = group.Average(r => r.Rating)
            })
            .SingleOrDefaultAsync();

        return stats is null
            ? (0, null)
            : (stats.Count, Math.Round(stats.AverageRating, 1));
    }

    private async Task<List<PlaceReview>> GetPlaceReviewPreviewAsync(int placeId)
    {
        return await _db.PlaceReviews
            .AsNoTracking()
            .Include(r => r.User)
            .Where(r => r.PlaceId == placeId)
            .OrderByDescending(r => r.CreatedAt)
            .ThenByDescending(r => r.Id)
            .Take(ReviewPaging.DefaultTake)
            .ToListAsync();
    }

    private async Task<int?> GetApprovedEditableContributionIdAsync(int placeId, int userId)
    {
        return await _db.PlaceContributions
            .AsNoTracking()
            .Where(item =>
                item.PlaceId == placeId &&
                item.UserId == userId &&
                item.Status == PlaceContributionStatus.Approved &&
                item.OwnershipProofDocumentUrl != null)
            .OrderByDescending(item => item.Type == PlaceContributionType.ClaimOwnership)
            .ThenByDescending(item => item.UpdatedAt ?? item.CreatedAt)
            .Select(item => (int?)item.Id)
            .FirstOrDefaultAsync();
    }

    private async Task<PlaceListingResponseDto> BuildPlaceListItemsAsync(
        IQueryable<Place> query,
        int destinationId,
        int? userId,
        Point? userPoint,
        int skip,
        int? take)
    {
        var normalizedSkip = Math.Max(0, skip);
        var normalizedTake = take.HasValue
            ? Math.Clamp(take.Value, 1, ListingMaxTake)
            : (int?)null;

        var favoriteIds = userId.HasValue
            ? await _favoriteService.GetFavoriteIdsAsync(userId.Value, "place")
            : new HashSet<int>();

        IQueryable<PlaceListProjection> orderedQuery;

        if (userPoint is null)
        {
            orderedQuery = query
                .Select(p => new PlaceListProjection
                {
                    Id = p.Id,
                    Title = p.Name,
                    Subtitle = p.Subtitle,
                    Description = p.Description,
                    Location = p.Location.Name,
                    Image = p.Images
                        .OrderBy(image => image.ImageRole == EntityImageRole.Card ? 0 : image.ImageRole == EntityImageRole.Cover ? 1 : 2)
                        .ThenBy(image => image.SortOrder)
                        .ThenBy(image => image.CreatedAt)
                        .ThenBy(image => image.Id)
                        .Select(image => image.Url)
                        .FirstOrDefault(),
                    Rating = p.PlaceReviews.Select(r => (double?)r.Rating).Average(),
                    ReviewCount = p.PlaceReviews.Count(),
                    OpeningHours = p.OpeningHours,
                    PricePerNight = p.Stay != null ? p.Stay.PricePerNight : null,
                    DestinationPriority = p.Location.DestinationId == destinationId ? 0 : 1,
                    Tags = p.PlaceTags
                        .OrderBy(pt => pt.Tag.Name)
                        .Select(pt => pt.Tag.Name)
                        .ToList()
                })
                .OrderBy(p => p.DestinationPriority)
                .ThenByDescending(p => p.Rating ?? 0)
                .ThenBy(p => p.Title)
                .ThenBy(p => p.Id);
        }
        else
        {
            orderedQuery = query
                .Where(p => EF.Property<Point?>(p, SpatialPointFactory.CoordinatesPropertyName) != null)
                .Select(p => new PlaceListProjection
                {
                    Id = p.Id,
                    Title = p.Name,
                    Subtitle = p.Subtitle,
                    Description = p.Description,
                    Location = p.Location.Name,
                    Image = p.Images
                        .OrderBy(image => image.ImageRole == EntityImageRole.Card ? 0 : image.ImageRole == EntityImageRole.Cover ? 1 : 2)
                        .ThenBy(image => image.SortOrder)
                        .ThenBy(image => image.CreatedAt)
                        .ThenBy(image => image.Id)
                        .Select(image => image.Url)
                        .FirstOrDefault(),
                    Rating = p.PlaceReviews.Select(r => (double?)r.Rating).Average(),
                    ReviewCount = p.PlaceReviews.Count(),
                    OpeningHours = p.OpeningHours,
                    PricePerNight = p.Stay != null ? p.Stay.PricePerNight : null,
                    DestinationPriority = p.Location.DestinationId == destinationId ? 0 : 1,
                    DistanceMeters = EF.Property<Point>(p, SpatialPointFactory.CoordinatesPropertyName).Distance(userPoint),
                    Tags = p.PlaceTags
                        .OrderBy(pt => pt.Tag.Name)
                        .Select(pt => pt.Tag.Name)
                        .ToList()
                })
                .OrderBy(p => p.DestinationPriority)
                .ThenBy(p => p.DistanceMeters)
                .ThenByDescending(p => p.Rating ?? 0)
                .ThenBy(p => p.Title)
                .ThenBy(p => p.Id);
        }

        if (normalizedSkip > 0)
        {
            orderedQuery = orderedQuery.Skip(normalizedSkip);
        }

        if (normalizedTake.HasValue)
        {
            orderedQuery = orderedQuery.Take(normalizedTake.Value + 1);
        }

        var places = await orderedQuery.ToListAsync();
        var pageSize = normalizedTake ?? places.Count;
        var hasMore = normalizedTake.HasValue && places.Count > pageSize;
        var pagePlaces = hasMore
            ? places.Take(pageSize).ToList()
            : places;

        var items = pagePlaces
            .DistinctBy(place => place.Id)
            .Select((place, index) => new PlaceListItemDto
            {
                Id = place.Id,
                Title = DisplayTextSafety.CleanRequired(place.Title, "Place"),
                Subtitle = DisplayTextSafety.CleanOptional(place.Subtitle),
                Description = TrimDescription(place.Description, 150),
                Location = DisplayTextSafety.CleanOptional(place.Location),
                Image = place.Image,
                Rating = place.Rating,
                ReviewCount = FormatReviewCountLabel(place.ReviewCount),
                OpeningHours = place.OpeningHours,
                Distance = SpatialDistanceFormatter.Format(place.DistanceMeters),
                PricePerNight = place.PricePerNight,
                Tags = DisplayTextSafety.CleanLabels(place.Tags),
                Variant = normalizedSkip + index == 0 ? "large" : "compact",
                IsSaved = favoriteIds.Contains(place.Id)
            })
            .ToList();

        return new PlaceListingResponseDto
        {
            Items = items,
            Skip = normalizedSkip,
            Take = normalizedTake ?? items.Count,
            NextSkip = normalizedSkip + items.Count,
            HasMore = hasMore
        };
    }

    private static Point? BuildUserPoint(double? latitude, double? longitude)
    {
        return latitude.HasValue && longitude.HasValue && HasUsableCoordinates(latitude.Value, longitude.Value)
            ? SpatialPointFactory.Create(latitude.Value, longitude.Value)
            : null;
    }

    private static bool IsAllFilter(string value) =>
        string.Equals(value.Trim(), "all", StringComparison.OrdinalIgnoreCase);

    private static bool IsStayFilter(IEnumerable<string> terms)
    {
        return terms.Any(term =>
            term is "stay" or "stays" or "hotel" or "hotels" or "accommodation" or "accommodations");
    }

    private static string? GetCoverImageUrl(IEnumerable<EntityImage> images)
    {
        return images
            .Where(image => image.ImageRole == EntityImageRole.Cover)
            .OrderBy(image => image.SortOrder)
            .ThenBy(image => image.CreatedAt)
            .ThenBy(image => image.Id)
            .Select(image => image.Url)
            .FirstOrDefault();
    }

    private static string? GetMainImageUrl(IEnumerable<EntityImage> images)
    {
        return OrderImages(images)
            .Select(image => image.Url)
            .FirstOrDefault();
    }

    private static List<EntityImageDto> ToImageDtos(IEnumerable<EntityImage> images)
    {
        return OrderImages(images)
            .Select(image => new EntityImageDto
            {
                Id = image.Id,
                Url = image.Url,
                AltText = DisplayTextSafety.CleanOptional(image.AltText),
                Caption = DisplayTextSafety.CleanOptional(image.Caption),
                IsMain = image.IsMain,
                ImageRole = EntityImageRole.Normalize(image.ImageRole),
                SortOrder = image.SortOrder,
                ImagePosition = string.IsNullOrWhiteSpace(image.ImagePosition) ? "50% 50%" : image.ImagePosition,
                ImageSize = string.IsNullOrWhiteSpace(image.ImageSize) ? "cover" : image.ImageSize
            })
            .ToList();
    }

    private static IOrderedEnumerable<EntityImage> OrderImages(IEnumerable<EntityImage> images)
    {
        return images
            .OrderBy(image => image.ImageRole == EntityImageRole.Card ? 0 : image.ImageRole == EntityImageRole.Cover ? 1 : 2)
            .ThenBy(image => image.SortOrder)
            .ThenBy(image => image.CreatedAt)
            .ThenBy(image => image.Id);
    }

    private static List<string> BuildFilterTerms(string? rawValue)
    {
        if (string.IsNullOrWhiteSpace(rawValue) ||
            string.Equals(rawValue.Trim(), "all", StringComparison.OrdinalIgnoreCase))
        {
            return [];
        }

        var terms = new HashSet<string>();

        foreach (var rawTerm in rawValue.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            var normalized = rawTerm.ToLowerInvariant();
            AddTermVariants(terms, normalized);
            AddTermVariants(terms, normalized.Replace(" ", "-"));
            AddTermVariants(terms, normalized.Replace("-", " "));
        }

        return terms.ToList();
    }

    private static void AddTermVariants(HashSet<string> terms, string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        var normalized = value.Trim();
        terms.Add(normalized);

        if (normalized.EndsWith("ies", StringComparison.Ordinal) && normalized.Length > 3)
        {
            terms.Add(normalized[..^3] + "y");
            return;
        }

        if (normalized.EndsWith("s", StringComparison.Ordinal) && normalized.Length > 1)
        {
            terms.Add(normalized[..^1]);
            return;
        }

        terms.Add(normalized + "s");
    }

    private static (double? lat, double? lng) ResolveDistanceOrigin(
        bool useCurrentLocationDistance,
        double? userLat, double? userLng,
        double? destinationLat, double? destinationLng) =>
        useCurrentLocationDistance
            ? (userLat, userLng)
            : (destinationLat, destinationLng);

    private static bool HasUsableCoordinates(double latitude, double longitude) =>
        !double.IsNaN(latitude) &&
        !double.IsNaN(longitude) &&
        !double.IsInfinity(latitude) &&
        !double.IsInfinity(longitude) &&
        latitude >= -90 &&
        latitude <= 90 &&
        longitude >= -180 &&
        longitude <= 180 &&
        (latitude != 0d || longitude != 0d);

    private static string GetAuthorName(Domain.Entities.Users.User user)
    {
        if (!string.IsNullOrWhiteSpace(user.FirstName))
        {
            return $"{user.FirstName} {user.LastName}".Trim();
        }

        return user.Username;
    }

    private static string? FormatReviewCountLabel(int count) => count switch
    {
        0 => null,
        1 => "1 review",
        _ => $"{count} reviews"
    };

    private static double? CalculateAverageRating(ICollection<PlaceReview> reviews)
    {
        if (reviews.Count == 0)
        {
            return null;
        }

        return Math.Round(reviews.Average(r => r.Rating), 1);
    }

    private static string? DeriveOpenStatus(bool enabled, string? operatingHoursJson) =>
        OperatingHoursStatusCalculator.Evaluate(enabled, operatingHoursJson) switch
        {
            OperatingHoursStatus.Open => "Open now",
            OperatingHoursStatus.Closed => "Closed now",
            _ => null
        };

    private static List<PlaceOperatingHourDto>? ParseStructuredOperatingHours(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
                return null;

            var result = new List<PlaceOperatingHourDto>();
            foreach (var item in document.RootElement.EnumerateArray())
            {
                var dayOfWeek = ReadJsonString(item, "dayOfWeek");
                if (string.IsNullOrWhiteSpace(dayOfWeek))
                    continue;

                var isOpen = ReadJsonBoolean(item, "isOpen");
                result.Add(new PlaceOperatingHourDto
                {
                    DayOfWeek = dayOfWeek,
                    IsOpen = isOpen,
                    OpenTime = isOpen ? ReadJsonString(item, "openTime") : null,
                    CloseTime = isOpen ? ReadJsonString(item, "closeTime") : null,
                });
            }

            return result.Count == 0 ? null : result;
        }
        catch
        {
            return null;
        }
    }

    private static List<OpeningHoursDayDto>? ParseOpeningHoursSchedule(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
                return null;

            var groupedSchedule = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

            foreach (var item in document.RootElement.EnumerateArray())
            {
                var legacyDay = ReadJsonString(item, "day");
                var legacyHours = ReadJsonString(item, "hours");
                if (!string.IsNullOrWhiteSpace(legacyDay))
                {
                    AddOpeningHoursScheduleEntry(
                        groupedSchedule,
                        NormalizeOpeningHoursDayLabel(legacyDay),
                        NormalizeOpeningHoursText(legacyHours));
                    continue;
                }

                var dayOfWeek = ReadJsonString(item, "dayOfWeek");
                if (string.IsNullOrWhiteSpace(dayOfWeek))
                    continue;

                var dayLabel = NormalizeOpeningHoursDayLabel(dayOfWeek);
                var isOpen = ReadJsonBoolean(item, "isOpen");
                var openTime = ReadJsonString(item, "openTime");
                var closeTime = ReadJsonString(item, "closeTime");

                var displayHours = isOpen && !string.IsNullOrWhiteSpace(openTime) && !string.IsNullOrWhiteSpace(closeTime)
                    ? FormatOpeningHoursSlot(openTime, closeTime)
                    : "Closed";

                AddOpeningHoursScheduleEntry(groupedSchedule, dayLabel, displayHours);
            }

            var schedule = DayOrder
                .Where(groupedSchedule.ContainsKey)
                .Select(day => new OpeningHoursDayDto
                {
                    Day = day,
                    Hours = FormatOpeningHoursDay(groupedSchedule[day])
                })
                .ToList();

            foreach (var entry in groupedSchedule.Where(entry => !DayOrder.Contains(entry.Key, StringComparer.OrdinalIgnoreCase)))
            {
                schedule.Add(new OpeningHoursDayDto
                {
                    Day = entry.Key,
                    Hours = FormatOpeningHoursDay(entry.Value)
                });
            }

            return schedule.Count == 0 ? null : schedule;
        }
        catch
        {
            return null;
        }
    }

    private static readonly string[] DayOrder =
    [
        "Monday",
        "Tuesday",
        "Wednesday",
        "Thursday",
        "Friday",
        "Saturday",
        "Sunday"
    ];

    private static void AddOpeningHoursScheduleEntry(
        IDictionary<string, List<string>> schedule,
        string day,
        string hours)
    {
        if (!schedule.TryGetValue(day, out var dayHours))
        {
            dayHours = new List<string>();
            schedule[day] = dayHours;
        }

        if (!string.IsNullOrWhiteSpace(hours))
            dayHours.Add(hours);
    }

    private static string FormatOpeningHoursSlot(string openTime, string closeTime)
    {
        var open = openTime.Trim();
        var close = closeTime.Trim();
        return open == "00:00" && close == "00:00"
            ? "Open 24 hours"
            : $"{open} - {close}";
    }

    private static string FormatOpeningHoursDay(IReadOnlyCollection<string> values)
    {
        var nonEmpty = values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .ToList();

        if (nonEmpty.Count == 0 || nonEmpty.All(value => string.Equals(value, "Closed", StringComparison.OrdinalIgnoreCase)))
            return "Closed";

        return string.Join(", ", nonEmpty.Where(value => !string.Equals(value, "Closed", StringComparison.OrdinalIgnoreCase)));
    }

    private static string? ReadJsonString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;
    }

    private static bool ReadJsonBoolean(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.True;
    }

    private static string NormalizeOpeningHoursDayLabel(string value)
    {
        return value.Trim().ToLowerInvariant() switch
        {
            "mon" or "monday" => "Monday",
            "tue" or "tuesday" => "Tuesday",
            "wed" or "wednesday" => "Wednesday",
            "thu" or "thursday" => "Thursday",
            "fri" or "friday" => "Friday",
            "sat" or "saturday" => "Saturday",
            "sun" or "sunday" => "Sunday",
            var other => other
        };
    }

    private static string NormalizeOpeningHoursText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "Closed";

        var normalized = value.Trim()
            .Replace('–', '-')
            .Replace('—', '-');

        normalized = Regex.Replace(normalized, @"\s*-\s*", " - ");
        normalized = Regex.Replace(normalized, @"\s*,\s*", ", ");
        return normalized;
    }

    private static OwnerUpdateOperatingHoursResult NormalizeOwnerUpdateOperatingHours(
        bool enabled,
        IReadOnlyCollection<OwnerUpdateOperatingHourDto>? hours)
    {
        if (!enabled)
            return new OwnerUpdateOperatingHoursResult(false, null, []);

        var normalizedHours = new List<OwnerUpdateOperatingHourDto>();
        var seenDays = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var input in hours ?? Array.Empty<OwnerUpdateOperatingHourDto>())
        {
            var day = NormalizeOwnerUpdateDayOfWeek(input.DayOfWeek);
            if (!seenDays.Add(day))
                throw new InvalidOperationException("Operating hours contain duplicate days.");

            if (!input.IsOpen)
            {
                normalizedHours.Add(new OwnerUpdateOperatingHourDto
                {
                    DayOfWeek = day,
                    IsOpen = false
                });
                continue;
            }

            var openTime = NormalizeOwnerUpdateClockTime(input.OpenTime, "Opening time is invalid.");
            var closeTime = NormalizeOwnerUpdateClockTime(input.CloseTime, "Closing time is invalid.");
            // 00:00 - 00:00 is the "open 24 hours" sentinel; any other equal pair is ambiguous.
            if (openTime == closeTime && openTime != "00:00")
                throw new InvalidOperationException("Opening time and closing time cannot be the same.");

            normalizedHours.Add(new OwnerUpdateOperatingHourDto
            {
                DayOfWeek = day,
                IsOpen = true,
                OpenTime = openTime,
                CloseTime = closeTime
            });
        }

        if (normalizedHours.Count == 0)
            throw new InvalidOperationException("Operating hours are required when enabled.");

        return new OwnerUpdateOperatingHoursResult(
            true,
            JsonSerializer.Serialize(normalizedHours, JsonOptions),
            normalizedHours);
    }

    private static List<OwnerUpdateOperatingHourDto> DeserializeOwnerUpdateOperatingHours(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return [];

        try
        {
            return JsonSerializer.Deserialize<List<OwnerUpdateOperatingHourDto>>(json, JsonOptions) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static string NormalizeOwnerUpdateDayOfWeek(string? dayOfWeek)
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

    private static string NormalizeOwnerUpdateClockTime(string? value, string errorMessage)
    {
        var normalized = value?.Trim() ?? string.Empty;
        if (!TimeSpan.TryParseExact(normalized, "hh\\:mm", null, out _))
            throw new InvalidOperationException(errorMessage);

        return normalized;
    }

    private static string? BuildOwnerUpdateOpeningHoursSummary(IReadOnlyCollection<OwnerUpdateOperatingHourDto> hours)
    {
        if (hours.Count == 7 && hours.All(hour =>
                hour.IsOpen && hour.OpenTime == "00:00" && hour.CloseTime == "00:00"))
            return "Open 24 hours";

        var firstOpen = hours.FirstOrDefault(hour => hour.IsOpen);
        return firstOpen is null
            ? "Closed"
            : $"{firstOpen.DayOfWeek} {firstOpen.OpenTime} - {firstOpen.CloseTime}";
    }

    private static string? TrimDescription(string? description, int maxLength)
    {
        var cleaned = DisplayTextSafety.CleanOptional(description);
        if (string.IsNullOrWhiteSpace(cleaned))
        {
            return null;
        }

        return cleaned.Length > maxLength
            ? cleaned[..maxLength] + "..."
            : cleaned;
    }

    private static string FormatTimeAgo(DateTime date)
    {
        var diff = DateTime.UtcNow - date;
        if (diff.TotalMinutes < 60) return $"{Math.Max(1, (int)diff.TotalMinutes)}m ago";
        if (diff.TotalHours < 24) return $"{(int)diff.TotalHours}h ago";
        if (diff.TotalDays < 30) return $"{(int)diff.TotalDays}d ago";
        if (diff.TotalDays < 365) return $"{(int)(diff.TotalDays / 30)}mo ago";
        return $"{(int)(diff.TotalDays / 365)}y ago";
    }

    private async Task<HashSet<int>> GetReportedReviewIdsAsync(
        int? currentUserId,
        ReviewReportTargetType targetType,
        IEnumerable<int> reviewIds)
    {
        if (!currentUserId.HasValue)
        {
            return new HashSet<int>();
        }

        var ids = reviewIds.Distinct().ToList();
        if (ids.Count == 0)
        {
            return new HashSet<int>();
        }

        var reportedIds = await _db.ReviewReports
            .AsNoTracking()
            .Where(report =>
                report.ReporterUserId == currentUserId.Value &&
                report.TargetType == targetType &&
                ids.Contains(report.ReviewId))
            .Select(report => report.ReviewId)
            .ToListAsync();

        return reportedIds.ToHashSet();
    }

    public async Task<PlaceDetailDto> OwnerUpdatePlaceAsync(int placeId, int userId, OwnerUpdatePlaceRequestDto request)
    {
        var isOwner = await _db.UserPlaces
            .AnyAsync(up => up.PlaceId == placeId && up.UserId == userId);

        if (!isOwner)
            throw new UnauthorizedAccessException("You are not the owner of this place.");

        var place = await _db.Places
            .Include(p => p.PlaceType)
            .Include(p => p.Location)
                .ThenInclude(l => l.Destination)
            .Include(p => p.Images)
            .Include(p => p.Stay)
                .ThenInclude(stay => stay!.StayAmenities)
                    .ThenInclude(stayAmenity => stayAmenity.StayAmenity)
            .Include(p => p.PlaceTags)
                .ThenInclude(pt => pt.Tag)
                    .ThenInclude(tag => tag.TagType)
                        .ThenInclude(tagType => tagType!.PlaceTypes)
            .Include(p => p.PlaceAmenities)
                .ThenInclude(placeAmenity => placeAmenity.Amenity)
            .Include(p => p.PlaceBookingPartners)
                .ThenInclude(placeBookingPartner => placeBookingPartner.BookingPartner)
            .AsSplitQuery()
            .FirstOrDefaultAsync(p =>
                p.Id == placeId &&
                p.Status == PublishedStatus &&
                p.Location.Status == PublishedStatus &&
                p.Location.Destination.Status == PublishedStatus)
            ?? throw new KeyNotFoundException("Place not found.");

        var now = DateTime.UtcNow;
        var changedFields = new List<string>();

        if (!string.IsNullOrWhiteSpace(request.Name))
        {
            place.Name = NormalizeOwnerUpdateRequiredName(request.Name, "Name", 200);
            changedFields.Add(nameof(request.Name));
        }

        if (request.Description != null)
        {
            place.Description = NormalizeOwnerUpdateDescription(request.Description);
            changedFields.Add(nameof(request.Description));
        }

        if (request.OperatingHoursEnabled.HasValue || request.OperatingHours is not null)
        {
            var operatingHours = NormalizeOwnerUpdateOperatingHours(
                request.OperatingHoursEnabled ?? place.OperatingHoursEnabled,
                request.OperatingHours ?? DeserializeOwnerUpdateOperatingHours(place.OperatingHoursJson));
            place.OperatingHoursEnabled = operatingHours.Enabled;
            place.OperatingHoursJson = operatingHours.Json;
            place.OpeningHours = operatingHours.Enabled ? BuildOwnerUpdateOpeningHoursSummary(operatingHours.Hours) : null;
            if (request.OperatingHoursEnabled.HasValue)
                changedFields.Add(nameof(request.OperatingHoursEnabled));
            if (request.OperatingHours is not null)
                changedFields.Add(nameof(request.OperatingHours));
        }
        else if (request.OpeningHours != null)
        {
            place.OpeningHours = NormalizeOwnerUpdateOptional(request.OpeningHours, 1000, "Opening hours are too long.");
            place.OperatingHoursEnabled = false;
            place.OperatingHoursJson = null;
            changedFields.Add(nameof(request.OpeningHours));
        }

        if (request.Website != null)
        {
            place.Website = NormalizeOwnerUpdateWebsite(request.Website);
            changedFields.Add(nameof(request.Website));
        }

        if (request.ContactEmail != null)
        {
            place.ContactEmail = NormalizeOwnerUpdateContactEmail(request.ContactEmail);
            changedFields.Add(nameof(request.ContactEmail));
        }

        if (request.PhoneNumber != null)
        {
            place.Number = NormalizeOwnerUpdatePhone(request.PhoneNumber);
            changedFields.Add(nameof(request.PhoneNumber));
        }

        if (request.AdmissionPriceRange != null)
        {
            place.AdmissionPriceRange = NormalizeOwnerUpdateOptional(request.AdmissionPriceRange, 200, "Admission / price range is too long.");
            changedFields.Add(nameof(request.AdmissionPriceRange));
        }

        if (request.BookingExternalLink != null)
        {
            place.BookingExternalLink = NormalizeOwnerUpdateExternalHttpsUrl(request.BookingExternalLink, 500, "Booking external link");
            changedFields.Add(nameof(request.BookingExternalLink));
        }

        if (request.BestTimeToVisit != null)
        {
            place.BestTimeToVisit = NormalizeOwnerUpdateOptional(request.BestTimeToVisit, 500, "Best time to visit is too long.");
            changedFields.Add(nameof(request.BestTimeToVisit));
        }

        if (request.VisitorTips != null)
        {
            place.VisitorTips = NormalizeOwnerUpdateOptional(request.VisitorTips, 1000, "Visitor tips are too long.");
            changedFields.Add(nameof(request.VisitorTips));
        }

        if (request.AccessNotes != null)
        {
            place.AccessNotes = NormalizeOwnerUpdateOptional(request.AccessNotes, 1000, "Access notes are too long.");
            changedFields.Add(nameof(request.AccessNotes));
        }

        if (request.AccessibilityNotes != null)
        {
            place.AccessibilityNotes = NormalizeOwnerUpdateOptional(request.AccessibilityNotes, 1000, "Accessibility notes are too long.");
            changedFields.Add(nameof(request.AccessibilityNotes));
        }

        if (request.PlaceTypeId.HasValue && request.PlaceTypeId.Value > 0 && request.PlaceTypeId.Value != place.PlaceTypeId)
            throw new InvalidOperationException("Place type cannot be changed after the place is approved.");

        if (request.Tags is not null)
        {
            await SyncOwnerUpdateTagsAsync(place.Id, request.Tags, now);
            changedFields.Add(nameof(request.Tags));
        }

        if (request.Amenities is not null)
        {
            if (place.Stay is not null)
                await SyncOwnerUpdateStayAmenitiesAsync(place.Id, request.Amenities);
            else
                await SyncOwnerUpdateAmenitiesAsync(place.Id, request.Amenities);

            changedFields.Add(nameof(request.Amenities));
        }

        if (request.BookingPartners is not null)
        {
            await SyncOwnerUpdateBookingPartnersAsync(place.Id, request.BookingPartners);
            changedFields.Add(nameof(request.BookingPartners));
        }

        if (request.Images is { Count: > 0 })
        {
            var existingImages = await _db.EntityImages
                .Where(img => img.PlaceId == place.Id)
                .ToListAsync();

            _db.EntityImages.RemoveRange(existingImages);

            var mainSeen = false;
            foreach (var img in request.Images.OrderBy(i => i.SortOrder))
            {
                if (string.IsNullOrWhiteSpace(img.Url)) continue;

                var role = EntityImageRole.Normalize(img.ImageRole);
                var isMain = !mainSeen && role == EntityImageRole.Cover;
                if (isMain) mainSeen = true;

                _db.EntityImages.Add(new EntityImage
                {
                    TargetType = EntityImageTargetType.Place,
                    PlaceId = place.Id,
                    Url = img.Url,
                    AltText = string.Empty,
                    Caption = string.Empty,
                    IsMain = isMain,
                    ImageRole = role,
                    SortOrder = img.SortOrder,
                    ImagePosition = img.ImagePosition ?? "50% 50%",
                    ImageSize = img.ImageSize ?? "cover",
                    CreatedAt = now,
                    UpdatedAt = now,
                });
            }

            changedFields.Add(nameof(request.Images));
        }

        place.UpdatedAt = now;

        if (changedFields.Count > 0)
        {
            await _auditService.RecordAsync(
                userId,
                "Place",
                place.Id,
                "OwnerUpdatedPlace",
                place.Status,
                place.Status,
                sourceArea: "OwnerDirectUpdate",
                metadataJson: JsonSerializer.Serialize(new
                {
                    ownerUserId = userId,
                    changedFields = changedFields.Distinct(StringComparer.Ordinal).ToList()
                }));
        }

        await _db.SaveChangesAsync();

        var reviewStats = await GetPlaceReviewStatsAsync(placeId);
        var previewReviews = await GetPlaceReviewPreviewAsync(placeId);
        var refreshedPlace = await _db.Places
            .AsNoTracking()
            .Include(p => p.PlaceType)
            .Include(p => p.Location)
                .ThenInclude(l => l.Destination)
            .Include(p => p.Images)
            .Include(p => p.Stay)
                .ThenInclude(stay => stay!.StayAmenities)
                    .ThenInclude(stayAmenity => stayAmenity.StayAmenity)
            .Include(p => p.PlaceTags)
                .ThenInclude(pt => pt.Tag)
            .Include(p => p.PlaceAmenities)
                .ThenInclude(placeAmenity => placeAmenity.Amenity)
            .Include(p => p.PlaceBookingPartners)
                .ThenInclude(placeBookingPartner => placeBookingPartner.BookingPartner)
            .AsSplitQuery()
            .SingleAsync(p => p.Id == placeId);

        var images = ToImageDtos(refreshedPlace.Images);
        var ownerUpdateAmenities = refreshedPlace.Stay is not null
            ? refreshedPlace.Stay.StayAmenities
                .Select(stayAmenity => new AmenityDto
                {
                    Id = stayAmenity.StayAmenityId,
                    Label = DisplayTextSafety.CleanRequired(stayAmenity.StayAmenity.Name, "Amenity"),
                    Icon = DisplayTextSafety.CleanOptional(stayAmenity.StayAmenity.IconKey) ?? string.Empty
                })
                .ToList()
            : refreshedPlace.PlaceAmenities
                .Select(placeAmenity => new AmenityDto
                {
                    Id = placeAmenity.AmenityId,
                    Label = DisplayTextSafety.CleanRequired(placeAmenity.Amenity.Name, "Amenity"),
                    Icon = DisplayTextSafety.CleanOptional(placeAmenity.Amenity.Icon) ?? string.Empty
                })
                .ToList();

        return new PlaceDetailDto
        {
            Id = refreshedPlace.Id,
            Title = DisplayTextSafety.CleanRequired(refreshedPlace.Name, "Place"),
            Location = DisplayTextSafety.CleanOptional(refreshedPlace.Location?.Name),
            LocationId = refreshedPlace.LocationId,
            LocationName = DisplayTextSafety.CleanOptional(refreshedPlace.Location?.Name),
            Latitude = HasUsableCoordinates(refreshedPlace.Latitude, refreshedPlace.Longitude) ? refreshedPlace.Latitude : null,
            Longitude = HasUsableCoordinates(refreshedPlace.Latitude, refreshedPlace.Longitude) ? refreshedPlace.Longitude : null,
            HeroImage = GetCoverImageUrl(refreshedPlace.Images) ?? images.FirstOrDefault()?.Url,
            Images = images,
            Tags = DisplayTextSafety.CleanLabels(refreshedPlace.PlaceTags.Select(pt => pt.Tag.Name))
                .Select(label => new TagLabelDto { Label = label })
                .ToList(),
            Rating = reviewStats.AverageRating,
            ReviewCount = FormatReviewCountLabel(reviewStats.Count),
            OpenStatus = DeriveOpenStatus(refreshedPlace.OperatingHoursEnabled, refreshedPlace.OperatingHoursJson),
            OpeningHours = DisplayTextSafety.CleanOptional(refreshedPlace.OpeningHours),
            OperatingHoursEnabled = refreshedPlace.OperatingHoursEnabled,
            OperatingHours = ParseStructuredOperatingHours(refreshedPlace.OperatingHoursJson),
            OpeningHoursSchedule = ParseOpeningHoursSchedule(refreshedPlace.OperatingHoursJson),
            Website = DisplayTextSafety.CleanOptional(refreshedPlace.Website),
            About = DisplayTextSafety.CleanOptionalRichMultiline(refreshedPlace.Description),
            Reviews = previewReviews
                .Select(r => new ReviewDto
                {
                    Id = r.Id,
                    AuthorUserId = r.UserId,
                    AuthorName = GetAuthorName(r.User),
                    AvatarUrl = r.User.AvatarUrl,
                    Rating = (int)r.Rating,
                    TimeAgo = FormatTimeAgo(r.CreatedAt),
                    Body = r.Comment,
                    HelpfulCount = 0,
                    ReportedByCurrentUser = false,
                    IsOwnReview = r.UserId == userId
                })
                .ToList(),
            RelatedEvents = new List<PlaceRelatedEventDto>(),
            Similar = new List<SimilarPlaceDto>(),
            IsSaved = false,
            IsOwnable = refreshedPlace.PlaceType?.IsOwnable ?? false,
            IsOwnedByCurrentUser = true,
            HasPendingOwnershipClaim = false,
            ContentType = refreshedPlace.Stay is not null ? "hotel" : "place",
            Category = DisplayTextSafety.CleanOptional(refreshedPlace.PlaceType?.Name)?.ToLowerInvariant(),
            ContactEmail = DisplayTextSafety.CleanOptional(refreshedPlace.ContactEmail),
            PhoneNumber = DisplayTextSafety.CleanOptional(refreshedPlace.Number),
            AdmissionPriceRange = DisplayTextSafety.CleanOptional(refreshedPlace.AdmissionPriceRange),
            BookingExternalLink = DisplayTextSafety.CleanOptional(refreshedPlace.BookingExternalLink),
            Amenities = ownerUpdateAmenities,
            BestTimeToVisit = DisplayTextSafety.CleanOptional(refreshedPlace.BestTimeToVisit),
            VisitorTips = DisplayTextSafety.CleanOptional(refreshedPlace.VisitorTips),
            AccessNotes = DisplayTextSafety.CleanOptional(refreshedPlace.AccessNotes),
            AccessibilityNotes = DisplayTextSafety.CleanOptional(refreshedPlace.AccessibilityNotes)
        };
    }

    private async Task SyncOwnerUpdateTagsAsync(int placeId, IEnumerable<string> tagNames, DateTime now)
    {
        var normalized = NormalizeOwnerUpdateLabels(tagNames, maxItems: 30);

        var existing = await _db.PlaceTags
            .Where(placeTag => placeTag.PlaceId == placeId)
            .ToListAsync();
        _db.PlaceTags.RemoveRange(existing);

        if (normalized.Count == 0)
            return;

        var keys = normalized.Select(SlugifyOwnerUpdateValue).ToList();
        var tags = await _db.Tags
            .Where(tag => tag.ForPlace &&
                (keys.Contains(tag.Slug.ToLower()) || normalized.Select(value => value.ToLower()).Contains(tag.Name.ToLower())))
            .ToListAsync();

        foreach (var label in normalized)
        {
            var slug = SlugifyOwnerUpdateValue(label);
            var tag = tags.FirstOrDefault(existingTag =>
                string.Equals(existingTag.Slug, slug, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(existingTag.Name, label, StringComparison.OrdinalIgnoreCase));

            if (tag is null)
            {
                tag = new Tag
                {
                    Name = label,
                    Slug = slug,
                    Description = string.Empty,
                    ForPlace = true,
                    CreatedAt = now,
                    UpdatedAt = now
                };
                _db.Tags.Add(tag);
            }

            _db.PlaceTags.Add(new PlaceTag { PlaceId = placeId, Tag = tag });
        }
    }

    private async Task SyncOwnerUpdateAmenitiesAsync(int placeId, IEnumerable<string> amenityNames)
    {
        var normalized = NormalizeOwnerUpdateLabels(amenityNames, maxItems: 30);

        var existing = await _db.PlaceAmenities
            .Where(placeAmenity => placeAmenity.PlaceId == placeId)
            .ToListAsync();
        _db.PlaceAmenities.RemoveRange(existing);

        if (normalized.Count == 0)
            return;

        var keys = normalized.Select(SlugifyOwnerUpdateValue).ToList();
        var amenities = await _db.Amenities
            .Where(amenity =>
                keys.Contains(amenity.Icon.ToLower()) ||
                normalized.Select(value => value.ToLower()).Contains(amenity.Name.ToLower()))
            .ToListAsync();

        foreach (var amenity in amenities.DistinctBy(amenity => amenity.Id))
        {
            _db.PlaceAmenities.Add(new PlaceAmenity
            {
                PlaceId = placeId,
                AmenityId = amenity.Id
            });
        }
    }

    private async Task SyncOwnerUpdateStayAmenitiesAsync(int placeId, IEnumerable<string> amenityNames)
    {
        var normalized = NormalizeOwnerUpdateLabels(amenityNames, maxItems: 30)
            .Select(SlugifyOwnerUpdateValue)
            .ToList();

        var existing = await _db.StayStayAmenities
            .Where(stayAmenity => stayAmenity.StayPlaceId == placeId)
            .ToListAsync();
        _db.StayStayAmenities.RemoveRange(existing);

        if (normalized.Count == 0)
            return;

        var amenities = await ResolveOwnerUpdateStayAmenitiesAsync(normalized);
        foreach (var amenity in amenities.DistinctBy(amenity => amenity.Id))
        {
            _db.StayStayAmenities.Add(new StayStayAmenity
            {
                StayPlaceId = placeId,
                StayAmenityId = amenity.Id
            });
        }
    }

    private async Task<List<StayAmenity>> ResolveOwnerUpdateStayAmenitiesAsync(List<string> normalized)
    {
        var alias = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["wifi"] = "free-wifi",
            ["pool"] = "swimming-pool",
            ["breakfast"] = "breakfast-included",
            ["gym"] = "fitness-center",
            ["ac"] = "air-conditioning",
            ["dining"] = "restaurant"
        };
        var slugs = normalized.Select(value => alias.GetValueOrDefault(value, value)).ToList();
        var knownDefinitions = new Dictionary<string, (string Name, string Icon, string Description)>(StringComparer.OrdinalIgnoreCase)
        {
            ["outdoor-seating"] = ("Outdoor Seating", "outdoor", "Outdoor seating or open-air areas"),
            ["sea-view"] = ("Sea View", "sea", "Sea view available"),
            ["family-friendly"] = ("Family Friendly", "family", "Family-friendly facilities or setting")
        };

        var amenities = await _db.StayAmenities
            .Where(amenity => slugs.Contains(amenity.Slug.ToLower()) ||
                              slugs.Contains(amenity.Name.ToLower()))
            .ToListAsync();

        var existingSlugs = amenities.Select(amenity => amenity.Slug).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var slug in slugs.Distinct(StringComparer.OrdinalIgnoreCase))
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
            amenities.Add(amenity);
        }

        if (amenities.Any(amenity => amenity.Id == 0))
            await _db.SaveChangesAsync();

        return amenities;
    }

    private async Task SyncOwnerUpdateBookingPartnersAsync(
        int placeId,
        IEnumerable<OwnerUpdateBookingPartnerDto> bookingPartners)
    {
        var existing = await _db.PlaceBookingPartners
            .Where(placeBookingPartner => placeBookingPartner.PlaceId == placeId)
            .ToListAsync();
        _db.PlaceBookingPartners.RemoveRange(existing);

        var normalized = new List<(string Id, string Url)>();
        foreach (var partner in bookingPartners)
        {
            var id = NormalizeOwnerUpdatePartnerId(partner.Id);
            if (id is null)
                continue;

            var url = NormalizeOwnerUpdateRequiredExternalHttpsUrl(partner.Url, 500, "Booking partner URL");
            normalized.Add((id, url));
        }

        normalized = normalized
            .DistinctBy(partner => partner.Id, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (normalized.Count > 10)
            throw new InvalidOperationException("A place can include at most 10 booking partners.");

        foreach (var partner in normalized)
        {
            var name = OwnerUpdatePartnerName(partner.Id);
            var bookingPartner = await _db.BookingPartners.FirstOrDefaultAsync(existingPartner =>
                existingPartner.Name.ToLower() == name.ToLower() ||
                existingPartner.Name.ToLower() == partner.Id);

            if (bookingPartner is null)
            {
                bookingPartner = new BookingPartner
                {
                    Name = name,
                    Variant = partner.Id == "airbnb" ? "brand" : "muted"
                };
                _db.BookingPartners.Add(bookingPartner);
                await _db.SaveChangesAsync();
            }

            _db.PlaceBookingPartners.Add(new PlaceBookingPartner
            {
                PlaceId = placeId,
                BookingPartnerId = bookingPartner.Id,
                Url = partner.Url
            });
        }
    }

    private static List<string> NormalizeOwnerUpdateLabels(IEnumerable<string> values, int maxItems)
    {
        var normalized = new List<string>();

        foreach (var value in values)
        {
            var item = NormalizeOwnerUpdateOptional(value);
            if (item is null)
                continue;

            if (item.Length > 80)
                throw new InvalidOperationException("Tags and amenities must be 80 characters or fewer.");

            if (!normalized.Contains(item, StringComparer.OrdinalIgnoreCase))
                normalized.Add(item);
        }

        if (normalized.Count > maxItems)
            throw new InvalidOperationException($"A place can include at most {maxItems} tags or amenities.");

        return normalized;
    }

    private static string? NormalizeOwnerUpdateOptional(string? value)
    {
        var normalized = value?.Trim();
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }

    private static string? NormalizeOwnerUpdateOptional(string? value, int maxLength, string tooLongMessage)
    {
        var normalized = NormalizeOwnerUpdateOptional(value);
        if (normalized is null)
            return null;

        if (normalized.Length > maxLength)
            throw new InvalidOperationException(tooLongMessage);

        return normalized;
    }

    private string NormalizeOwnerUpdateDescription(string? value)
    {
        var sanitized = _htmlSanitizer.SanitizeRichText(value);
        if (sanitized.Length > 2000)
            throw new InvalidOperationException("Description is too long.");

        return sanitized;
    }

    private static string NormalizeOwnerUpdateRequiredName(string? value, string fieldName, int maxLength)
    {
        var normalized = NormalizeOwnerUpdateOptional(value);
        if (normalized is null)
            throw new InvalidOperationException($"{fieldName} is required.");

        if (normalized.Length > maxLength)
            throw new InvalidOperationException($"{fieldName} must be {maxLength} characters or fewer.");

        return normalized;
    }

    private static string? NormalizeOwnerUpdateWebsite(string? value) =>
        NormalizeOwnerUpdateExternalHttpsUrl(value, 500, "Website URL");

    private static string? NormalizeOwnerUpdateContactEmail(string? value)
    {
        var normalized = NormalizeOwnerUpdateOptional(value);
        if (normalized is null)
            return null;

        if (normalized.Length > 200)
            throw new InvalidOperationException("Contact email is too long.");

        if (!Regex.IsMatch(normalized, @"^[^@\s]+@[^@\s]+\.[^@\s]+$"))
            throw new InvalidOperationException("Contact email is invalid.");

        return normalized.ToLowerInvariant();
    }

    private static string? NormalizeOwnerUpdatePhone(string? value) =>
        NormalizeOwnerUpdateOptional(value, 50, "Phone number is too long.");

    private static string? NormalizeOwnerUpdatePartnerId(string? value)
    {
        var normalized = NormalizeOwnerUpdateOptional(value)?.ToLowerInvariant();
        if (normalized is null)
            return null;

        if (normalized.Length > 80)
            throw new InvalidOperationException("Booking partner name must be 80 characters or fewer.");

        return normalized;
    }

    private static string NormalizeOwnerUpdateRequiredExternalHttpsUrl(string? value, int maxLength, string fieldName)
    {
        var normalized = NormalizeOwnerUpdateExternalHttpsUrl(value, maxLength, fieldName);
        if (normalized is null)
            throw new InvalidOperationException($"{fieldName} is required.");

        return normalized;
    }

    private static string? NormalizeOwnerUpdateExternalHttpsUrl(string? value, int maxLength, string fieldName)
    {
        var normalized = NormalizeOwnerUpdateOptional(value);
        if (normalized is null)
            return null;

        if (normalized.Length > maxLength)
            throw new InvalidOperationException($"{fieldName} must be {maxLength} characters or fewer.");

        if (normalized.StartsWith("//", StringComparison.Ordinal))
            throw new InvalidOperationException($"{fieldName} must be a valid https URL.");

        if (!Uri.TryCreate(normalized, UriKind.Absolute, out var uri) ||
            !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(uri.Host) ||
            uri.HostNameType == UriHostNameType.Unknown ||
            !string.IsNullOrEmpty(uri.UserInfo))
        {
            throw new InvalidOperationException($"{fieldName} must be a valid https URL.");
        }

        var absolute = uri.AbsoluteUri;
        if (absolute.Length > maxLength)
            throw new InvalidOperationException($"{fieldName} must be {maxLength} characters or fewer.");

        return absolute;
    }

    private static string SlugifyOwnerUpdateValue(string value)
    {
        var chars = value
            .Trim()
            .ToLowerInvariant()
            .Select(ch => char.IsLetterOrDigit(ch) ? ch : '-')
            .ToArray();

        var slug = string.Join(string.Empty, chars)
            .Split('-', StringSplitOptions.RemoveEmptyEntries);

        return string.Join("-", slug);
    }

    private static string OwnerUpdatePartnerName(string id) => id switch
    {
        "booking" => "Booking.com",
        "airbnb" => "Airbnb",
        "hotel" => "Official Website",
        "expedia" => "Expedia",
        _ => string.Join(" ", id.Split('-', '_', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => char.ToUpperInvariant(part[0]) + part[1..]))
    };

    private static List<BookingPartnerDto> BuildHotelBookingPartners(Place place)
    {
        var partners = place.PlaceBookingPartners
            .Select(pb => new BookingPartnerDto
            {
                Id = pb.BookingPartner.Id,
                Name = NormalizeHotelBookingPartnerName(pb.BookingPartner.Name),
                Variant = pb.BookingPartner.Variant,
                Url = pb.Url
            })
            .ToList();

        var website = DisplayTextSafety.CleanOptional(place.Website);
        if (!string.IsNullOrWhiteSpace(website) &&
            !partners.Any(partner =>
                IsOfficialWebsitePartner(partner.Name) ||
                string.Equals(partner.Url, website, StringComparison.OrdinalIgnoreCase)))
        {
            partners.Add(new BookingPartnerDto
            {
                Id = 0,
                Name = "Official Website",
                Variant = "muted",
                Url = website
            });
        }

        return partners;
    }

    private static string NormalizeHotelBookingPartnerName(string name) =>
        IsOfficialWebsitePartner(name) ? "Official Website" : name;

    private static bool IsOfficialWebsitePartner(string? name)
    {
        var normalized = (name ?? string.Empty).Trim().ToLowerInvariant();
        return normalized is "hotel website" or "official website";
    }

    private sealed class PlaceListProjection
    {
        public int Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public string? Subtitle { get; set; }
        public string Description { get; set; } = string.Empty;
        public string? Location { get; set; }
        public string? Image { get; set; }
        public double? Rating { get; set; }
        public int ReviewCount { get; set; }
        public string? OpeningHours { get; set; }
        public decimal? PricePerNight { get; set; }
        public int DestinationPriority { get; set; }
        public double? DistanceMeters { get; set; }
        public List<string> Tags { get; set; } = new();
    }

    private sealed record OwnerUpdateOperatingHoursResult(
        bool Enabled,
        string? Json,
        IReadOnlyList<OwnerUpdateOperatingHourDto> Hours);
}
