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
    }
}
