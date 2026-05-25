using Geotrack.Application.DTOs.Admin;
using Geotrack.Application.Interfaces.Service;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Geotrack.Server.Controllers;

[ApiController]
[Route("api/admin/territories")]
[Authorize(Policy = "RequireAdmin")]
public sealed class AdminTerritoriesController : ControllerBase
{
    private readonly IAdminTerritoryManagementService _territories;
    private readonly ILogger<AdminTerritoriesController> _logger;

    public AdminTerritoriesController(
        IAdminTerritoryManagementService territories,
        ILogger<AdminTerritoriesController> logger)
    {
        _territories = territories;
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
