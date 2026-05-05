namespace Geotrack.Domain.Entities.Users;

public class RefreshToken
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public User User { get; set; } = null!;

    /// <summary>SHA-256 hex digest of the opaque refresh token (64 chars, lowercase).</summary>
    public string TokenHash { get; set; } = string.Empty;

    /// <summary>True when issued with Remember-Me (long lifetime); false for session-only.</summary>
    public bool IsPersistent { get; set; }

    public DateTime ExpiresAt { get; set; }
    public bool IsRevoked { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? RevokedAt { get; set; }
}
