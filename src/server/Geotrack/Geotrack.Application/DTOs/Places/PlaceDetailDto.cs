using Geotrack.Application.DTOs.Images;
using Geotrack.Application.DTOs.Reviews;

namespace Geotrack.Application.DTOs.Places;

public class PlaceDetailDto
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Location { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public string? HeroImage { get; set; }
    public List<EntityImageDto> Images { get; set; } = new();
    public List<TagLabelDto> Tags { get; set; } = new();
    public double? Rating { get; set; }
    public string? ReviewCount { get; set; }
    public string? Distance { get; set; }
    public string? OpenStatus { get; set; }
    public string? About { get; set; }
    public List<ReviewDto> Reviews { get; set; } = new();
    public List<SimilarPlaceDto> Similar { get; set; } = new();
    public bool IsSaved { get; set; }
    public string ContentType { get; set; } = "place";
    public string? Category { get; set; }
}

public class TagLabelDto
{
    public string Label { get; set; } = string.Empty;
}

public class SimilarPlaceDto
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Image { get; set; }
    public double? Rating { get; set; }
    public string? Distance { get; set; }
    public bool IsSaved { get; set; }
}
