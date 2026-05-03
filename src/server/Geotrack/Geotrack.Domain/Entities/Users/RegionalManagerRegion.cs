using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Geotrack.Domain.Entities.Core;

namespace Geotrack.Domain.Entities.Users;

public class RegionalManagerRegion
{
    public int UserId { get; set; }
    public RegionalManager RegionalManager { get; set; } = null!; // NAVIGATIONAL
    public int RegionId { get; set; }
    public Region Region { get; set; } = null!; // NAVIGATIONAL

    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}
