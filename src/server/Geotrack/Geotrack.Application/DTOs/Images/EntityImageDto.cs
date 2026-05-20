namespace Geotrack.Application.DTOs.Images;

public class EntityImageDto
{
    public int Id { get; set; }
    public string Url { get; set; } = string.Empty;
    public string? AltText { get; set; }
    public string? Caption { get; set; }
    public bool IsMain { get; set; }
    public string ImageRole { get; set; } = "gallery";
    public int SortOrder { get; set; }
}
