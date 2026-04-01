using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Geotrack.Domain.Entities
{
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

        public string Description { get; set; } = string.Empty;
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }

        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
    }
}
