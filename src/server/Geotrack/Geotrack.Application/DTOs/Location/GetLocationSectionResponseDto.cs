using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Geotrack.Application.DTOs.Location
{
    public class GetLocationSectionResponseDto
    {
        public int Id { get; set; }
        public string SectionKey { get; set; }
        public string SectionTitle { get; set; }
        public List<LocationSectionPlaceItemDto> Places { get; set; }
        public List<LocationSectionEventItemDto> Events { get; set; }
    }
}
