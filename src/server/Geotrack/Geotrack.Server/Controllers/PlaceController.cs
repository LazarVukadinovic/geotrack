using System.Security.Claims;
using Geotrack.Application.DTOs.Place;
using Geotrack.Application.Interfaces.Service;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Geotrack.Server.Controllers
{
    [Route("api/places")]
    [ApiController]
    public class PlaceController : ControllerBase
    {
        private readonly IPlaceService _placeService;

        public PlaceController(IPlaceService placeService)
        {
            _placeService = placeService;
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> GetPlaceDetails(int id)
        {
           
            int? userId = null;
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (userIdClaim != null)
                userId = int.Parse(userIdClaim);

            var result = await _placeService.GetPlaceDetailsAsync(id, userId);
            return Ok(result);
        }

        [HttpGet("{id}/events")]
        public async Task<ActionResult<GetPlaceEventsResponseDto>> GetPlaceEvents(int id)
        {
            var response = await _placeService.GetPlaceEventsAsync(id);

            if (response == null)
            {
                return NotFound();
            }

            return Ok(response);
        }

        [HttpGet("{id}/reviews")]
        public async Task<ActionResult<GetPlaceReviewsresponseDto>> GetPlaceReviews(int id)
        {
            var response = await _placeService.GetPlaceReviewsAsync(id);
            return Ok(response);
        }

        [HttpPost("{id}/reviews")]
        [Authorize]
        public async Task<IActionResult> CreatePlaceReview(int id, [FromBody] CreatePlaceReviewRequestDto dto)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (userIdClaim == null)
                return Unauthorized();

            var userId = int.Parse(userIdClaim);

            var result = await _placeService.CreateReviewAsync(id, userId, dto);
            return CreatedAtAction(nameof(GetPlaceDetails), new { id = result.PlaceId }, result);
        }
    }
}
