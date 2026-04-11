using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Geotrack.Domain.Entities;

public class PlaceTag
{
    public int PlaceId { get; set; }
    public Place Place { get; set; } = null!; // NAVIGATIONAL

    public int TagId { get; set; }
    public Tag Tag { get; set; } = null!; // NAVIGATIONAL
}
