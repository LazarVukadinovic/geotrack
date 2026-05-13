using Geotrack.Domain.Entities.Core;

namespace Geotrack.Domain.Entities.Tags;

public class TagTypePlaceType
{
    public int TagTypeId { get; set; }
    public TagType TagType { get; set; } = null!;

    public int PlaceTypeId { get; set; }
    public PlaceType PlaceType { get; set; } = null!;
}