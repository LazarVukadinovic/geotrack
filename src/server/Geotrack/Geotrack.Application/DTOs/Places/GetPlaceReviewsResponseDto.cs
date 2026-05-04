using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Geotrack.Application.DTOs.Places;
public class GetPlaceReviewsResponseDto
{
    public int PlaceId { get; set; }
    public double AverageRating { get; set; }
    public int ReviewsCount { get; set; }
    public int TotalCount { get; set; }
    public int Skip { get; set; }
    public int Take { get; set; }
    public int NextSkip { get; set; }
    public bool HasMore { get; set; }
    public List<PlaceReviewItemDto> Reviews { get; set; } = new();
}
