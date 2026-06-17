namespace Geotrack.Application.DTOs.Admin;

public sealed class EntityProfileTagDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public int? TagTypeId { get; set; }
    public string? TagTypeName { get; set; }
    public string? TagTypeSlug { get; set; }
}

public sealed class EntityProfileDto
{
    public string? Description { get; set; }
    public string? CardImageUrl { get; set; }
    public string? CoverImageUrl { get; set; }
    public List<string> GalleryImageUrls { get; set; } = new();
    public List<EntityProfileTagDto> Tags { get; set; } = new();
}

public sealed class EntityProfileUpdateRequestDto
{
    public string? Description { get; set; }
    public string? CardImageUrl { get; set; }
    public string? CoverImageUrl { get; set; }
    public List<string> GalleryImageUrls { get; set; } = new();
    public List<int> TagIds { get; set; } = new();
}
