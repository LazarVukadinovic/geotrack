using Geotrack.Application.Interfaces.Service;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

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
        [FromQuery] int destinationId = 1,
        [FromQuery] string? type = null,
        [FromQuery] string? tags = null,
        [FromQuery] string? category = null)
    {
        var userId = GetUserId();
        var items = await _placeService.GetPlacesAsync(destinationId, type, tags, category, userId);
        return Ok(items);
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult> GetPlaceDetail(int id)
    {
        var userId = GetUserId();
        var detail = await _placeService.GetPlaceDetailAsync(id, userId);
        if (detail == null) return NotFound();
        return Ok(detail);
    }

    [HttpGet("hotels")]
    public async Task<ActionResult> GetHotels(
        [FromQuery] int destinationId = 1,
        [FromQuery] string? tag = null)
    {
        var userId = GetUserId();
        var items = await _placeService.GetHotelsAsync(destinationId, tag, userId);
        return Ok(items);
    }

    [HttpGet("hotels/{id:int}")]
    public async Task<ActionResult> GetHotelDetail(int id)
    {
        var userId = GetUserId();
        var detail = await _placeService.GetHotelDetailAsync(id, userId);
        if (detail == null) return NotFound();
        return Ok(detail);
    }

    private int? GetUserId()
    {
        var claim = User.FindFirst("userId")?.Value;
        return int.TryParse(claim, out var id) ? id : null;
    }
}
