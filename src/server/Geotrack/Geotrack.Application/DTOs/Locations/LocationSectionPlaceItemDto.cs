using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Geotrack.Application.DTOs.Locations;

public class LocationSectionPlaceItemDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Image { get; set; }
    public int LocationId { get; set; }
    public string? CategoryLabel { get; set; }
    public double? AverageRating { get; set; }
    public List<string> Tags { get; set; } = new();
}
