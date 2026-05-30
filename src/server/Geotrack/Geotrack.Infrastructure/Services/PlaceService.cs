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

namespace Geotrack.Infrastructure.Services;

public class PlaceService : IPlaceService
{
    private const int ListingMaxTake = 50;
    private const string PublishedStatus = "Published";

    private readonly AppDbContext _db;
    private readonly IFavoriteService _favoriteService;
    private readonly IReviewContentModerationService _contentModeration;

    public PlaceService(
        AppDbContext db,
        IFavoriteService favoriteService,
        IReviewContentModerationService contentModeration)
    {
        _db = db;
        _favoriteService = favoriteService;
        _contentModeration = contentModeration;
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
            .Include(p => p.PlaceAmenities)
                .ThenInclude(placeAmenity => placeAmenity.Amenity)
            .Include(p => p.PlaceBookingPartners)
                .ThenInclude(placeBookingPartner => placeBookingPartner.BookingPartner)
            .FirstOrDefaultAsync(p =>
                p.Id == id &&
                p.Status == PublishedStatus &&
                p.Location.Status == PublishedStatus &&
                p.Location.Destination.Status == PublishedStatus);

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
                p.Stay == null &&
                p.PlaceTypeId == place.PlaceTypeId &&
                p.Location.DestinationId == place.Location.DestinationId)
            .Take(5)
            .ToListAsync();

        var relatedEvents = await _db.Events
            .AsNoTracking()
            .Include(e => e.Images)
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
            OpenStatus = DeriveOpenStatus(place.OpeningHours),
            OpeningHours = DisplayTextSafety.CleanOptional(place.OpeningHours),
            OpeningHoursSchedule = ParseOpeningHoursSchedule(place.OperatingHoursJson),
            Website = DisplayTextSafety.CleanOptional(place.Website),
            About = DisplayTextSafety.CleanOptional(place.Description),
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
            About = DisplayTextSafety.CleanOptional(place.Description),
            ContactEmail = place.ContactEmail,
            PhoneNumber = place.Number,
            BookingPartners = place.PlaceBookingPartners
                .Select(pb => new BookingPartnerDto
                {
                    Id = pb.BookingPartner.Id,
                    Name = pb.BookingPartner.Name,
                    Variant = pb.BookingPartner.Variant,
                    Url = pb.Url
                })
                .ToList(),
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
            .Include(p => p.PlaceReviews)
            .Include(p => p.Events)
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
        var exists = await _db.Places.AnyAsync(p =>
            p.Id == placeId &&
            p.Status == PublishedStatus &&
            p.Location.Status == PublishedStatus &&
            p.Location.Destination.Status == PublishedStatus);
        if (!exists)
        {
            throw new KeyNotFoundException("Place not found");
        }

        var events = await _db.Events
            .Where(e => e.PlaceId == placeId && e.Status == PublishedStatus)
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

    private static string? DeriveOpenStatus(string? hours) =>
        string.IsNullOrWhiteSpace(hours) ? null : "Open";

    private static List<OpeningHoursDayDto>? ParseOpeningHoursSchedule(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            return JsonSerializer.Deserialize<List<OpeningHoursDayDto>>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch
        {
            return null;
        }
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
            .Include(p => p.PlaceTags)
                .ThenInclude(pt => pt.Tag)
                    .ThenInclude(tag => tag.TagType)
                        .ThenInclude(tagType => tagType!.PlaceTypes)
            .FirstOrDefaultAsync(p =>
                p.Id == placeId &&
                p.Status == PublishedStatus &&
                p.Location.Status == PublishedStatus &&
                p.Location.Destination.Status == PublishedStatus)
            ?? throw new KeyNotFoundException("Place not found.");

        if (!string.IsNullOrWhiteSpace(request.Name))
            place.Name = request.Name.Trim();

        if (request.Description != null)
            place.Description = request.Description.Trim();

        if (request.OpeningHours != null)
            place.OpeningHours = string.IsNullOrWhiteSpace(request.OpeningHours) ? null : request.OpeningHours.Trim();

        if (request.Website != null)
            place.Website = string.IsNullOrWhiteSpace(request.Website) ? null : request.Website.Trim();

        if (request.ContactEmail != null)
            place.ContactEmail = string.IsNullOrWhiteSpace(request.ContactEmail) ? null : request.ContactEmail.Trim();

        if (request.PhoneNumber != null)
            place.Number = string.IsNullOrWhiteSpace(request.PhoneNumber) ? null : request.PhoneNumber.Trim();

        if (request.AdmissionPriceRange != null)
            place.AdmissionPriceRange = string.IsNullOrWhiteSpace(request.AdmissionPriceRange) ? null : request.AdmissionPriceRange.Trim();

        if (request.BookingExternalLink != null)
            place.BookingExternalLink = string.IsNullOrWhiteSpace(request.BookingExternalLink) ? null : request.BookingExternalLink.Trim();

        if (request.BestTimeToVisit != null)
            place.BestTimeToVisit = string.IsNullOrWhiteSpace(request.BestTimeToVisit) ? null : request.BestTimeToVisit.Trim();

        if (request.VisitorTips != null)
            place.VisitorTips = string.IsNullOrWhiteSpace(request.VisitorTips) ? null : request.VisitorTips.Trim();

        if (request.AccessNotes != null)
            place.AccessNotes = string.IsNullOrWhiteSpace(request.AccessNotes) ? null : request.AccessNotes.Trim();

        if (request.AccessibilityNotes != null)
            place.AccessibilityNotes = string.IsNullOrWhiteSpace(request.AccessibilityNotes) ? null : request.AccessibilityNotes.Trim();

        if (request.Tags is not null)
            await SyncOwnerUpdateTagsAsync(place.Id, request.Tags, now: DateTime.UtcNow);

        if (request.Amenities is not null)
            await SyncOwnerUpdateAmenitiesAsync(place.Id, request.Amenities);

        if (request.BookingPartners is not null)
            await SyncOwnerUpdateBookingPartnersAsync(place.Id, request.BookingPartners);

        place.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();

        var reviewStats = await GetPlaceReviewStatsAsync(placeId);
        var previewReviews = await GetPlaceReviewPreviewAsync(placeId);
        var images = ToImageDtos(place.Images);

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
            Rating = reviewStats.AverageRating,
            ReviewCount = FormatReviewCountLabel(reviewStats.Count),
            OpenStatus = DeriveOpenStatus(place.OpeningHours),
            OpeningHours = DisplayTextSafety.CleanOptional(place.OpeningHours),
            OpeningHoursSchedule = ParseOpeningHoursSchedule(place.OperatingHoursJson),
            Website = DisplayTextSafety.CleanOptional(place.Website),
            About = DisplayTextSafety.CleanOptional(place.Description),
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
            IsOwnable = place.PlaceType?.IsOwnable ?? false,
            IsOwnedByCurrentUser = true,
            HasPendingOwnershipClaim = false,
            ContentType = "place",
            Category = DisplayTextSafety.CleanOptional(place.PlaceType?.Name)?.ToLowerInvariant(),
            ContactEmail = DisplayTextSafety.CleanOptional(place.ContactEmail),
            PhoneNumber = DisplayTextSafety.CleanOptional(place.Number),
            AdmissionPriceRange = DisplayTextSafety.CleanOptional(place.AdmissionPriceRange),
            BookingExternalLink = DisplayTextSafety.CleanOptional(place.BookingExternalLink),
            Amenities = place.PlaceAmenities
                .Select(placeAmenity => new AmenityDto
                {
                    Id = placeAmenity.AmenityId,
                    Label = DisplayTextSafety.CleanRequired(placeAmenity.Amenity.Name, "Amenity"),
                    Icon = DisplayTextSafety.CleanOptional(placeAmenity.Amenity.Icon) ?? string.Empty
                })
                .ToList(),
            BestTimeToVisit = DisplayTextSafety.CleanOptional(place.BestTimeToVisit),
            VisitorTips = DisplayTextSafety.CleanOptional(place.VisitorTips),
            AccessNotes = DisplayTextSafety.CleanOptional(place.AccessNotes),
            AccessibilityNotes = DisplayTextSafety.CleanOptional(place.AccessibilityNotes)
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

    private async Task SyncOwnerUpdateBookingPartnersAsync(
        int placeId,
        IEnumerable<OwnerUpdateBookingPartnerDto> bookingPartners)
    {
        var existing = await _db.PlaceBookingPartners
            .Where(placeBookingPartner => placeBookingPartner.PlaceId == placeId)
            .ToListAsync();
        _db.PlaceBookingPartners.RemoveRange(existing);

        var normalized = bookingPartners
            .Select(partner => new
            {
                Id = NormalizeOwnerUpdateOptional(partner.Id)?.ToLowerInvariant(),
                Url = NormalizeOwnerUpdateOptional(partner.Url)
            })
            .Where(partner => partner.Id is not null)
            .DistinctBy(partner => partner.Id)
            .Take(10)
            .ToList();

        foreach (var partner in normalized)
        {
            var name = OwnerUpdatePartnerName(partner.Id!);
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
        return values
            .Select(NormalizeOwnerUpdateOptional)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(maxItems)
            .ToList();
    }

    private static string? NormalizeOwnerUpdateOptional(string? value)
    {
        var normalized = value?.Trim();
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
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
        "hotel" => "Hotel Website",
        "expedia" => "Expedia",
        _ => string.Join(" ", id.Split('-', '_', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => char.ToUpperInvariant(part[0]) + part[1..]))
    };

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
}
