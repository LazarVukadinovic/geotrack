using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Geotrack.Application.DTOs.Location
{
    public class LocationSectionPreviewDto
    {
        public string Key { get; set; }
        public string Title { get; set; }
        public List<LocationSectionPlaceItemDto> Places { get; set; } = new();
        public List<LocationSectionEventItemDto> Events { get; set; } = new();
    }
}
