using Geotrack.Domain.Entities.Core;

namespace Geotrack.Domain.Entities.Tags;

public class TerritoryTag
{
    public int TerritoryId { get; set; }
    public Territory Territory { get; set; } = null!;

    public int TagId { get; set; }
    public Tag Tag { get; set; } = null!;
}
