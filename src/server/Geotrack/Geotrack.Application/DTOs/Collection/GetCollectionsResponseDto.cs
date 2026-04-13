namespace Geotrack.Application.DTOs.Collection;

public class GetCollectionsResponseDto
{
    public List<CollectionListItemDto> Collections { get; set; } = new();
}