using Geotrack.Application.DTOs.Locations;
using Geotrack.Application.DTOs.Reviews;
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
    private readonly IReviewService _reviewService;
    private readonly ILogger<LocationController> _logger;

    public LocationController(
        ILocationService locationService,
        IClientLocationResolverService clientLocationResolver,
        IReviewService reviewService,
        ILogger<LocationController> logger)
    {
        _locationService = locationService;
        _clientLocationResolver = clientLocationResolver;
        _reviewService = reviewService;
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
    public async Task<IActionResult> GetLocationReviews(
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
            var result = await _reviewService.GetLocationReviewsAsync(id, skip, take, TryGetUserId());
            return Ok(MapLegacyLocationReviewsResponse(id, result));
        }
        catch (Exception ex)
        {
            return MapException(ex);
        }
    }

    [HttpPost("{id}/reviews")]
    [Authorize]
    [EnableRateLimiting("reviews")]
    public async Task<IActionResult> CreateLocationReview([FromRoute] int id, [FromBody] CreateLocationReviewRequestDto request)
    {
        try
        {
            var userId = TryGetUserId();
            if (!userId.HasValue)
                return Unauthorized();

            var result = await _reviewService.CreateLocationReviewAsync(
                id,
                userId.Value,
                new CreateReviewDto
                {
                    Rating = (int)Math.Round(request.Rating, MidpointRounding.AwayFromZero),
                    Comment = request.Comment
                });

            return CreatedAtAction(nameof(GetLocationReviews), new { id }, MapLegacyLocationReviewResponse(id, result));
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

    private static GetLocationReviewsResponseDto MapLegacyLocationReviewsResponse(
        int locationId,
        PagedReviewsResponseDto response) => new()
    {
        LocationId = locationId,
        AverageRating = response.AverageRating ?? 0,
        ReviewsCount = response.TotalCount,
        TotalCount = response.TotalCount,
        Skip = response.Skip,
        Take = response.Take,
        NextSkip = response.NextSkip,
        HasMore = response.HasMore,
        Reviews = response.Items.Select(review => new LocationReviewItemDto
        {
            Id = review.Id,
            UserId = review.AuthorUserId,
            Rating = review.Rating,
            Comment = review.Body,
            CreatedAt = review.CreatedAt,
            UpdatedAt = review.UpdatedAt
        }).ToList()
    };

    private static CreateLocationReviewResponseDto MapLegacyLocationReviewResponse(
        int locationId,
        ReviewDto review) => new()
    {
        Id = review.Id,
        UserId = review.AuthorUserId,
        LocationId = locationId,
        Rating = review.Rating,
        Comment = review.Body,
        CreatedAt = review.CreatedAt,
        UpdatedAt = review.UpdatedAt
    };

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
