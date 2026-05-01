namespace Geotrack.Application.DTOs.Locations;

public class ApproximateLocationDto
{
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public string City { get; set; } = string.Empty;
    public string Region { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;
    public string DisplayLabel { get; set; } = string.Empty;
    public string Source { get; set; } = "ip";
}
