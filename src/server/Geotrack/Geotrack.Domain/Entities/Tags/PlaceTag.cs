using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Geotrack.Domain.Entities.Core;

namespace Geotrack.Domain.Entities.Tags;

public class PlaceTag
{
    public int PlaceId { get; set; }
    public Place Place { get; set; } = null!; // NAVIGATIONAL

    public int TagId { get; set; }
    public Tag Tag { get; set; } = null!; // NAVIGATIONAL
}
