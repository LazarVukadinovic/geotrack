using Geotrack.Application.DTOs.Admin;

namespace Geotrack.Application.Interfaces.Service;

public interface IAdminReviewManagementService
{
    Task<AdminReviewListResponseDto> GetReviewsAsync(
        int currentUserId,
        AdminReviewListRequestDto request,
        CancellationToken cancellationToken = default);

    Task<AdminReviewDetailDto> GetReviewAsync(
        int currentUserId,
        string reviewKey,
        CancellationToken cancellationToken = default);

    Task<AdminReviewActionResultDto> DeleteReviewAsync(
        int currentUserId,
        string reviewKey,
        AdminReviewModerationRequestDto? request,
        CancellationToken cancellationToken = default);

    Task<AdminReviewActionResultDto> AcceptReportAsync(
        int currentUserId,
        string reviewKey,
        int reportId,
        AdminReviewModerationRequestDto? request,
        CancellationToken cancellationToken = default);

    Task<AdminReviewDetailDto> RejectReportAsync(
        int currentUserId,
        string reviewKey,
        int reportId,
        AdminReviewModerationRequestDto? request,
        CancellationToken cancellationToken = default);

    Task<AdminReviewActionResultDto> RemoveReviewAndSuspendAuthorAsync(
        int currentUserId,
        string reviewKey,
        AdminReviewModerationRequestDto? request,
        CancellationToken cancellationToken = default);

    Task<AdminReviewActionResultDto> RemoveReviewAndDeleteAuthorAsync(
        int currentUserId,
        string reviewKey,
        AdminReviewModerationRequestDto? request,
        CancellationToken cancellationToken = default);
}
