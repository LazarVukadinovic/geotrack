using Geotrack.Domain.Entities.Users;

namespace Geotrack.Domain.Entities.Core;

public class Territory
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;
    public string? BoundaryShapeType { get; set; }
    public string? BoundarySource { get; set; }
    public long? OsmRelationId { get; set; }
    public int? OsmAdminLevel { get; set; }

    public ICollection<Region> Regions { get; set; } = new List<Region>();

    public ICollection<TerritoryManagerTerritory> TerritoryManagerTerritories { get; set; } = new List<TerritoryManagerTerritory>();

    public ICollection<EntityImage> Images { get; set; } = new List<EntityImage>();

    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}
