using Geotrack.Application.DTOs.Auth;
using Geotrack.Application.Interfaces.Service;
using Microsoft.AspNetCore.Mvc;

namespace Geotrack.Server.Controllers;

[ApiController]
[Route("api/auth")]
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
            var dto = new RegisterUserDto
            {
                Username = request.Username,
                Email = request.Email,
                Password = request.Password
            };

            await _authService.RegisterAsync(dto);

            // Auto-login after registration so the interests page can save tags
            var loginDto = new LoginUserDto
            {
                Email = request.Email,
                Password = request.Password
            };
            var token = await _authService.LoginAsync(loginDto);

            return Ok(new AuthResponse { Token = token });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
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
            var dto = new LoginUserDto
            {
                Email = request.Email,
                Password = request.Password
            };

            var token = await _authService.LoginAsync(dto);

            return Ok(new AuthResponse
            {
                Token = token
            });
        }
        catch (UnauthorizedAccessException ex)
        {
            return Unauthorized(new { message = ex.Message });
        }
    }
}