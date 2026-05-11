using System.Security.Claims;
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
    private readonly IPlaceContributionService _contributions;

    public ContributionsController(IPlaceContributionService contributions)
    {
        _contributions = contributions;
    }

    [HttpGet]
    [HttpGet("mine")]
    public async Task<ActionResult<GetPlaceContributionsResponseDto>> GetMine()
    {
        var userId = GetCurrentUserId();
        var response = await _contributions.GetForUserAsync(userId);
        return Ok(response);
    }

    [HttpGet("{id:int}")]
    [HttpGet("mine/{id:int}")]
    public async Task<ActionResult<PlaceContributionResponseDto>> GetMineById(int id)
    {
        var userId = GetCurrentUserId();
        var contribution = await _contributions.GetByIdForUserAsync(userId, id);

        if (contribution is null)
            return NotFound();

        return Ok(new PlaceContributionResponseDto
        {
            Contribution = contribution
        });
    }

    [HttpGet("pending")]
    [HttpGet("review")]
    [Authorize(Roles = "Admin,RegionalManager")]
    public async Task<ActionResult<GetPlaceContributionsResponseDto>> GetPending([FromQuery] string? status = null)
    {
        var userId = GetCurrentUserId();
        var response = await _contributions.GetPendingReviewAsync(userId);
        return Ok(response);
    }

    [HttpGet("pending/{id:int}")]
    [HttpGet("review/{id:int}")]
    [Authorize(Roles = "Admin,RegionalManager")]
    public async Task<ActionResult<PlaceContributionResponseDto>> GetPendingById(int id)
    {
        var userId = GetCurrentUserId();
        var contribution = await _contributions.GetPendingReviewByIdAsync(id, userId);

        if (contribution is null)
            return NotFound();

        return Ok(new PlaceContributionResponseDto
        {
            Contribution = contribution
        });
    }

    [HttpPost]
    [HttpPost("places")]
    [HttpPost("stays")]
    [Consumes("multipart/form-data")]
    public async Task<ActionResult<PlaceContributionResponseDto>> CreatePlaceContribution(
        [FromForm] string payload,
        IFormFile? mainImage,
        IFormFile? coverImage,
        IFormFile? ownershipProofDocument)
    {
        UpsertPlaceContributionDto? request;

        try
        {
            request = JsonSerializer.Deserialize<UpsertPlaceContributionDto>(
                payload,
                new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
        }
        catch (JsonException)
        {
            return BadRequest(new { message = "Invalid contribution payload." });
        }

        if (request is null)
            return BadRequest(new { message = "Contribution payload is required." });

        byte[]? mainImageBytes = null;
        byte[]? coverImageBytes = null;
        byte[]? ownershipProofBytes = null;

        try
        {
            mainImageBytes = await ReadFileBytesAsync(mainImage);
            coverImageBytes = await ReadFileBytesAsync(coverImage);
            ownershipProofBytes = await ReadFileBytesAsync(ownershipProofDocument);

            var contribution = await _contributions.CreateAsync(
                GetCurrentUserId(),
                request,
                mainImageBytes,
                coverImageBytes,
                ownershipProofBytes,
                ownershipProofDocument?.FileName,
                ownershipProofDocument?.ContentType);

            return CreatedAtAction(
                nameof(GetMineById),
                new { id = contribution.Id },
                new PlaceContributionResponseDto
                {
                    Contribution = contribution
                });
        }
        catch (Exception ex)
        {
            return HandleException(ex);
        }
    }

    [HttpPut("{id:int}")]
    [HttpPut("places/{id:int}")]
    [HttpPut("stays/{id:int}")]
    [Consumes("multipart/form-data")]
    public async Task<ActionResult<PlaceContributionResponseDto>> UpdatePlaceContribution(
        int id,
        [FromForm] string payload,
        IFormFile? mainImage,
        IFormFile? coverImage,
        IFormFile? ownershipProofDocument)
    {
        UpsertPlaceContributionDto? request;

        try
        {
            request = JsonSerializer.Deserialize<UpsertPlaceContributionDto>(
                payload,
                new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
        }
        catch (JsonException)
        {
            return BadRequest(new { message = "Invalid contribution payload." });
        }

        if (request is null)
            return BadRequest(new { message = "Contribution payload is required." });

        try
        {
            var contribution = await _contributions.UpdateAsync(
                GetCurrentUserId(),
                id,
                request,
                await ReadFileBytesAsync(mainImage),
                await ReadFileBytesAsync(coverImage),
                await ReadFileBytesAsync(ownershipProofDocument),
                ownershipProofDocument?.FileName,
                ownershipProofDocument?.ContentType);

            return Ok(new PlaceContributionResponseDto
            {
                Contribution = contribution
            });
        }
        catch (Exception ex)
        {
            return HandleException(ex);
        }
    }

    [HttpDelete("{id:int}")]
    [HttpDelete("places/{id:int}")]
    [HttpDelete("stays/{id:int}")]
    public async Task<IActionResult> DeletePlaceContribution(int id)
    {
        try
        {
            await _contributions.DeleteAsync(GetCurrentUserId(), id);
            return NoContent();
        }
        catch (Exception ex)
        {
            return HandleException(ex);
        }
    }

    [HttpPost("{id:int}/approve")]
    [HttpPost("places/{id:int}/approve")]
    [Authorize(Roles = "Admin,RegionalManager")]
    public async Task<ActionResult<PlaceContributionResponseDto>> ApprovePlaceContribution(
        int id,
        [FromBody] ReviewPlaceContributionRequestDto? request)
    {
        try
        {
            var contribution = await _contributions.ApproveAsync(
                id,
                GetCurrentUserId(),
                request?.Note);

            return Ok(new PlaceContributionResponseDto
            {
                Contribution = contribution
            });
        }
        catch (Exception ex)
        {
            return HandleException(ex);
        }
    }

    [HttpPost("{id:int}/reject")]
    [HttpPost("places/{id:int}/reject")]
    [Authorize(Roles = "Admin,RegionalManager")]
    public async Task<ActionResult<PlaceContributionResponseDto>> RejectPlaceContribution(
        int id,
        [FromBody] ReviewPlaceContributionRequestDto? request)
    {
        try
        {
            var contribution = await _contributions.RejectAsync(
                id,
                GetCurrentUserId(),
                request?.Note);

            return Ok(new PlaceContributionResponseDto
            {
                Contribution = contribution
            });
        }
        catch (Exception ex)
        {
            return HandleException(ex);
        }
    }

    private int GetCurrentUserId()
    {
        var value = User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirstValue("sub")
            ?? User.FindFirstValue("userId");

        return int.TryParse(value, out var userId)
            ? userId
            : throw new InvalidOperationException("Authenticated user id was not found.");
    }

    private static async Task<byte[]?> ReadFileBytesAsync(IFormFile? file)
    {
        if (file is null || file.Length == 0)
            return null;

        await using var stream = file.OpenReadStream();
        using var memory = new MemoryStream();
        await stream.CopyToAsync(memory);
        return memory.ToArray();
    }

    private ActionResult HandleException(Exception ex)
    {
        return ex switch
        {
            UnauthorizedAccessException => StatusCode(StatusCodes.Status403Forbidden, new { message = "Forbidden." }),
            KeyNotFoundException => NotFound(new { message = ex.Message }),
            InvalidOperationException => BadRequest(new { message = ex.Message }),
            _ => StatusCode(StatusCodes.Status500InternalServerError, new { message = "An unexpected error occurred." })
        };
    }
}
