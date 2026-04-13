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
    public async Task<ActionResult> GetLocalPlacesCategories([FromQuery] int destinationId = 1)
    {
        var categories = await _exploreService.GetLocalPlacesCategoriesAsync(destinationId);
        return Ok(categories);
    }

    private int? GetUserId()
    {
        var claim = User.FindFirst("userId")?.Value;
        return int.TryParse(claim, out var id) ? id : null;
    }
}
