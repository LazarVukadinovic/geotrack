using Geotrack.Application.DTOs.Auth;
using Geotrack.Application.Exceptions;
using Geotrack.Application.Interfaces.Service;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using HeaderNames = Microsoft.Net.Http.Headers.HeaderNames;

namespace Geotrack.Server.Controllers;

[ApiController]
[Route("api/auth")]
[EnableRateLimiting("auth")]
public class AuthController : ControllerBase
{
    private const string RefreshTokenCookieName = "geotrack_refresh";
    private readonly IAuthService _authService;

    public AuthController(IAuthService authService)
    {
        _authService = authService;
    }

    [HttpPost("register")]
    public async Task<ActionResult<LoginResponseDto>> Register([FromBody] RegisterRequestDto request)
    {
        try
        {
            await _authService.RegisterAsync(request);

            // Auto-login after registration so the interests page can save tags.
            var loginDto = new LoginRequestDto
            {
                Email      = request.Email,
                Password   = request.Password,
                RememberMe = true
            };
            var loginResponse = await _authService.LoginAsync(loginDto);
            WriteRefreshTokenCookie(loginResponse.RefreshToken, loginResponse.SessionPersistent);

            return Ok(Sanitize(loginResponse));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (ForbiddenAccessException ex)
        {
            DeleteRefreshTokenCookie();
            return StatusCode(StatusCodes.Status403Forbidden, new { message = ex.Message });
        }
        catch (UnauthorizedAccessException ex)
        {
            return Unauthorized(new { message = ex.Message });
        }
    }

    [HttpPost("login")]
    public async Task<ActionResult<LoginResponseDto>> Login([FromBody] LoginRequestDto request)
    {
        try
        {
            var response = await _authService.LoginAsync(request);
            WriteRefreshTokenCookie(response.RefreshToken, response.SessionPersistent);
            return Ok(Sanitize(response));
        }
        catch (UnauthorizedAccessException ex)
        {
            return Unauthorized(new { message = ex.Message });
        }
        catch (ForbiddenAccessException ex)
        {
            DeleteRefreshTokenCookie();
            return StatusCode(StatusCodes.Status403Forbidden, new { message = ex.Message });
        }
    }

    /// <summary>
    /// Exchange a valid refresh token for a new access token + new refresh token.
    /// The supplied refresh token is immediately revoked (rotation).
    /// </summary>
    [HttpPost("refresh")]
    public async Task<ActionResult<RefreshResponseDto>> Refresh([FromBody] RefreshRequestDto? request)
    {
        try
        {
            var refreshRequest = new RefreshRequestDto
            {
                RefreshToken = ResolveRefreshToken(request?.RefreshToken)
            };

            var response = await _authService.RefreshAsync(refreshRequest);
            WriteRefreshTokenCookie(response.RefreshToken, response.SessionPersistent);
            return Ok(Sanitize(response));
        }
        catch (UnauthorizedAccessException ex)
        {
            DeleteRefreshTokenCookie();
            return Unauthorized(new { message = ex.Message });
        }
        catch (ForbiddenAccessException ex)
        {
            DeleteRefreshTokenCookie();
            return StatusCode(StatusCodes.Status403Forbidden, new { message = ex.Message });
        }
    }

    /// <summary>
    /// Revoke a refresh token (manual logout).
    /// Always returns 204 — callers must not rely on the outcome for UX decisions.
    /// </summary>
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

    private string ResolveRefreshToken(string? requestRefreshToken)
    {
        var cookieToken = Request.Cookies[RefreshTokenCookieName];
        if (!string.IsNullOrWhiteSpace(cookieToken))
            return cookieToken;

        return requestRefreshToken?.Trim() ?? string.Empty;
    }

    private void WriteRefreshTokenCookie(string refreshToken, bool isPersistent)
    {
        var options = new CookieOptions
        {
            HttpOnly = true,
            IsEssential = true,
            Path = "/api/auth",
            SameSite = Microsoft.AspNetCore.Http.SameSiteMode.Lax,
            Secure = !HttpContext.Request.IsHttps
                ? !HttpContext.Request.Host.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
                : true
        };

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
        Response.Cookies.Delete(RefreshTokenCookieName, new CookieOptions
        {
            HttpOnly = true,
            IsEssential = true,
            Path = "/api/auth",
            SameSite = Microsoft.AspNetCore.Http.SameSiteMode.Lax,
            Secure = !HttpContext.Request.IsHttps
                ? !HttpContext.Request.Host.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
                : true
        });
        Response.Headers.Append(HeaderNames.CacheControl, "no-store");
    }

    private static LoginResponseDto Sanitize(LoginResponseDto response)
    {
        response.RefreshToken = string.Empty;
        return response;
    }

    private static RefreshResponseDto Sanitize(RefreshResponseDto response)
    {
        response.RefreshToken = string.Empty;
        return response;
    }
}
