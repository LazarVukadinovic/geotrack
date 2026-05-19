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
    private readonly ITerritoryUserAdminService _territoryUsers;
    private readonly ILogger<AdminTerritoryController> _logger;

    public AdminTerritoryController(
        ITerritoryDashboardService territoryDashboard,
        ITerritoryUserAdminService territoryUsers,
        ILogger<AdminTerritoryController> logger)
    {
        _territoryDashboard = territoryDashboard;
        _territoryUsers = territoryUsers;
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

    [HttpGet("regions/{regionId:int}/detail")]
    public async Task<ActionResult<TerritoryRegionDetailDto>> GetRegionDetail(
        [FromRoute] int regionId,
        [FromQuery] int? territoryId = null,
        [FromQuery] string? period = null)
    {
        if (regionId <= 0)
            return BadRequest(new { message = "regionId must be positive." });

        if (territoryId is <= 0)
            return BadRequest(new { message = "territoryId must be positive." });

        try
        {
            return Ok(await _territoryDashboard.GetRegionDetailAsync(
                GetCurrentUserId(),
                regionId,
                territoryId,
                period));
        }
        catch (Exception ex)
        {
            return HandleException(ex);
        }
    }

    [HttpGet("regions/{regionId:int}/content")]
    public async Task<ActionResult<TerritoryRegionContentResponseDto>> GetRegionContent(
        [FromRoute] int regionId,
        [FromQuery] int? territoryId = null,
        [FromQuery] string? entityType = null,
        [FromQuery] string? search = null,
        [FromQuery] string? sort = null,
        [FromQuery] int skip = 0,
        [FromQuery] int take = 25)
    {
        if (regionId <= 0)
            return BadRequest(new { message = "regionId must be positive." });

        if (territoryId is <= 0)
            return BadRequest(new { message = "territoryId must be positive." });

        try
        {
            return Ok(await _territoryDashboard.GetRegionContentAsync(
                GetCurrentUserId(),
                regionId,
                territoryId,
                entityType,
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

    [HttpGet("users")]
    public async Task<ActionResult<TerritoryUserListResponseDto>> GetUsers(
        [FromQuery] int? territoryId = null,
        [FromQuery] string? search = null,
        [FromQuery] string? sort = null,
        [FromQuery] int skip = 0,
        [FromQuery] int take = 8)
    {
        if (territoryId is <= 0)
            return BadRequest(new { message = "territoryId must be positive." });

        try
        {
            return Ok(await _territoryUsers.GetUsersAsync(
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

    [HttpPut("users/{userId:int}")]
    public async Task<ActionResult<TerritoryUserListItemDto>> UpdateUser(
        [FromRoute] int userId,
        [FromBody] TerritoryUserUpdateRequestDto? request)
    {
        if (userId <= 0)
            return BadRequest(new { message = "userId must be positive." });

        if (request is null)
            return BadRequest(new { message = "Request body is required." });

        if (request.TerritoryId is <= 0)
            return BadRequest(new { message = "territoryId must be positive." });

        try
        {
            return Ok(await _territoryUsers.UpdateUserAsync(
                GetCurrentUserId(),
                userId,
                request));
        }
        catch (Exception ex)
        {
            return HandleException(ex);
        }
    }

    [HttpGet("users/{userId:int}/available-regions")]
    public async Task<ActionResult<TerritoryUserAvailableRegionsResponseDto>> GetAvailableRegions(
        [FromRoute] int userId,
        [FromQuery] int? territoryId = null,
        [FromQuery] string? search = null,
        [FromQuery] int take = 50)
    {
        if (userId <= 0)
            return BadRequest(new { message = "userId must be positive." });

        if (territoryId is <= 0)
            return BadRequest(new { message = "territoryId must be positive." });

        try
        {
            return Ok(await _territoryUsers.GetAvailableRegionsAsync(
                GetCurrentUserId(),
                userId,
                territoryId,
                search,
                take));
        }
        catch (Exception ex)
        {
            return HandleException(ex);
        }
    }

    [HttpPost("users/{userId:int}/regions")]
    public async Task<ActionResult<TerritoryUserListItemDto>> AddRegion(
        [FromRoute] int userId,
        [FromBody] TerritoryUserAssignRegionRequestDto? request)
    {
        if (userId <= 0)
            return BadRequest(new { message = "userId must be positive." });

        if (request is null)
            return BadRequest(new { message = "Request body is required." });

        if (request.TerritoryId is <= 0)
            return BadRequest(new { message = "territoryId must be positive." });

        try
        {
            return Ok(await _territoryUsers.AddRegionAsync(
                GetCurrentUserId(),
                userId,
                request));
        }
        catch (Exception ex)
        {
            return HandleException(ex);
        }
    }

    [HttpPost("users/{userId:int}/remove-manager-role")]
    public async Task<ActionResult<TerritoryUserListItemDto>> RemoveManagerRole(
        [FromRoute] int userId,
        [FromBody] TerritoryUserRemoveManagerRoleRequestDto? request)
    {
        if (userId <= 0)
            return BadRequest(new { message = "userId must be positive." });

        if (request is null)
            return BadRequest(new { message = "Request body is required." });

        if (request.TerritoryId is <= 0)
            return BadRequest(new { message = "territoryId must be positive." });

        try
        {
            return Ok(await _territoryUsers.RemoveManagerRoleAsync(
                GetCurrentUserId(),
                userId,
                request));
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
