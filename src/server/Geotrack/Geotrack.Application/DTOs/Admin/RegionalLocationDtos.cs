namespace Geotrack.Application.DTOs.Admin;

public class RegionalLocationListItemDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Summary { get; set; }
    public string? Description { get; set; }
    public string? LocationType { get; set; }
    public int? LocationTypeId { get; set; }
    public string Status { get; set; } = "Published";
    public string? InternalReferenceId { get; set; }
    public int RegionId { get; set; }
    public string RegionDisplayName { get; set; } = string.Empty;
    public int DestinationId { get; set; }
    public string DestinationName { get; set; } = string.Empty;
    public int? CreatedByUserId { get; set; }
    public string? BoundaryNote { get; set; }
    public string? BoundaryShapeType { get; set; }
    public List<RegionalLocationTagDto> Tags { get; set; } = new();
    public DateTime? LastUpdatedAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? PublishedAt { get; set; }
    public string? ImageUrl { get; set; }
    public string? CardImageUrl { get; set; }
    public string? CoverImageUrl { get; set; }
    public List<string> GalleryImageUrls { get; set; } = new();
    public string? DetailRoute { get; set; }
    public string? EditRoute { get; set; }
}

public class RegionalLocationDetailDto : RegionalLocationListItemDto
{
    public double Latitude { get; set; }
    public double Longitude { get; set; }
}

public class RegionalLocationListResponseDto
{
    public List<RegionalLocationListItemDto> Items { get; set; } = new();
    public int TotalCount { get; set; }
    public int Skip { get; set; }
    public int Take { get; set; }
}

public class RegionalLocationUpdateRequestDto
{
    public string? Name { get; set; }
    public string? Summary { get; set; }
    public string? Description { get; set; }
    public int? DestinationId { get; set; }
    public int? LocationTypeId { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public List<int>? TagIds { get; set; }
}

public class RegionalLocationCreateRequestDto
{
    public int RegionId { get; set; }
    public int DestinationId { get; set; }
    public string? Name { get; set; }
    public string? Summary { get; set; }
    public string? Description { get; set; }
    public string? Status { get; set; }
    public int? LocationTypeId { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public string? BoundaryNote { get; set; }
    public RegionalLocationBoundaryDto? Boundary { get; set; }
    public List<int> TagIds { get; set; } = new();

    public string? CardImageUrl { get; set; }
    public string? CoverImageUrl { get; set; }
    public List<string> GalleryImageUrls { get; set; } = new();

    // Present only so accidental/tampered clients can be rejected explicitly.
    public string? InternalReferenceId { get; set; }
}

public class RegionalLocationBoundaryDto
{
    public string? ShapeType { get; set; }
    public List<RegionalLocationCoordinateDto> Coordinates { get; set; } = new();
}

public class RegionalLocationCoordinateDto
{
    public double Latitude { get; set; }
    public double Longitude { get; set; }
}

public class RegionalLocationTagDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
}

public class RegionalLocationParentDestinationDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Status { get; set; } = "Published";
}

public class RegionalLocationTypeDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
}
