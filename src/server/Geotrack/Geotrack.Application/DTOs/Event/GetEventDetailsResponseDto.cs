using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Geotrack.Application.DTOs.Event
{
    public class GetEventDetailsResponseDto
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public int EventTypeId { get; set; }
        public int LocationId { get; set; }
        public int? PlaceId { get; set; }
        public string Description { get; set; }
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
        public double AverageRating { get; set; }
        public int ReviewsCount { get; set; }
        public bool IsSaved { get; set; }
        public List<int> SavedCollectionIds { get; set; }
        public List<EventSectionPreviewDto> Sections { get; set; }
    }
}
