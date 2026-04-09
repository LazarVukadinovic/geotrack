using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Geotrack.Domain.Entities;

public class Tag
{
    public int Id { get; set; }
    public string Name { get; set; } = null!;
    public string Slug { get; set; } = null!;
    public bool ForDestination { get; set; }
    public bool ForLocation { get; set; }
    public bool ForPlace { get; set; }
    public bool ForEvent { get; set; }
    public int? PlaceTypeId { get; set; }

    public ICollection<DestinationTag> DestinationTags { get; set; } = new List<DestinationTag>(); // NAVIGATIONAL
    public ICollection<PlaceTag> PlaceTags { get; set; } = new List<PlaceTag>(); // NAVIGATIONAL
    public ICollection<EventTag> EventTags { get; set; } = new List<EventTag>(); // NAVIGATIONAL
    public ICollection<LocationTag> LocationTags { get; set; } = new List<LocationTag>(); // NAVIGATIONAL

    public string Description { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}
