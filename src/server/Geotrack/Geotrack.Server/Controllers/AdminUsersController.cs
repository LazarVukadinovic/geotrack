using System.Security.Claims;
using Geotrack.Application.DTOs.Admin;
using Geotrack.Application.Interfaces.Service;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Geotrack.Server.Controllers;

[ApiController]
[Route("api/admin/users")]
[Authorize(Policy = "RequireAdmin")]
public sealed class AdminUsersController : ControllerBase
{
    private readonly IAdminUserManagementService _users;
    private readonly ILogger<AdminUsersController> _logger;

    public AdminUsersController(
        IAdminUserManagementService users,
        ILogger<AdminUsersController> logger)
    {
        _users = users;
        _logger = logger;
    }

    [HttpGet]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<ActionResult<AdminUserListResponseDto>> GetUsers(
        [FromQuery] AdminUserListRequestDto request,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _users.GetUsersAsync(GetCurrentUserId(), request, cancellationToken));
        }
        catch (Exception ex)
        {
            return HandleException(ex);
        }
    }

    [HttpGet("{userId:int}")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<ActionResult<AdminUserListItemDto>> GetUser(
        [FromRoute] int userId,
        CancellationToken cancellationToken)
    {
        if (userId <= 0)
            return BadRequest(new { message = "userId must be positive." });

        try
        {
            return Ok(await _users.GetUserAsync(GetCurrentUserId(), userId, cancellationToken));
        }
        catch (Exception ex)
        {
            return HandleException(ex);
        }
    }

    [HttpGet("{userId:int}/preview")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<ActionResult<AdminUserPreviewDto>> GetUserPreview(
        [FromRoute] int userId,
        CancellationToken cancellationToken)
    {
        if (userId <= 0)
            return BadRequest(new { message = "userId must be positive." });

        try
        {
            return Ok(await _users.GetUserPreviewAsync(GetCurrentUserId(), userId, cancellationToken));
        }
        catch (Exception ex)
        {
            return HandleException(ex);
        }
    }

    [HttpPost("{userId:int}/suspend")]
    public async Task<ActionResult<AdminUserListItemDto>> SuspendUser(
        [FromRoute] int userId,
        CancellationToken cancellationToken)
    {
        if (userId <= 0)
            return BadRequest(new { message = "userId must be positive." });

        try
        {
            return Ok(await _users.SuspendUserAsync(GetCurrentUserId(), userId, cancellationToken));
        }
        catch (Exception ex)
        {
            return HandleException(ex);
        }
    }

    [HttpPost("{userId:int}/unsuspend")]
    public async Task<ActionResult<AdminUserListItemDto>> UnsuspendUser(
        [FromRoute] int userId,
        CancellationToken cancellationToken)
    {
        if (userId <= 0)
            return BadRequest(new { message = "userId must be positive." });

        try
        {
            return Ok(await _users.UnsuspendUserAsync(GetCurrentUserId(), userId, cancellationToken));
        }
        catch (Exception ex)
        {
            return HandleException(ex);
        }
    }

    [HttpDelete("{userId:int}")]
    public async Task<IActionResult> DeleteUser(
        [FromRoute] int userId,
        CancellationToken cancellationToken)
    {
        if (userId <= 0)
            return BadRequest(new { message = "userId must be positive." });

        try
        {
            await _users.DeleteUserAsync(GetCurrentUserId(), userId, cancellationToken);
            return NoContent();
        }
        catch (Exception ex)
        {
            return HandleException(ex);
        }
    }

    [HttpPost("{userId:int}/assign-territory-manager")]
    public async Task<ActionResult<AdminUserListItemDto>> AssignTerritoryManager(
        [FromRoute] int userId,
        [FromBody] AdminUserAssignTerritoryManagerRequestDto? request,
        CancellationToken cancellationToken)
    {
        if (userId <= 0)
            return BadRequest(new { message = "userId must be positive." });

        if (request is null)
            return BadRequest(new { message = "Request body is required." });

        try
        {
            return Ok(await _users.AssignTerritoryManagerAsync(
                GetCurrentUserId(),
                userId,
                request,
                cancellationToken));
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
            "Unexpected exception while handling administrator user request {Method} {Path}.",
            HttpContext.Request.Method,
            HttpContext.Request.Path.Value);

        return StatusCode(StatusCodes.Status500InternalServerError, new { message = "An unexpected error occurred." });
    }
}
