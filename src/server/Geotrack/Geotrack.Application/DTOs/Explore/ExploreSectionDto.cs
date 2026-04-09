namespace Geotrack.Application.DTOs.Explore;

public class ExploreSectionDto
{
    public string Key { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public List<ExploreLocationItemDto> Locations { get; set; } = new();
    public List<ExplorePlaceItemDto> Places { get; set; } = new();
}