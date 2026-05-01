using Geotrack.Application.DTOs.Moderation;
using Geotrack.Application.Interfaces.Service;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Geotrack.Server.Controllers;

[ApiController]
[Authorize(Roles = "Admin")]
[Route("api/moderation/terms")]
public class ModerationTermsController : ControllerBase
{
    private readonly IModerationTermService _moderationTermService;

    public ModerationTermsController(IModerationTermService moderationTermService)
    {
        _moderationTermService = moderationTermService;
    }

    [HttpGet]
    public async Task<IActionResult> GetTerms(
        [FromQuery] string? languageCode = null,
        [FromQuery] string? category = null,
        [FromQuery] bool includeInactive = true)
    {
        try
        {
            var result = await _moderationTermService.GetTermsAsync(languageCode, category, includeInactive);
            return Ok(result);
        }
        catch (Exception ex)
        {
            return MapException(ex);
        }
    }

    [HttpPost]
    public async Task<IActionResult> CreateTerm([FromBody] CreateModerationTermRequestDto request)
    {
        try
        {
            var result = await _moderationTermService.CreateTermAsync(GetUserId(), request);
            return Created($"/api/moderation/terms/{result.Id}", result);
        }
        catch (Exception ex)
        {
            return MapException(ex);
        }
    }

    [HttpPut("{termId:int}")]
    public async Task<IActionResult> UpdateTerm(
        [FromRoute] int termId,
        [FromBody] UpdateModerationTermRequestDto request)
    {
        try
        {
            var result = await _moderationTermService.UpdateTermAsync(GetUserId(), termId, request);
            return Ok(result);
        }
        catch (Exception ex)
        {
            return MapException(ex);
        }
    }

    [HttpPost("{termId:int}/activate")]
    public async Task<IActionResult> ActivateTerm([FromRoute] int termId)
    {
        try
        {
            var result = await _moderationTermService.SetActiveAsync(GetUserId(), termId, isActive: true);
            return Ok(result);
        }
        catch (Exception ex)
        {
            return MapException(ex);
        }
    }

    [HttpPost("{termId:int}/deactivate")]
    public async Task<IActionResult> DeactivateTerm([FromRoute] int termId)
    {
        try
        {
            var result = await _moderationTermService.SetActiveAsync(GetUserId(), termId, isActive: false);
            return Ok(result);
        }
        catch (Exception ex)
        {
            return MapException(ex);
        }
    }

    [HttpDelete("{termId:int}")]
    public async Task<IActionResult> DeleteTerm([FromRoute] int termId)
    {
        try
        {
            await _moderationTermService.DeleteTermAsync(termId);
            return NoContent();
        }
        catch (Exception ex)
        {
            return MapException(ex);
        }
    }

    private int GetUserId()
    {
        var userIdClaim = User.FindFirst("userId")?.Value;

        if (!int.TryParse(userIdClaim, out var userId))
            throw new UnauthorizedAccessException("Invalid user token");

        return userId;
    }

    private IActionResult MapException(Exception ex)
    {
        return ex switch
        {
            UnauthorizedAccessException => Unauthorized(new { message = ex.Message }),
            KeyNotFoundException => NotFound(new { message = ex.Message }),
            InvalidOperationException => BadRequest(new { message = ex.Message }),
            _ => StatusCode(500, new { message = "An unexpected error occurred." })
        };
    }
}
