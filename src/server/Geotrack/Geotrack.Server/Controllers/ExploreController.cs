using Geotrack.Application.DTOs.Destinations;
using Geotrack.Application.Interfaces.Service;
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

        var userId = GetUserId();
        var result = await _exploreService.GetExplorePageAsync(latitude.Value, longitude.Value, userId);
        return Ok(result);
    }

    [HttpGet("categories")]
    public async Task<ActionResult> GetLocalPlacesCategories(
        [FromQuery] int? destinationId = null,
        [FromQuery] double? latitude = null,
        [FromQuery] double? longitude = null)
    {
        var result = await _exploreService.GetLocalPlacesCategoriesAsync(destinationId, latitude, longitude);
        return result.Status switch
        {
            DestinationScopedStatus.Success => Ok(result.Data),
            DestinationScopedStatus.InvalidDestination => BadRequest(new { error = "destinationId is required and must be positive." }),
            DestinationScopedStatus.DestinationNotFound => NotFound(new { error = "Destination not found." }),
            _ => StatusCode(500)
        };
    }

    private int? GetUserId()
    {
        var claim = User.FindFirst("userId")?.Value;
        return int.TryParse(claim, out var id) ? id : null;
    }
}
