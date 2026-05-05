namespace Geotrack.Application.Interfaces.Service;

public interface IJwtService
{
    /// <summary>Generates a short-lived JWT access token (15 minutes).</summary>
    string GenerateAccessToken(int userId, IEnumerable<string> roles);

    /// <summary>Generates a cryptographically-random opaque refresh token string.</summary>
    string GenerateRefreshToken();
}
