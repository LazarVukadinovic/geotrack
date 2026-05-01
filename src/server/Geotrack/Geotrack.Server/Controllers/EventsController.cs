using Geotrack.Application.DTOs.Destinations;
using Geotrack.Application.DTOs.Events;
using Geotrack.Application.Interfaces.Service;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Geotrack.Server.Controllers;

[ApiController]
[Route("api/events")]
public class EventsController : ControllerBase
{
    private readonly IEventService _eventService;

    public EventsController(IEventService eventService)
    {
        _eventService = eventService;
    }

    [HttpGet]
    public async Task<ActionResult> GetEvents(
        [FromQuery] int? destinationId = null,
        [FromQuery] string? tags = null,
        [FromQuery] bool? upcoming = null,
        [FromQuery] double? latitude = null,
        [FromQuery] double? longitude = null,
        [FromQuery] int skip = 0,
        [FromQuery] int? take = null)
    {
        var pagingError = ValidatePaging(skip, take);
        if (pagingError is not null)
        {
            return pagingError;
        }

        var userId = GetUserId();
        var result = await _eventService.GetEventsAsync(destinationId, tags, upcoming, userId, latitude, longitude, skip, take);
        return result.Status switch
        {
            DestinationScopedStatus.Success => Ok(result.Data),
            DestinationScopedStatus.InvalidDestination => BadRequest(new { error = "destinationId is required and must be positive." }),
            DestinationScopedStatus.DestinationNotFound => NotFound(new { error = "Destination not found." }),
            _ => StatusCode(500)
        };
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult> GetEventDetail(
        int id,
        [FromQuery] double? latitude = null,
        [FromQuery] double? longitude = null)
    {
        var userId = GetUserId();
        var detail = await _eventService.GetEventDetailAsync(id, userId, latitude, longitude);
        if (detail == null) return NotFound();
        return Ok(detail);
    }

    [HttpGet("{id:int}/sections/{sectionKey}")]
    public async Task<IActionResult> GetEventSection([FromRoute] int id, [FromRoute] string sectionKey)
    {
        try
        {
            var result = await _eventService.GetEventSectionAsync(id, sectionKey);
            return Ok(result);
        }
        catch (Exception ex)
        {
            return MapException(ex);
        }
    }

    [HttpGet("{id:int}/reviews")]
    public async Task<IActionResult> GetEventReviews([FromRoute] int id)
    {
        try
        {
            var result = await _eventService.GetEventReviewsAsync(id);
            return Ok(result);
        }
        catch (Exception ex)
        {
            return MapException(ex);
        }
    }

    [HttpPost("{id:int}/reviews")]
    [Authorize]
    public async Task<IActionResult> CreateEventReview([FromRoute] int id, [FromBody] CreateEventReviewRequestDto request)
    {
        try
        {
            var userId = GetUserId();
            if (!userId.HasValue)
                return Unauthorized();

            var result = await _eventService.CreateEventReviewAsync(id, userId.Value, request);
            return CreatedAtAction(nameof(GetEventReviews), new { id }, result);
        }
        catch (Exception ex)
        {
            return MapException(ex);
        }
    }

    private int? GetUserId()
    {
        var claim = User.FindFirst("userId")?.Value;
        return int.TryParse(claim, out var id) ? id : null;
    }

    private ActionResult? ValidatePaging(int skip, int? take)
    {
        if (skip < 0)
        {
            return BadRequest(new { error = "skip must be zero or greater." });
        }

        if (take.HasValue && take.Value <= 0)
        {
            return BadRequest(new { error = "take must be greater than zero." });
        }

        return null;
    }

    private IActionResult MapException(Exception ex)
    {
        return ex switch
        {
            KeyNotFoundException => NotFound(new { message = ex.Message }),
            InvalidOperationException => BadRequest(new { message = ex.Message }),
            _ => StatusCode(500, new { message = "An unexpected error occurred." })
        };
    }
}
