namespace Geotrack.Domain.Entities.Tags;

public class TagType
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;

    public bool ForDestination { get; set; }
    public bool ForLocation { get; set; }
    public bool ForPlace { get; set; }
    public bool ForEvent { get; set; }

    public bool IsFilterable { get; set; }
    public string? FilterLabel { get; set; }
    public int FilterOrder { get; set; }

    public ICollection<Tag> Tags { get; set; } = new List<Tag>();

    public ICollection<TagTypePlaceType> PlaceTypes { get; set; } = new List<TagTypePlaceType>();

    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}