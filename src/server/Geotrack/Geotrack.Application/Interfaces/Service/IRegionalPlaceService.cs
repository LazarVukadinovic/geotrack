using Geotrack.Application.DTOs.Admin;

namespace Geotrack.Application.Interfaces.Service;

public interface IRegionalPlaceService
{
    Task<RegionalPlaceListResponseDto> GetPlacesAsync(
        int userId,
        int regionId,
        string? search,
        string? status,
        string? placeType,
        int? destinationId,
        int? locationId,
        int? placeTypeId,
        IReadOnlyCollection<int>? tagIds,
        IReadOnlyCollection<string>? issues,
        string? sort,
        int skip,
        int take);

    Task<RegionalPlaceDetailDto> GetPlaceAsync(int userId, int regionId, int placeId);

    Task<RegionalPlaceDetailDto> CreatePlaceAsync(
        int userId,
        RegionalPlaceCreateRequestDto request);

    Task<RegionalPlaceDetailDto> UpdatePlaceAsync(
        int userId,
        int regionId,
        int placeId,
        RegionalPlaceUpdateRequestDto request);

    Task<RegionalPlaceDetailDto> AddOwnerAsync(
        int userId,
        int regionId,
        int placeId,
        RegionalPlaceAddOwnerRequestDto request);

    Task DeletePlaceAsync(int userId, int regionId, int placeId);

    Task<List<RegionalPlaceParentDestinationDto>> GetParentDestinationsAsync(
        int userId,
        int regionId,
        string? search,
        int take);

    Task<List<RegionalPlaceParentLocationDto>> GetParentLocationsAsync(
        int userId,
        int regionId,
        int? destinationId,
        string? search,
        int take);

    Task<List<RegionalPlaceTagDto>> GetPlaceTagsAsync(int userId, string? search);

    Task<RegionalPlaceTagDto> CreatePlaceTagAsync(int userId, RegionalTagCreateRequestDto request);

    Task<List<RegionalPlaceTypeDto>> GetPlaceTypesAsync(int userId);

    Task<List<RegionalStayTypeDto>> GetStayTypesAsync(int userId);
}
