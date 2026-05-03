using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Geotrack.Application.DTOs.Search;
using Geotrack.Application.Interfaces.Service;
using Geotrack.Infrastructure.Spatial;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;

namespace Geotrack.Infrastructure.Services;

public class SearchService : ISearchService
{
    private const int MaxTake = 12;
    private const int MetadataOptionLimit = 10;
    private const string LanguageEnglish = "en";

    private static readonly Regex SeparatorPattern = new(@"[^a-z0-9]+", RegexOptions.Compiled);
    private static readonly Regex WhitespacePattern = new(@"\s+", RegexOptions.Compiled);

    private static readonly string[] DomainOrder =
    [
        SearchDomains.Destinations,
        SearchDomains.Places,
        SearchDomains.Stays,
        SearchDomains.Events,
        SearchDomains.Activities
    ];

    private static readonly HashSet<string> StayTypeTokens = new(StringComparer.Ordinal)
    {
        "accommodation",
        "accommodations",
        "apartment",
        "apartments",
        "boutique",
        "cabin",
        "cabins",
        "hotel",
        "hotels",
        "hostel",
        "hostels",
        "lodging",
        "resort",
        "resorts",
        "room",
        "rooms",
        "stay",
        "stays",
        "villa",
        "villas"
    };

    private static readonly SearchLanguageProfile EnglishProfile = new(
        LanguageEnglish,
        new HashSet<string>(StringComparer.Ordinal)
        {
            "a", "an", "and", "around", "at", "best", "by", "do", "find", "for", "in", "me", "near", "nearby",
            "of", "on", "show", "the", "things", "to", "what", "where", "with"
        },
        new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["beach bar"] = ["beach-bars", "beaches", "food-drink", "nightlife"],
            ["beach bars"] = ["beach-bars", "beaches", "food-drink", "nightlife"],
            ["fine dining"] = ["fine-dining", "food-drink"],
            ["food market"] = ["food-market", "food-drink"],
            ["live music"] = ["live-music", "nightlife", "cultural"],
            ["scenic view"] = ["scenic-views", "nature"],
            ["scenic views"] = ["scenic-views", "nature"],
            ["things to do"] = ["outdoor", "adventure", "culture", "history", "scenic-views"],
            ["walking tour"] = ["history", "culture", "outdoor", "scenic-walks"]
        },
        new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["activity"] = ["outdoor", "adventure", "culture", "history", "scenic-views"],
            ["activities"] = ["outdoor", "adventure", "culture", "history", "scenic-views"],
            ["beach"] = ["beaches", "outdoor", "scenic-views"],
            ["beaches"] = ["beaches", "outdoor", "scenic-views"],
            ["boat"] = ["outdoor", "adventure", "islands", "beaches"],
            ["boating"] = ["outdoor", "adventure", "islands", "beaches"],
            ["bar"] = ["nightlife", "food-drink", "beach-bars"],
            ["bars"] = ["nightlife", "food-drink", "beach-bars"],
            ["cafe"] = ["food-drink"],
            ["cafes"] = ["food-drink"],
            ["concert"] = ["live-music", "nightlife", "cultural"],
            ["concerts"] = ["live-music", "nightlife", "cultural"],
            ["culture"] = ["culture", "history", "cultural"],
            ["dining"] = ["food-drink", "fine-dining"],
            ["experience"] = ["outdoor", "adventure", "culture", "scenic-views"],
            ["experiences"] = ["outdoor", "adventure", "culture", "scenic-views"],
            ["fish"] = ["outdoor", "adventure", "beaches", "islands"],
            ["fishing"] = ["outdoor", "adventure", "beaches", "islands"],
            ["food"] = ["food-drink", "fine-dining", "food-market"],
            ["hike"] = ["hiking", "nature", "mountains", "scenic-views"],
            ["hiking"] = ["hiking", "nature", "mountains", "scenic-views"],
            ["kayak"] = ["outdoor", "adventure", "beaches", "islands"],
            ["kayaking"] = ["outdoor", "adventure", "beaches", "islands"],
            ["landmark"] = ["landmarks", "historic-sites", "culture", "history"],
            ["landmarks"] = ["landmarks", "historic-sites", "culture", "history"],
            ["museum"] = ["museums", "culture", "history"],
            ["museums"] = ["museums", "culture", "history"],
            ["music"] = ["live-music", "nightlife", "cultural"],
            ["restaurant"] = ["food-drink", "fine-dining"],
            ["restaurants"] = ["food-drink", "fine-dining"],
            ["spa"] = ["spa", "luxury", "pool"],
            ["swim"] = ["beaches", "pool", "outdoor"],
            ["swimming"] = ["beaches", "pool", "outdoor"],
            ["tour"] = ["outdoor", "culture", "history", "adventure"],
            ["tours"] = ["outdoor", "culture", "history", "adventure"],
            ["walk"] = ["scenic-walks", "culture", "history", "outdoor"],
            ["walking"] = ["scenic-walks", "culture", "history", "outdoor"]
        },
        new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["beach bar"] = [SearchDomains.Places],
            ["beach bars"] = [SearchDomains.Places],
            ["fine dining"] = [SearchDomains.Places],
            ["live music"] = [SearchDomains.Events],
            ["places to stay"] = [SearchDomains.Stays],
            ["things to do"] = [SearchDomains.Activities, SearchDomains.Places, SearchDomains.Events],
            ["where to stay"] = [SearchDomains.Stays]
        },
        new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["accommodation"] = [SearchDomains.Stays],
            ["accommodations"] = [SearchDomains.Stays],
            ["apartment"] = [SearchDomains.Stays],
            ["apartments"] = [SearchDomains.Stays],
            ["attraction"] = [SearchDomains.Places],
            ["attractions"] = [SearchDomains.Places],
            ["activity"] = [SearchDomains.Activities, SearchDomains.Places, SearchDomains.Events],
            ["activities"] = [SearchDomains.Activities, SearchDomains.Places, SearchDomains.Events],
            ["bar"] = [SearchDomains.Places],
            ["bars"] = [SearchDomains.Places],
            ["beach"] = [SearchDomains.Places],
            ["beaches"] = [SearchDomains.Places],
            ["cabin"] = [SearchDomains.Stays],
            ["cabins"] = [SearchDomains.Stays],
            ["cafe"] = [SearchDomains.Places],
            ["cafes"] = [SearchDomains.Places],
            ["concert"] = [SearchDomains.Events],
            ["concerts"] = [SearchDomains.Events],
            ["destination"] = [SearchDomains.Destinations],
            ["destinations"] = [SearchDomains.Destinations],
            ["dining"] = [SearchDomains.Places],
            ["event"] = [SearchDomains.Events],
            ["events"] = [SearchDomains.Events],
            ["experience"] = [SearchDomains.Activities, SearchDomains.Places, SearchDomains.Events],
            ["experiences"] = [SearchDomains.Activities, SearchDomains.Places, SearchDomains.Events],
            ["festival"] = [SearchDomains.Events],
            ["festivals"] = [SearchDomains.Events],
            ["food"] = [SearchDomains.Places],
            ["hotel"] = [SearchDomains.Stays],
            ["hotels"] = [SearchDomains.Stays],
            ["landmark"] = [SearchDomains.Places],
            ["landmarks"] = [SearchDomains.Places],
            ["lodging"] = [SearchDomains.Stays],
            ["museum"] = [SearchDomains.Places],
            ["museums"] = [SearchDomains.Places],
            ["place"] = [SearchDomains.Places],
            ["places"] = [SearchDomains.Places],
            ["restaurant"] = [SearchDomains.Places],
            ["restaurants"] = [SearchDomains.Places],
            ["resort"] = [SearchDomains.Stays],
            ["resorts"] = [SearchDomains.Stays],
            ["room"] = [SearchDomains.Stays],
            ["rooms"] = [SearchDomains.Stays],
            ["stay"] = [SearchDomains.Stays],
            ["stays"] = [SearchDomains.Stays],
            ["tour"] = [SearchDomains.Activities, SearchDomains.Places, SearchDomains.Events],
            ["tours"] = [SearchDomains.Activities, SearchDomains.Places, SearchDomains.Events],
            ["villa"] = [SearchDomains.Stays],
            ["villas"] = [SearchDomains.Stays]
        });

    private readonly AppDbContext _db;

    public SearchService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<GlobalSearchResponseDto> SearchAsync(
        string? query,
        string? domains = null,
        double? latitude = null,
        double? longitude = null,
        string? language = null,
        int take = 6,
        CancellationToken cancellationToken = default)
    {
        var profile = ResolveProfile(language);
        var normalizedTake = Math.Clamp(take, 1, MaxTake);
        var metadata = await GetMetadataAsync(profile.LanguageCode, cancellationToken);
        var trimmedQuery = (query ?? string.Empty).Trim();
        var selectedDomains = ParseDomains(domains);

        if (trimmedQuery.Length < 2)
        {
            return new GlobalSearchResponseDto
            {
                Query = trimmedQuery,
                Language = profile.LanguageCode,
                FilterGroups = metadata.FilterGroups
            };
        }

        var destinations = await LoadDestinationVocabularyAsync(cancellationToken);
        var vocabulary = await LoadVocabularyAsync(cancellationToken);
        var intent = BuildIntent(trimmedQuery, profile, destinations, vocabulary, latitude, longitude);

        var results = new List<SearchResultItemDto>();
        if (selectedDomains.Contains(SearchDomains.Destinations))
        {
            results.AddRange(await SearchDestinationsAsync(intent, normalizedTake, cancellationToken));
        }

        if (selectedDomains.Contains(SearchDomains.Places))
        {
            results.AddRange(await SearchPlacesAsync(intent, includeStays: false, normalizedTake, cancellationToken));
        }

        if (selectedDomains.Contains(SearchDomains.Stays))
        {
            results.AddRange(await SearchPlacesAsync(intent, includeStays: true, normalizedTake, cancellationToken));
        }

        if (selectedDomains.Contains(SearchDomains.Events))
        {
            results.AddRange(await SearchEventsAsync(intent, normalizedTake, cancellationToken));
        }

        if (selectedDomains.Contains(SearchDomains.Activities))
        {
            results.AddRange(SearchActivities(intent, vocabulary, normalizedTake));
        }

        var orderedResults = results
            .Where(result => result.Score > 0)
            .DistinctBy(result => $"{result.EntityType}:{result.Id}")
            .OrderByDescending(result => result.Score)
            .ThenBy(result => Array.IndexOf(DomainOrder, DomainForEntityType(result.EntityType)))
            .ThenBy(result => result.Title)
            .ToList();

        var topResult = orderedResults.FirstOrDefault();

        return new GlobalSearchResponseDto
        {
            Query = trimmedQuery,
            Language = profile.LanguageCode,
            Intent = MapIntent(intent),
            FilterGroups = metadata.FilterGroups,
            TopResult = topResult,
            Groups = BuildGroups(orderedResults, normalizedTake, topResult),
            TotalCount = orderedResults.Count
        };
    }

    public async Task<SearchMetadataDto> GetMetadataAsync(
        string? language = null,
        CancellationToken cancellationToken = default)
    {
        var profile = ResolveProfile(language);

        var destinationOptions = await _db.Destinations
            .AsNoTracking()
            .OrderBy(destination => destination.Name)
            .Take(MetadataOptionLimit)
            .Select(destination => new SearchFilterOptionDto
            {
                Id = $"destination:{destination.Id}",
                Label = destination.Name,
                Domain = SearchDomains.Destinations,
                Count = destination.Locations.Count
            })
            .ToListAsync(cancellationToken);

        var placeTypeOptions = await _db.PlaceTypes
            .AsNoTracking()
            .OrderBy(type => type.Name)
            .Take(MetadataOptionLimit)
            .Select(type => new SearchFilterOptionDto
            {
                Id = $"place-type:{type.Id}",
                Label = type.Name,
                Domain = SearchDomains.Places,
                Count = type.Places.Count(place => place.Stay == null)
            })
            .ToListAsync(cancellationToken);

        var stayOptions = await _db.Tags
            .AsNoTracking()
            .Where(tag => tag.ForPlace && tag.PlaceTags.Any(placeTag => placeTag.Place.Stay != null))
            .OrderByDescending(tag => tag.PlaceTags.Count(placeTag => placeTag.Place.Stay != null))
            .ThenBy(tag => tag.Name)
            .Take(MetadataOptionLimit)
            .Select(tag => new SearchFilterOptionDto
            {
                Id = $"tag:{tag.Slug}",
                Label = tag.Name,
                Domain = SearchDomains.Stays,
                Count = tag.PlaceTags.Count(placeTag => placeTag.Place.Stay != null)
            })
            .ToListAsync(cancellationToken);

        var activityOptions = await _db.Tags
            .AsNoTracking()
            .Where(tag => (tag.ForPlace || tag.ForEvent) &&
                (tag.PlaceTags.Any() || tag.EventTags.Any()))
            .OrderByDescending(tag => tag.PlaceTags.Count() + tag.EventTags.Count())
            .ThenBy(tag => tag.Name)
            .Take(MetadataOptionLimit)
            .Select(tag => new SearchFilterOptionDto
            {
                Id = $"tag:{tag.Slug}",
                Label = tag.Name,
                Domain = SearchDomains.Activities,
                Count = tag.PlaceTags.Count() + tag.EventTags.Count()
            })
            .ToListAsync(cancellationToken);

        var eventOptions = await _db.EventTypes
            .AsNoTracking()
            .OrderBy(type => type.Name)
            .Take(MetadataOptionLimit)
            .Select(type => new SearchFilterOptionDto
            {
                Id = $"event-type:{type.Id}",
                Label = type.Name,
                Domain = SearchDomains.Events,
                Count = type.Events.Count
            })
            .ToListAsync(cancellationToken);

        return new SearchMetadataDto
        {
            Language = profile.LanguageCode,
            FilterGroups =
            [
                new SearchFilterGroupDto
                {
                    Key = "domains",
                    Label = "Search",
                    Options =
                    [
                        new SearchFilterOptionDto { Id = SearchDomains.Destinations, Label = "Destinations", Domain = SearchDomains.Destinations },
                        new SearchFilterOptionDto { Id = SearchDomains.Places, Label = "Places", Domain = SearchDomains.Places },
                        new SearchFilterOptionDto { Id = SearchDomains.Stays, Label = "Stays", Domain = SearchDomains.Stays },
                        new SearchFilterOptionDto { Id = SearchDomains.Events, Label = "Events", Domain = SearchDomains.Events },
                        new SearchFilterOptionDto { Id = SearchDomains.Activities, Label = "Activities", Domain = SearchDomains.Activities },
                    ]
                },
                new SearchFilterGroupDto { Key = SearchDomains.Destinations, Label = "Destinations", Options = CleanFilterOptions(destinationOptions, "Destination") },
                new SearchFilterGroupDto { Key = SearchDomains.Places, Label = "Places", Options = CleanFilterOptions(placeTypeOptions, "Places") },
                new SearchFilterGroupDto { Key = SearchDomains.Stays, Label = "Stays", Options = CleanFilterOptions(stayOptions, "Stays") },
                new SearchFilterGroupDto { Key = SearchDomains.Activities, Label = "Activities", Options = CleanFilterOptions(activityOptions, "Activity") },
                new SearchFilterGroupDto { Key = SearchDomains.Events, Label = "Events", Options = CleanFilterOptions(eventOptions, "Events") }
            ]
        };
    }

    private async Task<List<SearchResultItemDto>> SearchDestinationsAsync(
        SearchIntent intent,
        int take,
        CancellationToken cancellationToken)
    {
        var pattern = LikePattern(intent.RawQuery);
        var destinationId = intent.Destination?.Id;
        var tagIds = intent.MatchedTagIds;
        var hasDestinationDomainIntent = intent.ContentTypes.Contains(SearchDomains.Destinations);

        var destinations = await _db.Destinations
            .AsNoTracking()
            .Where(destination =>
                hasDestinationDomainIntent ||
                (destinationId.HasValue && destination.Id == destinationId.Value) ||
                EF.Functions.ILike(destination.Name, pattern) ||
                EF.Functions.ILike(destination.Description, pattern) ||
                destination.DestinationTags.Any(destinationTag =>
                    tagIds.Contains(destinationTag.TagId) ||
                    EF.Functions.ILike(destinationTag.Tag.Name, pattern) ||
                    EF.Functions.ILike(destinationTag.Tag.Slug, pattern)))
            .Select(destination => new DestinationSearchProjection
            {
                Id = destination.Id,
                Name = destination.Name,
                Description = destination.Description,
                DestinationTypeName = destination.DestinationType.Name,
                Latitude = destination.Latitude,
                Longitude = destination.Longitude,
                ReviewCount = destination.DestinationReviews.Count,
                Tags = destination.DestinationTags
                    .Select(destinationTag => new SearchTagProjection
                    {
                        Id = destinationTag.TagId,
                        Name = destinationTag.Tag.Name,
                        Slug = destinationTag.Tag.Slug
                    })
                    .ToList()
            })
            .Take(50)
            .ToListAsync(cancellationToken);

        return destinations
            .Select(destination => RankDestination(destination, intent))
            .Where(result => result.Score > 0)
            .OrderByDescending(result => result.Score)
            .ThenBy(result => result.Title)
            .Take(take)
            .ToList();
    }

    private async Task<List<SearchResultItemDto>> SearchPlacesAsync(
        SearchIntent intent,
        bool includeStays,
        int take,
        CancellationToken cancellationToken)
    {
        var pattern = LikePattern(intent.RawQuery);
        var destinationId = intent.Destination?.Id;
        var tagIds = intent.MatchedTagIds;
        var placeTypeIds = intent.MatchedPlaceTypeIds;
        var amenityIds = intent.MatchedAmenityIds;
        var activePoint = intent.ActivePoint;
        var hasDomainCandidateIntent = includeStays
            ? intent.ContentTypes.Contains(SearchDomains.Stays)
            : intent.ContentTypes.Contains(SearchDomains.Places) || intent.ContentTypes.Contains(SearchDomains.Activities);

        var query = _db.Places
            .AsNoTracking()
            .Where(place => includeStays ? place.Stay != null && place.Stay.PricePerNight != null : place.Stay == null || place.Stay.PricePerNight == null)
            .Where(place =>
                hasDomainCandidateIntent ||
                (destinationId.HasValue && place.Location.DestinationId == destinationId.Value) ||
                tagIds.Count > 0 && place.PlaceTags.Any(placeTag => tagIds.Contains(placeTag.TagId)) ||
                placeTypeIds.Count > 0 && placeTypeIds.Contains(place.PlaceTypeId) ||
                includeStays && amenityIds.Count > 0 && place.PlaceAmenities.Any(amenity => amenityIds.Contains(amenity.AmenityId)) ||
                EF.Functions.ILike(place.Name, pattern) ||
                (place.Subtitle != null && EF.Functions.ILike(place.Subtitle, pattern)) ||
                EF.Functions.ILike(place.Description, pattern) ||
                (place.Address != null && EF.Functions.ILike(place.Address, pattern)) ||
                EF.Functions.ILike(place.PlaceType.Name, pattern) ||
                EF.Functions.ILike(place.Location.Name, pattern) ||
                EF.Functions.ILike(place.Location.Destination.Name, pattern) ||
                place.PlaceTags.Any(placeTag =>
                    EF.Functions.ILike(placeTag.Tag.Name, pattern) ||
                    EF.Functions.ILike(placeTag.Tag.Slug, pattern)) ||
                includeStays && place.PlaceAmenities.Any(amenity =>
                    EF.Functions.ILike(amenity.Amenity.Name, pattern) ||
                    EF.Functions.ILike(amenity.Amenity.Icon, pattern)));

        List<PlaceSearchProjection> places;
        if (activePoint is null)
        {
            places = await query
                .Select(place => new PlaceSearchProjection
                {
                    Id = place.Id,
                    Name = place.Name,
                    Subtitle = place.Subtitle,
                    Description = place.Description,
                    Address = place.Address,
                    OpeningHours = place.OpeningHours,
                    PricePerNight = (place.Stay != null ? place.Stay.PricePerNight : null),
                    PlaceTypeName = place.PlaceType.Name,
                    LocationName = place.Location.Name,
                    DestinationId = place.Location.DestinationId,
                    DestinationName = place.Location.Destination.Name,
                    Latitude = place.Latitude,
                    Longitude = place.Longitude,
                    AverageRating = place.PlaceReviews.Select(review => (double?)review.Rating).Average(),
                    ReviewCount = place.PlaceReviews.Count,
                    Tags = place.PlaceTags
                        .Select(placeTag => new SearchTagProjection
                        {
                            Id = placeTag.TagId,
                            Name = placeTag.Tag.Name,
                            Slug = placeTag.Tag.Slug
                        })
                        .ToList(),
                    Amenities = place.PlaceAmenities
                        .Select(amenity => new SearchAmenityProjection
                        {
                            Id = amenity.AmenityId,
                            Name = amenity.Amenity.Name,
                            Icon = amenity.Amenity.Icon
                        })
                        .ToList()
                })
                .Take(120)
                .ToListAsync(cancellationToken);
        }
        else
        {
            places = await query
                .Where(place => EF.Property<Point?>(place, SpatialPointFactory.CoordinatesPropertyName) != null)
                .Select(place => new PlaceSearchProjection
                {
                    Id = place.Id,
                    Name = place.Name,
                    Subtitle = place.Subtitle,
                    Description = place.Description,
                    Address = place.Address,
                    OpeningHours = place.OpeningHours,
                    PricePerNight = (place.Stay != null ? place.Stay.PricePerNight : null),
                    PlaceTypeName = place.PlaceType.Name,
                    LocationName = place.Location.Name,
                    DestinationId = place.Location.DestinationId,
                    DestinationName = place.Location.Destination.Name,
                    Latitude = place.Latitude,
                    Longitude = place.Longitude,
                    AverageRating = place.PlaceReviews.Select(review => (double?)review.Rating).Average(),
                    ReviewCount = place.PlaceReviews.Count,
                    DistanceMeters = EF.Property<Point>(place, SpatialPointFactory.CoordinatesPropertyName).Distance(activePoint),
                    Tags = place.PlaceTags
                        .Select(placeTag => new SearchTagProjection
                        {
                            Id = placeTag.TagId,
                            Name = placeTag.Tag.Name,
                            Slug = placeTag.Tag.Slug
                        })
                        .ToList(),
                    Amenities = place.PlaceAmenities
                        .Select(amenity => new SearchAmenityProjection
                        {
                            Id = amenity.AmenityId,
                            Name = amenity.Amenity.Name,
                            Icon = amenity.Amenity.Icon
                        })
                        .ToList()
                })
                .OrderBy(place => destinationId.HasValue && place.DestinationId == destinationId.Value ? 0 : 1)
                .ThenBy(place => place.DistanceMeters)
                .Take(120)
                .ToListAsync(cancellationToken);
        }

        return places
            .Select(place => RankPlace(place, intent, includeStays))
            .Where(result => result.Score > 0)
            .OrderByDescending(result => result.Score)
            .ThenBy(result => result.Distance ?? string.Empty)
            .ThenBy(result => result.Title)
            .Take(take)
            .ToList();
    }

    private async Task<List<SearchResultItemDto>> SearchEventsAsync(
        SearchIntent intent,
        int take,
        CancellationToken cancellationToken)
    {
        var pattern = LikePattern(intent.RawQuery);
        var destinationId = intent.Destination?.Id;
        var tagIds = intent.MatchedTagIds;
        var eventTypeIds = intent.MatchedEventTypeIds;
        var activePoint = intent.ActivePoint;
        var hasEventDomainIntent = intent.ContentTypes.Contains(SearchDomains.Events) ||
            intent.ContentTypes.Contains(SearchDomains.Activities);

        var query = _db.Events
            .AsNoTracking()
            .Where(@event =>
                hasEventDomainIntent ||
                (destinationId.HasValue && @event.Location.DestinationId == destinationId.Value) ||
                tagIds.Count > 0 && @event.EventTags.Any(eventTag => tagIds.Contains(eventTag.TagId)) ||
                eventTypeIds.Count > 0 && eventTypeIds.Contains(@event.EventTypeId) ||
                EF.Functions.ILike(@event.Name, pattern) ||
                EF.Functions.ILike(@event.Description, pattern) ||
                EF.Functions.ILike(@event.EventType.Name, pattern) ||
                EF.Functions.ILike(@event.Location.Name, pattern) ||
                EF.Functions.ILike(@event.Location.Destination.Name, pattern) ||
                (@event.HostedBy != null && EF.Functions.ILike(@event.HostedBy, pattern)) ||
                @event.EventTags.Any(eventTag =>
                    EF.Functions.ILike(eventTag.Tag.Name, pattern) ||
                    EF.Functions.ILike(eventTag.Tag.Slug, pattern)));

        List<EventSearchProjection> events;
        if (activePoint is null)
        {
            events = await query
                .Select(@event => new EventSearchProjection
                {
                    Id = @event.Id,
                    Name = @event.Name,
                    Description = @event.Description,
                    EventTypeName = @event.EventType.Name,
                    LocationName = @event.Location.Name,
                    DestinationId = @event.Location.DestinationId,
                    DestinationName = @event.Location.Destination.Name,
                    Latitude = @event.Place != null ? @event.Place.Latitude : @event.Location.Latitude,
                    Longitude = @event.Place != null ? @event.Place.Longitude : @event.Location.Longitude,
                    StartDate = @event.StartDate,
                    EndDate = @event.EndDate,
                    Tags = @event.EventTags
                        .Select(eventTag => new SearchTagProjection
                        {
                            Id = eventTag.TagId,
                            Name = eventTag.Tag.Name,
                            Slug = eventTag.Tag.Slug
                        })
                        .ToList()
                })
                .Take(120)
                .ToListAsync(cancellationToken);
        }
        else
        {
            events = await query
                .Where(@event => EF.Property<Point?>(@event, SpatialPointFactory.CoordinatesPropertyName) != null)
                .Select(@event => new EventSearchProjection
                {
                    Id = @event.Id,
                    Name = @event.Name,
                    Description = @event.Description,
                    EventTypeName = @event.EventType.Name,
                    LocationName = @event.Location.Name,
                    DestinationId = @event.Location.DestinationId,
                    DestinationName = @event.Location.Destination.Name,
                    Latitude = @event.Place != null ? @event.Place.Latitude : @event.Location.Latitude,
                    Longitude = @event.Place != null ? @event.Place.Longitude : @event.Location.Longitude,
                    StartDate = @event.StartDate,
                    EndDate = @event.EndDate,
                    DistanceMeters = EF.Property<Point>(@event, SpatialPointFactory.CoordinatesPropertyName).Distance(activePoint),
                    Tags = @event.EventTags
                        .Select(eventTag => new SearchTagProjection
                        {
                            Id = eventTag.TagId,
                            Name = eventTag.Tag.Name,
                            Slug = eventTag.Tag.Slug
                        })
                        .ToList()
                })
                .OrderBy(@event => destinationId.HasValue && @event.DestinationId == destinationId.Value ? 0 : 1)
                .ThenBy(@event => @event.DistanceMeters)
                .ThenBy(@event => @event.StartDate)
                .Take(120)
                .ToListAsync(cancellationToken);
        }

        return events
            .Select(@event => RankEvent(@event, intent))
            .Where(result => result.Score > 0)
            .OrderByDescending(result => result.Score)
            .ThenBy(result => result.Title)
            .Take(take)
            .ToList();
    }

    private List<SearchResultItemDto> SearchActivities(
        SearchIntent intent,
        SearchVocabulary vocabulary,
        int take)
    {
        var activityResults = vocabulary.Tags
            .Where(tag => tag.ForPlace || tag.ForEvent)
            .Select(tag => RankActivity(tag, intent))
            .Where(result => result.Score > 0)
            .OrderByDescending(result => result.Score)
            .ThenBy(result => result.Title)
            .Take(take)
            .ToList();

        var categoryResults = vocabulary.PlaceTypes
            .Select(type => RankCategory(type, intent))
            .Where(result => result.Score > 0)
            .OrderByDescending(result => result.Score)
            .ThenBy(result => result.Title)
            .Take(Math.Max(0, take - activityResults.Count));

        activityResults.AddRange(categoryResults);
        return activityResults
            .DistinctBy(result => result.Title.ToLowerInvariant())
            .Take(take)
            .ToList();
    }

    private SearchResultItemDto RankDestination(DestinationSearchProjection destination, SearchIntent intent)
    {
        var matchedTerms = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var isDetectedDestination = intent.Destination?.Id == destination.Id;
        var score = isDetectedDestination && intent.HasDiscoveryIntent ? 4.0 : 15.0;
        score += ScoreText(destination.Name, intent, matchedTerms, exact: 260, contains: 150, token: 42);
        score += ScoreText(destination.Description, intent, matchedTerms, exact: 40, contains: 24, token: 12);
        score += ScoreTags(destination.Tags, intent, matchedTerms, exact: 70);

        if (isDetectedDestination)
        {
            score += intent.HasDiscoveryIntent ? 72 : 240;
            matchedTerms.Add(destination.Name);
        }

        score += Math.Min(destination.ReviewCount, 30) * 0.4;

        return new SearchResultItemDto
        {
            Id = destination.Id,
            EntityType = SearchEntityTypes.Destination,
            Title = DisplayTextSafety.CleanRequired(destination.Name, "Destination"),
            Subtitle = DisplayTextSafety.CleanOptional(destination.DestinationTypeName),
            Description = TrimDescription(destination.Description, 150),
            Image = destination.Image,
            DestinationId = destination.Id,
            DestinationName = DisplayTextSafety.CleanOptional(destination.Name),
            Latitude = HasUsableCoordinates(destination.Latitude, destination.Longitude) ? destination.Latitude : null,
            Longitude = HasUsableCoordinates(destination.Latitude, destination.Longitude) ? destination.Longitude : null,
            CategoryLabel = DisplayTextSafety.CleanOptional(destination.DestinationTypeName),
            Tags = DisplayTextSafety.CleanLabels(destination.Tags.Select(tag => tag.Name)),
            ReviewCount = FormatReviewCountLabel(destination.ReviewCount),
            CanOpenInExplore = HasUsableCoordinates(destination.Latitude, destination.Longitude),
            Score = Math.Round(score, 3),
            MatchedTerms = matchedTerms.OrderBy(term => term).Take(5).ToList()
        };
    }

    private SearchResultItemDto RankPlace(PlaceSearchProjection place, SearchIntent intent, bool isStay)
    {
        var matchedTerms = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var score = isStay ? 12.0 : 14.0;

        score += ScoreDestination(place.DestinationId, place.DestinationName, intent, matchedTerms);
        score += ScoreText(place.Name, intent, matchedTerms, exact: 210, contains: 130, token: 36);
        score += ScoreText(place.Subtitle, intent, matchedTerms, exact: 55, contains: 32, token: 15);
        score += ScoreText(place.Description, intent, matchedTerms, exact: 36, contains: 24, token: 10);
        score += ScoreText(place.PlaceTypeName, intent, matchedTerms, exact: 88, contains: 62, token: 24);
        score += ScoreText(place.LocationName, intent, matchedTerms, exact: 45, contains: 28, token: 12);
        score += ScoreTags(place.Tags, intent, matchedTerms, exact: 94);

        if (isStay)
        {
            score += ScoreAmenities(place.Amenities, intent, matchedTerms);
        }

        score += ScoreDomainHint(isStay ? SearchDomains.Stays : SearchDomains.Places, intent);
        score += ScoreDistance(place.DistanceMeters, intent);
        score += Math.Min(place.ReviewCount, 50) * 0.45;
        score += (place.AverageRating ?? 0) * 2.0;

        var title = DisplayTextSafety.CleanRequired(place.Name, isStay ? "Stay" : "Place");
        return new SearchResultItemDto
        {
            Id = place.Id,
            EntityType = isStay ? SearchEntityTypes.Stay : SearchEntityTypes.Place,
            Title = title,
            Subtitle = DisplayTextSafety.CleanOptional(place.Subtitle)
                ?? DisplayTextSafety.CleanOptional(place.PlaceTypeName),
            Description = TrimDescription(place.Description, 160),
            Image = place.Image,
            Route = isStay ? $"/stays/{place.Id}" : $"/places/{place.Id}",
            DestinationId = place.DestinationId,
            DestinationName = DisplayTextSafety.CleanOptional(place.DestinationName),
            Latitude = HasUsableCoordinates(place.Latitude, place.Longitude) ? place.Latitude : null,
            Longitude = HasUsableCoordinates(place.Latitude, place.Longitude) ? place.Longitude : null,
            CategoryLabel = DisplayTextSafety.CleanOptional(place.PlaceTypeName),
            Tags = DisplayTextSafety.CleanLabels(place.Tags.Select(tag => tag.Name)),
            Rating = place.AverageRating.HasValue ? Math.Round(place.AverageRating.Value, 1) : null,
            ReviewCount = FormatReviewCountLabel(place.ReviewCount),
            Distance = SpatialDistanceFormatter.Format(place.DistanceMeters),
            Score = Math.Round(score, 3),
            MatchedTerms = matchedTerms.OrderBy(term => term).Take(5).ToList()
        };
    }

    private SearchResultItemDto RankEvent(EventSearchProjection @event, SearchIntent intent)
    {
        var matchedTerms = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var score = 14.0;

        score += ScoreDestination(@event.DestinationId, @event.DestinationName, intent, matchedTerms);
        score += ScoreText(@event.Name, intent, matchedTerms, exact: 220, contains: 135, token: 38);
        score += ScoreText(@event.Description, intent, matchedTerms, exact: 38, contains: 26, token: 12);
        score += ScoreText(@event.EventTypeName, intent, matchedTerms, exact: 105, contains: 75, token: 28);
        score += ScoreText(@event.LocationName, intent, matchedTerms, exact: 45, contains: 28, token: 12);
        score += ScoreTags(@event.Tags, intent, matchedTerms, exact: 96);
        score += ScoreDomainHint(SearchDomains.Events, intent);
        score += ScoreDistance(@event.DistanceMeters, intent);

        if (@event.EndDate >= DateTime.UtcNow)
        {
            score += 26;
            var daysAway = Math.Max(0, (@event.StartDate.Date - DateTime.UtcNow.Date).TotalDays);
            score += Math.Max(0, 18 - daysAway);
        }

        return new SearchResultItemDto
        {
            Id = @event.Id,
            EntityType = SearchEntityTypes.Event,
            Title = DisplayTextSafety.CleanRequired(@event.Name, "Event"),
            Subtitle = FormatDateLabel(@event.StartDate, @event.EndDate),
            Description = TrimDescription(@event.Description, 160),
            Image = @event.Image,
            Route = $"/events/{@event.Id}",
            DestinationId = @event.DestinationId,
            DestinationName = DisplayTextSafety.CleanOptional(@event.DestinationName),
            Latitude = HasUsableCoordinates(@event.Latitude, @event.Longitude) ? @event.Latitude : null,
            Longitude = HasUsableCoordinates(@event.Latitude, @event.Longitude) ? @event.Longitude : null,
            CategoryLabel = DisplayTextSafety.CleanOptional(@event.EventTypeName),
            Tags = DisplayTextSafety.CleanLabels(@event.Tags.Select(tag => tag.Name)),
            Distance = SpatialDistanceFormatter.Format(@event.DistanceMeters),
            Score = Math.Round(score, 3),
            MatchedTerms = matchedTerms.OrderBy(term => term).Take(5).ToList()
        };
    }

    private SearchResultItemDto RankActivity(SearchTagProjection tag, SearchIntent intent)
    {
        var matchedTerms = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var score = 0.0;
        score += ScoreText(tag.Name, intent, matchedTerms, exact: 135, contains: 92, token: 40);
        score += ScoreText(tag.Slug, intent, matchedTerms, exact: 120, contains: 82, token: 36);

        if (intent.MatchedTagIds.Contains(tag.Id) || intent.MatchedTagSlugs.Contains(tag.Slug))
        {
            score += 115;
            matchedTerms.Add(tag.Name);
        }

        score += ScoreDomainHint(SearchDomains.Activities, intent);

        return new SearchResultItemDto
        {
            Id = tag.Id,
            EntityType = SearchEntityTypes.Activity,
            Title = DisplayTextSafety.CleanRequired(tag.Name, "Activity"),
            Subtitle = "Searchable activity",
            Description = TrimDescription(tag.Description, 120),
            Image = tag.Image,
            Route = RouteForActivity(tag),
            CategoryLabel = "Activity",
            Score = Math.Round(score, 3),
            MatchedTerms = matchedTerms.OrderBy(term => term).Take(5).ToList()
        };
    }

    private SearchResultItemDto RankCategory(SearchTypeProjection type, SearchIntent intent)
    {
        var matchedTerms = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var score = 0.0;
        score += ScoreText(type.Name, intent, matchedTerms, exact: 125, contains: 84, token: 35);

        if (intent.MatchedPlaceTypeIds.Contains(type.Id))
        {
            score += 105;
            matchedTerms.Add(type.Name);
        }

        score += ScoreDomainHint(SearchDomains.Activities, intent);

        return new SearchResultItemDto
        {
            Id = type.Id,
            EntityType = SearchEntityTypes.Activity,
            Title = DisplayTextSafety.CleanRequired(type.Name, "Category"),
            Subtitle = "Place category",
            Description = TrimDescription(type.Description, 120),
            Route = $"/local-places/{ToCategoryKey(type.Name)}",
            CategoryLabel = "Category",
            Score = Math.Round(score, 3),
            MatchedTerms = matchedTerms.OrderBy(term => term).Take(5).ToList()
        };
    }

    private SearchIntent BuildIntent(
        string rawQuery,
        SearchLanguageProfile profile,
        IReadOnlyCollection<DestinationVocabularyItem> destinations,
        SearchVocabulary vocabulary,
        double? latitude,
        double? longitude)
    {
        var normalized = Normalize(rawQuery);
        var tokens = Tokenize(normalized);
        var destination = DetectDestination(normalized, tokens, destinations);
        var meaningfulTokens = tokens
            .Where(token => !profile.StopWords.Contains(token))
            .Where(token => destination is null || !destination.NormalizedTokens.Contains(token))
            .ToList();

        var matchedTagIds = new HashSet<int>();
        var matchedTagSlugs = new HashSet<string>(StringComparer.Ordinal);
        var matchedPlaceTypeIds = new HashSet<int>();
        var matchedEventTypeIds = new HashSet<int>();
        var matchedAmenityIds = new HashSet<int>();
        var activityLabels = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var contentTypes = new HashSet<string>(StringComparer.Ordinal);

        foreach (var phraseAlias in profile.PhraseActivityAliases)
        {
            if (!ContainsPhrase(normalized, Normalize(phraseAlias.Key)))
            {
                continue;
            }

            foreach (var alias in phraseAlias.Value)
            {
                matchedTagSlugs.Add(alias);
                activityLabels.Add(Humanize(alias));
            }
        }

        foreach (var phraseAlias in profile.PhraseContentTypeAliases)
        {
            if (!ContainsPhrase(normalized, Normalize(phraseAlias.Key)))
            {
                continue;
            }

            foreach (var domain in phraseAlias.Value)
            {
                contentTypes.Add(domain);
            }
        }

        foreach (var token in meaningfulTokens)
        {
            if (profile.ActivityAliases.TryGetValue(token, out var aliases))
            {
                foreach (var alias in aliases)
                {
                    matchedTagSlugs.Add(alias);
                    activityLabels.Add(Humanize(alias));
                }
            }

            if (profile.ContentTypeAliases.TryGetValue(token, out var domains))
            {
                foreach (var domain in domains)
                {
                    contentTypes.Add(domain);
                }
            }
        }

        foreach (var tag in vocabulary.Tags)
        {
            var normalizedName = Normalize(tag.Name);
            var normalizedSlug = Normalize(tag.Slug);
            if (matchedTagSlugs.Contains(tag.Slug) ||
                meaningfulTokens.Any(token => TokenMatchesValue(token, normalizedName) || TokenMatchesValue(token, normalizedSlug)) ||
                (!string.IsNullOrEmpty(normalizedName) && normalized.Contains(normalizedName, StringComparison.Ordinal)))
            {
                matchedTagIds.Add(tag.Id);
                matchedTagSlugs.Add(tag.Slug);
                activityLabels.Add(tag.Name);
            }
        }

        foreach (var type in vocabulary.PlaceTypes)
        {
            var normalizedName = Normalize(type.Name);
            if (meaningfulTokens.Any(token => TokenMatchesValue(token, normalizedName)) ||
                (!string.IsNullOrEmpty(normalizedName) && normalized.Contains(normalizedName, StringComparison.Ordinal)))
            {
                matchedPlaceTypeIds.Add(type.Id);
                contentTypes.Add(IsStayTypeName(normalizedName) ? SearchDomains.Stays : SearchDomains.Places);
                activityLabels.Add(type.Name);
            }
        }

        foreach (var type in vocabulary.EventTypes)
        {
            var normalizedName = Normalize(type.Name);
            if (meaningfulTokens.Any(token => TokenMatchesValue(token, normalizedName)) ||
                (!string.IsNullOrEmpty(normalizedName) && normalized.Contains(normalizedName, StringComparison.Ordinal)))
            {
                matchedEventTypeIds.Add(type.Id);
                contentTypes.Add(SearchDomains.Events);
                activityLabels.Add(type.Name);
            }
        }

        foreach (var amenity in vocabulary.Amenities)
        {
            var normalizedName = Normalize(amenity.Name);
            var normalizedIcon = Normalize(amenity.Icon);
            if (meaningfulTokens.Any(token => TokenMatchesValue(token, normalizedName) || TokenMatchesValue(token, normalizedIcon)))
            {
                matchedAmenityIds.Add(amenity.Id);
                contentTypes.Add(SearchDomains.Stays);
                activityLabels.Add(amenity.Name);
            }
        }

        var userPoint = BuildUserPoint(latitude, longitude);
        var destinationPoint = destination is not null && HasUsableCoordinates(destination.Latitude, destination.Longitude)
            ? SpatialPointFactory.Create(destination.Latitude, destination.Longitude)
            : null;

        return new SearchIntent
        {
            RawQuery = rawQuery,
            NormalizedQuery = normalized,
            Tokens = tokens,
            MeaningfulTokens = meaningfulTokens,
            Destination = destination,
            MatchedTagIds = matchedTagIds,
            MatchedTagSlugs = matchedTagSlugs,
            MatchedPlaceTypeIds = matchedPlaceTypeIds,
            MatchedEventTypeIds = matchedEventTypeIds,
            MatchedAmenityIds = matchedAmenityIds,
            ActivityLabels = activityLabels,
            ContentTypes = contentTypes,
            UserPoint = userPoint,
            ActivePoint = destinationPoint ?? userPoint
        };
    }

    private static DestinationVocabularyItem? DetectDestination(
        string normalizedQuery,
        IReadOnlyCollection<string> tokens,
        IEnumerable<DestinationVocabularyItem> destinations)
    {
        return destinations
            .Select(destination =>
            {
                var score = 0;
                if (normalizedQuery == destination.NormalizedName)
                {
                    score += 300;
                }
                else if (ContainsPhrase(normalizedQuery, destination.NormalizedName))
                {
                    score += 220;
                }

                score += destination.NormalizedTokens.Count(token => tokens.Contains(token)) * 60;
                return new { Destination = destination, Score = score };
            })
            .Where(candidate => candidate.Score > 0)
            .OrderByDescending(candidate => candidate.Score)
            .ThenByDescending(candidate => candidate.Destination.NormalizedName.Length)
            .Select(candidate => candidate.Destination)
            .FirstOrDefault();
    }

    private async Task<List<DestinationVocabularyItem>> LoadDestinationVocabularyAsync(CancellationToken cancellationToken)
    {
        var destinations = await _db.Destinations
            .AsNoTracking()
            .Select(destination => new
            {
                destination.Id,
                destination.Name,
                destination.Latitude,
                destination.Longitude
            })
            .ToListAsync(cancellationToken);

        return destinations.Select(destination => new DestinationVocabularyItem
        {
            Id = destination.Id,
            Name = destination.Name,
            Latitude = destination.Latitude,
            Longitude = destination.Longitude,
            NormalizedName = Normalize(destination.Name),
            NormalizedTokens = Tokenize(Normalize(destination.Name)).ToHashSet(StringComparer.Ordinal)
        }).ToList();
    }

    private async Task<SearchVocabulary> LoadVocabularyAsync(CancellationToken cancellationToken)
    {
        var tags = await _db.Tags
            .AsNoTracking()
            .Select(tag => new SearchTagProjection
            {
                Id = tag.Id,
                Name = tag.Name,
                Slug = tag.Slug,
                Description = tag.Description,
                ForDestination = tag.ForDestination,
                ForPlace = tag.ForPlace,
                ForEvent = tag.ForEvent
            })
            .ToListAsync(cancellationToken);

        var placeTypes = await _db.PlaceTypes
            .AsNoTracking()
            .Select(type => new SearchTypeProjection
            {
                Id = type.Id,
                Name = type.Name,
                Description = type.Description
            })
            .ToListAsync(cancellationToken);

        var eventTypes = await _db.EventTypes
            .AsNoTracking()
            .Select(type => new SearchTypeProjection
            {
                Id = type.Id,
                Name = type.Name,
                Description = type.Description
            })
            .ToListAsync(cancellationToken);

        var amenities = await _db.Amenities
            .AsNoTracking()
            .Select(amenity => new SearchAmenityProjection
            {
                Id = amenity.Id,
                Name = amenity.Name,
                Icon = amenity.Icon
            })
            .ToListAsync(cancellationToken);

        return new SearchVocabulary
        {
            Tags = tags,
            PlaceTypes = placeTypes,
            EventTypes = eventTypes,
            Amenities = amenities
        };
    }

    private static GlobalSearchIntentDto MapIntent(SearchIntent intent) => new()
    {
        Destination = intent.Destination is null
            ? null
            : new SearchIntentDestinationDto
            {
                Id = intent.Destination.Id,
                Name = intent.Destination.Name,
                Latitude = intent.Destination.Latitude,
                Longitude = intent.Destination.Longitude
            },
        Activities = intent.ActivityLabels.OrderBy(label => label).Take(6).ToList(),
        ContentTypes = intent.ContentTypes.OrderBy(type => Array.IndexOf(DomainOrder, type)).ToList(),
        UsesCurrentLocation = intent.Destination is null && intent.UserPoint is not null
    };

    private static List<SearchResultGroupDto> BuildGroups(
        IReadOnlyCollection<SearchResultItemDto> results,
        int take,
        SearchResultItemDto? topResult)
    {
        var groups = new List<SearchResultGroupDto>();
        foreach (var domain in DomainOrder)
        {
            var items = results
                .Where(result => topResult is null ||
                    result.EntityType != topResult.EntityType ||
                    result.Id != topResult.Id)
                .Where(result => DomainForEntityType(result.EntityType) == domain)
                .OrderByDescending(result => result.Score)
                .Take(take)
                .ToList();

            if (items.Count == 0)
            {
                continue;
            }

            groups.Add(new SearchResultGroupDto
            {
                Key = domain,
                Label = LabelForDomain(domain),
                Items = items
            });
        }

        return groups;
    }

    private static double ScoreDestination(
        int destinationId,
        string? destinationName,
        SearchIntent intent,
        ISet<string> matchedTerms)
    {
        if (intent.Destination is null)
        {
            return 0;
        }

        if (destinationId == intent.Destination.Id)
        {
            matchedTerms.Add(intent.Destination.Name);
            return 150;
        }

        var score = ScoreText(destinationName, intent, matchedTerms, exact: 80, contains: 45, token: 18);
        return score > 0 ? score : -15;
    }

    private static double ScoreText(
        string? value,
        SearchIntent intent,
        ISet<string> matchedTerms,
        double exact,
        double contains,
        double token)
    {
        var normalizedValue = Normalize(value);
        if (string.IsNullOrWhiteSpace(normalizedValue))
        {
            return 0;
        }

        var score = 0.0;
        if (normalizedValue == intent.NormalizedQuery)
        {
            matchedTerms.Add(value!.Trim());
            return exact;
        }

        if (ContainsPhrase(normalizedValue, intent.NormalizedQuery))
        {
            matchedTerms.Add(value!.Trim());
            score += contains;
        }

        foreach (var term in intent.MeaningfulTokens)
        {
            if (term.Length < 2 || !TokenMatchesValue(term, normalizedValue))
            {
                continue;
            }

            score += token;
            matchedTerms.Add(Humanize(term));
        }

        return score;
    }

    private static double ScoreTags(
        IEnumerable<SearchTagProjection> tags,
        SearchIntent intent,
        ISet<string> matchedTerms,
        double exact)
    {
        var score = 0.0;
        foreach (var tag in tags)
        {
            var normalizedName = Normalize(tag.Name);
            var normalizedSlug = Normalize(tag.Slug);
            var strongMatch = intent.MatchedTagIds.Contains(tag.Id) ||
                intent.MatchedTagSlugs.Contains(tag.Slug);

            if (strongMatch)
            {
                score += exact;
                matchedTerms.Add(tag.Name);
                continue;
            }

            if (intent.MeaningfulTokens.Any(token =>
                TokenMatchesValue(token, normalizedName) || TokenMatchesValue(token, normalizedSlug)))
            {
                score += exact * 0.72;
                matchedTerms.Add(tag.Name);
            }
        }

        return score;
    }

    private static double ScoreAmenities(
        IEnumerable<SearchAmenityProjection> amenities,
        SearchIntent intent,
        ISet<string> matchedTerms)
    {
        var score = 0.0;
        foreach (var amenity in amenities)
        {
            if (intent.MatchedAmenityIds.Contains(amenity.Id))
            {
                score += 86;
                matchedTerms.Add(amenity.Name);
                continue;
            }

            var normalizedName = Normalize(amenity.Name);
            var normalizedIcon = Normalize(amenity.Icon);
            if (intent.MeaningfulTokens.Any(token =>
                TokenMatchesValue(token, normalizedName) || TokenMatchesValue(token, normalizedIcon)))
            {
                score += 62;
                matchedTerms.Add(amenity.Name);
            }
        }

        return score;
    }

    private static double ScoreDomainHint(string domain, SearchIntent intent)
    {
        if (intent.ContentTypes.Count == 0)
        {
            return 0;
        }

        return intent.ContentTypes.Contains(domain) ? 80 : -18;
    }

    private static double ScoreDistance(double? distanceMeters, SearchIntent intent)
    {
        if (!distanceMeters.HasValue)
        {
            return 0;
        }

        var distanceKm = Math.Max(0, distanceMeters.Value / 1000d);
        if (intent.Destination is not null)
        {
            return Math.Max(0, 42 - distanceKm * 0.55);
        }

        return Math.Max(0, 44 - distanceKm * 1.1);
    }

    private static SearchLanguageProfile ResolveProfile(string? language)
    {
        var normalized = string.IsNullOrWhiteSpace(language)
            ? LanguageEnglish
            : language.Trim().ToLowerInvariant();

        return normalized switch
        {
            LanguageEnglish => EnglishProfile,
            _ => EnglishProfile
        };
    }

    private static HashSet<string> ParseDomains(string? domains)
    {
        if (string.IsNullOrWhiteSpace(domains))
        {
            return DomainOrder.ToHashSet(StringComparer.Ordinal);
        }

        var selected = domains
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(domain => domain.ToLowerInvariant())
            .Where(domain => DomainOrder.Contains(domain))
            .ToHashSet(StringComparer.Ordinal);

        return selected.Count == 0
            ? DomainOrder.ToHashSet(StringComparer.Ordinal)
            : selected;
    }

    private static string DomainForEntityType(string entityType) => entityType switch
    {
        SearchEntityTypes.Destination => SearchDomains.Destinations,
        SearchEntityTypes.Stay => SearchDomains.Stays,
        SearchEntityTypes.Event => SearchDomains.Events,
        SearchEntityTypes.Activity => SearchDomains.Activities,
        _ => SearchDomains.Places
    };

    private static string LabelForDomain(string domain) => domain switch
    {
        SearchDomains.Destinations => "Destinations",
        SearchDomains.Places => "Places",
        SearchDomains.Stays => "Stays",
        SearchDomains.Events => "Events",
        SearchDomains.Activities => "Activities",
        _ => "Results"
    };

    private static string? RouteForActivity(SearchTagProjection tag)
    {
        var slug = tag.Slug.ToLowerInvariant();
        if (new[] { "food-drink", "fine-dining", "beach-bars", "nightlife" }.Contains(slug))
        {
            return "/food-and-music";
        }

        if (new[] { "history", "culture", "museums", "landmarks", "historic-sites", "galleries", "exhibitions" }.Contains(slug))
        {
            return "/culture-and-history";
        }

        if (new[] { "nature", "beaches", "islands", "parks", "mountains", "hiking", "outdoor", "adventure", "scenic-views" }.Contains(slug))
        {
            return "/nature";
        }

        if (new[] { "spa", "pool", "breakfast", "luxury", "budget", "family-friendly", "sea-view", "boutique" }.Contains(slug))
        {
            return "/hotels";
        }

        if (tag.ForEvent)
        {
            return "/events";
        }

        return "/local-places";
    }

    private static string LikePattern(string value) => $"%{value.Trim().Replace("%", "\\%").Replace("_", "\\_")}%";

    private static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var decomposed = value.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (var c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(c);
            }
        }

        var separated = SeparatorPattern.Replace(builder.ToString().Normalize(NormalizationForm.FormC), " ");
        return WhitespacePattern.Replace(separated, " ").Trim();
    }

    private static List<string> Tokenize(string normalized) =>
        normalized
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .SelectMany(TokenVariants)
            .Distinct(StringComparer.Ordinal)
            .ToList();

    private static IEnumerable<string> TokenVariants(string token)
    {
        yield return token;

        if (token.EndsWith("ies", StringComparison.Ordinal) && token.Length > 3)
        {
            yield return token[..^3] + "y";
        }
        else if (token.EndsWith("s", StringComparison.Ordinal) && token.Length > 2)
        {
            yield return token[..^1];
        }
        else if (token.Length > 2)
        {
            yield return token + "s";
        }
    }

    private static bool TokenMatchesValue(string token, string normalizedValue) =>
        normalizedValue == token ||
        normalizedValue.StartsWith(token + " ", StringComparison.Ordinal) ||
        normalizedValue.EndsWith(" " + token, StringComparison.Ordinal) ||
        normalizedValue.Contains(" " + token + " ", StringComparison.Ordinal) ||
        normalizedValue.Contains(token, StringComparison.Ordinal);

    private static bool ContainsPhrase(string normalizedValue, string normalizedPhrase) =>
        !string.IsNullOrWhiteSpace(normalizedPhrase) &&
        (normalizedValue == normalizedPhrase ||
         normalizedValue.StartsWith(normalizedPhrase + " ", StringComparison.Ordinal) ||
         normalizedValue.EndsWith(" " + normalizedPhrase, StringComparison.Ordinal) ||
         normalizedValue.Contains(" " + normalizedPhrase + " ", StringComparison.Ordinal));

    private static string Humanize(string value)
    {
        var normalized = Normalize(value);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return value;
        }

        return string.Join(
            ' ',
            normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Select(word => word.Length == 1
                    ? word.ToUpperInvariant()
                    : char.ToUpperInvariant(word[0]) + word[1..]));
    }

    private static string ToCategoryKey(string value) =>
        Normalize(value).Replace(" ", "-");

    private static bool IsStayTypeName(string normalizedName) =>
        Tokenize(normalizedName).Any(StayTypeTokens.Contains);

    private static Point? BuildUserPoint(double? latitude, double? longitude)
    {
        return HasUsableCoordinates(latitude, longitude)
            ? SpatialPointFactory.Create(latitude!.Value, longitude!.Value)
            : null;
    }

    private static bool HasUsableCoordinates(double? latitude, double? longitude) =>
        latitude.HasValue &&
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

    private static string? FormatReviewCountLabel(int count) => count switch
    {
        0 => null,
        1 => "1 review",
        _ => $"{count} reviews"
    };

    private static List<SearchFilterOptionDto> CleanFilterOptions(
        IEnumerable<SearchFilterOptionDto> options,
        string fallbackLabel)
    {
        return options
            .Select(option => new SearchFilterOptionDto
            {
                Id = option.Id,
                Label = DisplayTextSafety.CleanRequired(option.Label, fallbackLabel),
                Domain = option.Domain,
                Count = option.Count
            })
            .DistinctBy(option => option.Label.ToLowerInvariant())
            .ToList();
    }

    private static string FormatDateLabel(DateTime start, DateTime end)
    {
        return start.Date == end.Date
            ? start.ToString("d MMM yyyy")
            : $"{start:d MMM} - {end:d MMM yyyy}";
    }

    private sealed record SearchLanguageProfile(
        string LanguageCode,
        IReadOnlySet<string> StopWords,
        IReadOnlyDictionary<string, string[]> PhraseActivityAliases,
        IReadOnlyDictionary<string, string[]> ActivityAliases,
        IReadOnlyDictionary<string, string[]> PhraseContentTypeAliases,
        IReadOnlyDictionary<string, string[]> ContentTypeAliases);

    private sealed class SearchIntent
    {
        public string RawQuery { get; init; } = string.Empty;
        public string NormalizedQuery { get; init; } = string.Empty;
        public List<string> Tokens { get; init; } = new();
        public List<string> MeaningfulTokens { get; init; } = new();
        public DestinationVocabularyItem? Destination { get; init; }
        public HashSet<int> MatchedTagIds { get; init; } = new();
        public HashSet<string> MatchedTagSlugs { get; init; } = new(StringComparer.Ordinal);
        public HashSet<int> MatchedPlaceTypeIds { get; init; } = new();
        public HashSet<int> MatchedEventTypeIds { get; init; } = new();
        public HashSet<int> MatchedAmenityIds { get; init; } = new();
        public HashSet<string> ActivityLabels { get; init; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> ContentTypes { get; init; } = new(StringComparer.Ordinal);
        public Point? UserPoint { get; init; }
        public Point? ActivePoint { get; init; }
        public bool HasDiscoveryIntent =>
            ActivityLabels.Count > 0 ||
            ContentTypes.Count > 0 ||
            MatchedTagIds.Count > 0 ||
            MatchedTagSlugs.Count > 0 ||
            MatchedPlaceTypeIds.Count > 0 ||
            MatchedEventTypeIds.Count > 0 ||
            MatchedAmenityIds.Count > 0;
    }

    private sealed class SearchVocabulary
    {
        public List<SearchTagProjection> Tags { get; init; } = new();
        public List<SearchTypeProjection> PlaceTypes { get; init; } = new();
        public List<SearchTypeProjection> EventTypes { get; init; } = new();
        public List<SearchAmenityProjection> Amenities { get; init; } = new();
    }

    private sealed class DestinationVocabularyItem
    {
        public int Id { get; init; }
        public string Name { get; init; } = string.Empty;
        public double Latitude { get; init; }
        public double Longitude { get; init; }
        public string NormalizedName { get; init; } = string.Empty;
        public HashSet<string> NormalizedTokens { get; init; } = new(StringComparer.Ordinal);
    }

    private sealed class DestinationSearchProjection
    {
        public int Id { get; init; }
        public string Name { get; init; } = string.Empty;
        public string Description { get; init; } = string.Empty;
        public string? DestinationTypeName { get; init; }
        public string? Image { get; init; }
        public double Latitude { get; init; }
        public double Longitude { get; init; }
        public int ReviewCount { get; init; }
        public List<SearchTagProjection> Tags { get; init; } = new();
    }

    private sealed class PlaceSearchProjection
    {
        public int Id { get; init; }
        public string Name { get; init; } = string.Empty;
        public string? Subtitle { get; init; }
        public string Description { get; init; } = string.Empty;
        public string? Image { get; init; }
        public string? Address { get; init; }
        public string? OpeningHours { get; init; }
        public decimal? PricePerNight { get; init; }
        public string? PlaceTypeName { get; init; }
        public string? LocationName { get; init; }
        public int DestinationId { get; init; }
        public string? DestinationName { get; init; }
        public double Latitude { get; init; }
        public double Longitude { get; init; }
        public double? AverageRating { get; init; }
        public int ReviewCount { get; init; }
        public double? DistanceMeters { get; init; }
        public List<SearchTagProjection> Tags { get; init; } = new();
        public List<SearchAmenityProjection> Amenities { get; init; } = new();
    }

    private sealed class EventSearchProjection
    {
        public int Id { get; init; }
        public string Name { get; init; } = string.Empty;
        public string Description { get; init; } = string.Empty;
        public string? EventTypeName { get; init; }
        public string? LocationName { get; init; }
        public int DestinationId { get; init; }
        public string? DestinationName { get; init; }
        public double? Latitude { get; init; }
        public double? Longitude { get; init; }
        public string? Image { get; init; }
        public DateTime StartDate { get; init; }
        public DateTime EndDate { get; init; }
        public double? DistanceMeters { get; init; }
        public List<SearchTagProjection> Tags { get; init; } = new();
    }

    private sealed class SearchTagProjection
    {
        public int Id { get; init; }
        public string Name { get; init; } = string.Empty;
        public string Slug { get; init; } = string.Empty;
        public string Description { get; init; } = string.Empty;
        public string? Image { get; init; }
        public bool ForDestination { get; init; }
        public bool ForPlace { get; init; }
        public bool ForEvent { get; init; }
    }

    private sealed class SearchTypeProjection
    {
        public int Id { get; init; }
        public string Name { get; init; } = string.Empty;
        public string Description { get; init; } = string.Empty;
    }

    private sealed class SearchAmenityProjection
    {
        public int Id { get; init; }
        public string Name { get; init; } = string.Empty;
        public string Icon { get; init; } = string.Empty;
    }

    private static class SearchDomains
    {
        public const string Destinations = "destinations";
        public const string Places = "places";
        public const string Stays = "stays";
        public const string Events = "events";
        public const string Activities = "activities";
    }

    private static class SearchEntityTypes
    {
        public const string Destination = "destination";
        public const string Place = "place";
        public const string Stay = "stay";
        public const string Event = "event";
        public const string Activity = "activity";
    }
}
