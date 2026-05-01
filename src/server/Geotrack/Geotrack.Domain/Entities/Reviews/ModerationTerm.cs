using Geotrack.Domain.Entities.Users;

namespace Geotrack.Domain.Entities.Reviews;

public class ModerationTerm
{
    public int Id { get; set; }
    public string Term { get; set; } = string.Empty;
    public string NormalizedTerm { get; set; } = string.Empty;
    public string LanguageCode { get; set; } = string.Empty;
    public ModerationTermCategory Category { get; set; } = ModerationTermCategory.Profanity;
    public ModerationTermMatchType MatchType { get; set; } = ModerationTermMatchType.Exact;
    public bool IsActive { get; set; } = true;
    public int Severity { get; set; } = 2;
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public int? CreatedByUserId { get; set; }
    public int? UpdatedByUserId { get; set; }

    public User? CreatedByUser { get; set; }
    public User? UpdatedByUser { get; set; }
}
