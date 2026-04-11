namespace Geotrack.Application.Interfaces.Service;

public interface IJwtService
{
    string GenerateToken(int userId);
}