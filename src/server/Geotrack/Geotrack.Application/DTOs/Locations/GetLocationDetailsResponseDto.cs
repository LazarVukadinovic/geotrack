using Geotrack.Application.DTOs.Common;
using Geotrack.Application.DTOs.Tags;

namespace Geotrack.Application.DTOs.Locations;

public class GetLocationDetailsResponseDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int DestinationId { get; set; }
    public string? DestinationName { get; set; }
    public int LocationTypeId { get; set; }
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public double AverageRating { get; set; }
    public int ReviewsCount { get; set; }
    public bool IsSaved { get; set; }
    public List<int> SavedCollectionIds { get; set; } = new();
    public List<PublicEntityImageDto> Images { get; set; } = new();
    public List<TagDto> Tags { get; set; } = new();
    public List<LocationSectionPreviewDto> Sections { get; set; } = new();
    public List<LocationRelatedItemDto> SimilarLocations { get; set; } = new();
    public List<LocationSectionEventItemDto> RelatedEvents { get; set; } = new();
}

public class LocationRelatedItemDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? Image { get; set; }
    public double AverageRating { get; set; }
}
