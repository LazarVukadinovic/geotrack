using Geotrack.Application.DTOs;
using Geotrack.Application.Interfaces;
using Geotrack.Domain.Entities;
using System.Security.Cryptography;
using System.Text;

namespace Geotrack.Application.Services;

public class AuthService : IAuthService
{
    private readonly IUserRepository _userRepository;
    private readonly IJwtService _jwtService;

    public AuthService(IUserRepository userRepository, IJwtService jwtService)
    {
        _userRepository = userRepository;
        _jwtService = jwtService;
    }

    public async Task RegisterAsync(RegisterUserDto request)
    {
        var exists = await _userRepository.ExistsAsync(request.Username, request.Email);

        if (exists)
            throw new InvalidOperationException("User already exists");

        var user = new User
        {
            Username = request.Username,
            Email = request.Email,
            PasswordHash = HashPassword(request.Password),
            CreatedAt = DateTime.UtcNow,
        };

        await _userRepository.AddAsync(user);
        await _userRepository.SaveChangesAsync();
    }

    public async Task<string> LoginAsync(LoginUserDto request)
    {
        var user = await _userRepository.GetByUsernameAsync(request.Username);

        if (user == null || user.PasswordHash != HashPassword(request.Password))
            throw new UnauthorizedAccessException("Invalid credentials");

        return _jwtService.GenerateToken(user.Id);
    }

    private string HashPassword(string password)
    {
        using var sha256 = SHA256.Create();
        var bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(password));
        return Convert.ToBase64String(bytes);
    }
}