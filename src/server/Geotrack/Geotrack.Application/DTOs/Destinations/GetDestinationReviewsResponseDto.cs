using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Geotrack.Application.DTOs.Destinations;
public class GetDestinationReviewsResponseDto
{
    public int DestinationId { get; set; }
    public double AverageRating { get; set; }
    public int ReviewsCount { get; set; }
    public List<DestinationReviewItemDto> Reviews { get; set; } = new();
}