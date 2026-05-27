using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Geotrack.Domain.Entities.Users;

namespace Geotrack.Domain.Entities.Core;

public class Region
{
    public int Id { get; set; } // PRIMARY KEY
    public string Name { get; set; } = string.Empty;
    public string? BoundaryShapeType { get; set; }
    public string? BoundaryShapeGroup { get; set; }
    public string? BoundaryShapeId { get; set; }
    public string? BoundaryExternalKey { get; set; }
    public string? BoundarySource { get; set; }
    public string? BoundaryAdmLevel { get; set; }
    public string? BoundaryReleaseType { get; set; }
    public string? BoundaryLicense { get; set; }
    public DateTime? BoundaryImportedAt { get; set; }
    public int? BoundaryCandidateId { get; set; }
    public AdministrativeBoundaryCandidate? BoundaryCandidate { get; set; }
    public long? OsmRelationId { get; set; }
    public int? OsmAdminLevel { get; set; }
    public int? CreatedByUserId { get; set; }

    public ICollection<Destination> Destinations { get; set; } = new List<Destination>(); // NAVIGATIONAL
    public ICollection<RegionalManagerRegion> RegionalManagerRegions { get; set; } = new List<RegionalManagerRegion>(); // NAVIGATIONAL

    public int TerritoryId { get; set; }
    public Territory Territory { get; set; } = null!; // NAVIGATIONAL

    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }

    public ICollection<EntityImage> Images { get; set; } = new List<EntityImage>();
}
