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

    Task<RegionalLocationDetailDto> UpdateLocationAsync(
        int userId,
        int regionId,
        int locationId,
        RegionalLocationUpdateRequestDto request);

    Task DeleteLocationAsync(int userId, int regionId, int locationId);
}
