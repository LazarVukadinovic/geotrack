using Geotrack.Application.Interfaces.Service;
using Microsoft.AspNetCore.Mvc;

namespace Geotrack.Server.Controllers;

[ApiController]
[Route("api/map")]
public class MapController : ControllerBase
{
    private readonly IMapService _mapService;

    public MapController(IMapService mapService)
    {
        _mapService = mapService;
    }

    [HttpGet("search")]
    public async Task<IActionResult> Search(
        [FromQuery] string? query,
        [FromQuery] string? types,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(query))
            return Ok(Array.Empty<object>());

        if (!string.IsNullOrWhiteSpace(types))
        {
            var typeList = types.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            return Ok(await _mapService.SearchAsync(query, typeList, cancellationToken));
        }

        return Ok(await _mapService.SearchAsync(query, cancellationToken));
    }

    [HttpGet("reverse")]
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

    [HttpGet("markers")]
    public async Task<IActionResult> GetMarkers(
        [FromQuery] double? north,
        [FromQuery] double? south,
        [FromQuery] double? east,
        [FromQuery] double? west,
        [FromQuery] double? zoom,
        [FromQuery] string? categories,
        CancellationToken cancellationToken)
    {
        if (!north.HasValue || !south.HasValue || !east.HasValue || !west.HasValue ||
            !HasValidBounds(north.Value, south.Value, east.Value, west.Value))
        {
            return BadRequest(new { message = "Valid north/south/east/west bounds are required." });
        }

        var effectiveZoom = zoom.HasValue && !double.IsNaN(zoom.Value) && !double.IsInfinity(zoom.Value)
            ? Math.Clamp(zoom.Value, 0d, 22d)
            : 13d;

        var categoryList = (categories ?? string.Empty)
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

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
    public async Task<IActionResult> GetEntities(
        [FromQuery] double? latitude,
        [FromQuery] double? longitude,
        [FromQuery] string? categories,
        [FromQuery] double? radiusKm,
        CancellationToken cancellationToken)
    {
        if (!latitude.HasValue || !longitude.HasValue || !HasValidCoordinates(latitude.Value, longitude.Value))
            return BadRequest(new { message = "Valid latitude and longitude are required." });

        var categoryList = (categories ?? string.Empty)
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

        var result = await _mapService.GetEntitiesAsync(
            latitude.Value,
            longitude.Value,
            categoryList,
            radiusKm,
            TryGetUserId(),
            cancellationToken);

        return Ok(result);
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
        return north > south;
    }
}
