namespace Geotrack.Application.Interfaces;

public interface IJwtService
{
    string GenerateToken(int userId);
}