using System.Security.Claims;
using Geotrack.Application.DTOs.Admin;
using Geotrack.Application.Interfaces.Service;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Geotrack.Server.Controllers;

[ApiController]
[Route("api/admin/dashboard")]
[Authorize(Policy = "RequireAdmin")]
public class AdminDashboardController : ControllerBase
{
    private readonly IAdminDashboardService _adminDashboardService;
    private readonly ILogger<AdminDashboardController> _logger;

    public AdminDashboardController(
        IAdminDashboardService adminDashboardService,
        ILogger<AdminDashboardController> logger)
    {
        _adminDashboardService = adminDashboardService;
        _logger = logger;
    }

    [HttpGet]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<ActionResult<AdminDashboardDto>> GetDashboard(CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _adminDashboardService.GetDashboardAsync(GetCurrentUserId(), cancellationToken));
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
        switch (ex)
        {
            case UnauthorizedAccessException:
                return StatusCode(StatusCodes.Status403Forbidden, new { message = "Forbidden." });
            case KeyNotFoundException:
                return NotFound(new { message = ex.Message });
            case InvalidOperationException:
                return BadRequest(new { message = ex.Message });
            default:
                _logger.LogError(ex, "Failed to load administrator dashboard.");
                return StatusCode(StatusCodes.Status500InternalServerError, new { message = "An unexpected error occurred." });
        }
    }
}
