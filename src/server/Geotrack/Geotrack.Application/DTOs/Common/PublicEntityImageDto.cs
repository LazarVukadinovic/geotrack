namespace Geotrack.Application.DTOs.Common;

public class PublicEntityImageDto
{
    public int Id { get; set; }
    public string Url { get; set; } = string.Empty;
    public string? AltText { get; set; }
    public string? Caption { get; set; }
    public bool IsMain { get; set; }
    public string ImageRole { get; set; } = "gallery";
    public int SortOrder { get; set; }
    public string ImagePosition { get; set; } = "50% 50%";
    public string ImageSize { get; set; } = "cover";
}
