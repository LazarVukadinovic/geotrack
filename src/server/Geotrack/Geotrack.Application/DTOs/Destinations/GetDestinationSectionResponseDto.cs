using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Geotrack.Application.DTOs.Destinations;
public class GetDestinationSectionResponseDto
{
    public int DestinationId { get; set; }
    public string SectionKey { get; set; } = string.Empty;
    public string SectionTitle { get; set; } = string.Empty;
    public List<DestinationSectionLocationItemDto> Locations { get; set; } = new();
    public List<DestinationSectionPlaceItemDto> Places { get; set; } = new();
}