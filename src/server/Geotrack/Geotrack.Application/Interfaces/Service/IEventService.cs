using Geotrack.Application.DTOs.Events;
namespace Geotrack.Application.Interfaces.Service;
public interface IEventService
{
    Task<GetEventDetailsResponseDto> GetEventDetailsAsync(int eventId, int? userId);
    Task<GetEventSectionResponseDto> GetEventSectionAsync(int eventId, string sectionKey);
    Task<GetEventReviewsResponseDto> GetEventReviewsAsync(int eventId);
    Task<CreateEventReviewResponseDto> CreateEventReviewAsync(int eventId, int userId, CreateEventReviewRequestDto request);
}