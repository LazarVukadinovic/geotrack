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
    public async Task<ActionResult> GetPlaceReviews(
        int placeId,
        [FromQuery] int skip = 0,
        [FromQuery] int take = 10)
    {
        var pagingError = ValidatePaging(skip, take);
        if (pagingError is not null) return pagingError;

        var reviews = await _reviewService.GetPlaceReviewsAsync(placeId, skip, take, GetOptionalUserId());
        return Ok(reviews);
    }

    [HttpGet("events/{eventId:int}")]
    public async Task<ActionResult> GetEventReviews(
        int eventId,
        [FromQuery] int skip = 0,
        [FromQuery] int take = 10)
    {
        var pagingError = ValidatePaging(skip, take);
        if (pagingError is not null) return pagingError;

        var reviews = await _reviewService.GetEventReviewsAsync(eventId, skip, take, GetOptionalUserId());
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

    // --- Current user's own reviews ---

    [Authorize]
    [HttpGet("my")]
    public async Task<IActionResult> GetMyReviews(
        [FromQuery] int skip = 0,
        [FromQuery] int take = 10)
    {
        var userId = GetRequiredUserId();
        if (userId == null) return Unauthorized();

        var pagingError = ValidatePaging(skip, take);
        if (pagingError is not null) return pagingError;

        try
        {
            var result = await _reviewService.GetMyReviewsAsync(userId.Value, skip, take);
            return Ok(result);
        }
        catch (Exception ex)
        {
            return MapException(ex);
        }
    }

    // --- Public: any user's reviews ---

    [HttpGet("users/{userId:int}")]
    public async Task<IActionResult> GetUserReviews(
        int userId,
        [FromQuery] int skip = 0,
        [FromQuery] int take = 10)
    {
        var pagingError = ValidatePaging(skip, take);
        if (pagingError is not null) return pagingError;

        try
        {
            var result = await _reviewService.GetUserReviewsAsync(userId, skip, take);
            return Ok(result);
        }
        catch (Exception ex)
        {
            return MapException(ex);
        }
    }

    // --- Delete own review ---

    [Authorize]
    [HttpDelete("places/{reviewId:int}")]
    public async Task<IActionResult> DeletePlaceReview([FromRoute] int reviewId)
    {
        var userId = GetRequiredUserId();
        if (userId == null) return Unauthorized();

        try
        {
            await _reviewService.DeletePlaceReviewAsync(reviewId, userId.Value);
            return NoContent();
        }
        catch (Exception ex)
        {
            return MapException(ex);
        }
    }

    [Authorize]
    [HttpDelete("events/{reviewId:int}")]
    public async Task<IActionResult> DeleteEventReview([FromRoute] int reviewId)
    {
        var userId = GetRequiredUserId();
        if (userId == null) return Unauthorized();

        try
        {
            await _reviewService.DeleteEventReviewAsync(reviewId, userId.Value);
            return NoContent();
        }
        catch (Exception ex)
        {
            return MapException(ex);
        }
    }

    // --- Edit own review ---

    [Authorize]
    [HttpPut("places/{reviewId:int}")]
    public async Task<IActionResult> UpdatePlaceReview([FromRoute] int reviewId, [FromBody] CreateReviewDto dto)
    {
        var userId = GetRequiredUserId();
        if (userId == null) return Unauthorized();

        try
        {
            var result = await _reviewService.UpdatePlaceReviewAsync(reviewId, userId.Value, dto);
            return Ok(result);
        }
        catch (Exception ex)
        {
            return MapException(ex);
        }
    }

    [Authorize]
    [HttpPut("events/{reviewId:int}")]
    public async Task<IActionResult> UpdateEventReview([FromRoute] int reviewId, [FromBody] CreateReviewDto dto)
    {
        var userId = GetRequiredUserId();
        if (userId == null) return Unauthorized();

        try
        {
            var result = await _reviewService.UpdateEventReviewAsync(reviewId, userId.Value, dto);
            return Ok(result);
        }
        catch (Exception ex)
        {
            return MapException(ex);
        }
    }

    // --- Report reasons ---

    [HttpGet("report-reasons")]
    public IActionResult GetReportReasons()
    {
        return Ok(_reviewService.GetReportReasons());
    }

    // --- Reporting ---

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

    // --- Regional Manager moderation ---

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

    private int? GetRequiredUserId() => GetOptionalUserId();

    private ActionResult? ValidatePaging(int skip, int take)
    {
        if (skip < 0)
            return BadRequest(new { error = "skip must be zero or greater." });

        if (take <= 0)
            return BadRequest(new { error = "take must be greater than zero." });

        return null;
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
