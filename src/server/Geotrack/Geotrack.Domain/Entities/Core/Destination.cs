using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Geotrack.Domain.Entities.Reviews;
using Geotrack.Domain.Entities.Tags;

namespace Geotrack.Domain.Entities.Core;
public class Destination
{
    public int Id { get; set; } // PRIMARY KEY
    public string Name { get; set; } = string.Empty;
    public string SearchText { get; set; } = string.Empty;
    public string? Summary { get; set; }
    public string? InternalReferenceId { get; set; }
    public string Status { get; set; } = "Published";

    public int DestinationTypeId { get; set; } // FOREIGN KEY
    public DestinationType DestinationType { get; set; } = null!; // NAVIGATIONAL

    public ICollection<Location> Locations { get; set; } = new List<Location>(); // NAVIGATIONAL
    public ICollection<Event> Events { get; set; } = new List<Event>(); // NAVIGATIONAL
    public ICollection<DestinationTag> DestinationTags { get; set; } = new List<DestinationTag>(); // NAVIGATIONAL

    public int RegionId { get; set; } // FOREIGN KEY
    public Region Region { get; set; } = null!; // NAVIGATIONAL

    public ICollection<DestinationReview> DestinationReviews { get; set; } = new List<DestinationReview>(); // NAVIGATIONAL

    public string Description { get; set; } = string.Empty;
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public int? CreatedByUserId { get; set; }
    public DateTime? PublishedAt { get; set; }
    public string? BoundaryNote { get; set; }
    public string? BoundaryShapeType { get; set; }

    public ICollection<EntityImage> Images { get; set; } = new List<EntityImage>();

    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}
