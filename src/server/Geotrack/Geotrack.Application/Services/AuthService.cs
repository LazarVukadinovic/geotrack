using System.Text.RegularExpressions;
using Geotrack.Application.DTOs.Auth;
using Geotrack.Application.Interfaces.Repository;
using Geotrack.Application.Interfaces.Service;
using Geotrack.Domain.Entities.Users;
using Microsoft.AspNetCore.Identity;

namespace Geotrack.Application.Services;

public class AuthService : IAuthService
{
    private readonly IUserRepository _userRepository;
    private readonly IJwtService _jwtService;
    private readonly PasswordHasher<User> _passwordHasher = new();

    public AuthService(IUserRepository userRepository, IJwtService jwtService)
    {
        _userRepository = userRepository;
        _jwtService = jwtService;
    }

    public async Task<RegisterResponseDto> RegisterAsync(RegisterRequestDto request)
    {
        if (string.IsNullOrWhiteSpace(request.Username) ||
            string.IsNullOrWhiteSpace(request.Email) ||
            string.IsNullOrWhiteSpace(request.Password))
            throw new InvalidOperationException("All fields are required");

        if (!IsValidEmail(request.Email))
            throw new InvalidOperationException("Invalid email format");

        var exists = await _userRepository.ExistsAsync(request.Username, request.Email);

        var exists = await _userRepository.ExistsAsync(request.Username.Trim(), request.Email.Trim());
        if (exists)
            throw new InvalidOperationException("User already exists");

        var user = new User
        {
            Username = request.Username.Trim(),
            Email = request.Email.Trim(),
            FirstName = string.Empty,
            LastName = string.Empty,
            PhoneNumber = string.Empty,
            CreatedAt = DateTime.UtcNow
        };

        user.PasswordHash = _passwordHasher.HashPassword(user, request.Password);

        await _userRepository.AddAsync(user);
        await _userRepository.SaveChangesAsync();

        return new RegisterResponseDto
        {
            Message = "User registered successfully"
        };
    }

    public async Task<LoginResponseDto> LoginAsync(LoginRequestDto request)
    {
        if (string.IsNullOrWhiteSpace(request.Email) ||
            string.IsNullOrWhiteSpace(request.Password))
            throw new UnauthorizedAccessException("Email and password are required");

        var user = await _userRepository.GetByEmailAsync(request.Email);

        var user = await _userRepository.GetByUsernameAsync(request.Username.Trim());
        if (user == null)
            throw new UnauthorizedAccessException("Invalid credentials");

        var result = _passwordHasher.VerifyHashedPassword(
            user,
            user.PasswordHash,
            request.Password
        );

        var result = _passwordHasher.VerifyHashedPassword(user, user.PasswordHash, request.Password);
        if (result == PasswordVerificationResult.Failed)
            throw new UnauthorizedAccessException("Invalid credentials");

        return new LoginResponseDto
        {
            Token = _jwtService.GenerateToken(user.Id)
        };
    }

    private static bool IsValidEmail(string email)
    {
        return Regex.IsMatch(email, @"^[^@\s]+@[^@\s]+\.[^@\s]+$");
    }
}