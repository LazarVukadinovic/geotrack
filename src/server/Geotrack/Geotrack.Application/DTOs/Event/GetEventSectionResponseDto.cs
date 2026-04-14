using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Geotrack.Application.DTOs.Event
{
    public class GetEventSectionResponseDto
    {
        public int EventId { get; set; }
        public string SectionKey { get; set; }
        public string SectionTitle { get; set; }
        public List<EventReviewItemDto> Reviews { get; set; } = new();
        public List<EventTagItemDto> Tags { get; set; } = new();
    }
}
