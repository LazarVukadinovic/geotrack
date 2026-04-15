using Geotrack.Application.DTOs.Places;
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
    public async Task<IActionResult> GetPlaceReviews([FromRoute] int id)
    {
        try
        {
            var result = await _placeService.GetPlaceReviewsAsync(id);
            return Ok(result);
        }
        catch (Exception ex)
        {
            return MapException(ex);
        }
    }

    [HttpPost("{id:int}/reviews")]
    [Authorize]
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

    private IActionResult MapException(Exception ex)
    {
        return ex switch
        {
            KeyNotFoundException => NotFound(new { message = ex.Message }),
            InvalidOperationException => BadRequest(new { message = ex.Message }),
            _ => StatusCode(500, new { message = ex.Message })
        };
    }
}
