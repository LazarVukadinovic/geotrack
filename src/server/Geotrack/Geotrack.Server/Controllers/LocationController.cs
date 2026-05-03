using Geotrack.Application.DTOs.Locations;
using Geotrack.Application.Interfaces.Service;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Geotrack.Server.Controllers;

[ApiController]
[Route("api/locations")]
public class LocationController : ControllerBase
{
    private const string UnexpectedErrorMessage = "An unexpected error occurred.";

    private readonly ILocationService _locationService;
    private readonly IClientLocationResolverService _clientLocationResolver;
    private readonly ILogger<LocationController> _logger;

    public LocationController(
        ILocationService locationService,
        IClientLocationResolverService clientLocationResolver,
        ILogger<LocationController> logger)
    {
        _locationService = locationService;
        _clientLocationResolver = clientLocationResolver;
        _logger = logger;
    }

    [HttpGet("approximate")]
    [EnableRateLimiting("approximateLocation")]
    public async Task<IActionResult> GetApproximateLocation(CancellationToken cancellationToken)
    {
        var location = await _clientLocationResolver.ResolveApproximateLocationAsync(
            GetClientIpAddress(),
            cancellationToken);

        if (location is null)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { message = "Unable to determine approximate location from IP." });
        }

        return Ok(location);
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

    private string? GetClientIpAddress()
    {
        return HttpContext.Connection.RemoteIpAddress?.ToString();
    }

    private IActionResult MapException(Exception ex)
    {
        return ex switch
        {
            KeyNotFoundException => NotFound(new { message = ex.Message }),
            InvalidOperationException => BadRequest(new { message = ex.Message }),
            _ => HandleUnexpectedException(ex)
        };
    }

    private IActionResult HandleUnexpectedException(Exception ex)
    {
        var httpContext = ControllerContext.HttpContext;
        _logger.LogError(
            ex,
            "Unexpected exception while handling {Method} {Path}.",
            httpContext?.Request.Method ?? "unknown",
            httpContext?.Request.Path.Value ?? "unknown");

        return StatusCode(StatusCodes.Status500InternalServerError, new { message = UnexpectedErrorMessage });
    }
}
