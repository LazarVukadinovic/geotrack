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
    private readonly ITerritoryAnalyticsService _territoryAnalytics;
    private readonly ITerritoryUserAdminService _territoryUsers;
    private readonly IAdmBoundaryRegionSyncService _regionSync;
    private readonly ILogger<AdminTerritoryController> _logger;

    public AdminTerritoryController(
        ITerritoryDashboardService territoryDashboard,
        ITerritoryAnalyticsService territoryAnalytics,
        ITerritoryUserAdminService territoryUsers,
        IAdmBoundaryRegionSyncService regionSync,
        ILogger<AdminTerritoryController> logger)
    {
        _territoryDashboard = territoryDashboard;
        _territoryAnalytics = territoryAnalytics;
        _territoryUsers = territoryUsers;
        _regionSync = regionSync;
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

    [HttpGet("analytics/overview")]
    public async Task<ActionResult<TerritoryAnalyticsOverviewDto>> GetAnalyticsOverview(
        [FromQuery] int? territoryId = null,
        [FromQuery] string? period = null,
        [FromQuery] List<int>? regionIds = null,
        [FromQuery] string? status = null,
        [FromQuery] int skip = 0,
        [FromQuery] int take = 5,
        [FromQuery] string? sort = null,
        CancellationToken cancellationToken = default)
    {
        if (territoryId is <= 0)
            return BadRequest(new { message = "territoryId must be positive." });

        try
        {
            return Ok(await _territoryAnalytics.GetOverviewAsync(
                GetCurrentUserId(),
                new TerritoryAnalyticsOverviewQuery
                {
                    TerritoryId = territoryId,
                    Period = period,
                    RegionIds = regionIds ?? new List<int>(),
                    Status = status,
                    Skip = skip,
                    Take = take,
                    Sort = sort
                },
                cancellationToken));
        }
        catch (Exception ex)
        {
            return HandleException(ex);
        }
    }

    [HttpGet("analytics/export")]
    public async Task<IActionResult> ExportAnalytics(
        [FromQuery] int? territoryId = null,
        [FromQuery] string? period = null,
        [FromQuery] List<int>? regionIds = null,
        [FromQuery] string? status = null,
        [FromQuery] string? sort = null,
        CancellationToken cancellationToken = default)
    {
        if (territoryId is <= 0)
            return BadRequest(new { message = "territoryId must be positive." });

        try
        {
            var csv = await _territoryAnalytics.ExportCsvAsync(
                GetCurrentUserId(),
                new TerritoryAnalyticsOverviewQuery
                {
                    TerritoryId = territoryId,
                    Period = period,
                    RegionIds = regionIds ?? new List<int>(),
                    Status = status,
                    Sort = sort
                },
                cancellationToken);

            return File(
                System.Text.Encoding.UTF8.GetBytes(csv),
                "text/csv",
                "territory-analytics.csv");
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

    [HttpGet("regions/map")]
    public async Task<ActionResult<TerritoryRegionMapDto>> GetRegionMap(
        [FromQuery] int? territoryId = null,
        CancellationToken cancellationToken = default)
    {
        if (territoryId is <= 0)
            return BadRequest(new { message = "territoryId must be positive." });

        try
        {
            return Ok(await _territoryDashboard.GetRegionMapAsync(
                GetCurrentUserId(),
                territoryId,
                cancellationToken));
        }
        catch (Exception ex)
        {
            return HandleException(ex);
        }
    }

    [HttpGet("regions/create-context")]
    public async Task<ActionResult<TerritoryRegionCreateContextDto>> GetRegionCreateContext(
        [FromQuery] int? territoryId = null)
    {
        if (territoryId is <= 0)
            return BadRequest(new { message = "territoryId must be positive." });

        try
        {
            return Ok(await _territoryDashboard.GetRegionCreateContextAsync(GetCurrentUserId(), territoryId));
        }
        catch (Exception ex)
        {
            return HandleException(ex);
        }
    }

    [HttpGet("regions/boundary-candidates")]
    public async Task<ActionResult<BoundaryRegionCandidateResponseDto>> GetRegionBoundaryCandidates(
        [FromQuery] int? territoryId = null,
        [FromQuery] string? level = null,
        [FromQuery] string? search = null,
        [FromQuery] int skip = 0,
        [FromQuery] int? take = null)
    {
        if (territoryId.HasValue && territoryId.Value <= 0)
            return BadRequest(new { message = "territoryId must be positive." });

        if (skip < 0)
            return BadRequest(new { message = "skip must be zero or greater." });

        if (take is <= 0)
            return BadRequest(new { message = "take must be positive." });

        try
        {
            return Ok(await _territoryDashboard.GetRegionBoundaryCandidatesAsync(
                GetCurrentUserId(),
                territoryId,
                level,
                search,
                skip,
                take));
        }
        catch (Exception ex)
        {
            return HandleException(ex);
        }
    }

    [HttpPost("regions/sync-from-adm1")]
    public async Task<ActionResult<RegionSyncSummaryDto>> SyncRegionsFromAdm1(
        [FromQuery] int? territoryId = null,
        [FromQuery] bool dryRun = true,
        [FromQuery] bool forceUpdateGeometry = false,
        CancellationToken cancellationToken = default)
    {
        if (territoryId is <= 0)
            return BadRequest(new { message = "territoryId must be positive." });

        try
        {
            return Ok(await _regionSync.SyncRegionsFromAdm1CandidatesForTerritoryManagerAsync(
                GetCurrentUserId(),
                territoryId,
                dryRun,
                forceUpdateGeometry,
                createMissingRegions: true,
                linkExistingRegions: true,
                cancellationToken));
        }
        catch (Exception ex)
        {
            return HandleException(ex);
        }
    }

    [HttpPost("regions")]
    public async Task<ActionResult<TerritoryRegionCreateResponseDto>> CreateRegion(
        [FromBody] TerritoryRegionCreateRequestDto? request)
    {
        if (request is null)
            return BadRequest(new { message = "Request body is required." });

        if (request.TerritoryId <= 0 && !HasBoundaryCandidateRequest(request))
            return BadRequest(new { message = "territoryId is required." });

        try
        {
            var created = await _territoryDashboard.CreateRegionAsync(GetCurrentUserId(), request);
            return CreatedAtAction(
                nameof(GetRegionDetail),
                new { regionId = created.RegionId, territoryId = created.TerritoryId },
                created);
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

    [HttpGet("users/{userId:int}")]
    public async Task<ActionResult<TerritoryUserListItemDto>> GetUser(
        [FromRoute] int userId,
        [FromQuery] int? territoryId = null)
    {
        if (userId <= 0)
            return BadRequest(new { message = "userId must be positive." });

        if (territoryId is <= 0)
            return BadRequest(new { message = "territoryId must be positive." });

        try
        {
            return Ok(await _territoryUsers.GetUserAsync(
                GetCurrentUserId(),
                userId,
                territoryId));
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

    [HttpGet("users/search")]
    public async Task<ActionResult<TerritoryAssignableUserSearchResponseDto>> SearchAssignableUsers(
        [FromQuery] int? territoryId = null,
        [FromQuery] string? query = null,
        [FromQuery] int take = 10,
        [FromQuery] bool excludeAlreadyRegionalManager = false,
        [FromQuery] bool excludeAlreadyTerritoryAssociate = false)
    {
        if (territoryId is <= 0)
            return BadRequest(new { message = "territoryId must be positive." });

        try
        {
            return Ok(await _territoryUsers.SearchAssignableUsersAsync(
                GetCurrentUserId(),
                territoryId,
                query,
                take,
                excludeAlreadyRegionalManager,
                excludeAlreadyTerritoryAssociate));
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

    [HttpDelete("users/{userId:int}/regions/{regionId:int}")]
    public async Task<ActionResult<TerritoryUserListItemDto>> RemoveRegion(
        [FromRoute] int userId,
        [FromRoute] int regionId,
        [FromQuery] int? territoryId = null)
    {
        if (userId <= 0)
            return BadRequest(new { message = "userId must be positive." });

        if (regionId <= 0)
            return BadRequest(new { message = "regionId must be positive." });

        if (territoryId is <= 0)
            return BadRequest(new { message = "territoryId must be positive." });

        try
        {
            return Ok(await _territoryUsers.RemoveRegionAsync(
                GetCurrentUserId(),
                userId,
                regionId,
                territoryId));
        }
        catch (Exception ex)
        {
            return HandleException(ex);
        }
    }

    [HttpPost("users/{userId:int}/regional-manager")]
    public async Task<ActionResult<TerritoryUserListItemDto>> AddRegionalManager(
        [FromRoute] int userId,
        [FromBody] TerritoryUserAddRegionalManagerRequestDto? request)
    {
        if (userId <= 0)
            return BadRequest(new { message = "userId must be positive." });

        if (request is null)
            return BadRequest(new { message = "Request body is required." });

        if (request.TerritoryId is <= 0)
            return BadRequest(new { message = "territoryId must be positive." });

        if (request.RegionIds.Count == 0)
            return BadRequest(new { message = "At least one region is required." });

        try
        {
            return Ok(await _territoryUsers.AddRegionalManagerAsync(
                GetCurrentUserId(),
                userId,
                request));
        }
        catch (Exception ex)
        {
            return HandleException(ex);
        }
    }

    [HttpPost("users/{userId:int}/territory-associate")]
    public async Task<ActionResult<TerritoryUserListItemDto>> AddTerritoryAssociate(
        [FromRoute] int userId,
        [FromBody] TerritoryUserAddTerritoryAssociateRequestDto? request)
    {
        if (userId <= 0)
            return BadRequest(new { message = "userId must be positive." });

        if (request is null)
            return BadRequest(new { message = "Request body is required." });

        if (request.TerritoryId is <= 0)
            return BadRequest(new { message = "territoryId must be positive." });

        try
        {
            return Ok(await _territoryUsers.AddTerritoryAssociateAsync(
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

    [HttpPost("users/{userId:int}/territory-associate/remove")]
    public async Task<ActionResult<TerritoryUserListItemDto>> RemoveTerritoryAssociate(
        [FromRoute] int userId,
        [FromBody] TerritoryUserRemoveTerritoryAssociateRequestDto? request)
    {
        if (userId <= 0)
            return BadRequest(new { message = "userId must be positive." });

        if (request is null)
            return BadRequest(new { message = "Request body is required." });

        if (request.TerritoryId is <= 0)
            return BadRequest(new { message = "territoryId must be positive." });

        try
        {
            return Ok(await _territoryUsers.RemoveTerritoryAssociateAsync(
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
            InvalidOperationException when IsConflict(ex) => Conflict(new { message = ex.Message }),
            InvalidOperationException => BadRequest(new { message = ex.Message }),
            _ => StatusCode(StatusCodes.Status500InternalServerError, new { message = "An unexpected error occurred." })
        };
    }

    [HttpPost("regions/from-boundary-candidate")]
    public async Task<ActionResult<TerritoryRegionCreateResponseDto>> CreateRegionFromBoundaryCandidate(
        [FromBody] TerritoryRegionCreateRequestDto? request)
    {
        if (request is null)
            return BadRequest(new { message = "Request body is required." });

        if (!HasBoundaryCandidateRequest(request))
            return BadRequest(new { message = "Boundary candidate id is required." });

        try
        {
            var created = await _territoryDashboard.CreateRegionAsync(GetCurrentUserId(), request);
            return CreatedAtAction(
                nameof(GetRegionDetail),
                new { regionId = created.RegionId, territoryId = created.TerritoryId },
                created);
        }
        catch (Exception ex)
        {
            return HandleException(ex);
        }
    }

    private static bool IsConflict(Exception ex) =>
        string.Equals(
            ex.Message,
            "This administrative region has already been created.",
            StringComparison.Ordinal);

    private static bool HasBoundaryCandidateRequest(TerritoryRegionCreateRequestDto request) =>
        !string.IsNullOrWhiteSpace(request.ShapeId) ||
        !string.IsNullOrWhiteSpace(request.CandidateExternalKey) ||
        !string.IsNullOrWhiteSpace(request.BoundaryCandidateId) ||
        !string.IsNullOrWhiteSpace(request.BoundaryExternalKey);
}
