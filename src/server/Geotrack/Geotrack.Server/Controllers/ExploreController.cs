using Geotrack.Application.Interfaces.Service;
using Microsoft.AspNetCore.Mvc;

namespace Geotrack.Server.Controllers;

[ApiController]
[Route("api/explore")]
public class ExploreController : ControllerBase
{
    private readonly IExploreService _exploreService;
    private readonly IExploreSearchService _exploreSearchService;

    public ExploreController(
        IExploreService exploreService,
        IExploreSearchService exploreSearchService)
    {
        _exploreService = exploreService;
        _exploreSearchService = exploreSearchService;
    }

    [HttpGet]
    public async Task<ActionResult> GetExplorePage(
        [FromQuery] double? latitude,
        [FromQuery] double? longitude,
        [FromQuery] int? destinationId = null)
    {
        if (destinationId.HasValue)
        {
            if (destinationId.Value <= 0)
            {
                return BadRequest(new { error = "destinationId must be positive when provided." });
            }

            var destinationResult = await _exploreService.GetExplorePageForDestinationAsync(
                destinationId.Value,
                GetUserId());

            if (destinationResult.SelectedDestination is null)
            {
                return NotFound(new { error = "Destination not found." });
            }

            return Ok(destinationResult);
        }

        if (!latitude.HasValue || !longitude.HasValue)
        {
            return BadRequest(new { error = "latitude and longitude are required." });
        }

        if (!HasValidCoordinates(latitude.Value, longitude.Value))
        {
            return BadRequest(new { error = "latitude and longitude must be finite values in valid coordinate ranges." });
        }

        var result = await _exploreService.GetExplorePageAsync(
            latitude.Value,
            longitude.Value,
            GetUserId());

        return Ok(result);
    }

    [HttpGet("search")]
    public async Task<ActionResult> Search(
        [FromQuery] int destinationId,
        [FromQuery(Name = "q")] string? query,
        [FromQuery] int skip = 0,
        [FromQuery] int take = 20,
        CancellationToken cancellationToken = default)
    {
        if (destinationId <= 0)
        {
            return BadRequest(new { error = "destinationId is required and must be positive." });
        }

        if (skip < 0)
        {
            return BadRequest(new { error = "skip must be zero or greater." });
        }

        if (take <= 0)
        {
            return BadRequest(new { error = "take must be greater than zero." });
        }

        if (!string.IsNullOrWhiteSpace(query) && query.Trim().Length > 100)
        {
            return BadRequest(new { error = "Search query must be 100 characters or fewer." });
        }

        try
        {
            var result = await _exploreSearchService.SearchAsync(
                destinationId,
                query,
                skip,
                take,
                cancellationToken);

            return Ok(result);
        }
        catch (KeyNotFoundException)
        {
            return NotFound(new { error = "Destination not found." });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }


    [HttpGet("{sectionKey}")]
    public async Task<ActionResult> GetExploreSectionPage(
        [FromRoute] string sectionKey,
        [FromQuery] int destinationId,
        [FromQuery] int skip = 0,
        [FromQuery] int take = 20)
    {
        if (string.IsNullOrWhiteSpace(sectionKey))
        {
            return BadRequest(new { error = "sectionKey is required." });
        }

        if (string.Equals(sectionKey, "search", StringComparison.OrdinalIgnoreCase))
        {
            return BadRequest(new { error = "Use /api/explore/search for search." });
        }

        if (destinationId <= 0)
        {
            return BadRequest(new { error = "destinationId must be positive." });
        }

        if (skip < 0)
        {
            return BadRequest(new { error = "skip must be zero or greater." });
        }

        if (take <= 0)
        {
            return BadRequest(new { error = "take must be greater than zero." });
        }

        var result = await _exploreService.GetExploreSectionPageAsync(
            sectionKey,
            destinationId,
            GetUserId(),
            skip,
            take);

        if (result.SelectedDestination is null)
        {
            return NotFound(new { error = "Destination not found." });
        }

        if (!result.IsKnownSection)
        {
            return NotFound(new { error = "Explore section not found." });
        }

        return Ok(result);
    }

    private int? GetUserId()
    {
        var claim = User.FindFirst("userId")?.Value;
        return int.TryParse(claim, out var id) ? id : null;
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
}