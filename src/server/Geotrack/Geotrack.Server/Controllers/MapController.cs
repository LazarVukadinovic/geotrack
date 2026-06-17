using Geotrack.Application.Interfaces.Service;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Geotrack.Server.Controllers;

[ApiController]
[Route("api/map")]
public class MapController : ControllerBase
{
    private const int MinSearchQueryLength = 2;
    private const int MaxSearchQueryLength = 100;
    private const int MaxSearchTypes = 4;
    private const int MaxCategories = 3;
    private const int MaxFilterValueLength = 20;
    private const double DestinationZoomThreshold = 10d;
    private const double MaxEntityViewportLatitudeSpan = 10d;
    private const double MaxEntityViewportLongitudeSpan = 10d;
    private const double MaxRadiusKm = 25d;
    private const double MaxRouteStraightLineDistanceKm = 500d;
    private const double MinRouteDistanceMeters = 5d;

    private static readonly HashSet<string> AllowedCategories = new(StringComparer.OrdinalIgnoreCase)
    {
        "places",
        "stays",
        "events",
    };

    private static readonly HashSet<string> AllowedSearchTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "destination",
        "location",
        "place",
        "event",
    };

    private readonly IMapService _mapService;

    public MapController(IMapService mapService)
    {
        _mapService = mapService;
    }

    [HttpGet("destinations")]
    [EnableRateLimiting("map")]
    public async Task<IActionResult> GetDestinations(CancellationToken cancellationToken)
    {
        return Ok(await _mapService.GetAllDestinationsAsync(cancellationToken));
    }

    [HttpGet("search")]
    [EnableRateLimiting("mapSearch")]
    public async Task<IActionResult> Search(
        [FromQuery] string? query,
        [FromQuery] string? types,
        CancellationToken cancellationToken)
    {
        var queryError = ValidateSearchQuery(query, out var trimmedQuery);
        if (queryError is not null)
            return queryError;

        if (string.IsNullOrWhiteSpace(trimmedQuery))
            return Ok(Array.Empty<object>());

        if (trimmedQuery.Length < MinSearchQueryLength)
            return Ok(Array.Empty<object>());

        var typesError = TryParseDelimitedValues(
            types,
            AllowedSearchTypes,
            MaxSearchTypes,
            "types",
            out var typeList);
        if (typesError is not null)
            return typesError;

        if (typeList.Count > 0)
        {
            return Ok(await _mapService.SearchAsync(trimmedQuery, typeList, cancellationToken));
        }

        return Ok(await _mapService.SearchAsync(trimmedQuery, cancellationToken));
    }

    [HttpGet("reverse")]
    [EnableRateLimiting("mapExternalLookup")]
    public async Task<IActionResult> ReverseGeocode(
        [FromQuery] double? latitude,
        [FromQuery] double? longitude,
        CancellationToken cancellationToken)
    {
        if (!latitude.HasValue || !longitude.HasValue || !HasValidCoordinates(latitude.Value, longitude.Value))
            return BadRequest(new { message = "Valid latitude and longitude are required." });

        var result = await _mapService.ReverseGeocodeAsync(latitude.Value, longitude.Value, cancellationToken);
        if (result is null)
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { message = "Reverse geocoding is unavailable right now." });

        return Ok(result);
    }

    [HttpGet("route")]
    [EnableRateLimiting("mapExternalLookup")]
    public async Task<IActionResult> GetWalkingRoute(
        [FromQuery] double? originLatitude,
        [FromQuery] double? originLongitude,
        [FromQuery] double? destinationLatitude,
        [FromQuery] double? destinationLongitude,
        CancellationToken cancellationToken)
    {
        if (!originLatitude.HasValue ||
            !originLongitude.HasValue ||
            !destinationLatitude.HasValue ||
            !destinationLongitude.HasValue ||
            !HasValidCoordinates(originLatitude.Value, originLongitude.Value) ||
            !HasValidCoordinates(destinationLatitude.Value, destinationLongitude.Value))
        {
            return BadRequest(new { message = "Valid origin and destination coordinates are required." });
        }

        var routeDistanceMeters = HaversineMeters(
            originLatitude.Value,
            originLongitude.Value,
            destinationLatitude.Value,
            destinationLongitude.Value);
        if (routeDistanceMeters < MinRouteDistanceMeters)
        {
            return BadRequest(new { message = "Origin and destination must be different." });
        }

        if (routeDistanceMeters > MaxRouteStraightLineDistanceKm * 1000d)
        {
            return BadRequest(new { message = $"Walking routes are limited to {MaxRouteStraightLineDistanceKm:0} km." });
        }

        var route = await _mapService.GetWalkingRouteAsync(
            originLatitude.Value,
            originLongitude.Value,
            destinationLatitude.Value,
            destinationLongitude.Value,
            cancellationToken);

        if (route is null)
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { message = "Routing is unavailable right now." });

        return Ok(route);
    }

    [HttpGet("driving-route")]
    [EnableRateLimiting("mapExternalLookup")]
    public async Task<IActionResult> GetDrivingRoute(
        [FromQuery] double? originLatitude,
        [FromQuery] double? originLongitude,
        [FromQuery] double? destinationLatitude,
        [FromQuery] double? destinationLongitude,
        CancellationToken cancellationToken)
    {
        if (!originLatitude.HasValue ||
            !originLongitude.HasValue ||
            !destinationLatitude.HasValue ||
            !destinationLongitude.HasValue ||
            !HasValidCoordinates(originLatitude.Value, originLongitude.Value) ||
            !HasValidCoordinates(destinationLatitude.Value, destinationLongitude.Value))
        {
            return BadRequest(new { message = "Valid origin and destination coordinates are required." });
        }

        var routeDistanceMeters = HaversineMeters(
            originLatitude.Value,
            originLongitude.Value,
            destinationLatitude.Value,
            destinationLongitude.Value);
        if (routeDistanceMeters < MinRouteDistanceMeters)
        {
            return BadRequest(new { message = "Origin and destination must be different." });
        }

        if (routeDistanceMeters > MaxRouteStraightLineDistanceKm * 1000d)
        {
            return BadRequest(new { message = $"Driving routes are limited to {MaxRouteStraightLineDistanceKm:0} km." });
        }

        var route = await _mapService.GetDrivingRouteAsync(
            originLatitude.Value,
            originLongitude.Value,
            destinationLatitude.Value,
            destinationLongitude.Value,
            cancellationToken);

        if (route is null)
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { message = "Routing is unavailable right now." });

        return Ok(route);
    }

    [HttpGet("markers")]
    [EnableRateLimiting("map")]
    public async Task<IActionResult> GetMarkers(
        [FromQuery] double? north,
        [FromQuery] double? south,
        [FromQuery] double? east,
        [FromQuery] double? west,
        [FromQuery] double? zoom,
        [FromQuery] string? categories,
        CancellationToken cancellationToken)
    {
        var effectiveZoom = NormalizeZoom(zoom);

        if (!north.HasValue || !south.HasValue || !east.HasValue || !west.HasValue)
        {
            return BadRequest(new { message = "Valid north/south/east/west bounds are required." });
        }

        var categoriesError = TryParseDelimitedValues(
            categories,
            AllowedCategories,
            MaxCategories,
            "categories",
            out var categoryList);
        if (categoriesError is not null)
            return categoriesError;

        var boundsError = ValidateBounds(
            north.Value,
            south.Value,
            east.Value,
            west.Value,
            effectiveZoom,
            categoryList.Count > 0);
        if (boundsError is not null)
            return boundsError;

        var result = await _mapService.GetMarkersInViewportAsync(
            north.Value,
            south.Value,
            east.Value,
            west.Value,
            effectiveZoom,
            categoryList,
            TryGetUserId(),
            cancellationToken);

        return Ok(result);
    }

    [HttpGet("entities")]
    [EnableRateLimiting("map")]
    public async Task<IActionResult> GetEntities(
        [FromQuery] double? latitude,
        [FromQuery] double? longitude,
        [FromQuery] string? categories,
        [FromQuery] double? radiusKm,
        CancellationToken cancellationToken)
    {
        if (!latitude.HasValue || !longitude.HasValue || !HasValidCoordinates(latitude.Value, longitude.Value))
            return BadRequest(new { message = "Valid latitude and longitude are required." });

        var categoriesError = TryParseDelimitedValues(
            categories,
            AllowedCategories,
            MaxCategories,
            "categories",
            out var categoryList);
        if (categoriesError is not null)
            return categoriesError;

        var radiusError = ValidateRadius(radiusKm);
        if (radiusError is not null)
            return radiusError;

        var result = await _mapService.GetEntitiesAsync(
            latitude.Value,
            longitude.Value,
            categoryList,
            radiusKm,
            TryGetUserId(),
            cancellationToken);

        return Ok(result);
    }

    private IActionResult? ValidateSearchQuery(string? query, out string trimmedQuery)
    {
        trimmedQuery = query?.Trim() ?? string.Empty;

        if (trimmedQuery.Length == 0)
            return null;

        if (trimmedQuery.Length > MaxSearchQueryLength)
            return BadRequest(new { message = $"Search query must be {MaxSearchQueryLength} characters or fewer." });

        if (trimmedQuery.Any(char.IsControl))
            return BadRequest(new { message = "Search query contains unsupported characters." });

        return null;
    }

    private IActionResult? TryParseDelimitedValues(
        string? rawValue,
        HashSet<string> allowedValues,
        int maxCount,
        string parameterName,
        out IReadOnlyList<string> values)
    {
        values = Array.Empty<string>();
        if (string.IsNullOrWhiteSpace(rawValue))
            return null;

        if (rawValue.Length > maxCount * (MaxFilterValueLength + 1))
            return BadRequest(new { message = $"{parameterName} is too long." });

        var parsed = rawValue
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(value => value.ToLowerInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (parsed.Count > maxCount)
            return BadRequest(new { message = $"{parameterName} supports at most {maxCount} values." });

        if (parsed.Any(value => value.Length == 0 || value.Length > MaxFilterValueLength))
            return BadRequest(new { message = $"{parameterName} contains an invalid value." });

        if (parsed.Any(value => !allowedValues.Contains(value)))
        {
            var allowed = string.Join(", ", allowedValues.OrderBy(value => value));
            return BadRequest(new { message = $"{parameterName} may include only: {allowed}." });
        }

        values = parsed;
        return null;
    }

    private IActionResult? ValidateBounds(
        double north,
        double south,
        double east,
        double west,
        double zoom,
        bool entityModeRequested)
    {
        if (!HasValidBounds(north, south, east, west))
            return BadRequest(new { message = "Valid north/south/east/west bounds are required." });

        if ((zoom >= DestinationZoomThreshold || entityModeRequested) &&
            (north - south > MaxEntityViewportLatitudeSpan ||
             GetLongitudeSpan(east, west) > MaxEntityViewportLongitudeSpan))
        {
            return BadRequest(new { message = "Viewport bounds are too large for entity map results." });
        }

        return null;
    }

    private IActionResult? ValidateRadius(double? radiusKm)
    {
        if (!radiusKm.HasValue)
            return null;

        if (double.IsNaN(radiusKm.Value) ||
            double.IsInfinity(radiusKm.Value) ||
            radiusKm.Value <= 0d ||
            radiusKm.Value > MaxRadiusKm)
        {
            return BadRequest(new { message = $"radiusKm must be greater than zero and at most {MaxRadiusKm:0}." });
        }

        return null;
    }

    private int? TryGetUserId()
    {
        var claim = User.FindFirst("userId")?.Value;
        return int.TryParse(claim, out var id) ? id : null;
    }

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

    private static bool HasValidBounds(double north, double south, double east, double west)
    {
        if (double.IsNaN(north) || double.IsNaN(south) || double.IsNaN(east) || double.IsNaN(west))
            return false;
        if (double.IsInfinity(north) || double.IsInfinity(south) || double.IsInfinity(east) || double.IsInfinity(west))
            return false;
        if (north < -90 || north > 90 || south < -90 || south > 90)
            return false;
        if (east < -180 || east > 180 || west < -180 || west > 180)
            return false;
        return north > south && GetLongitudeSpan(east, west) > 0d;
    }

    private static double GetLongitudeSpan(double east, double west) =>
        east > west
            ? east - west
            : (180d - west) + (east + 180d);

    private static double NormalizeZoom(double? zoom)
    {
        if (!zoom.HasValue || double.IsNaN(zoom.Value) || double.IsInfinity(zoom.Value))
            return 13d;

        return Math.Clamp(zoom.Value, 0d, 22d);
    }

    private static double HaversineMeters(double latitude1, double longitude1, double latitude2, double longitude2)
    {
        const double earthRadiusMeters = 6_371_000d;
        var deltaLatitude = DegreesToRadians(latitude2 - latitude1);
        var deltaLongitude = DegreesToRadians(longitude2 - longitude1);
        var lat1Radians = DegreesToRadians(latitude1);
        var lat2Radians = DegreesToRadians(latitude2);

        var a = Math.Sin(deltaLatitude / 2d) * Math.Sin(deltaLatitude / 2d) +
            Math.Cos(lat1Radians) * Math.Cos(lat2Radians) *
            Math.Sin(deltaLongitude / 2d) * Math.Sin(deltaLongitude / 2d);

        return earthRadiusMeters * 2d * Math.Asin(Math.Min(1d, Math.Sqrt(a)));
    }

    private static double DegreesToRadians(double degrees) => degrees * Math.PI / 180d;
}
