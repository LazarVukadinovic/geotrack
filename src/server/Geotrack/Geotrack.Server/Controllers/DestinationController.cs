using System.Security.Claims;
using Geotrack.Application.DTOs;
using Geotrack.Application.DTOs.Destionation;
using Geotrack.Application.DTOs.Explore;
using Geotrack.Application.Interfaces;
using Geotrack.Application.Interfaces.Service;
using Geotrack.Application.Services;
using Geotrack.Server.Requests;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Geotrack.Server.Controllers;

[ApiController]
[Route("api/destinations")]
public class DestinationController : ControllerBase
{
    private readonly DestinationService _destinationService;
    private readonly IDestinationReviewService _reviewService;

    public DestinationController(DestinationService destinationService, IDestinationReviewService reviewService)
    {
        _destinationService = destinationService;
        _reviewService = reviewService;
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

    [HttpPost("{id}/reviews")]
    [Authorize]
    public async Task<IActionResult> CreateReview(
    [FromRoute] int id,
    [FromBody] CreateDestinationReviewRequestDto dto)
    {
        var userIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!int.TryParse(userIdClaim, out var userId))
            return Unauthorized();

        var result = await _reviewService.CreateReviewAsync(id, userId, dto);
        return CreatedAtAction(nameof(CreateReview), new { id }, result);
    }

}