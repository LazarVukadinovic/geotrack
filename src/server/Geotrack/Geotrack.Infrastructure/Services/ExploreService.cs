using Geotrack.Application.DTOs.Destinations;
using Geotrack.Application.DTOs.Explore;
using Geotrack.Application.Interfaces.Service;
using Geotrack.Infrastructure.Spatial;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;

namespace Geotrack.Infrastructure.Services;

public class ExploreService : IExploreService
{
    private static readonly string[] FoodMusicTagSlugs =
    [
        "food-drink",
        "nightlife",
        "live-music",
        "fine-dining",
        "beach-bars"
    ];

    private static readonly string[] FoodMusicTypeNames =
    [
        "restaurant",
        "bar",
        "cafe"
    ];

    private static readonly string[] CultureHistoryTagSlugs =
    [
        "culture",
        "history",
        "historic-sites",
        "museums",
        "galleries",
        "exhibitions",
        "landmarks"
    ];

    private static readonly string[] CultureHistoryTypeNames =
    [
        "museum",
        "landmark",
        "attraction"
    ];

    private static readonly string[] NatureTagSlugs =
    [
        "nature",
        "beaches",
        "islands",
        "parks",
        "mountains",
        "hiking",
        "scenic-views",
        "outdoor",
        "adventure"
    ];

    private static readonly string[] NatureTypeNames =
    [
        "nature",
        "beach",
        "activity",
        "attraction"
    ];

    private readonly AppDbContext _db;
    private readonly IFavoriteService _favoriteService;

    public ExploreService(AppDbContext db, IFavoriteService favoriteService)
    {
        _db = db;
        _favoriteService = favoriteService;
    }

    public async Task<ExplorePageDto> GetExplorePageAsync(double latitude, double longitude, int? userId = null)
    {
        var userPoint = SpatialPointFactory.Create(latitude, longitude);
        var resolvedDestination = await ResolveDestinationAsync(userPoint);
        if (resolvedDestination is null)
        {
            return new ExplorePageDto();
        }

        var placeFavoriteIds = userId.HasValue
            ? await _favoriteService.GetFavoriteIdsAsync(userId.Value, "place")
            : new HashSet<int>();
        var userTagIds = userId.HasValue
            ? await _db.UserTags
                .AsNoTracking()
                .Where(ut => ut.UserId == userId.Value)
                .Select(ut => ut.TagId)
                .ToListAsync()
            : new List<int>();

        var events = await _db.Events
            .AsNoTracking()
            .Where(e =>
                e.Location.DestinationId == resolvedDestination.Id &&
                EF.Property<Point?>(e, SpatialPointFactory.CoordinatesPropertyName) != null &&
                e.EndDate >= DateTime.UtcNow)
            .Select(e => new
            {
                e.Id,
                e.Name,
                EventTypeName = e.EventType.Name,
                e.StartDate,
                e.EndDate,
                e.ImageUrl,
                DistanceMeters = EF.Property<Point>(e, SpatialPointFactory.CoordinatesPropertyName).Distance(userPoint)
            })
            .OrderBy(e => e.DistanceMeters)
            .ThenBy(e => e.StartDate)
            .Take(6)
            .ToListAsync();

        var topStays = await _db.Places
            .AsNoTracking()
            .Where(p =>
                p.Location.DestinationId == resolvedDestination.Id &&
                p.PricePerNight != null &&
                EF.Property<Point?>(p, SpatialPointFactory.CoordinatesPropertyName) != null)
            .Select(p => new
            {
                p.Id,
                p.Name,
                p.ImageUrl,
                AverageRating = p.PlaceReviews.Select(r => (double?)r.Rating).Average(),
                ReviewCount = p.PlaceReviews.Count(),
                p.PricePerNight,
                DistanceMeters = EF.Property<Point>(p, SpatialPointFactory.CoordinatesPropertyName).Distance(userPoint)
            })
            .OrderBy(p => p.DistanceMeters)
            .ThenByDescending(p => p.AverageRating ?? 0)
            .Take(6)
            .ToListAsync();

        var nearbyNature = await _db.Places
            .AsNoTracking()
            .Where(p =>
                p.Location.DestinationId == resolvedDestination.Id &&
                p.PricePerNight == null &&
                EF.Property<Point?>(p, SpatialPointFactory.CoordinatesPropertyName) != null &&
                (NatureTypeNames.Contains(p.PlaceType.Name.ToLower()) ||
                 p.PlaceTags.Any(pt => NatureTagSlugs.Contains(pt.Tag.Slug.ToLower()))))
            .Select(p => new
            {
                p.Id,
                p.Name,
                PlaceTypeName = p.PlaceType.Name,
                p.ImageUrl,
                AverageRating = p.PlaceReviews.Select(r => (double?)r.Rating).Average(),
                ReviewCount = p.PlaceReviews.Count(),
                DistanceMeters = EF.Property<Point>(p, SpatialPointFactory.CoordinatesPropertyName).Distance(userPoint)
            })
            .OrderBy(p => p.DistanceMeters)
            .ThenByDescending(p => p.AverageRating ?? 0)
            .Take(6)
            .ToListAsync();

        var cultureHistory = await _db.Places
            .AsNoTracking()
            .Where(p =>
                p.Location.DestinationId == resolvedDestination.Id &&
                p.PricePerNight == null &&
                EF.Property<Point?>(p, SpatialPointFactory.CoordinatesPropertyName) != null &&
                (CultureHistoryTypeNames.Contains(p.PlaceType.Name.ToLower()) ||
                 p.PlaceTags.Any(pt => CultureHistoryTagSlugs.Contains(pt.Tag.Slug.ToLower()))))
            .Select(p => new
            {
                p.Id,
                p.Name,
                PlaceTypeName = p.PlaceType.Name,
                p.ImageUrl,
                ReviewCount = p.PlaceReviews.Count(),
                DistanceMeters = EF.Property<Point>(p, SpatialPointFactory.CoordinatesPropertyName).Distance(userPoint)
            })
            .OrderBy(p => p.DistanceMeters)
            .ThenByDescending(p => p.ReviewCount)
            .Take(6)
            .ToListAsync();

        var foodMusic = await _db.Places
            .AsNoTracking()
            .Where(p =>
                p.Location.DestinationId == resolvedDestination.Id &&
                p.PricePerNight == null &&
                EF.Property<Point?>(p, SpatialPointFactory.CoordinatesPropertyName) != null &&
                (FoodMusicTypeNames.Contains(p.PlaceType.Name.ToLower()) ||
                 p.PlaceTags.Any(pt => FoodMusicTagSlugs.Contains(pt.Tag.Slug.ToLower()))))
            .Select(p => new
            {
                p.Id,
                p.Name,
                p.Subtitle,
                p.Description,
                Category = p.PlaceType.Name,
                LocationName = p.Location.Name,
                p.ImageUrl,
                AverageRating = p.PlaceReviews.Select(r => (double?)r.Rating).Average(),
                ReviewCount = p.PlaceReviews.Count(),
                p.OpeningHours,
                DistanceMeters = EF.Property<Point>(p, SpatialPointFactory.CoordinatesPropertyName).Distance(userPoint)
            })
            .OrderBy(p => p.DistanceMeters)
            .ThenByDescending(p => p.AverageRating ?? 0)
            .Take(6)
            .ToListAsync();

        var curatedPlacesQuery = _db.Places
            .AsNoTracking()
            .Where(p =>
                p.Location.DestinationId == resolvedDestination.Id &&
                p.PricePerNight == null &&
                EF.Property<Point?>(p, SpatialPointFactory.CoordinatesPropertyName) != null);

        if (userTagIds.Count > 0)
        {
            curatedPlacesQuery = curatedPlacesQuery
                .Where(p => p.PlaceTags.Any(pt => userTagIds.Contains(pt.TagId)));
        }

        var curatedPlaces = await curatedPlacesQuery
            .Select(p => new
            {
                p.Id,
                p.Name,
                p.Subtitle,
                p.ImageUrl,
                DistanceMeters = EF.Property<Point>(p, SpatialPointFactory.CoordinatesPropertyName).Distance(userPoint),
                Rating = p.PlaceReviews.Select(r => (double?)r.Rating).Average() ?? 0,
                MatchCount = p.PlaceTags.Count(pt => userTagIds.Contains(pt.TagId)),
                CategoryLabel = p.PlaceTags
                    .OrderByDescending(pt => userTagIds.Contains(pt.TagId))
                    .ThenBy(pt => pt.Tag.Name)
                    .Select(pt => pt.Tag.Name)
                    .FirstOrDefault()
            })
            .OrderByDescending(p => p.MatchCount)
            .ThenBy(p => p.DistanceMeters)
            .ThenByDescending(p => p.Rating)
            .Take(6)
            .ToListAsync();

        var landmarkCategories = await BuildLandmarkCategoriesAsync(userPoint, resolvedDestination.Id);

        return new ExplorePageDto
        {
            SelectedDestination = new ExploreDestinationDto
            {
                Id = resolvedDestination.Id,
                Name = resolvedDestination.Name,
                Description = resolvedDestination.Description,
                Latitude = resolvedDestination.Latitude,
                Longitude = resolvedDestination.Longitude
            },
            EventsThisWeek = events.Select(e => new ExploreEventDto
            {
                Id = e.Id,
                Title = e.Name,
                Subtitle = e.EventTypeName,
                DateLabel = FormatDateLabel(e.StartDate, e.EndDate),
                Image = e.ImageUrl
            }).ToList(),
            TopStays = topStays.Select(s => new ExploreStayDto
            {
                Id = s.Id,
                Title = s.Name,
                Image = s.ImageUrl,
                Rating = s.AverageRating.HasValue ? Math.Round(s.AverageRating.Value, 1) : null,
                ReviewCount = FormatReviewCountLabel(s.ReviewCount),
                Distance = SpatialDistanceFormatter.Format(s.DistanceMeters),
                PricePerNight = s.PricePerNight ?? 0,
                IsSaved = placeFavoriteIds.Contains(s.Id)
            }).ToList(),
            PopularNearby = nearbyNature.Select(p => new ExploreNearbyItemDto
            {
                Id = p.Id,
                Title = p.Name,
                CategoryLabel = p.PlaceTypeName,
                Image = p.ImageUrl,
                Rating = p.AverageRating.HasValue ? Math.Round(p.AverageRating.Value, 1) : null,
                ReviewCount = FormatReviewCountLabel(p.ReviewCount),
                Distance = SpatialDistanceFormatter.Format(p.DistanceMeters),
                IsSaved = placeFavoriteIds.Contains(p.Id)
            }).ToList(),
            MustSeeAttractions = cultureHistory.Select(p => new ExploreAttractionDto
            {
                Id = p.Id,
                Title = p.Name,
                Type = p.PlaceTypeName,
                Image = p.ImageUrl
            }).ToList(),
            LandmarkCategories = landmarkCategories,
            CuratedForYou = curatedPlaces.Select(p => new ExploreCuratedItemDto
            {
                Id = p.Id,
                Title = p.Name,
                Subtitle = string.IsNullOrWhiteSpace(p.Subtitle) ? "Near your current area" : p.Subtitle,
                CategoryLabel = p.CategoryLabel,
                Image = p.ImageUrl,
                IsSaved = placeFavoriteIds.Contains(p.Id)
            }).ToList(),
            LocalFavorites = foodMusic.Select(p => new ExploreLocalFavoriteDto
            {
                Id = p.Id,
                Title = p.Name,
                Subtitle = p.Subtitle,
                Description = p.Description.Length > 100 ? p.Description[..100] + "..." : p.Description,
                Category = p.Category.ToLowerInvariant(),
                Type = p.Category,
                Location = p.LocationName,
                Image = p.ImageUrl,
                Rating = p.AverageRating.HasValue ? Math.Round(p.AverageRating.Value, 1) : null,
                ReviewCount = FormatReviewCountLabel(p.ReviewCount),
                IsSaved = placeFavoriteIds.Contains(p.Id),
                OpeningHours = p.OpeningHours,
                IsRecommended = true
            }).ToList()
        };
    }

    public async Task<DestinationScopedResult<List<LocalPlacesCategoryDto>>> GetLocalPlacesCategoriesAsync(
        int? destinationId,
        double? latitude = null,
        double? longitude = null)
    {
        if (!destinationId.HasValue || destinationId.Value <= 0)
        {
            return new DestinationScopedResult<List<LocalPlacesCategoryDto>> { Status = DestinationScopedStatus.InvalidDestination };
        }

        if (!await _db.Destinations.AsNoTracking().AnyAsync(d => d.Id == destinationId.Value))
        {
            return new DestinationScopedResult<List<LocalPlacesCategoryDto>> { Status = DestinationScopedStatus.DestinationNotFound };
        }

        var userPoint = latitude.HasValue && longitude.HasValue
            ? SpatialPointFactory.Create(latitude.Value, longitude.Value)
            : null;

        IQueryable<LocalPlacesCategoryDto> query;

        if (userPoint is null)
        {
            query = _db.Places
                .AsNoTracking()
                .Where(p => p.Location.DestinationId == destinationId.Value && p.PricePerNight == null)
                .GroupBy(p => new { p.PlaceTypeId, p.PlaceType.Name })
                .Select(g => new LocalPlacesCategoryDto
                {
                    Id = g.Key.PlaceTypeId,
                    Title = g.Key.Name,
                    CategoryKey = g.Key.Name.ToLower().Replace(" ", "-"),
                    Subtitle = $"{g.Count()} places"
                })
                .OrderBy(c => c.Title);
        }
        else
        {
            query = _db.Places
                .AsNoTracking()
                .Where(p =>
                    p.Location.DestinationId == destinationId.Value &&
                    p.PricePerNight == null &&
                    EF.Property<Point?>(p, SpatialPointFactory.CoordinatesPropertyName) != null)
                .GroupBy(p => new { p.PlaceTypeId, p.PlaceType.Name })
                .Select(g => new
                {
                    g.Key.PlaceTypeId,
                    g.Key.Name,
                    Count = g.Count(),
                    MinDistanceMeters = g.Min(p => EF.Property<Point>(p, SpatialPointFactory.CoordinatesPropertyName).Distance(userPoint))
                })
                .OrderBy(g => g.MinDistanceMeters)
                .ThenBy(g => g.Name)
                .Select(g => new LocalPlacesCategoryDto
                {
                    Id = g.PlaceTypeId,
                    Title = g.Name,
                    CategoryKey = g.Name.ToLower().Replace(" ", "-"),
                    Subtitle = $"{g.Count} places"
                });
        }

        return new DestinationScopedResult<List<LocalPlacesCategoryDto>>
        {
            Status = DestinationScopedStatus.Success,
            Data = await query.Take(10).ToListAsync()
        };
    }

    private async Task<List<ExploreLandmarkCategoryDto>> BuildLandmarkCategoriesAsync(Point userPoint, int destinationId)
    {
        return await _db.Places
            .AsNoTracking()
            .Where(p =>
                p.Location.DestinationId == destinationId &&
                p.PricePerNight == null &&
                EF.Property<Point?>(p, SpatialPointFactory.CoordinatesPropertyName) != null)
            .GroupBy(p => new { p.PlaceTypeId, p.PlaceType.Name })
            .Select(g => new
            {
                g.Key.PlaceTypeId,
                g.Key.Name,
                MinDistanceMeters = g.Min(p => EF.Property<Point>(p, SpatialPointFactory.CoordinatesPropertyName).Distance(userPoint))
            })
            .OrderBy(g => g.MinDistanceMeters)
            .ThenBy(g => g.Name)
            .Take(8)
            .Select(g => new ExploreLandmarkCategoryDto
            {
                Id = g.PlaceTypeId,
                Title = g.Name,
                Image = null,
                CategoryKey = g.Name.ToLower().Replace(" ", "-")
            })
            .ToListAsync();
    }

    private async Task<ResolvedDestinationCandidate?> ResolveDestinationAsync(Point userPoint)
    {
        var nearestPlace = await _db.Places
            .AsNoTracking()
            .Where(p => EF.Property<Point?>(p, SpatialPointFactory.CoordinatesPropertyName) != null)
            .Select(p => new ResolvedDestinationCandidate
            {
                Id = p.Location.Destination.Id,
                Name = p.Location.Destination.Name,
                Description = p.Location.Destination.Description,
                Latitude = p.Location.Destination.Latitude,
                Longitude = p.Location.Destination.Longitude,
                DistanceMeters = EF.Property<Point>(p, SpatialPointFactory.CoordinatesPropertyName).Distance(userPoint),
                Priority = 0
            })
            .OrderBy(p => p.DistanceMeters)
            .FirstOrDefaultAsync();

        var nearestEvent = await _db.Events
            .AsNoTracking()
            .Where(e => EF.Property<Point?>(e, SpatialPointFactory.CoordinatesPropertyName) != null)
            .Select(e => new ResolvedDestinationCandidate
            {
                Id = e.Location.Destination.Id,
                Name = e.Location.Destination.Name,
                Description = e.Location.Destination.Description,
                Latitude = e.Location.Destination.Latitude,
                Longitude = e.Location.Destination.Longitude,
                DistanceMeters = EF.Property<Point>(e, SpatialPointFactory.CoordinatesPropertyName).Distance(userPoint),
                Priority = 1
            })
            .OrderBy(e => e.DistanceMeters)
            .FirstOrDefaultAsync();

        var nearestDestination = await _db.Destinations
            .AsNoTracking()
            .Where(d =>
                EF.Property<Point?>(d, SpatialPointFactory.CoordinatesPropertyName) != null &&
                d.Locations.Any(l => l.Places.Any() || l.Events.Any()))
            .Select(d => new ResolvedDestinationCandidate
            {
                Id = d.Id,
                Name = d.Name,
                Description = d.Description,
                Latitude = d.Latitude,
                Longitude = d.Longitude,
                DistanceMeters = EF.Property<Point>(d, SpatialPointFactory.CoordinatesPropertyName).Distance(userPoint),
                Priority = 2
            })
            .OrderBy(d => d.DistanceMeters)
            .FirstOrDefaultAsync();

        return new[] { nearestPlace, nearestEvent, nearestDestination }
            .Where(candidate => candidate is not null)
            .OrderBy(candidate => candidate!.DistanceMeters)
            .ThenBy(candidate => candidate!.Priority)
            .FirstOrDefault();
    }

    private static string FormatDateLabel(DateTime start, DateTime end)
    {
        if (start.Date == end.Date)
        {
            return start.ToString("d MMM");
        }

        return $"{start:d MMM} - {end:d MMM}";
    }

    private static string? FormatReviewCountLabel(int count)
    {
        return count switch
        {
            0 => null,
            1 => "1 review",
            _ => $"{count} reviews"
        };
    }

    private sealed class ResolvedDestinationCandidate
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public double DistanceMeters { get; set; }
        public int Priority { get; set; }
    }
}
