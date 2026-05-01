namespace Geotrack.Application.DTOs.Reviews;

public class ReportReviewRequestDto
{
    public string? Reason { get; set; }
    public string? Note { get; set; }
}

public class ReportReviewResponseDto
{
    public int ReportId { get; set; }
    public bool AlreadyReported { get; set; }
    public string Status { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
}

public class ModerateReviewReportRequestDto
{
    public string? Note { get; set; }
}

public class ReviewReportItemDto
{
    public int Id { get; set; }
    public string TargetType { get; set; } = string.Empty;
    public int ReviewId { get; set; }
    public int ReporterUserId { get; set; }
    public string ReporterName { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public string Note { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public int ReviewAuthorUserId { get; set; }
    public string ReviewAuthorName { get; set; } = string.Empty;
    public int EntityId { get; set; }
    public string EntityName { get; set; } = string.Empty;
    public int RegionId { get; set; }
    public string RegionName { get; set; } = string.Empty;
    public double ReviewRating { get; set; }
    public string ReviewComment { get; set; } = string.Empty;
    public DateTime ReviewCreatedAt { get; set; }
    public int? ReviewedByUserId { get; set; }
    public DateTime? ReviewedAt { get; set; }
    public string? Decision { get; set; }
    public string DecisionNote { get; set; } = string.Empty;
}

public class GetReviewReportsResponseDto
{
    public List<ReviewReportItemDto> Reports { get; set; } = new();
}
