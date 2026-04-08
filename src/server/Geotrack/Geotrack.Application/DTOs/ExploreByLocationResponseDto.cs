namespace Geotrack.Application.DTOs;

public class ExploreByLocationResponseDto
{
    public ExploreDestinationDto? SelectedDestination { get; set; }
    public List<ExploreSectionDto> Sections { get; set; } = new();
}