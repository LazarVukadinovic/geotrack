using Geotrack.Application.DTOs.Tags;
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
[Route("api/user")]
[Authorize]
public class UserController : ControllerBase
{
    private readonly IUserService _userService;
    private readonly UploadLimitsOptions _uploadLimits;

    public UserController(
        IUserService userService,
        IOptions<UploadLimitsOptions> uploadLimits)
    {
        _userService = userService;
        _uploadLimits = uploadLimits.Value;
    }

    [HttpGet("me")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<ActionResult<GetMeResponseDto>> GetMe()
    {
        try
        {
            return Ok(await _userService.GetMeAsync(GetUserId()));
        }
        catch (Exception ex)
        {
            return HandleException(ex);
        }
    }

    [HttpPut("me")]
    public async Task<ActionResult<UpdateMeResponseDto>> UpdateMe([FromBody] UpdateMeRequestDto request)
    {
        try
        {
            return Ok(await _userService.UpdateMeAsync(GetUserId(), request));
        }
        catch (Exception ex)
        {
            return HandleException(ex);
        }
    }

    [HttpPut("me/avatar")]
    [Consumes("multipart/form-data")]
    [EnableRateLimiting("uploads")]
    [RequestSizeLimit(UploadLimitDefaults.UserAvatarMaxFileSizeBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = UploadLimitDefaults.UserAvatarMaxFileSizeBytes)]
    public async Task<ActionResult<UpdateAvatarResponseDto>> UpdateAvatar(
        IFormFile? avatar)
    {
        try
        {
            if (avatar is null || avatar.Length == 0)
                return BadRequest(new { message = "Please select an image to upload." });

            await UploadValidation.ValidateUserAvatarAsync(avatar, _uploadLimits, HttpContext.RequestAborted);
            var fileBytes = await UploadValidation.ReadFileBytesAsync(avatar, HttpContext.RequestAborted);

            return Ok(await _userService.UpdateAvatarAsync(GetUserId(), fileBytes!));
        }
        catch (Exception ex)
        {
            return HandleException(ex);
        }
    }

    [HttpDelete("me/avatar")]
    public async Task<ActionResult<UpdateAvatarResponseDto>> DeleteAvatar()
    {
        try
        {
            return Ok(await _userService.DeleteAvatarAsync(GetUserId()));
        }
        catch (Exception ex)
        {
            return HandleException(ex);
        }
    }

    [HttpPut("me/password")]
    public async Task<ActionResult<ChangePasswordResponseDto>> ChangePassword([FromBody] ChangePasswordRequestDto request)
    {
        try
        {
            return Ok(await _userService.ChangePasswordAsync(GetUserId(), request));
        }
        catch (Exception ex)
        {
            return HandleException(ex);
        }
    }

    [HttpGet("me/notifications")]
    public async Task<ActionResult<GetNotificationSettingsResponseDto>> GetNotificationSettings()
    {
        try
        {
            return Ok(await _userService.GetNotificationSettingsAsync(GetUserId()));
        }
        catch (Exception ex)
        {
            return HandleException(ex);
        }
    }

    [HttpPut("me/notifications")]
    public async Task<ActionResult<UpdateNotificationSettingsResponseDto>> UpdateNotificationSettings([FromBody] UpdateNotificationSettingsRequestDto request)
    {
        try
        {
            return Ok(await _userService.UpdateNotificationSettingsAsync(GetUserId(), request));
        }
        catch (Exception ex)
        {
            return HandleException(ex);
        }
    }

    [HttpGet("me/analytics")]
    public async Task<ActionResult<GetAnalyticsSettingsResponseDto>> GetAnalyticsSettings()
    {
        try
        {
            return Ok(await _userService.GetAnalyticsSettingsAsync(GetUserId()));
        }
        catch (Exception ex)
        {
            return HandleException(ex);
        }
    }

    [HttpPut("me/analytics")]
    public async Task<ActionResult<UpdateAnalyticsSettingsResponseDto>> UpdateAnalyticsSettings([FromBody] UpdateAnalyticsSettingsRequestDto request)
    {
        try
        {
            return Ok(await _userService.UpdateAnalyticsSettingsAsync(GetUserId(), request));
        }
        catch (Exception ex)
        {
            return HandleException(ex);
        }
    }

    [HttpGet("me/interests")]
    public async Task<ActionResult<GetUserInterestsResponseDto>> GetUserInterests()
    {
        try
        {
            return Ok(await _userService.GetUserInterestsAsync(GetUserId()));
        }
        catch (Exception ex)
        {
            return HandleException(ex);
        }
    }

    [HttpGet("me/support-requests")]
    public async Task<ActionResult<GetSupportRequestsResponseDto>> GetSupportRequests()
    {
        try
        {
            return Ok(await _userService.GetSupportRequestsAsync(GetUserId()));
        }
        catch (Exception ex)
        {
            return HandleException(ex);
        }
    }

    [HttpPost("me/support-requests")]
    [EnableRateLimiting("support")]
    public async Task<ActionResult<CreateSupportRequestResponseDto>> CreateSupportRequest([FromBody] CreateSupportRequestRequestDto request)
    {
        try
        {
            return Ok(await _userService.CreateSupportRequestAsync(GetUserId(), request));
        }
        catch (Exception ex)
        {
            return HandleException(ex);
        }
    }

    [HttpPut("me/interests")]
    public async Task<ActionResult<UpdateUserInterestsResponseDto>> UpdateUserInterests([FromBody] UpdateUserInterestsRequestDto request)
    {
        try
        {
            return Ok(await _userService.UpdateUserInterestsAsync(GetUserId(), request));
        }
        catch (Exception ex)
        {
            return HandleException(ex);
        }
    }

    [HttpDelete("me/interests/inferred/{tagId:int}")]
    public async Task<ActionResult> DismissInferredInterest([FromRoute] int tagId)
    {
        try
        {
            await _userService.DismissInferredInterestAsync(GetUserId(), tagId);
            return NoContent();
        }
        catch (Exception ex)
        {
            return HandleException(ex);
        }
    }

    [HttpGet("me/collections")]
    public async Task<ActionResult<GetCollectionsResponseDto>> GetCollections()
    {
        try
        {
            return Ok(await _userService.GetCollectionsAsync(GetUserId()));
        }
        catch (Exception ex)
        {
            return HandleException(ex);
        }
    }

    [HttpPost("me/collections")]
    [EnableRateLimiting("userActions")]
    public async Task<ActionResult<CreateCollectionResponseDto>> CreateCollection([FromBody] CreateCollectionRequestDto request)
    {
        try
        {
            return Ok(await _userService.CreateCollectionAsync(GetUserId(), request));
        }
        catch (Exception ex)
        {
            return HandleException(ex);
        }
    }

    [HttpGet("me/collections/{id:int}")]
    public async Task<ActionResult<GetCollectionDetailsResponseDto>> GetCollectionDetails([FromRoute] int id)
    {
        try
        {
            return Ok(await _userService.GetCollectionDetailsAsync(GetUserId(), id));
        }
        catch (Exception ex)
        {
            return HandleException(ex);
        }
    }

    [HttpPut("me/collections/{id:int}")]
    [EnableRateLimiting("userActions")]
    public async Task<ActionResult<UpdateCollectionResponseDto>> UpdateCollection([FromRoute] int id, [FromBody] UpdateCollectionRequestDto request)
    {
        try
        {
            return Ok(await _userService.UpdateCollectionAsync(GetUserId(), id, request));
        }
        catch (Exception ex)
        {
            return HandleException(ex);
        }
    }

    [HttpDelete("me/collections/{id:int}")]
    [EnableRateLimiting("userActions")]
    public async Task<ActionResult> DeleteCollection([FromRoute] int id)
    {
        try
        {
            await _userService.DeleteCollectionAsync(GetUserId(), id);
            return NoContent();
        }
        catch (Exception ex)
        {
            return HandleException(ex);
        }
    }

    [HttpPost("me/collections/{id:int}/items")]
    [EnableRateLimiting("userActions")]
    public async Task<ActionResult<AddCollectionItemResponseDto>> AddCollectionItem([FromRoute] int id, [FromBody] AddCollectionItemRequestDto request)
    {
        try
        {
            return Ok(await _userService.AddCollectionItemAsync(GetUserId(), id, request));
        }
        catch (Exception ex)
        {
            return HandleException(ex);
        }
    }

    [HttpDelete("me/collections/{id:int}/items/{itemId:int}")]
    [EnableRateLimiting("userActions")]
    public async Task<ActionResult> DeleteCollectionItem([FromRoute] int id, [FromRoute] int itemId)
    {
        try
        {
            await _userService.DeleteCollectionItemAsync(GetUserId(), id, itemId);
            return NoContent();
        }
        catch (Exception ex)
        {
            return HandleException(ex);
        }
    }

    [HttpPut("~/api/users/{id:int}/role/regional-manager")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> AssignRegionalManagerRole([FromRoute] int id)
    {
        try
        {
            var result = await _userService.AssignRegionalManagerRoleAsync(id);
            return Ok(result);
        }
        catch (Exception ex)
        {
            return HandleException(ex);
        }
    }

    [HttpDelete("~/api/users/{id:int}/role/regional-manager")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> RemoveRegionalManagerRole([FromRoute] int id)
    {
        try
        {
            var result = await _userService.RemoveRegionalManagerRoleAsync(id);
            return Ok(result);
        }
        catch (Exception ex)
        {
            return HandleException(ex);
        }
    }

    [HttpGet("~/api/users/{id:int}/regions")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> GetUserRegions([FromRoute] int id)
    {
        try
        {
            var result = await _userService.GetUserRegionsAsync(id);
            return Ok(result);
        }
        catch (Exception ex)
        {
            return HandleException(ex);
        }
    }

    [HttpPut("~/api/users/{id:int}/regions")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> UpdateUserRegions([FromRoute] int id, [FromBody] UpdateUserRegionsRequestDto request)
    {
        try
        {
            var result = await _userService.UpdateUserRegionsAsync(id, request);
            return Ok(result);
        }
        catch (Exception ex)
        {
            return HandleException(ex);
        }
    }

    [HttpPost("~/api/users/{id:int}/regions/{regionId:int}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> AddUserRegion([FromRoute] int id, [FromRoute] int regionId)
    {
        try
        {
            var result = await _userService.AddUserRegionAsync(id, regionId);
            return Ok(result);
        }
        catch (Exception ex)
        {
            return HandleException(ex);
        }
    }

    [HttpDelete("~/api/users/{id:int}/regions/{regionId:int}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> DeleteUserRegion([FromRoute] int id, [FromRoute] int regionId)
    {
        try
        {
            var result = await _userService.DeleteUserRegionAsync(id, regionId);
            return Ok(result);
        }
        catch (Exception ex)
        {
            return HandleException(ex);
        }
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
            UploadValidationException upload => StatusCode(upload.StatusCode, new { message = upload.Message }),
            UnauthorizedAccessException => Unauthorized(new { message = ex.Message }),
            KeyNotFoundException => NotFound(new { message = ex.Message }),
            InvalidOperationException => BadRequest(new { message = ex.Message }),
            _ => StatusCode(500, new { message = "An unexpected error occurred." })
        };
    }
}
