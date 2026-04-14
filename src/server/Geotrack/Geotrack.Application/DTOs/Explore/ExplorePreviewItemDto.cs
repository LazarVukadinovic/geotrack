using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Geotrack.Application.DTOs.Explore;

public class ExplorePreviewItemDto
{
    public string EntityType { get; set; } = string.Empty;
    public int EntityId { get; set; }

    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;

    public double? Latitude { get; set; }
    public double? Longitude { get; set; }

    public double AverageRating { get; set; }
    public int ReviewCount { get; set; }

    public bool IsSaved { get; set; }
    public int? UserRating { get; set; }

    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }
}