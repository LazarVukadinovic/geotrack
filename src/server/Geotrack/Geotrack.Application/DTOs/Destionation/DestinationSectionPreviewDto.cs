using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Geotrack.Application.DTOs.Destionation
{
    public class DestinationSectionPreviewDto
    {
        public string Key { get; set; }
        public string Title { get; set; }
        public List<DestinationSectionLocationItemDto> Locations { get; set; }
        public List<DestinationSectionPlaceItemDto> Places { get; set; }
        
    }
}
