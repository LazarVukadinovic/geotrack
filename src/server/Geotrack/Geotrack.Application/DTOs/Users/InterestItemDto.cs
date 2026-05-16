using System.Text.Json.Serialization;
using Geotrack.Application.DTOs.Interests;

namespace Geotrack.Application.DTOs.Users;

public class InterestItemDto
{
    public int InterestTopicId { get; set; }

    // Backwards-compatible alias for older clients that used tagId for selected interests.
    public int TagId
    {
        get => InterestTopicId;
        set => InterestTopicId = value;
    }

    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string IconKey { get; set; } = string.Empty;
    public string Group { get; set; } = string.Empty;
    public int SortOrder { get; set; }

    public List<InterestTopicTagDto> Tags { get; set; } = new();

    // Legacy tag-scoping fields kept only so old frontend code does not break when deserializing.
    public bool ForDestination { get; set; }
    public bool ForLocation { get; set; }
    public bool ForPlace { get; set; }
    public bool ForEvent { get; set; }

    public int? TagTypeId { get; set; }
    public string? TagTypeName { get; set; }
    public string? TagTypeSlug { get; set; }

    public List<int> AllowedPlaceTypeIds { get; set; } = new();

    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}
