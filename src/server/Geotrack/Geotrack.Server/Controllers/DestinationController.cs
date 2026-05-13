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
    private const string UnexpectedErrorMessage = "An unexpected error occurred.";

    private readonly IDestinationService _destinationService;
    private readonly IExploreService _exploreService;
    private readonly ILogger<DestinationController> _logger;

    public DestinationController(
        IDestinationService destinationService,
        IExploreService exploreService,
        ILogger<DestinationController> logger)
    {
        _destinationService = destinationService;
        _exploreService = exploreService;
        _logger = logger;
    }

    [Obsolete("Use GET /api/explore?latitude={latitude}&longitude={longitude}. This compatibility endpoint is backed by the generic Explore feed.")]
    [HttpPost("explore-by-location")]
    public async Task<IActionResult> ExploreByLocation([FromBody] ExploreByLocationRequestDto request)
    {
        if (!HasValidCoordinates(request.Latitude, request.Longitude))
        {
            return BadRequest(new { error = "latitude and longitude must be finite values in valid coordinate ranges." });
        }

        Response.Headers["Deprecation"] = "true";
        Response.Headers["Link"] = "</api/explore>; rel=\"successor-version\"";

        try
        {
            var result = await _exploreService.GetExplorePageAsync(
                request.Latitude,
                request.Longitude,
                TryGetUserId());

            return Ok(MapLegacyExploreResponse(result));
        }
        catch (Exception ex)
        {
            return MapException(ex);
        }
    }

    [HttpGet("search")]
    public async Task<IActionResult> Search([FromQuery] string? query)
    {
        var results = await _destinationService.SearchAsync(query ?? string.Empty);
        return Ok(results);
    }

    [HttpGet("{destinationId}/search")]
    public async Task<IActionResult> SearchWithinDestination(
        [FromRoute] int destinationId,
        [FromQuery] string? query,
        [FromQuery] string? types)
    {
        try
        {
            var typeList = types?.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            var results = await _destinationService.SearchWithinDestinationAsync(
                destinationId,
                query ?? string.Empty,
                typeList);

            return Ok(results);
        }
        catch (Exception ex)
        {
            return MapException(ex);
        }
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
    public async Task<IActionResult> GetDestinationReviews(
        [FromRoute] int id,
        [FromQuery] int skip = 0,
        [FromQuery] int take = 10)
    {
        var pagingError = ValidatePaging(skip, take);
        if (pagingError is not null)
        {
            return pagingError;
        }

        try
        {
            var result = await _destinationService.GetDestinationReviewsAsync(id, skip, take);
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

    private static ExploreByLocationResponseDto MapLegacyExploreResponse(ExplorePageDto page)
    {
        return new ExploreByLocationResponseDto
        {
            SelectedDestination = page.SelectedDestination,
            Sections = page.Sections
                .Where(section => section.Items.Count > 0)
                .Select(section => new ExploreSectionDto
                {
                    Key = section.Key,
                    Title = section.Title,
                    Items = section.Items.Select(item => new ExplorePreviewItemDto
                    {
                        EntityType = item.EntityType,
                        EntityId = item.Id,
                        Name = item.Title,
                        Description = item.Description
                            ?? item.Subtitle
                            ?? item.CategoryLabel
                            ?? item.DateLabel
                            ?? item.Distance
                            ?? string.Empty,
                        AverageRating = item.Rating ?? 0,
                        IsSaved = item.IsSaved
                    }).ToList()
                })
                .ToList()
        };
    }

    private ActionResult? ValidatePaging(int skip, int take)
    {
        if (skip < 0)
        {
            return BadRequest(new { error = "skip must be zero or greater." });
        }

        if (take <= 0)
        {
            return BadRequest(new { error = "take must be greater than zero." });
        }

        return null;
    }

    private static bool HasValidCoordinates(double latitude, double longitude)
    {
        return !double.IsNaN(latitude) &&
               !double.IsNaN(longitude) &&
               !double.IsInfinity(latitude) &&
               !double.IsInfinity(longitude) &&
               latitude >= -90 &&
               latitude <= 90 &&
               longitude >= -180 &&
               longitude <= 180 &&
               (latitude != 0d || longitude != 0d);
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
