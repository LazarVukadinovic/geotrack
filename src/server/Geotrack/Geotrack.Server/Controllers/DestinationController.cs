using Geotrack.Application.DTOs.Destinations;
using Geotrack.Application.DTOs.Explore;
using Geotrack.Application.Interfaces.Service;
using Microsoft.AspNetCore.Authorization;
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
    public async Task<IActionResult> ExploreByLocation([FromBody] ExploreByLocationRequestDto request)
    {
        var query = new ExploreByLocationQueryDto
        {
            Latitude = request.Latitude,
            Longitude = request.Longitude,
            UserId = TryGetUserId()
        };

        var result = await _destinationService.ExploreByLocationAsync(query);
        return Ok(result);
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetDestinationDetails([FromRoute] int id)
    {
        try
        {
            var result = await _destinationService.GetDestinationDetailsAsync(id, TryGetUserId());
            return Ok(result);
        }
        catch (Exception ex)
        {
            return MapException(ex);
        }
    }

    [HttpGet("{id}/sections/{sectionKey}")]
    public async Task<IActionResult> GetDestinationSection([FromRoute] int id, [FromRoute] string sectionKey)
    {
        try
        {
            var result = await _destinationService.GetDestinationSectionAsync(id, sectionKey);
            return Ok(result);
        }
        catch (Exception ex)
        {
            return MapException(ex);
        }
    }

    [HttpGet("{id}/reviews")]
    public async Task<IActionResult> GetDestinationReviews([FromRoute] int id)
    {
        try
        {
            var result = await _destinationService.GetDestinationReviewsAsync(id);
            return Ok(result);
        }
        catch (Exception ex)
        {
            return MapException(ex);
        }
    }

    [HttpPost("{id}/reviews")]
    [Authorize]
    public async Task<IActionResult> CreateDestinationReview([FromRoute] int id, [FromBody] CreateDestinationReviewRequestDto request)
    {
        try
        {
            var userId = TryGetUserId();
            if (!userId.HasValue)
                return Unauthorized();

            var result = await _destinationService.CreateDestinationReviewAsync(id, userId.Value, request);
            return CreatedAtAction(nameof(GetDestinationReviews), new { id }, result);
        }
        catch (Exception ex)
        {
            return MapException(ex);
        }
    }

    private int? TryGetUserId()
    {
        var userIdClaim = User.FindFirst("userId")?.Value;
        return int.TryParse(userIdClaim, out var userId) ? userId : null;
    }

    private IActionResult MapException(Exception ex)
    {
        return ex switch
        {
            KeyNotFoundException => NotFound(new { message = ex.Message }),
            InvalidOperationException => BadRequest(new { message = ex.Message }),
            _ => StatusCode(500, new { message = ex.Message })
        };
    }
}
