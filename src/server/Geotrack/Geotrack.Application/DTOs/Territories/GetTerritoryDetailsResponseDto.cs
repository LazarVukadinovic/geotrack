using Geotrack.Application.DTOs.Common;
using Geotrack.Application.DTOs.Tags;

namespace Geotrack.Application.DTOs.Territories;

public class GetTerritoryDetailsResponseDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public List<PublicEntityImageDto> Images { get; set; } = new();
    public List<TagDto> Tags { get; set; } = new();

    public List<TerritoryRegionItemDto> Regions { get; set; } = new();
}