using System.Security.Claims;
using Geotrack.Application.DTOs.Admin;
using Geotrack.Application.Interfaces.Service;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Geotrack.Server.Controllers;

[ApiController]
[Route("api/admin/territory")]
[Authorize(Policy = "RequireTerritoryManager")]
public sealed class AdminTerritoryController : ControllerBase
{
    private readonly ITerritoryDashboardService _territoryDashboard;
    private readonly ILogger<AdminTerritoryController> _logger;

    public AdminTerritoryController(
        ITerritoryDashboardService territoryDashboard,
        ILogger<AdminTerritoryController> logger)
    {
        _territoryDashboard = territoryDashboard;
        _logger = logger;
    }

    [HttpGet("territories")]
    public async Task<ActionResult<List<TerritoryAssignedTerritoryDto>>> GetAssignedTerritories()
    {
        try
        {
            return Ok(await _territoryDashboard.GetAssignedTerritoriesAsync(GetCurrentUserId()));
        }
        catch (Exception ex)
        {
            return HandleException(ex);
        }
    }

    [HttpGet("sidebar-summary")]
    public async Task<ActionResult<TerritorySidebarSummaryDto>> GetSidebarSummary([FromQuery] int? territoryId = null)
    {
        if (territoryId is <= 0)
            return BadRequest(new { message = "territoryId must be positive." });

        try
        {
            return Ok(await _territoryDashboard.GetSidebarSummaryAsync(GetCurrentUserId(), territoryId));
        }
        catch (Exception ex)
        {
            return HandleException(ex);
        }
    }

    [HttpGet("dashboard")]
    public async Task<ActionResult<TerritoryDashboardDto>> GetDashboard([FromQuery] int? territoryId = null)
    {
        if (territoryId is <= 0)
            return BadRequest(new { message = "territoryId must be positive." });

        try
        {
            return Ok(await _territoryDashboard.GetDashboardAsync(GetCurrentUserId(), territoryId));
        }
        catch (Exception ex)
        {
            return HandleException(ex);
        }
    }

    [HttpGet("regions")]
    public async Task<ActionResult<TerritoryRegionDirectoryResponseDto>> GetRegions(
        [FromQuery] int? territoryId = null,
        [FromQuery] string? search = null,
        [FromQuery] string? sort = null,
        [FromQuery] int skip = 0,
        [FromQuery] int take = 25)
    {
        if (territoryId is <= 0)
            return BadRequest(new { message = "territoryId must be positive." });

        try
        {
            return Ok(await _territoryDashboard.GetRegionsAsync(
                GetCurrentUserId(),
                territoryId,
                search,
                sort,
                skip,
                take));
        }
        catch (Exception ex)
        {
            return HandleException(ex);
        }
    }

    [HttpGet("content")]
    public async Task<ActionResult<TerritoryContentDirectoryResponseDto>> GetContent(
        [FromQuery] int? territoryId = null,
        [FromQuery] string? type = null,
        [FromQuery] string? search = null,
        [FromQuery] string? sort = null,
        [FromQuery] int skip = 0,
        [FromQuery] int take = 25)
    {
        if (territoryId is <= 0)
            return BadRequest(new { message = "territoryId must be positive." });

        try
        {
            return Ok(await _territoryDashboard.GetContentAsync(
                GetCurrentUserId(),
                territoryId,
                type,
                search,
                sort,
                skip,
                take));
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
        if (ex is not UnauthorizedAccessException &&
            ex is not KeyNotFoundException &&
            ex is not InvalidOperationException)
        {
            _logger.LogError(ex, "Unexpected territory admin request failure.");
        }

        return ex switch
        {
            UnauthorizedAccessException => StatusCode(StatusCodes.Status403Forbidden, new { message = "Forbidden." }),
            KeyNotFoundException => NotFound(new { message = ex.Message }),
            InvalidOperationException => BadRequest(new { message = ex.Message }),
            _ => StatusCode(StatusCodes.Status500InternalServerError, new { message = "An unexpected error occurred." })
        };
    }
}
