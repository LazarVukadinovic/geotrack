using Geotrack.Application.DTOs.Notifications;
using Geotrack.Application.Interfaces.Service;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Geotrack.Server.Controllers;

[ApiController]
[Route("api/notifications")]
[Authorize]
public class NotificationsController : ControllerBase
{
    private readonly IUserNotificationService _notificationService;

    public NotificationsController(IUserNotificationService notificationService)
    {
        _notificationService = notificationService;
    }

    [HttpGet]
    public async Task<ActionResult<GetUserNotificationsResponseDto>> GetMine(
        [FromQuery] int skip = 0,
        [FromQuery] int take = 25)
    {
        try
        {
            return Ok(await _notificationService.GetMineAsync(GetUserId(), skip, take));
        }
        catch (Exception ex)
        {
            return HandleException(ex);
        }
    }

    [HttpGet("unread-count")]
    public async Task<ActionResult<UserNotificationUnreadCountDto>> GetUnreadCount()
    {
        try
        {
            return Ok(await _notificationService.GetUnreadCountAsync(GetUserId()));
        }
        catch (Exception ex)
        {
            return HandleException(ex);
        }
    }

    [HttpPut("{id:int}/read")]
    [HttpPost("{id:int}/read")]
    public async Task<IActionResult> MarkRead([FromRoute] int id)
    {
        try
        {
            await _notificationService.MarkReadAsync(GetUserId(), id);
            return NoContent();
        }
        catch (Exception ex)
        {
            return HandleException(ex);
        }
    }

    [HttpPut("read-all")]
    [HttpPost("mark-all-read")]
    public async Task<IActionResult> MarkAllRead()
    {
        try
        {
            await _notificationService.MarkAllReadAsync(GetUserId());
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
            _ => StatusCode(500, new { message = "An unexpected error occurred." })
        };
    }
}
