using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Geotrack.Domain.Entities
{
    public class Activity
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;

        public ICollection<Location> Locations { get; set; } = new List<Location>();
    }
}
