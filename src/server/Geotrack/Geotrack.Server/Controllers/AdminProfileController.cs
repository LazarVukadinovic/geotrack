using System.Security.Claims;
using Geotrack.Application.DTOs.Admin;
using Geotrack.Application.DTOs.Users;
using Geotrack.Application.Interfaces.Service;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Geotrack.Server.Controllers;

[ApiController]
[Route("api/admin/profile")]
[Authorize(Policy = "RequireRegionalManager")]
public class AdminProfileController : ControllerBase
{
    private const long MaxAdminAvatarFileSizeBytes = 2 * 1024 * 1024;

    private static readonly HashSet<string> AllowedAvatarContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/png",
        "image/jpeg",
        "image/jpg",
        "image/webp",
    };

    private readonly IAdminProfileService _adminProfileService;
    private readonly IUserService _userService;

    public AdminProfileController(
        IAdminProfileService adminProfileService,
        IUserService userService)
    {
        _adminProfileService = adminProfileService;
        _userService = userService;
    }

    [HttpGet]
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
    public async Task<ActionResult<UpdateAvatarResponseDto>> UploadAvatar(IFormFile? avatar)
    {
        try
        {
            if (avatar is null || avatar.Length == 0)
                return BadRequest(new { message = "Please select an image to upload." });

            if (avatar.Length > MaxAdminAvatarFileSizeBytes)
                return BadRequest(new { message = "Profile image must be 2 MB or smaller." });

            await using var memoryStream = new MemoryStream();
            await avatar.CopyToAsync(memoryStream);
            var fileBytes = memoryStream.ToArray();

            if (!IsAllowedAvatarContentType(avatar.ContentType) || !IsSupportedAdminAvatar(fileBytes))
                return BadRequest(new { message = "Only PNG, JPG, JPEG, or WEBP images are supported." });

            return Ok(await _userService.UpdateAvatarAsync(GetCurrentUserId(), fileBytes));
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

    private static bool IsAllowedAvatarContentType(string? contentType)
    {
        return string.IsNullOrWhiteSpace(contentType) ||
            AllowedAvatarContentTypes.Contains(contentType.Trim());
    }

    private static bool IsSupportedAdminAvatar(ReadOnlySpan<byte> fileBytes)
    {
        if (fileBytes.Length >= 8 &&
            fileBytes[0] == 0x89 &&
            fileBytes[1] == 0x50 &&
            fileBytes[2] == 0x4E &&
            fileBytes[3] == 0x47 &&
            fileBytes[4] == 0x0D &&
            fileBytes[5] == 0x0A &&
            fileBytes[6] == 0x1A &&
            fileBytes[7] == 0x0A)
        {
            return true;
        }

        if (fileBytes.Length >= 3 &&
            fileBytes[0] == 0xFF &&
            fileBytes[1] == 0xD8 &&
            fileBytes[2] == 0xFF)
        {
            return true;
        }

        return fileBytes.Length >= 12 &&
            fileBytes[0] == 0x52 &&
            fileBytes[1] == 0x49 &&
            fileBytes[2] == 0x46 &&
            fileBytes[3] == 0x46 &&
            fileBytes[8] == 0x57 &&
            fileBytes[9] == 0x45 &&
            fileBytes[10] == 0x42 &&
            fileBytes[11] == 0x50;
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
