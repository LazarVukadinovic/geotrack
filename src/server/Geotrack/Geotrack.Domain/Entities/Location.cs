using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Geotrack.Domain.Entities
{
    public class Location
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;

        public int DestinationId { get; set; }
        public Destination Destination { get; set; } = null!;

        public ICollection<Activity> Activities { get; set; } = new List<Activity>();
        public ICollection<Event> Events { get; set; } = new List<Event>();
    }
}
