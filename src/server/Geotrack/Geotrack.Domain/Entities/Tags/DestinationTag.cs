using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Geotrack.Domain.Entities.Core;

namespace Geotrack.Domain.Entities.Tags;
public class DestinationTag
{
    public int DestinationId { get; set; }
    public Destination Destination { get; set; } = null!; // NAVIGATIONAL

    public int TagId { get; set; }
    public Tag Tag { get; set; } = null!; // NAVIGATIONAL
}
