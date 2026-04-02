using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Geotrack.Domain.Entities
{
    public class Region
    {
        public int Id { get; set; } // PRIMARY KEY
        public string Name { get; set; } = string.Empty;

        public ICollection<Destination> Destinations { get; set; } = new List<Destination>(); // NAVIGATIONAL
        public ICollection<RegionalOrganizationRegion> RegionalOrganizationRegions { get; set; } = new List<RegionalOrganizationRegion>(); // NAVIGATIONAL

        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
    }
}