namespace Geotrack.Domain.Entities.Tags;

public class Tag
{
    public int Id { get; set; }

    public string Name { get; set; } = null!;
    public string Slug { get; set; } = null!;
    public string Description { get; set; } = string.Empty;
    public string? ImageUrl { get; set; }

    public bool ForDestination { get; set; }
    public bool ForLocation { get; set; }
    public bool ForPlace { get; set; }
    public bool ForEvent { get; set; }
    public bool ForRegion { get; set; }
    public bool ForTerritory { get; set; }

    public bool CanGenerateExploreSection { get; set; }

    public int? TagTypeId { get; set; }
    public TagType? TagType { get; set; }

    public ICollection<DestinationTag> DestinationTags { get; set; } = new List<DestinationTag>();
    public ICollection<LocationTag> LocationTags { get; set; } = new List<LocationTag>();
    public ICollection<PlaceTag> PlaceTags { get; set; } = new List<PlaceTag>();
    public ICollection<EventTag> EventTags { get; set; } = new List<EventTag>();
    public ICollection<UserTag> UserTags { get; set; } = new List<UserTag>();

    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}