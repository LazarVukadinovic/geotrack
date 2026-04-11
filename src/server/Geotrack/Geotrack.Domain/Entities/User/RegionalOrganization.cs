using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Geotrack.Domain.Entities;

public class RegionalOrganization
{
    public int UserId { get; set; } // FOREIGN KEY
    public User User { get; set; } = null!; // NAVIGATIONAL

    public ICollection<RegionalOrganizationRegion> RegionalOrganizationRegions { get; set; } = new List<RegionalOrganizationRegion>(); // NAVIGATIONAL
}
