using Geotrack.Application.Interfaces.Service;
using Microsoft.AspNetCore.Mvc;

namespace Geotrack.Server.Controllers;

[ApiController]
[Route("api/search")]
public class SearchController : ControllerBase
{
    private readonly ISearchService _searchService;

    public SearchController(ISearchService searchService)
    {
        _searchService = searchService;
    }

    [HttpGet]
    public async Task<IActionResult> Search(
        [FromQuery] string? q = null,
        [FromQuery] string? domains = null,
        [FromQuery] double? latitude = null,
        [FromQuery] double? longitude = null,
        [FromQuery] string? language = null,
        [FromQuery] int take = 6,
        CancellationToken cancellationToken = default)
    {
        if (latitude.HasValue != longitude.HasValue)
        {
            return BadRequest(new { error = "latitude and longitude must be provided together." });
        }

        if (latitude.HasValue && !HasValidCoordinates(latitude.Value, longitude!.Value))
        {
            return BadRequest(new { error = "latitude and longitude must be finite values in valid coordinate ranges." });
        }

        if (take <= 0)
        {
            return BadRequest(new { error = "take must be greater than zero." });
        }

        var result = await _searchService.SearchAsync(
            q,
            domains,
            latitude,
            longitude,
            language,
            take,
            cancellationToken);

        return Ok(result);
    }

    [HttpGet("metadata")]
    public async Task<IActionResult> Metadata(
        [FromQuery] string? language = null,
        CancellationToken cancellationToken = default)
    {
        var metadata = await _searchService.GetMetadataAsync(language, cancellationToken);
        return Ok(metadata);
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
