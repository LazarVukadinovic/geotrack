using Geotrack.Domain.Entities.Tags;

namespace Geotrack.Domain.Entities.Core;

public class PlaceType
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool IsOwnable { get; set; }

    public ICollection<Place> Places { get; set; } = new List<Place>();

    public ICollection<TagTypePlaceType> TagTypes { get; set; } = new List<TagTypePlaceType>();

    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}