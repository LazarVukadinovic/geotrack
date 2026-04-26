using Geotrack.Application.DTOs.Reviews;
using Geotrack.Application.Interfaces.Service;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Geotrack.Server.Controllers;

[ApiController]
[Route("api/reviews")]
public class ReviewsController : ControllerBase
{
    private readonly IReviewService _reviewService;

    public ReviewsController(IReviewService reviewService)
    {
        _reviewService = reviewService;
    }

    [HttpGet("places/{placeId:int}")]
    public async Task<ActionResult> GetPlaceReviews(int placeId)
    {
        var reviews = await _reviewService.GetPlaceReviewsAsync(placeId);
        return Ok(reviews);
    }

    [HttpGet("events/{eventId:int}")]
    public async Task<ActionResult> GetEventReviews(int eventId)
    {
        var reviews = await _reviewService.GetEventReviewsAsync(eventId);
        return Ok(reviews);
    }

    [Authorize]
    [HttpPost("places/{placeId:int}")]
    public async Task<IActionResult> CreatePlaceReview(int placeId, [FromBody] CreateReviewDto dto)
    {
        var userId = GetRequiredUserId();
        if (userId == null) return Unauthorized();

        try
        {
            var review = await _reviewService.CreatePlaceReviewAsync(placeId, userId.Value, dto);
            return Ok(review);
        }
        catch (Exception ex)
        {
            return MapException(ex);
        }
    }

    [Authorize]
    [HttpPost("events/{eventId:int}")]
    public async Task<IActionResult> CreateEventReview(int eventId, [FromBody] CreateReviewDto dto)
    {
        var userId = GetRequiredUserId();
        if (userId == null) return Unauthorized();

        try
        {
            var review = await _reviewService.CreateEventReviewAsync(eventId, userId.Value, dto);
            return Ok(review);
        }
        catch (Exception ex)
        {
            return MapException(ex);
        }
    }

    private int? GetRequiredUserId()
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
            _ => StatusCode(500, new { message = "An unexpected error occurred." })
        };
    }
}
