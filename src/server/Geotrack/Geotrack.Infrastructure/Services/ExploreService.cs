using Geotrack.Domain.Entities.Core;
using Geotrack.Application.DTOs.Explore;
using Geotrack.Application.Interfaces.Service;
using Geotrack.Application.Services;
using Geotrack.Infrastructure.Spatial;
using System.Text.Json;
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
    private const int ExploreSectionItemLimit = 6;
    private const int LandmarkCategoryPreviewLimit = 8;
    private const int DynamicExploreSectionLimit = 6;
    private const int DynamicSectionCandidateLimit = 250;
    private const int DynamicSectionItemLimit = 6;
    private const int SectionPageDefaultTake = 20;
    private const int SectionPageMaxTake = 50;
    private const int SectionPageCandidateLimit = 500;
    private const double NearbyDestinationMaxDistanceMeters = 75_000d;

    private static readonly string[] WhereToEatTagSlugs =
    [
        "food-drink",
        "fine-dining"
    ];

    private static readonly string[] WhereToEatTypeNames =
    [
        "restaurant"
    ];

    private static readonly string[] WhereToEatExcludedTypeNames =
    [
        "bar"
    ];

    private static readonly string[] WhereToEatTagTypeSlugs =
    [
        "cuisine",
        "dining-style"
    ];

    private static readonly string[] DrinksTypeNames =
    [
        "cafe",
        "bar"
    ];

    private static readonly string[] DrinksTagSlugs =
    [
        "specialty-coffee",
        "wine-bar"
    ];

    private readonly AppDbContext _db;
    private readonly IFavoriteService _favoriteService;

    public ExploreService(AppDbContext db, IFavoriteService favoriteService)
    {
        _db = db;
        _favoriteService = favoriteService;
    }

    public Task<ExplorePageDto> GetExplorePageAsync(
        double latitude,
        double longitude,
        int? userId = null,
        bool useCurrentLocationDistance = false) =>
        GetExplorePageCoreAsync(latitude, longitude, userId, preferredDestinationId: null, useCurrentLocationDistance);

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
            destinationId,
            useCurrentLocationDistance: false);
    }

    public async Task<ExploreSectionPageDto> GetExploreSectionPageAsync(
        string sectionKey,
        int destinationId,
        double? latitude = null,
        double? longitude = null,
        int? userId = null,
        int skip = 0,
        int take = SectionPageDefaultTake,
        bool useCurrentLocationDistance = false,
        IReadOnlyCollection<int>? tagIds = null)
    {
        var normalizedKey = ToCategoryKey(sectionKey ?? string.Empty);
        var normalizedSkip = Math.Max(0, skip);
        var normalizedTake = Math.Clamp(take, 1, SectionPageMaxTake);
        var selectedTagIds = NormalizeSelectedTagIds(tagIds);

        var destination = await ResolveDestinationForSectionPageAsync(destinationId);
        if (destination is null)
        {
            return new ExploreSectionPageDto
            {
                Key = normalizedKey,
                Title = HumanizeSectionKey(normalizedKey),
                Skip = normalizedSkip,
                Take = normalizedTake,
                NextSkip = normalizedSkip
            };
        }

        var destinationPoint = SpatialPointFactory.Create(destination.Latitude, destination.Longitude);
        var useCurrentDistanceLabel = useCurrentLocationDistance && HasUsableCoordinates(latitude, longitude);
        var distancePoint = useCurrentDistanceLabel
            ? SpatialPointFactory.Create(latitude!.Value, longitude!.Value)
            : destinationPoint;
        var placeFavoriteIds = userId.HasValue
            ? await _favoriteService.GetFavoriteIdsAsync(userId.Value, "place")
            : new HashSet<int>();
        var eventFavoriteIds = userId.HasValue
            ? await _favoriteService.GetFavoriteIdsAsync(userId.Value, "event")
            : new HashSet<int>();

        var candidateTake = Math.Max(SectionPageCandidateLimit, normalizedSkip + normalizedTake);

        var page = normalizedKey switch
        {
            "curated-for-you" => await BuildCuratedForYouPageAsync(
                destination,
                distancePoint,
                userId,
                placeFavoriteIds,
                0,
                candidateTake,
                useCurrentDistanceLabel),

            "top-stays" or "stays" or "hotels" => await BuildTopStaysPageAsync(
                destination,
                distancePoint,
                placeFavoriteIds,
                0,
                candidateTake,
                useCurrentDistanceLabel),

            "where-to-eat" or "food" or "food-and-music" => await BuildWhereToEatPageAsync(
                destination,
                distancePoint,
                placeFavoriteIds,
                0,
                candidateTake,
                useCurrentDistanceLabel),

            "drinks" or "cafes" or "bars" or "coffee" => await BuildDrinksPageAsync(
                destination,
                distancePoint,
                placeFavoriteIds,
                0,
                candidateTake,
                useCurrentDistanceLabel),

            "events" => await BuildEventsPageAsync(
                destination,
                distancePoint,
                eventFavoriteIds,
                0,
                candidateTake,
                useCurrentDistanceLabel),

            _ => await BuildDynamicSectionPageAsync(
                normalizedKey,
                destination,
                distancePoint,
                placeFavoriteIds,
                0,
                candidateTake,
                useCurrentDistanceLabel)
        };

        return await ApplySectionFiltersAndPagingAsync(
            page,
            selectedTagIds,
            normalizedSkip,
            normalizedTake);
    }



    private async Task<ExplorePageDto> GetExplorePageCoreAsync(
        double latitude,
        double longitude,
        int? userId,
        int? preferredDestinationId,
        bool useCurrentLocationDistance)
    {
        var userPoint = SpatialPointFactory.Create(latitude, longitude);
        var resolvedDestination = preferredDestinationId.HasValue
            ? await ResolveDestinationByIdAsync(preferredDestinationId.Value, userPoint)
            : await ResolveDestinationAsync(userPoint);
        if (resolvedDestination is null)
        {
            return new ExplorePageDto();
        }

        var distancePoint = useCurrentLocationDistance
            ? userPoint
            : SpatialPointFactory.Create(resolvedDestination.Latitude, resolvedDestination.Longitude);

        var placeFavoriteIds = userId.HasValue
            ? await _favoriteService.GetFavoriteIdsAsync(userId.Value, "place")
            : new HashSet<int>();
        var curationSignals = userId.HasValue
            ? await LoadCurationSignalsAsync(userId.Value)
            : new List<CuratedInterestSignal>();

        var nowUtc = DateTime.UtcNow;

        var events = await _db.Events
            .AsNoTracking()
            .Where(e =>
                e.DestinationId == resolvedDestination.Id &&
                EF.Property<Point?>(e, SpatialPointFactory.CoordinatesPropertyName) != null)
            .Select(e => new
            {
                e.Id,
                e.Name,
                EventTypeName = e.EventType.Name,
                LocationName = e.Location != null ? e.Location.Name : e.Place != null ? e.Place.Location.Name : null,
                e.StartDate,
                e.EndDate,
                IsHappeningNow = e.StartDate <= nowUtc && e.EndDate >= nowUtc,
                IsUpcoming = e.StartDate > nowUtc,
                Image = e.Images.OrderBy(image => image.ImageRole == EntityImageRole.Card ? 0 : image.ImageRole == EntityImageRole.Cover ? 1 : 2).ThenBy(image => image.SortOrder).ThenBy(image => image.CreatedAt).ThenBy(image => image.Id).Select(image => image.Url).FirstOrDefault(),
                DistanceMeters = EF.Property<Point>(e, SpatialPointFactory.CoordinatesPropertyName).Distance(distancePoint)
            })
            .OrderBy(e => e.IsHappeningNow ? 0 : e.IsUpcoming ? 1 : 2)
            .ThenBy(e => e.IsUpcoming || e.IsHappeningNow ? e.StartDate : DateTime.MaxValue)
            .ThenByDescending(e => !e.IsUpcoming && !e.IsHappeningNow ? e.EndDate : DateTime.MinValue)
            .ThenBy(e => e.DistanceMeters)
            .Take(ExploreSectionItemLimit)
            .ToListAsync();

        var topStays = await _db.Places
            .AsNoTracking()
            .Where(p =>
                p.Stay != null &&
                p.Location.DestinationId == resolvedDestination.Id &&
                EF.Property<Point?>(p, SpatialPointFactory.CoordinatesPropertyName) != null)
            .Select(p => new
            {
                p.Id,
                p.Name,
                p.Subtitle,
                p.Description,
                CategoryLabel = p.Stay != null && p.Stay.StayType != null ? p.Stay.StayType.Name : p.PlaceType.Name,
                LocationName = p.Location.Name,
                Image = p.Images.OrderBy(image => image.ImageRole == EntityImageRole.Card ? 0 : image.ImageRole == EntityImageRole.Cover ? 1 : 2).ThenBy(image => image.SortOrder).ThenBy(image => image.CreatedAt).ThenBy(image => image.Id).Select(image => image.Url).FirstOrDefault(),
                AverageRating = p.PlaceReviews.Select(r => (double?)r.Rating).Average(),
                ReviewCount = p.PlaceReviews.Count(),
                PricePerNight = p.Stay != null ? p.Stay.PricePerNight : null,
                DistanceMeters = EF.Property<Point>(p, SpatialPointFactory.CoordinatesPropertyName).Distance(distancePoint)
            })
            .OrderBy(p => p.DistanceMeters)
            .ThenByDescending(p => p.AverageRating ?? 0)
            .Take(ExploreSectionItemLimit)
            .ToListAsync();

        var whereToEatSection = await BuildWhereToEatSectionAsync(
            resolvedDestination.Id,
            distancePoint,
            placeFavoriteIds,
            useCurrentLocationDistance);

        var drinksSection = await BuildDrinksSectionAsync(
            resolvedDestination.Id,
            distancePoint,
            placeFavoriteIds,
            useCurrentLocationDistance);

        var dynamicSections = await BuildDynamicPlaceSectionsAsync(
            resolvedDestination.Id,
            distancePoint,
            curationSignals,
            placeFavoriteIds,
            useCurrentLocationDistance);

        var duplicatePlaceIds = dynamicSections
            .SelectMany(section => section.Items)
            .Select(item => item.Id)
            .Concat(whereToEatSection.Items.Select(item => item.Id))
            .Concat(drinksSection.Items.Select(item => item.Id))
            .ToHashSet();

        var curatedPlaces = await BuildCuratedPlacesAsync(
            resolvedDestination.Id,
            distancePoint,
            curationSignals,
            duplicatePlaceIds);

        var sections = new List<ExploreFeedSectionDto>();

        var curatedSection = BuildCuratedForYouSection(
            resolvedDestination.Id,
            curatedPlaces,
            placeFavoriteIds,
            useCurrentLocationDistance);
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
            MoreRoute = $"/explore/top-stays?destinationId={resolvedDestination.Id}",
            Priority = 30,
            Items = topStays.Select(s => new ExploreFeedSectionItemDto
            {
                Id = s.Id,
                EntityType = "stay",
                DetailRoute = $"/stays/{s.Id}",
                Title = DisplayTextSafety.CleanRequired(s.Name, "Stay"),
                Subtitle = DisplayTextSafety.CleanOptional(s.Subtitle),
                Description = TrimDescription(s.Description, 170),
                CategoryLabel = DisplayTextSafety.CleanOptional(s.CategoryLabel),
                LocationName = DisplayTextSafety.CleanOptional(s.LocationName),
                Image = s.Image,
                Rating = s.AverageRating.HasValue ? Math.Round(s.AverageRating.Value, 1) : null,
                ReviewCount = FormatReviewCountLabel(s.ReviewCount),
                Distance = FormatDistanceLabel(s.DistanceMeters, useCurrentLocationDistance),
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

        if (drinksSection.Items.Count > 0)
        {
            sections.Add(drinksSection);
        }

        var eventsSection = new ExploreFeedSectionDto
        {
            Key = "events",
            Title = "Events",
            Subtitle = "What's happening around town",
            Layout = "horizontal-cards",
            CardType = "event-card",
            MoreRoute = $"/explore/events?destinationId={resolvedDestination.Id}",
            Priority = 20,
            Items = events.Select(e => new ExploreFeedSectionItemDto
            {
                Id = e.Id,
                EntityType = "event",
                DetailRoute = $"/events/{e.Id}",
                Title = DisplayTextSafety.CleanRequired(e.Name, "Event"),
                CategoryLabel = DisplayTextSafety.CleanOptional(e.EventTypeName),
                LocationName = DisplayTextSafety.CleanOptional(e.LocationName),
                Subtitle = null,
                StatusBadge = BuildEventStatusBadge(e.IsHappeningNow, e.IsUpcoming),
                DateLabel = FormatDateLabel(e.StartDate, e.EndDate),
                Image = e.Image,
                Distance = FormatDistanceLabel(e.DistanceMeters, useCurrentLocationDistance)
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
                Longitude = resolvedDestination.Longitude,
                TerritoryId = resolvedDestination.TerritoryId,
                TerritoryName = resolvedDestination.TerritoryName
            },
            Sections = sections
                .Where(section => section.Items.Count > 0)
                .OrderBy(section => section.Priority)
                .ThenBy(section => section.Title)
                .ToList()
        };
    }

    



    




    private async Task<ExploreSectionPageDto> BuildCuratedForYouPageAsync(
        ResolvedDestinationCandidate destination,
        Point destinationPoint,
        int? userId,
        ISet<int> placeFavoriteIds,
        int skip,
        int take,
        bool useCurrentLocationDistance)
    {
        if (!userId.HasValue)
        {
            return EmptySectionPage(
                destination,
                "curated-for-you",
                "Curated for You",
                "Based on your interests and recent activity nearby",
                "place-card",
                skip,
                take,
                "Sign in and save interests to get personal recommendations.");
        }

        var curationSignals = await LoadCurationSignalsAsync(userId.Value);
        if (curationSignals.Count == 0)
        {
            return EmptySectionPage(
                destination,
                "curated-for-you",
                "Curated for You",
                "Based on your interests and recent activity nearby",
                "place-card",
                skip,
                take,
                "Add interests or keep exploring places so GeoTrack can build recommendations for you.");
        }

        var rankedPlaces = await BuildRankedCuratedPlacesAsync(
            destination.Id,
            destinationPoint,
            curationSignals,
            duplicatePlaceIds: new HashSet<int>(),
            candidateLimit: SectionPageCandidateLimit);

        var pageItems = rankedPlaces
            .Skip(skip)
            .Take(take)
            .Select(place => MapRankedCuratedPlace(place, placeFavoriteIds, useCurrentLocationDistance))
            .ToList();

        return SectionPage(
            destination,
            "curated-for-you",
            "Curated for You",
            "Based on your interests and recent activity nearby",
            "place-card",
            pageItems,
            rankedPlaces.Count,
            skip,
            take);
    }

    private async Task<ExploreSectionPageDto> BuildTopStaysPageAsync(
        ResolvedDestinationCandidate destination,
        Point destinationPoint,
        ISet<int> placeFavoriteIds,
        int skip,
        int take,
        bool useCurrentLocationDistance)
    {
        var query = _db.Places
            .AsNoTracking()
            .Where(p =>
                p.Stay != null &&
                p.Location.DestinationId == destination.Id &&
                EF.Property<Point?>(p, SpatialPointFactory.CoordinatesPropertyName) != null)
            .Select(p => new SectionPlaceProjection
            {
                Id = p.Id,
                Title = p.Name,
                Subtitle = p.Subtitle,
                Description = p.Description,
                CategoryLabel = p.Stay != null && p.Stay.StayType != null ? p.Stay.StayType.Name : p.PlaceType.Name,
                LocationName = p.Location.Name,
                OpeningHours = p.OpeningHours,
                OperatingHoursEnabled = p.OperatingHoursEnabled,
                OperatingHoursJson = p.OperatingHoursJson,
                Image = p.Images
                    .OrderBy(image => image.ImageRole == EntityImageRole.Card ? 0 : image.ImageRole == EntityImageRole.Cover ? 1 : 2)
                    .ThenBy(image => image.SortOrder)
                    .ThenBy(image => image.CreatedAt)
                    .ThenBy(image => image.Id)
                    .Select(image => image.Url)
                    .FirstOrDefault(),
                AverageRating = p.PlaceReviews.Select(r => (double?)r.Rating).Average(),
                ReviewCount = p.PlaceReviews.Count(),
                PricePerNight = p.Stay != null ? p.Stay.PricePerNight : null,
                DistanceMeters = EF.Property<Point>(p, SpatialPointFactory.CoordinatesPropertyName).Distance(destinationPoint),
                Tags = p.PlaceTags
                    .Where(pt => pt.Tag.ForPlace)
                    .Select(pt => new ExploreFeedSectionTagDto
                    {
                        TagId = pt.TagId,
                        Name = pt.Tag.Name,
                        Slug = pt.Tag.Slug
                    })
                    .ToList()
            });

        var totalCount = await query.CountAsync();
        var items = await query
            .OrderBy(p => p.DistanceMeters)
            .ThenByDescending(p => p.AverageRating ?? 0)
            .ThenByDescending(p => p.ReviewCount)
            .ThenBy(p => p.Title)
            .Skip(skip)
            .Take(take)
            .ToListAsync();

        return SectionPage(
            destination,
            "top-stays",
            "Top Stays",
            "Highly rated accommodation nearby",
            "stay-card",
            items.Select(item => MapPlaceProjection(item, "stay", "stay-card", placeFavoriteIds, useCurrentLocationDistance)).ToList(),
            totalCount,
            skip,
            take);
    }

    private async Task<ExploreSectionPageDto> BuildWhereToEatPageAsync(
        ResolvedDestinationCandidate destination,
        Point destinationPoint,
        ISet<int> placeFavoriteIds,
        int skip,
        int take,
        bool useCurrentLocationDistance)
    {
        var query = _db.Places
            .AsNoTracking()
            .Where(p =>
                p.Stay == null &&
                p.Location.DestinationId == destination.Id &&
                !WhereToEatExcludedTypeNames.Contains(p.PlaceType.Name.ToLower()) &&
                EF.Property<Point?>(p, SpatialPointFactory.CoordinatesPropertyName) != null &&
                (
                    WhereToEatTypeNames.Contains(p.PlaceType.Name.ToLower()) ||
                    p.PlaceTags.Any(pt =>
                        pt.Tag.ForPlace &&
                        (
                            WhereToEatTagSlugs.Contains(pt.Tag.Slug) ||
                            pt.Tag.TagType != null &&
                            WhereToEatTagTypeSlugs.Contains(pt.Tag.TagType.Slug)
                        ))
                ))
            .Select(p => new SectionPlaceProjection
            {
                Id = p.Id,
                Title = p.Name,
                Subtitle = p.Subtitle,
                Description = p.Description,
                CategoryLabel = p.PlaceType.Name,
                LocationName = p.Location.Name,
                OpeningHours = p.OpeningHours,
                OperatingHoursEnabled = p.OperatingHoursEnabled,
                OperatingHoursJson = p.OperatingHoursJson,
                Image = p.Images
                    .OrderBy(image => image.ImageRole == EntityImageRole.Card ? 0 : image.ImageRole == EntityImageRole.Cover ? 1 : 2)
                    .ThenBy(image => image.SortOrder)
                    .ThenBy(image => image.CreatedAt)
                    .ThenBy(image => image.Id)
                    .Select(image => image.Url)
                    .FirstOrDefault(),
                AverageRating = p.PlaceReviews.Select(r => (double?)r.Rating).Average(),
                ReviewCount = p.PlaceReviews.Count(),
                DistanceMeters = EF.Property<Point>(p, SpatialPointFactory.CoordinatesPropertyName).Distance(destinationPoint),
                Tags = p.PlaceTags
                    .Where(pt => pt.Tag.ForPlace)
                    .Select(pt => new ExploreFeedSectionTagDto
                    {
                        TagId = pt.TagId,
                        Name = pt.Tag.Name,
                        Slug = pt.Tag.Slug
                    })
                    .ToList()
            });

        var totalCount = await query.CountAsync();

        var items = await query
            .OrderBy(p => p.DistanceMeters)
            .ThenByDescending(p => p.AverageRating ?? 0)
            .ThenByDescending(p => p.ReviewCount)
            .ThenBy(p => p.Title)
            .Skip(skip)
            .Take(take)
            .ToListAsync();

        return SectionPage(
            destination,
            "where-to-eat",
            "Where to eat",
            "Restaurants and local food nearby",
            "place-card",
            items.Select(item => MapPlaceProjection(item, "place", "place-card", placeFavoriteIds, useCurrentLocationDistance)).ToList(),
            totalCount,
            skip,
            take);
    }

    private async Task<ExploreSectionPageDto> BuildDrinksPageAsync(
        ResolvedDestinationCandidate destination,
        Point destinationPoint,
        ISet<int> placeFavoriteIds,
        int skip,
        int take,
        bool useCurrentLocationDistance)
    {
        var query = _db.Places
            .AsNoTracking()
            .Where(p =>
                p.Stay == null &&
                p.Location.DestinationId == destination.Id &&
                EF.Property<Point?>(p, SpatialPointFactory.CoordinatesPropertyName) != null &&
                (
                    DrinksTypeNames.Contains(p.PlaceType.Name.ToLower()) ||
                    p.PlaceTags.Any(pt =>
                        pt.Tag.ForPlace &&
                        DrinksTagSlugs.Contains(pt.Tag.Slug))
                ))
            .Select(p => new SectionPlaceProjection
            {
                Id = p.Id,
                Title = p.Name,
                Subtitle = p.Subtitle,
                Description = p.Description,
                CategoryLabel = p.PlaceType.Name,
                LocationName = p.Location.Name,
                OpeningHours = p.OpeningHours,
                OperatingHoursEnabled = p.OperatingHoursEnabled,
                OperatingHoursJson = p.OperatingHoursJson,
                Image = p.Images
                    .OrderBy(image => image.ImageRole == EntityImageRole.Card ? 0 : image.ImageRole == EntityImageRole.Cover ? 1 : 2)
                    .ThenBy(image => image.SortOrder)
                    .ThenBy(image => image.CreatedAt)
                    .ThenBy(image => image.Id)
                    .Select(image => image.Url)
                    .FirstOrDefault(),
                AverageRating = p.PlaceReviews.Select(r => (double?)r.Rating).Average(),
                ReviewCount = p.PlaceReviews.Count(),
                DistanceMeters = EF.Property<Point>(p, SpatialPointFactory.CoordinatesPropertyName).Distance(destinationPoint),
                Tags = p.PlaceTags
                    .Where(pt => pt.Tag.ForPlace)
                    .Select(pt => new ExploreFeedSectionTagDto
                    {
                        TagId = pt.TagId,
                        Name = pt.Tag.Name,
                        Slug = pt.Tag.Slug
                    })
                    .ToList()
            });

        var totalCount = await query.CountAsync();

        var items = await query
            .OrderBy(p => p.DistanceMeters)
            .ThenByDescending(p => p.AverageRating ?? 0)
            .ThenByDescending(p => p.ReviewCount)
            .ThenBy(p => p.Title)
            .Skip(skip)
            .Take(take)
            .ToListAsync();

        return SectionPage(
            destination,
            "drinks",
            "Drinks & coffee",
            "Cafes, bars and places for drinks nearby",
            "place-card",
            items.Select(item => MapPlaceProjection(item, "place", "place-card", placeFavoriteIds, useCurrentLocationDistance)).ToList(),
            totalCount,
            skip,
            take);
    }

    private async Task<ExploreSectionPageDto> BuildEventsPageAsync(
        ResolvedDestinationCandidate destination,
        Point destinationPoint,
        ISet<int> eventFavoriteIds,
        int skip,
        int take,
        bool useCurrentLocationDistance)
    {
        var nowUtc = DateTime.UtcNow;

        var query = _db.Events
            .AsNoTracking()
            .Where(e =>
                e.DestinationId == destination.Id &&
                EF.Property<Point?>(e, SpatialPointFactory.CoordinatesPropertyName) != null)
            .Select(e => new SectionEventProjection
            {
                Id = e.Id,
                Title = e.Name,
                Subtitle = e.EventType.Name,
                LocationName = e.Location != null ? e.Location.Name : e.Place != null ? e.Place.Location.Name : null,
                Description = e.Description,
                StartDate = e.StartDate,
                EndDate = e.EndDate,
                IsHappeningNow = e.StartDate <= nowUtc && e.EndDate >= nowUtc,
                IsUpcoming = e.StartDate > nowUtc,
                Image = e.Images
                    .OrderBy(image => image.ImageRole == EntityImageRole.Card ? 0 : image.ImageRole == EntityImageRole.Cover ? 1 : 2)
                    .ThenBy(image => image.SortOrder)
                    .ThenBy(image => image.CreatedAt)
                    .ThenBy(image => image.Id)
                    .Select(image => image.Url)
                    .FirstOrDefault(),
                DistanceMeters = EF.Property<Point>(e, SpatialPointFactory.CoordinatesPropertyName).Distance(destinationPoint),
                Tags = e.EventTags
                    .Where(et => et.Tag.ForEvent)
                    .Select(et => new ExploreFeedSectionTagDto
                    {
                        TagId = et.TagId,
                        Name = et.Tag.Name,
                        Slug = et.Tag.Slug
                    })
                    .ToList()
            });

        var totalCount = await query.CountAsync();
        var items = await query
            .OrderBy(e => e.IsHappeningNow ? 0 : e.IsUpcoming ? 1 : 2)
            .ThenBy(e => e.IsUpcoming || e.IsHappeningNow ? e.StartDate : DateTime.MaxValue)
            .ThenByDescending(e => !e.IsUpcoming && !e.IsHappeningNow ? e.EndDate : DateTime.MinValue)
            .ThenBy(e => e.DistanceMeters)
            .ThenBy(e => e.Title)
            .Skip(skip)
            .Take(take)
            .ToListAsync();

        return SectionPage(
            destination,
            "events",
            "Events",
            "What's happening around town",
            "event-card",
            items.Select(item => MapEventProjection(item, eventFavoriteIds, useCurrentLocationDistance)).ToList(),
            totalCount,
            skip,
            take);
    }

    private async Task<ExploreSectionPageDto> BuildDynamicSectionPageAsync(
        string sectionKey,
        ResolvedDestinationCandidate destination,
        Point destinationPoint,
        ISet<int> placeFavoriteIds,
        int skip,
        int take,
        bool useCurrentLocationDistance)
    {
        var possibleTags = await _db.Tags
            .AsNoTracking()
            .Where(t => t.ForPlace && t.CanGenerateExploreSection)
            .Select(t => new
            {
                t.Id,
                t.Name,
                t.Slug,
                t.TagTypeId
            })
            .ToListAsync();

        var matchedTags = possibleTags
            .Where(tag =>
                string.Equals(ToCategoryKey(tag.Slug), sectionKey, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(ToCategoryKey(tag.Name), sectionKey, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (matchedTags.Count == 0)
        {
            return new ExploreSectionPageDto
            {
                IsKnownSection = false,
                SelectedDestination = MapExploreDestination(destination),
                Key = sectionKey,
                Title = HumanizeSectionKey(sectionKey),
                Skip = skip,
                Take = take,
                NextSkip = skip
            };
        }

        var tagIds = matchedTags.Select(tag => tag.Id).ToList();
        var title = DisplayTextSafety.CleanRequired(matchedTags.First().Name, HumanizeSectionKey(sectionKey));

        var query = _db.Places
            .AsNoTracking()
            .Where(p =>
                p.Stay == null &&
                p.Location.DestinationId == destination.Id &&
                EF.Property<Point?>(p, SpatialPointFactory.CoordinatesPropertyName) != null &&
                p.PlaceTags.Any(pt => tagIds.Contains(pt.TagId)))
            .Select(p => new SectionPlaceProjection
            {
                Id = p.Id,
                Title = p.Name,
                Subtitle = p.Subtitle,
                Description = p.Description,
                CategoryLabel = p.PlaceType.Name,
                LocationName = p.Location.Name,
                OpeningHours = p.OpeningHours,
                OperatingHoursEnabled = p.OperatingHoursEnabled,
                OperatingHoursJson = p.OperatingHoursJson,
                Image = p.Images
                    .OrderBy(image => image.ImageRole == EntityImageRole.Card ? 0 : image.ImageRole == EntityImageRole.Cover ? 1 : 2)
                    .ThenBy(image => image.SortOrder)
                    .ThenBy(image => image.CreatedAt)
                    .ThenBy(image => image.Id)
                    .Select(image => image.Url)
                    .FirstOrDefault(),
                AverageRating = p.PlaceReviews.Select(r => (double?)r.Rating).Average(),
                ReviewCount = p.PlaceReviews.Count(),
                DistanceMeters = EF.Property<Point>(p, SpatialPointFactory.CoordinatesPropertyName).Distance(destinationPoint),
                Tags = p.PlaceTags
                    .Where(pt => pt.Tag.ForPlace)
                    .Select(pt => new ExploreFeedSectionTagDto
                    {
                        TagId = pt.TagId,
                        Name = pt.Tag.Name,
                        Slug = pt.Tag.Slug
                    })
                    .ToList()
            });

        var totalCount = await query.CountAsync();
        var items = await query
            .OrderBy(p => p.DistanceMeters)
            .ThenByDescending(p => p.AverageRating ?? 0)
            .ThenByDescending(p => p.ReviewCount)
            .ThenBy(p => p.Title)
            .Skip(skip)
            .Take(take)
            .ToListAsync();

        return SectionPage(
            destination,
            sectionKey,
            title,
            totalCount == 1 ? "1 matching place" : $"{totalCount} matching places",
            "place-card",
            items.Select(item => MapPlaceProjection(item, "place", "place-card", placeFavoriteIds, useCurrentLocationDistance)).ToList(),
            totalCount,
            skip,
            take);
    }

    private async Task<ExploreSectionPageDto> ApplySectionFiltersAndPagingAsync(
        ExploreSectionPageDto page,
        IReadOnlyCollection<int> selectedTagIds,
        int skip,
        int take)
    {
        var allItems = page.Items;
        var filters = await BuildSectionFilterGroupsAsync(allItems);
        var filteredItems = ApplySelectedSectionTagFilters(allItems, selectedTagIds, filters);
        var filteredTotalCount = selectedTagIds.Count > 0 ? filteredItems.Count : page.TotalCount;
        var pageItems = filteredItems
            .Skip(skip)
            .Take(take)
            .ToList();

        page.Filters = filters;
        page.Items = pageItems;
        page.Skip = skip;
        page.Take = take;
        page.NextSkip = skip + pageItems.Count;
        page.TotalCount = filteredTotalCount;
        page.HasMore = skip + pageItems.Count < filteredTotalCount;

        return page;
    }

    private async Task<List<ExploreSectionFilterGroupDto>> BuildSectionFilterGroupsAsync(
        IReadOnlyCollection<ExploreFeedSectionItemDto> items)
    {
        if (items.Count == 0)
        {
            return [];
        }

        var tagIds = items
            .SelectMany(item => item.Tags)
            .Select(tag => tag.TagId)
            .Distinct()
            .ToList();

        if (tagIds.Count == 0)
        {
            return [];
        }

        var tagMetadata = await _db.Tags
            .AsNoTracking()
            .Where(tag => tagIds.Contains(tag.Id) && tag.TagType != null)
            .Select(tag => new SectionTagMetadata
            {
                TagId = tag.Id,
                TagName = tag.Name,
                TagSlug = tag.Slug,
                TagTypeId = tag.TagTypeId ?? 0,
                TagTypeName = tag.TagType != null ? tag.TagType.Name : "Tags",
                TagTypeSlug = tag.TagType != null ? tag.TagType.Slug : "tags",
                TagTypeLabel = tag.TagType != null && tag.TagType.FilterLabel != null
                    ? tag.TagType.FilterLabel
                    : tag.TagType != null
                        ? tag.TagType.Name
                        : "Tags",
                TagTypeOrder = tag.TagType != null ? tag.TagType.FilterOrder : 0
            })
            .Where(tag => tag.TagTypeId > 0)
            .ToListAsync();

        if (tagMetadata.Count == 0)
        {
            return [];
        }

        var tagLookup = tagMetadata.ToDictionary(tag => tag.TagId);
        var itemTags = items
            .Select((item, index) => new
            {
                ItemKey = $"{item.EntityType}:{item.Id}:{index}",
                TagIds = item.Tags
                    .Select(tag => tag.TagId)
                    .Where(tagLookup.ContainsKey)
                    .Distinct()
                    .ToList()
            })
            .Where(item => item.TagIds.Count > 0)
            .ToList();

        return tagMetadata
            .GroupBy(tag => new
            {
                tag.TagTypeId,
                tag.TagTypeName,
                tag.TagTypeSlug,
                tag.TagTypeLabel,
                tag.TagTypeOrder
            })
            .Select(group => new ExploreSectionFilterGroupDto
            {
                TagTypeId = group.Key.TagTypeId,
                Name = group.Key.TagTypeName,
                Slug = group.Key.TagTypeSlug,
                Label = group.Key.TagTypeLabel,
                Order = group.Key.TagTypeOrder,
                Options = group
                    .GroupBy(tag => new { tag.TagId, tag.TagName, tag.TagSlug })
                    .Select(option => new ExploreSectionFilterOptionDto
                    {
                        TagId = option.Key.TagId,
                        Name = option.Key.TagName,
                        Slug = option.Key.TagSlug,
                        Count = itemTags.Count(item => item.TagIds.Contains(option.Key.TagId))
                    })
                    .Where(option => option.Count > 0)
                    .OrderByDescending(option => option.Count)
                    .ThenBy(option => option.Name)
                    .ToList()
            })
            .Where(group => group.Options.Count > 0)
            .OrderBy(group => group.Order == 0 ? int.MaxValue : group.Order)
            .ThenBy(group => group.Label)
            .ToList();
    }

    private static List<ExploreFeedSectionItemDto> ApplySelectedSectionTagFilters(
        IReadOnlyCollection<ExploreFeedSectionItemDto> items,
        IReadOnlyCollection<int> selectedTagIds,
        IReadOnlyCollection<ExploreSectionFilterGroupDto> filters)
    {
        if (selectedTagIds.Count == 0)
        {
            return items.ToList();
        }

        var selectedTagSet = selectedTagIds.ToHashSet();
        var selectedGroups = filters
            .Select(group => new
            {
                Group = group,
                TagIds = group.Options
                    .Select(option => option.TagId)
                    .Where(selectedTagSet.Contains)
                    .ToHashSet()
            })
            .Where(group => group.TagIds.Count > 0)
            .ToList();

        if (selectedGroups.Count == 0)
        {
            return [];
        }

        return items
            .Where(item =>
            {
                var itemTagIds = item.Tags.Select(tag => tag.TagId).ToHashSet();
                return selectedGroups.All(group => itemTagIds.Overlaps(group.TagIds));
            })
            .ToList();
    }

    private static IReadOnlyCollection<int> NormalizeSelectedTagIds(IReadOnlyCollection<int>? tagIds)
    {
        return tagIds is null
            ? []
            : tagIds
                .Where(id => id > 0)
                .Distinct()
                .ToList();
    }

    private async Task<ResolvedDestinationCandidate?> ResolveDestinationForSectionPageAsync(int destinationId)
    {
        return await _db.Destinations
            .AsNoTracking()
            .Where(d => d.Id == destinationId)
            .Select(d => new ResolvedDestinationCandidate
            {
                Id = d.Id,
                Name = d.Name,
                Description = d.Description,
                Latitude = d.Latitude,
                Longitude = d.Longitude,
                DistanceMeters = 0,
                Priority = -1,
                TerritoryId = d.Region.TerritoryId,
                TerritoryName = d.Region.Territory.Name
            })
            .SingleOrDefaultAsync();
    }

    private static ExploreSectionPageDto EmptySectionPage(
        ResolvedDestinationCandidate destination,
        string key,
        string title,
        string? subtitle,
        string cardType,
        int skip,
        int take,
        string? emptyReason)
    {
        return SectionPage(
            destination,
            key,
            title,
            subtitle,
            cardType,
            [],
            0,
            skip,
            take,
            emptyReason);
    }

    private static ExploreSectionPageDto SectionPage(
        ResolvedDestinationCandidate destination,
        string key,
        string title,
        string? subtitle,
        string cardType,
        List<ExploreFeedSectionItemDto> items,
        int totalCount,
        int skip,
        int take,
        string? emptyReason = null)
    {
        return new ExploreSectionPageDto
        {
            IsKnownSection = true,
            SelectedDestination = MapExploreDestination(destination),
            Key = key,
            Title = DisplayTextSafety.CleanRequired(title, "Explore"),
            Subtitle = DisplayTextSafety.CleanOptional(subtitle),
            CardType = cardType,
            Items = items,
            Skip = skip,
            Take = take,
            NextSkip = skip + items.Count,
            HasMore = skip + items.Count < totalCount,
            TotalCount = totalCount,
            EmptyReason = emptyReason
        };
    }

    private static ExploreFeedSectionItemDto MapPlaceProjection(
        SectionPlaceProjection item,
        string entityType,
        string cardType,
        ISet<int> placeFavoriteIds,
        bool useCurrentLocationDistance = false)
    {
        return new ExploreFeedSectionItemDto
        {
            Id = item.Id,
            EntityType = entityType,
            DetailRoute = entityType == "stay" ? $"/stays/{item.Id}" : $"/places/{item.Id}",
            Title = DisplayTextSafety.CleanRequired(item.Title, entityType == "stay" ? "Stay" : "Place"),
            Subtitle = DisplayTextSafety.CleanOptional(item.Subtitle),
            Description = TrimDescription(item.Description, 170),
            CategoryLabel = DisplayTextSafety.CleanOptional(item.CategoryLabel),
            LocationName = DisplayTextSafety.CleanOptional(item.LocationName),
            Image = item.Image,
            Rating = item.AverageRating.HasValue ? Math.Round(item.AverageRating.Value, 1) : null,
            ReviewCount = FormatReviewCountLabel(item.ReviewCount),
            Distance = FormatDistanceLabel(item.DistanceMeters, useCurrentLocationDistance),
            OpeningHours = item.OpeningHours,
            StatusBadge = cardType == "stay-card" ? null : BuildPlaceStatusBadge(item.OperatingHoursEnabled, item.OperatingHoursJson),
            PricePerNight = cardType == "stay-card" ? item.PricePerNight : null,
            IsSaved = placeFavoriteIds.Contains(item.Id),
            Tags = item.Tags
        };
    }

    private static ExploreFeedSectionItemDto MapEventProjection(
        SectionEventProjection item,
        ISet<int> eventFavoriteIds,
        bool useCurrentLocationDistance = false)
    {
        return new ExploreFeedSectionItemDto
        {
            Id = item.Id,
            EntityType = "event",
            DetailRoute = $"/events/{item.Id}",
            Title = DisplayTextSafety.CleanRequired(item.Title, "Event"),
            CategoryLabel = DisplayTextSafety.CleanOptional(item.Subtitle),
            LocationName = DisplayTextSafety.CleanOptional(item.LocationName),
            Subtitle = null,
            StatusBadge = BuildEventStatusBadge(item.IsHappeningNow, item.IsUpcoming),
            Description = TrimDescription(item.Description, 170),
            Image = item.Image,
            Distance = FormatDistanceLabel(item.DistanceMeters, useCurrentLocationDistance),
            DateLabel = FormatDateLabel(item.StartDate, item.EndDate),
            IsSaved = eventFavoriteIds.Contains(item.Id),
            Tags = item.Tags
        };
    }

    private static ExploreFeedSectionItemDto MapRankedCuratedPlace(
        RankedCuratedPlace place,
        ISet<int> placeFavoriteIds,
        bool useCurrentLocationDistance = false)
    {
        return new ExploreFeedSectionItemDto
        {
            Id = place.Id,
            EntityType = "place",
            DetailRoute = $"/places/{place.Id}",
            Title = place.Title,
            Subtitle = place.Reason,
            Description = TrimDescription(place.Description, 170),
            CategoryLabel = place.CategoryLabel,
            LocationName = place.Location,
            Image = place.Image,
            Rating = place.AverageRating.HasValue ? Math.Round(place.AverageRating.Value, 1) : null,
            ReviewCount = FormatReviewCountLabel(place.ReviewCount),
            Distance = FormatDistanceLabel(place.DistanceMeters, useCurrentLocationDistance),
            OpeningHours = place.OpeningHours,
            StatusBadge = BuildPlaceStatusBadge(place.OperatingHoursEnabled, place.OperatingHoursJson),
            IsSaved = placeFavoriteIds.Contains(place.Id),
            Tags = place.MatchedTags.Select(tag => new ExploreFeedSectionTagDto
            {
                TagId = tag.TagId,
                Name = tag.Name,
                Slug = ToCategoryKey(tag.Name)
            }).ToList()
        };
    }

    private static ExploreFeedItemStatusBadgeDto? BuildEventStatusBadge(bool isHappeningNow, bool isUpcoming)
    {
        if (isHappeningNow)
        {
            return new ExploreFeedItemStatusBadgeDto
            {
                Label = "Happening now",
                Tone = "success"
            };
        }

        if (isUpcoming)
        {
            return new ExploreFeedItemStatusBadgeDto
            {
                Label = "Upcoming",
                Tone = "warning"
            };
        }

        return new ExploreFeedItemStatusBadgeDto
        {
            Label = "Past",
            Tone = "muted"
        };
    }

    private static ExploreFeedItemStatusBadgeDto? BuildPlaceStatusBadge(
        bool operatingHoursEnabled,
        string? operatingHoursJson)
    {
        if (!operatingHoursEnabled || string.IsNullOrWhiteSpace(operatingHoursJson))
        {
            return null;
        }

        try
        {
            var hours = JsonSerializer.Deserialize<List<OperatingHourEntry>>(
                operatingHoursJson,
                new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

            if (hours is null || hours.Count == 0)
            {
                return null;
            }

            var today = DateTime.UtcNow.DayOfWeek.ToString();
            var todayHours = hours.FirstOrDefault(hour =>
                string.Equals(hour.DayOfWeek, today, StringComparison.OrdinalIgnoreCase));

            if (todayHours is null)
            {
                return null;
            }

            if (!todayHours.IsOpen)
            {
                return new ExploreFeedItemStatusBadgeDto
                {
                    Label = "Closed",
                    Tone = "muted"
                };
            }

            if (!TimeSpan.TryParse(todayHours.OpenTime, out var openTime) ||
                !TimeSpan.TryParse(todayHours.CloseTime, out var closeTime))
            {
                return null;
            }

            var now = DateTime.UtcNow.TimeOfDay;
            var isOpen = now >= openTime && now < closeTime;

            return new ExploreFeedItemStatusBadgeDto
            {
                Label = isOpen ? "Open" : "Closed",
                Tone = isOpen ? "success" : "muted"
            };
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string HumanizeSectionKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return "Explore";
        }

        return string.Join(' ', key
                .Split('-', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                .Select(part => char.ToUpperInvariant(part[0]) + part[1..]));
    }

    private static ExploreFeedSectionDto BuildCuratedForYouSection(
        int destinationId,
        IReadOnlyCollection<RankedCuratedPlace> curatedPlaces,
        ISet<int> placeFavoriteIds,
        bool useCurrentLocationDistance)
    {
        return new ExploreFeedSectionDto
        {
            Key = "curated-for-you",
            Title = "Curated for You",
            Subtitle = "Based on your interests and recent activity nearby",
            Layout = "horizontal-cards",
            CardType = "place-card",
            MoreRoute = $"/explore/curated-for-you?destinationId={destinationId}",
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
                LocationName = p.Location,
                Image = p.Image,
                Rating = p.AverageRating.HasValue ? Math.Round(p.AverageRating.Value, 1) : null,
                ReviewCount = FormatReviewCountLabel(p.ReviewCount),
                Distance = FormatDistanceLabel(p.DistanceMeters, useCurrentLocationDistance),
                OpeningHours = p.OpeningHours,
                StatusBadge = BuildPlaceStatusBadge(p.OperatingHoursEnabled, p.OperatingHoursJson),
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
      Point distancePoint,
      ISet<int> placeFavoriteIds,
      bool useCurrentLocationDistance)
    {
        var items = await _db.Places
            .AsNoTracking()
            .Where(p =>
                p.Stay == null &&
                p.Location.DestinationId == destinationId &&
                !WhereToEatExcludedTypeNames.Contains(p.PlaceType.Name.ToLower()) &&
                EF.Property<Point?>(p, SpatialPointFactory.CoordinatesPropertyName) != null &&
                (
                    WhereToEatTypeNames.Contains(p.PlaceType.Name.ToLower()) ||
                    p.PlaceTags.Any(pt =>
                        pt.Tag.ForPlace &&
                        (
                            WhereToEatTagSlugs.Contains(pt.Tag.Slug) ||
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
                LocationName = p.Location.Name,
                p.OpeningHours,
                p.OperatingHoursEnabled,
                p.OperatingHoursJson,
                Image = p.Images
                    .OrderBy(image => image.ImageRole == EntityImageRole.Card ? 0 : image.ImageRole == EntityImageRole.Cover ? 1 : 2)
                    .ThenBy(image => image.SortOrder)
                    .ThenBy(image => image.CreatedAt)
                    .ThenBy(image => image.Id)
                    .Select(image => image.Url)
                    .FirstOrDefault(),
                AverageRating = p.PlaceReviews.Select(r => (double?)r.Rating).Average(),
                ReviewCount = p.PlaceReviews.Count(),
                DistanceMeters = EF.Property<Point>(p, SpatialPointFactory.CoordinatesPropertyName).Distance(distancePoint),
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
            .OrderBy(p => p.DistanceMeters)
            .ThenByDescending(p => p.AverageRating ?? 0)
            .ThenByDescending(p => p.ReviewCount)
            .Take(ExploreSectionItemLimit)
            .ToListAsync();

        return new ExploreFeedSectionDto
        {
            Key = "where-to-eat",
            Title = "Where to eat",
            Subtitle = "Restaurants and local food nearby",
            Layout = "horizontal-cards",
            CardType = "place-card",
            MoreRoute = $"/explore/where-to-eat?destinationId={destinationId}",
            Priority = 40,
            Items = items.Select(p => new ExploreFeedSectionItemDto
            {
                Id = p.Id,
                EntityType = "place",
                DetailRoute = $"/places/{p.Id}",
                Title = DisplayTextSafety.CleanRequired(p.Name, "Place"),
                Subtitle = DisplayTextSafety.CleanOptional(p.Subtitle),
                Description = TrimDescription(p.Description, 130),
                CategoryLabel = DisplayTextSafety.CleanOptional(p.PlaceTypeName),
                LocationName = DisplayTextSafety.CleanOptional(p.LocationName),
                Image = p.Image,
                Rating = p.AverageRating.HasValue ? Math.Round(p.AverageRating.Value, 1) : null,
                ReviewCount = FormatReviewCountLabel(p.ReviewCount),
                Distance = FormatDistanceLabel(p.DistanceMeters, useCurrentLocationDistance),
                OpeningHours = p.OpeningHours,
                StatusBadge = BuildPlaceStatusBadge(p.OperatingHoursEnabled, p.OperatingHoursJson),
                IsSaved = placeFavoriteIds.Contains(p.Id),
                Tags = p.Tags
            }).ToList()
        };
    }

    private async Task<ExploreFeedSectionDto> BuildDrinksSectionAsync(
        int destinationId,
        Point distancePoint,
        ISet<int> placeFavoriteIds,
        bool useCurrentLocationDistance)
    {
        var items = await _db.Places
            .AsNoTracking()
            .Where(p =>
                p.Stay == null &&
                p.Location.DestinationId == destinationId &&
                EF.Property<Point?>(p, SpatialPointFactory.CoordinatesPropertyName) != null &&
                (
                    DrinksTypeNames.Contains(p.PlaceType.Name.ToLower()) ||
                    p.PlaceTags.Any(pt =>
                        pt.Tag.ForPlace &&
                        DrinksTagSlugs.Contains(pt.Tag.Slug))
                ))
            .Select(p => new
            {
                p.Id,
                p.Name,
                p.Subtitle,
                p.Description,
                PlaceTypeName = p.PlaceType.Name,
                LocationName = p.Location.Name,
                p.OpeningHours,
                p.OperatingHoursEnabled,
                p.OperatingHoursJson,
                Image = p.Images
                    .OrderBy(image => image.ImageRole == EntityImageRole.Card ? 0 : image.ImageRole == EntityImageRole.Cover ? 1 : 2)
                    .ThenBy(image => image.SortOrder)
                    .ThenBy(image => image.CreatedAt)
                    .ThenBy(image => image.Id)
                    .Select(image => image.Url)
                    .FirstOrDefault(),
                AverageRating = p.PlaceReviews.Select(r => (double?)r.Rating).Average(),
                ReviewCount = p.PlaceReviews.Count(),
                DistanceMeters = EF.Property<Point>(p, SpatialPointFactory.CoordinatesPropertyName).Distance(distancePoint),
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
            .OrderBy(p => p.DistanceMeters)
            .ThenByDescending(p => p.AverageRating ?? 0)
            .ThenByDescending(p => p.ReviewCount)
            .Take(ExploreSectionItemLimit)
            .ToListAsync();

        return new ExploreFeedSectionDto
        {
            Key = "drinks",
            Title = "Drinks & coffee",
            Subtitle = "Cafes, bars and places for drinks nearby",
            Layout = "horizontal-cards",
            CardType = "place-card",
            MoreRoute = $"/explore/drinks?destinationId={destinationId}",
            Priority = 50,
            Items = items.Select(p => new ExploreFeedSectionItemDto
            {
                Id = p.Id,
                EntityType = "place",
                DetailRoute = $"/places/{p.Id}",
                Title = DisplayTextSafety.CleanRequired(p.Name, "Place"),
                Subtitle = DisplayTextSafety.CleanOptional(p.Subtitle),
                Description = TrimDescription(p.Description, 130),
                CategoryLabel = DisplayTextSafety.CleanOptional(p.PlaceTypeName),
                LocationName = DisplayTextSafety.CleanOptional(p.LocationName),
                Image = p.Image,
                Rating = p.AverageRating.HasValue ? Math.Round(p.AverageRating.Value, 1) : null,
                ReviewCount = FormatReviewCountLabel(p.ReviewCount),
                Distance = FormatDistanceLabel(p.DistanceMeters, useCurrentLocationDistance),
                OpeningHours = p.OpeningHours,
                StatusBadge = BuildPlaceStatusBadge(p.OperatingHoursEnabled, p.OperatingHoursJson),
                IsSaved = placeFavoriteIds.Contains(p.Id),
                Tags = p.Tags
            }).ToList()
        };
    }

    private async Task<List<ExploreFeedSectionDto>> BuildDynamicPlaceSectionsAsync(
        int destinationId,
        Point distancePoint,
        IReadOnlyCollection<CuratedInterestSignal> curationSignals,
        ISet<int> placeFavoriteIds,
        bool useCurrentLocationDistance)
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
                p.Location.DestinationId == destinationId &&
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
                OperatingHoursEnabled = p.OperatingHoursEnabled,
                OperatingHoursJson = p.OperatingHoursJson,
                Image = p.Images
                    .OrderBy(image => image.ImageRole == EntityImageRole.Card ? 0 : image.ImageRole == EntityImageRole.Cover ? 1 : 2)
                    .ThenBy(image => image.SortOrder)
                    .ThenBy(image => image.CreatedAt)
                    .ThenBy(image => image.Id)
                    .Select(image => image.Url)
                    .FirstOrDefault(),
                AverageRating = p.PlaceReviews.Select(r => (double?)r.Rating).Average(),
                ReviewCount = p.PlaceReviews.Count(),
                DistanceMeters = EF.Property<Point>(p, SpatialPointFactory.CoordinatesPropertyName).Distance(distancePoint),
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
            .OrderBy(p => p.DistanceMeters)
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
                        TagId = identity.TagId,
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
                        CategoryLabel = DisplayTextSafety.CleanOptional(candidate.PlaceTypeName) ?? "Place",
                        LocationName = DisplayTextSafety.CleanOptional(candidate.Location),
                        Image = candidate.Image,
                        Rating = candidate.AverageRating.HasValue ? Math.Round(candidate.AverageRating.Value, 1) : null,
                        ReviewCount = FormatReviewCountLabel(candidate.ReviewCount),
                        Distance = FormatDistanceLabel(candidate.DistanceMeters, useCurrentLocationDistance),
                        OpeningHours = candidate.OpeningHours,
                        StatusBadge = BuildPlaceStatusBadge(candidate.OperatingHoursEnabled, candidate.OperatingHoursJson),
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
                MoreRoute = $"/explore/{section.Key}?destinationId={destinationId}",
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
                        CategoryLabel = item.CategoryLabel,
                        LocationName = item.LocationName,
                        Image = item.Image,
                        Rating = item.Rating,
                        ReviewCount = item.ReviewCount,
                        Distance = item.Distance,
                        OpeningHours = item.OpeningHours,
                        StatusBadge = item.StatusBadge,
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

    private static readonly Dictionary<string, string> DynamicSectionSubtitles = new(StringComparer.OrdinalIgnoreCase)
    {
        ["swimming"] = "Swimming spots and water activities nearby",
        ["kayaking"] = "Kayaking and water sports nearby",
        ["fishing"] = "Fishing spots nearby",
        ["walking-route"] = "Scenic walks and sightseeing routes nearby",
        ["culture"] = "Culture and arts venues nearby",
        ["history"] = "Historic sites and heritage nearby",
        ["art"] = "Art galleries and exhibitions nearby",
        ["nature"] = "Nature spots and parks nearby",
        ["architecture"] = "Architecture and historic buildings nearby",
        ["monastery"] = "Monasteries and religious heritage nearby",
        ["religious-site"] = "Religious sites and heritage nearby",
        ["garden"] = "Gardens and botanical spaces nearby",
        ["park"] = "Parks and green spaces nearby",
        ["gallery"] = "Art galleries and exhibition spaces nearby",
        ["hiking"] = "Hiking trails and outdoor routes nearby",
        ["boat-tour"] = "Boat tours and water excursions nearby",
        ["island-hopping"] = "Island hopping and sea trips nearby",
        ["photo-walk"] = "Photography spots and scenic walks nearby",
        ["diving-snorkeling"] = "Diving and snorkeling spots nearby",
        ["cable-car"] = "Cable car and panoramic rides nearby",
        ["guided-tour"] = "Guided tours and experiences nearby",
        ["skiing"] = "Ski slopes and winter sports nearby",
    };

    private static string? BuildDynamicSectionSubtitle(DynamicSectionAccumulator section)
    {
        if (section.Items.Count == 0) return null;
        if (DynamicSectionSubtitles.TryGetValue(section.Key, out var subtitle)) return subtitle;
        return $"Places and experiences nearby";
    }

    private async Task<List<ExploreLandmarkCategoryDto>> BuildLandmarkCategoriesAsync(Point userPoint, int destinationId)
    {
        var categories = await _db.Places
            .AsNoTracking()
            .Where(p =>
                p.Stay == null &&
                p.Location.DestinationId == destinationId &&
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
            .OrderBy(g => g.MinDistanceMeters)
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
        var explicitTopics = await _db.UserInterestTopics
            .AsNoTracking()
            .Include(uit => uit.InterestTopic)
                .ThenInclude(topic => topic.TopicTags)
                    .ThenInclude(link => link.Tag)
            .Where(uit => uit.UserId == userId && uit.InterestTopic.IsActive)
            .ToListAsync();

        var explicitTopicIds = explicitTopics
            .Select(interest => interest.InterestTopicId)
            .ToHashSet();

        var explicitSignals = explicitTopics
            .SelectMany(interest => interest.InterestTopic.TopicTags.Select(link => new CuratedInterestSignal
            {
                TagId = link.TagId,
                InterestTopicId = interest.InterestTopicId,
                InterestTopicName = interest.InterestTopic.Name,
                IsExplicit = true,
                Weight = ExplicitInterestWeight * Math.Max(0.1, link.Weight)
            }))
            .ToList();

        var inferredTopicSignals = await _db.UserInterestTopicSignals
            .AsNoTracking()
            .Include(signal => signal.InterestTopic)
                .ThenInclude(topic => topic.TopicTags)
                    .ThenInclude(link => link.Tag)
            .Where(signal =>
                signal.UserId == userId &&
                signal.Source == "inferred" &&
                !signal.IsDismissed &&
                signal.Score >= MinimumInferredInterestScore &&
                signal.InterestTopic.IsActive &&
                !explicitTopicIds.Contains(signal.InterestTopicId))
            .OrderByDescending(signal => signal.Score)
            .ThenByDescending(signal => signal.LastInteractionAt)
            .Take(20)
            .ToListAsync();

        explicitSignals.AddRange(inferredTopicSignals.SelectMany(signal =>
            signal.InterestTopic.TopicTags.Select(link => new CuratedInterestSignal
            {
                TagId = link.TagId,
                InterestTopicId = signal.InterestTopicId,
                InterestTopicName = signal.InterestTopic.Name,
                IsExplicit = false,
                Weight = Math.Min(signal.Score, MaximumInferredInterestWeight) * Math.Max(0.1, link.Weight),
                LastInteractionAt = signal.LastInteractionAt
            })));

        return explicitSignals;
    }

    private async Task<List<RankedCuratedPlace>> BuildCuratedPlacesAsync(
        int destinationId,
        Point distancePoint,
        IReadOnlyCollection<CuratedInterestSignal> curationSignals,
        ISet<int> duplicatePlaceIds)
    {
        var rankedPlaces = await BuildRankedCuratedPlacesAsync(
            destinationId,
            distancePoint,
            curationSignals,
            duplicatePlaceIds,
            CuratedCandidateLimit);

        return rankedPlaces
            .Take(CuratedSectionItemLimit)
            .ToList();
    }

    private async Task<List<RankedCuratedPlace>> BuildRankedCuratedPlacesAsync(
        int destinationId,
        Point distancePoint,
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
                p.Location.DestinationId == destinationId &&
                EF.Property<Point?>(p, SpatialPointFactory.CoordinatesPropertyName) != null &&
                p.PlaceTags.Any(pt => curationTagIds.Contains(pt.TagId)))
            .Select(p => new CuratedPlaceCandidate
            {
                Id = p.Id,
                Title = p.Name,
                Description = p.Description,
                Location = p.Location.Name,
                Image = p.Images.OrderBy(image => image.ImageRole == EntityImageRole.Card ? 0 : image.ImageRole == EntityImageRole.Cover ? 1 : 2).ThenBy(image => image.SortOrder).ThenBy(image => image.CreatedAt).ThenBy(image => image.Id).Select(image => image.Url).FirstOrDefault(),
                DestinationId = p.Location.DestinationId,
                PlaceTypeName = p.PlaceType.Name,
                OpeningHours = p.OpeningHours,
                OperatingHoursEnabled = p.OperatingHoursEnabled,
                OperatingHoursJson = p.OperatingHoursJson,
                DistanceMeters = EF.Property<Point>(p, SpatialPointFactory.CoordinatesPropertyName).Distance(distancePoint),
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
            candidate.PlaceTypeName,
            bestTag?.Name,
            "Local place");
        var topicLabel = DisplayTextSafety.CleanLabelOrHumanizedKey(
            bestSignal?.InterestTopicName,
            bestTag?.Name,
            categoryLabel);
        var reason = bestSignal?.IsExplicit == true
            ? $"Matches your interest: {topicLabel}"
            : $"Based on recent activity: {topicLabel}";

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
            OperatingHoursEnabled = candidate.OperatingHoursEnabled,
            OperatingHoursJson = candidate.OperatingHoursJson,
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
        var nearestDestination = await _db.Destinations
            .AsNoTracking()
            .Where(d => EF.Property<Point?>(d, SpatialPointFactory.CoordinatesPropertyName) != null)
            .Select(d => new ResolvedDestinationCandidate
            {
                Id = d.Id,
                Name = d.Name,
                Description = d.Description,
                Latitude = d.Latitude,
                Longitude = d.Longitude,
                DistanceMeters = EF.Property<Point>(d, SpatialPointFactory.CoordinatesPropertyName).Distance(userPoint),
                Priority = 0,
                TerritoryId = d.Region.TerritoryId,
                TerritoryName = d.Region.Territory.Name
            })
            .OrderBy(d => d.DistanceMeters)
            .ThenBy(d => d.Name)
            .FirstOrDefaultAsync();

        if (nearestDestination is null)
        {
            return null;
        }

        if (nearestDestination.DistanceMeters > NearbyDestinationMaxDistanceMeters)
        {
            return null;
        }

        return nearestDestination;
    }

    private async Task<ResolvedDestinationCandidate?> ResolveDestinationByIdAsync(int destinationId, Point userPoint)
    {
        return await _db.Destinations
            .AsNoTracking()
            .Where(d => d.Id == destinationId)
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
                Priority = -1,
                TerritoryId = d.Region.TerritoryId,
                TerritoryName = d.Region.Territory.Name
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

    private static string? FormatDistanceLabel(double? distanceMeters, bool useCurrentLocationDistance)
    {
        var label = SpatialDistanceFormatter.Format(distanceMeters);
        if (string.IsNullOrWhiteSpace(label) || useCurrentLocationDistance)
        {
            return label;
        }

        return label.EndsWith(" away", StringComparison.OrdinalIgnoreCase)
            ? label[..^5] + " from centre"
            : label + " from centre";
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
            Longitude = destination.Longitude,
            TerritoryId = destination.TerritoryId,
            TerritoryName = destination.TerritoryName
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


    private sealed class SectionTagMetadata
    {
        public int TagId { get; set; }
        public string TagName { get; set; } = string.Empty;
        public string TagSlug { get; set; } = string.Empty;
        public int TagTypeId { get; set; }
        public string TagTypeName { get; set; } = string.Empty;
        public string TagTypeSlug { get; set; } = string.Empty;
        public string TagTypeLabel { get; set; } = string.Empty;
        public int TagTypeOrder { get; set; }
    }

    private sealed class SectionPlaceProjection
    {
        public int Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public string? Subtitle { get; set; }
        public string? Description { get; set; }
        public string? CategoryLabel { get; set; }
        public string? LocationName { get; set; }
        public string? OpeningHours { get; set; }
        public bool OperatingHoursEnabled { get; set; }
        public string? OperatingHoursJson { get; set; }
        public string? Image { get; set; }
        public double? AverageRating { get; set; }
        public int ReviewCount { get; set; }
        public decimal? PricePerNight { get; set; }
        public double DistanceMeters { get; set; }
        public List<ExploreFeedSectionTagDto> Tags { get; set; } = new();
    }

    private sealed class SectionEventProjection
    {
        public int Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public string? Subtitle { get; set; }
        public string? LocationName { get; set; }
        public string? Description { get; set; }
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public bool IsHappeningNow { get; set; }
        public bool IsUpcoming { get; set; }
        public string? Image { get; set; }
        public double DistanceMeters { get; set; }
        public List<ExploreFeedSectionTagDto> Tags { get; set; } = new();
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
        public bool OperatingHoursEnabled { get; set; }
        public string? OperatingHoursJson { get; set; }
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
        public int TagId { get; set; }
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
        public string? LocationName { get; set; }
        public string? Image { get; set; }
        public double? Rating { get; set; }
        public string? ReviewCount { get; set; }
        public string? Distance { get; set; }
        public string? OpeningHours { get; set; }
        public ExploreFeedItemStatusBadgeDto? StatusBadge { get; set; }
        public bool IsSaved { get; set; }
        public List<ExploreFeedSectionTagDto> Tags { get; set; } = new();
    }

    private sealed class OperatingHourEntry
    {
        public string? DayOfWeek { get; set; }
        public bool IsOpen { get; set; }
        public string? OpenTime { get; set; }
        public string? CloseTime { get; set; }
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
        public int? TerritoryId { get; set; }
        public string? TerritoryName { get; set; }
    }

    private sealed class CuratedInterestSignal
    {
        public int TagId { get; set; }
        public int InterestTopicId { get; set; }
        public string InterestTopicName { get; set; } = string.Empty;
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
        public bool OperatingHoursEnabled { get; set; }
        public string? OperatingHoursJson { get; set; }
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
        public bool OperatingHoursEnabled { get; set; }
        public string? OperatingHoursJson { get; set; }
        public List<CuratedTagMatch> MatchedTags { get; set; } = new();
        public bool IsDuplicateWithOtherSection { get; set; }
        public bool IsOutsideCurrentDestination { get; set; }
    }
}
