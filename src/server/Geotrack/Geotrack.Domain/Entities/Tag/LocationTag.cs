using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Geotrack.Domain.Entities;

public class LocationTag
{
    public int LocationId { get; set; }
    public Location Location { get; set; } = null!; // NAVIGATIONAL

    public int TagId { get; set; }
    public Tag Tag { get; set; } = null!; // NAVIGATIONAL
}
