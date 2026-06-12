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
    public async Task<ActionResult<RegisterResponseDto>> Register([FromBody] RegisterRequestDto request)
    {
        try
        {
            var response = await _authService.RegisterAsync(request);
            DeleteRefreshTokenCookie();
            return Ok(response);
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

    [HttpPost("verify-email")]
    public async Task<ActionResult<AuthActionResponseDto>> VerifyEmail([FromBody] VerifyEmailRequestDto request)
    {
        try
        {
            var response = await _authService.VerifyEmailAsync(request);
            if (!string.IsNullOrWhiteSpace(response.RefreshToken))
                WriteRefreshTokenCookie(response.RefreshToken, response.SessionPersistent);

            return Ok(Sanitize(response));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("resend-verification")]
    public async Task<ActionResult<AuthMessageResponseDto>> ResendEmailVerification([FromBody] ResendEmailVerificationRequestDto request)
    {
        return Ok(await _authService.ResendEmailVerificationAsync(request));
    }

    [HttpPost("forgot-password")]
    public async Task<ActionResult<AuthMessageResponseDto>> ForgotPassword([FromBody] ForgotPasswordRequestDto request)
    {
        return Ok(await _authService.RequestPasswordResetAsync(request));
    }

    [HttpPost("reset-password")]
    public async Task<ActionResult<AuthActionResponseDto>> ResetPassword([FromBody] ResetPasswordRequestDto request)
    {
        try
        {
            var response = await _authService.ResetPasswordAsync(request);
            if (!string.IsNullOrWhiteSpace(response.RefreshToken))
                WriteRefreshTokenCookie(response.RefreshToken, response.SessionPersistent);

            return Ok(Sanitize(response));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
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

    private static AuthActionResponseDto Sanitize(AuthActionResponseDto response)
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
