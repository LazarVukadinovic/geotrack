using Geotrack.Domain.Entities.Users;

namespace Geotrack.Domain.Entities.Interests;

/// <summary>
/// Explicit interest topic selected by the user.
/// </summary>
public class UserInterestTopic
{
    public int UserId { get; set; }
    public User User { get; set; } = null!;

    public int InterestTopicId { get; set; }
    public InterestTopic InterestTopic { get; set; } = null!;

    public DateTime CreatedAt { get; set; }
}
