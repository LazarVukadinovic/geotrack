using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Geotrack.Application.DTOs.Locations;
public class GetLocationSectionResponseDto
{
    public int LocationId { get; set; }
    public string SectionKey { get; set; } = string.Empty;
    public string SectionTitle { get; set; } = string.Empty;
    public List<LocationSectionPlaceItemDto> Places { get; set; } = new();
    public List<LocationSectionEventItemDto> Events { get; set; } = new();
}