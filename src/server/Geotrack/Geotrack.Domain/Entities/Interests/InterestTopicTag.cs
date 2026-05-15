using Geotrack.Domain.Entities.Tags;

namespace Geotrack.Domain.Entities.Interests;

/// <summary>
/// Weighted mapping from a user-facing interest topic to content tags.
/// A topic can map to multiple tags and a tag can support multiple topics.
/// </summary>
public class InterestTopicTag
{
    public int InterestTopicId { get; set; }
    public InterestTopic InterestTopic { get; set; } = null!;

    public int TagId { get; set; }
    public Tag Tag { get; set; } = null!;

    public double Weight { get; set; } = 1.0;
}
