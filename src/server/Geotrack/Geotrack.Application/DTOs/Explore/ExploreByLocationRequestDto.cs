using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Geotrack.Application.DTOs.Explore;

public class ExploreByLocationRequestDto
{
    public double Latitude { get; set; }
    public double Longitude { get; set; }
}