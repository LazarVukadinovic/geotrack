namespace Geotrack.Application.DTOs.Interests;

public class InterestTopicDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string IconKey { get; set; } = string.Empty;
    public string Group { get; set; } = string.Empty;
    public int SortOrder { get; set; }
    public List<InterestTopicTagDto> Tags { get; set; } = new();
}
