namespace Geotrack.Domain.Entities.Users;

public class PlaceContributionImage
{
    public int Id { get; set; }

    public int PlaceContributionId { get; set; }
    public PlaceContribution PlaceContribution { get; set; } = null!;

    public string Url { get; set; } = string.Empty;

    public string? AltText { get; set; }
    public string? Caption { get; set; }

    public bool IsMain { get; set; }
    public int SortOrder { get; set; }

    public string ImagePosition { get; set; } = "50% 50%";
    public string ImageSize { get; set; } = "cover";

    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}