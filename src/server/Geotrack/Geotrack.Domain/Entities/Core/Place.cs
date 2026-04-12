using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Geotrack.Domain.Entities.Reviews;
using Geotrack.Domain.Entities.Tags;

namespace Geotrack.Domain.Entities.Core;

public class Place
{
    public int Id { get; set; } // PRIMARY KEY
    public string Name { get; set; } = string.Empty;

    public int PlaceTypeId { get; set; } // FOREIGN KEY
    public PlaceType PlaceType { get; set; } = null!; // NAVIGATIONAL

    public int LocationId { get; set; } // FOREIGN KEY
    public Location Location { get; set; } = null!; // NAVIGATIONAL

    public ICollection<Event> Events { get; set; } = new List<Event>(); // NAVIGATIONAL

    public ICollection<PlaceTag> PlaceTags { get; set; } = new List<PlaceTag>(); // NAVIGATIONAL
    public ICollection<PlaceReview> PlaceReviews { get; set; } = new List<PlaceReview>(); // NAVIGATIONAL

    public string Description { get; set; } = string.Empty;
    public double Latitude { get; set; }
    public double Longitude { get; set; }

    public string? ContactEmail { get; set; } = string.Empty;
    public string? Number { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}
