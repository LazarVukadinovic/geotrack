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

            var destinations = await destService.ExploreByLocationAsync(
                request.Lat,
                request.Long,
                request.radius
             );

            return Ok(destinations);
        }
    }
}
