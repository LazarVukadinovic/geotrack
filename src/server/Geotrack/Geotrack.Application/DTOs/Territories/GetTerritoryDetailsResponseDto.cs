namespace Geotrack.Application.DTOs.Territories;

public class GetTerritoryDetailsResponseDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;

    public List<TerritoryRegionItemDto> Regions { get; set; } = new();
}