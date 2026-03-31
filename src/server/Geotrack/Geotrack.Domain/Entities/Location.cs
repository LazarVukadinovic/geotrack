using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Geotrack.Domain.Entities
{
    public class Location
    {
        public int Id { get; set; } // PRIMARY KEY
        public string Name { get; set; } = string.Empty;

        public int DestinationId { get; set; } // FOREIGN KEY
        public Destination Destination { get; set; } = null!; // NAVIGATIONAL

        public int LocationTypeId { get; set; } // FOREIGN KEY
        public LocationType LocationType { get; set; } = null!; // NAVIGATIONAL

        public ICollection<Place> Places { get; set; } = new List<Place>(); // NAVIGATIONAL
        public ICollection<Event> Events { get; set; } = new List<Event>(); // NAVIGATIONAL

        public string Address { get; set; } = string.Empty;
        public double Latitude { get; set; }
        public double Longitude { get; set; }

        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
        public bool IsActive { get; set; }
    }
}
