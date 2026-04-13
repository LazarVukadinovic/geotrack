namespace Geotrack.Application.DTOs.Places;

public class PlaceListItemDto
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Subtitle { get; set; }
    public string? Description { get; set; }
    public string? Location { get; set; }
    public string? Image { get; set; }
    public double? Rating { get; set; }
    public string? ReviewCount { get; set; }
    public string? OpeningHours { get; set; }
    public string? Distance { get; set; }
    public decimal? PricePerNight { get; set; }
    public List<string> Tags { get; set; } = new();
    public string Variant { get; set; } = "large";
    public bool IsSaved { get; set; }
}
