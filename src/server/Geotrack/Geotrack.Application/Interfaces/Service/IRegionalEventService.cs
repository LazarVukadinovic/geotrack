using Geotrack.Application.DTOs.Admin;

namespace Geotrack.Application.Interfaces.Service;

public interface IRegionalEventService
{
    Task<RegionalEventListResponseDto> GetEventsAsync(
        int userId,
        int regionId,
        string? search,
        string? status,
        string? eventType,
        string? sort,
        int skip,
        int take);

    Task<RegionalEventDetailDto> GetEventAsync(int userId, int regionId, int eventId);

    Task<RegionalEventDetailDto> UpdateEventAsync(
        int userId,
        int regionId,
        int eventId,
        RegionalEventUpdateRequestDto request);

    Task DeleteEventAsync(int userId, int regionId, int eventId);
}
