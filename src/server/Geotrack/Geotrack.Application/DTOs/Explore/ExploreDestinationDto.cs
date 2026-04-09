namespace Geotrack.Application.DTOs.Explore;

public class ExploreDestinationDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public double Latitude { get; set; }
    public double Longitude { get; set; }
}