namespace Geotrack.Domain.Entities.Core;

public class Territory
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public ICollection<Region> Regions { get; set; } = new List<Region>(); // NAVIGATIONAL

    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}