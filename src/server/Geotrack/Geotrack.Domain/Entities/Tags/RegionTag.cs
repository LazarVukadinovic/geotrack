using Geotrack.Domain.Entities.Core;

namespace Geotrack.Domain.Entities.Tags;

public class RegionTag
{
    public int RegionId { get; set; }
    public Region Region { get; set; } = null!;

    public int TagId { get; set; }
    public Tag Tag { get; set; } = null!;
}
