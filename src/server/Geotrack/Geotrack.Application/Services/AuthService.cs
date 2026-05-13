using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Geotrack.Application.DTOs.Admin;
using Geotrack.Application.DTOs.Auth;
using Geotrack.Application.Exceptions;
using Geotrack.Application.Interfaces.Repository;
using Geotrack.Application.Interfaces.Service;
using Geotrack.Application.Security;
using Geotrack.Domain.Entities.Users;
using Microsoft.AspNetCore.Identity;

namespace Geotrack.Application.Services;

public class AuthService : IAuthService
{
    private const int PersistentRefreshTokenLifetimeDays  = 7;
    private const int SessionOnlyRefreshTokenLifetimeDays = 1;
    private const string InvalidCredentialsMessage = "Invalid email or password.";
    private const string AdminAccessDeniedMessage = "You do not have access to the admin app.";

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
        var normalizedEmail = (request.Email ?? string.Empty).Trim().ToLowerInvariant();

        if (string.IsNullOrWhiteSpace(request.FullName) ||
            string.IsNullOrWhiteSpace(request.Email) ||
            string.IsNullOrWhiteSpace(request.Password))
            throw new InvalidOperationException("All fields are required");

        if (!IsValidEmail(normalizedEmail))
            throw new InvalidOperationException("Invalid email format");

        if (!IsStrongPassword(request.Password))
            throw new InvalidOperationException("Password must be at least 8 characters long and include a number and a special character.");

        var exists = await _userRepository.ExistsByEmailAsync(normalizedEmail);
        if (exists)
            throw new InvalidOperationException("An account with this email already exists.");

        var nameParts = request.FullName.Trim().Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        var firstName = nameParts.Length > 0 ? nameParts[0] : string.Empty;
        var lastName  = nameParts.Length > 1 ? nameParts[1] : string.Empty;

        var user = new User
        {
            Username                 = normalizedEmail,
            Email                    = normalizedEmail,
            FirstName                = firstName,
            LastName                 = lastName,
            PhoneNumber              = string.Empty,
            NeedsInterestsOnboarding = true,
            CreatedAt                = DateTime.UtcNow
        };

        user.PasswordHash = _passwordHasher.HashPassword(user, request.Password);

        await _userRepository.AddAsync(user);
        await _userRepository.SaveChangesAsync();

        return new RegisterResponseDto { Message = "User registered successfully" };
    }

    public async Task<LoginResponseDto> LoginAsync(LoginRequestDto request)
    {
        var user = await AuthenticateUserAsync(
            request,
            missingFieldsMessage: "Email and password are required",
            invalidCredentialsMessage: "Invalid credentials");
        return await IssueTokenPairAsync(user.Id, request.RememberMe);
    }

    public async Task<RefreshResponseDto> RefreshAsync(RefreshRequestDto request)
    {
        var pair = await RotateRefreshTokenAsync(request);

        return new RefreshResponseDto
        {
            AccessToken  = pair.AccessToken,
            RefreshToken = pair.RefreshToken,
            ExpiresIn    = pair.ExpiresIn,
            SessionPersistent = pair.SessionPersistent
        };
    }

    public async Task LogoutAsync(LogoutRequestDto request)
    {
        if (string.IsNullOrWhiteSpace(request.RefreshToken))
            return; // Nothing to revoke.

        var stored = await _refreshTokenRepository.GetActiveByTokenHashAsync(HashToken(request.RefreshToken));
        if (stored == null)
            return; // Already expired / revoked — nothing to do.

        stored.IsRevoked = true;
        stored.RevokedAt = DateTime.UtcNow;
        await _refreshTokenRepository.SaveChangesAsync();
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    public async Task<AdminLoginResponseDto> LoginRegionalManagerAsync(LoginRequestDto request)
    {
        var user = await AuthenticateUserAsync(
            request,
            missingFieldsMessage: "Email and password are required.",
            invalidCredentialsMessage: InvalidCredentialsMessage);

        var adminUser = await GetRegionalAdminUserAsync(user.Id);
        var pair = await IssueTokenPairAsync(user.Id, request.RememberMe);

        return ToAdminLoginResponse(pair, adminUser);
    }

    public async Task<AdminLoginResponseDto> RefreshRegionalManagerAsync(RefreshRequestDto request)
    {
        var rotated = await RotateRefreshTokenWithUserAsync(request, RoleNames.RegionalManager);
        var adminUser = await GetRegionalAdminUserAsync(rotated.UserId);

        return ToAdminLoginResponse(rotated.Pair, adminUser);
    }

    public async Task<AdminAuthUserDto> GetRegionalAdminUserAsync(int userId)
    {
        var user = await _userRepository.GetByIdWithRolesAsync(userId)
            ?? throw new UnauthorizedAccessException(InvalidCredentialsMessage);

        var roles = NormalizeRoles(user.UserRoles.Select(userRole => userRole.Role.Name));
        if (!roles.Contains(RoleNames.RegionalManager, StringComparer.Ordinal))
            throw new ForbiddenAccessException(AdminAccessDeniedMessage);

        return new AdminAuthUserDto
        {
            Id = user.Id,
            Email = user.Email,
            FirstName = user.FirstName,
            LastName = user.LastName,
            DisplayName = BuildDisplayName(user),
            AvatarUrl = user.AvatarUrl,
            Roles = roles,
            CanAccessRegionalAdmin = true
        };
    }

    private async Task<User> AuthenticateUserAsync(
        LoginRequestDto request,
        string missingFieldsMessage,
        string invalidCredentialsMessage)
    {
        var normalizedEmail = (request.Email ?? string.Empty).Trim().ToLowerInvariant();

        if (string.IsNullOrWhiteSpace(request.Email) ||
            string.IsNullOrWhiteSpace(request.Password))
            throw new UnauthorizedAccessException(missingFieldsMessage);

        var user = await _userRepository.GetByEmailAsync(normalizedEmail);
        if (user == null)
            throw new UnauthorizedAccessException(invalidCredentialsMessage);

        var result = _passwordHasher.VerifyHashedPassword(user, user.PasswordHash, request.Password);
        if (result == PasswordVerificationResult.Failed)
            throw new UnauthorizedAccessException(invalidCredentialsMessage);

        return user;
    }

    private async Task<LoginResponseDto> RotateRefreshTokenAsync(
        RefreshRequestDto request,
        string? requiredRole = null)
    {
        return (await RotateRefreshTokenWithUserAsync(request, requiredRole)).Pair;
    }

    private async Task<(LoginResponseDto Pair, int UserId)> RotateRefreshTokenWithUserAsync(
        RefreshRequestDto request,
        string? requiredRole = null)
    {
        if (string.IsNullOrWhiteSpace(request.RefreshToken))
            throw new UnauthorizedAccessException("Refresh token is required");

        var stored = await _refreshTokenRepository.GetActiveByTokenHashAsync(HashToken(request.RefreshToken));
        if (stored == null)
            throw new UnauthorizedAccessException("Invalid or expired refresh token");

        stored.IsRevoked = true;
        stored.RevokedAt = DateTime.UtcNow;
        await _refreshTokenRepository.SaveChangesAsync();

        if (!string.IsNullOrWhiteSpace(requiredRole))
        {
            var user = await _userRepository.GetByIdWithRolesAsync(stored.UserId)
                ?? throw new UnauthorizedAccessException("Invalid or expired refresh token");
            var roles = NormalizeRoles(user.UserRoles.Select(userRole => userRole.Role.Name));

            if (!roles.Contains(RoleNames.Normalize(requiredRole), StringComparer.Ordinal))
                throw new ForbiddenAccessException(AdminAccessDeniedMessage);
        }

        var pair = await IssueTokenPairAsync(stored.UserId, stored.IsPersistent);
        return (pair, stored.UserId);
    }

    private static AdminLoginResponseDto ToAdminLoginResponse(LoginResponseDto pair, AdminAuthUserDto user)
    {
        return new AdminLoginResponseDto
        {
            AccessToken = pair.AccessToken,
            RefreshToken = pair.RefreshToken,
            ExpiresIn = pair.ExpiresIn,
            ExpiresAt = DateTime.UtcNow.AddSeconds(pair.ExpiresIn),
            SessionPersistent = pair.SessionPersistent,
            User = user
        };
    }

    private async Task<LoginResponseDto> IssueTokenPairAsync(int userId, bool isPersistent)
    {
        var user = await _userRepository.GetByIdWithRolesAsync(userId)
            ?? throw new UnauthorizedAccessException("Invalid credentials");
        var roles = NormalizeRoles(user.UserRoles.Select(userRole => userRole.Role.Name));

        var accessToken  = _jwtService.GenerateAccessToken(userId, roles);
        var refreshValue = _jwtService.GenerateRefreshToken();

        var lifetimeDays = isPersistent
            ? PersistentRefreshTokenLifetimeDays
            : SessionOnlyRefreshTokenLifetimeDays;

        var refreshToken = new RefreshToken
        {
            UserId       = userId,
            TokenHash    = HashToken(refreshValue),
            IsPersistent = isPersistent,
            ExpiresAt    = DateTime.UtcNow.AddDays(lifetimeDays),
            IsRevoked    = false,
            CreatedAt    = DateTime.UtcNow
        };

        await _refreshTokenRepository.AddAsync(refreshToken);
        await _refreshTokenRepository.SaveChangesAsync();

        return new LoginResponseDto
        {
            AccessToken  = accessToken,
            RefreshToken = refreshValue,
            ExpiresIn    = 900,
            SessionPersistent = isPersistent
        };
    }

    /// <summary>
    /// SHA-256 hex digest. No per-row salt — 64-byte CSPRNG refresh tokens
    /// already carry enough entropy that rainbow-table / brute-force attacks
    /// are infeasible, and the refresh path should not pay a password-KDF
    /// cost on every request.
    /// </summary>
    private static string HashToken(string raw)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw))).ToLowerInvariant();

    private static List<string> NormalizeRoles(IEnumerable<string?> roles)
    {
        return roles
            .Select(RoleNames.Normalize)
            .Where(role => !string.IsNullOrWhiteSpace(role))
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    private static string BuildDisplayName(User user)
    {
        if (!string.IsNullOrWhiteSpace(user.DisplayName))
            return user.DisplayName.Trim();

        var fullName = string.Join(" ", new[] { user.FirstName, user.LastName }
            .Where(part => !string.IsNullOrWhiteSpace(part))
            .Select(part => part!.Trim()));

        if (!string.IsNullOrWhiteSpace(fullName))
            return fullName;

        return string.IsNullOrWhiteSpace(user.Username)
            ? user.Email
            : user.Username;
    }

    private static bool IsValidEmail(string email)
        => Regex.IsMatch(email, @"^[^@\s]+@[^@\s]+\.[^@\s]+$");

    private static bool IsStrongPassword(string password)
    {
        return password.Length >= 8 &&
               password.Any(char.IsDigit) &&
               Regex.IsMatch(password, @"[!@#$%^&*(),.?""{}|<>]");
    }
}
