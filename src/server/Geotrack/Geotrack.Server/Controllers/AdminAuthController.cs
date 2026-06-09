using System.Security.Claims;
using Geotrack.Application.DTOs.Admin;
using Geotrack.Application.DTOs.Auth;
using Geotrack.Application.Exceptions;
using Geotrack.Application.Interfaces.Service;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Geotrack.Server.Controllers;

[ApiController]
[Route("api/admin/auth")]
[EnableRateLimiting("auth")]
public class AdminAuthController : ControllerBase
{
    private readonly IAuthService _authService;

    public AdminAuthController(IAuthService authService)
    {
        _authService = authService;
    }

    [HttpPost("login")]
    public async Task<ActionResult<AdminLoginResponseDto>> Login([FromBody] LoginRequestDto request)
    {
        try
        {
            var response = await _authService.LoginAdminAsync(request);
            WriteRefreshTokenCookie(response.RefreshToken, response.SessionPersistent);

            return Ok(Sanitize(response));
        }
        catch (ForbiddenAccessException ex)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { message = ex.Message });
        }
        catch (UnauthorizedAccessException ex) when (ex.Message == "Email and password are required.")
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (UnauthorizedAccessException)
        {
            return Unauthorized(new { message = "Invalid email or password." });
        }
    }

    [HttpPost("refresh")]
    public async Task<ActionResult<AdminLoginResponseDto>> Refresh([FromBody] RefreshRequestDto? request)
    {
        try
        {
            var response = await _authService.RefreshAdminAsync(new RefreshRequestDto
            {
                RefreshToken = ResolveRefreshToken(request?.RefreshToken)
            });
            WriteRefreshTokenCookie(response.RefreshToken, response.SessionPersistent);

            return Ok(Sanitize(response));
        }
        catch (ForbiddenAccessException ex)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { message = ex.Message });
        }
        catch (UnauthorizedAccessException)
        {
            DeleteRefreshTokenCookie();
            return Unauthorized(new { message = "Invalid or expired admin session." });
        }
    }

    [Authorize(Policy = "RequireAdminAppUser")]
    [HttpGet("me")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<ActionResult<AdminAuthUserDto>> Me()
    {
        try
        {
            return Ok(await _authService.GetAdminUserAsync(GetCurrentUserId()));
        }
        catch (ForbiddenAccessException ex)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { message = ex.Message });
        }
        catch (UnauthorizedAccessException)
        {
            return Unauthorized(new { message = "Invalid admin session." });
        }
    }

    [HttpPost("logout")]
    public async Task<IActionResult> Logout([FromBody] LogoutRequestDto? request)
    {
        await _authService.LogoutAsync(new LogoutRequestDto
        {
            RefreshToken = ResolveRefreshToken(request?.RefreshToken)
        });
        DeleteRefreshTokenCookie();

        return NoContent();
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

    private string ResolveRefreshToken(string? requestRefreshToken)
    {
        return AuthRefreshCookie.Resolve(Request, requestRefreshToken);
    }

    private void WriteRefreshTokenCookie(string refreshToken, bool isPersistent)
    {
        AuthRefreshCookie.Append(HttpContext, refreshToken, isPersistent);
    }

    private void DeleteRefreshTokenCookie()
    {
        AuthRefreshCookie.DeleteAll(HttpContext);
    }

    private static AdminLoginResponseDto Sanitize(AdminLoginResponseDto response)
    {
        response.RefreshToken = string.Empty;
        return response;
    }
}
