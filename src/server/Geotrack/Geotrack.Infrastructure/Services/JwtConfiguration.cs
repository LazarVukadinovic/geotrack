using System.Text;
using Microsoft.Extensions.Configuration;

namespace Geotrack.Infrastructure.Services;

public static class JwtConfiguration
{
    public static string RequireSigningKey(IConfiguration configuration)
    {
        var key = configuration["Jwt:Key"];
        if (string.IsNullOrWhiteSpace(key) || Encoding.UTF8.GetByteCount(key) < 32 ||
            key.Distinct().Count() < 10 || key.Contains('<') || key.Contains('>'))
            throw new InvalidOperationException("Jwt:Key requires securely supplied signing material of at least 32 UTF-8 bytes; placeholders are not allowed.");
        return key;
    }

    public static string RequireValue(IConfiguration configuration, string name) =>
        !string.IsNullOrWhiteSpace(configuration[name])
            ? configuration[name]!
            : throw new InvalidOperationException($"{name} is required.");
}
