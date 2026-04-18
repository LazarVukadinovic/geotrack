using Geotrack.Application.DTOs.Favorites;
using Geotrack.Application.Interfaces.Service;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Geotrack.Server.Controllers;

[ApiController]
[Route("api/favorites")]
[Authorize]
public class FavoritesController : ControllerBase
{
    private readonly IFavoriteService _favoriteService;

    public FavoritesController(IFavoriteService favoriteService)
    {
        _favoriteService = favoriteService;
    }

    [HttpPost("toggle")]
    public async Task<ActionResult> Toggle([FromBody] FavoriteToggleDto dto)
    {
        var userId = GetRequiredUserId();
        if (userId == null) return Unauthorized();

        var result = await _favoriteService.ToggleAsync(userId.Value, dto);
        return result.Status switch
        {
            FavoriteToggleStatus.InvalidEntityType => BadRequest(new { error = "Invalid entityType. Allowed: 'place', 'event'." }),
            FavoriteToggleStatus.EntityNotFound    => NotFound(new { error = "Target entity not found." }),
            _ => Ok(new FavoriteToggleResponseDto { IsSaved = result.IsSaved }),
        };
    }

    [HttpGet]
    public async Task<ActionResult> GetFavorites([FromQuery] string entityType = "place")
    {
        var userId = GetRequiredUserId();
        if (userId == null) return Unauthorized();

        var ids = await _favoriteService.GetFavoriteIdsAsync(userId.Value, entityType);
        return Ok(ids);
    }

    private int? GetRequiredUserId()
    {
        var claim = User.FindFirst("userId")?.Value;
        return int.TryParse(claim, out var id) ? id : null;
    }
}
