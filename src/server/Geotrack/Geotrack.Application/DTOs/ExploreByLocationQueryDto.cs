namespace Geotrack.Application.DTOs;
public class ExploreByLocationQueryDto
{
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public int? UserId { get; set; }
}