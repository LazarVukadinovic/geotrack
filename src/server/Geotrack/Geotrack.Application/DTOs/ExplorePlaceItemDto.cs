namespace Geotrack.Application.DTOs;

public class ExplorePlaceItemDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int? LocationId { get; set; }
}