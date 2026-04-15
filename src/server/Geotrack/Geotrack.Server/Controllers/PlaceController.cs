using Geotrack.Application.DTOs.Places;
using Geotrack.Application.Interfaces.Service;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Geotrack.Server.Controllers;

[ApiController]
[Route("api/places")]
public class PlaceController : ControllerBase
{
    private readonly IPlaceService _placeService;

    public PlaceController(IPlaceService placeService)
    {
        _placeService = placeService;
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetPlaceDetails([FromRoute] int id)
    {
        try
        {
            var result = await _placeService.GetPlaceDetailsAsync(id, TryGetUserId());
            return Ok(result);
        }
        catch (Exception ex)
        {
            return MapException(ex);
        }
    }

    [HttpGet("{id}/events")]
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

    [HttpGet("{id}/reviews")]
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

    [HttpPost("{id}/reviews")]
    [Authorize]
    public async Task<IActionResult> CreatePlaceReview([FromRoute] int id, [FromBody] CreatePlaceReviewRequestDto request)
    {
        try
        {
            var userId = TryGetUserId();
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

    private int? TryGetUserId()
    {
        var userIdClaim = User.FindFirst("userId")?.Value;
        return int.TryParse(userIdClaim, out var userId) ? userId : null;
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