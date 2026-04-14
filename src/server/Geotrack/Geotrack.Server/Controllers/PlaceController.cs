using System.Security.Claims;
using Geotrack.Application.Interfaces.Service;
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
    }
}
