using System.Text.Json;
using Geotrack.Application.DTOs.Contributions;
using Geotrack.Application.Interfaces.Service;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Geotrack.Server.Controllers;

[ApiController]
[Route("api/contributions")]
[Authorize]
public class ContributionsController : ControllerBase
{
    private const long MaxMainImageBytes = 8 * 1024 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IPlaceContributionService _placeContributionService;

    public ContributionsController(IPlaceContributionService placeContributionService)
    {
        _placeContributionService = placeContributionService;
    }

    [HttpGet]
    public async Task<ActionResult<GetPlaceContributionsResponseDto>> GetMine()
    {
        try
        {
            return Ok(await _placeContributionService.GetMineAsync(GetUserId()));
        }
        catch (Exception ex)
        {
            return HandleException(ex);
        }
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<PlaceContributionDetailDto>> GetMineDetail([FromRoute] int id)
    {
        try
        {
            return Ok(await _placeContributionService.GetMineDetailAsync(GetUserId(), id));
        }
        catch (Exception ex)
        {
            return HandleException(ex);
        }
    }

    [HttpPost]
    [Consumes("multipart/form-data")]
    public async Task<ActionResult<PlaceContributionResponseDto>> Create(
        [FromForm(Name = "payload")] string payload,
        IFormFile? mainImage,
        IFormFile? coverImage)
    {
        try
        {
            var request = ParsePayload(payload);
            var contribution = await _placeContributionService.CreateAsync(
                GetUserId(),
                request,
                await ReadMainImageAsync(mainImage ?? coverImage));

            return CreatedAtAction(nameof(GetMineDetail), new { id = contribution.Id }, new PlaceContributionResponseDto
            {
                Contribution = contribution
            });
        }
        catch (Exception ex)
        {
            return HandleException(ex);
        }
    }

    [HttpPost("places")]
    [Consumes("multipart/form-data")]
    public Task<ActionResult<PlaceContributionResponseDto>> CreatePlace(
        [FromForm(Name = "payload")] string payload,
        IFormFile? mainImage,
        IFormFile? coverImage) =>
        Create(payload, mainImage, coverImage);

    [HttpPost("stays")]
    [Consumes("multipart/form-data")]
    public Task<ActionResult<PlaceContributionResponseDto>> CreateStay(
        [FromForm(Name = "payload")] string payload,
        IFormFile? mainImage,
        IFormFile? coverImage) =>
        Create(payload, mainImage, coverImage);

    [HttpPut("{id:int}")]
    [Consumes("multipart/form-data")]
    public async Task<ActionResult<PlaceContributionResponseDto>> Update(
        [FromRoute] int id,
        [FromForm(Name = "payload")] string payload,
        IFormFile? mainImage,
        IFormFile? coverImage)
    {
        try
        {
            var request = ParsePayload(payload);
            var contribution = await _placeContributionService.UpdateAsync(
                GetUserId(),
                id,
                request,
                await ReadMainImageAsync(mainImage ?? coverImage));

            return Ok(new PlaceContributionResponseDto { Contribution = contribution });
        }
        catch (Exception ex)
        {
            return HandleException(ex);
        }
    }

    [HttpPut("places/{id:int}")]
    [Consumes("multipart/form-data")]
    public Task<ActionResult<PlaceContributionResponseDto>> UpdatePlace(
        [FromRoute] int id,
        [FromForm(Name = "payload")] string payload,
        IFormFile? mainImage,
        IFormFile? coverImage) =>
        Update(id, payload, mainImage, coverImage);

    [HttpPut("stays/{id:int}")]
    [Consumes("multipart/form-data")]
    public Task<ActionResult<PlaceContributionResponseDto>> UpdateStay(
        [FromRoute] int id,
        [FromForm(Name = "payload")] string payload,
        IFormFile? mainImage,
        IFormFile? coverImage) =>
        Update(id, payload, mainImage, coverImage);

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete([FromRoute] int id)
    {
        try
        {
            await _placeContributionService.DeleteAsync(GetUserId(), id);
            return NoContent();
        }
        catch (Exception ex)
        {
            return HandleException(ex);
        }
    }

    [HttpGet("review")]
    [Authorize(Roles = "Admin,RegionalManager")]
    public async Task<ActionResult<GetPlaceContributionsResponseDto>> GetReviewQueue([FromQuery] string? status = null)
    {
        try
        {
            return Ok(await _placeContributionService.GetReviewQueueAsync(GetUserId(), status));
        }
        catch (Exception ex)
        {
            return HandleException(ex);
        }
    }

    [HttpPost("{id:int}/approve")]
    [Authorize(Roles = "Admin,RegionalManager")]
    public async Task<ActionResult<PlaceContributionResponseDto>> Approve(
        [FromRoute] int id,
        [FromBody] ReviewPlaceContributionRequestDto request)
    {
        try
        {
            var contribution = await _placeContributionService.ApproveAsync(GetUserId(), id, request);
            return Ok(new PlaceContributionResponseDto { Contribution = contribution });
        }
        catch (Exception ex)
        {
            return HandleException(ex);
        }
    }

    [HttpPost("{id:int}/reject")]
    [Authorize(Roles = "Admin,RegionalManager")]
    public async Task<ActionResult<PlaceContributionResponseDto>> Reject(
        [FromRoute] int id,
        [FromBody] ReviewPlaceContributionRequestDto request)
    {
        try
        {
            var contribution = await _placeContributionService.RejectAsync(GetUserId(), id, request);
            return Ok(new PlaceContributionResponseDto { Contribution = contribution });
        }
        catch (Exception ex)
        {
            return HandleException(ex);
        }
    }

    private static UpsertPlaceContributionDto ParsePayload(string payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
            throw new InvalidOperationException("Contribution payload is required.");

        try
        {
            return JsonSerializer.Deserialize<UpsertPlaceContributionDto>(payload, JsonOptions)
                ?? throw new InvalidOperationException("Contribution payload is invalid.");
        }
        catch (JsonException)
        {
            throw new InvalidOperationException("Contribution payload is invalid.");
        }
    }

    private static async Task<byte[]?> ReadMainImageAsync(IFormFile? mainImage)
    {
        if (mainImage is null || mainImage.Length == 0)
            return null;

        if (mainImage.Length > MaxMainImageBytes)
            throw new InvalidOperationException("Contribution image must be 8 MB or smaller.");

        await using var stream = new MemoryStream();
        await mainImage.CopyToAsync(stream);
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
            UnauthorizedAccessException authEx when authEx.Message == "Invalid user token" => Unauthorized(new { message = "Invalid user token" }),
            UnauthorizedAccessException => StatusCode(StatusCodes.Status403Forbidden, new { message = "Forbidden." }),
            KeyNotFoundException => NotFound(new { message = ex.Message }),
            InvalidOperationException => BadRequest(new { message = ex.Message }),
            _ => StatusCode(500, new { message = "An unexpected error occurred." })
        };
    }
}
