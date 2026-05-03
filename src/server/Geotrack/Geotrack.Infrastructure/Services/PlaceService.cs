using Geotrack.Application.DTOs.Destinations;
using Geotrack.Application.DTOs.Places;
using Geotrack.Application.DTOs.Reviews;
using Geotrack.Application.Interfaces.Service;
using Geotrack.Application.Services;
using Geotrack.Domain.Entities.Core;
using Geotrack.Domain.Entities.Reviews;
using Geotrack.Infrastructure.Spatial;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;

namespace Geotrack.Infrastructure.Services;

public class PlaceService : IPlaceService
{
    private const int ListingMaxTake = 50;

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
            .Where(d => d.Id == destinationId.Value)
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
        double? longitude = null)
    {
        var place = await _db.Places
            .Include(p => p.PlaceType)
            .Include(p => p.Location)
            .Include(p => p.Images)
            .Include(p => p.PlaceTags)
                .ThenInclude(pt => pt.Tag)
            .Include(p => p.PlaceReviews)
                .ThenInclude(r => r.User)
            .FirstOrDefaultAsync(p => p.Id == id);

        if (place is null)
        {
            return null;
        }

        var isSaved = userId.HasValue &&
            await _favoriteService.IsFavoriteAsync(userId.Value, "place", id);

        var similar = await _db.Places
            .AsNoTracking()
            .Include(p => p.PlaceReviews)
            .Include(p => p.Images)
            .Where(p =>
                p.Id != id &&
                p.Stay == null &&
                p.PlaceTypeId == place.PlaceTypeId &&
                p.Location.DestinationId == place.Location.DestinationId)
            .Take(5)
            .ToListAsync();

        var favoriteIds = userId.HasValue
            ? await _favoriteService.GetFavoriteIdsAsync(userId.Value, "place")
            : new HashSet<int>();

        var reviewCount = place.PlaceReviews.Count;
        var averageRating = CalculateAverageRating(place.PlaceReviews);

        var reportedReviewIds = await GetReportedReviewIdsAsync(
            userId,
            ReviewReportTargetType.Place,
            place.PlaceReviews.Select(r => r.Id));

        var detailDistance = SpatialDistanceFormatter.FormatBetween(
            latitude,
            longitude,
            place.Latitude,
            place.Longitude);

        return new PlaceDetailDto
        {
            Id = place.Id,
            Title = DisplayTextSafety.CleanRequired(place.Name, "Place"),
            Location = DisplayTextSafety.CleanOptional(place.Location?.Name),
            Latitude = HasUsableCoordinates(place.Latitude, place.Longitude) ? place.Latitude : null,
            Longitude = HasUsableCoordinates(place.Latitude, place.Longitude) ? place.Longitude : null,
            HeroImage = GetMainImageUrl(place.Images),
            Tags = DisplayTextSafety.CleanLabels(place.PlaceTags.Select(pt => pt.Tag.Name))
                .Select(label => new TagLabelDto { Label = label })
                .ToList(),
            Rating = averageRating,
            ReviewCount = FormatReviewCountLabel(reviewCount),
            Distance = detailDistance,
            OpenStatus = DeriveOpenStatus(place.OpeningHours),
            About = DisplayTextSafety.CleanOptional(place.Description),
            Reviews = place.PlaceReviews
                .OrderByDescending(r => r.CreatedAt)
                .Take(10)
                .Select(r => new ReviewDto
                {
                    Id = r.Id,
                    AuthorName = GetAuthorName(r.User),
                    AvatarUrl = r.User.AvatarUrl,
                    Rating = (int)r.Rating,
                    TimeAgo = FormatTimeAgo(r.CreatedAt),
                    Body = r.Comment,
                    HelpfulCount = 0,
                    ReportedByCurrentUser = reportedReviewIds.Contains(r.Id)
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
                        latitude,
                        longitude,
                        s.Latitude,
                        s.Longitude),
                    IsSaved = favoriteIds.Contains(s.Id)
                })
                .ToList(),
            IsSaved = isSaved,
            ContentType = "place",
            Category = DisplayTextSafety.CleanOptional(place.PlaceType?.Name)?.ToLowerInvariant()
        };
    }

    public async Task<HotelDetailDto?> GetHotelDetailAsync(
        int id,
        int? userId = null,
        double? latitude = null,
        double? longitude = null)
    {
        var place = await _db.Places
            .Include(p => p.PlaceType)
            .Include(p => p.Location)
            .Include(p => p.Images)
            .Include(p => p.Stay)
                .ThenInclude(s => s!.StayType)
            .Include(p => p.Stay)
                .ThenInclude(s => s!.StayAmenities)
                    .ThenInclude(ssa => ssa.StayAmenity)
            .Include(p => p.PlaceTags)
                .ThenInclude(pt => pt.Tag)
            .Include(p => p.PlaceReviews)
                .ThenInclude(r => r.User)
            .Include(p => p.PlaceBookingPartners)
                .ThenInclude(pb => pb.BookingPartner)
            .FirstOrDefaultAsync(p => p.Id == id && p.Stay != null);

        if (place is null)
        {
            return null;
        }

        var isSaved = userId.HasValue &&
            await _favoriteService.IsFavoriteAsync(userId.Value, "place", id);

        var similar = await _db.Places
            .AsNoTracking()
            .Include(p => p.PlaceReviews)
            .Include(p => p.Images)
            .Where(p =>
                p.Id != id &&
                p.Stay != null &&
                p.Location.DestinationId == place.Location.DestinationId)
            .Take(5)
            .ToListAsync();

        var favoriteIds = userId.HasValue
            ? await _favoriteService.GetFavoriteIdsAsync(userId.Value, "place")
            : new HashSet<int>();

        var reviewCount = place.PlaceReviews.Count;
        var averageRating = CalculateAverageRating(place.PlaceReviews);

        var reportedReviewIds = await GetReportedReviewIdsAsync(
            userId,
            ReviewReportTargetType.Place,
            place.PlaceReviews.Select(r => r.Id));

        var detailDistance = SpatialDistanceFormatter.FormatBetween(
            latitude,
            longitude,
            place.Latitude,
            place.Longitude);

        var stayAmenities = place.Stay?.StayAmenities
            .Select(sa => new AmenityDto
            {
                Id = sa.StayAmenity.Id,
                Label = sa.StayAmenity.Name,
                Icon = sa.StayAmenity.IconKey ?? string.Empty
            }) ?? Enumerable.Empty<AmenityDto>();

        return new HotelDetailDto
        {
            Id = place.Id,
            Title = DisplayTextSafety.CleanRequired(place.Name, "Stay"),
            Location = DisplayTextSafety.CleanOptional(place.Location?.Name),
            Latitude = HasUsableCoordinates(place.Latitude, place.Longitude) ? place.Latitude : null,
            Longitude = HasUsableCoordinates(place.Latitude, place.Longitude) ? place.Longitude : null,
            HeroImage = GetMainImageUrl(place.Images),
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
            Reviews = place.PlaceReviews
                .OrderByDescending(r => r.CreatedAt)
                .Take(10)
                .Select(r => new ReviewDto
                {
                    Id = r.Id,
                    AuthorName = GetAuthorName(r.User),
                    AvatarUrl = r.User.AvatarUrl,
                    Rating = (int)r.Rating,
                    TimeAgo = FormatTimeAgo(r.CreatedAt),
                    Body = r.Comment,
                    HelpfulCount = 0,
                    ReportedByCurrentUser = reportedReviewIds.Contains(r.Id)
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
                        latitude,
                        longitude,
                        s.Latitude,
                        s.Longitude),
                    IsSaved = favoriteIds.Contains(s.Id)
                })
                .ToList(),
            IsSaved = isSaved
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
            .Where(d => d.Id == destinationId.Value)
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
            .FirstOrDefaultAsync(p => p.Id == placeId)
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
                .OrderBy(e => e.StartDate)
                .Select(e => new PlaceEventItemDto
                {
                    Id = e.Id,
                    Name = e.Name,
                    LocationId = e.LocationId,
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
        var exists = await _db.Places.AnyAsync(p => p.Id == placeId);
        if (!exists)
        {
            throw new KeyNotFoundException("Place not found");
        }

        var events = await _db.Events
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
                    LocationId = e.LocationId,
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

    public async Task<GetPlaceReviewsResponseDto> GetPlaceReviewsAsync(int placeId)
    {
        var exists = await _db.Places.AnyAsync(p => p.Id == placeId);
        if (!exists)
        {
            throw new KeyNotFoundException("Place not found");
        }

        var reviews = await _db.PlaceReviews
            .Where(r => r.PlaceId == placeId)
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync();

        var averageRating = reviews.Count > 0 ? Math.Round(reviews.Average(r => r.Rating), 2) : 0;

        return new GetPlaceReviewsResponseDto
        {
            PlaceId = placeId,
            AverageRating = averageRating,
            ReviewsCount = reviews.Count,
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

        var exists = await _db.Places.AnyAsync(p => p.Id == placeId);
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
                        .OrderByDescending(image => image.IsMain)
                        .ThenBy(image => image.SortOrder)
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
                        .OrderByDescending(image => image.IsMain)
                        .ThenBy(image => image.SortOrder)
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

    private static string? GetMainImageUrl(IEnumerable<EntityImage> images)
    {
        return images
            .OrderByDescending(image => image.IsMain)
            .ThenBy(image => image.SortOrder)
            .Select(image => image.Url)
            .FirstOrDefault();
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

    private static bool HasUsableCoordinates(double latitude, double longitude) =>
        !double.IsNaN(latitude) &&
        !double.IsNaN(longitude) &&
        !double.IsInfinity(latitude) &&
        !double.IsInfinity(longitude) &&
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
