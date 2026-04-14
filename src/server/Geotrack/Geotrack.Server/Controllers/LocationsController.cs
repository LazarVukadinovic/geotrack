using System.Security.Claims;
using Geotrack.Application.DTOs.Location;
using Geotrack.Application.Interfaces.Service;
using Microsoft.AspNetCore.Authorization;
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

        [HttpGet("{id}/reviews")]
        public async Task<IActionResult> GetLocationReviews(int id)
        {
            var result = await _locationService.GetLocationReviewsAsync(id);
            return Ok(result);
        }

        [HttpPost("{id}/reviews")]
        [Authorize]
        public async Task<IActionResult> CreateLocationReview(int id, [FromBody] CreateLocationReviewRequestDto dto)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (userIdClaim == null)
                return Unauthorized();

            var userId = int.Parse(userIdClaim);

            var result = await _locationService.CreateLocationReviewAsync(id, userId, dto);
            return CreatedAtAction(nameof(GetLocationReviews), new { id = result.LocationId }, result);
        }
    }
}
