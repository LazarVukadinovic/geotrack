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

    Task<RegionalEventDetailDto> CreateEventAsync(
        int userId,
        RegionalEventCreateRequestDto request);

    Task<RegionalEventDetailDto> UpdateEventAsync(
        int userId,
        int regionId,
        int eventId,
        RegionalEventUpdateRequestDto request);

    Task DeleteEventAsync(int userId, int regionId, int eventId);

    Task<List<RegionalEventParentDestinationDto>> GetParentDestinationsAsync(
        int userId,
        int regionId,
        string? search,
        int take);

    Task<List<RegionalEventParentLocationDto>> GetParentLocationsAsync(
        int userId,
        int regionId,
        string? search,
        int take);

    Task<List<RegionalEventParentPlaceDto>> GetParentPlacesAsync(
        int userId,
        int regionId,
        string? search,
        int take);

    Task<List<RegionalEventTagDto>> GetEventTagsAsync(int userId, string? search);

    Task<List<RegionalEventTypeDto>> GetEventTypesAsync(int userId);
}
