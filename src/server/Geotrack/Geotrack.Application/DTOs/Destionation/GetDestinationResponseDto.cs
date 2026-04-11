using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Geotrack.Application.DTOs.Destionation
{
    public class GetDestinationResponseDto
    {
        public int Id { get; set; }
        public string? Name { get; set; }
        public string? Description { get; set; }
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public int DestinationTypeId { get; set; }
        public int RegionId { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
        public double AverageRating { get; set; }
        public int ReviewsCount { get; set; }
        public bool IsSaved { get; set; }
        public List<int> SavedCollectionIds { get; set; }
        public List<DestinationSectionPreviewDto> Sections { get; set; }
    }
}
