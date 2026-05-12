namespace Geotrack.Application.DTOs.Users;

public class InterestItemDto
{
    public int TagId { get; set; }

    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;

    public bool ForDestination { get; set; }
    public bool ForLocation { get; set; }
    public bool ForPlace { get; set; }
    public bool ForEvent { get; set; }

    public int? TagTypeId { get; set; }
    public string? TagTypeName { get; set; }
    public string? TagTypeSlug { get; set; }

    public List<int> AllowedPlaceTypeIds { get; set; } = new();

    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}
