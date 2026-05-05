namespace Geotrack.Application.DTOs.Territories;

public class GetTerritoriesResponseDto
{
    public List<TerritoryItemDto> Territories { get; set; } = new();
}