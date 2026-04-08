using Geotrack.Application.DTOs;

namespace Geotrack.Application.Interfaces;

public interface IAuthService
{
    Task RegisterAsync(RegisterUserDto request);
    Task<string> LoginAsync(LoginUserDto request);
}