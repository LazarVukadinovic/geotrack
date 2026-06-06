using Geotrack.Application.DTOs.Contributions;

namespace Geotrack.Application.Interfaces.Service;

public interface IPlaceContributionService
{
    Task<GetPlaceContributionsResponseDto> GetForUserAsync(int userId);
    Task<PlaceContributionDetailDto?> GetByIdForUserAsync(int userId, int contributionId);
    Task<GetPlaceContributionsResponseDto> GetPendingReviewAsync(int? reviewerUserId = null);
    Task<PlaceContributionDetailDto?> GetPendingReviewByIdAsync(int contributionId, int? reviewerUserId = null);
    Task<GetPlaceContributionsResponseDto> GetPendingForContentManagerAsync();
    Task<PlaceContributionDetailDto?> GetPendingForContentManagerByIdAsync(int contributionId);
    Task<List<ContributionPlaceTypeDto>> GetPlaceTypesAsync();
    Task<List<ContributionStayTypeDto>> GetStayTypesAsync();
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

    Task<ResolveContributionLocationDto?> ResolveNearestLocationAsync(double latitude, double longitude);
    Task DeleteAsync(int userId, int contributionId);
    Task<PlaceContributionDetailDto> ApproveAsync(int contributionId, int reviewerUserId, string? note);
    Task<PlaceContributionDetailDto> ApproveForContentManagerAsync(int contributionId, int reviewerUserId, int regionId, int? existingDestinationId, string? newDestinationName, string? newLocationName, string? note);
    Task<PlaceContributionDetailDto> RejectAsync(int contributionId, int reviewerUserId, string? note);
    Task<ContributionRegionOptionsDto> GetRegionOptionsAsync();
    Task<ContributionLocationOptionsDto> GetDestinationLocationOptionsAsync(int destinationId);
    Task AddDestinationCardImageAsync(int destinationId, byte[] imageBytes);
    Task AddLocationCardImageAsync(int locationId, byte[] imageBytes);
}