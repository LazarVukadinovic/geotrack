using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace Geotrack.Domain.Entities
{
    public class DestinationType
    {
        public int Id { get; set; } // PRIMARY KEY
        public string Name { get; set; } = string.Empty;

        public ICollection<Destination> Destinations { get; set; } = new List<Destination>(); // NAVIGATIONAL

        public string Description { get; set; } = string.Empty;

        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
        public bool IsActive { get; set; }
    }
}
