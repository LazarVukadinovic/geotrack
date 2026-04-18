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
        [FromQuery] double latitude = 42.6507,
        [FromQuery] double longitude = 18.0944)
    {
        var userId = GetUserId();
        var result = await _exploreService.GetExplorePageAsync(latitude, longitude, userId);
        return Ok(result);
    }

    [HttpGet("categories")]
    public async Task<ActionResult> GetLocalPlacesCategories([FromQuery] int? destinationId = null)
    {
        var result = await _exploreService.GetLocalPlacesCategoriesAsync(destinationId);
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
