using Geotrack.Application.DTOs;
using Geotrack.Application.DTOs.Explore;
using Geotrack.Application.Interfaces;
using Geotrack.Application.Services;
using Geotrack.Server.Requests;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace Geotrack.Server.Controllers;

[ApiController]
[Route("api/destinations")]
public class DestinationController : ControllerBase
{
    private readonly DestinationService _destinationService;

    public DestinationController(DestinationService destinationService)
    {
        _destinationService = destinationService;
    }

    [HttpPost("explore-by-location")]
    public async Task<ActionResult> ExploreByLocation([FromBody] ExploreByLocationRequest request)
    {
        int? userId = null;

        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
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

    [HttpGet("{destinationId}")]
    public async Task<ActionResult> GetDestinationDetails(int destinationId)
    {
        var result = await _destinationService.GetDestinationDetailsAsync(destinationId);
        return Ok(result);
    }

    [HttpGet("{destinationId}/sections/{sectionKey}")]
    public async Task<ActionResult> GetDestinationSection(int destinationId, string sectionKey)
    {
        var result = await _destinationService.GetDestinationSectionAsync(destinationId, sectionKey);
        return Ok(result);
    }

    [HttpGet("{destinationId}/reviews")]
    public async Task<ActionResult> GetDestinationReviews(int destinationId)
    {
        var result = await _destinationService.GetDestinationReviewsAsync(destinationId);
        return Ok(result);
    }

}