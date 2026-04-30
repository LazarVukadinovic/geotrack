using Geotrack.Application.DTOs.Destinations;
using Geotrack.Application.Interfaces.Service;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Geotrack.Server.Controllers;

[ApiController]
[Route("api/explore")]
public class ExploreController : ControllerBase
{
    private readonly IExploreService _exploreService;

    public ExploreController(IExploreService exploreService)
    {
        _exploreService = exploreService;
    }

    [HttpGet]
    public async Task<ActionResult> GetExplorePage(
        [FromQuery] double? latitude,
        [FromQuery] double? longitude)
    {
        if (!latitude.HasValue || !longitude.HasValue)
        {
            return BadRequest(new { error = "latitude and longitude are required." });
        }

        if (!HasValidCoordinates(latitude.Value, longitude.Value))
        {
            return BadRequest(new { error = "latitude and longitude must be finite values in valid coordinate ranges." });
        }

        var userId = GetUserId();
        var result = await _exploreService.GetExplorePageAsync(latitude.Value, longitude.Value, userId);
        return Ok(result);
    }

    [HttpGet("categories")]
    public async Task<ActionResult> GetLocalPlacesCategories(
        [FromQuery] int? destinationId = null,
        [FromQuery] double? latitude = null,
        [FromQuery] double? longitude = null,
        [FromQuery] int skip = 0,
        [FromQuery] int? take = null)
    {
        if (skip < 0)
        {
            return BadRequest(new { error = "skip must be zero or greater." });
        }

        if (take.HasValue && take.Value <= 0)
        {
            return BadRequest(new { error = "take must be greater than zero." });
        }

        var result = await _exploreService.GetLocalPlacesCategoriesAsync(destinationId, latitude, longitude, skip, take ?? 10);
        var wantsPagedResponse = Request.Query.ContainsKey("skip") || Request.Query.ContainsKey("take");
        return result.Status switch
        {
            DestinationScopedStatus.Success => Ok(wantsPagedResponse ? result.Data! : result.Data!.Items),
            DestinationScopedStatus.InvalidDestination => BadRequest(new { error = "destinationId is required and must be positive." }),
            DestinationScopedStatus.DestinationNotFound => NotFound(new { error = "Destination not found." }),
            _ => StatusCode(500)
        };
    }

    [Authorize]
    [HttpGet("recommendations")]
    public async Task<ActionResult> GetRecommendations(
        [FromQuery] double? latitude,
        [FromQuery] double? longitude,
        [FromQuery] int? tagId = null,
        [FromQuery] int skip = 0,
        [FromQuery] int take = 8)
    {
        if (!latitude.HasValue || !longitude.HasValue)
        {
            return BadRequest(new { error = "latitude and longitude are required." });
        }

        if (!HasValidCoordinates(latitude.Value, longitude.Value))
        {
            return BadRequest(new { error = "latitude and longitude must be finite values in valid coordinate ranges." });
        }

        if (tagId.HasValue && tagId.Value <= 0)
        {
            return BadRequest(new { error = "tagId must be positive when provided." });
        }

        if (skip < 0)
        {
            return BadRequest(new { error = "skip must be zero or greater." });
        }

        if (take <= 0)
        {
            return BadRequest(new { error = "take must be greater than zero." });
        }

        var userId = GetUserId();
        if (!userId.HasValue)
        {
            return Unauthorized();
        }

        var result = await _exploreService.GetRecommendationsAsync(
            latitude.Value,
            longitude.Value,
            userId.Value,
            tagId,
            skip,
            take);

        return Ok(result);
    }

    private int? GetUserId()
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
}
