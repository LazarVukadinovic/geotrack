using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Geotrack.Domain.Entities.Users;

public class RegionalManager
{
    public int UserId { get; set; } // FOREIGN KEY
    public User User { get; set; } = null!; // NAVIGATIONAL

    public ICollection<RegionalManagerRegion> RegionalManagerRegions { get; set; } = new List<RegionalManagerRegion>(); // NAVIGATIONAL
}
