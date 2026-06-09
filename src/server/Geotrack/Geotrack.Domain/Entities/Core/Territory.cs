using Geotrack.Domain.Entities.Users;

namespace Geotrack.Domain.Entities.Core;

public class Territory
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string TerritoryKind { get; set; } = TerritoryKinds.Country;
    public string? CountryCode { get; set; }
    public string? CountryIso3 { get; set; }
    public string? BoundaryShapeType { get; set; }
    public string? BoundaryShapeGroup { get; set; }
    public string? BoundaryShapeId { get; set; }
    public string? BoundaryExternalKey { get; set; }
    public string? BoundarySource { get; set; }
    public string? BoundarySourceUrl { get; set; }
    public string? BoundaryAdmLevel { get; set; }
    public string? BoundaryReleaseType { get; set; }
    public string? BoundaryLicense { get; set; }
    public DateTime? BoundaryImportedAt { get; set; }
    public long? OsmRelationId { get; set; }
    public int? OsmAdminLevel { get; set; }

    public ICollection<Region> Regions { get; set; } = new List<Region>();

    public ICollection<TerritoryManagerTerritory> TerritoryManagerTerritories { get; set; } = new List<TerritoryManagerTerritory>();

    public ICollection<EntityImage> Images { get; set; } = new List<EntityImage>();

    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

public static class TerritoryKinds
{
    public const string Country = "Country";
    public const string DisputedTerritory = "DisputedTerritory";
}
