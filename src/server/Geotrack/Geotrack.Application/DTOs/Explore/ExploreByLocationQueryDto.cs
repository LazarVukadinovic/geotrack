namespace Geotrack.Application.DTOs.Explore;
public class ExploreByLocationQueryDto
{
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public int? UserId { get; set; }
}