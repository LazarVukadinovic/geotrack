using System.Security.Claims;
using Geotrack.Application.DTOs.Admin;
using Geotrack.Application.DTOs.Auth;
using Geotrack.Application.Exceptions;
using Geotrack.Application.Interfaces.Service;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using HeaderNames = Microsoft.Net.Http.Headers.HeaderNames;

namespace Geotrack.Server.Controllers;

[ApiController]
[Route("api/admin/auth")]
[EnableRateLimiting("auth")]
public class AdminAuthController : ControllerBase
{
    private const string RefreshTokenCookieName = "geotrack_admin_refresh";
    private const string RefreshTokenCookiePath = "/api/admin/auth";
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
            var response = await _authService.LoginRegionalManagerAsync(request);
            WriteRefreshTokenCookie(response.RefreshToken, response.SessionPersistent);

            return Ok(response);
        }
        catch (ForbiddenAccessException ex)
        {
            DeleteRefreshTokenCookie();
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
            var response = await _authService.RefreshRegionalManagerAsync(new RefreshRequestDto
            {
                RefreshToken = ResolveRefreshToken(request?.RefreshToken)
            });
            WriteRefreshTokenCookie(response.RefreshToken, response.SessionPersistent);

            return Ok(response);
        }
        catch (ForbiddenAccessException ex)
        {
            DeleteRefreshTokenCookie();
            return StatusCode(StatusCodes.Status403Forbidden, new { message = ex.Message });
        }
        catch (UnauthorizedAccessException)
        {
            DeleteRefreshTokenCookie();
            return Unauthorized(new { message = "Invalid or expired admin session." });
        }
    }

    [Authorize(Policy = "RequireRegionalManager")]
    [HttpGet("me")]
    public async Task<ActionResult<AdminAuthUserDto>> Me()
    {
        try
        {
            return Ok(await _authService.GetRegionalAdminUserAsync(GetCurrentUserId()));
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
        var cookieToken = Request.Cookies[RefreshTokenCookieName];
        if (!string.IsNullOrWhiteSpace(cookieToken))
            return cookieToken;

        return requestRefreshToken?.Trim() ?? string.Empty;
    }

    private void WriteRefreshTokenCookie(string refreshToken, bool isPersistent)
    {
        var options = BuildRefreshCookieOptions();

        if (isPersistent)
        {
            options.Expires = DateTimeOffset.UtcNow.AddDays(7);
            options.MaxAge = TimeSpan.FromDays(7);
        }

        Response.Cookies.Append(RefreshTokenCookieName, refreshToken, options);
        Response.Headers.Append(HeaderNames.CacheControl, "no-store");
    }

    private void DeleteRefreshTokenCookie()
    {
        Response.Cookies.Delete(RefreshTokenCookieName, BuildRefreshCookieOptions());
        Response.Headers.Append(HeaderNames.CacheControl, "no-store");
    }

    private CookieOptions BuildRefreshCookieOptions()
    {
        return new CookieOptions
        {
            HttpOnly = true,
            IsEssential = true,
            Path = RefreshTokenCookiePath,
            SameSite = Microsoft.AspNetCore.Http.SameSiteMode.Lax,
            Secure = !HttpContext.Request.IsHttps
                ? !HttpContext.Request.Host.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
                : true
        };
    }
}
