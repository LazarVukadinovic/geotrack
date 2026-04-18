using Geotrack.Application.DTOs.Destinations;
using Geotrack.Application.DTOs.Explore;
using Geotrack.Application.Interfaces.Service;
using Geotrack.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Geotrack.Infrastructure.Services;

public class ExploreService : IExploreService
{
    private readonly AppDbContext _db;
    private readonly IFavoriteService _favoriteService;

    public ExploreService(AppDbContext db, IFavoriteService favoriteService)
    {
        _db = db;
        _favoriteService = favoriteService;
    }

    public async Task<ExplorePageDto> GetExplorePageAsync(double latitude, double longitude, int? userId = null)
    {
        // Find nearest destination
        var destinations = await _db.Destinations.ToListAsync();
        var nearest = destinations
            .Select(d => new { Destination = d, Distance = HaversineDistance(latitude, longitude, d.Latitude, d.Longitude) })
            .OrderBy(x => x.Distance)
            .FirstOrDefault();

        if (nearest == null)
            return new ExplorePageDto();

        var destId = nearest.Destination.Id;

        var placeFavIds = userId.HasValue ? await _favoriteService.GetFavoriteIdsAsync(userId.Value, "place") : new HashSet<int>();
        var eventFavIds = userId.HasValue ? await _favoriteService.GetFavoriteIdsAsync(userId.Value, "event") : new HashSet<int>();

        // Events this week
        var events = await _db.Events
            .Include(e => e.EventType)
            .Where(e => e.Location.DestinationId == destId && e.EndDate >= DateTime.UtcNow)
            .OrderBy(e => e.StartDate)
            .Take(5)
            .ToListAsync();

        // Top stays (hotels)
        var stays = await _db.Places
            .Include(p => p.PlaceReviews)
            .Where(p => p.Location.DestinationId == destId && p.PricePerNight != null)
            .OrderByDescending(p => p.AverageRating)
            .Take(5)
            .ToListAsync();

        // Popular nearby places
        var popular = await _db.Places
            .Include(p => p.PlaceType)
            .Include(p => p.PlaceTags).ThenInclude(pt => pt.Tag)
            .Where(p => p.Location.DestinationId == destId && p.PricePerNight == null)
            .OrderByDescending(p => p.AverageRating)
            .Take(6)
            .ToListAsync();

        // Must-see attractions
        var attractions = await _db.Places
            .Include(p => p.PlaceType)
            .Where(p => p.Location.DestinationId == destId && p.PricePerNight == null)
            .OrderByDescending(p => p.ReviewCount)
            .Take(6)
            .ToListAsync();

        // Landmark categories from PlaceTypes
        var categories = await _db.PlaceTypes
            .Where(pt => pt.Places.Any(p => p.Location.DestinationId == destId))
            .Take(8)
            .ToListAsync();

        // Local favorites (high-rated places)
        var favorites = await _db.Places
            .Include(p => p.PlaceType)
            .Include(p => p.Location)
            .Where(p => p.Location.DestinationId == destId && p.AverageRating >= 4.0)
            .OrderByDescending(p => p.AverageRating)
            .Take(4)
            .ToListAsync();

        // Curated for you (based on user tags if logged in)
        var curated = new List<ExploreCuratedItemDto>();
        if (userId.HasValue)
        {
            var userTagIds = await _db.UserTags
                .Where(ut => ut.UserId == userId.Value)
                .Select(ut => ut.TagId)
                .ToListAsync();

            var curatedPlaces = userTagIds.Any()
                ? await _db.Places
                    .Include(p => p.PlaceTags).ThenInclude(pt => pt.Tag)
                    .Where(p => p.Location.DestinationId == destId && p.PlaceTags.Any(pt => userTagIds.Contains(pt.TagId)))
                    .Take(5)
                    .ToListAsync()
                : await _db.Places
                    .Include(p => p.PlaceTags).ThenInclude(pt => pt.Tag)
                    .Where(p => p.Location.DestinationId == destId)
                    .OrderByDescending(p => p.AverageRating)
                    .Take(5)
                    .ToListAsync();

            curated = curatedPlaces.Select(p => new ExploreCuratedItemDto
            {
                Id = p.Id,
                Title = p.Name,
                Subtitle = p.Subtitle ?? (p.Description.Length > 60 ? p.Description[..60] + "..." : p.Description),
                CategoryLabel = p.PlaceTags.FirstOrDefault()?.Tag.Name,
                Image = p.ImageUrl,
                IsSaved = placeFavIds.Contains(p.Id)
            }).ToList();
        }

        return new ExplorePageDto
        {
            SelectedDestination = new ExploreDestinationDto
            {
                Id = nearest.Destination.Id,
                Name = nearest.Destination.Name,
                Latitude = nearest.Destination.Latitude,
                Longitude = nearest.Destination.Longitude
            },
            EventsThisWeek = events.Select(e => new ExploreEventDto
            {
                Id = e.Id,
                Title = e.Name,
                Subtitle = e.EventType?.Name,
                DateLabel = FormatDateLabel(e.StartDate, e.EndDate),
                Image = e.ImageUrl
            }).ToList(),
            TopStays = stays.Select(s => new ExploreStayDto
            {
                Id = s.Id,
                Title = s.Name,
                Image = s.ImageUrl,
                Rating = s.AverageRating ?? 0,
                ReviewCount = s.ReviewCount > 0 ? $"{s.ReviewCount} reviews" : null,
                Distance = null,
                PricePerNight = s.PricePerNight ?? 0,
                IsSaved = placeFavIds.Contains(s.Id)
            }).ToList(),
            PopularNearby = popular.Select(p => new ExploreNearbyItemDto
            {
                Id = p.Id,
                Title = p.Name,
                CategoryLabel = p.PlaceType?.Name,
                Image = p.ImageUrl,
                Rating = p.AverageRating ?? 0,
                ReviewCount = p.ReviewCount > 0 ? $"{p.ReviewCount}" : null,
                Distance = null,
                IsSaved = placeFavIds.Contains(p.Id)
            }).ToList(),
            MustSeeAttractions = attractions.Select(a => new ExploreAttractionDto
            {
                Id = a.Id,
                Title = a.Name,
                Type = a.PlaceType?.Name,
                Image = a.ImageUrl
            }).ToList(),
            LandmarkCategories = categories.Select(c => new ExploreLandmarkCategoryDto
            {
                Id = c.Id,
                Title = c.Name,
                Image = null,
                CategoryKey = c.Name.ToLower().Replace(" ", "-")
            }).ToList(),
            CuratedForYou = curated,
            LocalFavorites = favorites.Select(f => new ExploreLocalFavoriteDto
            {
                Id = f.Id,
                Title = f.Name,
                Subtitle = f.Subtitle,
                Description = f.Description.Length > 100 ? f.Description[..100] + "..." : f.Description,
                Category = f.PlaceType?.Name?.ToLower(),
                Type = f.PlaceType?.Name,
                Location = f.Location?.Name,
                Image = f.ImageUrl,
                Rating = f.AverageRating,
                IsSaved = placeFavIds.Contains(f.Id),
                OpeningHours = f.OpeningHours,
                IsRecommended = true
            }).ToList()
        };
    }

    public async Task<DestinationScopedResult<List<LocalPlacesCategoryDto>>> GetLocalPlacesCategoriesAsync(int? destinationId)
    {
        if (!destinationId.HasValue || destinationId.Value <= 0)
        {
            return new DestinationScopedResult<List<LocalPlacesCategoryDto>> { Status = DestinationScopedStatus.InvalidDestination };
        }
        if (!await _db.Destinations.AnyAsync(d => d.Id == destinationId.Value))
        {
            return new DestinationScopedResult<List<LocalPlacesCategoryDto>> { Status = DestinationScopedStatus.DestinationNotFound };
        }

        var destId = destinationId.Value;

        var types = await _db.PlaceTypes
            .Where(pt => pt.Places.Any(p => p.Location.DestinationId == destId && p.PricePerNight == null))
            .ToListAsync();

        // Also get tags that can serve as categories
        var tags = await _db.Tags
            .Where(t => t.ForPlace && t.PlaceTags.Any(pt => pt.Place.Location.DestinationId == destId))
            .Take(10)
            .ToListAsync();

        var cats = new List<LocalPlacesCategoryDto>();

        foreach (var pt in types)
        {
            var count = await _db.Places.CountAsync(p => p.PlaceTypeId == pt.Id && p.Location.DestinationId == destId);
            cats.Add(new LocalPlacesCategoryDto
            {
                Id = pt.Id,
                Title = pt.Name,
                Image = null,
                CategoryKey = pt.Name.ToLower().Replace(" ", "-"),
                Subtitle = $"{count} places"
            });
        }

        return new DestinationScopedResult<List<LocalPlacesCategoryDto>> { Status = DestinationScopedStatus.Success, Data = cats };
    }

    private static string FormatDateLabel(DateTime start, DateTime end)
    {
        if (start.Date == end.Date) return start.ToString("d MMM");
        return $"{start:d MMM} - {end:d MMM}";
    }

    private static double HaversineDistance(double lat1, double lon1, double lat2, double lon2)
    {
        const double R = 6371.0;
        var dLat = (lat2 - lat1) * Math.PI / 180.0;
        var dLon = (lon2 - lon1) * Math.PI / 180.0;
        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                Math.Cos(lat1 * Math.PI / 180.0) * Math.Cos(lat2 * Math.PI / 180.0) *
                Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        return R * 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
    }
}
