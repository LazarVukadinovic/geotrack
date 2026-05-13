using Geotrack.Application.DTOs.Admin;

namespace Geotrack.Application.Interfaces.Service;

public interface IRegionalDestinationService
{
    Task<RegionalDestinationListResponseDto> GetDestinationsAsync(
        int userId,
        int regionId,
        string? search,
        string? status,
        string? sort,
        int skip,
        int take);

    Task<RegionalDestinationDetailDto> GetDestinationAsync(int userId, int regionId, int destinationId);

    Task<RegionalDestinationDetailDto> UpdateDestinationAsync(
        int userId,
        int regionId,
        int destinationId,
        RegionalDestinationUpdateRequestDto request);

    Task DeleteDestinationAsync(int userId, int regionId, int destinationId);
}
