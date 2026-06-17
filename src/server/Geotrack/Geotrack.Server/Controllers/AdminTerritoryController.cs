using System.Security.Claims;
using Geotrack.Application.DTOs.Admin;
using Geotrack.Application.Interfaces.Service;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;

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
    private readonly ILocalBoundaryImportService _localBoundaryImport;
    private readonly IEntityProfileService _entityProfile;
    private readonly IRegionalEntityImageStorageService _imageStorage;
    private readonly ILogger<AdminTerritoryController> _logger;

    public AdminTerritoryController(
        ITerritoryDashboardService territoryDashboard,
        ITerritoryAnalyticsService territoryAnalytics,
        ITerritoryUserAdminService territoryUsers,
        IAdmBoundaryRegionSyncService regionSync,
        ILocalBoundaryImportService localBoundaryImport,
        IEntityProfileService entityProfile,
        IRegionalEntityImageStorageService imageStorage,
        ILogger<AdminTerritoryController> logger)
    {
        _territoryDashboard = territoryDashboard;
        _territoryAnalytics = territoryAnalytics;
        _territoryUsers = territoryUsers;
        _regionSync = regionSync;
        _localBoundaryImport = localBoundaryImport;
        _entityProfile = entityProfile;
        _imageStorage = imageStorage;
        _logger = logger;
    }

    [HttpGet("territory-profile")]
    public async Task<ActionResult<EntityProfileDto>> GetTerritoryProfile(
        [FromQuery] int territoryId,
        CancellationToken cancellationToken = default)
    {
        if (territoryId <= 0)
            return BadRequest(new { message = "territoryId is required." });

        try
        {
            return Ok(await _entityProfile.GetTerritoryProfileAsync(GetCurrentUserId(), territoryId, cancellationToken));
        }
        catch (Exception ex)
        {
            return HandleException(ex);
        }
    }

    [HttpPut("territory-profile")]
    public async Task<ActionResult<EntityProfileDto>> UpdateTerritoryProfile(
        [FromQuery] int territoryId,
        [FromBody] EntityProfileUpdateRequestDto? request,
        CancellationToken cancellationToken = default)
    {
        if (territoryId <= 0)
            return BadRequest(new { message = "territoryId is required." });

        if (request is null)
            return BadRequest(new { message = "Request body is required." });

        try
        {
            return Ok(await _entityProfile.UpdateTerritoryProfileAsync(GetCurrentUserId(), territoryId, request, cancellationToken));
        }
        catch (Exception ex)
        {
            return HandleException(ex);
        }
    }

    [HttpGet("territory-tags")]
    public async Task<ActionResult<List<EntityProfileTagDto>>> GetTerritoryTags(
        [FromQuery] int territoryId,
        CancellationToken cancellationToken = default)
    {
        if (territoryId <= 0)
            return BadRequest(new { message = "territoryId is required." });

        try
        {
            return Ok(await _entityProfile.GetTerritoryTagsAsync(GetCurrentUserId(), territoryId, cancellationToken));
        }
        catch (Exception ex)
        {
            return HandleException(ex);
        }
    }

    [HttpPost("media/images")]
    [Consumes("multipart/form-data")]
    [Microsoft.AspNetCore.RateLimiting.EnableRateLimiting("uploads")]
    [RequestSizeLimit(Geotrack.Application.Options.UploadLimitDefaults.RegionalMaxTotalRequestBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = Geotrack.Application.Options.UploadLimitDefaults.RegionalMaxTotalRequestBytes)]
    public async Task<ActionResult<RegionalImageUploadResponseDto>> UploadTerritoryImages(
        [FromQuery] int territoryId,
        List<IFormFile>? files,
        CancellationToken cancellationToken = default)
    {
        if (territoryId <= 0)
            return BadRequest(new { message = "territoryId is required." });

        if (files is null || files.Count == 0)
            return BadRequest(new { message = "Please select at least one image to upload." });

        try
        {
            var uploads = new List<RegionalImageUploadFileDto>();
            foreach (var file in files.Where(f => f is { Length: > 0 }))
            {
                await using var stream = new MemoryStream();
                await file.CopyToAsync(stream, cancellationToken);
                uploads.Add(new RegionalImageUploadFileDto
                {
                    FileName = file.FileName,
                    ContentType = file.ContentType,
                    Content = stream.ToArray()
                });
            }

            return Ok(await _entityProfile.UploadTerritoryImagesAsync(GetCurrentUserId(), territoryId, uploads, cancellationToken));
        }
        catch (Exception ex)
        {
            return HandleException(ex);
        }
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

    [HttpGet("regions/setup-status")]
    public async Task<ActionResult<TerritoryRegionSetupStatusDto>> GetRegionSetupStatus(
        [FromQuery] int? territoryId = null,
        CancellationToken cancellationToken = default)
    {
        if (territoryId is <= 0)
            return BadRequest(new { message = "territoryId must be positive." });

        try
        {
            return Ok(await _regionSync.GetRegionSetupStatusForTerritoryManagerAsync(
                GetCurrentUserId(),
                territoryId,
                cancellationToken));
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
        [FromQuery] int? take = null,
        [FromQuery] string? geometry = null)
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
                take,
                geometry));
        }
        catch (Exception ex)
        {
            return HandleException(ex);
        }
    }

    [HttpPost("regions/sync-from-adm1")]
    public async Task<ActionResult<RegionSyncSummaryDto>> SyncRegionsFromAdm1(
        [FromQuery] int? territoryId = null,
        [FromQuery] bool dryRun = false,
        [FromQuery] bool force = false,
        [FromQuery] bool forceUpdateGeometry = false,
        CancellationToken cancellationToken = default)
    {
        if (territoryId is <= 0)
            return BadRequest(new { message = "territoryId must be positive." });

        try
        {
            var userId = GetCurrentUserId();
            var setupStatus = await _regionSync.GetRegionSetupStatusForTerritoryManagerAsync(
                userId,
                territoryId,
                cancellationToken);

            if (setupStatus.MissingCandidateData)
            {
                if (dryRun)
                {
                    throw new InvalidOperationException(
                        "ADM1 boundary data has not been imported yet. Run Get Started to import boundaries and initialize regions.");
                }

                _logger.LogInformation(
                    "Territory Manager {UserId} is importing ADM1 boundaries for territory {TerritoryId} ({CountryIso3}) before region setup.",
                    userId,
                    setupStatus.TerritoryId,
                    setupStatus.CountryIso3);

                var importSummary = await _localBoundaryImport.ImportLocalBoundariesAsync(
                    "ADM1",
                    forceRefresh: force,
                    countryIso3: setupStatus.CountryIso3,
                    take: null,
                    cancellationToken: cancellationToken);

                _logger.LogInformation(
                    "Territory Manager ADM1 import completed for territory {TerritoryId} ({CountryIso3}). imported={Imported}; updated={Updated}; skipped={Skipped}; failed={Failed}.",
                    setupStatus.TerritoryId,
                    setupStatus.CountryIso3,
                    importSummary.ImportedCandidatesCount,
                    importSummary.UpdatedCandidatesCount,
                    importSummary.SkippedCandidatesCount,
                    importSummary.FailedCandidatesCount);

                setupStatus = await _regionSync.GetRegionSetupStatusForTerritoryManagerAsync(
                    userId,
                    setupStatus.TerritoryId,
                    cancellationToken);

                if (setupStatus.MissingCandidateData)
                {
                    var firstFailure = importSummary.FailedItems
                        .Select(item => item.Message)
                        .FirstOrDefault(message => !string.IsNullOrWhiteSpace(message));

                    throw new InvalidOperationException(
                        firstFailure is null
                            ? $"ADM1 boundary import completed, but no boundary candidates were found for {setupStatus.CountryIso3}."
                            : $"ADM1 boundary import failed for {setupStatus.CountryIso3}: {firstFailure}");
                }
            }

            return Ok(await _regionSync.SyncRegionsFromAdm1CandidatesForTerritoryManagerAsync(
                userId,
                setupStatus.TerritoryId,
                dryRun,
                force || forceUpdateGeometry,
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
