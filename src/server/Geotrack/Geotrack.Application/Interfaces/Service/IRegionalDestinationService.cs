using Geotrack.Application.DTOs.Admin;

namespace Geotrack.Application.Interfaces.Service;

public interface IRegionalDestinationService
{
    Task<RegionalDestinationListResponseDto> GetDestinationsAsync(
        int userId,
        int regionId,
        string? search,
        string? status,
        int? destinationTypeId,
        IReadOnlyCollection<int>? tagIds,
        IReadOnlyCollection<string>? issues,
        string? sort,
        int skip,
        int take);

    Task<RegionalDestinationDetailDto> GetDestinationAsync(int userId, int regionId, int destinationId);

    Task<RegionalDestinationBoundarySuggestionsDto> SearchBoundarySuggestionsAsync(
        int userId,
        int regionId,
        string query,
        double? latitude = null,
        double? longitude = null,
        CancellationToken cancellationToken = default);

    Task<RegionalDestinationBoundarySuggestionDto> ParseUploadedBoundaryAsync(
        int userId,
        int regionId,
        string fileName,
        byte[] content,
        CancellationToken cancellationToken = default);

    Task<DestinationCoordinateResolutionDto> ResolveDestinationAsync(
        int userId,
        int regionId,
        double latitude,
        double longitude,
        CancellationToken cancellationToken = default);

    Task<RegionalDestinationDetailDto> CreateDestinationAsync(
        int userId,
        RegionalDestinationCreateRequestDto request);

    Task<RegionalDestinationDetailDto> UpdateDestinationAsync(
        int userId,
        int regionId,
        int destinationId,
        RegionalDestinationUpdateRequestDto request);

    Task DeleteDestinationAsync(int userId, int regionId, int destinationId);

    Task<List<RegionalDestinationTagDto>> GetDestinationTagsAsync(int userId, string? search);

    Task<List<RegionalDestinationTypeDto>> GetDestinationTypesAsync(int userId);

    Task<RegionalDestinationTagDto> CreateDestinationTagAsync(int userId, RegionalTagCreateRequestDto request);
}
