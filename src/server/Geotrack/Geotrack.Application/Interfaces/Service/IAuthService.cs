using Geotrack.Application.DTOs.Auth;

namespace Geotrack.Application.Interfaces.Service;

public interface IAuthService
{
    Task RegisterAsync(RegisterUserDto request);
    Task<string> LoginAsync(LoginUserDto request);
}