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
    private readonly IReviewReportService _reviewReportService;

    public ReviewsController(IReviewService reviewService, IReviewReportService reviewReportService)
    {
        _reviewService = reviewService;
        _reviewReportService = reviewReportService;
    }

    [HttpGet("places/{placeId:int}")]
    public async Task<ActionResult> GetPlaceReviews(int placeId)
    {
        var reviews = await _reviewService.GetPlaceReviewsAsync(placeId, GetOptionalUserId());
        return Ok(reviews);
    }

    [HttpGet("events/{eventId:int}")]
    public async Task<ActionResult> GetEventReviews(int eventId)
    {
        var reviews = await _reviewService.GetEventReviewsAsync(eventId, GetOptionalUserId());
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

    [Authorize]
    [HttpPost("reports/{targetType}/{reviewId:int}")]
    public async Task<IActionResult> ReportReview(
        [FromRoute] string targetType,
        [FromRoute] int reviewId,
        [FromBody] ReportReviewRequestDto request)
    {
        var userId = GetRequiredUserId();
        if (userId == null) return Unauthorized();

        try
        {
            var result = await _reviewReportService.ReportReviewAsync(userId.Value, targetType, reviewId, request);
            return Ok(result);
        }
        catch (Exception ex)
        {
            return MapException(ex);
        }
    }

    [Authorize(Roles = "RegionalManager")]
    [HttpGet("reports")]
    public async Task<IActionResult> GetReviewReports([FromQuery] string? status = null)
    {
        var userId = GetRequiredUserId();
        if (userId == null) return Unauthorized();

        try
        {
            var result = await _reviewReportService.GetReviewQueueAsync(userId.Value, status);
            return Ok(result);
        }
        catch (Exception ex)
        {
            return MapException(ex);
        }
    }

    [Authorize(Roles = "RegionalManager")]
    [HttpPost("reports/{reportId:int}/dismiss")]
    public async Task<IActionResult> DismissReviewReport(
        [FromRoute] int reportId,
        [FromBody] ModerateReviewReportRequestDto request)
    {
        var userId = GetRequiredUserId();
        if (userId == null) return Unauthorized();

        try
        {
            var result = await _reviewReportService.DismissReportAsync(userId.Value, reportId, request);
            return Ok(result);
        }
        catch (Exception ex)
        {
            return MapException(ex);
        }
    }

    [Authorize(Roles = "RegionalManager")]
    [HttpPost("reports/{reportId:int}/remove-review")]
    public async Task<IActionResult> RemoveReportedReview(
        [FromRoute] int reportId,
        [FromBody] ModerateReviewReportRequestDto request)
    {
        var userId = GetRequiredUserId();
        if (userId == null) return Unauthorized();

        try
        {
            var result = await _reviewReportService.RemoveReportedReviewAsync(userId.Value, reportId, request);
            return Ok(result);
        }
        catch (Exception ex)
        {
            return MapException(ex);
        }
    }

    private int? GetOptionalUserId()
    {
        var claim = User.FindFirst("userId")?.Value;
        return int.TryParse(claim, out var id) ? id : null;
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
            UnauthorizedAccessException => Unauthorized(new { message = ex.Message }),
            KeyNotFoundException => NotFound(new { message = ex.Message }),
            InvalidOperationException => BadRequest(new { message = ex.Message }),
            _ => StatusCode(500, new { message = "An unexpected error occurred." })
        };
    }
}
