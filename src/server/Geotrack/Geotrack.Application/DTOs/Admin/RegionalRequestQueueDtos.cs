using Geotrack.Application.DTOs.Contributions;

namespace Geotrack.Application.DTOs.Admin;

public class RegionalRequestQueueItemDto
{
    public string RequestKey { get; set; } = string.Empty;
    public int RequestId { get; set; }
    public string DisplayRequestId { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string EntityType { get; set; } = string.Empty;
    public string? ContributionType { get; set; }
    public int RegionId { get; set; }
    public string RegionDisplayName { get; set; } = string.Empty;
    public int SubmittedByUserId { get; set; }
    public string SubmittedByDisplayName { get; set; } = string.Empty;
    public string? SubmittedByInitials { get; set; }
    public DateTime SubmittedAt { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? ThumbnailImageUrl { get; set; }
    public string? PreviewText { get; set; }
    public string? DetailRoute { get; set; }
}

public class RegionalRequestQueueCountsByTypeDto
{
    public int Destinations { get; set; }
    public int Locations { get; set; }
    public int Places { get; set; }
    public int Events { get; set; }
}

public class RegionalRequestQueueResponseDto
{
    public List<RegionalRequestQueueItemDto> Items { get; set; } = new();
    public int TotalCount { get; set; }
    public int PendingCount { get; set; }
    public int ApprovedCount { get; set; }
    public int RejectedCount { get; set; }
    public RegionalRequestQueueCountsByTypeDto CountsByType { get; set; } = new();
    public int Skip { get; set; }
    public int Take { get; set; }
}

public class RegionalRequestQueueDetailDto : RegionalRequestQueueItemDto
{
    public string? Description { get; set; }
    public string? Address { get; set; }
    public string? OpeningHours { get; set; }
    public string? LocationName { get; set; }
    public string? DestinationName { get; set; }
    public string? City { get; set; }
    public string? Country { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public string? ContactEmail { get; set; }
    public string? PhoneNumber { get; set; }
    public string? Website { get; set; }
    public string? PlaceType { get; set; }
    public string? StayType { get; set; }
    public int? TargetEntityId { get; set; }
    public string? TargetEntityName { get; set; }
    public List<string> Tags { get; set; } = new();
    public List<string> Amenities { get; set; } = new();
    public List<string> StayAmenities { get; set; } = new();
    public List<ContributionBookingPartnerDto> BookingPartners { get; set; } = new();
    public List<PlaceContributionImageDto> Images { get; set; } = new();
    public bool HasOwnershipProofDocument { get; set; }
    public string? OwnershipProofDocumentUrl { get; set; }
    public string? OwnershipProofOriginalFileName { get; set; }
    public string? OwnershipProofContentType { get; set; }
    public DateTime? OwnershipProofUploadedAt { get; set; }
    public string? DecisionNote { get; set; }
}

public class RegionalRequestDecisionRequestDto
{
    public int RegionId { get; set; }
    public string? Reason { get; set; }
    public string? Note { get; set; }
}

public class RegionalRequestBulkActionRequestDto
{
    public int RegionId { get; set; }
    public List<string> RequestKeys { get; set; } = new();
    public string? Reason { get; set; }
    public string? Note { get; set; }
}

public class RegionalRequestActionResultDto
{
    public string RequestKey { get; set; } = string.Empty;
    public bool Success { get; set; }
    public string? Status { get; set; }
    public string? Message { get; set; }
    public RegionalRequestQueueItemDto? Item { get; set; }
}

public class RegionalRequestBulkActionResponseDto
{
    public List<RegionalRequestActionResultDto> Results { get; set; } = new();
    public int SucceededCount { get; set; }
    public int FailedCount { get; set; }
}
