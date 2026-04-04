using System.Security.Claims;
using Geotrack.Application.Services;
using Geotrack.Server.Requests;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Geotrack.Server.Controllers
{
    
    [ApiController]
    [Route("api/destinations")]
    public class DestinationController : ControllerBase
    {
        private readonly DestinationService destService;


        public DestinationController(DestinationService ds)
        { 
            this.destService = ds;
        }

        [HttpPost("explore-by-location")]

        public async Task<IActionResult> ExploreByLocation([FromBody] ExploreByLocationRequest request)
        {
            if (request == null)
            {
                return BadRequest("Request can't be null");
            }

            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            int? userId = userIdClaim != null ? int.Parse(userIdClaim):null;

            var destinations = await destService.ExploreByLocationAsync(
                request.Latitude,
                request.Longitude,
                request.Radius,
                userId
                
             );

            return Ok(destinations);
        }
    }
}
