using System.Security.Claims;
using Geotrack.Application.Interfaces.Service;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Geotrack.Server.Controllers
{
    [Route("api/events")]
    [ApiController]
    public class EventsController : ControllerBase
    {
        private readonly IEventService _eventService;

        public EventsController(IEventService eventService)
        {
            _eventService = eventService;
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> GetEventDetails(int id)
        {
            int? userId = null;
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (userIdClaim != null)
                userId = int.Parse(userIdClaim);

            var result = await _eventService.GetEventDetailsAsync(id, userId);
            return Ok(result);
        }

        [HttpGet("{id}/sections/{sectionKey}")]
        public async Task<IActionResult> GetEventSection(int id, string sectionKey)
        {
            var result = await _eventService.GetEventSectionAsync(id, sectionKey);
            return Ok(result);
        }
    }
}
