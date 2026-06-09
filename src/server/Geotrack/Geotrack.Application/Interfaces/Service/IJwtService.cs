namespace Geotrack.Application.Interfaces.Service;

public interface IJwtService
{
    /// <summary>Configured JWT access token lifetime in seconds.</summary>
    int AccessTokenLifetimeSeconds { get; }

    /// <summary>Generates a short-lived JWT access token.</summary>
    string GenerateAccessToken(int userId, IEnumerable<string> roles);

    /// <summary>Generates a cryptographically-random opaque refresh token string.</summary>
    string GenerateRefreshToken();
}
