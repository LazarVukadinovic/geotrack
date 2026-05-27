using System.Security.Claims;
using Geotrack.Application.DTOs.Admin;
using Geotrack.Application.Interfaces.Service;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Geotrack.Server.Controllers;

[ApiController]
[Route("api/admin/reviews")]
[Authorize(Policy = "RequireReviewModerator")]
public sealed class AdminReviewsController : ControllerBase
{
    private readonly IAdminReviewManagementService _reviews;
    private readonly ILogger<AdminReviewsController> _logger;

    public AdminReviewsController(
        IAdminReviewManagementService reviews,
        ILogger<AdminReviewsController> logger)
    {
        _reviews = reviews;
        _logger = logger;
    }

    [HttpGet]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<ActionResult<AdminReviewListResponseDto>> GetReviews(
        [FromQuery] AdminReviewListRequestDto request,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _reviews.GetReviewsAsync(GetCurrentUserId(), request, cancellationToken));
        }
        catch (Exception ex)
        {
            return HandleException(ex);
        }
    }

    [HttpGet("{reviewKey}")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<ActionResult<AdminReviewDetailDto>> GetReview(
        [FromRoute] string reviewKey,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(reviewKey))
            return BadRequest(new { message = "reviewKey is required." });

        try
        {
            return Ok(await _reviews.GetReviewAsync(GetCurrentUserId(), reviewKey, cancellationToken));
        }
        catch (Exception ex)
        {
            return HandleException(ex);
        }
    }

    [HttpPost("{reviewKey}/delete")]
    public async Task<ActionResult<AdminReviewActionResultDto>> DeleteReview(
        [FromRoute] string reviewKey,
        [FromBody] AdminReviewModerationRequestDto? request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(reviewKey))
            return BadRequest(new { message = "reviewKey is required." });

        try
        {
            return Ok(await _reviews.DeleteReviewAsync(
                GetCurrentUserId(),
                reviewKey,
                request,
                cancellationToken));
        }
        catch (Exception ex)
        {
            return HandleException(ex);
        }
    }

    [HttpPost("{reviewKey}/reports/{reportId:int}/accept")]
    public async Task<ActionResult<AdminReviewActionResultDto>> AcceptReport(
        [FromRoute] string reviewKey,
        [FromRoute] int reportId,
        [FromBody] AdminReviewModerationRequestDto? request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(reviewKey))
            return BadRequest(new { message = "reviewKey is required." });

        if (reportId <= 0)
            return BadRequest(new { message = "reportId must be positive." });

        try
        {
            return Ok(await _reviews.AcceptReportAsync(
                GetCurrentUserId(),
                reviewKey,
                reportId,
                request,
                cancellationToken));
        }
        catch (Exception ex)
        {
            return HandleException(ex);
        }
    }

    [HttpPost("{reviewKey}/reports/{reportId:int}/reject")]
    public async Task<ActionResult<AdminReviewDetailDto>> RejectReport(
        [FromRoute] string reviewKey,
        [FromRoute] int reportId,
        [FromBody] AdminReviewModerationRequestDto? request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(reviewKey))
            return BadRequest(new { message = "reviewKey is required." });

        if (reportId <= 0)
            return BadRequest(new { message = "reportId must be positive." });

        try
        {
            return Ok(await _reviews.RejectReportAsync(
                GetCurrentUserId(),
                reviewKey,
                reportId,
                request,
                cancellationToken));
        }
        catch (Exception ex)
        {
            return HandleException(ex);
        }
    }

    [HttpPost("{reviewKey}/remove-and-suspend-author")]
    public async Task<ActionResult<AdminReviewActionResultDto>> RemoveReviewAndSuspendAuthor(
        [FromRoute] string reviewKey,
        [FromBody] AdminReviewModerationRequestDto? request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(reviewKey))
            return BadRequest(new { message = "reviewKey is required." });

        try
        {
            return Ok(await _reviews.RemoveReviewAndSuspendAuthorAsync(
                GetCurrentUserId(),
                reviewKey,
                request,
                cancellationToken));
        }
        catch (Exception ex)
        {
            return HandleException(ex);
        }
    }

    [HttpPost("{reviewKey}/remove-and-delete-author")]
    public async Task<ActionResult<AdminReviewActionResultDto>> RemoveReviewAndDeleteAuthor(
        [FromRoute] string reviewKey,
        [FromBody] AdminReviewModerationRequestDto? request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(reviewKey))
            return BadRequest(new { message = "reviewKey is required." });

        try
        {
            return Ok(await _reviews.RemoveReviewAndDeleteAuthorAsync(
                GetCurrentUserId(),
                reviewKey,
                request,
                cancellationToken));
        }
        catch (Exception ex)
        {
            return HandleException(ex);
        }
    }

    private int GetCurrentUserId()
    {
        var value = User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirstValue("sub")
            ?? User.FindFirstValue("userId");

        return int.TryParse(value, out var userId)
            ? userId
            : throw new UnauthorizedAccessException("Invalid admin session.");
    }

    private ActionResult HandleException(Exception ex)
    {
        return ex switch
        {
            UnauthorizedAccessException => StatusCode(StatusCodes.Status403Forbidden, new { message = "Forbidden." }),
            KeyNotFoundException => NotFound(new { message = ex.Message }),
            InvalidOperationException => BadRequest(new { message = ex.Message }),
            _ => HandleUnexpectedException(ex)
        };
    }

    private ActionResult HandleUnexpectedException(Exception ex)
    {
        _logger.LogError(
            ex,
            "Unexpected exception while handling review moderation request {Method} {Path}.",
            HttpContext.Request.Method,
            HttpContext.Request.Path.Value);

        return StatusCode(StatusCodes.Status500InternalServerError, new { message = "An unexpected error occurred." });
    }
}
