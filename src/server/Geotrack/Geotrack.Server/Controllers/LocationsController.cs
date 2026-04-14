using System.Security.Claims;
using Geotrack.Application.Interfaces.Service;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Geotrack.Server.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class LocationsController : ControllerBase
    {
        private readonly ILocationService _locationService;

        public LocationsController(ILocationService locationService)
        {
            _locationService = locationService;
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> GetLocationDetails([FromRoute] int id)
        {
            int? userId = null;
            var userIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (int.TryParse(userIdClaim, out var parsedUserId))
                userId = parsedUserId;

            var result = await _locationService.GetLocationDetailsAsync(id, userId);
            return Ok(result);
        }

        [HttpGet("{id}/sections/{sectionKey}")]
        public async Task<IActionResult> GetLocationSection(int id, string sectionKey)
        {
            var result = await _locationService.GetLocationSectionAsync(id, sectionKey);
            return Ok(result);
        }
    }
}
