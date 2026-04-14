using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Geotrack.Application.DTOs.Place
{
    public class GetPlaceReviewsresponseDto
    {
        public int PlaceId { get; set; }
        public double AverageRating { get; set; }
        public int ReviewsCount { get; set; }
        public List<PlaceReviewItemDto> Reviews { get; set; } = new();
    }
}
