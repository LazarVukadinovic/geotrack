using System.Security.Claims;
using Geotrack.Application.DTOs.Admin;
using Geotrack.Application.Interfaces.Service;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Geotrack.Server.Controllers;

[ApiController]
[Route("api/admin/content-moderator/dashboard")]
[Authorize(Policy = "RequireContentModerator")]
public sealed class ContentModeratorDashboardController : ControllerBase
{
    private readonly IContentModeratorDashboardService _dashboardService;
    private readonly ILogger<ContentModeratorDashboardController> _logger;

    public ContentModeratorDashboardController(
        IContentModeratorDashboardService dashboardService,
        ILogger<ContentModeratorDashboardController> logger)
    {
        _dashboardService = dashboardService;
        _logger = logger;
    }

    [HttpGet]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<ActionResult<ContentModeratorDashboardDto>> GetDashboard(CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _dashboardService.GetDashboardAsync(GetCurrentUserId(), cancellationToken));
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
        _logger.LogError(ex, "Failed to load content moderator dashboard.");
        return StatusCode(StatusCodes.Status500InternalServerError, new { message = "An unexpected error occurred." });
    }
}
