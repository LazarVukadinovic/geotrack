using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Geotrack.Domain.Entities.Reviews;
using Geotrack.Domain.Entities.Tags;

namespace Geotrack.Domain.Entities.Core;

public class Event
{
    public int Id { get; set; } // PRIMARY KEY
    public string Name { get; set; } = string.Empty;

    public int EventTypeId { get; set; } // FOREIGN KEY
    public EventType EventType { get; set; } = null!; // NAVIGATIONAL

    public int LocationId { get; set; } // FOREIGN KEY
    public Location Location { get; set; } = null!; // NAVIGATIONAL

    public int? PlaceId { get; set; } // FOREIGN KEY
    public Place? Place { get; set; } // NAVIGATIONAL

    public ICollection<EventTag> EventTags { get; set; } = new List<EventTag>(); // NAVIGATIONAL

    public ICollection<EventReview> EventReviews { get; set; } = new List<EventReview>(); // NAVIGATIONAL

    public string? ImageUrl { get; set; }
    public string Description { get; set; } = string.Empty;
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }

    // Extended event fields
    public string? TicketPrice { get; set; }
    public string? Duration { get; set; }
    public string? HostedBy { get; set; }
    public string? VenueDescription { get; set; }
    public string? ArrivalTips { get; set; }
    public string? Accessibility { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}
