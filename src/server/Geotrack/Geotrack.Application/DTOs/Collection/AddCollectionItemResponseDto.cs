namespace Geotrack.Application.DTOs.Collection;

public class AddCollectionItemResponseDto
{
    public int Id { get; set; }
    public int CollectionId { get; set; }

    public int? DestinationId { get; set; }
    public int? LocationId { get; set; }
    public int? PlaceId { get; set; }
    public int? EventId { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}