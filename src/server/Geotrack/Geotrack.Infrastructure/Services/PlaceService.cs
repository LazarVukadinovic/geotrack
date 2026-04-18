using Geotrack.Application.DTOs.Destinations;
using Geotrack.Application.DTOs.Places;
using Geotrack.Application.DTOs.Reviews;
using Geotrack.Application.Interfaces.Service;
using Geotrack.Domain.Entities.Reviews;
using Microsoft.EntityFrameworkCore;

namespace Geotrack.Infrastructure.Services;

public class PlaceService : IPlaceService
{
    private readonly AppDbContext _db;
    private readonly IFavoriteService _favoriteService;

    public PlaceService(AppDbContext db, IFavoriteService favoriteService)
    {
        _db = db;
        _favoriteService = favoriteService;
    }

    // --- Frontend-facing methods ---

    public async Task<DestinationScopedResult<List<PlaceListItemDto>>> GetPlacesAsync(int? destinationId, string? type = null, string? tags = null, string? category = null, int? userId = null)
    {
        if (!destinationId.HasValue || destinationId.Value <= 0)
        {
            return new DestinationScopedResult<List<PlaceListItemDto>> { Status = DestinationScopedStatus.InvalidDestination };
        }
        if (!await _db.Destinations.AnyAsync(d => d.Id == destinationId.Value))
        {
            return new DestinationScopedResult<List<PlaceListItemDto>> { Status = DestinationScopedStatus.DestinationNotFound };
        }

        var query = _db.Places
            .Include(p => p.PlaceType)
            .Include(p => p.Location)
            .Include(p => p.PlaceTags).ThenInclude(pt => pt.Tag)
            .Include(p => p.PlaceReviews)
            .Where(p => p.Location.DestinationId == destinationId.Value);

        if (!string.IsNullOrEmpty(type))
            query = query.Where(p => p.PlaceType.Name.ToLower() == type.ToLower());

        if (!string.IsNullOrEmpty(tags) && tags.ToLower() != "all")
        {
            var tagList = tags.Split(',').Select(t => t.Trim().ToLower()).ToList();
            query = query.Where(p => p.PlaceTags.Any(pt => tagList.Contains(pt.Tag.Slug.ToLower()) || tagList.Contains(pt.Tag.Name.ToLower())));
        }

        if (!string.IsNullOrEmpty(category))
        {
            query = query.Where(p =>
                p.PlaceTags.Any(pt => pt.Tag.Slug.ToLower() == category.ToLower()) ||
                p.PlaceType.Name.ToLower() == category.ToLower());
        }

        // Exclude hotels from generic place listings
        if (string.IsNullOrEmpty(type))
            query = query.Where(p => p.PricePerNight == null);

        var places = await query.OrderByDescending(p => p.AverageRating ?? 0).ToListAsync();

        var favoriteIds = userId.HasValue
            ? await _favoriteService.GetFavoriteIdsAsync(userId.Value, "place")
            : new HashSet<int>();

        var items = new List<PlaceListItemDto>();
        for (int i = 0; i < places.Count; i++)
        {
            var p = places[i];
            items.Add(new PlaceListItemDto
            {
                Id = p.Id,
                Title = p.Name,
                Subtitle = p.Subtitle,
                Description = p.Description.Length > 150 ? p.Description[..150] + "..." : p.Description,
                Location = p.Location?.Name,
                Image = p.ImageUrl,
                Rating = p.AverageRating,
                ReviewCount = p.ReviewCount > 0 ? $"{p.ReviewCount} reviews" : null,
                OpeningHours = p.OpeningHours,
                Distance = null,
                PricePerNight = p.PricePerNight,
                Tags = p.PlaceTags.Select(pt => pt.Tag.Name).ToList(),
                Variant = i == 0 ? "large" : "compact",
                IsSaved = favoriteIds.Contains(p.Id)
            });
        }
        return new DestinationScopedResult<List<PlaceListItemDto>> { Status = DestinationScopedStatus.Success, Data = items };
    }

    public async Task<PlaceDetailDto?> GetPlaceDetailAsync(int id, int? userId = null)
    {
        var place = await _db.Places
            .Include(p => p.PlaceType)
            .Include(p => p.Location)
            .Include(p => p.PlaceTags).ThenInclude(pt => pt.Tag)
            .Include(p => p.PlaceReviews).ThenInclude(r => r.User)
            .FirstOrDefaultAsync(p => p.Id == id);

        if (place == null) return null;

        var isSaved = userId.HasValue && await _favoriteService.IsFavoriteAsync(userId.Value, "place", id);

        var similar = await _db.Places
            .Include(p => p.PlaceReviews)
            .Where(p => p.Id != id && p.PlaceTypeId == place.PlaceTypeId && p.Location.DestinationId == place.Location.DestinationId)
            .Take(5)
            .ToListAsync();

        var favoriteIds = userId.HasValue
            ? await _favoriteService.GetFavoriteIdsAsync(userId.Value, "place")
            : new HashSet<int>();

        return new PlaceDetailDto
        {
            Id = place.Id,
            Title = place.Name,
            Location = place.Location?.Name,
            HeroImage = place.ImageUrl,
            Tags = place.PlaceTags.Select(pt => new TagLabelDto { Label = pt.Tag.Name }).ToList(),
            Rating = place.AverageRating ?? 0,
            ReviewCount = FormatReviewCount(place.ReviewCount),
            Distance = null,
            OpenStatus = DeriveOpenStatus(place.OpeningHours),
            About = place.Description,
            Reviews = place.PlaceReviews.OrderByDescending(r => r.CreatedAt).Take(10).Select(r => new ReviewDto
            {
                Id = r.Id,
                AuthorName = GetAuthorName(r.User),
                AvatarUrl = r.User.AvatarUrl,
                Rating = (int)r.Rating,
                TimeAgo = FormatTimeAgo(r.CreatedAt),
                Body = r.Comment,
                HelpfulCount = 0
            }).ToList(),
            Similar = similar.Select(s => new SimilarPlaceDto
            {
                Id = s.Id,
                Title = s.Name,
                Image = s.ImageUrl,
                Rating = s.AverageRating ?? 0,
                Distance = null,
                IsSaved = favoriteIds.Contains(s.Id)
            }).ToList(),
            IsSaved = isSaved,
            ContentType = "place",
            Category = place.PlaceType?.Name?.ToLower()
        };
    }

    public async Task<HotelDetailDto?> GetHotelDetailAsync(int id, int? userId = null)
    {
        var place = await _db.Places
            .Include(p => p.PlaceType)
            .Include(p => p.Location)
            .Include(p => p.PlaceTags).ThenInclude(pt => pt.Tag)
            .Include(p => p.PlaceReviews).ThenInclude(r => r.User)
            .Include(p => p.PlaceAmenities).ThenInclude(pa => pa.Amenity)
            .Include(p => p.PlaceBookingPartners).ThenInclude(pb => pb.BookingPartner)
            .FirstOrDefaultAsync(p => p.Id == id);

        if (place == null) return null;

        var isSaved = userId.HasValue && await _favoriteService.IsFavoriteAsync(userId.Value, "place", id);

        var similar = await _db.Places
            .Include(p => p.PlaceReviews)
            .Where(p => p.Id != id && p.PricePerNight != null && p.Location.DestinationId == place.Location.DestinationId)
            .Take(5)
            .ToListAsync();

        var favoriteIds = userId.HasValue
            ? await _favoriteService.GetFavoriteIdsAsync(userId.Value, "place")
            : new HashSet<int>();

        return new HotelDetailDto
        {
            Id = place.Id,
            Title = place.Name,
            Location = place.Location?.Name,
            HeroImage = place.ImageUrl,
            Tags = place.PlaceTags.Select(pt => new TagLabelDto { Label = pt.Tag.Name }).ToList(),
            Rating = place.AverageRating ?? 0,
            ReviewCount = FormatReviewCount(place.ReviewCount),
            Distance = null,
            PricePerNight = place.PricePerNight ?? 0,
            About = place.Description,
            BookingPartners = place.PlaceBookingPartners.Select(pb => new BookingPartnerDto
            {
                Id = pb.BookingPartner.Id,
                Name = pb.BookingPartner.Name,
                Variant = pb.BookingPartner.Variant
            }).ToList(),
            Amenities = place.PlaceAmenities.Select(pa => new AmenityDto
            {
                Id = pa.Amenity.Id,
                Label = pa.Amenity.Name,
                Icon = pa.Amenity.Icon
            }).ToList(),
            Reviews = place.PlaceReviews.OrderByDescending(r => r.CreatedAt).Take(10).Select(r => new ReviewDto
            {
                Id = r.Id,
                AuthorName = GetAuthorName(r.User),
                AvatarUrl = r.User.AvatarUrl,
                Rating = (int)r.Rating,
                TimeAgo = FormatTimeAgo(r.CreatedAt),
                Body = r.Comment,
                HelpfulCount = 0
            }).ToList(),
            Similar = similar.Select(s => new SimilarPlaceDto
            {
                Id = s.Id,
                Title = s.Name,
                Image = s.ImageUrl,
                Rating = s.AverageRating ?? 0,
                Distance = null,
                IsSaved = favoriteIds.Contains(s.Id)
            }).ToList(),
            IsSaved = isSaved
        };
    }

    public async Task<DestinationScopedResult<List<PlaceListItemDto>>> GetHotelsAsync(int? destinationId, string? tag = null, int? userId = null)
    {
        if (!destinationId.HasValue || destinationId.Value <= 0)
        {
            return new DestinationScopedResult<List<PlaceListItemDto>> { Status = DestinationScopedStatus.InvalidDestination };
        }
        if (!await _db.Destinations.AnyAsync(d => d.Id == destinationId.Value))
        {
            return new DestinationScopedResult<List<PlaceListItemDto>> { Status = DestinationScopedStatus.DestinationNotFound };
        }

        var query = _db.Places
            .Include(p => p.PlaceType)
            .Include(p => p.Location)
            .Include(p => p.PlaceTags).ThenInclude(pt => pt.Tag)
            .Include(p => p.PlaceReviews)
            .Where(p => p.Location.DestinationId == destinationId.Value && p.PricePerNight != null);

        if (!string.IsNullOrEmpty(tag) && tag.ToLower() != "all")
            query = query.Where(p => p.PlaceTags.Any(pt => pt.Tag.Name.ToLower() == tag.ToLower()));

        var places = await query.OrderByDescending(p => p.AverageRating ?? 0).ToListAsync();

        var favoriteIds = userId.HasValue
            ? await _favoriteService.GetFavoriteIdsAsync(userId.Value, "place")
            : new HashSet<int>();

        var items = new List<PlaceListItemDto>();
        for (int i = 0; i < places.Count; i++)
        {
            var p = places[i];
            items.Add(new PlaceListItemDto
            {
                Id = p.Id,
                Title = p.Name,
                Subtitle = p.Subtitle,
                Description = p.Description.Length > 150 ? p.Description[..150] + "..." : p.Description,
                Location = p.Location?.Name,
                Image = p.ImageUrl,
                Rating = p.AverageRating,
                ReviewCount = p.ReviewCount > 0 ? $"{p.ReviewCount} reviews" : null,
                Distance = null,
                PricePerNight = p.PricePerNight,
                Tags = p.PlaceTags.Select(pt => pt.Tag.Name).ToList(),
                Variant = i == 0 ? "large" : "compact",
                IsSaved = favoriteIds.Contains(p.Id)
            });
        }
        return new DestinationScopedResult<List<PlaceListItemDto>> { Status = DestinationScopedStatus.Success, Data = items };
    }

    // --- Master detail/review methods ---

    public async Task<GetPlaceDetailsResponseDto> GetPlaceDetailsAsync(int placeId, int? userId)
    {
        var place = await _db.Places
            .Include(p => p.PlaceReviews)
            .Include(p => p.Events)
            .FirstOrDefaultAsync(p => p.Id == placeId)
            ?? throw new KeyNotFoundException("Place not found");

        var reviews = place.PlaceReviews.ToList();
        var averageRating = reviews.Any() ? Math.Round(reviews.Average(r => r.Rating), 2) : 0;

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
            Events = place.Events.OrderBy(e => e.StartDate).Select(e => new PlaceEventItemDto
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
            }).ToList()
        };
    }

    public async Task<GetPlaceEventsResponseDto> GetPlaceEventsAsync(int placeId)
    {
        var place = await _db.Places.FindAsync(placeId);
        if (place == null)
            throw new KeyNotFoundException("Place not found");

        var events = await _db.Events
            .Where(e => e.PlaceId == placeId)
            .OrderBy(e => e.StartDate)
            .ToListAsync();

        return new GetPlaceEventsResponseDto
        {
            PlaceId = placeId,
            Events = events.Select(e => new PlaceEventItemDto
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
            }).ToList()
        };
    }

    public async Task<GetPlaceReviewsResponseDto> GetPlaceReviewsAsync(int placeId)
    {
        var place = await _db.Places.FindAsync(placeId);
        if (place == null)
            throw new KeyNotFoundException("Place not found");

        var reviews = await _db.PlaceReviews
            .Where(r => r.PlaceId == placeId)
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync();

        var averageRating = reviews.Any() ? Math.Round(reviews.Average(r => r.Rating), 2) : 0;

        return new GetPlaceReviewsResponseDto
        {
            PlaceId = placeId,
            AverageRating = averageRating,
            ReviewsCount = reviews.Count,
            Reviews = reviews.Select(r => new PlaceReviewItemDto
            {
                Id = r.Id,
                UserId = r.UserId,
                Rating = r.Rating,
                Comment = r.Comment,
                CreatedAt = r.CreatedAt,
                UpdatedAt = r.UpdatedAt
            }).ToList()
        };
    }

    public async Task<CreatePlaceReviewResponseDto> CreatePlaceReviewAsync(int placeId, int userId, CreatePlaceReviewRequestDto request)
    {
        if (request.Rating < 1 || request.Rating > 5)
            throw new InvalidOperationException("Rating must be between 1 and 5");

        if ((request.Comment ?? string.Empty).Length > 1000)
            throw new InvalidOperationException("Comment is too long");

        var place = await _db.Places.FindAsync(placeId);
        if (place == null)
            throw new KeyNotFoundException("Place not found");

        var alreadyReviewed = await _db.PlaceReviews.AnyAsync(r => r.PlaceId == placeId && r.UserId == userId);
        if (alreadyReviewed)
            throw new InvalidOperationException("User has already reviewed this place");

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

    private static string GetAuthorName(Domain.Entities.Users.User user)
    {
        if (!string.IsNullOrEmpty(user.FirstName))
            return $"{user.FirstName} {user.LastName}".Trim();
        return user.Username;
    }

    private static string FormatReviewCount(int count) => count switch
    {
        0 => "No reviews",
        1 => "1 review",
        _ => $"{count} reviews"
    };

    private static string? DeriveOpenStatus(string? hours) =>
        string.IsNullOrEmpty(hours) ? null : "Open";

    private static string FormatTimeAgo(DateTime date)
    {
        var diff = DateTime.UtcNow - date;
        if (diff.TotalMinutes < 60) return $"{(int)diff.TotalMinutes}m ago";
        if (diff.TotalHours < 24) return $"{(int)diff.TotalHours}h ago";
        if (diff.TotalDays < 30) return $"{(int)diff.TotalDays}d ago";
        if (diff.TotalDays < 365) return $"{(int)(diff.TotalDays / 30)}mo ago";
        return $"{(int)(diff.TotalDays / 365)}y ago";
    }
}
