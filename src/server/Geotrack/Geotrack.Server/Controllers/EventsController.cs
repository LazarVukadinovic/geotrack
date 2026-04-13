using Geotrack.Application.Interfaces.Service;
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
        [FromQuery] int destinationId = 1,
        [FromQuery] string? tags = null,
        [FromQuery] bool? upcoming = null)
    {
        var userId = GetUserId();
        var result = await _eventService.GetEventsAsync(destinationId, tags, upcoming, userId);
        return Ok(result);
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult> GetEventDetail(int id)
    {
        var userId = GetUserId();
        var detail = await _eventService.GetEventDetailAsync(id, userId);
        if (detail == null) return NotFound();
        return Ok(detail);
    }

    private int? GetUserId()
    {
        var claim = User.FindFirst("userId")?.Value;
        return int.TryParse(claim, out var id) ? id : null;
    }
}
