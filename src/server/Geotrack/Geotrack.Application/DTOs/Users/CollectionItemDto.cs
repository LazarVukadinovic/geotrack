using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Geotrack.Application.DTOs.Users;

public class CollectionItemDto
{
    public int Id { get; set; }
    public int CollectionId { get; set; }
    public string EntityType { get; set; } = string.Empty;

    public int? DestinationId { get; set; }
    public int? LocationId { get; set; }
    public int? PlaceId { get; set; }
    public int? EventId { get; set; }

    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public string? ContactEmail { get; set; }
    public string? Number { get; set; }
    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}