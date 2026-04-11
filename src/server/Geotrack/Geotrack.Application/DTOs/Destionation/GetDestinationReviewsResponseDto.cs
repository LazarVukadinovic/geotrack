using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Geotrack.Application.DTOs.Destionation
{
    public class GetDestinationReviewsResponseDto
    {
        public int DestinationId { get; set; }
        public double AverageRating { get; set; }
        public int ReviewsCount { get; set; }
        public List<DestinationReviewItemDto> Reviews { get; set; }
    }


    public class DestinationReviewItemDto
    {
        public int DestinationId { get; set; }
        public int UserId { get; set; }
        public double Rating { get; set; }
        public string? Comment { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
    }
}
