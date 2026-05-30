using Geotrack.Domain.Entities.Core;

namespace Geotrack.Domain.Entities.Users;

public class TerritoryManagerTerritory
{
    public int UserId { get; set; }
    public TerritoryManager TerritoryManager { get; set; } = null!;

    public int TerritoryId { get; set; }
    public Territory Territory { get; set; } = null!;

    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public DateTime? RegionSetupCompletedAt { get; set; }
}
