using System.Security.Claims;
using Geotrack.Application.DTOs.Admin;
using Geotrack.Application.Interfaces.Service;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Geotrack.Server.Controllers;

[ApiController]
[Route("api/admin/content-moderator")]
[Authorize(Policy = "RequireContentModerator")]
public sealed class ContentModeratorEntityReviewController : ControllerBase
{
    private readonly IContentModeratorEntityReviewService _entityReviewService;
    private readonly ILogger<ContentModeratorEntityReviewController> _logger;

    public ContentModeratorEntityReviewController(
        IContentModeratorEntityReviewService entityReviewService,
        ILogger<ContentModeratorEntityReviewController> logger)
    {
        _entityReviewService = entityReviewService;
        _logger = logger;
    }

    [HttpGet("entities")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<ActionResult<ContentModeratorEntityReviewListResponseDto>> GetEntities(
        [FromQuery] string? type,
        [FromQuery] string? search,
        [FromQuery] string? status,
        [FromQuery] string? sort,
        [FromQuery] int skip = 0,
        [FromQuery] int take = 25,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _entityReviewService.GetEntitiesAsync(
                GetCurrentUserId(),
                type,
                search,
                status,
                sort,
                skip,
                take,
                cancellationToken);

            return Ok(response);
        }
        catch (Exception ex)
        {
            return HandleException(ex);
        }
    }

    [HttpGet("entities/{type}/{entityId:int}")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<ActionResult<ContentModeratorEntityReviewDetailDto>> GetEntity(
        string type,
        int entityId,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _entityReviewService.GetEntityAsync(
                GetCurrentUserId(),
                type,
                entityId,
                cancellationToken));
        }
        catch (Exception ex)
        {
            return HandleException(ex);
        }
    }

    [HttpPost("entities/{type}/{entityId:int}/suspend")]
    public async Task<ActionResult<ContentModeratorEntityReviewDetailDto>> SuspendEntity(
        string type,
        int entityId,
        [FromBody] ContentModeratorModerationActionRequestDto? request,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _entityReviewService.SuspendEntityAsync(
                GetCurrentUserId(),
                type,
                entityId,
                request,
                cancellationToken));
        }
        catch (Exception ex)
        {
            return HandleException(ex);
        }
    }

    [HttpPost("entities/{type}/{entityId:int}/unsuspend")]
    public async Task<ActionResult<ContentModeratorEntityReviewDetailDto>> UnsuspendEntity(
        string type,
        int entityId,
        [FromBody] ContentModeratorModerationActionRequestDto? request,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _entityReviewService.UnsuspendEntityAsync(
                GetCurrentUserId(),
                type,
                entityId,
                request,
                cancellationToken));
        }
        catch (Exception ex)
        {
            return HandleException(ex);
        }
    }

    [HttpDelete("entities/{type}/{entityId:int}")]
    public async Task<ActionResult<ContentModeratorEntityReviewDetailDto>> DeleteEntity(
        string type,
        int entityId,
        [FromBody] ContentModeratorModerationActionRequestDto? request,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _entityReviewService.DeleteEntityAsync(
                GetCurrentUserId(),
                type,
                entityId,
                request,
                cancellationToken));
        }
        catch (Exception ex)
        {
            return HandleException(ex);
        }
    }

    [HttpPost("entities/{type}/{entityId:int}/delete")]
    public async Task<ActionResult<ContentModeratorEntityReviewDetailDto>> DeleteEntityPost(
        string type,
        int entityId,
        [FromBody] ContentModeratorModerationActionRequestDto? request,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _entityReviewService.DeleteEntityAsync(
                GetCurrentUserId(),
                type,
                entityId,
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
        _logger.LogError(ex, "Failed to process content moderator entity review request.");
        return StatusCode(StatusCodes.Status500InternalServerError, new { message = "An unexpected error occurred." });
    }
}
