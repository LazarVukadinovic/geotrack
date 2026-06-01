using System.Security.Claims;
using System.Text.Json;
using Geotrack.Application.DTOs.Contributions;
using Geotrack.Application.Interfaces.Service;
using Geotrack.Application.Options;
using Geotrack.Server.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace Geotrack.Server.Controllers;

[ApiController]
[Route("api/contributions")]
[Authorize]
public class ContributionsController : ControllerBase
{
    private readonly IPlaceContributionService _contributions;
    private readonly UploadLimitsOptions _uploadLimits;

    public ContributionsController(
        IPlaceContributionService contributions,
        IOptions<UploadLimitsOptions> uploadLimits)
    {
        _contributions = contributions;
        _uploadLimits = uploadLimits.Value;
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

    [HttpGet("place-types")]
    public async Task<ActionResult<List<ContributionPlaceTypeDto>>> GetPlaceTypes()
    {
        return Ok(await _contributions.GetPlaceTypesAsync());
    }

    [HttpPost]
    [HttpPost("places")]
    [HttpPost("stays")]
    [Consumes("multipart/form-data")]
    [EnableRateLimiting("contributions")]
    [RequestSizeLimit(UploadLimitDefaults.ContributionMaxTotalRequestBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = UploadLimitDefaults.ProofDocumentMaxFileSizeBytes)]
    public async Task<ActionResult<PlaceContributionResponseDto>> CreatePlaceContribution(
        [FromForm] string payload,
        IFormFile? cardImage,
        IFormFile? coverImage,
        List<IFormFile>? galleryImages,
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

        byte[]? cardImageBytes = null;
        byte[]? coverImageBytes = null;
        List<byte[]> galleryImageBytes = new();
        byte[]? ownershipProofBytes = null;

        try
        {
            await UploadValidation.ValidateContributionUploadAsync(
                cardImage,
                coverImage,
                galleryImages,
                ownershipProofDocument,
                _uploadLimits,
                HttpContext.RequestAborted);

            cardImageBytes = await UploadValidation.ReadFileBytesAsync(cardImage, HttpContext.RequestAborted);
            coverImageBytes = await UploadValidation.ReadFileBytesAsync(coverImage, HttpContext.RequestAborted);
            galleryImageBytes = await UploadValidation.ReadFileBytesAsync(galleryImages, HttpContext.RequestAborted);
            ownershipProofBytes = await UploadValidation.ReadFileBytesAsync(ownershipProofDocument, HttpContext.RequestAborted);

            var contribution = await _contributions.CreateAsync(
                GetCurrentUserId(),
                request,
                cardImageBytes,
                coverImageBytes,
                galleryImageBytes,
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
    [EnableRateLimiting("contributions")]
    [RequestSizeLimit(UploadLimitDefaults.ContributionMaxTotalRequestBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = UploadLimitDefaults.ProofDocumentMaxFileSizeBytes)]
    public async Task<ActionResult<PlaceContributionResponseDto>> UpdatePlaceContribution(
        int id,
        [FromForm] string payload,
        IFormFile? cardImage,
        IFormFile? coverImage,
        List<IFormFile>? galleryImages,
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
            await UploadValidation.ValidateContributionUploadAsync(
                cardImage,
                coverImage,
                galleryImages,
                ownershipProofDocument,
                _uploadLimits,
                HttpContext.RequestAborted);

            var contribution = await _contributions.UpdateAsync(
                GetCurrentUserId(),
                id,
                request,
                await UploadValidation.ReadFileBytesAsync(cardImage, HttpContext.RequestAborted),
                await UploadValidation.ReadFileBytesAsync(coverImage, HttpContext.RequestAborted),
                await UploadValidation.ReadFileBytesAsync(galleryImages, HttpContext.RequestAborted),
                await UploadValidation.ReadFileBytesAsync(ownershipProofDocument, HttpContext.RequestAborted),
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

    private ActionResult HandleException(Exception ex)
    {
        return ex switch
        {
            UploadValidationException upload => StatusCode(upload.StatusCode, new { message = upload.Message }),
            UnauthorizedAccessException => StatusCode(StatusCodes.Status403Forbidden, new { message = "Forbidden." }),
            KeyNotFoundException => NotFound(new { message = ex.Message }),
            InvalidOperationException => BadRequest(new { message = ex.Message }),
            _ => StatusCode(StatusCodes.Status500InternalServerError, new { message = "An unexpected error occurred." })
        };
    }
}
