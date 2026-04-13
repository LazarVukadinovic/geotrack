namespace Geotrack.Application.DTOs.Collection;

public class CollectionItemDto
{
    public int Id { get; set; }
    public int CollectionId { get; set; }

    public string EntityType { get; set; } = string.Empty;

    public int? DestinationId { get; set; }
    public int? LocationId { get; set; }
    public int? PlaceId { get; set; }
    public int? EventId { get; set; }

    public string? Name { get; set; }
    public string? Description { get; set; }

    public double? Latitude { get; set; }
    public double? Longitude { get; set; }

    public string? ContactEmail { get; set; }
    public string? Number { get; set; }

    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}