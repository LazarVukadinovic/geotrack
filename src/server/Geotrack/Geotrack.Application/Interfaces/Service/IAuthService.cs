using Geotrack.Application.DTOs.Auth;

namespace Geotrack.Application.Interfaces.Service;

public interface IAuthService
{
    Task<RegisterResponseDto> RegisterAsync(RegisterRequestDto request);
    Task<LoginResponseDto> LoginAsync(LoginRequestDto request);
    Task<RefreshResponseDto> RefreshAsync(RefreshRequestDto request);
    Task LogoutAsync(LogoutRequestDto request);
}
