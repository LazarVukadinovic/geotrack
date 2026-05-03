using Geotrack.Application.DTOs.Contributions;

namespace Geotrack.Application.Interfaces.Service;

public interface IPlaceContributionService
{
    Task<GetPlaceContributionsResponseDto> GetMineAsync(int userId);
    Task<PlaceContributionDetailDto> GetMineDetailAsync(int userId, int contributionId);
    Task<PlaceContributionDetailDto> CreateAsync(int userId, UpsertPlaceContributionDto request, byte[]? mainImageBytes);
    Task<PlaceContributionDetailDto> UpdateAsync(int userId, int contributionId, UpsertPlaceContributionDto request, byte[]? mainImageBytes);
    Task DeleteAsync(int userId, int contributionId);
    Task<GetPlaceContributionsResponseDto> GetReviewQueueAsync(int reviewerUserId, string? status);
    Task<PlaceContributionDetailDto> ApproveAsync(int reviewerUserId, int contributionId, ReviewPlaceContributionRequestDto request);
    Task<PlaceContributionDetailDto> RejectAsync(int reviewerUserId, int contributionId, ReviewPlaceContributionRequestDto request);
}
