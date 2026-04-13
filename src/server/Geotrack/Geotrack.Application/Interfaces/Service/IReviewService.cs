using Geotrack.Application.DTOs.Reviews;

namespace Geotrack.Application.Interfaces.Service;

public interface IReviewService
{
    Task<List<ReviewDto>> GetPlaceReviewsAsync(int placeId);
    Task<List<ReviewDto>> GetEventReviewsAsync(int eventId);
    Task<ReviewDto> CreatePlaceReviewAsync(int placeId, int userId, CreateReviewDto dto);
    Task<ReviewDto> CreateEventReviewAsync(int eventId, int userId, CreateReviewDto dto);
}
