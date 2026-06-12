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
    public string SearchText { get; set; } = string.Empty;
    public string? Summary { get; set; }
    public string? InternalReferenceId { get; set; }
    public string Status { get; set; } = "Published";

    public int EventTypeId { get; set; } // FOREIGN KEY
    public EventType EventType { get; set; } = null!; // NAVIGATIONAL

    public int DestinationId { get; set; } // FOREIGN KEY
    public Destination Destination { get; set; } = null!; // NAVIGATIONAL

    public int? LocationId { get; set; } // FOREIGN KEY
    public Location? Location { get; set; } // NAVIGATIONAL

    public int? PlaceId { get; set; } // FOREIGN KEY
    public Place? Place { get; set; } // NAVIGATIONAL

    public string ParentEntityType { get; set; } = "Location";
    public string? VenueName { get; set; }

    public ICollection<EventTag> EventTags { get; set; } = new List<EventTag>(); // NAVIGATIONAL

    public ICollection<EventReview> EventReviews { get; set; } = new List<EventReview>(); // NAVIGATIONAL

    public string Description { get; set; } = string.Empty;
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public bool IsAllDay { get; set; }
    public bool IsRecurring { get; set; }

    // Extended event fields
    public string? TicketPrice { get; set; }
    public bool IsFree { get; set; }
    public string? ExternalTicketLink { get; set; }
    public string? Duration { get; set; }
    public string? HostedBy { get; set; }
    public string? ContactEmail { get; set; }
    public string? ContactPhone { get; set; }
    public int? AttendanceLimit { get; set; }
    public string? AgeRestriction { get; set; }
    public string? VenueDescription { get; set; }
    public string? ArrivalTips { get; set; }
    public string? Accessibility { get; set; }

    public int? CreatedByUserId { get; set; }
    public DateTime? PublishedAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }

    public ICollection<EntityImage> Images { get; set; } = new List<EntityImage>();
}
