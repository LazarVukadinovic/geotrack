using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Geotrack.Application.DTOs.Places;
public class GetPlaceDetailsResponseDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int PlaceTypeId { get; set; }
    public int LocationId { get; set; }
    public string Description { get; set; } = string.Empty;
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public string? ContactEmail { get; set; }
    public string? Number { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public double AverageRating { get; set; }
    public int ReviewsCount { get; set; }
    public bool IsSaved { get; set; }
    public List<int> SavedCollectionIds { get; set; } = new();
    public List<PlaceEventItemDto> Events { get; set; } = new();
}
