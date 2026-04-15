using Geotrack.Application.DTOs.Locations;
using Geotrack.Application.Interfaces.Service;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Geotrack.Server.Controllers;

[ApiController]
[Route("api/locations")]
public class LocationController : ControllerBase
{
    private readonly ILocationService _locationService;

    public LocationController(ILocationService locationService)
    {
        _locationService = locationService;
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetLocationDetails([FromRoute] int id)
    {
        try
        {
            var result = await _locationService.GetLocationDetailsAsync(id, TryGetUserId());
            return Ok(result);
        }
        catch (Exception ex)
        {
            return MapException(ex);
        }
    }

    [HttpGet("{id}/sections/{sectionKey}")]
    public async Task<IActionResult> GetLocationSection([FromRoute] int id, [FromRoute] string sectionKey)
    {
        try
        {
            var result = await _locationService.GetLocationSectionAsync(id, sectionKey);
            return Ok(result);
        }
        catch (Exception ex)
        {
            return MapException(ex);
        }
    }

    [HttpGet("{id}/reviews")]
    public async Task<IActionResult> GetLocationReviews([FromRoute] int id)
    {
        try
        {
            var result = await _locationService.GetLocationReviewsAsync(id);
            return Ok(result);
        }
        catch (Exception ex)
        {
            return MapException(ex);
        }
    }

    [HttpPost("{id}/reviews")]
    [Authorize]
    public async Task<IActionResult> CreateLocationReview([FromRoute] int id, [FromBody] CreateLocationReviewRequestDto request)
    {
        try
        {
            var userId = TryGetUserId();
            if (!userId.HasValue)
                return Unauthorized();

            var result = await _locationService.CreateLocationReviewAsync(id, userId.Value, request);
            return CreatedAtAction(nameof(GetLocationReviews), new { id }, result);
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