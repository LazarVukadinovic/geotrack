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
    private const int EmailVerificationTokenLifetimeHours = 24;
    private const int PasswordResetTokenLifetimeMinutes = 30;
    private const int EmailVerificationResendCooldownSeconds = 60;
    private const int PasswordResetRequestCooldownSeconds = 60;
    private const string InvalidCredentialsMessage = "Invalid email or password.";
    private const string AdminAccessDeniedMessage = "You do not have access to the admin app.";

    private readonly IUserRepository _userRepository;
    private readonly IRefreshTokenRepository _refreshTokenRepository;
    private readonly IJwtService _jwtService;
    private readonly IAuthEmailService _authEmailService;
    private readonly PasswordHasher<User> _passwordHasher = new();

    public AuthService(
        IUserRepository userRepository,
        IRefreshTokenRepository refreshTokenRepository,
        IJwtService jwtService,
        IAuthEmailService authEmailService)
    {
        _userRepository = userRepository;
        _refreshTokenRepository = refreshTokenRepository;
        _jwtService = jwtService;
        _authEmailService = authEmailService;
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
        var verificationToken = GenerateUrlSafeToken();
        var now = DateTime.UtcNow;

        var user = new User
        {
            Username                         = normalizedEmail,
            Email                            = normalizedEmail,
            FirstName                        = firstName,
            LastName                         = lastName,
            PhoneNumber                      = string.Empty,
            NeedsInterestsOnboarding         = true,
            IsEmailConfirmed                 = false,
            EmailVerificationTokenHash       = HashToken(verificationToken),
            EmailVerificationTokenExpiresAt  = now.AddHours(EmailVerificationTokenLifetimeHours),
            EmailVerificationSentAt          = now,
            CreatedAt                        = now
        };

        user.PasswordHash = _passwordHasher.HashPassword(user, request.Password);

        await _userRepository.AddAsync(user);
        await _userRepository.SaveChangesAsync();
        await _authEmailService.SendEmailVerificationAsync(user, verificationToken);

        return new RegisterResponseDto
        {
            Message = "Registration successful. Please check your email to verify your account before signing in."
        };
    }

    public async Task<AuthActionResponseDto> VerifyEmailAsync(VerifyEmailRequestDto request)
    {
        var normalizedEmail = (request.Email ?? string.Empty).Trim().ToLowerInvariant();
        var token = request.Token?.Trim() ?? string.Empty;

        if (!IsValidEmail(normalizedEmail) || string.IsNullOrWhiteSpace(token))
            throw new InvalidOperationException("Invalid verification link.");

        var user = await _userRepository.GetByEmailAsync(normalizedEmail);
        if (user == null || user.IsDeleted)
            throw new InvalidOperationException("Invalid verification link.");

        if (user.IsEmailConfirmed)
        {
            return new AuthActionResponseDto { Message = "Email address is already verified. You can sign in." };
        }

        if (user.EmailVerificationTokenExpiresAt == null ||
            user.EmailVerificationTokenExpiresAt < DateTime.UtcNow ||
            !TokenMatches(user.EmailVerificationTokenHash, token))
        {
            throw new InvalidOperationException("Verification link is invalid or expired.");
        }

        user.IsEmailConfirmed = true;
        user.EmailConfirmedAt = DateTime.UtcNow;
        user.EmailVerificationTokenHash = null;
        user.EmailVerificationTokenExpiresAt = null;
        user.EmailVerificationSentAt = null;
        MarkSuccessfulLogin(user);

        await _userRepository.SaveChangesAsync();

        var auth = await IssueTokenPairAsync(user.Id, isPersistent: false);
        return ToAuthActionResponse("Email verified successfully. Signing you in...", auth);
    }

    public async Task<AuthMessageResponseDto> ResendEmailVerificationAsync(ResendEmailVerificationRequestDto request)
    {
        var normalizedEmail = (request.Email ?? string.Empty).Trim().ToLowerInvariant();

        if (!IsValidEmail(normalizedEmail))
        {
            return new AuthMessageResponseDto
            {
                Message = "If this account needs verification, a new verification email will be sent."
            };
        }

        var user = await _userRepository.GetByEmailAsync(normalizedEmail);
        if (user is { IsDeleted: false, IsEmailConfirmed: false })
        {
            var now = DateTime.UtcNow;
            var retryAfterSeconds = GetRetryAfterSeconds(user.EmailVerificationSentAt, EmailVerificationResendCooldownSeconds, now);
            if (retryAfterSeconds > 0)
            {
                return new AuthMessageResponseDto
                {
                    Message = $"A verification email was sent recently. Please wait {retryAfterSeconds} seconds before requesting another one.",
                    RetryAfterSeconds = retryAfterSeconds
                };
            }

            var token = GenerateUrlSafeToken();
            user.EmailVerificationTokenHash = HashToken(token);
            user.EmailVerificationTokenExpiresAt = now.AddHours(EmailVerificationTokenLifetimeHours);
            user.EmailVerificationSentAt = now;
            user.UpdatedAt = now;

            await _userRepository.SaveChangesAsync();
            await _authEmailService.SendEmailVerificationAsync(user, token);
        }

        return new AuthMessageResponseDto
        {
            Message = "If this account needs verification, a new verification email will be sent."
        };
    }

    public async Task<AuthMessageResponseDto> RequestPasswordResetAsync(ForgotPasswordRequestDto request)
    {
        var normalizedEmail = (request.Email ?? string.Empty).Trim().ToLowerInvariant();
        var genericResponse = new AuthMessageResponseDto
        {
            Message = "If an account with this email exists, a password reset link has been sent."
        };

        if (!IsValidEmail(normalizedEmail))
            return genericResponse;

        var user = await _userRepository.GetByEmailAsync(normalizedEmail);
        if (user is null || user.IsDeleted || user.IsSuspended)
            return genericResponse;

        var now = DateTime.UtcNow;
        var retryAfterSeconds = GetRetryAfterSeconds(user.PasswordResetRequestedAt, PasswordResetRequestCooldownSeconds, now);
        if (retryAfterSeconds > 0)
        {
            genericResponse.RetryAfterSeconds = retryAfterSeconds;
            return genericResponse;
        }

        var token = GenerateUrlSafeToken();
        user.PasswordResetTokenHash = HashToken(token);
        user.PasswordResetTokenExpiresAt = now.AddMinutes(PasswordResetTokenLifetimeMinutes);
        user.PasswordResetRequestedAt = now;
        user.UpdatedAt = now;

        await _userRepository.SaveChangesAsync();
        await _authEmailService.SendPasswordResetAsync(user, token);

        return genericResponse;
    }

    public async Task<AuthActionResponseDto> ResetPasswordAsync(ResetPasswordRequestDto request)
    {
        var normalizedEmail = (request.Email ?? string.Empty).Trim().ToLowerInvariant();
        var token = request.Token?.Trim() ?? string.Empty;

        if (!IsValidEmail(normalizedEmail) || string.IsNullOrWhiteSpace(token))
            throw new InvalidOperationException("Invalid or expired password reset link.");

        if (!IsStrongPassword(request.NewPassword))
            throw new InvalidOperationException("Password must be at least 8 characters long and include a number and a special character.");

        var user = await _userRepository.GetByEmailAsync(normalizedEmail);
        if (user == null || user.IsDeleted || user.IsSuspended)
            throw new InvalidOperationException("Invalid or expired password reset link.");

        if (user.PasswordResetTokenExpiresAt == null ||
            user.PasswordResetTokenExpiresAt < DateTime.UtcNow ||
            !TokenMatches(user.PasswordResetTokenHash, token))
        {
            throw new InvalidOperationException("Invalid or expired password reset link.");
        }

        user.PasswordHash = _passwordHasher.HashPassword(user, request.NewPassword);
        user.PasswordResetTokenHash = null;
        user.PasswordResetTokenExpiresAt = null;
        user.PasswordResetRequestedAt = null;

        // A successful email-link reset proves the user controls the mailbox.
        if (!user.IsEmailConfirmed)
        {
            user.IsEmailConfirmed = true;
            user.EmailConfirmedAt = DateTime.UtcNow;
            user.EmailVerificationTokenHash = null;
            user.EmailVerificationTokenExpiresAt = null;
            user.EmailVerificationSentAt = null;
        }

        MarkSuccessfulLogin(user);
        await _userRepository.SaveChangesAsync();
        await _refreshTokenRepository.RevokeActiveForUserAsync(user.Id, DateTime.UtcNow);

        var auth = await IssueTokenPairAsync(user.Id, isPersistent: false);
        return ToAuthActionResponse("Password has been reset successfully. Signing you in...", auth);
    }

    public async Task<LoginResponseDto> LoginAsync(LoginRequestDto request)
    {
        var user = await AuthenticateUserAsync(
            request,
            missingFieldsMessage: "Email and password are required",
            invalidCredentialsMessage: "Invalid credentials");
        MarkSuccessfulLogin(user);
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
            SessionPersistent = pair.SessionPersistent,
            Session = pair.Session
        };
    }

    public async Task<AuthSessionDto> GetSessionAsync(RefreshRequestDto request)
    {
        if (string.IsNullOrWhiteSpace(request.RefreshToken))
            throw new UnauthorizedAccessException("Refresh token is required");

        var stored = await _refreshTokenRepository.GetActiveByTokenHashAsync(HashToken(request.RefreshToken));
        if (stored == null)
            throw new UnauthorizedAccessException("Invalid or expired refresh token");

        var user = await _userRepository.GetByIdWithRolesAsync(stored.UserId)
            ?? throw new UnauthorizedAccessException("Invalid or expired refresh token");

        EnsureAccountCanAuthenticate(user);
        return BuildSessionDto(user, stored.IsPersistent, stored.ExpiresAt);
    }

    public async Task<AuthSessionDto> GetSessionForUserAsync(int userId)
    {
        var user = await _userRepository.GetByIdWithRolesAsync(userId)
            ?? throw new UnauthorizedAccessException("Invalid user session.");

        EnsureAccountCanAuthenticate(user);
        return BuildSessionDto(user, sessionPersistent: false, sessionExpiresAt: null);
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

    public async Task<AdminLoginResponseDto> LoginAdminAsync(LoginRequestDto request)
    {
        var user = await AuthenticateUserAsync(
            request,
            missingFieldsMessage: "Email and password are required.",
            invalidCredentialsMessage: InvalidCredentialsMessage);

        var adminUser = await GetAdminUserAsync(user.Id);
        MarkSuccessfulLogin(user);
        var pair = await IssueTokenPairAsync(user.Id, request.RememberMe);

        return ToAdminLoginResponse(pair, adminUser);
    }

    public async Task<AdminLoginResponseDto> LoginRegionalManagerAsync(LoginRequestDto request)
    {
        var user = await AuthenticateUserAsync(
            request,
            missingFieldsMessage: "Email and password are required.",
            invalidCredentialsMessage: InvalidCredentialsMessage);

        var adminUser = await GetRegionalAdminUserAsync(user.Id);
        MarkSuccessfulLogin(user);
        var pair = await IssueTokenPairAsync(user.Id, request.RememberMe);

        return ToAdminLoginResponse(pair, adminUser);
    }

    public async Task<AdminLoginResponseDto> RefreshAdminAsync(RefreshRequestDto request)
    {
        var rotated = await RotateRefreshTokenWithUserAsync(request, RoleNames.AdminAppRoles);
        var adminUser = await GetAdminUserAsync(rotated.UserId);

        return ToAdminLoginResponse(rotated.Pair, adminUser);
    }

    public async Task<AdminLoginResponseDto> RefreshRegionalManagerAsync(RefreshRequestDto request)
    {
        var rotated = await RotateRefreshTokenWithUserAsync(request, [RoleNames.RegionalManager]);
        var adminUser = await GetRegionalAdminUserAsync(rotated.UserId);

        return ToAdminLoginResponse(rotated.Pair, adminUser);
    }

    public async Task<AdminAuthUserDto> GetAdminUserAsync(int userId)
    {
        return await GetAdminUserAsync(userId, RoleNames.AdminAppRoles);
    }

    public async Task<AdminAuthUserDto> GetRegionalAdminUserAsync(int userId)
    {
        return await GetAdminUserAsync(userId, [RoleNames.RegionalManager]);
    }

    private async Task<AdminAuthUserDto> GetAdminUserAsync(int userId, IReadOnlyCollection<string> requiredRoles)
    {
        var user = await _userRepository.GetByIdWithRolesAsync(userId)
            ?? throw new UnauthorizedAccessException(InvalidCredentialsMessage);

        EnsureAccountCanAuthenticate(user);

        var roles = NormalizeRoles(user.UserRoles.Select(userRole => userRole.Role.Name));
        if (!requiredRoles.Any(requiredRole => roles.Contains(RoleNames.Normalize(requiredRole), StringComparer.Ordinal)))
            throw new ForbiddenAccessException(AdminAccessDeniedMessage);

        return new AdminAuthUserDto
        {
            Id = user.Id,
            Email = user.Email,
            FirstName = user.FirstName,
            LastName = user.LastName,
            DisplayName = BuildDisplayName(user),
            AvatarUrl = user.AvatarUrl,
            AvatarVersion = AvatarVersionBuilder.From(user.AvatarUrl, user.UpdatedAt, user.CreatedAt),
            Roles = roles,
            CanAccessAdminApp = RoleNames.CanAccessAdminApp(roles),
            CanAccessContentModeration = roles.Contains(RoleNames.ContentModerator, StringComparer.Ordinal),
            CanAccessRegionalAdmin = roles.Contains(RoleNames.RegionalManager, StringComparer.Ordinal),
            CanAccessTerritoryAdmin =
                roles.Contains(RoleNames.TerritoryManager, StringComparer.Ordinal) ||
                roles.Contains(RoleNames.TerritoryAssociate, StringComparer.Ordinal)
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
        if (user == null || user.IsDeleted)
            throw new UnauthorizedAccessException(invalidCredentialsMessage);

        var result = _passwordHasher.VerifyHashedPassword(user, user.PasswordHash, request.Password);
        if (result == PasswordVerificationResult.Failed)
            throw new UnauthorizedAccessException(invalidCredentialsMessage);

        EnsureAccountCanAuthenticate(user);
        return user;
    }

    private async Task<LoginResponseDto> RotateRefreshTokenAsync(
        RefreshRequestDto request,
        string? requiredRole = null)
    {
        IReadOnlyCollection<string>? requiredRoles = string.IsNullOrWhiteSpace(requiredRole)
            ? null
            : [requiredRole];

        return (await RotateRefreshTokenWithUserAsync(request, requiredRoles)).Pair;
    }

    private async Task<(LoginResponseDto Pair, int UserId)> RotateRefreshTokenWithUserAsync(
        RefreshRequestDto request,
        IReadOnlyCollection<string>? requiredRoles = null)
    {
        if (string.IsNullOrWhiteSpace(request.RefreshToken))
            throw new UnauthorizedAccessException("Refresh token is required");

        var stored = await _refreshTokenRepository.GetActiveByTokenHashAsync(HashToken(request.RefreshToken));
        if (stored == null)
            throw new UnauthorizedAccessException("Invalid or expired refresh token");

        var user = await _userRepository.GetByIdWithRolesAsync(stored.UserId)
            ?? throw new UnauthorizedAccessException("Invalid or expired refresh token");
        EnsureAccountCanAuthenticate(user);

        if (requiredRoles is { Count: > 0 })
        {
            var roles = NormalizeRoles(user.UserRoles.Select(userRole => userRole.Role.Name));

            if (!requiredRoles.Any(requiredRole => roles.Contains(RoleNames.Normalize(requiredRole), StringComparer.Ordinal)))
                throw new ForbiddenAccessException(AdminAccessDeniedMessage);
        }

        var pair = await IssueTokenPairAsync(stored.UserId, stored.IsPersistent);
        stored.IsRevoked = true;
        stored.RevokedAt = DateTime.UtcNow;
        await _refreshTokenRepository.SaveChangesAsync();

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
        EnsureAccountCanAuthenticate(user);
        var roles = NormalizeRoles(user.UserRoles.Select(userRole => userRole.Role.Name));

        var accessToken  = _jwtService.GenerateAccessToken(userId, roles);
        var refreshValue = _jwtService.GenerateRefreshToken();

        var lifetimeDays = isPersistent
            ? PersistentRefreshTokenLifetimeDays
            : SessionOnlyRefreshTokenLifetimeDays;
        var expiresAt = DateTime.UtcNow.AddDays(lifetimeDays);

        var refreshToken = new RefreshToken
        {
            UserId       = userId,
            TokenHash    = HashToken(refreshValue),
            IsPersistent = isPersistent,
            ExpiresAt    = expiresAt,
            IsRevoked    = false,
            CreatedAt    = DateTime.UtcNow
        };

        await _refreshTokenRepository.AddAsync(refreshToken);
        await _refreshTokenRepository.SaveChangesAsync();

        return new LoginResponseDto
        {
            AccessToken  = accessToken,
            RefreshToken = refreshValue,
            ExpiresIn    = _jwtService.AccessTokenLifetimeSeconds,
            SessionPersistent = isPersistent,
            Session = BuildSessionDto(user, isPersistent, expiresAt)
        };
    }

    private static AuthSessionDto BuildSessionDto(
        User user,
        bool sessionPersistent,
        DateTime? sessionExpiresAt)
    {
        var roles = NormalizeRoles(user.UserRoles.Select(userRole => userRole.Role.Name));
        var adminRoleType = ResolvePrimaryAdminRole(roles);

        return new AuthSessionDto
        {
            UserId = user.Id,
            Email = user.Email,
            DisplayName = BuildDisplayName(user),
            AvatarUrl = user.AvatarUrl,
            AvatarVersion = AvatarVersionBuilder.From(user.AvatarUrl, user.UpdatedAt, user.CreatedAt),
            Roles = roles,
            IsAuthenticated = true,
            CanAccessAdminApp = adminRoleType is not null,
            AdminRoleType = adminRoleType,
            AdminLandingRoute = adminRoleType is null ? null : ResolveAdminLandingRoute(adminRoleType),
            UserAppLandingRoute = "/profile",
            SessionPersistent = sessionPersistent,
            SessionExpiresAt = sessionExpiresAt
        };
    }

    private static string? ResolvePrimaryAdminRole(IReadOnlyCollection<string> roles)
    {
        if (roles.Contains(RoleNames.Admin, StringComparer.Ordinal))
            return RoleNames.Admin;

        if (roles.Contains(RoleNames.ContentModerator, StringComparer.Ordinal))
            return RoleNames.ContentModerator;

        if (roles.Contains(RoleNames.TerritoryManager, StringComparer.Ordinal))
            return RoleNames.TerritoryManager;

        if (roles.Contains(RoleNames.TerritoryAssociate, StringComparer.Ordinal))
            return RoleNames.TerritoryAssociate;

        if (roles.Contains(RoleNames.RegionalManager, StringComparer.Ordinal))
            return RoleNames.RegionalManager;

        return null;
    }

    private static string ResolveAdminLandingRoute(string role)
    {
        return role switch
        {
            RoleNames.Admin => "/admin-dashboard",
            RoleNames.ContentModerator => "/content-moderator/dashboard",
            RoleNames.TerritoryManager => "/territory/overview",
            RoleNames.TerritoryAssociate => "/territory/overview",
            RoleNames.RegionalManager => "/dashboard",
            _ => "/login"
        };
    }


    private static AuthActionResponseDto ToAuthActionResponse(string message, LoginResponseDto auth)
    {
        return new AuthActionResponseDto
        {
            Message = message,
            AccessToken = auth.AccessToken,
            RefreshToken = auth.RefreshToken,
            ExpiresIn = auth.ExpiresIn,
            SessionPersistent = auth.SessionPersistent,
            Session = auth.Session
        };
    }

    private static int GetRetryAfterSeconds(DateTime? lastSentAt, int cooldownSeconds, DateTime now)
    {
        if (lastSentAt == null)
            return 0;

        var allowedAt = lastSentAt.Value.AddSeconds(cooldownSeconds);
        if (allowedAt <= now)
            return 0;

        return Math.Max(1, (int)Math.Ceiling((allowedAt - now).TotalSeconds));
    }

    /// <summary>
    /// SHA-256 hex digest. No per-row salt — 64-byte CSPRNG refresh tokens
    /// already carry enough entropy that rainbow-table / brute-force attacks
    /// are infeasible, and the refresh path should not pay a password-KDF
    /// cost on every request.
    /// </summary>
    private static string HashToken(string raw)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw))).ToLowerInvariant();

    private static string GenerateUrlSafeToken()
    {
        var tokenBytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToBase64String(tokenBytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private static bool TokenMatches(string? storedHash, string rawToken)
    {
        if (string.IsNullOrWhiteSpace(storedHash) || string.IsNullOrWhiteSpace(rawToken))
            return false;

        var expected = Encoding.UTF8.GetBytes(storedHash);
        var actual = Encoding.UTF8.GetBytes(HashToken(rawToken));

        return expected.Length == actual.Length && CryptographicOperations.FixedTimeEquals(expected, actual);
    }

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

    private static void MarkSuccessfulLogin(User user)
    {
        user.LastLoginAt = DateTime.UtcNow;
        user.UpdatedAt = DateTime.UtcNow;
    }

    private static void EnsureAccountCanAuthenticate(User user)
    {
        if (user.IsDeleted)
            throw new UnauthorizedAccessException(InvalidCredentialsMessage);

        if (user.IsSuspended)
            throw new ForbiddenAccessException("Account is suspended.");

        if (!user.IsEmailConfirmed)
            throw new UnauthorizedAccessException("Please verify your email address before signing in.");
    }

    private static bool IsValidEmail(string email)
        => Regex.IsMatch(email, @"^[^@\s]+@[^@\s]+\.[^@\s]+$");

    private static bool IsStrongPassword(string? password)
    {
        return !string.IsNullOrEmpty(password) &&
               password.Length >= 8 &&
               password.Any(char.IsDigit) &&
               Regex.IsMatch(password, @"[!@#$%^&*(),.?""{}|<>]");
    }
}
