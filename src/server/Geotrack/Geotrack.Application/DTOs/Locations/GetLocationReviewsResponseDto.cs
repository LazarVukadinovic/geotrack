using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Geotrack.Application.DTOs.Locations;

namespace Geotrack.Application.DTOs.Locations;
public class GetLocationReviewsResponseDto
{
    public int LocationId { get; set; }
    public double AverageRating { get; set; }
    public int ReviewsCount { get; set; }
    public List<LocationReviewItemDto> Reviews { get; set; } = new();
}