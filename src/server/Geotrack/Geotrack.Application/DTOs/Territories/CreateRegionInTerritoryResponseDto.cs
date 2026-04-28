namespace Geotrack.Application.DTOs.Territories;

public class CreateRegionInTerritoryResponseDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int TerritoryId { get; set; }
}