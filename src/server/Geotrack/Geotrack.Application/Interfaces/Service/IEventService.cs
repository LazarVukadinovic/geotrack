using Geotrack.Application.DTOs.Events;

namespace Geotrack.Application.Interfaces.Service;

public interface IEventService
{
    Task<EventsListingResponseDto> GetEventsAsync(int destinationId, string? tags = null, bool? upcoming = null, int? userId = null);
    Task<EventDetailDto?> GetEventDetailAsync(int id, int? userId = null);
}
