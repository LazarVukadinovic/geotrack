namespace Geotrack.Domain.Entities.Users;

public class UserAiProviderKey
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public string Provider { get; set; } = string.Empty;
    public string EncryptedApiKey { get; set; } = string.Empty;
    public string KeyLast4 { get; set; } = string.Empty;
    public string? DisplayName { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? LastUsedAt { get; set; }
    public DateTime? RevokedAt { get; set; }
    public bool IsActive { get; set; } = true;
    public int FailedUseCount { get; set; }

    public User User { get; set; } = null!;
}
