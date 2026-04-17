using System.Text.RegularExpressions;
using Geotrack.Application.DTOs.Auth;
using Geotrack.Application.Interfaces.Repository;
using Geotrack.Application.Interfaces.Service;
using Geotrack.Domain.Entities.Users;
using Microsoft.AspNetCore.Identity;

namespace Geotrack.Application.Services;

public class AuthService : IAuthService
{
    private const int RefreshTokenLifetimeDays = 7;

    private readonly IUserRepository _userRepository;
    private readonly IRefreshTokenRepository _refreshTokenRepository;
    private readonly IJwtService _jwtService;
    private readonly PasswordHasher<User> _passwordHasher = new();

    public AuthService(
        IUserRepository userRepository,
        IRefreshTokenRepository refreshTokenRepository,
        IJwtService jwtService)
    {
        _userRepository = userRepository;
        _refreshTokenRepository = refreshTokenRepository;
        _jwtService = jwtService;
    }

    public async Task<RegisterResponseDto> RegisterAsync(RegisterRequestDto request)
    {
        if (string.IsNullOrWhiteSpace(request.FullName) ||
            string.IsNullOrWhiteSpace(request.Email) ||
            string.IsNullOrWhiteSpace(request.Password))
            throw new InvalidOperationException("All fields are required");

        if (!IsValidEmail(request.Email))
            throw new InvalidOperationException("Invalid email format");

        var exists = await _userRepository.ExistsByEmailAsync(request.Email.Trim());
        if (exists)
            throw new InvalidOperationException("User already exists");

        var nameParts = request.FullName.Trim().Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        var firstName = nameParts.Length > 0 ? nameParts[0] : string.Empty;
        var lastName  = nameParts.Length > 1 ? nameParts[1] : string.Empty;

        var user = new User
        {
            Username    = request.Email.Trim(),
            Email       = request.Email.Trim(),
            FirstName   = firstName,
            LastName    = lastName,
            PhoneNumber = string.Empty,
            CreatedAt   = DateTime.UtcNow
        };

        user.PasswordHash = _passwordHasher.HashPassword(user, request.Password);

        await _userRepository.AddAsync(user);
        await _userRepository.SaveChangesAsync();

        return new RegisterResponseDto { Message = "User registered successfully" };
    }

    public async Task<LoginResponseDto> LoginAsync(LoginRequestDto request)
    {
        if (string.IsNullOrWhiteSpace(request.Email) ||
            string.IsNullOrWhiteSpace(request.Password))
            throw new UnauthorizedAccessException("Email and password are required");

        var user = await _userRepository.GetByEmailAsync(request.Email.Trim());
        if (user == null)
            throw new UnauthorizedAccessException("Invalid credentials");

        var result = _passwordHasher.VerifyHashedPassword(user, user.PasswordHash, request.Password);
        if (result == PasswordVerificationResult.Failed)
            throw new UnauthorizedAccessException("Invalid credentials");

        return await IssueTokenPairAsync(user.Id);
    }

    public async Task<RefreshResponseDto> RefreshAsync(RefreshRequestDto request)
    {
        if (string.IsNullOrWhiteSpace(request.RefreshToken))
            throw new UnauthorizedAccessException("Refresh token is required");

        var stored = await _refreshTokenRepository.GetActiveByTokenAsync(request.RefreshToken);
        if (stored == null)
            throw new UnauthorizedAccessException("Invalid or expired refresh token");

        // Revoke the consumed token (rotation — each refresh token is single-use).
        stored.IsRevoked  = true;
        stored.RevokedAt  = DateTime.UtcNow;
        await _refreshTokenRepository.SaveChangesAsync();

        // Issue a fresh pair — sliding window resets here.
        var pair = await IssueTokenPairAsync(stored.UserId);

        return new RefreshResponseDto
        {
            AccessToken  = pair.AccessToken,
            RefreshToken = pair.RefreshToken,
            ExpiresIn    = pair.ExpiresIn
        };
    }

    public async Task LogoutAsync(LogoutRequestDto request)
    {
        if (string.IsNullOrWhiteSpace(request.RefreshToken))
            return; // Nothing to revoke.

        var stored = await _refreshTokenRepository.GetActiveByTokenAsync(request.RefreshToken);
        if (stored == null)
            return; // Already expired / revoked — nothing to do.

        stored.IsRevoked = true;
        stored.RevokedAt = DateTime.UtcNow;
        await _refreshTokenRepository.SaveChangesAsync();
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private async Task<LoginResponseDto> IssueTokenPairAsync(int userId)
    {
        var accessToken  = _jwtService.GenerateAccessToken(userId);
        var refreshValue = _jwtService.GenerateRefreshToken();

        var refreshToken = new RefreshToken
        {
            UserId    = userId,
            Token     = refreshValue,
            ExpiresAt = DateTime.UtcNow.AddDays(RefreshTokenLifetimeDays),
            IsRevoked = false,
            CreatedAt = DateTime.UtcNow
        };

        await _refreshTokenRepository.AddAsync(refreshToken);
        await _refreshTokenRepository.SaveChangesAsync();

        return new LoginResponseDto
        {
            AccessToken  = accessToken,
            RefreshToken = refreshValue,
            ExpiresIn    = 900
        };
    }

    private static bool IsValidEmail(string email)
        => Regex.IsMatch(email, @"^[^@\s]+@[^@\s]+\.[^@\s]+$");
}
