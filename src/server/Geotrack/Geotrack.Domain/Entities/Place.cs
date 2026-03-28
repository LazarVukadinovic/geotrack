using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Geotrack.Domain.Entities
{
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

    }
}
