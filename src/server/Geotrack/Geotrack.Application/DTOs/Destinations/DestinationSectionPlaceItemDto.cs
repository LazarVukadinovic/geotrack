using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Geotrack.Application.DTOs.Destinations;

public class DestinationSectionPlaceItemDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Image { get; set; }
    public int LocationId { get; set; }
    public double AverageRating { get; set; }
    public int ReviewCount { get; set; }
}