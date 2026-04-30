using Geotrack.Application.DTOs.Destinations;
using Geotrack.Application.DTOs.Events;
namespace Geotrack.Application.Interfaces.Service;
public interface IEventService
{
    // Frontend-facing methods (existing API shape)
    Task<DestinationScopedResult<EventsListingResponseDto>> GetEventsAsync(
        int? destinationId,
        string? tags = null,
        bool? upcoming = null,
        int? userId = null,
        double? latitude = null,
        double? longitude = null,
        int skip = 0,
        int? take = null);
    Task<EventDetailDto?> GetEventDetailAsync(
        int id,
        int? userId = null,
        double? latitude = null,
        double? longitude = null);

    // Master detail/section/review methods
    Task<GetEventDetailsResponseDto> GetEventDetailsAsync(int eventId, int? userId);
    Task<GetEventSectionResponseDto> GetEventSectionAsync(int eventId, string sectionKey);
    Task<GetEventReviewsResponseDto> GetEventReviewsAsync(int eventId);
    Task<CreateEventReviewResponseDto> CreateEventReviewAsync(int eventId, int userId, CreateEventReviewRequestDto request);
}
