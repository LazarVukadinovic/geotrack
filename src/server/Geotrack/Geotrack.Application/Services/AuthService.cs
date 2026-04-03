using Geotrack.Application.Interfaces;
using Geotrack.Domain.Entities;
using Geotrack.Server.Requests;
using System.Security.Cryptography;
using System.Text;

namespace Geotrack.Application.Services;

public class AuthService
{
    private readonly IUserRepository _userRepository;
    private readonly IJwtService _jwtService;

    public AuthService(IUserRepository userRepository, IJwtService jwtService)
    {
        _userRepository = userRepository;
        _jwtService = jwtService;
    }

    public async Task Register(RegisterRequest request)
    {
        var exists = await _userRepository.ExistsAsync(request.Username, request.Email);

        if (exists)
            throw new Exception("User already exists");

        var user = new User
        {
            Username = request.Username,
            Email = request.Email,
            PasswordHash = HashPassword(request.Password),
            CreatedAt = DateTime.UtcNow,
            IsActive = true
        };

        await _userRepository.AddAsync(user);
    }

    public async Task<string> Login(LoginRequest request)
    {
        var user = await _userRepository.GetByUsernameAsync(request.Username);

        if (user == null || user.PasswordHash != HashPassword(request.Password))
            throw new Exception("Invalid credentials");

        return _jwtService.GenerateToken(user.Id);
    }

    private string HashPassword(string password)
    {
        using var sha256 = SHA256.Create();
        var bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(password));
        return Convert.ToBase64String(bytes);
    }
}