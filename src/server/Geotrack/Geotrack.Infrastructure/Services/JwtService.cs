using Geotrack.Application.Interfaces.Service;
using Geotrack.Application.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;

namespace Geotrack.Infrastructure.Services;

public class JwtService : IJwtService
{
    private const int DefaultAccessTokenLifetimeMinutes = 60;

    private readonly IConfiguration _config;
    private readonly int _accessTokenLifetimeMinutes;
    private readonly string _signingKey;

    public JwtService(IConfiguration config)
    {
        _config = config;
        _signingKey = JwtConfiguration.RequireSigningKey(config);
        _ = JwtConfiguration.RequireValue(config, "Jwt:Issuer");
        _ = JwtConfiguration.RequireValue(config, "Jwt:Audience");
        _accessTokenLifetimeMinutes = ResolveAccessTokenLifetimeMinutes(config);
    }

    public int AccessTokenLifetimeSeconds => _accessTokenLifetimeMinutes * 60;

    /// <inheritdoc />
    public string GenerateAccessToken(int userId, IEnumerable<string> roles)
    {
        var key = new SymmetricSecurityKey(
            Encoding.UTF8.GetBytes(_signingKey)
        );

        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new List<Claim>
        {
            new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
            new Claim("userId", userId.ToString())
        };

        claims.AddRange(roles
            .Select(RoleNames.Normalize)
            .Where(role => !string.IsNullOrWhiteSpace(role))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(role => new Claim(ClaimTypes.Role, role)));

        var token = new JwtSecurityToken(
            issuer: _config["Jwt:Issuer"],
            audience: _config["Jwt:Audience"],
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(_accessTokenLifetimeMinutes),
            signingCredentials: creds
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    /// <inheritdoc />
    public string GenerateRefreshToken()
    {
        // 64 cryptographically-random bytes → 128-char hex string.
        var bytes = RandomNumberGenerator.GetBytes(64);
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static int ResolveAccessTokenLifetimeMinutes(IConfiguration config)
    {
        var configured = config.GetValue<int?>("Jwt:AccessTokenMinutes")
            ?? DefaultAccessTokenLifetimeMinutes;

        return configured > 0
            ? configured
            : DefaultAccessTokenLifetimeMinutes;
    }
}
