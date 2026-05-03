using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Geotrack.Domain.Entities.Users;

namespace Geotrack.Domain.Entities.Core;

public class Region
{
    public int Id { get; set; } // PRIMARY KEY
    public string Name { get; set; } = string.Empty;

    public ICollection<Destination> Destinations { get; set; } = new List<Destination>(); // NAVIGATIONAL
    public ICollection<RegionalManagerRegion> RegionalManagerRegions { get; set; } = new List<RegionalManagerRegion>(); // NAVIGATIONAL

    public int TerritoryId { get; set; }
    public Territory Territory { get; set; } = null!; // NAVIGATIONAL

    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }

    public ICollection<EntityImage> Images { get; set; } = new List<EntityImage>();
}