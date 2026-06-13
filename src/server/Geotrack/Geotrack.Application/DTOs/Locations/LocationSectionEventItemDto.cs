using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Geotrack.Application.DTOs.Locations;

public class LocationSectionEventItemDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Image { get; set; }
    public int? LocationId { get; set; }
    public int? PlaceId { get; set; }
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
}
