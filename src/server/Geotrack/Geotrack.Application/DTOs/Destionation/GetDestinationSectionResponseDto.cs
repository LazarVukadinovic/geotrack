using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Geotrack.Application.DTOs.Destionation
{
    public class GetDestinationSectionResponseDto
    {
        public int Id { get; set;}
        public string SectionKey { get; set;}
        public string SectionTitle { get; set;}
        public List<DestinationSectionLocationItemDto> Locations { get; set; }
        public List<DestinationSectionPlaceItemDto> Places { get; set; }

    }
}
