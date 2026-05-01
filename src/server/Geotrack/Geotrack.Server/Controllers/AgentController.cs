using Geotrack.Application.DTOs.Agent;
using Geotrack.Application.Interfaces.Service;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Geotrack.Server.Controllers;

[ApiController]
[Route("api/agent")]
[EnableRateLimiting("agent")]
public class AgentController : ControllerBase
{
    private readonly IAgentService _agentService;

    public AgentController(IAgentService agentService)
    {
        _agentService = agentService;
    }

    [HttpPost("message")]
    public async Task<ActionResult> SendMessage([FromBody] AgentSendMessageDto dto)
    {
        var userId = GetUserId();
        var response = await _agentService.ProcessMessageAsync(dto.Text, userId);
        return Ok(response);
    }

    private int? GetUserId()
    {
        var claim = User.FindFirst("userId")?.Value;
        return int.TryParse(claim, out var id) ? id : null;
    }
}
