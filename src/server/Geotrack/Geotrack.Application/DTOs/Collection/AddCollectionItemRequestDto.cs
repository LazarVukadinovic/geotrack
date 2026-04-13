namespace Geotrack.Application.DTOs.Collection;

public class AddCollectionItemRequestDto
{
    public string EntityType { get; set; } = string.Empty;
    public int EntityId { get; set; }
}