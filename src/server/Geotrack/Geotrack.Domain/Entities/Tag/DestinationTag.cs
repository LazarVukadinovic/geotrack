using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Geotrack.Domain.Entities;
public class DestinationTag
{
    public int DestinationId { get; set; }
    public Destination Destination { get; set; } = null!; // NAVIGATIONAL

    public int TagId { get; set; }
    public Tag Tag { get; set; } = null!; // NAVIGATIONAL
}
