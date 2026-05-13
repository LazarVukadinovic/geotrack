namespace Geotrack.Application.Interfaces.Service;

public interface IApiKeyEncryptionService
{
    string Protect(string apiKey);
    string Unprotect(string encryptedApiKey);
}
