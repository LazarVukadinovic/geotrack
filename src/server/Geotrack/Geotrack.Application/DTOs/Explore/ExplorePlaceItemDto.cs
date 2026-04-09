namespace Geotrack.Application.DTOs.Explore;

public class ExplorePlaceItemDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int? LocationId { get; set; }
}