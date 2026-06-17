using Geotrack.Domain.Entities.Core;
using System.Globalization;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Geotrack.Application.DTOs.Map;
using Geotrack.Application.Interfaces.Service;
using Geotrack.Infrastructure.Search;
using Geotrack.Infrastructure.Spatial;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;

namespace Geotrack.Infrastructure.Services;

public class MapService : IMapService
{
    private const string NominatimBaseUrl = "https://nominatim.openstreetmap.org";
    private const string OsrmBaseUrl = "https://router.project-osrm.org/route/v1/foot";
    private const string OsrmCarBaseUrl = "https://router.project-osrm.org/route/v1/car";
    private const double DestinationZoomThreshold = 10d;
    private const string PublishedStatus = "Published";
    private static readonly TimeSpan SearchCacheDuration = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan ReverseGeocodeCacheDuration = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan RouteCacheDuration = TimeSpan.FromMinutes(5);

    private static readonly HashSet<string> AllowedCategories = new(StringComparer.OrdinalIgnoreCase)
    {
        "places",
        "stays",
        "events",
    };

    private static readonly HashSet<string> SearchTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "destination",
        "location",
        "place",
        "event",
    };

    private static readonly GeometryFactory ViewportGeometryFactory = new(new PrecisionModel(), 4326);

    private readonly HttpClient _httpClient;
    private readonly AppDbContext _db;
    private readonly IFavoriteService _favoriteService;
    private readonly IMemoryCache _cache;
    private readonly ILogger<MapService> _logger;

    public MapService(
        HttpClient httpClient,
        AppDbContext db,
        IFavoriteService favoriteService,
        IMemoryCache cache,
        ILogger<MapService> logger)
    {
        _httpClient = httpClient;
        _db = db;
        _favoriteService = favoriteService;
        _cache = cache;
        _logger = logger;

        if (!_httpClient.DefaultRequestHeaders.UserAgent.Any())
        {
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("GeoTrack/1.0");
        }
    }

    public async Task<List<MapSearchResultDto>> SearchAsync(string query, CancellationToken cancellationToken = default)
    {
        var trimmed = query.Trim();
        if (trimmed.Length < 2)
        {
            return new List<MapSearchResultDto>();
        }

        var cacheKey = BuildCacheKey("map:search:v1", trimmed.ToLowerInvariant());
        if (_cache.TryGetValue<List<MapSearchResultDto>>(cacheKey, out var cachedResults))
        {
            return cachedResults ?? new List<MapSearchResultDto>();
        }

        var url =
            $"{NominatimBaseUrl}/search?format=jsonv2&q={Uri.EscapeDataString(trimmed)}&limit=5&accept-language=en&addressdetails=1&namedetails=0";

        List<NominatimSearchResult> results;
        try
        {
            results = await _httpClient.GetFromJsonAsync<List<NominatimSearchResult>>(url, cancellationToken)
                ?? new List<NominatimSearchResult>();
        }
        catch (Exception ex) when (IsProviderFailure(ex, cancellationToken))
        {
            LogProviderFailure("nominatim-search", ex);
            return new List<MapSearchResultDto>();
        }

        var mappedResults = results
            .Where(result => TryParseCoordinate(result.Lat, out _) && TryParseCoordinate(result.Lon, out _))
            .Select(result => new MapSearchResultDto
            {
                Name = BuildSearchTitle(result),
                Subtitle = BuildSearchSubtitle(result.DisplayName),
                Latitude = ParseCoordinate(result.Lat),
                Longitude = ParseCoordinate(result.Lon),
            })
            .ToList();

        _cache.Set(
            cacheKey,
            mappedResults,
            new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = SearchCacheDuration
            });

        return mappedResults;
    }

    public async Task<List<MapSearchResultDto>> SearchAsync(
        string query,
        IEnumerable<string>? types,
        CancellationToken cancellationToken = default)
    {
        var trimmed = query.Trim();
        if (trimmed.Length < 2)
        {
            return new List<MapSearchResultDto>();
        }

        var requestedTypes = (types ?? Enumerable.Empty<string>())
            .Select(type => type.Trim().ToLowerInvariant())
            .Where(type => SearchTypes.Contains(type))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (requestedTypes.Count == 0)
        {
            requestedTypes = SearchTypes.ToHashSet(StringComparer.OrdinalIgnoreCase);
        }

        var searchTerm = SearchTextNormalizer.Normalize(trimmed);
        var searchTerms = searchTerm
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (searchTerm.Length < 2 || searchTerms.Length == 0)
        {
            return new List<MapSearchResultDto>();
        }

        const int candidateLimitPerType = 120;
        var candidates = new List<MapSearchProjection>();

        if (requestedTypes.Contains("destination"))
        {
            var destinations = _db.Destinations
                .AsNoTracking()
                .Where(destination => destination.Status == PublishedStatus);

            foreach (var term in searchTerms)
            {
                var token = term;
                destinations = destinations.Where(destination =>
                    destination.SearchText.Contains(token) ||
                    destination.Name.ToLower().Contains(token) ||
                    (destination.Summary != null && destination.Summary.ToLower().Contains(token)) ||
                    destination.Description.ToLower().Contains(token) ||
                    (destination.InternalReferenceId != null && destination.InternalReferenceId.ToLower().Contains(token)) ||
                    destination.Region.Name.ToLower().Contains(token) ||
                    (destination.DestinationType != null && destination.DestinationType.Name.ToLower().Contains(token)));
            }

            candidates.AddRange(await destinations
                .OrderByDescending(destination => destination.SearchText.StartsWith(searchTerm))
                .ThenByDescending(destination => destination.SearchText.Contains(searchTerm))
                .ThenBy(destination => destination.Name)
                .Take(candidateLimitPerType)
                .Select(destination => new MapSearchProjection
                {
                    Id = destination.Id,
                    Type = "destination",
                    Name = destination.Name,
                    Description = destination.Description,
                    Subtitle = destination.Region.Name,
                    Latitude = destination.Latitude,
                    Longitude = destination.Longitude,
                    ParentName = destination.Region.Name,
                    OwnSearchText = destination.Name,
                    ContentSearchText = destination.SearchText + " " +
                        (destination.Summary ?? string.Empty) + " " +
                        destination.Description,
                    ParentSearchText = destination.Region.Name,
                    TypeSearchText = destination.DestinationType != null ? destination.DestinationType.Name : string.Empty,
                    IdentifierSearchText = destination.InternalReferenceId ?? string.Empty,
                })
                .ToListAsync(cancellationToken));
        }

        if (requestedTypes.Contains("location"))
        {
            var locations = _db.Locations
                .AsNoTracking()
                .Where(location =>
                    location.Status == PublishedStatus &&
                    location.Destination.Status == PublishedStatus);

            foreach (var term in searchTerms)
            {
                var token = term;
                locations = locations.Where(location =>
                    location.SearchText.Contains(token) ||
                    location.Name.ToLower().Contains(token) ||
                    (location.Summary != null && location.Summary.ToLower().Contains(token)) ||
                    location.Description.ToLower().Contains(token) ||
                    (location.InternalReferenceId != null && location.InternalReferenceId.ToLower().Contains(token)) ||
                    location.Destination.SearchText.Contains(token) ||
                    location.Destination.Name.ToLower().Contains(token) ||
                    location.LocationType.Name.ToLower().Contains(token));
            }

            candidates.AddRange(await locations
                .OrderByDescending(location => location.SearchText.StartsWith(searchTerm))
                .ThenByDescending(location => location.SearchText.Contains(searchTerm))
                .ThenByDescending(location => location.Destination.SearchText.Contains(searchTerm))
                .ThenBy(location => location.Name)
                .Take(candidateLimitPerType)
                .Select(location => new MapSearchProjection
                {
                    Id = location.Id,
                    Type = "location",
                    Name = location.Name,
                    Description = location.Description,
                    Subtitle = location.Destination.Name,
                    Latitude = location.Latitude,
                    Longitude = location.Longitude,
                    ParentName = location.Destination.Name,
                    OwnSearchText = location.Name,
                    ContentSearchText = location.SearchText + " " +
                        (location.Summary ?? string.Empty) + " " +
                        location.Description,
                    ParentSearchText = location.Destination.Name,
                    TypeSearchText = location.LocationType.Name,
                    IdentifierSearchText = location.InternalReferenceId ?? string.Empty,
                    DestinationId = location.DestinationId,
                })
                .ToListAsync(cancellationToken));
        }

        if (requestedTypes.Contains("place"))
        {
            var places = _db.Places
                .AsNoTracking()
                .Where(place =>
                    place.Status == PublishedStatus &&
                    place.Location.Status == PublishedStatus &&
                    place.Location.Destination.Status == PublishedStatus);

            foreach (var term in searchTerms)
            {
                var token = term;
                places = places.Where(place =>
                    place.SearchText.Contains(token) ||
                    place.Name.ToLower().Contains(token) ||
                    (place.Subtitle != null && place.Subtitle.ToLower().Contains(token)) ||
                    (place.Address != null && place.Address.ToLower().Contains(token)) ||
                    place.Description.ToLower().Contains(token) ||
                    (place.InternalReferenceId != null && place.InternalReferenceId.ToLower().Contains(token)) ||
                    place.Location.SearchText.Contains(token) ||
                    place.Location.Name.ToLower().Contains(token) ||
                    place.Location.Destination.SearchText.Contains(token) ||
                    place.Location.Destination.Name.ToLower().Contains(token) ||
                    place.PlaceType.Name.ToLower().Contains(token) ||
                    (place.Stay != null &&
                        (place.Stay.StayType.Name.ToLower().Contains(token) ||
                         place.Stay.StayType.Slug.ToLower().Contains(token))));
            }

            candidates.AddRange(await places
                .OrderByDescending(place => place.SearchText.StartsWith(searchTerm))
                .ThenByDescending(place => place.SearchText.Contains(searchTerm))
                .ThenByDescending(place => place.Location.SearchText.Contains(searchTerm))
                .ThenByDescending(place => place.Location.Destination.SearchText.Contains(searchTerm))
                .ThenBy(place => place.Name)
                .Take(candidateLimitPerType)
                .Select(place => new MapSearchProjection
                {
                    Id = place.Id,
                    Type = place.Stay != null ? "stay" : "place",
                    Name = place.Name,
                    Description = place.Description,
                    Subtitle = place.Location.Destination.Name + " · " + place.Location.Name,
                    Latitude = place.Latitude,
                    Longitude = place.Longitude,
                    ParentName = place.Location.Name,
                    OwnSearchText = place.Name,
                    ContentSearchText = place.SearchText + " " +
                        (place.Subtitle ?? string.Empty) + " " +
                        (place.Address ?? string.Empty) + " " +
                        place.Description,
                    ParentSearchText = place.Location.Name,
                    DestinationSearchText = place.Location.Destination.Name,
                    TypeSearchText = place.Stay != null ? place.Stay.StayType.Name : place.PlaceType.Name,
                    IdentifierSearchText = place.InternalReferenceId ?? string.Empty,
                    LocationId = place.LocationId,
                    DestinationId = place.Location.DestinationId,
                })
                .ToListAsync(cancellationToken));
        }

        if (requestedTypes.Contains("event"))
        {
            var events = _db.Events
                .AsNoTracking()
                .Where(@event =>
                    @event.Status == PublishedStatus &&
                    @event.Destination.Status == PublishedStatus &&
                    (@event.Location == null || @event.Location.Status == PublishedStatus) &&
                    (@event.Place == null ||
                        (@event.Place.Status == PublishedStatus &&
                         @event.Place.Location.Status == PublishedStatus &&
                         @event.Place.Location.Destination.Status == PublishedStatus)));

            foreach (var term in searchTerms)
            {
                var token = term;
                events = events.Where(@event =>
                    @event.SearchText.Contains(token) ||
                    @event.Name.ToLower().Contains(token) ||
                    (@event.Summary != null && @event.Summary.ToLower().Contains(token)) ||
                    @event.Description.ToLower().Contains(token) ||
                    (@event.VenueName != null && @event.VenueName.ToLower().Contains(token)) ||
                    (@event.InternalReferenceId != null && @event.InternalReferenceId.ToLower().Contains(token)) ||
                    @event.Destination.SearchText.Contains(token) ||
                    @event.Destination.Name.ToLower().Contains(token) ||
                    @event.EventType.Name.ToLower().Contains(token) ||
                    (@event.Location != null &&
                        (@event.Location.SearchText.Contains(token) ||
                         @event.Location.Name.ToLower().Contains(token))) ||
                    (@event.Place != null &&
                        (@event.Place.SearchText.Contains(token) ||
                         @event.Place.Name.ToLower().Contains(token) ||
                         (@event.Place.Address != null && @event.Place.Address.ToLower().Contains(token)) ||
                         @event.Place.Description.ToLower().Contains(token) ||
                         @event.Place.Location.SearchText.Contains(token) ||
                         @event.Place.Location.Name.ToLower().Contains(token) ||
                         @event.Place.Location.Destination.SearchText.Contains(token) ||
                         @event.Place.Location.Destination.Name.ToLower().Contains(token))));
            }

            candidates.AddRange(await events
                .OrderByDescending(@event => @event.SearchText.StartsWith(searchTerm))
                .ThenByDescending(@event => @event.SearchText.Contains(searchTerm))
                .ThenByDescending(@event => @event.Location != null && @event.Location.SearchText.Contains(searchTerm))
                .ThenByDescending(@event => @event.Destination.SearchText.Contains(searchTerm))
                .ThenBy(@event => @event.Name)
                .Take(candidateLimitPerType)
                .Select(@event => new MapSearchProjection
                {
                    Id = @event.Id,
                    Type = "event",
                    Name = @event.Name,
                    Description = @event.Description,
                    Subtitle = @event.VenueName ?? (@event.Place != null
                        ? @event.Place.Name
                        : @event.Location != null
                            ? @event.Location.Name
                            : @event.Destination.Name),
                    Latitude = @event.Place != null
                        ? @event.Place.Latitude
                        : @event.Location != null
                            ? @event.Location.Latitude
                            : @event.Destination.Latitude,
                    Longitude = @event.Place != null
                        ? @event.Place.Longitude
                        : @event.Location != null
                            ? @event.Location.Longitude
                            : @event.Destination.Longitude,
                    ParentName = @event.Place != null
                        ? @event.Place.Name
                        : @event.Location != null
                            ? @event.Location.Name
                            : @event.Destination.Name,
                    OwnSearchText = @event.Name,
                    ContentSearchText = @event.SearchText + " " +
                        (@event.Summary ?? string.Empty) + " " +
                        @event.Description + " " +
                        (@event.VenueName ?? string.Empty),
                    ParentSearchText = @event.Place != null
                        ? @event.Place.Name
                        : @event.Location != null
                            ? @event.Location.Name
                            : string.Empty,
                    AreaSearchText = @event.Place != null
                        ? @event.Place.Location.Name
                        : @event.Location != null
                            ? @event.Location.Name
                            : string.Empty,
                    DestinationSearchText = @event.Destination.Name,
                    TypeSearchText = @event.EventType.Name,
                    IdentifierSearchText = @event.InternalReferenceId ?? string.Empty,
                    LocationId = @event.Place != null ? @event.Place.LocationId : @event.LocationId,
                    DestinationId = @event.DestinationId,
                })
                .ToListAsync(cancellationToken));
        }

        var relevantCandidates = candidates
            .Where(candidate => IsMeaningfulSearchMatch(candidate, searchTerms))
            .ToList();

        await AddParentContextResultsAsync(
            relevantCandidates,
            requestedTypes,
            searchTerm,
            searchTerms,
            cancellationToken);

        return relevantCandidates
            .Select(candidate => new
            {
                Candidate = candidate,
                Score = CalculateSearchScore(candidate, searchTerm, searchTerms),
            })
            .OrderByDescending(item => item.Score)
            .ThenBy(item => item.Candidate.Name)
            .Take(30)
            .Select(item => item.Candidate.ToDto())
            .ToList();
    }

    public async Task<List<MapSearchResultDto>> GetAllDestinationsAsync(CancellationToken cancellationToken = default)
    {
        const string cacheKey = "map:destinations:v2";
        if (_cache.TryGetValue<List<MapSearchResultDto>>(cacheKey, out var cached))
            return cached ?? new List<MapSearchResultDto>();

        var destinations = await _db.Destinations
            .AsNoTracking()
            .Where(d => d.Status == PublishedStatus)
            .OrderBy(d => d.Name)
            .Select(d => new MapSearchResultDto
            {
                Id = d.Id,
                Type = "destination",
                Name = d.Name,
                Subtitle = d.Region.Name,
                Latitude = d.Latitude,
                Longitude = d.Longitude,
                ParentName = d.Region.Name,
            })
            .ToListAsync(cancellationToken);

        _cache.Set(cacheKey, destinations, new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(30)
        });

        return destinations;
    }

    public async Task<MapReverseGeocodeDto?> ReverseGeocodeAsync(
        double latitude,
        double longitude,
        CancellationToken cancellationToken = default)
    {
        if (!HasValidCoordinates(latitude, longitude))
        {
            return null;
        }

        var normalizedLatitude = NormalizeCoordinate(latitude);
        var normalizedLongitude = NormalizeCoordinate(longitude);
        var cacheKey = BuildCacheKey(
            "map:reverse:v1",
            $"{FormatCoordinate(normalizedLatitude)},{FormatCoordinate(normalizedLongitude)}");
        if (_cache.TryGetValue<MapReverseGeocodeDto>(cacheKey, out var cachedResult))
        {
            return cachedResult;
        }

        var url =
            $"{NominatimBaseUrl}/reverse?lat={FormatCoordinate(normalizedLatitude)}&lon={FormatCoordinate(normalizedLongitude)}&format=jsonv2&accept-language=en&addressdetails=1";

        NominatimReverseResult? result;
        try
        {
            result = await _httpClient.GetFromJsonAsync<NominatimReverseResult>(url, cancellationToken);
        }
        catch (Exception ex) when (IsProviderFailure(ex, cancellationToken))
        {
            LogProviderFailure("nominatim-reverse", ex);
            return null;
        }

        if (result is null)
        {
            return null;
        }

        var address = result.Address ?? new NominatimAddress();
        var city = FirstNonEmpty(
            address.City,
            address.Town,
            address.Municipality,
            address.Village,
            address.County,
            address.StateDistrict,
            address.State);
        var region = FirstNonEmpty(address.State, address.County);
        var country = address.Country ?? string.Empty;

        var displayLabel = BuildDisplayLabel(city, region, country);
        var reverseGeocode = new MapReverseGeocodeDto
        {
            Address = BuildStreetAddress(address, result.DisplayName, displayLabel),
            City = city,
            Region = region,
            Country = country,
            DisplayLabel = displayLabel,
        };

        _cache.Set(
            cacheKey,
            reverseGeocode,
            new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = ReverseGeocodeCacheDuration
            });

        return reverseGeocode;
    }

    public async Task<MapRouteResponseDto?> GetWalkingRouteAsync(
        double originLatitude,
        double originLongitude,
        double destinationLatitude,
        double destinationLongitude,
        CancellationToken cancellationToken = default)
    {
        if (!HasValidCoordinates(originLatitude, originLongitude) ||
            !HasValidCoordinates(destinationLatitude, destinationLongitude))
        {
            return null;
        }

        var normalizedOriginLatitude = NormalizeCoordinate(originLatitude);
        var normalizedOriginLongitude = NormalizeCoordinate(originLongitude);
        var normalizedDestinationLatitude = NormalizeCoordinate(destinationLatitude);
        var normalizedDestinationLongitude = NormalizeCoordinate(destinationLongitude);
        var cacheKey = BuildCacheKey(
            "map:route:v1",
            string.Join(
                ";",
                $"{FormatCoordinate(normalizedOriginLatitude)},{FormatCoordinate(normalizedOriginLongitude)}",
                $"{FormatCoordinate(normalizedDestinationLatitude)},{FormatCoordinate(normalizedDestinationLongitude)}"));
        if (_cache.TryGetValue<MapRouteResponseDto>(cacheKey, out var cachedRoute))
        {
            return cachedRoute;
        }

        var url =
            $"{OsrmBaseUrl}/{FormatCoordinate(normalizedOriginLongitude)},{FormatCoordinate(normalizedOriginLatitude)};{FormatCoordinate(normalizedDestinationLongitude)},{FormatCoordinate(normalizedDestinationLatitude)}?overview=full&geometries=geojson";

        OsrmRouteResponse? result;
        try
        {
            result = await _httpClient.GetFromJsonAsync<OsrmRouteResponse>(url, cancellationToken);
        }
        catch (Exception ex) when (IsProviderFailure(ex, cancellationToken))
        {
            LogProviderFailure("osrm-route", ex);
            return null;
        }

        var route = result?.Routes?.FirstOrDefault();
        if (!string.Equals(result?.Code, "Ok", StringComparison.OrdinalIgnoreCase) ||
            route?.Geometry?.Coordinates is not { Count: > 1 })
        {
            return null;
        }

        var rawCoords = route.Geometry.Coordinates
            .Where(pair => pair.Count >= 2)
            .Select(pair => new MapCoordinateDto
            {
                Latitude = pair[1],
                Longitude = pair[0],
            })
            .Where(point => HasValidCoordinates(point.Latitude, point.Longitude))
            .ToList();

        const double walkingSpeedMs = 5000.0 / 3600.0; // 5 km/h in m/s
        var mappedRoute = new MapRouteResponseDto
        {
            Coordinates = DecimateCoordinates(rawCoords),
            DistanceMeters = route.Distance,
            DurationSeconds = route.Distance > 0 ? route.Distance / walkingSpeedMs : route.Duration,
        };

        if (mappedRoute.Coordinates.Count <= 1)
        {
            return null;
        }

        _cache.Set(
            cacheKey,
            mappedRoute,
            new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = RouteCacheDuration
            });

        return mappedRoute;
    }

    public async Task<MapRouteResponseDto?> GetDrivingRouteAsync(
        double originLatitude,
        double originLongitude,
        double destinationLatitude,
        double destinationLongitude,
        CancellationToken cancellationToken = default)
    {
        if (!HasValidCoordinates(originLatitude, originLongitude) ||
            !HasValidCoordinates(destinationLatitude, destinationLongitude))
        {
            return null;
        }

        var normalizedOriginLatitude = NormalizeCoordinate(originLatitude);
        var normalizedOriginLongitude = NormalizeCoordinate(originLongitude);
        var normalizedDestinationLatitude = NormalizeCoordinate(destinationLatitude);
        var normalizedDestinationLongitude = NormalizeCoordinate(destinationLongitude);
        var cacheKey = BuildCacheKey(
            "map:driving-route:v1",
            string.Join(
                ";",
                $"{FormatCoordinate(normalizedOriginLatitude)},{FormatCoordinate(normalizedOriginLongitude)}",
                $"{FormatCoordinate(normalizedDestinationLatitude)},{FormatCoordinate(normalizedDestinationLongitude)}"));
        if (_cache.TryGetValue<MapRouteResponseDto>(cacheKey, out var cachedRoute))
        {
            return cachedRoute;
        }

        var url =
            $"{OsrmCarBaseUrl}/{FormatCoordinate(normalizedOriginLongitude)},{FormatCoordinate(normalizedOriginLatitude)};{FormatCoordinate(normalizedDestinationLongitude)},{FormatCoordinate(normalizedDestinationLatitude)}?overview=full&geometries=geojson";

        OsrmRouteResponse? result;
        try
        {
            result = await _httpClient.GetFromJsonAsync<OsrmRouteResponse>(url, cancellationToken);
        }
        catch (Exception ex) when (IsProviderFailure(ex, cancellationToken))
        {
            LogProviderFailure("osrm-driving-route", ex);
            return null;
        }

        var route = result?.Routes?.FirstOrDefault();
        if (!string.Equals(result?.Code, "Ok", StringComparison.OrdinalIgnoreCase) ||
            route?.Geometry?.Coordinates is not { Count: > 1 })
        {
            return null;
        }

        var rawDrivingCoords = route.Geometry.Coordinates
            .Where(pair => pair.Count >= 2)
            .Select(pair => new MapCoordinateDto
            {
                Latitude = pair[1],
                Longitude = pair[0],
            })
            .Where(point => HasValidCoordinates(point.Latitude, point.Longitude))
            .ToList();

        var mappedRoute = new MapRouteResponseDto
        {
            Coordinates = DecimateCoordinates(rawDrivingCoords),
            DistanceMeters = route.Distance,
            DurationSeconds = route.Duration,
        };

        if (mappedRoute.Coordinates.Count <= 1)
        {
            return null;
        }

        _cache.Set(
            cacheKey,
            mappedRoute,
            new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = RouteCacheDuration
            });

        return mappedRoute;
    }

    public async Task<GetMapEntitiesResponseDto> GetEntitiesAsync(
        double latitude,
        double longitude,
        IEnumerable<string>? categories,
        double? radiusKm,
        int? userId = null,
        CancellationToken cancellationToken = default)
    {
        var normalizedCategories = NormalizeCategories(categories);
        var effectiveRadiusKm = NormalizeRadiusKm(radiusKm);
        var radiusMeters = effectiveRadiusKm * 1000d;
        var userPoint = SpatialPointFactory.Create(latitude, longitude)
            ?? throw new InvalidOperationException("Valid coordinates are required.");

        var favoritePlaceIds = userId.HasValue
            ? await _favoriteService.GetFavoriteIdsAsync(userId.Value, "place")
            : new HashSet<int>();

        var favoriteEventIds = userId.HasValue
            ? await _favoriteService.GetFavoriteIdsAsync(userId.Value, "event")
            : new HashSet<int>();

        var markers = new List<MapEntityMarkerDto>();

        if (ShouldIncludePlaces(normalizedCategories))
        {
            var places = await _db.Places
                .AsNoTracking()
                .Where(place =>
                    place.Status == PublishedStatus &&
                    place.Location.Status == PublishedStatus &&
                    place.Location.Destination.Status == PublishedStatus)
                .Where(place => EF.Property<Point?>(place, SpatialPointFactory.CoordinatesPropertyName) != null)
                .Select(place => new PlaceMarkerProjection
                {
                    Id = place.Id,
                    Title = place.Name,
                    Subtitle = place.Location.Name,
                    CategoryLabel = place.PlaceType.Name,
                    ImageUrl = place.Images
                        .OrderBy(image => image.ImageRole == EntityImageRole.Card ? 0 : image.ImageRole == EntityImageRole.Cover ? 1 : 2)
                        .ThenBy(image => image.SortOrder)
                        .ThenBy(image => image.CreatedAt)
                        .ThenBy(image => image.Id)
                        .Select(image => image.Url)
                        .FirstOrDefault(),
                    Latitude = place.Latitude,
                    Longitude = place.Longitude,
                    PricePerNight = place.Stay != null ? place.Stay.PricePerNight : null,
                    IsStay = place.Stay != null,
                    DistanceMeters = EF.Property<Point>(place, SpatialPointFactory.CoordinatesPropertyName).Distance(userPoint),
                })
                .Where(place => place.DistanceMeters <= radiusMeters)
                .Where(place => normalizedCategories.Count == 0
                    || (normalizedCategories.Contains("stays") && place.IsStay)
                    || (normalizedCategories.Contains("places") && !place.IsStay))
                .OrderBy(place => place.DistanceMeters)
                .Take(40)
                .ToListAsync(cancellationToken);

            markers.AddRange(places.Select(place => new MapEntityMarkerDto
            {
                EntityType = place.IsStay ? "stay" : "place",
                EntityId = place.Id,
                Title = place.Title,
                Subtitle = place.Subtitle,
                CategoryLabel = place.CategoryLabel,
                ImageUrl = place.ImageUrl,
                DetailRoute = place.IsStay ? $"/stays/{place.Id}" : $"/places/{place.Id}",
                Latitude = place.Latitude,
                Longitude = place.Longitude,
                Distance = SpatialDistanceFormatter.Format(place.DistanceMeters),
                IsSaved = favoritePlaceIds.Contains(place.Id),
            }));
        }

        if (normalizedCategories.Count == 0 || normalizedCategories.Contains("events"))
        {
            var events = await _db.Events
                .AsNoTracking()
                .Where(@event =>
                    @event.Status == PublishedStatus &&
                    @event.Destination.Status == PublishedStatus &&
                    (@event.Location == null || @event.Location.Status == PublishedStatus) &&
                    (@event.Place == null ||
                        (@event.Place.Status == PublishedStatus &&
                         @event.Place.Location.Status == PublishedStatus &&
                         @event.Place.Location.Destination.Status == PublishedStatus)))
                .Where(@event => EF.Property<Point?>(@event, SpatialPointFactory.CoordinatesPropertyName) != null)
                .Select(@event => new EventMarkerProjection
                {
                    Id = @event.Id,
                    Title = @event.Name,
                    Subtitle = @event.VenueName ?? (@event.Place != null
                        ? @event.Place.Name
                        : @event.Location != null
                            ? @event.Location.Name
                            : @event.Destination.Name),
                    CategoryLabel = @event.EventType.Name,
                    ImageUrl = @event.Images
                        .OrderBy(image => image.ImageRole == EntityImageRole.Card ? 0 : image.ImageRole == EntityImageRole.Cover ? 1 : 2)
                        .ThenBy(image => image.SortOrder)
                        .ThenBy(image => image.CreatedAt)
                        .ThenBy(image => image.Id)
                        .Select(image => image.Url)
                        .FirstOrDefault(),
                    Latitude = @event.Place != null
                        ? @event.Place.Latitude
                        : @event.Location != null
                            ? @event.Location.Latitude
                            : @event.Destination.Latitude,
                    Longitude = @event.Place != null
                        ? @event.Place.Longitude
                        : @event.Location != null
                            ? @event.Location.Longitude
                            : @event.Destination.Longitude,
                    DistanceMeters = EF.Property<Point>(@event, SpatialPointFactory.CoordinatesPropertyName).Distance(userPoint),
                })
                .Where(@event => @event.DistanceMeters <= radiusMeters)
                .OrderBy(@event => @event.DistanceMeters)
                .Take(20)
                .ToListAsync(cancellationToken);

            markers.AddRange(events.Select(@event => new MapEntityMarkerDto
            {
                EntityType = "event",
                EntityId = @event.Id,
                Title = @event.Title,
                Subtitle = @event.Subtitle,
                CategoryLabel = @event.CategoryLabel,
                ImageUrl = @event.ImageUrl,
                DetailRoute = $"/events/{@event.Id}",
                Latitude = @event.Latitude,
                Longitude = @event.Longitude,
                Distance = SpatialDistanceFormatter.Format(@event.DistanceMeters),
                IsSaved = favoriteEventIds.Contains(@event.Id),
            }));
        }

        var focusLabel = await _db.Destinations
            .AsNoTracking()
            .Where(destination => destination.Status == PublishedStatus)
            .Where(destination => EF.Property<Point?>(destination, SpatialPointFactory.CoordinatesPropertyName) != null)
            .Select(destination => new
            {
                destination.Name,
                DistanceMeters = EF.Property<Point>(destination, SpatialPointFactory.CoordinatesPropertyName).Distance(userPoint),
            })
            .OrderBy(destination => destination.DistanceMeters)
            .Select(destination => destination.Name)
            .FirstOrDefaultAsync(cancellationToken);

        return new GetMapEntitiesResponseDto
        {
            FocusLabel = focusLabel,
            Markers = markers
                .OrderBy(marker => ParseDistanceMeters(marker.Distance))
                .ThenBy(marker => marker.Title)
                .Take(50)
                .ToList(),
        };
    }

    public async Task<GetMapMarkersResponseDto> GetMarkersInViewportAsync(
        double north,
        double south,
        double east,
        double west,
        double zoom,
        IEnumerable<string>? categories,
        int? userId = null,
        CancellationToken cancellationToken = default)
    {
        if (north <= south)
        {
            throw new InvalidOperationException("'north' must be greater than 'south'.");
        }

        if (east == west)
        {
            throw new InvalidOperationException("'east' and 'west' must describe a non-empty longitude range.");
        }

        var viewportGeometry = CreateViewportGeometry(west, south, east, north);
        var normalizedCategories = NormalizeCategories(categories);
        var viewportCenterLatitude = (north + south) / 2d;
        var viewportCrossesDateline = east < west;
        var viewportCenterLongitude = viewportCrossesDateline
            ? (west + east + 360d) / 2d
            : (east + west) / 2d;
        var entityMarkerLimit = zoom switch
        {
            >= 15d => 500,
            >= 13d => 300,
            >= 11d => 180,
            _ => 120,
        };

        // Keep the quick place-type filters stable while the user pans between
        // viewports. Counts are still calculated for the current map area.
        var availablePlaceTypes = await _db.PlaceTypes
            .AsNoTracking()
            .OrderBy(placeType => placeType.Name)
            .Select(placeType => new MapFilterOptionDto
            {
                Id = placeType.Id,
                Label = placeType.Name,
                Count = 0,
                Score = 0,
            })
            .ToListAsync(cancellationToken);

        if (zoom < DestinationZoomThreshold && normalizedCategories.Count == 0)
        {
            var destinations = await _db.Destinations
                .AsNoTracking()
                .Where(destination => destination.Status == PublishedStatus)
                .Where(destination => EF.Property<Point?>(destination, SpatialPointFactory.CoordinatesPropertyName) != null)
                .Where(destination => EF.Property<Point>(destination, SpatialPointFactory.CoordinatesPropertyName).Intersects(viewportGeometry))
                .OrderBy(destination =>
                    (destination.Latitude - viewportCenterLatitude) * (destination.Latitude - viewportCenterLatitude) +
                    ((viewportCrossesDateline && destination.Longitude < west
                        ? destination.Longitude + 360d
                        : destination.Longitude) - viewportCenterLongitude) *
                    ((viewportCrossesDateline && destination.Longitude < west
                        ? destination.Longitude + 360d
                        : destination.Longitude) - viewportCenterLongitude))
                .ThenBy(destination => destination.Name)
                .Take(100)
                .Select(destination => new MapEntityMarkerDto
                {
                    EntityType = "destination",
                    EntityId = destination.Id,
                    Title = destination.Name,
                    Subtitle = null,
                    CategoryLabel = destination.DestinationType != null ? destination.DestinationType.Name : null,
                    ImageUrl = destination.Images
                        .OrderBy(image => image.ImageRole == EntityImageRole.Card ? 0 : image.ImageRole == EntityImageRole.Cover ? 1 : 2)
                        .ThenBy(image => image.SortOrder)
                        .ThenBy(image => image.CreatedAt)
                        .ThenBy(image => image.Id)
                        .Select(image => image.Url)
                        .FirstOrDefault(),
                    DetailRoute = $"/destinations/{destination.Id}",
                    Latitude = destination.Latitude,
                    Longitude = destination.Longitude,
                    Distance = null,
                    IsSaved = false,
                })
                .ToListAsync(cancellationToken);

            return new GetMapMarkersResponseDto
            {
                Mode = "destinations",
                Markers = destinations,
                PlaceTypes = availablePlaceTypes,
            };
        }

        var favoritePlaceIds = userId.HasValue
            ? await _favoriteService.GetFavoriteIdsAsync(userId.Value, "place")
            : new HashSet<int>();

        var places = ShouldIncludePlaces(normalizedCategories)
            ? await _db.Places
                .AsNoTracking()
                .Where(place =>
                    place.Status == PublishedStatus &&
                    place.Location.Status == PublishedStatus &&
                    place.Location.Destination.Status == PublishedStatus)
                .Where(place => EF.Property<Point?>(place, SpatialPointFactory.CoordinatesPropertyName) != null)
                .Where(place => EF.Property<Point>(place, SpatialPointFactory.CoordinatesPropertyName).Intersects(viewportGeometry))
                .Select(place => new PlaceMarkerProjection
                {
                    Id = place.Id,
                    Title = place.Name,
                    Subtitle = place.Location.Name,
                    CategoryLabel = place.PlaceType.Name,
                    PlaceTypeId = place.PlaceTypeId,
                    PlaceTypeLabel = place.Stay != null ? "Stays" : place.PlaceType.Name,
                    ImageUrl = place.Images
                        .OrderBy(image => image.ImageRole == EntityImageRole.Card ? 0 : image.ImageRole == EntityImageRole.Cover ? 1 : 2)
                        .ThenBy(image => image.SortOrder)
                        .ThenBy(image => image.CreatedAt)
                        .ThenBy(image => image.Id)
                        .Select(image => image.Url)
                        .FirstOrDefault(),
                    Latitude = place.Latitude,
                    Longitude = place.Longitude,
                    PricePerNight = place.Stay != null ? place.Stay.PricePerNight : null,
                    IsStay = place.Stay != null,
                    DistanceMeters = null,
                })
                .Where(place => normalizedCategories.Count == 0
                    || (normalizedCategories.Contains("stays") && place.IsStay)
                    || (normalizedCategories.Contains("places") && !place.IsStay))
                .OrderBy(place =>
                    (place.Latitude - viewportCenterLatitude) * (place.Latitude - viewportCenterLatitude) +
                    ((viewportCrossesDateline && place.Longitude < west
                        ? place.Longitude + 360d
                        : place.Longitude) - viewportCenterLongitude) *
                    ((viewportCrossesDateline && place.Longitude < west
                        ? place.Longitude + 360d
                        : place.Longitude) - viewportCenterLongitude))
                .ThenBy(place => place.Title)
                .Take(entityMarkerLimit)
                .ToListAsync(cancellationToken)
            : new List<PlaceMarkerProjection>();

        var placeIds = places.Select(place => place.Id).ToList();
        var placeTagRows = placeIds.Count == 0
            ? new List<PlaceTagProjection>()
            : await _db.PlaceTags
                .AsNoTracking()
                .Where(placeTag => placeIds.Contains(placeTag.PlaceId) && placeTag.Tag.ForPlace)
                .OrderBy(placeTag => placeTag.Tag.Name)
                .Select(placeTag => new PlaceTagProjection
                {
                    PlaceId = placeTag.PlaceId,
                    TagId = placeTag.TagId,
                    Label = placeTag.Tag.Name,
                })
                .ToListAsync(cancellationToken);

        var tagsByPlace = placeTagRows
            .GroupBy(row => row.PlaceId)
            .ToDictionary(group => group.Key, group => group.ToList());

        var markers = places.Select(place =>
        {
            var tags = tagsByPlace.GetValueOrDefault(place.Id) ?? new List<PlaceTagProjection>();
            return new MapEntityMarkerDto
            {
                EntityType = place.IsStay ? "stay" : "place",
                EntityId = place.Id,
                Title = place.Title,
                Subtitle = place.Subtitle,
                CategoryLabel = place.CategoryLabel,
                PlaceTypeId = place.PlaceTypeId,
                PlaceTypeLabel = place.PlaceTypeLabel,
                TagIds = tags.Select(tag => tag.TagId).Distinct().ToList(),
                TagLabels = tags.Select(tag => tag.Label).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                ImageUrl = place.ImageUrl,
                DetailRoute = place.IsStay ? $"/stays/{place.Id}" : $"/places/{place.Id}",
                Latitude = place.Latitude,
                Longitude = place.Longitude,
                Distance = null,
                IsSaved = favoritePlaceIds.Contains(place.Id),
            };
        }).ToList();

        var recommendedTags = await BuildRecommendedMapTagsAsync(placeTagRows, userId, cancellationToken);

        return new GetMapMarkersResponseDto
        {
            Mode = "entities",
            Markers = markers,
            PlaceTypes = availablePlaceTypes,
            RecommendedTags = recommendedTags,
        };
    }

    public async Task<MapEntityMarkerDto?> GetCurrentDestinationAsync(
        double latitude,
        double longitude,
        double zoom,
        CancellationToken cancellationToken = default)
    {
        if (zoom < DestinationZoomThreshold)
        {
            return null;
        }

        var centerPoint = SpatialPointFactory.Create(latitude, longitude);
        const string boundaryGeometryPropertyName = "BoundaryGeometry";

        var containingDestination = await _db.Destinations
            .AsNoTracking()
            .Where(destination => destination.Status == PublishedStatus)
            .Where(destination => EF.Property<Geometry?>(destination, boundaryGeometryPropertyName) != null)
            .Where(destination => EF.Property<Geometry>(destination, boundaryGeometryPropertyName).Covers(centerPoint))
            .OrderBy(destination => EF.Property<Geometry>(destination, boundaryGeometryPropertyName).Area)
            .ThenBy(destination => destination.Name)
            .Select(destination => new MapEntityMarkerDto
            {
                EntityType = "destination",
                EntityId = destination.Id,
                Title = destination.Name,
                Subtitle = destination.DestinationType != null ? destination.DestinationType.Name : null,
                CategoryLabel = "Destination",
                ImageUrl = null,
                DetailRoute = $"/destinations/{destination.Id}",
                Latitude = destination.Latitude,
                Longitude = destination.Longitude,
                Distance = null,
                IsSaved = false,
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (containingDestination is not null)
        {
            return containingDestination;
        }

        // Some older destinations may not have a boundary yet. In that case,
        // use the nearest destination centre, but only within a zoom-sensitive
        // radius so an unrelated destination is never shown for an empty area.
        var fallbackRadiusMeters = zoom switch
        {
            >= 16d => 5_000d,
            >= 14d => 15_000d,
            >= 12d => 40_000d,
            _ => 80_000d,
        };

        return await _db.Destinations
            .AsNoTracking()
            .Where(destination => destination.Status == PublishedStatus)
            .Where(destination => EF.Property<Point?>(destination, SpatialPointFactory.CoordinatesPropertyName) != null)
            .Select(destination => new
            {
                Destination = destination,
                DistanceMeters = EF.Property<Point>(destination, SpatialPointFactory.CoordinatesPropertyName).Distance(centerPoint),
            })
            .Where(item => item.DistanceMeters <= fallbackRadiusMeters)
            .OrderBy(item => item.DistanceMeters)
            .ThenBy(item => item.Destination.Name)
            .Select(item => new MapEntityMarkerDto
            {
                EntityType = "destination",
                EntityId = item.Destination.Id,
                Title = item.Destination.Name,
                Subtitle = item.Destination.DestinationType != null ? item.Destination.DestinationType.Name : null,
                CategoryLabel = "Destination",
                ImageUrl = null,
                DetailRoute = $"/destinations/{item.Destination.Id}",
                Latitude = item.Destination.Latitude,
                Longitude = item.Destination.Longitude,
                Distance = null,
                IsSaved = false,
            })
            .FirstOrDefaultAsync(cancellationToken);
    }

    private async Task<List<MapFilterOptionDto>> BuildRecommendedMapTagsAsync(
        IReadOnlyCollection<PlaceTagProjection> placeTags,
        int? userId,
        CancellationToken cancellationToken)
    {
        if (placeTags.Count == 0)
        {
            return new List<MapFilterOptionDto>();
        }

        var counts = placeTags
            .GroupBy(tag => new { tag.TagId, tag.Label })
            .ToDictionary(group => group.Key.TagId, group => new
            {
                group.Key.Label,
                Count = group.Select(tag => tag.PlaceId).Distinct().Count(),
            });

        var availableTagIds = counts.Keys.ToList();
        var explicitTagIds = new HashSet<int>();
        var inferredScores = new Dictionary<int, double>();

        if (userId.HasValue)
        {
            explicitTagIds = (await _db.UserTags
                .AsNoTracking()
                .Where(userTag => userTag.UserId == userId.Value && availableTagIds.Contains(userTag.TagId))
                .Select(userTag => userTag.TagId)
                .ToListAsync(cancellationToken))
                .ToHashSet();

            inferredScores = await _db.UserInterestSignals
                .AsNoTracking()
                .Where(signal =>
                    signal.UserId == userId.Value &&
                    !signal.IsDismissed &&
                    signal.Score > 0 &&
                    availableTagIds.Contains(signal.TagId))
                .GroupBy(signal => signal.TagId)
                .Select(group => new
                {
                    TagId = group.Key,
                    Score = group.Max(signal => signal.Score),
                })
                .ToDictionaryAsync(item => item.TagId, item => item.Score, cancellationToken);
        }

        return counts
            .Select(entry => new MapFilterOptionDto
            {
                Id = entry.Key,
                Label = entry.Value.Label,
                Count = entry.Value.Count,
                Score = (explicitTagIds.Contains(entry.Key) ? 1000d : 0d)
                    + (inferredScores.GetValueOrDefault(entry.Key) * 100d)
                    + entry.Value.Count,
            })
            .OrderByDescending(option => option.Score)
            .ThenByDescending(option => option.Count)
            .ThenBy(option => option.Label)
            .Take(8)
            .ToList();
    }

    private static List<MapCoordinateDto> DecimateCoordinates(List<MapCoordinateDto> coords, int maxPoints = 5000)
    {
        if (coords.Count <= maxPoints)
            return coords;

        var result = new List<MapCoordinateDto>(maxPoints);
        double stride = (double)(coords.Count - 1) / (maxPoints - 1);

        for (int i = 0; i < maxPoints - 1; i++)
            result.Add(coords[(int)(i * stride)]);

        result.Add(coords[coords.Count - 1]);
        return result;
    }

    private static Geometry CreateViewportGeometry(double west, double south, double east, double north)
    {
        if (east > west)
        {
            return CreateViewportPolygon(west, south, east, north);
        }

        var polygons = new List<Polygon>(2);
        if (west < 180d)
        {
            polygons.Add(CreateViewportPolygon(west, south, 180d, north));
        }

        if (east > -180d)
        {
            polygons.Add(CreateViewportPolygon(-180d, south, east, north));
        }

        return polygons.Count switch
        {
            0 => throw new InvalidOperationException("Viewport longitude range is empty."),
            1 => polygons[0],
            _ => ViewportGeometryFactory.CreateMultiPolygon(polygons.ToArray()),
        };
    }

    private static Polygon CreateViewportPolygon(double west, double south, double east, double north) =>
        ViewportGeometryFactory.CreatePolygon(new[]
        {
            new Coordinate(west, south),
            new Coordinate(east, south),
            new Coordinate(east, north),
            new Coordinate(west, north),
            new Coordinate(west, south),
        });

    private static HashSet<string> NormalizeCategories(IEnumerable<string>? categories) =>
        (categories ?? Enumerable.Empty<string>())
            .Select(category => category.Trim().ToLowerInvariant())
            .Where(category => AllowedCategories.Contains(category))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static bool ShouldIncludePlaces(HashSet<string> categories) =>
        categories.Count == 0 || categories.Contains("places") || categories.Contains("stays");

    private static double NormalizeRadiusKm(double? radiusKm)
    {
        if (!radiusKm.HasValue || double.IsNaN(radiusKm.Value) || double.IsInfinity(radiusKm.Value))
        {
            return 5d;
        }

        return Math.Clamp(radiusKm.Value, 0.5d, 25d);
    }

    private void LogProviderFailure(string provider, Exception exception)
    {
        _logger.LogWarning(
            "Map provider {Provider} request failed with {ExceptionType}.",
            provider,
            exception.GetType().Name);
    }

    private static bool IsProviderFailure(Exception exception, CancellationToken cancellationToken) =>
        !cancellationToken.IsCancellationRequested &&
        (exception is HttpRequestException ||
         exception is TaskCanceledException ||
         exception is JsonException ||
         exception is NotSupportedException);

    private static bool HasValidCoordinates(double latitude, double longitude)
    {
        return !double.IsNaN(latitude) &&
               !double.IsNaN(longitude) &&
               !double.IsInfinity(latitude) &&
               !double.IsInfinity(longitude) &&
               latitude >= -90 &&
               latitude <= 90 &&
               longitude >= -180 &&
               longitude <= 180 &&
               (latitude != 0d || longitude != 0d);
    }

    private static double NormalizeCoordinate(double value) =>
        Math.Round(value, 5, MidpointRounding.AwayFromZero);

    private static string BuildCacheKey(string prefix, string value)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return $"{prefix}:{Convert.ToHexString(hash)}";
    }

    private static string BuildSearchTitle(NominatimSearchResult result)
    {
        var first = result.DisplayName?.Split(',', 2, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        return !string.IsNullOrWhiteSpace(first) ? first : result.DisplayName ?? "Selected location";
    }

    private static string? BuildSearchSubtitle(string? displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName))
        {
            return null;
        }

        var parts = displayName.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        return parts.Length <= 1 ? null : string.Join(", ", parts.Skip(1).Take(2));
    }

    private static string BuildStreetAddress(NominatimAddress address, string? providerDisplayName, string fallbackDisplayLabel)
    {
        var streetLine = string.Join(" ", new[] { address.Road, address.HouseNumber }
            .Where(part => !string.IsNullOrWhiteSpace(part)));
        if (!string.IsNullOrWhiteSpace(streetLine))
            return streetLine;

        return FirstNonEmpty(
            address.Pedestrian,
            address.Footway,
            address.Path,
            address.Neighbourhood,
            address.Suburb,
            address.Quarter,
            address.Municipality,
            address.City,
            address.Town,
            address.Village,
            providerDisplayName,
            fallbackDisplayLabel);
    }

    private static string BuildDisplayLabel(string city, string region, string country)
    {
        if (!string.IsNullOrWhiteSpace(city) && !string.IsNullOrWhiteSpace(country))
        {
            return $"{city}, {country}";
        }

        if (!string.IsNullOrWhiteSpace(city))
        {
            return city;
        }

        if (!string.IsNullOrWhiteSpace(region) && !string.IsNullOrWhiteSpace(country))
        {
            return $"{region}, {country}";
        }

        if (!string.IsNullOrWhiteSpace(region))
        {
            return region;
        }

        return string.IsNullOrWhiteSpace(country) ? "Explore" : country;
    }

    private static string FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? string.Empty;

    private static bool TryParseCoordinate(string? value, out double parsed) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed);

    private static double ParseCoordinate(string? value) =>
        double.Parse(value ?? "0", NumberStyles.Float, CultureInfo.InvariantCulture);

    private static string FormatCoordinate(double value) =>
        value.ToString("0.######", CultureInfo.InvariantCulture);

    private static double ParseDistanceMeters(string? formattedDistance)
    {
        if (string.IsNullOrWhiteSpace(formattedDistance))
        {
            return double.MaxValue;
        }

        if (formattedDistance.EndsWith(" km", StringComparison.OrdinalIgnoreCase) &&
            double.TryParse(formattedDistance.Replace(" km", string.Empty, StringComparison.OrdinalIgnoreCase), NumberStyles.Float, CultureInfo.InvariantCulture, out var kilometers))
        {
            return kilometers * 1000d;
        }

        if (formattedDistance.EndsWith(" m", StringComparison.OrdinalIgnoreCase) &&
            double.TryParse(formattedDistance.Replace(" m", string.Empty, StringComparison.OrdinalIgnoreCase), NumberStyles.Float, CultureInfo.InvariantCulture, out var meters))
        {
            return meters;
        }

        return double.MaxValue;
    }

    private static int CalculateSearchScore(
        MapSearchProjection candidate,
        string searchTerm,
        IReadOnlyCollection<string> searchTerms)
    {
        var name = SearchTextNormalizer.Normalize(candidate.Name);
        var own = SearchTextNormalizer.Normalize(candidate.OwnSearchText);
        var content = SearchTextNormalizer.Normalize(candidate.ContentSearchText);
        var type = SearchTextNormalizer.Normalize(candidate.TypeSearchText);
        var parent = SearchTextNormalizer.Normalize(candidate.ParentSearchText);
        var area = SearchTextNormalizer.Normalize(candidate.AreaSearchText);
        var destination = SearchTextNormalizer.Normalize(candidate.DestinationSearchText);
        var identifier = SearchTextNormalizer.Normalize(candidate.IdentifierSearchText);

        var score = candidate.Type switch
        {
            "destination" => 40,
            "location" => 30,
            _ => 20,
        };

        score += candidate.ContextBoost;

        if (name == searchTerm)
        {
            score += 1000;
        }
        else if (name.StartsWith(searchTerm, StringComparison.Ordinal))
        {
            score += 900;
        }
        else if (name.Contains(searchTerm, StringComparison.Ordinal))
        {
            score += 800;
        }

        if (own.StartsWith(searchTerm, StringComparison.Ordinal)) score += 500;
        else if (own.Contains(searchTerm, StringComparison.Ordinal)) score += 420;

        if (type == searchTerm) score += 380;
        else if (type.Contains(searchTerm, StringComparison.Ordinal)) score += 320;

        if (parent.Contains(searchTerm, StringComparison.Ordinal)) score += 260;
        if (area.Contains(searchTerm, StringComparison.Ordinal)) score += 220;
        if (destination.Contains(searchTerm, StringComparison.Ordinal)) score += 180;
        if (identifier.Contains(searchTerm, StringComparison.Ordinal)) score += 650;

        // SearchText contains descriptive content such as summaries, descriptions,
        // addresses, subtitles and venue names. Keep these matches discoverable,
        // but rank them below direct name, type and parent-area matches.
        if (content.Contains(searchTerm, StringComparison.Ordinal)) score += 110;

        foreach (var term in searchTerms)
        {
            if (name.Contains(term, StringComparison.Ordinal)) score += 70;
            else if (own.Contains(term, StringComparison.Ordinal)) score += 55;
            else if (type.Contains(term, StringComparison.Ordinal)) score += 45;
            else if (parent.Contains(term, StringComparison.Ordinal)) score += 35;
            else if (area.Contains(term, StringComparison.Ordinal)) score += 30;
            else if (destination.Contains(term, StringComparison.Ordinal)) score += 25;
            else if (identifier.Contains(term, StringComparison.Ordinal)) score += 60;
            else if (content.Contains(term, StringComparison.Ordinal)) score += 15;
        }

        return score;
    }

    private static bool IsMeaningfulSearchMatch(
        MapSearchProjection candidate,
        IReadOnlyCollection<string> searchTerms)
    {
        var searchableFields = new[]
        {
            SearchTextNormalizer.Normalize(candidate.Name),
            SearchTextNormalizer.Normalize(candidate.ContentSearchText),
            SearchTextNormalizer.Normalize(candidate.TypeSearchText),
            SearchTextNormalizer.Normalize(candidate.ParentSearchText),
            SearchTextNormalizer.Normalize(candidate.AreaSearchText),
            SearchTextNormalizer.Normalize(candidate.DestinationSearchText),
            SearchTextNormalizer.Normalize(candidate.IdentifierSearchText),
        };

        return searchTerms.All(term =>
            searchableFields.Any(field => field.Contains(term, StringComparison.Ordinal)));
    }

    private static bool IsStrongOwnNameMatch(
        MapSearchProjection candidate,
        string searchTerm,
        IReadOnlyCollection<string> searchTerms)
    {
        var name = SearchTextNormalizer.Normalize(candidate.Name);
        return name == searchTerm ||
               name.StartsWith(searchTerm, StringComparison.Ordinal) ||
               searchTerms.All(term => name.Contains(term, StringComparison.Ordinal));
    }

    private async Task AddParentContextResultsAsync(
        List<MapSearchProjection> candidates,
        IReadOnlySet<string> requestedTypes,
        string searchTerm,
        IReadOnlyCollection<string> searchTerms,
        CancellationToken cancellationToken)
    {
        var directlyMatchedPlaces = candidates
            .Where(candidate =>
                (candidate.Type == "place" || candidate.Type == "stay") &&
                IsStrongOwnNameMatch(candidate, searchTerm, searchTerms))
            .OrderByDescending(candidate => CalculateSearchScore(candidate, searchTerm, searchTerms))
            .Take(10)
            .ToList();

        if (directlyMatchedPlaces.Count == 0)
        {
            return;
        }

        var existingKeys = candidates
            .Select(candidate => (candidate.Type, candidate.Id))
            .ToHashSet();

        if (requestedTypes.Contains("location"))
        {
            var locationIds = directlyMatchedPlaces
                .Where(candidate => candidate.LocationId.HasValue)
                .Select(candidate => candidate.LocationId.Value)
                .Distinct()
                .ToArray();

            if (locationIds.Length > 0)
            {
                var locations = await _db.Locations
                    .AsNoTracking()
                    .Where(location =>
                        locationIds.Contains(location.Id) &&
                        location.Status == PublishedStatus &&
                        location.Destination.Status == PublishedStatus)
                    .Select(location => new MapSearchProjection
                    {
                        Id = location.Id,
                        Type = "location",
                        Name = location.Name,
                        Description = location.Description,
                        Subtitle = location.Destination.Name,
                        Latitude = location.Latitude,
                        Longitude = location.Longitude,
                        ParentName = location.Destination.Name,
                        OwnSearchText = location.Name,
                        ContentSearchText = location.SearchText + " " +
                        (location.Summary ?? string.Empty) + " " +
                        location.Description,
                        ParentSearchText = location.Destination.Name,
                        TypeSearchText = location.LocationType.Name,
                        IdentifierSearchText = location.InternalReferenceId ?? string.Empty,
                        DestinationId = location.DestinationId,
                        ContextBoost = 720,
                    })
                    .ToListAsync(cancellationToken);

                foreach (var location in locations)
                {
                    if (existingKeys.Add((location.Type, location.Id)))
                    {
                        candidates.Add(location);
                    }
                }
            }
        }

        if (requestedTypes.Contains("destination"))
        {
            var destinationIds = directlyMatchedPlaces
                .Where(candidate => candidate.DestinationId.HasValue)
                .Select(candidate => candidate.DestinationId.Value)
                .Distinct()
                .ToArray();

            if (destinationIds.Length > 0)
            {
                var destinations = await _db.Destinations
                    .AsNoTracking()
                    .Where(destination =>
                        destinationIds.Contains(destination.Id) &&
                        destination.Status == PublishedStatus)
                    .Select(destination => new MapSearchProjection
                    {
                        Id = destination.Id,
                        Type = "destination",
                        Name = destination.Name,
                        Description = destination.Description,
                        Subtitle = destination.Region.Name,
                        Latitude = destination.Latitude,
                        Longitude = destination.Longitude,
                        ParentName = destination.Region.Name,
                        OwnSearchText = destination.Name,
                        ContentSearchText = destination.SearchText + " " +
                        (destination.Summary ?? string.Empty) + " " +
                        destination.Description,
                        ParentSearchText = destination.Region.Name,
                        TypeSearchText = destination.DestinationType != null
                            ? destination.DestinationType.Name
                            : string.Empty,
                        IdentifierSearchText = destination.InternalReferenceId ?? string.Empty,
                        ContextBoost = 680,
                    })
                    .ToListAsync(cancellationToken);

                foreach (var destination in destinations)
                {
                    if (existingKeys.Add((destination.Type, destination.Id)))
                    {
                        candidates.Add(destination);
                    }
                }
            }
        }
    }

    private sealed class MapSearchProjection
    {
        public int Id { get; set; }
        public string Type { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public string? Subtitle { get; set; }
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public string? ParentName { get; set; }
        public string OwnSearchText { get; set; } = string.Empty;
        public string ContentSearchText { get; set; } = string.Empty;
        public string ParentSearchText { get; set; } = string.Empty;
        public string AreaSearchText { get; set; } = string.Empty;
        public string DestinationSearchText { get; set; } = string.Empty;
        public string TypeSearchText { get; set; } = string.Empty;
        public string IdentifierSearchText { get; set; } = string.Empty;
        public int? LocationId { get; set; }
        public int? DestinationId { get; set; }
        public int ContextBoost { get; set; }

        public MapSearchResultDto ToDto() => new()
        {
            Id = Id,
            Type = Type,
            Name = Name,
            Description = Description,
            Subtitle = Subtitle,
            Latitude = Latitude,
            Longitude = Longitude,
            ParentName = ParentName,
        };
    }

    private sealed class PlaceMarkerProjection
    {
        public int Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Subtitle { get; set; } = string.Empty;
        public string CategoryLabel { get; set; } = string.Empty;
        public int PlaceTypeId { get; set; }
        public string PlaceTypeLabel { get; set; } = string.Empty;
        public string? ImageUrl { get; set; }
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public decimal? PricePerNight { get; set; }
        public bool IsStay { get; set; }
        public double? DistanceMeters { get; set; }
    }


    private sealed class PlaceTagProjection
    {
        public int PlaceId { get; set; }
        public int TagId { get; set; }
        public string Label { get; set; } = string.Empty;
    }

    private sealed class EventMarkerProjection
    {
        public int Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Subtitle { get; set; } = string.Empty;
        public string CategoryLabel { get; set; } = string.Empty;
        public string? ImageUrl { get; set; }
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public double? DistanceMeters { get; set; }
    }

    private sealed class NominatimSearchResult
    {
        [JsonPropertyName("display_name")]
        public string? DisplayName { get; set; }

        public string? Lat { get; set; }

        public string? Lon { get; set; }
    }

    private sealed class NominatimReverseResult
    {
        [JsonPropertyName("display_name")]
        public string? DisplayName { get; set; }

        public NominatimAddress? Address { get; set; }
    }

    private sealed class NominatimAddress
    {
        [JsonPropertyName("house_number")]
        public string? HouseNumber { get; set; }

        public string? Road { get; set; }

        public string? Pedestrian { get; set; }

        public string? Footway { get; set; }

        public string? Path { get; set; }

        public string? Neighbourhood { get; set; }

        public string? Suburb { get; set; }

        public string? Quarter { get; set; }

        public string? City { get; set; }

        public string? Town { get; set; }

        public string? Village { get; set; }

        public string? Municipality { get; set; }

        public string? County { get; set; }

        [JsonPropertyName("state_district")]
        public string? StateDistrict { get; set; }

        public string? State { get; set; }

        public string? Country { get; set; }
    }

    private sealed class OsrmRouteResponse
    {
        public string? Code { get; set; }

        public List<OsrmRouteItem>? Routes { get; set; }
    }

    private sealed class OsrmRouteItem
    {
        public double? Distance { get; set; }

        public double? Duration { get; set; }

        public OsrmGeometry? Geometry { get; set; }
    }

    private sealed class OsrmGeometry
    {
        public List<List<double>> Coordinates { get; set; } = new();
    }
}
