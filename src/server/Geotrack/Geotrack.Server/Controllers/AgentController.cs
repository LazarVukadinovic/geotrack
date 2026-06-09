using Geotrack.Application.DTOs.Agent;
using Geotrack.Application.Interfaces.Service;
using Geotrack.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Geotrack.Server.Controllers;

[ApiController]
[Route("api/agent")]
[EnableRateLimiting("agent")]
public class AgentController : ControllerBase
{
    private readonly IAgentOrchestrator _agentOrchestrator;

    public AgentController(IAgentOrchestrator agentOrchestrator)
    {
        _agentOrchestrator = agentOrchestrator;
    }

    [HttpGet("providers")]
    public async Task<ActionResult<AgentProvidersResponseDto>> GetProviders(CancellationToken cancellationToken)
    {
        return Ok(await _agentOrchestrator.GetProvidersAsync(GetUserId(), cancellationToken));
    }

    [Authorize]
    [HttpPost("chat")]
    public async Task<ActionResult<AgentChatResponseDto>> Chat(
        [FromBody] AgentChatRequestDto request,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _agentOrchestrator.ChatAsync(request, GetRequiredUserId(), cancellationToken));
        }
        catch (AgentException ex)
        {
            return StatusCode(ex.StatusCode, new { code = ex.Code, message = ex.SafeMessage });
        }
    }

    private int? GetUserId()
    {
        var claim = User.FindFirst("userId")?.Value;
        return int.TryParse(claim, out var id) ? id : null;
    }

    private int GetRequiredUserId() =>
        GetUserId() ?? throw new UnauthorizedAccessException();
}
