namespace Geotrack.Application.DTOs.Explore;

public class ExploreByLocationResponseDto
{
    public ExploreDestinationDto? SelectedDestination { get; set; }
    public List<ExploreSectionDto> Sections { get; set; } = new();
}