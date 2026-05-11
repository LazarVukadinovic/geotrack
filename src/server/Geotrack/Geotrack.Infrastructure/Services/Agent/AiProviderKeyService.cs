using Geotrack.Application.DTOs.Agent;
using Geotrack.Application.Interfaces.Service;
using Geotrack.Domain.Entities.Users;
using Microsoft.EntityFrameworkCore;

namespace Geotrack.Infrastructure.Services.Agent;

public class AiProviderKeyService : IAiProviderKeyService
{
    private const int MaxApiKeyLength = 2048;
    private readonly AppDbContext _db;
    private readonly IApiKeyEncryptionService _encryptionService;

    public AiProviderKeyService(AppDbContext db, IApiKeyEncryptionService encryptionService)
    {
        _db = db;
        _encryptionService = encryptionService;
    }

    public async Task<IReadOnlyList<AgentApiKeyMetadataDto>> GetKeysAsync(
        int userId,
        CancellationToken cancellationToken = default)
    {
        return await _db.UserAiProviderKeys
            .AsNoTracking()
            .Where(key => key.UserId == userId && key.IsActive && key.RevokedAt == null)
            .OrderBy(key => key.Provider)
            .Select(key => ToMetadata(key))
            .ToListAsync(cancellationToken);
    }

    public async Task<AgentApiKeyMetadataDto?> GetKeyMetadataAsync(
        int userId,
        string provider,
        CancellationToken cancellationToken = default)
    {
        var normalizedProvider = NormalizeProvider(provider);
        var key = await FindActiveKeyAsync(userId, normalizedProvider, tracking: false, cancellationToken);
        return key is null ? null : ToMetadata(key);
    }

    public async Task<string?> GetDecryptedKeyAsync(
        int userId,
        string provider,
        CancellationToken cancellationToken = default)
    {
        var normalizedProvider = NormalizeProvider(provider);
        var key = await FindActiveKeyAsync(userId, normalizedProvider, tracking: false, cancellationToken);
        return key is null ? null : _encryptionService.Unprotect(key.EncryptedApiKey);
    }

    public async Task<AgentApiKeyMetadataDto> SaveOrReplaceKeyAsync(
        int userId,
        string provider,
        string apiKey,
        string? displayName,
        CancellationToken cancellationToken = default)
    {
        var normalizedProvider = NormalizeProvider(provider);
        var normalizedKey = NormalizeApiKey(apiKey);
        var now = DateTime.UtcNow;
        var existing = await FindActiveKeyAsync(userId, normalizedProvider, tracking: true, cancellationToken);

        if (existing is null)
        {
            existing = new UserAiProviderKey
            {
                UserId = userId,
                Provider = normalizedProvider,
                CreatedAt = now
            };
            await _db.UserAiProviderKeys.AddAsync(existing, cancellationToken);
        }

        existing.EncryptedApiKey = _encryptionService.Protect(normalizedKey);
        existing.KeyLast4 = Last4(normalizedKey);
        existing.DisplayName = NormalizeDisplayName(displayName);
        existing.UpdatedAt = now;
        existing.RevokedAt = null;
        existing.IsActive = true;
        existing.FailedUseCount = 0;

        await _db.SaveChangesAsync(cancellationToken);
        return ToMetadata(existing);
    }

    public async Task DeleteKeyAsync(int userId, string provider, CancellationToken cancellationToken = default)
    {
        var normalizedProvider = NormalizeProvider(provider);
        var key = await FindActiveKeyAsync(userId, normalizedProvider, tracking: true, cancellationToken);
        if (key is null)
        {
            return;
        }

        _db.UserAiProviderKeys.Remove(key);
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task MarkKeyUsedAsync(
        int userId,
        string provider,
        bool success,
        CancellationToken cancellationToken = default)
    {
        var normalizedProvider = NormalizeProvider(provider);
        var key = await FindActiveKeyAsync(userId, normalizedProvider, tracking: true, cancellationToken);
        if (key is null)
        {
            return;
        }

        key.LastUsedAt = DateTime.UtcNow;
        key.FailedUseCount = success ? 0 : key.FailedUseCount + 1;
        await _db.SaveChangesAsync(cancellationToken);
    }

    private Task<UserAiProviderKey?> FindActiveKeyAsync(
        int userId,
        string provider,
        bool tracking,
        CancellationToken cancellationToken)
    {
        var query = _db.UserAiProviderKeys
            .Where(key =>
                key.UserId == userId &&
                key.Provider == provider &&
                key.IsActive &&
                key.RevokedAt == null);

        if (!tracking)
        {
            query = query.AsNoTracking();
        }

        return query.SingleOrDefaultAsync(cancellationToken);
    }

    private static string NormalizeProvider(string provider)
    {
        if (string.IsNullOrWhiteSpace(provider))
        {
            throw new ArgumentException("Provider is required.", nameof(provider));
        }

        var normalized = provider.Trim().ToLowerInvariant();
        if (normalized.Length > 40 || normalized.Any(ch => !char.IsLetterOrDigit(ch) && ch != '-' && ch != '_'))
        {
            throw new ArgumentException("Provider is invalid.", nameof(provider));
        }

        return normalized;
    }

    private static string NormalizeApiKey(string apiKey)
    {
        var normalized = apiKey?.Trim() ?? string.Empty;
        if (normalized.Length < 8 || normalized.Length > MaxApiKeyLength)
        {
            throw new ArgumentException("API key length is invalid.", nameof(apiKey));
        }

        if (normalized.Any(char.IsControl))
        {
            throw new ArgumentException("API key contains unsupported characters.", nameof(apiKey));
        }

        return normalized;
    }

    private static string? NormalizeDisplayName(string? displayName)
    {
        var normalized = displayName?.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return null;
        }

        return normalized.Length <= 120 ? normalized : normalized[..120];
    }

    private static string Last4(string apiKey) =>
        apiKey.Length <= 4 ? apiKey : apiKey[^4..];

    private static AgentApiKeyMetadataDto ToMetadata(UserAiProviderKey key) => new()
    {
        Provider = key.Provider,
        KeyLast4 = key.KeyLast4,
        DisplayName = key.DisplayName,
        CreatedAt = key.CreatedAt,
        UpdatedAt = key.UpdatedAt,
        LastUsedAt = key.LastUsedAt
    };
}
