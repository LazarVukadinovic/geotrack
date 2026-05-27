using Geotrack.Application.DTOs.Admin;
using Geotrack.Application.Interfaces.Service;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Geotrack.Server.Controllers;

[ApiController]
[Route("api/admin/regions")]
[Authorize(Policy = "RequireAdmin")]
public sealed class AdminRegionsController : ControllerBase
{
    private readonly IRegionBoundaryBackfillService _backfill;
    private readonly ILogger<AdminRegionsController> _logger;

    public AdminRegionsController(
        IRegionBoundaryBackfillService backfill,
        ILogger<AdminRegionsController> logger)
    {
        _backfill = backfill;
        _logger = logger;
    }

    [HttpPost("backfill-boundary-candidates")]
    public async Task<ActionResult<RegionBoundaryBackfillSummaryDto>> BackfillBoundaryCandidates(
        [FromQuery] string? countryIso3 = null,
        [FromQuery] bool dryRun = true,
        [FromQuery] bool force = false,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return Ok(await _backfill.BackfillBoundaryCandidatesAsync(
                countryIso3,
                dryRun,
                force,
                cancellationToken));
        }
        catch (UnauthorizedAccessException)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { message = "Forbidden." });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected region boundary backfill failure.");
            return StatusCode(
                StatusCodes.Status500InternalServerError,
                new { message = "An unexpected error occurred." });
        }
    }
}
