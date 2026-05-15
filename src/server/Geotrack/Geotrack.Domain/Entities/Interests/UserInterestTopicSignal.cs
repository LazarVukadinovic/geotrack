using Geotrack.Domain.Entities.Users;

namespace Geotrack.Domain.Entities.Interests;

/// <summary>
/// Inferred interest topic score built from analytics events.
/// </summary>
public class UserInterestTopicSignal
{
    public int Id { get; set; }

    public int UserId { get; set; }
    public User User { get; set; } = null!;

    public int InterestTopicId { get; set; }
    public InterestTopic InterestTopic { get; set; } = null!;

    public double Score { get; set; }

    public string Source { get; set; } = "inferred";

    public DateTime LastInteractionAt { get; set; }
    public string? LastReason { get; set; }

    public bool IsDismissed { get; set; }
    public DateTime? DismissedAt { get; set; }
    public double? DismissedAtScore { get; set; }
    public double EvidenceSinceDismissal { get; set; }
}
