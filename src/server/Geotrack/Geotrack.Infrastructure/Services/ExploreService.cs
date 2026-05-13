using Geotrack.Application.DTOs.Destinations;
using Geotrack.Application.DTOs.Explore;
using Geotrack.Application.Interfaces.Service;
using Geotrack.Application.Services;
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
    private const int ExploreSectionItemLimit = 6;
    private const int LandmarkCategoryPreviewLimit = 8;
    private const int DynamicExploreSectionLimit = 6;
    private const int DynamicSectionCandidateLimit = 250;
    private const int DynamicSectionItemLimit = 6;

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

    private static readonly string[] WhereToEatTagTypeSlugs =
    [
        "cuisine",
        "dining-style"
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

    public Task<ExplorePageDto> GetExplorePageAsync(double latitude, double longitude, int? userId = null) =>
        GetExplorePageCoreAsync(latitude, longitude, userId, preferredDestinationId: null);

    public async Task<ExplorePageDto> GetExplorePageForDestinationAsync(int destinationId, int? userId = null)
    {
        if (destinationId <= 0)
        {
            return new ExplorePageDto();
        }

        var destination = await _db.Destinations
            .AsNoTracking()
            .Where(d => d.Id == destinationId)
            .Select(d => new { d.Latitude, d.Longitude })
            .SingleOrDefaultAsync();

        if (destination is null || !HasUsableCoordinates(destination.Latitude, destination.Longitude))
        {
            return new ExplorePageDto();
        }

        return await GetExplorePageCoreAsync(
            destination.Latitude,
            destination.Longitude,
            userId,
            destinationId);
    }

    private async Task<ExplorePageDto> GetExplorePageCoreAsync(
        double latitude,
        double longitude,
        int? userId,
        int? preferredDestinationId)
    {
        var userPoint = SpatialPointFactory.Create(latitude, longitude);
        var resolvedDestination = preferredDestinationId.HasValue
            ? await ResolveDestinationByIdAsync(preferredDestinationId.Value, userPoint)
            : await ResolveDestinationAsync(userPoint);
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
                EF.Property<Point?>(e, SpatialPointFactory.CoordinatesPropertyName) != null &&
                e.EndDate >= DateTime.UtcNow)
            .Select(e => new
            {
                e.Id,
                e.Name,
                DestinationId = e.DestinationId,
                EventTypeName = e.EventType.Name,
                e.StartDate,
                e.EndDate,
                Image = e.Images.OrderByDescending(image => image.IsMain).ThenBy(image => image.SortOrder).ThenBy(image => image.CreatedAt).ThenBy(image => image.Id).Select(image => image.Url).FirstOrDefault(),
                DistanceMeters = EF.Property<Point>(e, SpatialPointFactory.CoordinatesPropertyName).Distance(userPoint)
            })
            .OrderBy(e => e.DestinationId == resolvedDestination.Id ? 0 : 1)
            .ThenBy(e => e.DistanceMeters)
            .ThenBy(e => e.StartDate)
            .Take(ExploreSectionItemLimit)
            .ToListAsync();

        var topStays = await _db.Places
            .AsNoTracking()
            .Where(p =>
                p.Stay != null &&
                EF.Property<Point?>(p, SpatialPointFactory.CoordinatesPropertyName) != null)
            .Select(p => new
            {
                p.Id,
                p.Name,
                DestinationId = p.Location.DestinationId,
                Image = p.Images.OrderByDescending(image => image.IsMain).ThenBy(image => image.SortOrder).ThenBy(image => image.CreatedAt).ThenBy(image => image.Id).Select(image => image.Url).FirstOrDefault(),
                AverageRating = p.PlaceReviews.Select(r => (double?)r.Rating).Average(),
                ReviewCount = p.PlaceReviews.Count(),
                PricePerNight = p.Stay != null ? p.Stay.PricePerNight : null,
                DistanceMeters = EF.Property<Point>(p, SpatialPointFactory.CoordinatesPropertyName).Distance(userPoint)
            })
            .OrderBy(p => p.DestinationId == resolvedDestination.Id ? 0 : 1)
            .ThenBy(p => p.DistanceMeters)
            .ThenByDescending(p => p.AverageRating ?? 0)
            .Take(ExploreSectionItemLimit)
            .ToListAsync();

        var whereToEatSection = await BuildWhereToEatSectionAsync(
            resolvedDestination.Id,
            userPoint,
            placeFavoriteIds);

        var dynamicSections = await BuildDynamicPlaceSectionsAsync(
            resolvedDestination.Id,
            userPoint,
            curationSignals,
            placeFavoriteIds);

        var duplicatePlaceIds = dynamicSections
            .SelectMany(section => section.Items)
            .Select(item => item.Id)
            .Concat(whereToEatSection.Items.Select(item => item.Id))
            .ToHashSet();

        var curatedPlaces = await BuildCuratedPlacesAsync(
            resolvedDestination.Id,
            userPoint,
            curationSignals,
            duplicatePlaceIds);

        var sections = new List<ExploreFeedSectionDto>();

        var curatedSection = BuildCuratedForYouSection(curatedPlaces, placeFavoriteIds);
        if (curatedSection.Items.Count > 0)
        {
            sections.Add(curatedSection);
        }

        var staysSection = new ExploreFeedSectionDto
        {
            Key = "top-stays",
            Title = "Top Stays",
            Subtitle = "Highly rated accommodation nearby",
            Layout = "horizontal-cards",
            CardType = "stay-card",
            MoreRoute = "/hotels",
            Priority = 20,
            Items = topStays.Select(s => new ExploreFeedSectionItemDto
            {
                Id = s.Id,
                EntityType = "stay",
                DetailRoute = $"/stays/{s.Id}",
                Title = DisplayTextSafety.CleanRequired(s.Name, "Stay"),
                Image = s.Image,
                Rating = s.AverageRating.HasValue ? Math.Round(s.AverageRating.Value, 1) : null,
                ReviewCount = FormatReviewCountLabel(s.ReviewCount),
                Distance = SpatialDistanceFormatter.Format(s.DistanceMeters),
                PricePerNight = s.PricePerNight,
                IsSaved = placeFavoriteIds.Contains(s.Id)
            }).ToList()
        };
        if (staysSection.Items.Count > 0)
        {
            sections.Add(staysSection);
        }

        if (whereToEatSection.Items.Count > 0)
        {
            sections.Add(whereToEatSection);
        }

        var eventsSection = new ExploreFeedSectionDto
        {
            Key = "events",
            Title = "Events",
            Subtitle = "What's happening around town",
            Layout = "horizontal-cards",
            CardType = "event-card",
            MoreRoute = "/events",
            Priority = 40,
            Items = events.Select(e => new ExploreFeedSectionItemDto
            {
                Id = e.Id,
                EntityType = "event",
                DetailRoute = $"/events/{e.Id}",
                Title = DisplayTextSafety.CleanRequired(e.Name, "Event"),
                Subtitle = DisplayTextSafety.CleanOptional(e.EventTypeName),
                DateLabel = FormatDateLabel(e.StartDate, e.EndDate),
                Image = e.Image,
                Distance = SpatialDistanceFormatter.Format(e.DistanceMeters)
            }).ToList()
        };
        if (eventsSection.Items.Count > 0)
        {
            sections.Add(eventsSection);
        }

        sections.AddRange(dynamicSections);

        return new ExplorePageDto
        {
            SelectedDestination = new ExploreDestinationDto
            {
                Id = resolvedDestination.Id,
                Name = DisplayTextSafety.CleanRequired(resolvedDestination.Name, "Nearby"),
                Description = DisplayTextSafety.CleanOptional(resolvedDestination.Description) ?? string.Empty,
                Latitude = resolvedDestination.Latitude,
                Longitude = resolvedDestination.Longitude
            },
            Sections = sections
                .Where(section => section.Items.Count > 0)
                .OrderBy(section => section.Priority)
                .ThenBy(section => section.Title)
                .ToList()
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
        var userPoint = BuildUserPoint(latitude, longitude)
            ?? await ResolveDestinationPointAsync(destinationId.Value);

        var categoryQuery = _db.Places
            .AsNoTracking()
            .Where(p => p.Stay == null);

        IQueryable<LocalCategoryProjection> orderedQuery;

        if (userPoint is null)
        {
            orderedQuery = categoryQuery
                .GroupBy(p => new { p.PlaceTypeId, p.PlaceType.Name })
                .Select(g => new LocalCategoryProjection
                {
                    Id = g.Key.PlaceTypeId,
                    Title = g.Key.Name,
                    Count = g.Count(),
                    CurrentDestinationCount = g.Count(p => p.Location.DestinationId == destinationId.Value)
                })
                .OrderBy(g => g.CurrentDestinationCount > 0 ? 0 : 1)
                .ThenByDescending(g => g.Count)
                .ThenBy(g => g.Title);
        }
        else
        {
            orderedQuery = categoryQuery
                .Where(p => EF.Property<Point?>(p, SpatialPointFactory.CoordinatesPropertyName) != null)
                .GroupBy(p => new { p.PlaceTypeId, p.PlaceType.Name })
                .Select(g => new LocalCategoryProjection
                {
                    Id = g.Key.PlaceTypeId,
                    Title = g.Key.Name,
                    Count = g.Count(),
                    CurrentDestinationCount = g.Count(p => p.Location.DestinationId == destinationId.Value),
                    MinDistanceMeters = g.Min(p => EF.Property<Point>(p, SpatialPointFactory.CoordinatesPropertyName).Distance(userPoint))
                })
                .OrderBy(g => g.CurrentDestinationCount > 0 ? 0 : 1)
                .ThenBy(g => g.MinDistanceMeters)
                .ThenByDescending(g => g.Count)
                .ThenBy(g => g.Title);
        }

        var categories = await orderedQuery
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
                Items = pageItems.Select(category => new LocalPlacesCategoryDto
                {
                    Id = category.Id,
                    Title = DisplayTextSafety.CleanRequired(category.Title, "Local places"),
                    CategoryKey = ToCategoryKey(category.Title),
                    Subtitle = FormatPlacesCountLabel(category.Count)
                })
                .DistinctBy(category => category.Title.ToLowerInvariant())
                .ToList(),
                Skip = normalizedSkip,
                Take = normalizedTake,
                NextSkip = normalizedSkip + pageItems.Count,
                HasMore = hasMore
            }
        };
    }


    private static ExploreFeedSectionDto BuildCuratedForYouSection(
        IReadOnlyCollection<RankedCuratedPlace> curatedPlaces,
        ISet<int> placeFavoriteIds)
    {
        return new ExploreFeedSectionDto
        {
            Key = "curated-for-you",
            Title = "Curated for You",
            Subtitle = "Based on your interests and recent activity nearby",
            Layout = "horizontal-cards",
            CardType = "place-card",
            MoreRoute = "/recommendations",
            Priority = 10,
            Items = curatedPlaces.Select(p => new ExploreFeedSectionItemDto
            {
                Id = p.Id,
                EntityType = "place",
                DetailRoute = $"/places/{p.Id}",
                Title = p.Title,
                Subtitle = p.Reason,
                Description = TrimDescription(p.Description, 130),
                CategoryLabel = p.CategoryLabel,
                Image = p.Image,
                Rating = p.AverageRating.HasValue ? Math.Round(p.AverageRating.Value, 1) : null,
                ReviewCount = FormatReviewCountLabel(p.ReviewCount),
                Distance = SpatialDistanceFormatter.Format(p.DistanceMeters),
                OpeningHours = p.OpeningHours,
                IsSaved = placeFavoriteIds.Contains(p.Id),
                Tags = p.MatchedTags.Select(tag => new ExploreFeedSectionTagDto
                {
                    TagId = tag.TagId,
                    Name = tag.Name,
                    Slug = ToCategoryKey(tag.Name)
                }).ToList()
            }).ToList()
        };
    }


    private async Task<ExploreFeedSectionDto> BuildWhereToEatSectionAsync(
        int destinationId,
        Point userPoint,
        ISet<int> placeFavoriteIds)
    {
        var items = await _db.Places
            .AsNoTracking()
            .Where(p =>
                p.Stay == null &&
                EF.Property<Point?>(p, SpatialPointFactory.CoordinatesPropertyName) != null &&
                (
                    FoodMusicTypeNames.Contains(p.PlaceType.Name.ToLower()) ||
                    p.PlaceTags.Any(pt =>
                        pt.Tag.ForPlace &&
                        (
                            FoodMusicTagSlugs.Contains(pt.Tag.Slug) ||
                            pt.Tag.TagType != null &&
                            WhereToEatTagTypeSlugs.Contains(pt.Tag.TagType.Slug)
                        ))
                ))
            .Select(p => new
            {
                p.Id,
                p.Name,
                p.Subtitle,
                p.Description,
                PlaceTypeName = p.PlaceType.Name,
                DestinationId = p.Location.DestinationId,
                p.OpeningHours,
                Image = p.Images
                    .OrderByDescending(image => image.IsMain)
                    .ThenBy(image => image.SortOrder)
                    .ThenBy(image => image.CreatedAt)
                    .ThenBy(image => image.Id)
                    .Select(image => image.Url)
                    .FirstOrDefault(),
                AverageRating = p.PlaceReviews.Select(r => (double?)r.Rating).Average(),
                ReviewCount = p.PlaceReviews.Count(),
                DistanceMeters = EF.Property<Point>(p, SpatialPointFactory.CoordinatesPropertyName).Distance(userPoint),
                Tags = p.PlaceTags
                    .Where(pt => pt.Tag.ForPlace)
                    .Select(pt => new ExploreFeedSectionTagDto
                    {
                        TagId = pt.TagId,
                        Name = pt.Tag.Name,
                        Slug = pt.Tag.Slug
                    })
                    .ToList()
            })
            .OrderBy(p => p.DestinationId == destinationId ? 0 : 1)
            .ThenBy(p => p.DistanceMeters)
            .ThenByDescending(p => p.AverageRating ?? 0)
            .ThenByDescending(p => p.ReviewCount)
            .Take(ExploreSectionItemLimit)
            .ToListAsync();

        return new ExploreFeedSectionDto
        {
            Key = "where-to-eat",
            Title = "Where to eat",
            Subtitle = "Restaurants, cafes and local food nearby",
            Layout = "horizontal-cards",
            CardType = "place-card",
            MoreRoute = "/food-and-music",
            Priority = 30,
            Items = items.Select(p => new ExploreFeedSectionItemDto
            {
                Id = p.Id,
                EntityType = "place",
                DetailRoute = $"/places/{p.Id}",
                Title = DisplayTextSafety.CleanRequired(p.Name, "Place"),
                Subtitle = DisplayTextSafety.CleanOptional(p.Subtitle),
                Description = TrimDescription(p.Description, 130),
                CategoryLabel = DisplayTextSafety.CleanOptional(p.PlaceTypeName),
                Image = p.Image,
                Rating = p.AverageRating.HasValue ? Math.Round(p.AverageRating.Value, 1) : null,
                ReviewCount = FormatReviewCountLabel(p.ReviewCount),
                Distance = SpatialDistanceFormatter.Format(p.DistanceMeters),
                OpeningHours = p.OpeningHours,
                IsSaved = placeFavoriteIds.Contains(p.Id),
                Tags = p.Tags
            }).ToList()
        };
    }

    private async Task<List<ExploreFeedSectionDto>> BuildDynamicPlaceSectionsAsync(
        int destinationId,
        Point userPoint,
        IReadOnlyCollection<CuratedInterestSignal> curationSignals,
        ISet<int> placeFavoriteIds)
    {
        var signalByTagId = curationSignals
            .GroupBy(signal => signal.TagId)
            .ToDictionary(
                group => group.Key,
                group => group.OrderByDescending(signal => signal.Weight).First());

        var candidates = await _db.Places
            .AsNoTracking()
            .Where(p =>
                p.Stay == null &&
                EF.Property<Point?>(p, SpatialPointFactory.CoordinatesPropertyName) != null &&
                p.PlaceTags.Any(pt =>
                    pt.Tag.ForPlace &&
                    pt.Tag.CanGenerateExploreSection))
            .Select(p => new DynamicSectionCandidate
            {
                Id = p.Id,
                Title = p.Name,
                Subtitle = p.Subtitle,
                Description = p.Description,
                Location = p.Location.Name,
                DestinationId = p.Location.DestinationId,
                PlaceTypeId = p.PlaceTypeId,
                PlaceTypeName = p.PlaceType.Name,
                OpeningHours = p.OpeningHours,
                Image = p.Images
                    .OrderByDescending(image => image.IsMain)
                    .ThenBy(image => image.SortOrder)
                    .ThenBy(image => image.CreatedAt)
                    .ThenBy(image => image.Id)
                    .Select(image => image.Url)
                    .FirstOrDefault(),
                AverageRating = p.PlaceReviews.Select(r => (double?)r.Rating).Average(),
                ReviewCount = p.PlaceReviews.Count(),
                DistanceMeters = EF.Property<Point>(p, SpatialPointFactory.CoordinatesPropertyName).Distance(userPoint),
                MatchedTags = p.PlaceTags
                    .Where(pt => pt.Tag.ForPlace)
                    .Select(pt => new DynamicSectionTagMatch
                    {
                        TagId = pt.TagId,
                        Name = pt.Tag.Name,
                        Slug = pt.Tag.Slug,
                        TagTypeId = pt.Tag.TagTypeId,
                        TagTypeName = pt.Tag.TagType != null ? pt.Tag.TagType.Name : null,
                        TagTypeSlug = pt.Tag.TagType != null ? pt.Tag.TagType.Slug : null,
                        CanGenerateExploreSection = pt.Tag.CanGenerateExploreSection
                    })
                    .ToList()
            })
            .OrderBy(p => p.DestinationId == destinationId ? 0 : 1)
            .ThenBy(p => p.DistanceMeters)
            .ThenByDescending(p => p.AverageRating ?? 0)
            .ThenByDescending(p => p.ReviewCount)
            .Take(DynamicSectionCandidateLimit)
            .ToListAsync();

        var sectionAccumulators = new Dictionary<string, DynamicSectionAccumulator>(StringComparer.OrdinalIgnoreCase);

        foreach (var candidate in candidates)
        {
            var identities = BuildSectionIdentities(candidate);

            foreach (var identity in identities)
            {
                if (!sectionAccumulators.TryGetValue(identity.Key, out var section))
                {
                    section = new DynamicSectionAccumulator
                    {
                        TagTypeId = identity.TagTypeId,
                        Key = identity.Key,
                        Title = identity.Title
                    };
                    sectionAccumulators[identity.Key] = section;
                }

                var relevantTags = candidate.MatchedTags
                    .Where(tag => MatchesSectionIdentity(tag, identity))
                    .ToList();

                if (relevantTags.Count == 0)
                {
                    relevantTags = candidate.MatchedTags;
                }

                var itemScore = ScoreDynamicSectionItem(
                    candidate,
                    relevantTags,
                    signalByTagId,
                    destinationId);

                section.Score += itemScore;

                if (!section.Items.Any(item => item.Id == candidate.Id))
                {
                    section.Items.Add(new ScoredDynamicSectionItem
                    {
                        Score = itemScore,
                        Id = candidate.Id,
                        EntityType = "place",
                        DetailRoute = $"/places/{candidate.Id}",
                        Title = DisplayTextSafety.CleanRequired(candidate.Title, "Place"),
                        Subtitle = DisplayTextSafety.CleanOptional(candidate.Subtitle),
                        Description = TrimDescription(candidate.Description, 130),
                        CategoryLabel = DisplayTextSafety.CleanLabelOrHumanizedKey(
                            relevantTags.FirstOrDefault()?.Name,
                            candidate.PlaceTypeName,
                            "Place"),
                        Image = candidate.Image,
                        Rating = candidate.AverageRating.HasValue ? Math.Round(candidate.AverageRating.Value, 1) : null,
                        ReviewCount = FormatReviewCountLabel(candidate.ReviewCount),
                        Distance = SpatialDistanceFormatter.Format(candidate.DistanceMeters),
                        OpeningHours = candidate.OpeningHours,
                        IsSaved = placeFavoriteIds.Contains(candidate.Id),
                        DistanceMeters = candidate.DistanceMeters,
                        ReviewCountRaw = candidate.ReviewCount,
                        Tags = relevantTags
                            .Select(tag => new ExploreFeedSectionTagDto
                            {
                                TagId = tag.TagId,
                                Name = DisplayTextSafety.CleanRequired(tag.Name, "Tag"),
                                Slug = tag.Slug
                            })
                            .DistinctBy(tag => tag.TagId)
                            .ToList()
                    });
                }
            }
        }

        return sectionAccumulators.Values
            .Where(section => section.Items.Count > 0)
            .OrderByDescending(section => section.Score)
            .ThenByDescending(section => section.Items.Count)
            .ThenBy(section => section.Title)
            .Take(DynamicExploreSectionLimit)
            .Select((section, index) => new ExploreFeedSectionDto
            {
                TagTypeId = section.TagTypeId,
                Key = section.Key,
                Title = DisplayTextSafety.CleanRequired(section.Title, "Explore"),
                Subtitle = BuildDynamicSectionSubtitle(section),
                Layout = "horizontal-cards",
                CardType = "place-card",
                Priority = 100 + index,
                Items = section.Items
                    .OrderByDescending(item => item.Score)
                    .ThenBy(item => item.DistanceMeters)
                    .ThenByDescending(item => item.Rating ?? 0)
                    .ThenByDescending(item => item.ReviewCountRaw)
                    .ThenBy(item => item.Title)
                    .Take(DynamicSectionItemLimit)
                    .Select(item => new ExploreFeedSectionItemDto
                    {
                        Id = item.Id,
                        EntityType = item.EntityType,
                        DetailRoute = item.DetailRoute,
                        Title = item.Title,
                        Subtitle = item.Subtitle,
                        Description = item.Description,
                        CategoryLabel = null,
                        Image = item.Image,
                        Rating = item.Rating,
                        ReviewCount = item.ReviewCount,
                        Distance = item.Distance,
                        OpeningHours = item.OpeningHours,
                        IsSaved = item.IsSaved,
                        Tags = item.Tags
                    })
                    .ToList()
            })
            .ToList();
    }

    private static List<DynamicSectionIdentity> BuildSectionIdentities(DynamicSectionCandidate candidate)
    {
        return candidate.MatchedTags
            .Where(IsDynamicExploreSectionTag)
            .Select(tag =>
            {
                var title = DisplayTextSafety.CleanRequired(tag.Name, "Explore");
                var key = ToCategoryKey(!string.IsNullOrWhiteSpace(tag.Slug) ? tag.Slug : title);

                return new DynamicSectionIdentity
                {
                    TagId = tag.TagId,
                    TagTypeId = tag.TagTypeId,
                    Key = key,
                    Title = title
                };
            })
            .Where(identity => !string.IsNullOrWhiteSpace(identity.Key))
            .DistinctBy(identity => identity.Key)
            .ToList();
    }

    private static bool IsDynamicExploreSectionTag(DynamicSectionTagMatch tag)
    {
        return tag.CanGenerateExploreSection &&
               (!string.IsNullOrWhiteSpace(tag.Name) || !string.IsNullOrWhiteSpace(tag.Slug));
    }

    private static bool MatchesSectionIdentity(DynamicSectionTagMatch tag, DynamicSectionIdentity identity)
    {
        return tag.TagId == identity.TagId;
    }

    private static double ScoreDynamicSectionItem(
        DynamicSectionCandidate candidate,
        IReadOnlyCollection<DynamicSectionTagMatch> matchedTags,
        IReadOnlyDictionary<int, CuratedInterestSignal> signalByTagId,
        int currentDestinationId)
    {
        var score = 0.0;

        if (candidate.DestinationId == currentDestinationId)
        {
            score += 40;
        }

        foreach (var tag in matchedTags)
        {
            if (signalByTagId.TryGetValue(tag.TagId, out var signal))
            {
                score += signal.Weight;
            }
        }

        if (candidate.AverageRating.HasValue)
        {
            score += Math.Clamp(candidate.AverageRating.Value, 0, 5) * 5;
        }

        score += Math.Min(candidate.ReviewCount, 50) * 0.35;

        if (candidate.DistanceMeters < 2_000)
        {
            score += 18;
        }
        else if (candidate.DistanceMeters < 10_000)
        {
            score += 10;
        }
        else if (candidate.DistanceMeters < 30_000)
        {
            score += 4;
        }

        return score;
    }

    private static string? BuildDynamicSectionSubtitle(DynamicSectionAccumulator section)
    {
        var count = section.Items.Count;
        if (count == 0)
        {
            return null;
        }

        return count == 1
            ? "1 matching place nearby"
            : $"{count} matching places nearby";
    }

    private async Task<List<ExploreLandmarkCategoryDto>> BuildLandmarkCategoriesAsync(Point userPoint, int destinationId)
    {
        var categories = await _db.Places
            .AsNoTracking()
            .Where(p =>
                p.Stay == null &&
                EF.Property<Point?>(p, SpatialPointFactory.CoordinatesPropertyName) != null)
            .GroupBy(p => new { p.PlaceTypeId, p.PlaceType.Name })
            .Select(g => new
            {
                g.Key.PlaceTypeId,
                g.Key.Name,
                Count = g.Count(),
                CurrentDestinationCount = g.Count(p => p.Location.DestinationId == destinationId),
                MinDistanceMeters = g.Min(p => EF.Property<Point>(p, SpatialPointFactory.CoordinatesPropertyName).Distance(userPoint))
            })
            .OrderBy(g => g.CurrentDestinationCount > 0 ? 0 : 1)
            .ThenBy(g => g.MinDistanceMeters)
            .ThenByDescending(g => g.Count)
            .ThenBy(g => g.Name)
            .Take(LandmarkCategoryPreviewLimit)
            .ToListAsync();

        return categories
            .Select(g => new ExploreLandmarkCategoryDto
            {
                Id = g.PlaceTypeId,
                Title = DisplayTextSafety.CleanRequired(g.Name, "Local places"),
                Image = null,
                CategoryKey = ToCategoryKey(g.Name)
            })
            .DistinctBy(category => category.Title.ToLowerInvariant())
            .ToList();
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
                p.Stay == null &&
                EF.Property<Point?>(p, SpatialPointFactory.CoordinatesPropertyName) != null &&
                p.PlaceTags.Any(pt => curationTagIds.Contains(pt.TagId)))
            .Select(p => new CuratedPlaceCandidate
            {
                Id = p.Id,
                Title = p.Name,
                Description = p.Description,
                Location = p.Location.Name,
                Image = p.Images.OrderByDescending(image => image.IsMain).ThenBy(image => image.SortOrder).ThenBy(image => image.CreatedAt).ThenBy(image => image.Id).Select(image => image.Url).FirstOrDefault(),
                DestinationId = p.Location.DestinationId,
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
            .OrderBy(p => p.DestinationId == destinationId ? 0 : 1)
            .ThenByDescending(p => p.ExplicitMatchCount)
            .ThenByDescending(p => p.InferredMatchCount)
            .ThenBy(p => p.DistanceMeters)
            .ThenByDescending(p => p.AverageRating ?? 0)
            .Take(candidateLimit)
            .ToListAsync();

        return candidates
            .DistinctBy(candidate => candidate.Id)
            .Select(candidate => RankCuratedCandidate(
                candidate,
                signalByTagId,
                duplicatePlaceIds.Contains(candidate.Id),
                destinationId))
            .Where(candidate => candidate.Score > 0)
            .OrderBy(candidate => candidate.IsOutsideCurrentDestination)
            .ThenByDescending(candidate => candidate.Score)
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
        bool isDuplicateWithOtherSection,
        int currentDestinationId)
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

        var categoryLabel = DisplayTextSafety.CleanLabelOrHumanizedKey(
            bestTag?.Name,
            candidate.PlaceTypeName,
            "Local place");
        var reason = bestSignal?.IsExplicit == true
            ? $"Matches your saved interest: {categoryLabel}"
            : $"Based on recent activity: {categoryLabel}";

        return new RankedCuratedPlace
        {
            Id = candidate.Id,
            Title = DisplayTextSafety.CleanRequired(candidate.Title, "Recommended place"),
            Description = candidate.Description,
            Location = DisplayTextSafety.CleanOptional(candidate.Location),
            Image = candidate.Image,
            CategoryLabel = categoryLabel,
            PrimaryTagId = bestTag?.TagId,
            Reason = reason,
            Score = score,
            DistanceMeters = candidate.DistanceMeters,
            AverageRating = candidate.AverageRating,
            ReviewCount = candidate.ReviewCount,
            OpeningHours = candidate.OpeningHours,
            MatchedTags = candidate.MatchedTags
                .Select(tag => new CuratedTagMatch
                {
                    TagId = tag.TagId,
                    Name = DisplayTextSafety.CleanRequired(tag.Name, categoryLabel)
                })
                .DistinctBy(tag => tag.TagId)
                .ToList(),
            IsDuplicateWithOtherSection = isDuplicateWithOtherSection,
            IsOutsideCurrentDestination = candidate.DestinationId != currentDestinationId
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
                Id = e.Destination.Id,
                Name = e.Destination.Name,
                Description = e.Destination.Description,
                Latitude = e.Destination.Latitude,
                Longitude = e.Destination.Longitude,
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

    private async Task<ResolvedDestinationCandidate?> ResolveDestinationByIdAsync(int destinationId, Point userPoint)
    {
        return await _db.Destinations
            .AsNoTracking()
            .Where(d =>
                d.Id == destinationId &&
                (d.Locations.Any(l => l.Places.Any()) || d.Locations.Any(l => l.Events.Any())))
            .Select(d => new ResolvedDestinationCandidate
            {
                Id = d.Id,
                Name = d.Name,
                Description = d.Description,
                Latitude = d.Latitude,
                Longitude = d.Longitude,
                DistanceMeters = EF.Property<Point?>(d, SpatialPointFactory.CoordinatesPropertyName) != null
                    ? EF.Property<Point>(d, SpatialPointFactory.CoordinatesPropertyName).Distance(userPoint)
                    : 0,
                Priority = -1
            })
            .SingleOrDefaultAsync();
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

    private static string FormatPlacesCountLabel(int count) =>
        count == 1 ? "1 place" : $"{count} places";

    private static string ToCategoryKey(string value) =>
        value.Trim().ToLowerInvariant().Replace(" ", "-");

    private static Point? BuildUserPoint(double? latitude, double? longitude)
    {
        return HasUsableCoordinates(latitude, longitude)
            ? SpatialPointFactory.Create(latitude!.Value, longitude!.Value)
            : null;
    }

    private async Task<Point?> ResolveDestinationPointAsync(int destinationId)
    {
        var destination = await _db.Destinations
            .AsNoTracking()
            .Where(d => d.Id == destinationId)
            .Select(d => new { d.Latitude, d.Longitude })
            .SingleOrDefaultAsync();

        return destination is not null && HasUsableCoordinates(destination.Latitude, destination.Longitude)
            ? SpatialPointFactory.Create(destination.Latitude, destination.Longitude)
            : null;
    }

    private static bool HasUsableCoordinates(double? latitude, double? longitude)
    {
        return latitude.HasValue &&
               longitude.HasValue &&
               !double.IsNaN(latitude.Value) &&
               !double.IsNaN(longitude.Value) &&
               !double.IsInfinity(latitude.Value) &&
               !double.IsInfinity(longitude.Value) &&
               latitude.Value >= -90 &&
               latitude.Value <= 90 &&
               longitude.Value >= -180 &&
               longitude.Value <= 180 &&
               (latitude.Value != 0d || longitude.Value != 0d);
    }

    private static ExploreDestinationDto MapExploreDestination(ResolvedDestinationCandidate destination)
    {
        return new ExploreDestinationDto
        {
            Id = destination.Id,
            Name = DisplayTextSafety.CleanRequired(destination.Name, "Nearby"),
            Description = DisplayTextSafety.CleanOptional(destination.Description) ?? string.Empty,
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


    private sealed class DynamicSectionCandidate
    {
        public int Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public string? Subtitle { get; set; }
        public string? Description { get; set; }
        public string? Location { get; set; }
        public string? Image { get; set; }
        public int DestinationId { get; set; }
        public int PlaceTypeId { get; set; }
        public string PlaceTypeName { get; set; } = string.Empty;
        public string? OpeningHours { get; set; }
        public double DistanceMeters { get; set; }
        public double? AverageRating { get; set; }
        public int ReviewCount { get; set; }
        public List<DynamicSectionTagMatch> MatchedTags { get; set; } = new();
    }

    private sealed class DynamicSectionTagMatch
    {
        public int TagId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Slug { get; set; } = string.Empty;
        public int? TagTypeId { get; set; }
        public string? TagTypeName { get; set; }
        public string? TagTypeSlug { get; set; }
        public bool CanGenerateExploreSection { get; set; }
    }

    private sealed class DynamicSectionIdentity
    {
        public int TagId { get; set; }
        public int? TagTypeId { get; set; }
        public string Key { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
    }

    private sealed class DynamicSectionAccumulator
    {
        public int? TagTypeId { get; set; }
        public string Key { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public double Score { get; set; }
        public List<ScoredDynamicSectionItem> Items { get; set; } = new();
    }

    private sealed class ScoredDynamicSectionItem
    {
        public double Score { get; set; }
        public double DistanceMeters { get; set; }
        public int ReviewCountRaw { get; set; }

        public int Id { get; set; }
        public string EntityType { get; set; } = "place";
        public string DetailRoute { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string? Subtitle { get; set; }
        public string? Description { get; set; }
        public string? CategoryLabel { get; set; }
        public string? Image { get; set; }
        public double? Rating { get; set; }
        public string? ReviewCount { get; set; }
        public string? Distance { get; set; }
        public string? OpeningHours { get; set; }
        public bool IsSaved { get; set; }
        public List<ExploreFeedSectionTagDto> Tags { get; set; } = new();
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

    private sealed class LocalCategoryProjection
    {
        public int Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public int Count { get; set; }
        public int CurrentDestinationCount { get; set; }
        public double? MinDistanceMeters { get; set; }
    }

    private sealed class CuratedPlaceCandidate
    {
        public int Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public string? Description { get; set; }
        public string? Location { get; set; }
        public string? Image { get; set; }
        public int DestinationId { get; set; }
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
        public bool IsOutsideCurrentDestination { get; set; }
    }
}

