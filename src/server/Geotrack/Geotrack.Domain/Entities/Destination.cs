using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Geotrack.Domain.Entities
{
    public class Destination
    {
        public int Id { get; set; } // PRIMARY KEY
        public string Name { get; set; } = string.Empty;

        public int DestinationTypeId { get; set; } // FOREIGN KEY
        public DestinationType DestinationType { get; set; } = null!; // NAVIGATIONAL

        public ICollection<Location> Locations { get; set; } = new List<Location>(); // NAVIGATIONAL
        public ICollection<DestinationTag> DestinationTags { get; set; } = new List<DestinationTag>(); // NAVIGATIONAL

        public int RegionId { get; set; } // FOREIGN KEY
        public Region Region { get; set; } = null!; // NAVIGATIONAL

        public string Description { get; set; } = string.Empty;
        public double Latitude { get; set; }
        public double Longitude { get; set; }

        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
    }
}
