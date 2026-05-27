namespace Geotrack.Application.DTOs.Admin;

public class ContentModeratorEntityReviewListResponseDto
{
    public List<ContentModeratorEntityReviewListItemDto> Items { get; set; } = new();
    public int TotalCount { get; set; }
    public int Skip { get; set; }
    public int Take { get; set; }
    public int? NextSkip { get; set; }
    public bool HasMore { get; set; }
    public ContentModeratorEntityReviewCountsDto Counts { get; set; } = new();
}

public class ContentModeratorEntityReviewCountsDto
{
    public int AllCount { get; set; }
    public int ActiveCount { get; set; }
    public int SuspendedCount { get; set; }
    public int DeletedCount { get; set; }
}

public class ContentModeratorEntityReviewListItemDto
{
    public int EntityId { get; set; }
    public string EntityType { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Summary { get; set; }
    public string? DescriptionPreview { get; set; }
    public string? ThumbnailUrl { get; set; }
    public string Status { get; set; } = string.Empty;
    public string RawStatus { get; set; } = string.Empty;
    public bool IsPublished { get; set; }
    public bool IsSuspended { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public DateTime? PublishedAt { get; set; }
    public int? RegionId { get; set; }
    public string? RegionName { get; set; }
    public string? TerritoryName { get; set; }
    public int? DestinationId { get; set; }
    public string? DestinationName { get; set; }
    public int? LocationId { get; set; }
    public string? LocationName { get; set; }
    public int? PlaceId { get; set; }
    public string? PlaceName { get; set; }
    public string? TypeName { get; set; }
    public int? CreatedByUserId { get; set; }
    public DateTime? StartsAt { get; set; }
    public DateTime? EndsAt { get; set; }
    public string? PublicDetailRoute { get; set; }
    public bool CanOpenInUserApp { get; set; }
    public ContentModeratorEntityReviewPermissionsDto Permissions { get; set; } = new();
}

public class ContentModeratorEntityReviewDetailDto : ContentModeratorEntityReviewListItemDto
{
    public string? Description { get; set; }
    public List<ContentModeratorEntityImageDto> Images { get; set; } = new();
    public List<ContentModeratorEntityTagDto> Tags { get; set; } = new();
    public ContentModeratorEntityMetadataDto Metadata { get; set; } = new();
}

public class ContentModeratorEntityImageDto
{
    public int Id { get; set; }
    public string Url { get; set; } = string.Empty;
    public string? AltText { get; set; }
    public string? Caption { get; set; }
    public string ImageRole { get; set; } = string.Empty;
    public bool IsMain { get; set; }
    public int SortOrder { get; set; }
}

public class ContentModeratorEntityTagDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
}

public class ContentModeratorEntityMetadataDto
{
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public string? Address { get; set; }
    public string? BoundaryNote { get; set; }
    public string? BoundaryShapeType { get; set; }
    public string? ParentEntityType { get; set; }
    public string? VenueName { get; set; }
    public string? TicketPrice { get; set; }
    public bool? IsFree { get; set; }
    public bool? IsAllDay { get; set; }
    public bool? IsRecurring { get; set; }
    public string? ContactEmail { get; set; }
    public string? PhoneNumber { get; set; }
    public string? Website { get; set; }
    public Dictionary<string, int> ContentCounts { get; set; } = new();
}

public class ContentModeratorEntityReviewPermissionsDto
{
    public bool CanView { get; set; } = true;
    public bool CanSuspend { get; set; }
    public bool CanUnsuspend { get; set; }
    public bool CanDelete { get; set; }
    public bool CanRestore { get; set; }
}

public class ContentModeratorModerationActionRequestDto
{
    public string? Reason { get; set; }
    public string? InternalNote { get; set; }
}
