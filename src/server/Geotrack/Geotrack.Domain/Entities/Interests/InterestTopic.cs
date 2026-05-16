using Geotrack.Domain.Entities.Users;

namespace Geotrack.Domain.Entities.Interests;

/// <summary>
/// User-facing interest users select during onboarding/settings.
/// It is intentionally separate from Tag: tags describe content, while
/// interest topics describe a user's preference and map to one or more tags.
/// </summary>
public class InterestTopic
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string IconKey { get; set; } = string.Empty;
    public string Group { get; set; } = string.Empty;

    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
    public bool ShowOnOnboarding { get; set; } = true;

    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }

    public ICollection<InterestTopicTag> TopicTags { get; set; } = new List<InterestTopicTag>();
    public ICollection<UserInterestTopic> UserInterests { get; set; } = new List<UserInterestTopic>();
    public ICollection<UserInterestTopicSignal> UserSignals { get; set; } = new List<UserInterestTopicSignal>();
}
