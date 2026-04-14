using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Geotrack.Application.DTOs.Location
{
    public class GetLocationReviewsResponseDto
    {
        public int LocationId { get; set; }
        public double AverageRating { get; set; }
        public int ReviewsCount { get; set; }
        public List<LocationReviewItemDto> Reviews { get; set; }
    }
}
