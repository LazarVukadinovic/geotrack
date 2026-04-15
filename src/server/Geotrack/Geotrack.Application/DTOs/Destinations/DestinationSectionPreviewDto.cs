using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Geotrack.Application.DTOs.Destinations;
public class DestinationSectionPreviewDto
{
    public string Key { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public List<DestinationSectionLocationItemDto> Locations { get; set; } =
    new();
    public List<DestinationSectionPlaceItemDto> Places { get; set; } = new();
}