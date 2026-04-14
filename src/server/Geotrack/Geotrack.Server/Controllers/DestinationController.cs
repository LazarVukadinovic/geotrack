using Geotrack.Application.DTOs.Explore;
using Geotrack.Application.Interfaces.Service;
using Microsoft.AspNetCore.Mvc;

namespace Geotrack.Server.Controllers;

[ApiController]
[Route("api/destinations")]
public class DestinationController : ControllerBase
{
    private readonly IDestinationService _destinationService;

    public DestinationController(IDestinationService destinationService)
    {
        _destinationService = destinationService;
    }

    [HttpPost("explore-by-location")]
    public async Task<ActionResult<ExploreByLocationResponseDto>> ExploreByLocation(
        [FromBody] ExploreByLocationRequestDto request)
    {
        int? userId = null;

        var userIdClaim = User.FindFirst("userId")?.Value;
        if (int.TryParse(userIdClaim, out var parsedUserId))
        {
            userId = parsedUserId;
        }

        var query = new ExploreByLocationQueryDto
        {
            Latitude = request.Latitude,
            Longitude = request.Longitude,
            UserId = userId
        };

        var result = await _destinationService.ExploreByLocationAsync(query);
        return Ok(result);
    }
}