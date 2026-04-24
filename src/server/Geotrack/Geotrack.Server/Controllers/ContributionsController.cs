using System.Text.Json;
using Geotrack.Application.DTOs.Contributions;
using Geotrack.Application.Interfaces.Service;
using Geotrack.Server.Requests;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Geotrack.Server.Controllers;

[ApiController]
[Route("api/contributions")]
[Authorize]
public class ContributionsController : ControllerBase
{
    private const long MaxCoverImageBytes = 8 * 1024 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IStayContributionService _stayContributionService;

    public ContributionsController(IStayContributionService stayContributionService)
    {
        _stayContributionService = stayContributionService;
    }

    [HttpGet]
    public async Task<ActionResult<GetStayContributionsResponseDto>> GetMine()
    {
        try
        {
            return Ok(await _stayContributionService.GetMineAsync(GetUserId()));
        }
        catch (Exception ex)
        {
            return HandleException(ex);
        }
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<StayContributionDetailDto>> GetMineDetail([FromRoute] int id)
    {
        try
        {
            return Ok(await _stayContributionService.GetMineDetailAsync(GetUserId(), id));
        }
        catch (Exception ex)
        {
            return HandleException(ex);
        }
    }

    [HttpPost("stays")]
    [Consumes("multipart/form-data")]
    public async Task<ActionResult<StayContributionResponseDto>> CreateStay([FromForm] StayContributionFormRequest request)
    {
        try
        {
            var payload = ParsePayload(request.Payload);
            var contribution = await _stayContributionService.CreateAsync(
                GetUserId(),
                payload,
                await ReadCoverImageAsync(request.CoverImage));

            return CreatedAtAction(nameof(GetMineDetail), new { id = contribution.Id }, new StayContributionResponseDto
            {
                Contribution = contribution
            });
        }
        catch (Exception ex)
        {
            return HandleException(ex);
        }
    }

    [HttpPut("stays/{id:int}")]
    [Consumes("multipart/form-data")]
    public async Task<ActionResult<StayContributionResponseDto>> UpdateStay(
        [FromRoute] int id,
        [FromForm] StayContributionFormRequest request)
    {
        try
        {
            var payload = ParsePayload(request.Payload);
            var contribution = await _stayContributionService.UpdateAsync(
                GetUserId(),
                id,
                payload,
                await ReadCoverImageAsync(request.CoverImage));

            return Ok(new StayContributionResponseDto { Contribution = contribution });
        }
        catch (Exception ex)
        {
            return HandleException(ex);
        }
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete([FromRoute] int id)
    {
        try
        {
            await _stayContributionService.DeleteAsync(GetUserId(), id);
            return NoContent();
        }
        catch (Exception ex)
        {
            return HandleException(ex);
        }
    }

    [HttpGet("review")]
    public async Task<ActionResult<GetStayContributionsResponseDto>> GetReviewQueue([FromQuery] string? status = null)
    {
        try
        {
            return Ok(await _stayContributionService.GetReviewQueueAsync(GetUserId(), status));
        }
        catch (Exception ex)
        {
            return HandleException(ex);
        }
    }

    [HttpPost("{id:int}/approve")]
    public async Task<ActionResult<StayContributionResponseDto>> Approve(
        [FromRoute] int id,
        [FromBody] ReviewStayContributionRequestDto request)
    {
        try
        {
            var contribution = await _stayContributionService.ApproveAsync(GetUserId(), id, request);
            return Ok(new StayContributionResponseDto { Contribution = contribution });
        }
        catch (Exception ex)
        {
            return HandleException(ex);
        }
    }

    [HttpPost("{id:int}/reject")]
    public async Task<ActionResult<StayContributionResponseDto>> Reject(
        [FromRoute] int id,
        [FromBody] ReviewStayContributionRequestDto request)
    {
        try
        {
            var contribution = await _stayContributionService.RejectAsync(GetUserId(), id, request);
            return Ok(new StayContributionResponseDto { Contribution = contribution });
        }
        catch (Exception ex)
        {
            return HandleException(ex);
        }
    }

    private static UpsertStayContributionDto ParsePayload(string payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
            throw new InvalidOperationException("Contribution payload is required.");

        try
        {
            return JsonSerializer.Deserialize<UpsertStayContributionDto>(payload, JsonOptions)
                ?? throw new InvalidOperationException("Contribution payload is invalid.");
        }
        catch (JsonException)
        {
            throw new InvalidOperationException("Contribution payload is invalid.");
        }
    }

    private static async Task<byte[]?> ReadCoverImageAsync(IFormFile? coverImage)
    {
        if (coverImage is null || coverImage.Length == 0)
            return null;

        if (coverImage.Length > MaxCoverImageBytes)
            throw new InvalidOperationException("Contribution image must be 8 MB or smaller.");

        await using var stream = new MemoryStream();
        await coverImage.CopyToAsync(stream);
        return stream.ToArray();
    }

    private int GetUserId()
    {
        var userIdClaim = User.FindFirst("userId")?.Value;

        if (!int.TryParse(userIdClaim, out var userId))
            throw new UnauthorizedAccessException("Invalid user token");

        return userId;
    }

    private ActionResult HandleException(Exception ex)
    {
        return ex switch
        {
            UnauthorizedAccessException => Unauthorized(new { message = ex.Message }),
            KeyNotFoundException => NotFound(new { message = ex.Message }),
            InvalidOperationException => BadRequest(new { message = ex.Message }),
            _ => BadRequest(new { message = ex.Message })
        };
    }
}
