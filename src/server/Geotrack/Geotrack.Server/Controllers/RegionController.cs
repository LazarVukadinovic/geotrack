using Geotrack.Application.DTOs.Region;
using Geotrack.Application.Interfaces.Service;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Geotrack.Server.Controllers
{
    [Route("api/regions")]
    [ApiController]
    public class RegionController : ControllerBase
    {
        private readonly IRegionService _regionService;

        public RegionController(IRegionService regionService)
        {
            _regionService = regionService;
        }

        [HttpGet]
        public async Task<IActionResult> GetRegions()
        {
            try
            {
                var result = await _regionService.GetRegionsAsync();
                return Ok(result);
            }
            catch (Exception ex)
            {
                return MapUnexpected(ex);
            }
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> GetRegionDetails([FromRoute] int id)
        {
            try
            {
                var result = await _regionService.GetRegionDetailsAsync(id);
                return Ok(result);
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                return MapUnexpected(ex);
            }
        }

        [HttpPost]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> CreateRegion([FromBody] CreateRegionRequestDto request)
        {
            try
            {
                var result = await _regionService.CreateRegionAsync(request);
                return Ok(result);
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                return MapUnexpected(ex);
            }
        }

        [HttpDelete("{id}")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> DeleteRegion([FromRoute] int id)
        {
            try
            {
                await _regionService.DeleteRegionAsync(id);
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
                return MapUnexpected(ex);
            }
        }

        private IActionResult MapUnexpected(Exception _)
        {
            return StatusCode(500, new { message = "An unexpected error occurred." });
        }
    }
}
