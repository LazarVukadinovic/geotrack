using Geotrack.Application.DTOs.Territories;
using Geotrack.Application.Interfaces.Service;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Geotrack.Server.Controllers;

[Route("api/[controller]")]
[ApiController]
public class TerritoryController : ControllerBase
{
    private const string UnexpectedErrorMessage = "An unexpected error occurred.";

    private readonly ITerritoryService _territoryService;
    private readonly ILogger<TerritoryController> _logger;

    public TerritoryController(
        ITerritoryService territoryService,
        ILogger<TerritoryController> logger)
    {
        _territoryService = territoryService;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> GetTerritories()
    {
        try
        {
            var result = await _territoryService.GetTerritoriesAsync();
            return Ok(result);
        }
        catch (Exception ex)
        {
            return MapException(ex);
        }
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetTerritoryDetails([FromRoute] int id)
    {
        try
        {
            var result = await _territoryService.GetTerritoryDetailsAsync(id);
            return Ok(result);
        }
        catch (Exception ex)
        {
            return MapException(ex);
        }
    }

    [HttpPost]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> CreateTerritory([FromBody] CreateTerritoryRequestDto request)
    {
        try
        {
            var result = await _territoryService.CreateTerritoryAsync(request);
            return Ok(result);
        }
        catch (Exception ex)
        {
            return MapException(ex);
        }
    }

    [HttpDelete("{id}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> DeleteTerritory([FromRoute] int id)
    {
        try
        {
            await _territoryService.DeleteTerritoryAsync(id);
            return NoContent();
        }
        catch (Exception ex)
        {
            return MapException(ex);
        }
    }

    [HttpPost("{territoryId}/regions")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> CreateRegion(
        [FromRoute] int territoryId,
        [FromBody] CreateRegionInTerritoryRequestDto request)
    {
        try
        {
            var result = await _territoryService.CreateRegionAsync(territoryId, request);
            return Ok(result);
        }
        catch (Exception ex)
        {
            return MapException(ex);
        }
    }

    [HttpDelete("{territoryId}/regions/{regionId}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> DeleteRegion(
        [FromRoute] int territoryId,
        [FromRoute] int regionId)
    {
        try
        {
            await _territoryService.DeleteRegionAsync(territoryId, regionId);
            return NoContent();
        }
        catch (Exception ex)
        {
            return MapException(ex);
        }
    }

    private IActionResult MapException(Exception ex)
    {
        return ex switch
        {
            KeyNotFoundException => NotFound(new { message = ex.Message }),
            InvalidOperationException => BadRequest(new { message = ex.Message }),
            _ => HandleUnexpectedException(ex)
        };
    }

    private IActionResult HandleUnexpectedException(Exception ex)
    {
        var httpContext = ControllerContext.HttpContext;
        _logger.LogError(
            ex,
            "Unexpected exception while handling territory request {Method} {Path}.",
            httpContext?.Request.Method ?? "unknown",
            httpContext?.Request.Path.Value ?? "unknown");

        return StatusCode(StatusCodes.Status500InternalServerError, new { message = UnexpectedErrorMessage });
    }
}
