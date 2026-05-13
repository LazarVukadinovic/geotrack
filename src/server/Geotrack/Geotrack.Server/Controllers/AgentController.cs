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
    private readonly IAgentService _agentService;
    private readonly IAgentOrchestrator _agentOrchestrator;
    private readonly IAiProviderKeyService _keyService;

    public AgentController(
        IAgentService agentService,
        IAgentOrchestrator agentOrchestrator,
        IAiProviderKeyService keyService)
    {
        _agentService = agentService;
        _agentOrchestrator = agentOrchestrator;
        _keyService = keyService;
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

    [HttpPost("message")]
    public async Task<ActionResult> SendMessage([FromBody] AgentSendMessageDto dto)
    {
        var userId = GetUserId();
        var response = await _agentService.ProcessMessageAsync(dto.Text, userId);
        return Ok(response);
    }

    [Authorize]
    [HttpGet("api-keys")]
    public async Task<ActionResult<IReadOnlyList<AgentApiKeyMetadataDto>>> GetApiKeys(CancellationToken cancellationToken)
    {
        return Ok(await _keyService.GetKeysAsync(GetRequiredUserId(), cancellationToken));
    }

    [Authorize]
    [HttpPost("api-keys")]
    public async Task<ActionResult<AgentApiKeyMetadataDto>> SaveApiKey(
        [FromBody] SaveAgentApiKeyRequestDto request,
        CancellationToken cancellationToken)
    {
        try
        {
            var saved = await _keyService.SaveOrReplaceKeyAsync(
                GetRequiredUserId(),
                request.Provider,
                request.ApiKey,
                request.DisplayName,
                cancellationToken);

            return Ok(saved);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { code = "invalid_key_request", message = ex.Message });
        }
    }

    [Authorize]
    [HttpDelete("api-keys/{provider}")]
    public async Task<ActionResult> DeleteApiKey([FromRoute] string provider, CancellationToken cancellationToken)
    {
        try
        {
            await _keyService.DeleteKeyAsync(GetRequiredUserId(), provider, cancellationToken);
            return NoContent();
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { code = "invalid_key_request", message = ex.Message });
        }
    }

    [Authorize]
    [HttpPost("api-keys/test")]
    public async Task<ActionResult<TestAgentApiKeyResponseDto>> TestApiKey(
        [FromBody] TestAgentApiKeyRequestDto request,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _agentOrchestrator.TestKeyAsync(request, GetRequiredUserId(), cancellationToken));
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
