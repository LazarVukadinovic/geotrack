namespace Geotrack.Application.DTOs.Interests;

public class InterestTopicTagDto
{
    public int TagId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public double Weight { get; set; }
}
