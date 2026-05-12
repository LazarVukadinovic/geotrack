using System.Security.Claims;
using Geotrack.Application.DTOs.Admin;
using Geotrack.Application.Interfaces.Service;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Geotrack.Server.Controllers;

[ApiController]
[Route("api/admin/regional")]
[Authorize(Policy = "RequireRegionalManager")]
public class AdminRegionalController : ControllerBase
{
    private readonly IRegionalDashboardService _regionalDashboard;

    public AdminRegionalController(IRegionalDashboardService regionalDashboard)
    {
        _regionalDashboard = regionalDashboard;
    }

    [HttpGet("regions")]
    public async Task<ActionResult<List<RegionalAssignedRegionDto>>> GetAssignedRegions()
    {
        try
        {
            return Ok(await _regionalDashboard.GetAssignedRegionsAsync(GetCurrentUserId()));
        }
        catch (Exception ex)
        {
            return HandleException(ex);
        }
    }

    [HttpGet("dashboard")]
    public async Task<ActionResult<RegionalDashboardDto>> GetDashboard([FromQuery] int regionId)
    {
        if (regionId <= 0)
            return BadRequest(new { message = "regionId is required." });

        try
        {
            return Ok(await _regionalDashboard.GetDashboardAsync(GetCurrentUserId(), regionId));
        }
        catch (Exception ex)
        {
            return HandleException(ex);
        }
    }

    [HttpGet("activity")]
    public async Task<ActionResult<RegionalDashboardActivityResponseDto>> GetActivity(
        [FromQuery] int regionId,
        [FromQuery] int skip = 0,
        [FromQuery] int take = 20)
    {
        if (regionId <= 0)
            return BadRequest(new { message = "regionId is required." });

        try
        {
            return Ok(await _regionalDashboard.GetActivityAsync(GetCurrentUserId(), regionId, skip, take));
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
            : throw new UnauthorizedAccessException("Invalid user token.");
    }

    private ActionResult HandleException(Exception ex)
    {
        return ex switch
        {
            UnauthorizedAccessException => StatusCode(StatusCodes.Status403Forbidden, new { message = "Forbidden." }),
            KeyNotFoundException => NotFound(new { message = ex.Message }),
            InvalidOperationException => BadRequest(new { message = ex.Message }),
            _ => StatusCode(StatusCodes.Status500InternalServerError, new { message = "An unexpected error occurred." })
        };
    }
}
