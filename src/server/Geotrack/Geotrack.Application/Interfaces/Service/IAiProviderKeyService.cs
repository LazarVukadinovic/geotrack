using Geotrack.Application.DTOs.Agent;

namespace Geotrack.Application.Interfaces.Service;

public interface IAiProviderKeyService
{
    Task<IReadOnlyList<AgentApiKeyMetadataDto>> GetKeysAsync(int userId, CancellationToken cancellationToken = default);
    Task<AgentApiKeyMetadataDto?> GetKeyMetadataAsync(int userId, string provider, CancellationToken cancellationToken = default);
    Task<string?> GetDecryptedKeyAsync(int userId, string provider, CancellationToken cancellationToken = default);
    Task<AgentApiKeyMetadataDto> SaveOrReplaceKeyAsync(int userId, string provider, string apiKey, string? displayName, CancellationToken cancellationToken = default);
    Task DeleteKeyAsync(int userId, string provider, CancellationToken cancellationToken = default);
    Task MarkKeyUsedAsync(int userId, string provider, bool success, CancellationToken cancellationToken = default);
}
