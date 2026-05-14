using Geotrack.Application.DTOs.Destinations;
using Geotrack.Infrastructure;
using Geotrack.Infrastructure.Services;
using Microsoft.AspNetCore.Mvc;

namespace Geotrack.Server.Controllers;

[ApiController]
[Route("api/destinations/discovery-search")]
public class DestinationDiscoverySearchController : ControllerBase
{
    private readonly DestinationDiscoverySearchService _searchService;
    private readonly ILogger<DestinationDiscoverySearchController> _logger;

    public DestinationDiscoverySearchController(
        AppDbContext db,
        ILogger<DestinationDiscoverySearchController> logger)
    {
        _searchService = new DestinationDiscoverySearchService(db);
        _logger = logger;
    }

    [HttpGet]
    public async Task<ActionResult<DestinationDiscoverySearchResponseDto>> Search(
        [FromQuery(Name = "q")] string? query = null,
        [FromQuery] string? tagIds = null,
        [FromQuery] string? placeTypeIds = null,
        [FromQuery] int skip = 0,
        [FromQuery] int take = 20,
        CancellationToken cancellationToken = default)
    {
        if (skip < 0)
            return BadRequest(new { message = "skip must be zero or greater." });

        if (take <= 0)
            return BadRequest(new { message = "take must be greater than zero." });

        try
        {
            var result = await _searchService.SearchAsync(
                query,
                ParsePositiveIds(tagIds),
                ParsePositiveIds(placeTypeIds),
                skip,
                take,
                cancellationToken);

            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected destination discovery search error.");
            return StatusCode(StatusCodes.Status500InternalServerError, new { message = "An unexpected error occurred." });
        }
    }

    [HttpGet("filters")]
    public async Task<ActionResult<DestinationDiscoveryFiltersDto>> GetFilters(CancellationToken cancellationToken = default)
    {
        try
        {
            return Ok(await _searchService.GetFiltersAsync(cancellationToken));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected destination discovery search filters error.");
            return StatusCode(StatusCodes.Status500InternalServerError, new { message = "An unexpected error occurred." });
        }
    }

    private static List<int> ParsePositiveIds(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return [];

        var ids = new List<int>();
        foreach (var token in value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!int.TryParse(token, out var id) || id <= 0)
            {
                throw new InvalidOperationException($"Invalid id value '{token}'.");
            }

            if (!ids.Contains(id))
            {
                ids.Add(id);
            }
        }

        return ids;
    }
}
