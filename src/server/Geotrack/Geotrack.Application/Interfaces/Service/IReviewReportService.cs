using Geotrack.Application.DTOs.Reviews;

namespace Geotrack.Application.Interfaces.Service;

public interface IReviewReportService
{
    Task<ReportReviewResponseDto> ReportReviewAsync(
        int reporterUserId,
        string targetType,
        int reviewId,
        ReportReviewRequestDto request);

    Task<GetReviewReportsResponseDto> GetReviewQueueAsync(int reviewerUserId, string? status);

    Task<ReviewReportItemDto> DismissReportAsync(
        int reviewerUserId,
        int reportId,
        ModerateReviewReportRequestDto request);

    Task<ReviewReportItemDto> RemoveReportedReviewAsync(
        int reviewerUserId,
        int reportId,
        ModerateReviewReportRequestDto request);
}
