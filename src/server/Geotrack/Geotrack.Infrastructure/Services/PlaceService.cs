using Geotrack.Application.DTOs.Places;
using Geotrack.Application.DTOs.Reviews;
using Geotrack.Application.Interfaces.Service;
using Geotrack.Infrastructure;
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

    public async Task<List<PlaceListItemDto>> GetPlacesAsync(int destinationId, string? type = null, string? tags = null, string? category = null, int? userId = null)
    {
        var query = _db.Places
            .Include(p => p.PlaceType)
            .Include(p => p.Location)
            .Include(p => p.PlaceTags).ThenInclude(pt => pt.Tag)
            .Include(p => p.PlaceReviews)
            .Where(p => p.Location.DestinationId == destinationId);

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
        return items;
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
                Rating = r.Rating,
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
                Rating = r.Rating,
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

    public async Task<List<PlaceListItemDto>> GetHotelsAsync(int destinationId, string? tag = null, int? userId = null)
    {
        var query = _db.Places
            .Include(p => p.PlaceType)
            .Include(p => p.Location)
            .Include(p => p.PlaceTags).ThenInclude(pt => pt.Tag)
            .Include(p => p.PlaceReviews)
            .Where(p => p.Location.DestinationId == destinationId && p.PricePerNight != null);

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
        return items;
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
