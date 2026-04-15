using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Geotrack.Application.DTOs.Users;

public class InterestItemDto
{
    public int TagId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool ForDestination { get; set; }
    public bool ForLocation { get; set; }
    public bool ForPlace { get; set; }
    public bool ForEvent { get; set; }
    public int? PlaceTypeId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}