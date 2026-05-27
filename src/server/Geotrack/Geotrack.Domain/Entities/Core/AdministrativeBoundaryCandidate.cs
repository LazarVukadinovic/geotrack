namespace Geotrack.Domain.Entities.Core;

public class AdministrativeBoundaryCandidate
{
    public int Id { get; set; }

    public string Source { get; set; } = string.Empty;
    public string ReleaseType { get; set; } = string.Empty;
    public string AdmLevel { get; set; } = string.Empty;
    public string ShapeGroup { get; set; } = string.Empty;
    public string ShapeId { get; set; } = string.Empty;
    public string ShapeName { get; set; } = string.Empty;
    public string? ShapeType { get; set; }
    public string ExternalKey { get; set; } = string.Empty;
    public string CountryIso3 { get; set; } = string.Empty;

    public int? CountryTerritoryId { get; set; }
    public Territory? CountryTerritory { get; set; }

    public DateTime ImportedAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}
