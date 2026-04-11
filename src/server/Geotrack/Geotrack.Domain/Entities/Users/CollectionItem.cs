using Geotrack.Domain.Entities.Core;

namespace Geotrack.Domain.Entities.Users;
public class CollectionItem
{
    public int Id { get; set; } // PRIMARY KEY

    public int CollectionId { get; set; } // FOREIGN KEY
    public Collection Collection { get; set; } = null!; // NAVIGATIONAL

    public int? DestinationId { get; set; } // FOREIGN KEY
    public Destination? Destination { get; set; } = null; // NAVIGATIONAL

    public int? LocationId { get; set; } // FOREIGN KEY
    public Location? Location { get; set; } = null; // NAVIGATIONAL

    public int? EventId { get; set; } // FOREIGN KEY
    public Event? Event { get; set; } = null; // NAVIGATIONAL

    public int? PlaceId { get; set; } // FOREIGN KEY
    public Place? Place { get; set; } = null; // NAVIGATIONAL

    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}
