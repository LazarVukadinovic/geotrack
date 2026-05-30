using Geotrack.Application.DTOs.Auth;
using Geotrack.Application.DTOs.Admin;

namespace Geotrack.Application.Interfaces.Service;

public interface IAuthService
{
    Task<RegisterResponseDto> RegisterAsync(RegisterRequestDto request);
    Task<LoginResponseDto> LoginAsync(LoginRequestDto request);
    Task<RefreshResponseDto> RefreshAsync(RefreshRequestDto request);
    Task<AuthSessionDto> GetSessionAsync(RefreshRequestDto request);
    Task<AuthSessionDto> GetSessionForUserAsync(int userId);
    Task LogoutAsync(LogoutRequestDto request);
    Task<AdminLoginResponseDto> LoginAdminAsync(LoginRequestDto request);
    Task<AdminLoginResponseDto> RefreshAdminAsync(RefreshRequestDto request);
    Task<AdminAuthUserDto> GetAdminUserAsync(int userId);
    Task<AdminLoginResponseDto> LoginRegionalManagerAsync(LoginRequestDto request);
    Task<AdminLoginResponseDto> RefreshRegionalManagerAsync(RefreshRequestDto request);
    Task<AdminAuthUserDto> GetRegionalAdminUserAsync(int userId);
}
