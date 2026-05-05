using Geotrack.Domain.Entities.Tags;
using Geotrack.Domain.Entities.Users;

namespace Geotrack.Domain.Entities.Analytics;

public class UserInterestSignal
{
    public int Id { get; set; }

    public int UserId { get; set; }
    public User User { get; set; } = null!;

    public int TagId { get; set; }
    public Tag Tag { get; set; } = null!;

    public double Score { get; set; }

    public string Source { get; set; } = "inferred";

    public DateTime LastInteractionAt { get; set; }
    public string? LastReason { get; set; }

    public bool IsDismissed { get; set; }
    public DateTime? DismissedAt { get; set; }
    public double? DismissedAtScore { get; set; }
    public double EvidenceSinceDismissal { get; set; }
}
