using Geotrack.Application.DTOs.Tags;
using Geotrack.Application.DTOs.Users;
using Geotrack.Application.Interfaces.Service;
using Geotrack.Server.Requests;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Geotrack.Server.Controllers;

[ApiController]
[Route("api/user")]
[Authorize]
public class UserController : ControllerBase
{
    private const long MaxAvatarFileSizeBytes = 5 * 1024 * 1024;
    private readonly IUserService _userService;

    public UserController(IUserService userService)
    {
        _userService = userService;
    }

    [HttpGet("me")]
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
    public async Task<ActionResult<UpdateAvatarResponseDto>> UpdateAvatar([FromForm] UpdateAvatarRequest request)
    {
        try
        {
            var avatar = request.Avatar;

            if (avatar is null || avatar.Length == 0)
                return BadRequest(new { message = "Please select an image to upload." });

            if (avatar.Length > MaxAvatarFileSizeBytes)
                return BadRequest(new { message = "Profile image must be 5 MB or smaller." });

            await using var memoryStream = new MemoryStream();
            await avatar.CopyToAsync(memoryStream);

            return Ok(await _userService.UpdateAvatarAsync(GetUserId(), memoryStream.ToArray()));
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
