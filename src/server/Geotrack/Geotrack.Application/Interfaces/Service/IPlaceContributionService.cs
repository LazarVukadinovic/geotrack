using Geotrack.Application.DTOs.Contributions;

namespace Geotrack.Application.Interfaces.Service;

public interface IPlaceContributionService
{
    Task<GetPlaceContributionsResponseDto> GetForUserAsync(int userId);
    Task<PlaceContributionDetailDto?> GetByIdForUserAsync(int userId, int contributionId);
    Task<GetPlaceContributionsResponseDto> GetPendingReviewAsync(int? reviewerUserId = null);
    Task<PlaceContributionDetailDto?> GetPendingReviewByIdAsync(int contributionId, int? reviewerUserId = null);
    Task<List<ContributionPlaceTypeDto>> GetPlaceTypesAsync();
    Task<ContributionFeatureOptionsDto> GetFeatureOptionsAsync();

    Task<PlaceContributionDetailDto> CreateAsync(
        int userId,
        UpsertPlaceContributionDto request,
        byte[]? cardImageBytes,
        byte[]? coverImageBytes,
        IReadOnlyList<byte[]>? galleryImageBytes,
        byte[]? ownershipProofBytes,
        string? ownershipProofOriginalFileName,
        string? ownershipProofContentType);

    Task<PlaceContributionDetailDto> UpdateAsync(
        int userId,
        int contributionId,
        UpsertPlaceContributionDto request,
        byte[]? cardImageBytes,
        byte[]? coverImageBytes,
        IReadOnlyList<byte[]>? galleryImageBytes,
        byte[]? ownershipProofBytes,
        string? ownershipProofOriginalFileName,
        string? ownershipProofContentType);

    Task DeleteAsync(int userId, int contributionId);
    Task<PlaceContributionDetailDto> ApproveAsync(int contributionId, int reviewerUserId, string? note);
    Task<PlaceContributionDetailDto> RejectAsync(int contributionId, int reviewerUserId, string? note);
}