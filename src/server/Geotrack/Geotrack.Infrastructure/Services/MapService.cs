using System.Globalization;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Geotrack.Application.DTOs.Map;
using Geotrack.Application.Interfaces.Service;
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

        var loweredQuery = trimmed.ToLowerInvariant();
        var results = new List<MapSearchResultDto>();

        if (requestedTypes.Contains("destination"))
        {
            results.AddRange(await _db.Destinations
                .AsNoTracking()
                .Where(destination => destination.Name.ToLower().Contains(loweredQuery))
                .OrderBy(destination => destination.Name)
                .Take(10)
                .Select(destination => new MapSearchResultDto
                {
                    Id = destination.Id,
                    Type = "destination",
                    Name = destination.Name,
                    Description = destination.Description,
                    Subtitle = destination.Region.Name,
                    Latitude = destination.Latitude,
                    Longitude = destination.Longitude,
                    ParentName = destination.Region.Name,
                })
                .ToListAsync(cancellationToken));
        }

        if (requestedTypes.Contains("location"))
        {
            results.AddRange(await _db.Locations
                .AsNoTracking()
                .Where(location => location.Name.ToLower().Contains(loweredQuery))
                .OrderBy(location => location.Name)
                .Take(10)
                .Select(location => new MapSearchResultDto
                {
                    Id = location.Id,
                    Type = "location",
                    Name = location.Name,
                    Subtitle = location.Destination.Name,
                    Latitude = location.Latitude,
                    Longitude = location.Longitude,
                    ParentName = location.Destination.Name,
                })
                .ToListAsync(cancellationToken));
        }

        if (requestedTypes.Contains("place"))
        {
            results.AddRange(await _db.Places
                .AsNoTracking()
                .Where(place => place.Status == PublishedStatus)
                .Where(place => place.Name.ToLower().Contains(loweredQuery))
                .OrderBy(place => place.Name)
                .Take(10)
                .Select(place => new MapSearchResultDto
                {
                    Id = place.Id,
                    Type = place.Stay != null ? "stay" : "place",
                    Name = place.Name,
                    Description = place.Description,
                    Subtitle = place.Location.Name,
                    Latitude = place.Latitude,
                    Longitude = place.Longitude,
                    ParentName = place.Location.Name,
                })
                .ToListAsync(cancellationToken));
        }

        if (requestedTypes.Contains("event"))
        {
            results.AddRange(await _db.Events
                .AsNoTracking()
                .Where(@event => @event.Name.ToLower().Contains(loweredQuery))
                .OrderBy(@event => @event.Name)
                .Take(10)
                .Select(@event => new MapSearchResultDto
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
                })
                .ToListAsync(cancellationToken));
        }

        return results
            .OrderBy(result => result.Type)
            .ThenBy(result => result.Name)
            .Take(30)
            .ToList();
    }

    public async Task<List<MapSearchResultDto>> GetAllDestinationsAsync(CancellationToken cancellationToken = default)
    {
        const string cacheKey = "map:destinations:v1";
        if (_cache.TryGetValue<List<MapSearchResultDto>>(cacheKey, out var cached))
            return cached ?? new List<MapSearchResultDto>();

        var destinations = await _db.Destinations
            .AsNoTracking()
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
            address.Village,
            address.Municipality,
            address.County,
            address.StateDistrict,
            address.State);
        var region = FirstNonEmpty(address.State, address.County);
        var country = address.Country ?? string.Empty;

        var reverseGeocode = new MapReverseGeocodeDto
        {
            Address = string.Join(" ", new[] { address.Road, address.HouseNumber }.Where(x => !string.IsNullOrWhiteSpace(x))),
            City = city,
            Region = region,
            Country = country,
            DisplayLabel = BuildDisplayLabel(city, region, country),
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

        var mappedRoute = new MapRouteResponseDto
        {
            Coordinates = route.Geometry.Coordinates
                .Where(pair => pair.Count >= 2)
                .Select(pair => new MapCoordinateDto
                {
                    Latitude = pair[1],
                    Longitude = pair[0],
                })
                .Where(point => HasValidCoordinates(point.Latitude, point.Longitude))
                .Take(5_000)
                .ToList(),
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
                .Where(place => place.Status == PublishedStatus)
                .Where(place => EF.Property<Point?>(place, SpatialPointFactory.CoordinatesPropertyName) != null)
                .Select(place => new PlaceMarkerProjection
                {
                    Id = place.Id,
                    Title = place.Name,
                    Subtitle = place.Location.Name,
                    CategoryLabel = place.PlaceType.Name,
                    ImageUrl = place.Images
                        .OrderByDescending(image => image.IsMain)
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
                        .OrderByDescending(image => image.IsMain)
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

        var envelope = ViewportGeometryFactory.CreatePolygon(new[]
        {
            new Coordinate(west, south),
            new Coordinate(east, south),
            new Coordinate(east, north),
            new Coordinate(west, north),
            new Coordinate(west, south),
        });

        if (zoom < DestinationZoomThreshold)
        {
            var destinations = await _db.Destinations
                .AsNoTracking()
                .Where(destination => EF.Property<Point?>(destination, SpatialPointFactory.CoordinatesPropertyName) != null)
                .Where(destination => EF.Property<Point>(destination, SpatialPointFactory.CoordinatesPropertyName).Intersects(envelope))
                .OrderBy(destination => destination.Name)
                .Take(30)
                .Select(destination => new MapEntityMarkerDto
                {
                    EntityType = "destination",
                    EntityId = destination.Id,
                    Title = destination.Name,
                    Subtitle = null,
                    CategoryLabel = destination.DestinationType != null ? destination.DestinationType.Name : null,
                    ImageUrl = destination.Images
                        .OrderByDescending(image => image.IsMain)
                        .ThenBy(image => image.SortOrder)
                        .ThenBy(image => image.CreatedAt)
                        .ThenBy(image => image.Id)
                        .Select(image => image.Url)
                        .FirstOrDefault(),
                    DetailRoute = null,
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
            };
        }

        var normalizedCategories = NormalizeCategories(categories);

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
                .Where(place => place.Status == PublishedStatus)
                .Where(place => EF.Property<Point?>(place, SpatialPointFactory.CoordinatesPropertyName) != null)
                .Where(place => EF.Property<Point>(place, SpatialPointFactory.CoordinatesPropertyName).Intersects(envelope))
                .Select(place => new PlaceMarkerProjection
                {
                    Id = place.Id,
                    Title = place.Name,
                    Subtitle = place.Location.Name,
                    CategoryLabel = place.PlaceType.Name,
                    ImageUrl = place.Images
                        .OrderByDescending(image => image.IsMain)
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
                .OrderBy(place => place.Title)
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
                Distance = null,
                IsSaved = favoritePlaceIds.Contains(place.Id),
            }));
        }

        if (normalizedCategories.Count == 0 || normalizedCategories.Contains("events"))
        {
            var events = await _db.Events
                .AsNoTracking()
                .Where(@event => EF.Property<Point?>(@event, SpatialPointFactory.CoordinatesPropertyName) != null)
                .Where(@event => EF.Property<Point>(@event, SpatialPointFactory.CoordinatesPropertyName).Intersects(envelope))
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
                        .OrderByDescending(image => image.IsMain)
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
                    DistanceMeters = null,
                })
                .OrderBy(@event => @event.Title)
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
                Distance = null,
                IsSaved = favoriteEventIds.Contains(@event.Id),
            }));
        }

        return new GetMapMarkersResponseDto
        {
            Mode = "entities",
            Markers = markers
                .OrderBy(marker => marker.Title)
                .Take(60)
                .ToList(),
        };
    }

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

    private sealed class PlaceMarkerProjection
    {
        public int Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Subtitle { get; set; } = string.Empty;
        public string CategoryLabel { get; set; } = string.Empty;
        public string? ImageUrl { get; set; }
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public decimal? PricePerNight { get; set; }
        public bool IsStay { get; set; }
        public double? DistanceMeters { get; set; }
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
        public NominatimAddress? Address { get; set; }
    }

    private sealed class NominatimAddress
    {
        [JsonPropertyName("house_number")]
        public string? HouseNumber { get; set; }

        public string? Road { get; set; }

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
