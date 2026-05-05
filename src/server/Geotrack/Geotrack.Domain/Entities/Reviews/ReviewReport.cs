using Geotrack.Domain.Entities.Users;

namespace Geotrack.Domain.Entities.Reviews;

public class ReviewReport
{
    public int Id { get; set; }

    public ReviewReportTargetType TargetType { get; set; }
    public int ReviewId { get; set; }

    public int? PlaceReviewId { get; set; }
    public PlaceReview? PlaceReview { get; set; }

    public int? EventReviewId { get; set; }
    public EventReview? EventReview { get; set; }

    public int ReporterUserId { get; set; }
    public User ReporterUser { get; set; } = null!;

    public ReviewReportReason Reason { get; set; }
    public string Note { get; set; } = string.Empty;
    public ReviewReportStatus Status { get; set; } = ReviewReportStatus.Pending;

    public int? ReviewedByUserId { get; set; }
    public User? ReviewedByUser { get; set; }
    public DateTime? ReviewedAt { get; set; }
    public ReviewReportDecision? Decision { get; set; }
    public string DecisionNote { get; set; } = string.Empty;

    public int ReviewAuthorUserId { get; set; }
    public string ReviewAuthorName { get; set; } = string.Empty;
    public int EntityId { get; set; }
    public string EntityName { get; set; } = string.Empty;
    public int RegionId { get; set; }
    public string RegionName { get; set; } = string.Empty;
    public double ReviewRating { get; set; }
    public string ReviewCommentSnapshot { get; set; } = string.Empty;
    public DateTime ReviewCreatedAt { get; set; }

    public DateTime CreatedAt { get; set; }
}
