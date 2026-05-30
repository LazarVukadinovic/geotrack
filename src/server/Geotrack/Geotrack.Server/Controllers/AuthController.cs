using Geotrack.Application.DTOs.Auth;
using Geotrack.Application.Exceptions;
using Geotrack.Application.Interfaces.Service;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using System.Security.Claims;

namespace Geotrack.Server.Controllers;

[ApiController]
[Route("api/auth")]
[EnableRateLimiting("auth")]
public class AuthController : ControllerBase
{
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

    [HttpGet("session")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<ActionResult<AuthSessionDto>> Session()
    {
        try
        {
            if (User.Identity?.IsAuthenticated == true)
            {
                return Ok(await _authService.GetSessionForUserAsync(GetCurrentUserId()));
            }

            return Ok(await _authService.GetSessionAsync(new RefreshRequestDto
            {
                RefreshToken = ResolveRefreshToken(null)
            }));
        }
        catch (UnauthorizedAccessException ex)
        {
            return Unauthorized(new { message = ex.Message });
        }
        catch (ForbiddenAccessException ex)
        {
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

    private int GetCurrentUserId()
    {
        var value = User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirstValue("sub")
            ?? User.FindFirstValue("userId");

        return int.TryParse(value, out var userId)
            ? userId
            : throw new UnauthorizedAccessException("Invalid user session.");
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
