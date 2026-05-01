using Geotrack.Application.Interfaces.Service;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Geotrack.Server.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class MapsController : ControllerBase
    {
        private readonly IMapService _mapService;

        public MapsController(IMapService mapService)
        {
            _mapService = mapService;
        }

        [HttpGet("search")]
        public async Task<IActionResult> Search(
            [FromQuery] string? query,
            [FromQuery] string? types)
        {
            var typeList = types?.Split(',', StringSplitOptions.RemoveEmptyEntries);
            var results = await _mapService.SearchAsync(query ?? string.Empty, typeList);
            return Ok(results);
        }
    }
}
