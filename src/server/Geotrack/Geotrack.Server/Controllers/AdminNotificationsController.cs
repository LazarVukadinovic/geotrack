using System.Security.Claims;
using Geotrack.Application.DTOs.Admin;
using Geotrack.Application.Interfaces.Service;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Geotrack.Server.Controllers;

[ApiController]
[Route("api/admin/notifications")]
[Authorize(Policy = "RequireAdminAppUser")]
public sealed class AdminNotificationsController : ControllerBase
{
    private readonly IAdminNotificationService _notifications;
    private readonly ILogger<AdminNotificationsController> _logger;

    public AdminNotificationsController(
        IAdminNotificationService notifications,
        ILogger<AdminNotificationsController> logger)
    {
        _notifications = notifications;
        _logger = logger;
    }

    [HttpGet]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<ActionResult<AdminNotificationListDto>> GetNotifications(
        [FromQuery] AdminNotificationListRequestDto request,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _notifications.GetNotificationsAsync(GetCurrentUserId(), request, cancellationToken));
        }
        catch (Exception ex)
        {
            return HandleException(ex);
        }
    }

    [HttpGet("unread-count")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<ActionResult<AdminNotificationUnreadCountDto>> GetUnreadCount(
        [FromQuery] string? audienceRole,
        [FromQuery] int? regionId,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _notifications.GetUnreadCountAsync(GetCurrentUserId(), audienceRole, regionId, cancellationToken));
        }
        catch (Exception ex)
        {
            return HandleException(ex);
        }
    }

    [HttpPost("{notificationId:int}/read")]
    public async Task<ActionResult<AdminNotificationMutationDto>> MarkRead(
        [FromRoute] int notificationId,
        [FromQuery] string? audienceRole,
        [FromQuery] int? regionId,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _notifications.MarkReadAsync(GetCurrentUserId(), notificationId, audienceRole, regionId, cancellationToken));
        }
        catch (Exception ex)
        {
            return HandleException(ex);
        }
    }

    [HttpPost("mark-all-read")]
    public async Task<ActionResult<AdminNotificationMutationDto>> MarkAllRead(
        [FromQuery] string? audienceRole,
        [FromQuery] int? regionId,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _notifications.MarkAllReadAsync(GetCurrentUserId(), audienceRole, regionId, cancellationToken));
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
            UnauthorizedAccessException => StatusCode(StatusCodes.Status403Forbidden, new { message = "Forbidden." }),
            KeyNotFoundException => NotFound(new { message = ex.Message }),
            InvalidOperationException => BadRequest(new { message = ex.Message }),
            _ => HandleUnexpectedException(ex)
        };
    }

    private ActionResult HandleUnexpectedException(Exception ex)
    {
        _logger.LogError(
            ex,
            "Unexpected exception while handling administrator notification request {Method} {Path}.",
            HttpContext.Request.Method,
            HttpContext.Request.Path.Value);

        return StatusCode(StatusCodes.Status500InternalServerError, new { message = "An unexpected error occurred." });
    }
}
