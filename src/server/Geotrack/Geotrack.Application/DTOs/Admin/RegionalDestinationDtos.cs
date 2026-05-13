namespace Geotrack.Application.DTOs.Admin;

public class RegionalDestinationListItemDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Summary { get; set; }
    public string? Description { get; set; }
    public string Status { get; set; } = "Published";
    public string? InternalReferenceId { get; set; }
    public int RegionId { get; set; }
    public string RegionDisplayName { get; set; } = string.Empty;
    public int DestinationTypeId { get; set; }
    public string? DestinationTypeName { get; set; }
    public int? CreatedByUserId { get; set; }
    public string? BoundaryNote { get; set; }
    public string? BoundaryShapeType { get; set; }
    public List<RegionalDestinationTagDto> Tags { get; set; } = new();
    public DateTime? LastUpdatedAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? PublishedAt { get; set; }
    public string? ImageUrl { get; set; }
    public string? DetailRoute { get; set; }
    public string? EditRoute { get; set; }
}

public class RegionalDestinationDetailDto : RegionalDestinationListItemDto
{
    public double Latitude { get; set; }
    public double Longitude { get; set; }
}

public class RegionalDestinationListResponseDto
{
    public List<RegionalDestinationListItemDto> Items { get; set; } = new();
    public int TotalCount { get; set; }
    public int Skip { get; set; }
    public int Take { get; set; }
}

public class RegionalDestinationUpdateRequestDto
{
    public string? Name { get; set; }
    public string? Description { get; set; }
    public int? DestinationTypeId { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
}

public class RegionalDestinationCreateRequestDto
{
    public int RegionId { get; set; }
    public string? Name { get; set; }
    public string? Summary { get; set; }
    public string? Description { get; set; }
    public string? Status { get; set; }
    public int? DestinationTypeId { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public string? BoundaryNote { get; set; }
    public RegionalDestinationBoundaryDto? Boundary { get; set; }
    public List<int> TagIds { get; set; } = new();

    // Present only so accidental/tampered clients can be rejected explicitly.
    public string? InternalReferenceId { get; set; }
}

public class RegionalDestinationBoundaryDto
{
    public string? ShapeType { get; set; }
    public List<RegionalDestinationCoordinateDto> Coordinates { get; set; } = new();
}

public class RegionalDestinationCoordinateDto
{
    public double Latitude { get; set; }
    public double Longitude { get; set; }
}

public class RegionalDestinationTagDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
}
