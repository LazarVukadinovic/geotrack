using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Geotrack.Application.DTOs.Map;
using Geotrack.Application.Interfaces.Service;
using Geotrack.Infrastructure.Spatial;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;

namespace Geotrack.Infrastructure.Services;

public class MapService : IMapService
{
    private const string NominatimBaseUrl = "https://nominatim.openstreetmap.org";
    private const string OsrmBaseUrl = "https://router.project-osrm.org/route/v1/foot";
    private const double DestinationZoomThreshold = 10d;
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

    public MapService(HttpClient httpClient, AppDbContext db, IFavoriteService favoriteService)
    {
        _httpClient = httpClient;
        _db = db;
        _favoriteService = favoriteService;

        if (!_httpClient.DefaultRequestHeaders.UserAgent.Any())
        {
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("GeoTrack/1.0");
        }
    }

    public async Task<List<MapSearchResultDto>> SearchAsync(string query, CancellationToken cancellationToken = default)
    {
        var trimmed = query.Trim();
        if (trimmed.Length < 2)
            return new List<MapSearchResultDto>();

        var url =
            $"{NominatimBaseUrl}/search?format=jsonv2&q={Uri.EscapeDataString(trimmed)}&limit=5&accept-language=en&addressdetails=1&namedetails=0";

        var results = await _httpClient.GetFromJsonAsync<List<NominatimSearchResult>>(url, cancellationToken)
            ?? new List<NominatimSearchResult>();

        return results
            .Where(result => TryParseCoordinate(result.Lat, out _) && TryParseCoordinate(result.Lon, out _))
            .Select(result => new MapSearchResultDto
            {
                Name = BuildSearchTitle(result),
                Subtitle = BuildSearchSubtitle(result.DisplayName),
                Latitude = ParseCoordinate(result.Lat),
                Longitude = ParseCoordinate(result.Lon),
            })
            .ToList();
    }

    public async Task<List<MapSearchResultDto>> SearchAsync(
        string query,
        IEnumerable<string>? types,
        CancellationToken cancellationToken = default)
    {
        var trimmed = query.Trim();
        if (trimmed.Length < 2)
            return new List<MapSearchResultDto>();

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
                .Include(location => location.Destination)
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
                .Include(place => place.Location)
                .Where(place => place.Name.ToLower().Contains(loweredQuery))
                .OrderBy(place => place.Name)
                .Take(10)
                .Select(place => new MapSearchResultDto
                {
                    Id = place.Id,
                    Type = "place",
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
                .Include(@event => @event.Location)
                .Include(@event => @event.Place)
                .Where(@event => @event.Name.ToLower().Contains(loweredQuery))
                .OrderBy(@event => @event.Name)
                .Take(10)
                .Select(@event => new MapSearchResultDto
                {
                    Id = @event.Id,
                    Type = "event",
                    Name = @event.Name,
                    Description = @event.Description,
                    Subtitle = @event.Place != null ? @event.Place.Name : @event.Location.Name,
                    Latitude = @event.Place != null ? @event.Place.Latitude : @event.Location.Latitude,
                    Longitude = @event.Place != null ? @event.Place.Longitude : @event.Location.Longitude,
                    ParentName = @event.Place != null ? @event.Place.Name : @event.Location.Name,
                })
                .ToListAsync(cancellationToken));
        }

        return results
            .OrderBy(result => result.Type)
            .ThenBy(result => result.Name)
            .Take(30)
            .ToList();
    }

    public async Task<MapReverseGeocodeDto?> ReverseGeocodeAsync(
        double latitude,
        double longitude,
        CancellationToken cancellationToken = default)
    {
        var url =
            $"{NominatimBaseUrl}/reverse?lat={FormatCoordinate(latitude)}&lon={FormatCoordinate(longitude)}&format=jsonv2&accept-language=en&addressdetails=1";

        var result = await _httpClient.GetFromJsonAsync<NominatimReverseResult>(url, cancellationToken);
        if (result is null)
            return null;

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

        return new MapReverseGeocodeDto
        {
            Address = string.Join(" ", new[] { address.Road, address.HouseNumber }.Where(x => !string.IsNullOrWhiteSpace(x))),
            City = city,
            Region = region,
            Country = country,
            DisplayLabel = BuildDisplayLabel(city, region, country),
        };
    }

    public async Task<MapRouteResponseDto?> GetWalkingRouteAsync(
        double originLatitude,
        double originLongitude,
        double destinationLatitude,
        double destinationLongitude,
        CancellationToken cancellationToken = default)
    {
        var url =
            $"{OsrmBaseUrl}/{FormatCoordinate(originLongitude)},{FormatCoordinate(originLatitude)};{FormatCoordinate(destinationLongitude)},{FormatCoordinate(destinationLatitude)}?overview=full&geometries=geojson";

        var result = await _httpClient.GetFromJsonAsync<OsrmRouteResponse>(url, cancellationToken);
        var route = result?.Routes?.FirstOrDefault();
        if (!string.Equals(result?.Code, "Ok", StringComparison.OrdinalIgnoreCase) ||
            route?.Geometry?.Coordinates is not { Count: > 1 })
        {
            return null;
        }

        return new MapRouteResponseDto
        {
            Coordinates = route.Geometry.Coordinates
                .Where(pair => pair.Count >= 2)
                .Select(pair => new MapCoordinateDto
                {
                    Latitude = pair[1],
                    Longitude = pair[0],
                })
                .ToList(),
            DistanceMeters = route.Distance,
            DurationSeconds = route.Duration,
        };
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
                .Include(place => place.PlaceType)
                .Include(place => place.Location)
                .Where(place => EF.Property<Point?>(place, SpatialPointFactory.CoordinatesPropertyName) != null)
                .Select(place => new PlaceMarkerProjection
                {
                    Id = place.Id,
                    Title = place.Name,
                    Subtitle = place.Location.Name,
                    CategoryLabel = place.PlaceType.Name,
                    ImageUrl = place.ImageUrl,
                    Latitude = place.Latitude,
                    Longitude = place.Longitude,
                    PricePerNight = place.PricePerNight,
                    DistanceMeters = EF.Property<Point>(place, SpatialPointFactory.CoordinatesPropertyName).Distance(userPoint),
                })
                .Where(place => place.DistanceMeters <= radiusMeters)
                .Where(place => normalizedCategories.Count == 0
                    || (normalizedCategories.Contains("stays") && place.PricePerNight != null)
                    || (normalizedCategories.Contains("places") && place.PricePerNight == null))
                .OrderBy(place => place.DistanceMeters)
                .Take(40)
                .ToListAsync(cancellationToken);

            markers.AddRange(places.Select(place => new MapEntityMarkerDto
            {
                EntityType = place.PricePerNight != null ? "stay" : "place",
                EntityId = place.Id,
                Title = place.Title,
                Subtitle = place.Subtitle,
                CategoryLabel = place.CategoryLabel,
                ImageUrl = place.ImageUrl,
                DetailRoute = place.PricePerNight != null ? $"/stays/{place.Id}" : $"/places/{place.Id}",
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
                .Include(@event => @event.EventType)
                .Include(@event => @event.Location)
                .Where(@event => EF.Property<Point?>(@event, SpatialPointFactory.CoordinatesPropertyName) != null)
                .Select(@event => new EventMarkerProjection
                {
                    Id = @event.Id,
                    Title = @event.Name,
                    Subtitle = @event.Location.Name,
                    CategoryLabel = @event.EventType.Name,
                    ImageUrl = @event.ImageUrl,
                    Latitude = @event.Place != null ? @event.Place.Latitude : @event.Location.Latitude,
                    Longitude = @event.Place != null ? @event.Place.Longitude : @event.Location.Longitude,
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
            throw new InvalidOperationException("'north' must be greater than 'south'.");

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
                .Include(destination => destination.DestinationType)
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
                    ImageUrl = destination.ImageUrl,
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
                .Include(place => place.PlaceType)
                .Include(place => place.Location)
                .Where(place => EF.Property<Point?>(place, SpatialPointFactory.CoordinatesPropertyName) != null)
                .Where(place => EF.Property<Point>(place, SpatialPointFactory.CoordinatesPropertyName).Intersects(envelope))
                .Select(place => new PlaceMarkerProjection
                {
                    Id = place.Id,
                    Title = place.Name,
                    Subtitle = place.Location.Name,
                    CategoryLabel = place.PlaceType.Name,
                    ImageUrl = place.ImageUrl,
                    Latitude = place.Latitude,
                    Longitude = place.Longitude,
                    PricePerNight = place.PricePerNight,
                    DistanceMeters = null,
                })
                .Where(place => normalizedCategories.Count == 0
                    || (normalizedCategories.Contains("stays") && place.PricePerNight != null)
                    || (normalizedCategories.Contains("places") && place.PricePerNight == null))
                .OrderBy(place => place.Title)
                .Take(40)
                .ToListAsync(cancellationToken);

            markers.AddRange(places.Select(place => new MapEntityMarkerDto
            {
                EntityType = place.PricePerNight != null ? "stay" : "place",
                EntityId = place.Id,
                Title = place.Title,
                Subtitle = place.Subtitle,
                CategoryLabel = place.CategoryLabel,
                ImageUrl = place.ImageUrl,
                DetailRoute = place.PricePerNight != null ? $"/stays/{place.Id}" : $"/places/{place.Id}",
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
                .Include(@event => @event.EventType)
                .Include(@event => @event.Location)
                .Where(@event => EF.Property<Point?>(@event, SpatialPointFactory.CoordinatesPropertyName) != null)
                .Where(@event => EF.Property<Point>(@event, SpatialPointFactory.CoordinatesPropertyName).Intersects(envelope))
                .Select(@event => new EventMarkerProjection
                {
                    Id = @event.Id,
                    Title = @event.Name,
                    Subtitle = @event.Location.Name,
                    CategoryLabel = @event.EventType.Name,
                    ImageUrl = @event.ImageUrl,
                    Latitude = @event.Place != null ? @event.Place.Latitude : @event.Location.Latitude,
                    Longitude = @event.Place != null ? @event.Place.Longitude : @event.Location.Longitude,
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
            return 5d;

        return Math.Clamp(radiusKm.Value, 0.5d, 25d);
    }

    private static string BuildSearchTitle(NominatimSearchResult result)
    {
        var first = result.DisplayName?.Split(',', 2, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        return !string.IsNullOrWhiteSpace(first) ? first : result.DisplayName ?? "Selected location";
    }

    private static string? BuildSearchSubtitle(string? displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName))
            return null;

        var parts = displayName.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        return parts.Length <= 1 ? null : string.Join(", ", parts.Skip(1).Take(2));
    }

    private static string BuildDisplayLabel(string city, string region, string country)
    {
        if (!string.IsNullOrWhiteSpace(city) && !string.IsNullOrWhiteSpace(country))
            return $"{city}, {country}";

        if (!string.IsNullOrWhiteSpace(city))
            return city;

        if (!string.IsNullOrWhiteSpace(region) && !string.IsNullOrWhiteSpace(country))
            return $"{region}, {country}";

        if (!string.IsNullOrWhiteSpace(region))
            return region;

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
            return double.MaxValue;

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
