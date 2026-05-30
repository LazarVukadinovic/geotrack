using Geotrack.Application.DTOs.Admin;
using Geotrack.Application.Interfaces.Service;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;

namespace Geotrack.Server.Controllers;

[ApiController]
[Route("api/admin/territories")]
[Authorize(Policy = "RequireAdmin")]
public sealed class AdminTerritoriesController : ControllerBase
{
    private readonly IAdminTerritoryManagementService _territories;
    private readonly ILocalBoundaryImportService _localBoundaryImport;
    private readonly IAdmBoundaryRegionSyncService _regionSync;
    private readonly IRegionContentAssignmentService _contentAssignment;
    private readonly ILogger<AdminTerritoriesController> _logger;

    public AdminTerritoriesController(
        IAdminTerritoryManagementService territories,
        ILocalBoundaryImportService localBoundaryImport,
        IAdmBoundaryRegionSyncService regionSync,
        IRegionContentAssignmentService contentAssignment,
        ILogger<AdminTerritoriesController> logger)
    {
        _territories = territories;
        _localBoundaryImport = localBoundaryImport;
        _regionSync = regionSync;
        _contentAssignment = contentAssignment;
        _logger = logger;
    }

    [HttpGet]
    public async Task<ActionResult<AdminTerritoryPageResponseDto>> GetTerritories(
        [FromQuery] AdminTerritoryPageRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _territories.GetTerritoriesAsync(request, cancellationToken));
        }
        catch (Exception ex)
        {
            return HandleException(ex);
        }
    }

    [HttpGet("map")]
    public async Task<ActionResult<AdminTerritoryMapFeatureCollectionDto>> GetTerritoryMap(
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await _territories.GetTerritoryMapAsync(cancellationToken);
            ApplyMapCacheHeaders(result);

            if (!string.IsNullOrWhiteSpace(result.ETag) &&
                Request.Headers.IfNoneMatch.Any(value =>
                    !string.IsNullOrWhiteSpace(value) &&
                    value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                        .Any(tag => string.Equals(tag, result.ETag, StringComparison.Ordinal))))
            {
                return StatusCode(StatusCodes.Status304NotModified);
            }

            return Ok(result);
        }
        catch (Exception ex)
        {
            return HandleException(ex);
        }
    }

    [HttpGet("map/boundaries")]
    public async Task<ActionResult<AdminTerritoryMapBoundaryFeatureCollectionDto>> GetTerritoryMapBoundaries(
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await _territories.GetTerritoryMapBoundariesAsync(cancellationToken);
            ApplyBoundaryMapCacheHeaders(result);

            if (!string.IsNullOrWhiteSpace(result.ETag) &&
                Request.Headers.IfNoneMatch.Any(value =>
                    !string.IsNullOrWhiteSpace(value) &&
                    value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                        .Any(tag => string.Equals(tag, result.ETag, StringComparison.Ordinal))))
            {
                return StatusCode(StatusCodes.Status304NotModified);
            }

            return Ok(result);
        }
        catch (Exception ex)
        {
            return HandleException(ex);
        }
    }

    [HttpGet("map/status")]
    public async Task<ActionResult<AdminTerritoryMapStatusCollectionDto>> GetTerritoryMapStatus(
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await _territories.GetTerritoryMapStatusAsync(cancellationToken);
            ApplyStatusCacheHeaders(result);
            return Ok(result);
        }
        catch (Exception ex)
        {
            return HandleException(ex);
        }
    }

    [HttpGet("{territoryId:int}")]
    public async Task<ActionResult<AdminTerritoryDetailDto>> GetTerritoryDetail(
        [FromRoute] int territoryId,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _territories.GetTerritoryDetailAsync(territoryId, cancellationToken));
        }
        catch (Exception ex)
        {
            return HandleException(ex);
        }
    }

    [HttpGet("managers/search")]
    public async Task<ActionResult<AdminTerritoryManagerSearchResponseDto>> SearchManagers(
        [FromQuery] string? query,
        [FromQuery] int take = 20,
        [FromQuery] int? territoryId = null,
        CancellationToken cancellationToken = default)
    {
        if (territoryId is <= 0)
            return BadRequest(new { message = "territoryId must be positive." });

        try
        {
            return Ok(await _territories.SearchManagerCandidatesAsync(query, take, territoryId, cancellationToken));
        }
        catch (Exception ex)
        {
            return HandleException(ex);
        }
    }

    [HttpGet("{territoryId:int}/assignable-managers")]
    public async Task<ActionResult<AdminTerritoryManagerSearchResponseDto>> GetAssignableManagers(
        [FromRoute] int territoryId,
        [FromQuery] string? query,
        [FromQuery] int take = 20,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return Ok(await _territories.GetAssignableManagersAsync(territoryId, query, take, cancellationToken));
        }
        catch (Exception ex)
        {
            return HandleException(ex);
        }
    }

    [HttpPost("import-boundary")]
    public async Task<ActionResult<AdminTerritoryDetailDto>> ImportBoundaryByIso3(
        [FromBody] AdminImportTerritoryBoundaryRequestDto? request,
        CancellationToken cancellationToken)
    {
        if (request is null)
            return BadRequest(new { message = "Request body is required." });

        try
        {
            return Ok(await _territories.ImportBoundaryAsync(request, cancellationToken));
        }
        catch (Exception ex)
        {
            return HandleException(ex);
        }
    }

    [HttpPost("{territoryId:int}/import-boundary")]
    public async Task<ActionResult<AdminTerritoryDetailDto>> ImportBoundary(
        [FromRoute] int territoryId,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _territories.ImportBoundaryAsync(territoryId, cancellationToken));
        }
        catch (Exception ex)
        {
            return HandleException(ex);
        }
    }

    [HttpPost("import-boundaries/missing")]
    [HttpPost("boundaries/import-missing")]
    public async Task<ActionResult<GeoBoundariesBatchImportResultDto>> ImportMissingBoundaries(
        [FromBody] AdminImportTerritoryBoundariesRequestDto? request,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _territories.ImportMissingBoundariesAsync(
                request ?? new AdminImportTerritoryBoundariesRequestDto(),
                cancellationToken));
        }
        catch (Exception ex)
        {
            return HandleException(ex);
        }
    }

    [HttpPost("import-boundaries/all")]
    [HttpPost("boundaries/import-all")]
    public async Task<ActionResult<GeoBoundariesBatchImportResultDto>> ImportAllBoundaries(
        [FromBody] AdminImportTerritoryBoundariesRequestDto? request,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _territories.ImportAllBoundariesAsync(
                request ?? new AdminImportTerritoryBoundariesRequestDto(),
                cancellationToken));
        }
        catch (Exception ex)
        {
            return HandleException(ex);
        }
    }

    [HttpPost("boundaries/import-local-adm0")]
    public async Task<ActionResult<BoundaryImportSummaryDto>> ImportLocalAdm0Boundary(
        [FromQuery] bool force = false,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return Ok(await _localBoundaryImport.ImportLocalBoundariesAsync(
                "ADM0",
                force,
                cancellationToken: cancellationToken));
        }
        catch (Exception ex)
        {
            return HandleException(ex);
        }
    }

    [HttpPost("boundaries/import-local-adm1")]
    public async Task<ActionResult<BoundaryImportSummaryDto>> ImportLocalAdm1Boundaries(
        [FromQuery] bool force = false,
        [FromQuery] string? countryIso3 = null,
        [FromQuery] int? take = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return Ok(await _localBoundaryImport.ImportLocalBoundariesAsync(
                "ADM1",
                force,
                countryIso3,
                take,
                cancellationToken));
        }
        catch (Exception ex)
        {
            return HandleException(ex);
        }
    }

    [HttpPost("boundaries/import-local-adm2")]
    public async Task<ActionResult<BoundaryImportSummaryDto>> ImportLocalAdm2Boundaries(
        [FromQuery] bool force = false,
        [FromQuery] string? countryIso3 = null,
        [FromQuery] int? take = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return Ok(await _localBoundaryImport.ImportLocalBoundariesAsync(
                "ADM2",
                force,
                countryIso3,
                take,
                cancellationToken));
        }
        catch (Exception ex)
        {
            return HandleException(ex);
        }
    }

    [HttpPost("{territoryId:int}/regions/sync-from-adm1")]
    public async Task<ActionResult<RegionSyncSummaryDto>> SyncRegionsFromAdm1(
        [FromRoute] int territoryId,
        [FromQuery] bool dryRun = true,
        [FromQuery] bool forceUpdateGeometry = false,
        CancellationToken cancellationToken = default)
    {
        if (territoryId <= 0)
            return BadRequest(new { message = "territoryId must be positive." });

        try
        {
            return Ok(await _regionSync.SyncRegionsFromAdm1CandidatesAsync(
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

    [HttpPost("{territoryId:int}/regions/assign-content-by-boundary")]
    public async Task<ActionResult<RegionContentAssignmentSummaryDto>> AssignContentByBoundary(
        [FromRoute] int territoryId,
        [FromQuery] bool dryRun = true,
        CancellationToken cancellationToken = default)
    {
        if (territoryId <= 0)
            return BadRequest(new { message = "territoryId must be positive." });

        try
        {
            return Ok(await _contentAssignment.AssignContentByBoundaryAsync(
                territoryId,
                dryRun,
                cancellationToken));
        }
        catch (Exception ex)
        {
            return HandleException(ex);
        }
    }

    [HttpPost("boundaries/import-local")]
    public async Task<ActionResult<BoundaryImportSummaryDto>> ImportLocalBoundaries(
        [FromQuery] string? level,
        [FromQuery] bool force = false,
        [FromQuery] string? countryIso3 = null,
        [FromQuery] int? take = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(level))
            return BadRequest(new { message = "level query parameter is required." });

        try
        {
            return Ok(await _localBoundaryImport.ImportLocalBoundariesAsync(
                level,
                force,
                countryIso3,
                take,
                cancellationToken));
        }
        catch (Exception ex)
        {
            return HandleException(ex);
        }
    }

    [HttpGet("/api/admin/territory/regions/boundary-candidates/diagnostics")]
    public async Task<ActionResult<BoundaryCandidateDiagnosticsDto>> GetBoundaryCandidateDiagnostics(
        [FromQuery] string? countryIso3 = null,
        [FromQuery] string? level = "ADM1",
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(countryIso3))
            return BadRequest(new { message = "countryIso3 query parameter is required." });

        try
        {
            return Ok(await _localBoundaryImport.GetRegionCandidateDiagnosticsAsync(
                countryIso3,
                level ?? "ADM1",
                cancellationToken));
        }
        catch (Exception ex)
        {
            return HandleException(ex);
        }
    }

    [HttpPost("boundaries/rebuild-display-geometry")]
    public async Task<ActionResult<BoundaryDisplayGeometryRebuildResultDto>> RebuildDisplayGeometry(
        [FromQuery] bool onlyMissing = true,
        [FromQuery] string? iso3 = null,
        [FromQuery] bool force = false,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return Ok(await _territories.RebuildDisplayGeometryAsync(onlyMissing, iso3, force, cancellationToken));
        }
        catch (Exception ex)
        {
            return HandleException(ex);
        }
    }

    [HttpGet("boundaries/debug/{iso3}")]
    public async Task<ActionResult<BoundaryGeometryDebugDto>> GetBoundaryDebug(
        [FromRoute] string iso3,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return Ok(await _territories.GetBoundaryDebugAsync(iso3, cancellationToken));
        }
        catch (Exception ex)
        {
            return HandleException(ex);
        }
    }

    [HttpGet("boundaries/reconciliation")]
    public async Task<ActionResult<BoundaryReconciliationReportDto>> GetBoundaryReconciliation(
        CancellationToken cancellationToken = default)
    {
        try
        {
            return Ok(await _localBoundaryImport.GetAdm0ReconciliationAsync(cancellationToken));
        }
        catch (Exception ex)
        {
            return HandleException(ex);
        }
    }

    [HttpPost]
    public async Task<ActionResult<AdminTerritoryDto>> CreateTerritory(
        [FromBody] AdminCreateTerritoryRequestDto? request,
        CancellationToken cancellationToken)
    {
        if (request is null)
            return BadRequest(new { message = "Request body is required." });

        try
        {
            var created = await _territories.CreateTerritoryAsync(request, cancellationToken);
            return CreatedAtAction(nameof(GetTerritories), new { search = created.Name }, created);
        }
        catch (Exception ex)
        {
            return HandleException(ex);
        }
    }

    [HttpPut("{territoryId:int}/manager")]
    public async Task<ActionResult<AdminTerritoryDetailDto>> AssignManager(
        [FromRoute] int territoryId,
        [FromBody] AdminAssignTerritoryManagerRequestDto? request,
        CancellationToken cancellationToken)
    {
        if (request is null)
            return BadRequest(new { message = "Request body is required." });

        try
        {
            return Ok(await _territories.AssignManagerAsync(territoryId, request, cancellationToken));
        }
        catch (Exception ex)
        {
            return HandleException(ex);
        }
    }

    [HttpDelete("{territoryId:int}")]
    public async Task<IActionResult> DeleteTerritory(
        [FromRoute] int territoryId,
        CancellationToken cancellationToken)
    {
        try
        {
            await _territories.DeleteTerritoryAsync(territoryId, cancellationToken);
            return NoContent();
        }
        catch (Exception ex)
        {
            return HandleException(ex);
        }
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

    private void ApplyMapCacheHeaders(AdminTerritoryMapFeatureCollectionDto result)
    {
        if (!string.IsNullOrWhiteSpace(result.ETag))
        {
            Response.Headers[HeaderNames.ETag] = result.ETag;
        }

        if (result.LastModified.HasValue)
        {
            Response.Headers[HeaderNames.LastModified] = result.LastModified.Value.ToString("R");
        }

        Response.Headers[HeaderNames.CacheControl] = "private, max-age=60";
        Response.Headers[HeaderNames.Vary] = "Authorization, Accept-Encoding";
    }

    private void ApplyBoundaryMapCacheHeaders(AdminTerritoryMapBoundaryFeatureCollectionDto result)
    {
        if (!string.IsNullOrWhiteSpace(result.ETag))
        {
            Response.Headers[HeaderNames.ETag] = result.ETag;
        }

        if (result.LastModified.HasValue)
        {
            Response.Headers[HeaderNames.LastModified] = result.LastModified.Value.ToString("R");
        }

        Response.Headers[HeaderNames.CacheControl] = "private, max-age=86400";
        Response.Headers[HeaderNames.Vary] = "Authorization, Accept-Encoding";
    }

    private void ApplyStatusCacheHeaders(AdminTerritoryMapStatusCollectionDto result)
    {
        if (!string.IsNullOrWhiteSpace(result.ETag))
        {
            Response.Headers[HeaderNames.ETag] = result.ETag;
        }

        if (result.LastModified.HasValue)
        {
            Response.Headers[HeaderNames.LastModified] = result.LastModified.Value.ToString("R");
        }

        Response.Headers[HeaderNames.CacheControl] = "no-store";
        Response.Headers[HeaderNames.Vary] = "Authorization, Accept-Encoding";
    }

    private ActionResult HandleUnexpectedException(Exception ex)
    {
        _logger.LogError(
            ex,
            "Unexpected exception while handling administrator territory request {Method} {Path}.",
            HttpContext.Request.Method,
            HttpContext.Request.Path.Value);

        return StatusCode(StatusCodes.Status500InternalServerError, new { message = "An unexpected error occurred." });
    }
}
