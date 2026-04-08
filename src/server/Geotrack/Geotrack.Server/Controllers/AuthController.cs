using Geotrack.Application.DTOs;
using Geotrack.Application.Interfaces;
using Geotrack.Server.Requests;
using Geotrack.Server.Responses;
using Microsoft.AspNetCore.Mvc;

namespace Geotrack.Server.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly IAuthService _authService;

    public AuthController(IAuthService authService)
    {
        _authService = authService;
    }

    [HttpPost("register")]
    public async Task<ActionResult> Register([FromBody] RegisterRequest request)
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
            return Ok(new { message = "User registered successfully" });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("login")]
    public async Task<ActionResult<AuthResponse>> Login([FromBody] LoginRequest request)
    {
        try
        {
            var dto = new LoginUserDto
            {
                Username = request.Username,
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