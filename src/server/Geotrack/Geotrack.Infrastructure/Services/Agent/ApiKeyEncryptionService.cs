using Geotrack.Application.Interfaces.Service;
using Microsoft.AspNetCore.DataProtection;

namespace Geotrack.Infrastructure.Services.Agent;

public class ApiKeyEncryptionService : IApiKeyEncryptionService
{
    private const string Purpose = "Geotrack.AiProviderKeys.v1";
    private readonly IDataProtector _protector;

    public ApiKeyEncryptionService(IDataProtectionProvider dataProtectionProvider)
    {
        _protector = dataProtectionProvider.CreateProtector(Purpose);
    }

    public string Protect(string apiKey)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new ArgumentException("API key is required.", nameof(apiKey));
        }

        return _protector.Protect(apiKey.Trim());
    }

    public string Unprotect(string encryptedApiKey)
    {
        if (string.IsNullOrWhiteSpace(encryptedApiKey))
        {
            throw new ArgumentException("Encrypted API key is required.", nameof(encryptedApiKey));
        }

        return _protector.Unprotect(encryptedApiKey);
    }
}
