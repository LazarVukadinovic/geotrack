using Geotrack.Application.DTOs.Destinations;
using Geotrack.Application.DTOs.Places;
using Geotrack.Application.Interfaces.Service;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Geotrack.Server.Controllers;

[ApiController]
[Route("api/places")]
public class PlacesController : ControllerBase
{
    private readonly IPlaceService _placeService;

    public PlacesController(IPlaceService placeService)
    {
        _placeService = placeService;
    }

    [HttpGet]
    public async Task<ActionResult> GetPlaces(
        [FromQuery] int? destinationId = null,
        [FromQuery] string? type = null,
        [FromQuery] string? tags = null,
        [FromQuery] string? category = null,
        [FromQuery] double? latitude = null,
        [FromQuery] double? longitude = null,
        [FromQuery] int skip = 0,
        [FromQuery] int? take = null)
    {
        var pagingError = ValidatePaging(skip, take);
        if (pagingError is not null)
        {
            return pagingError;
        }

        var coordinateError = ValidateOptionalCoordinates(latitude, longitude);
        if (coordinateError is not null)
        {
            return coordinateError;
        }

        var userId = GetUserId();
        var result = await _placeService.GetPlacesAsync(destinationId, type, tags, category, userId, latitude, longitude, skip, take);
        var wantsPagedResponse = Request.Query.ContainsKey("skip") || Request.Query.ContainsKey("take");
        return result.Status switch
        {
            DestinationScopedStatus.Success => Ok(wantsPagedResponse ? result.Data! : result.Data!.Items),
            DestinationScopedStatus.InvalidDestination => BadRequest(new { error = "destinationId is required and must be positive." }),
            DestinationScopedStatus.DestinationNotFound => NotFound(new { error = "Destination not found." }),
            _ => StatusCode(500)
        };
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult> GetPlaceDetail(
        int id,
        [FromQuery] double? latitude = null,
        [FromQuery] double? longitude = null,
        [FromQuery] string? distanceOrigin = null)
    {
        var userId = GetUserId();
        var detail = await _placeService.GetPlaceDetailAsync(id, userId, latitude, longitude, UseCurrentLocationDistance(distanceOrigin));
        if (detail == null) return NotFound();
        return Ok(detail);
    }

    [HttpGet("hotels")]
    public async Task<ActionResult> GetHotels(
        [FromQuery] int? destinationId = null,
        [FromQuery] string? tag = null,
        [FromQuery] double? latitude = null,
        [FromQuery] double? longitude = null,
        [FromQuery] int skip = 0,
        [FromQuery] int? take = null)
    {
        var pagingError = ValidatePaging(skip, take);
        if (pagingError is not null)
        {
            return pagingError;
        }

        var coordinateError = ValidateOptionalCoordinates(latitude, longitude);
        if (coordinateError is not null)
        {
            return coordinateError;
        }

        var userId = GetUserId();
        var result = await _placeService.GetHotelsAsync(destinationId, tag, userId, latitude, longitude, skip, take);
        var wantsPagedResponse = Request.Query.ContainsKey("skip") || Request.Query.ContainsKey("take");
        return result.Status switch
        {
            DestinationScopedStatus.Success => Ok(wantsPagedResponse ? result.Data! : result.Data!.Items),
            DestinationScopedStatus.InvalidDestination => BadRequest(new { error = "destinationId is required and must be positive." }),
            DestinationScopedStatus.DestinationNotFound => NotFound(new { error = "Destination not found." }),
            _ => StatusCode(500)
        };
    }

    [HttpGet("hotels/{id:int}")]
    public async Task<ActionResult> GetHotelDetail(
        int id,
        [FromQuery] double? latitude = null,
        [FromQuery] double? longitude = null,
        [FromQuery] string? distanceOrigin = null)
    {
        var userId = GetUserId();
        var detail = await _placeService.GetHotelDetailAsync(id, userId, latitude, longitude, UseCurrentLocationDistance(distanceOrigin));
        if (detail == null) return NotFound();
        return Ok(detail);
    }

    [HttpGet("{id:int}/events")]
    public async Task<IActionResult> GetPlaceEvents([FromRoute] int id)
    {
        try
        {
            var result = await _placeService.GetPlaceEventsAsync(id);
            return Ok(result);
        }
        catch (Exception ex)
        {
            return MapException(ex);
        }
    }

    [HttpGet("{id:int}/reviews")]
    public async Task<IActionResult> GetPlaceReviews(
        [FromRoute] int id,
        [FromQuery] int skip = 0,
        [FromQuery] int take = 10)
    {
        var pagingError = ValidatePaging(skip, take);
        if (pagingError is not null)
        {
            return pagingError;
        }

        try
        {
            var result = await _placeService.GetPlaceReviewsAsync(id, skip, take);
            return Ok(result);
        }
        catch (Exception ex)
        {
            return MapException(ex);
        }
    }

    [HttpPut("{id:int}/owner-update")]
    [Authorize]
    [EnableRateLimiting("contributions")]
    public async Task<IActionResult> OwnerUpdatePlace([FromRoute] int id, [FromBody] OwnerUpdatePlaceRequestDto request)
    {
        var userId = GetUserId();
        if (!userId.HasValue)
            return Unauthorized();

        try
        {
            var result = await _placeService.OwnerUpdatePlaceAsync(id, userId.Value, request);
            return Ok(result);
        }
        catch (Exception ex)
        {
            return ex is UnauthorizedAccessException
                ? StatusCode(403, new { message = "Forbidden." })
                : MapException(ex);
        }
    }

    [HttpPost("{id:int}/reviews")]
    [Authorize]
    [EnableRateLimiting("reviews")]
    public async Task<IActionResult> CreatePlaceReview([FromRoute] int id, [FromBody] CreatePlaceReviewRequestDto request)
    {
        try
        {
            var userId = GetUserId();
            if (!userId.HasValue)
                return Unauthorized();

            var result = await _placeService.CreatePlaceReviewAsync(id, userId.Value, request);
            return CreatedAtAction(nameof(GetPlaceReviews), new { id }, result);
        }
        catch (Exception ex)
        {
            return MapException(ex);
        }
    }

    private int? GetUserId()
    {
        var claim = User.FindFirst("userId")?.Value;
        return int.TryParse(claim, out var id) ? id : null;
    }

    private ActionResult? ValidatePaging(int skip, int? take)
    {
        if (skip < 0)
        {
            return BadRequest(new { error = "skip must be zero or greater." });
        }

        if (take.HasValue && take.Value <= 0)
        {
            return BadRequest(new { error = "take must be greater than zero." });
        }

        return null;
    }

    private ActionResult? ValidateOptionalCoordinates(double? latitude, double? longitude)
    {
        if (!latitude.HasValue && !longitude.HasValue)
        {
            return null;
        }

        if (latitude.HasValue != longitude.HasValue)
        {
            return BadRequest(new { error = "latitude and longitude must be provided together." });
        }

        if (!HasValidCoordinates(latitude!.Value, longitude!.Value))
        {
            return BadRequest(new { error = "latitude and longitude must be finite values in valid coordinate ranges." });
        }

        return null;
    }

    private static bool UseCurrentLocationDistance(string? distanceOrigin) =>
        string.Equals(distanceOrigin, "current-location", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(distanceOrigin, "user", StringComparison.OrdinalIgnoreCase);

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

    private IActionResult MapException(Exception ex)
    {
        return ex switch
        {
            KeyNotFoundException => NotFound(new { message = ex.Message }),
            InvalidOperationException => BadRequest(new { message = ex.Message }),
            _ => StatusCode(500, new { message = "An unexpected error occurred." })
        };
    }
}
