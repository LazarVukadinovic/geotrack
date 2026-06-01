using System.Security.Claims;
using Geotrack.Application.DTOs.Admin;
using Geotrack.Application.DTOs.Users;
using Geotrack.Application.Interfaces.Service;
using Geotrack.Application.Options;
using Geotrack.Server.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace Geotrack.Server.Controllers;

[ApiController]
[Route("api/admin/profile")]
[Authorize(Policy = "RequireAdminAppUser")]
public class AdminProfileController : ControllerBase
{
    private readonly IAdminProfileService _adminProfileService;
    private readonly IUserService _userService;
    private readonly UploadLimitsOptions _uploadLimits;

    public AdminProfileController(
        IAdminProfileService adminProfileService,
        IUserService userService,
        IOptions<UploadLimitsOptions> uploadLimits)
    {
        _adminProfileService = adminProfileService;
        _userService = userService;
        _uploadLimits = uploadLimits.Value;
    }

    [HttpGet]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<ActionResult<AdminProfileResponseDto>> GetProfile()
    {
        try
        {
            return Ok(await _adminProfileService.GetProfileAsync(GetCurrentUserId()));
        }
        catch (Exception ex)
        {
            return HandleException(ex);
        }
    }

    [HttpPut]
    public async Task<ActionResult<AdminProfileResponseDto>> UpdateProfile(
        [FromBody] UpdateAdminProfileRequestDto? request)
    {
        if (request is null)
            return BadRequest(new { message = "Request body is required." });

        try
        {
            return Ok(await _adminProfileService.UpdateProfileAsync(GetCurrentUserId(), request));
        }
        catch (Exception ex)
        {
            return HandleException(ex);
        }
    }

    [HttpPost("avatar")]
    [Consumes("multipart/form-data")]
    [EnableRateLimiting("uploads")]
    [RequestSizeLimit(UploadLimitDefaults.AdminAvatarMaxFileSizeBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = UploadLimitDefaults.AdminAvatarMaxFileSizeBytes)]
    public async Task<ActionResult<UpdateAvatarResponseDto>> UploadAvatar(IFormFile? avatar)
    {
        try
        {
            if (avatar is null || avatar.Length == 0)
                return BadRequest(new { message = "Please select an image to upload." });

            await UploadValidation.ValidateAdminAvatarAsync(avatar, _uploadLimits, HttpContext.RequestAborted);
            var fileBytes = await UploadValidation.ReadFileBytesAsync(avatar, HttpContext.RequestAborted);

            return Ok(await _userService.UpdateAvatarAsync(GetCurrentUserId(), fileBytes!));
        }
        catch (Exception ex)
        {
            return HandleException(ex);
        }
    }

    [HttpDelete("avatar")]
    public async Task<ActionResult<UpdateAvatarResponseDto>> DeleteAvatar()
    {
        try
        {
            return Ok(await _userService.DeleteAvatarAsync(GetCurrentUserId()));
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
            : throw new UnauthorizedAccessException("Invalid admin session.");
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
