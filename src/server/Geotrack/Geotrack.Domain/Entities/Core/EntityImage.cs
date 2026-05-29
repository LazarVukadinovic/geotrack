namespace Geotrack.Domain.Entities.Core;

public class EntityImage
{
    public int Id { get; set; }

    public EntityImageTargetType TargetType { get; set; }

    public int? PlaceId { get; set; }
    public Place? Place { get; set; }

    public int? EventId { get; set; }
    public Event? Event { get; set; }

    public int? RegionId { get; set; }
    public Region? Region { get; set; }

    public int? DestinationId { get; set; }
    public Destination? Destination { get; set; }

    public int? TerritoryId { get; set; }
    public Territory? Territory { get; set; }

    public int? LocationId { get; set; }
    public Location? Location { get; set; }

    public string Url { get; set; } = string.Empty;

    public string? AltText { get; set; }
    public string? Caption { get; set; }

    public bool IsMain { get; set; }

    public string ImageRole { get; set; } = EntityImageRole.Gallery;
    public int SortOrder { get; set; }
    public string ImagePosition { get; set; } = "50% 50%";
    public string ImageSize { get; set; } = "cover";

    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}