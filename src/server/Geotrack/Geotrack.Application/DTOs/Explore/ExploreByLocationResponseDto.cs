using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Geotrack.Application.DTOs.Explore;

public class ExploreByLocationResponseDto
{
    public ExploreDestinationDto? SelectedDestination { get; set; }
    public List<ExploreSectionDto> Sections { get; set; } = new();
}