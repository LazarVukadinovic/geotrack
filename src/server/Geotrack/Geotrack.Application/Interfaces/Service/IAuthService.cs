using Geotrack.Application.DTOs.Auth;
using Geotrack.Application.DTOs.Admin;

namespace Geotrack.Application.Interfaces.Service;

public interface IAuthService
{
    Task<RegisterResponseDto> RegisterAsync(RegisterRequestDto request);
    Task<LoginResponseDto> LoginAsync(LoginRequestDto request);
    Task<RefreshResponseDto> RefreshAsync(RefreshRequestDto request);
    Task LogoutAsync(LogoutRequestDto request);
    Task<AdminLoginResponseDto> LoginRegionalManagerAsync(LoginRequestDto request);
    Task<AdminLoginResponseDto> RefreshRegionalManagerAsync(RefreshRequestDto request);
    Task<AdminAuthUserDto> GetRegionalAdminUserAsync(int userId);
}
