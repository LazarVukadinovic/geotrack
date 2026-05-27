using Geotrack.Application.DTOs.Admin;

namespace Geotrack.Application.Interfaces.Service;

public interface IContentModeratorEntityReviewService
{
    Task<ContentModeratorEntityReviewListResponseDto> GetEntitiesAsync(
        int currentUserId,
        string? type,
        string? search,
        string? status,
        string? sort,
        int skip,
        int take,
        CancellationToken cancellationToken = default);

    Task<ContentModeratorEntityReviewDetailDto> GetEntityAsync(
        int currentUserId,
        string type,
        int entityId,
        CancellationToken cancellationToken = default);

    Task<ContentModeratorEntityReviewDetailDto> SuspendEntityAsync(
        int currentUserId,
        string type,
        int entityId,
        ContentModeratorModerationActionRequestDto? request,
        CancellationToken cancellationToken = default);

    Task<ContentModeratorEntityReviewDetailDto> UnsuspendEntityAsync(
        int currentUserId,
        string type,
        int entityId,
        ContentModeratorModerationActionRequestDto? request,
        CancellationToken cancellationToken = default);

    Task<ContentModeratorEntityReviewDetailDto> DeleteEntityAsync(
        int currentUserId,
        string type,
        int entityId,
        ContentModeratorModerationActionRequestDto? request,
        CancellationToken cancellationToken = default);
}
