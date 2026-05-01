using Geotrack.Application.DTOs.Territories;
using Geotrack.Application.Interfaces.Service;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Geotrack.Server.Controllers;

[Route("api/[controller]")]
[ApiController]
public class TerritoryController : ControllerBase
{
    private readonly ITerritoryService _territoryService;

    public TerritoryController(ITerritoryService territoryService)
    {
        _territoryService = territoryService;
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
            return BadRequest(new { message = ex.Message });
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
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = ex.Message });
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
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = ex.Message });
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
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = ex.Message });
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
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = ex.Message });
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
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }
}
