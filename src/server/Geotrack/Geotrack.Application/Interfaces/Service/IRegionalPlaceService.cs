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
        string? sort,
        int skip,
        int take);

    Task<RegionalPlaceDetailDto> GetPlaceAsync(int userId, int regionId, int placeId);

    Task<RegionalPlaceDetailDto> UpdatePlaceAsync(
        int userId,
        int regionId,
        int placeId,
        RegionalPlaceUpdateRequestDto request);

    Task DeletePlaceAsync(int userId, int regionId, int placeId);
}
