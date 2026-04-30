using Geotrack.Application.DTOs.Destinations;
using Geotrack.Application.DTOs.Explore;
using Geotrack.Application.Interfaces.Service;
using Geotrack.Infrastructure.Spatial;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;

namespace Geotrack.Infrastructure.Services;

public class ExploreService : IExploreService
{
    private const double ExplicitInterestWeight = 100.0;
    private const double MinimumInferredInterestScore = 5.0;
    private const double MaximumInferredInterestWeight = 30.0;
    private const int CuratedCandidateLimit = 60;
    private const int CuratedSectionItemLimit = 6;
    private const int RecommendationCandidateLimit = 200;
    private const int RecommendationDefaultTake = 8;
    private const int RecommendationMaxTake = 20;
    private const int RecommendationFilterLimit = 10;
    private const int LocalCategoryDefaultTake = 10;
    private const int LocalCategoryMaxTake = 50;

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
        var curationSignals = userId.HasValue
            ? await LoadCurationSignalsAsync(userId.Value)
            : new List<CuratedInterestSignal>();

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

        var duplicatePlaceIds = nearbyNature
            .Select(p => p.Id)
            .Concat(cultureHistory.Select(p => p.Id))
            .Concat(foodMusic.Select(p => p.Id))
            .ToHashSet();
        var curatedPlaces = await BuildCuratedPlacesAsync(
            resolvedDestination.Id,
            userPoint,
            curationSignals,
            duplicatePlaceIds);

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
                Title = p.Title,
                Subtitle = p.Reason,
                CategoryLabel = p.CategoryLabel,
                Image = p.Image,
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

    public async Task<ExploreRecommendationsPageDto> GetRecommendationsAsync(
        double latitude,
        double longitude,
        int userId,
        int? tagId = null,
        int skip = 0,
        int take = RecommendationDefaultTake)
    {
        var userPoint = SpatialPointFactory.Create(latitude, longitude);
        var resolvedDestination = await ResolveDestinationAsync(userPoint);
        var normalizedSkip = Math.Max(0, skip);
        var normalizedTake = Math.Clamp(take, 1, RecommendationMaxTake);

        if (resolvedDestination is null)
        {
            return new ExploreRecommendationsPageDto
            {
                HasSignals = false,
                EmptyReason = "We could not resolve a nearby destination for recommendations.",
                Skip = normalizedSkip,
                Take = normalizedTake,
                NextSkip = normalizedSkip
            };
        }

        var curationSignals = await LoadCurationSignalsAsync(userId);
        if (curationSignals.Count == 0)
        {
            return new ExploreRecommendationsPageDto
            {
                SelectedDestination = MapExploreDestination(resolvedDestination),
                HasSignals = false,
                EmptyReason = "Add saved interests or keep exploring places so GeoTrack can build recommendations for you.",
                Skip = normalizedSkip,
                Take = normalizedTake,
                NextSkip = normalizedSkip
            };
        }

        var favoriteIds = await _favoriteService.GetFavoriteIdsAsync(userId, "place");
        var rankedPlaces = await BuildRankedCuratedPlacesAsync(
            resolvedDestination.Id,
            userPoint,
            curationSignals,
            duplicatePlaceIds: new HashSet<int>(),
            candidateLimit: RecommendationCandidateLimit);

        var filters = rankedPlaces
            .SelectMany(place => place.MatchedTags)
            .GroupBy(tag => new { tag.TagId, tag.Name })
            .Select(group => new ExploreRecommendationFilterDto
            {
                TagId = group.Key.TagId,
                Label = group.Key.Name,
                Count = group.Select(tag => tag.TagId).Count()
            })
            .OrderByDescending(filter => filter.Count)
            .ThenBy(filter => filter.Label)
            .Take(RecommendationFilterLimit)
            .ToList();

        var filteredPlaces = tagId.HasValue
            ? rankedPlaces
                .Where(place => place.MatchedTags.Any(tag => tag.TagId == tagId.Value))
                .ToList()
            : rankedPlaces;

        var pageItems = filteredPlaces
            .Skip(normalizedSkip)
            .Take(normalizedTake)
            .ToList();

        return new ExploreRecommendationsPageDto
        {
            SelectedDestination = MapExploreDestination(resolvedDestination),
            Filters = filters,
            Items = pageItems.Select(place => new ExploreRecommendationItemDto
            {
                Id = place.Id,
                Title = place.Title,
                Reason = place.Reason,
                Description = TrimDescription(place.Description, 170),
                Location = place.Location,
                Image = place.Image,
                Rating = place.AverageRating.HasValue ? Math.Round(place.AverageRating.Value, 1) : null,
                ReviewCount = FormatReviewCountLabel(place.ReviewCount),
                Distance = SpatialDistanceFormatter.Format(place.DistanceMeters),
                OpeningHours = place.OpeningHours,
                CategoryLabel = place.CategoryLabel,
                MatchedTagId = place.PrimaryTagId,
                IsSaved = favoriteIds.Contains(place.Id)
            }).ToList(),
            HasSignals = true,
            EmptyReason = filteredPlaces.Count == 0
                ? "No recommendations match this filter yet."
                : null,
            Skip = normalizedSkip,
            Take = normalizedTake,
            NextSkip = normalizedSkip + pageItems.Count,
            HasMore = filteredPlaces.Count > normalizedSkip + pageItems.Count
        };
    }

    public async Task<DestinationScopedResult<LocalPlacesCategoriesPageDto>> GetLocalPlacesCategoriesAsync(
        int? destinationId,
        double? latitude = null,
        double? longitude = null,
        int skip = 0,
        int take = LocalCategoryDefaultTake)
    {
        if (!destinationId.HasValue || destinationId.Value <= 0)
        {
            return new DestinationScopedResult<LocalPlacesCategoriesPageDto> { Status = DestinationScopedStatus.InvalidDestination };
        }

        if (!await _db.Destinations.AsNoTracking().AnyAsync(d => d.Id == destinationId.Value))
        {
            return new DestinationScopedResult<LocalPlacesCategoriesPageDto> { Status = DestinationScopedStatus.DestinationNotFound };
        }

        var normalizedSkip = Math.Max(0, skip);
        var normalizedTake = Math.Clamp(take, 1, LocalCategoryMaxTake);
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

        var categories = await query
            .Skip(normalizedSkip)
            .Take(normalizedTake + 1)
            .ToListAsync();
        var hasMore = categories.Count > normalizedTake;
        var pageItems = hasMore
            ? categories.Take(normalizedTake).ToList()
            : categories;

        return new DestinationScopedResult<LocalPlacesCategoriesPageDto>
        {
            Status = DestinationScopedStatus.Success,
            Data = new LocalPlacesCategoriesPageDto
            {
                Items = pageItems,
                Skip = normalizedSkip,
                Take = normalizedTake,
                NextSkip = normalizedSkip + pageItems.Count,
                HasMore = hasMore
            }
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

    private async Task<List<CuratedInterestSignal>> LoadCurationSignalsAsync(int userId)
    {
        var explicitInterests = await _db.UserTags
            .AsNoTracking()
            .Where(ut => ut.UserId == userId)
            .Select(ut => new CuratedInterestSignal
            {
                TagId = ut.TagId,
                IsExplicit = true,
                Weight = ExplicitInterestWeight
            })
            .ToListAsync();

        var explicitTagIds = explicitInterests
            .Select(interest => interest.TagId)
            .ToList();

        var inferredSignals = await _db.UserInterestSignals
            .AsNoTracking()
            .Where(signal =>
                signal.UserId == userId &&
                signal.Source == "inferred" &&
                !signal.IsDismissed &&
                signal.Score >= MinimumInferredInterestScore &&
                !explicitTagIds.Contains(signal.TagId))
            .OrderByDescending(signal => signal.Score)
            .ThenByDescending(signal => signal.LastInteractionAt)
            .Select(signal => new
            {
                TagId = signal.TagId,
                signal.Score,
                LastInteractionAt = signal.LastInteractionAt
            })
            .Take(20)
            .ToListAsync();

        explicitInterests.AddRange(inferredSignals.Select(signal => new CuratedInterestSignal
        {
            TagId = signal.TagId,
            IsExplicit = false,
            Weight = Math.Min(signal.Score, MaximumInferredInterestWeight),
            LastInteractionAt = signal.LastInteractionAt
        }));

        return explicitInterests;
    }

    private async Task<List<RankedCuratedPlace>> BuildCuratedPlacesAsync(
        int destinationId,
        Point userPoint,
        IReadOnlyCollection<CuratedInterestSignal> curationSignals,
        ISet<int> duplicatePlaceIds)
    {
        var rankedPlaces = await BuildRankedCuratedPlacesAsync(
            destinationId,
            userPoint,
            curationSignals,
            duplicatePlaceIds,
            CuratedCandidateLimit);

        return rankedPlaces
            .Take(CuratedSectionItemLimit)
            .ToList();
    }

    private async Task<List<RankedCuratedPlace>> BuildRankedCuratedPlacesAsync(
        int destinationId,
        Point userPoint,
        IReadOnlyCollection<CuratedInterestSignal> curationSignals,
        ISet<int> duplicatePlaceIds,
        int candidateLimit)
    {
        if (curationSignals.Count == 0)
        {
            return [];
        }

        var signalByTagId = curationSignals
            .GroupBy(signal => signal.TagId)
            .ToDictionary(
                group => group.Key,
                group => group.OrderByDescending(signal => signal.Weight).First());
        var curationTagIds = signalByTagId.Keys.ToList();
        var explicitTagIds = curationSignals
            .Where(signal => signal.IsExplicit)
            .Select(signal => signal.TagId)
            .ToList();
        var inferredTagIds = curationSignals
            .Where(signal => !signal.IsExplicit)
            .Select(signal => signal.TagId)
            .ToList();

        var candidates = await _db.Places
            .AsNoTracking()
            .Where(p =>
                p.Location.DestinationId == destinationId &&
                p.PricePerNight == null &&
                EF.Property<Point?>(p, SpatialPointFactory.CoordinatesPropertyName) != null &&
                p.PlaceTags.Any(pt => curationTagIds.Contains(pt.TagId)))
            .Select(p => new CuratedPlaceCandidate
            {
                Id = p.Id,
                Title = p.Name,
                Description = p.Description,
                Location = p.Location.Name,
                Image = p.ImageUrl,
                PlaceTypeName = p.PlaceType.Name,
                OpeningHours = p.OpeningHours,
                DistanceMeters = EF.Property<Point>(p, SpatialPointFactory.CoordinatesPropertyName).Distance(userPoint),
                AverageRating = p.PlaceReviews.Select(r => (double?)r.Rating).Average(),
                ReviewCount = p.PlaceReviews.Count(),
                ExplicitMatchCount = p.PlaceTags.Count(pt => explicitTagIds.Contains(pt.TagId)),
                InferredMatchCount = p.PlaceTags.Count(pt => inferredTagIds.Contains(pt.TagId)),
                MatchedTags = p.PlaceTags
                    .Where(pt => curationTagIds.Contains(pt.TagId))
                    .Select(pt => new CuratedTagMatch
                    {
                        TagId = pt.TagId,
                        Name = pt.Tag.Name
                    })
                    .ToList()
            })
            .OrderByDescending(p => p.ExplicitMatchCount)
            .ThenByDescending(p => p.InferredMatchCount)
            .ThenBy(p => p.DistanceMeters)
            .ThenByDescending(p => p.AverageRating ?? 0)
            .Take(candidateLimit)
            .ToListAsync();

        return candidates
            .Select(candidate => RankCuratedCandidate(
                candidate,
                signalByTagId,
                duplicatePlaceIds.Contains(candidate.Id)))
            .Where(candidate => candidate.Score > 0)
            .OrderByDescending(candidate => candidate.Score)
            .ThenBy(candidate => candidate.IsDuplicateWithOtherSection)
            .ThenBy(candidate => candidate.DistanceMeters)
            .ThenByDescending(candidate => candidate.AverageRating ?? 0)
            .ThenByDescending(candidate => candidate.ReviewCount)
            .ThenBy(candidate => candidate.Title)
            .ToList();
    }

    private static RankedCuratedPlace RankCuratedCandidate(
        CuratedPlaceCandidate candidate,
        IReadOnlyDictionary<int, CuratedInterestSignal> signalByTagId,
        bool isDuplicateWithOtherSection)
    {
        var score = 0.0;
        CuratedTagMatch? bestTag = null;
        CuratedInterestSignal? bestSignal = null;

        foreach (var tag in candidate.MatchedTags)
        {
            if (!signalByTagId.TryGetValue(tag.TagId, out var signal))
            {
                continue;
            }

            score += signal.Weight;

            if (bestSignal is null ||
                signal.Weight > bestSignal.Weight ||
                (Math.Abs(signal.Weight - bestSignal.Weight) < 0.001 && signal.IsExplicit && !bestSignal.IsExplicit))
            {
                bestTag = tag;
                bestSignal = signal;
            }
        }

        var categoryLabel = bestTag?.Name ?? candidate.PlaceTypeName;
        var reason = bestSignal?.IsExplicit == true
            ? $"Matches your saved interest: {categoryLabel}"
            : $"Based on recent activity: {categoryLabel}";

        return new RankedCuratedPlace
        {
            Id = candidate.Id,
            Title = candidate.Title,
            Description = candidate.Description,
            Location = candidate.Location,
            Image = candidate.Image,
            CategoryLabel = categoryLabel,
            PrimaryTagId = bestTag?.TagId,
            Reason = reason,
            Score = score,
            DistanceMeters = candidate.DistanceMeters,
            AverageRating = candidate.AverageRating,
            ReviewCount = candidate.ReviewCount,
            OpeningHours = candidate.OpeningHours,
            MatchedTags = candidate.MatchedTags,
            IsDuplicateWithOtherSection = isDuplicateWithOtherSection
        };
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

    private static ExploreDestinationDto MapExploreDestination(ResolvedDestinationCandidate destination)
    {
        return new ExploreDestinationDto
        {
            Id = destination.Id,
            Name = destination.Name,
            Description = destination.Description,
            Latitude = destination.Latitude,
            Longitude = destination.Longitude
        };
    }

    private static string? TrimDescription(string? description, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(description))
        {
            return null;
        }

        var trimmed = description.Trim();
        return trimmed.Length > maxLength
            ? trimmed[..maxLength] + "..."
            : trimmed;
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

    private sealed class CuratedInterestSignal
    {
        public int TagId { get; set; }
        public bool IsExplicit { get; set; }
        public double Weight { get; set; }
        public DateTime? LastInteractionAt { get; set; }
    }

    private sealed class CuratedPlaceCandidate
    {
        public int Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public string? Description { get; set; }
        public string? Location { get; set; }
        public string? Image { get; set; }
        public string PlaceTypeName { get; set; } = string.Empty;
        public string? OpeningHours { get; set; }
        public double DistanceMeters { get; set; }
        public double? AverageRating { get; set; }
        public int ReviewCount { get; set; }
        public int ExplicitMatchCount { get; set; }
        public int InferredMatchCount { get; set; }
        public List<CuratedTagMatch> MatchedTags { get; set; } = new();
    }

    private sealed class CuratedTagMatch
    {
        public int TagId { get; set; }
        public string Name { get; set; } = string.Empty;
    }

    private sealed class RankedCuratedPlace
    {
        public int Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public string? Description { get; set; }
        public string? Location { get; set; }
        public string? Image { get; set; }
        public string CategoryLabel { get; set; } = string.Empty;
        public int? PrimaryTagId { get; set; }
        public string Reason { get; set; } = string.Empty;
        public double Score { get; set; }
        public double DistanceMeters { get; set; }
        public double? AverageRating { get; set; }
        public int ReviewCount { get; set; }
        public string? OpeningHours { get; set; }
        public List<CuratedTagMatch> MatchedTags { get; set; } = new();
        public bool IsDuplicateWithOtherSection { get; set; }
    }
}
