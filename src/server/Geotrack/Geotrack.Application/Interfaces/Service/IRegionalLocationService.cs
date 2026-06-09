using Geotrack.Application.DTOs.Admin;

namespace Geotrack.Application.Interfaces.Service;

public interface IRegionalLocationService
{
    Task<RegionalLocationListResponseDto> GetLocationsAsync(
        int userId,
        int regionId,
        string? search,
        string? status,
        string? sort,
        int skip,
        int take);

    Task<RegionalLocationDetailDto> GetLocationAsync(int userId, int regionId, int locationId);

    Task<RegionalLocationDetailDto> CreateLocationAsync(
        int userId,
        RegionalLocationCreateRequestDto request);

    Task<RegionalLocationDetailDto> UpdateLocationAsync(
        int userId,
        int regionId,
        int locationId,
        RegionalLocationUpdateRequestDto request);

    Task DeleteLocationAsync(int userId, int regionId, int locationId);

    Task<List<RegionalLocationParentDestinationDto>> GetParentDestinationsAsync(
        int userId,
        int regionId,
        string? search,
        int take);

    Task<List<RegionalLocationTagDto>> GetLocationTagsAsync(int userId, string? search);

    Task<RegionalLocationTagDto> CreateLocationTagAsync(int userId, RegionalTagCreateRequestDto request);

    Task<List<RegionalLocationTypeDto>> GetLocationTypesAsync(int userId);
}
