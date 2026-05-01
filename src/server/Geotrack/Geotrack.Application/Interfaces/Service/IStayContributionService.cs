using Geotrack.Application.DTOs.Contributions;

namespace Geotrack.Application.Interfaces.Service;

public interface IStayContributionService
{
    Task<GetStayContributionsResponseDto> GetMineAsync(int userId);
    Task<StayContributionDetailDto> GetMineDetailAsync(int userId, int contributionId);
    Task<StayContributionDetailDto> CreateAsync(int userId, UpsertStayContributionDto request, byte[]? coverImageBytes);
    Task<StayContributionDetailDto> UpdateAsync(int userId, int contributionId, UpsertStayContributionDto request, byte[]? coverImageBytes);
    Task DeleteAsync(int userId, int contributionId);
    Task<GetStayContributionsResponseDto> GetReviewQueueAsync(int reviewerUserId, string? status);
    Task<StayContributionDetailDto> ApproveAsync(int reviewerUserId, int contributionId, ReviewStayContributionRequestDto request);
    Task<StayContributionDetailDto> RejectAsync(int reviewerUserId, int contributionId, ReviewStayContributionRequestDto request);
}
