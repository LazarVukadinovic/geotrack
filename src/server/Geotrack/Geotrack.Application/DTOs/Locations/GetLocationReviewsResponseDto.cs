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
    public int TotalCount { get; set; }
    public int Skip { get; set; }
    public int Take { get; set; }
    public int NextSkip { get; set; }
    public bool HasMore { get; set; }
    public List<LocationReviewItemDto> Reviews { get; set; } = new();
}
