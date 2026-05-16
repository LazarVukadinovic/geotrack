namespace Geotrack.Application.DTOs.Interests;

public class InterestTopicGroupDto
{
    public string Group { get; set; } = string.Empty;
    public List<InterestTopicDto> Items { get; set; } = new();
}
