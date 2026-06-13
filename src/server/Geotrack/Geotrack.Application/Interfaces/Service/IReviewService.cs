using Geotrack.Application.DTOs.Reviews;

namespace Geotrack.Application.Interfaces.Service;

public interface IReviewService
{
    Task<PagedReviewsResponseDto> GetPlaceReviewsAsync(int placeId, int skip = 0, int take = 10, int? currentUserId = null);
    Task<PagedReviewsResponseDto> GetEventReviewsAsync(int eventId, int skip = 0, int take = 10, int? currentUserId = null);
    Task<PagedReviewsResponseDto> GetDestinationReviewsAsync(int destinationId, int skip = 0, int take = 10, int? currentUserId = null);
    Task<PagedReviewsResponseDto> GetLocationReviewsAsync(int locationId, int skip = 0, int take = 10, int? currentUserId = null);
    Task<ReviewDto> CreatePlaceReviewAsync(int placeId, int userId, CreateReviewDto dto);
    Task<ReviewDto> CreateEventReviewAsync(int eventId, int userId, CreateReviewDto dto);
    Task<ReviewDto> CreateDestinationReviewAsync(int destinationId, int userId, CreateReviewDto dto);
    Task<ReviewDto> CreateLocationReviewAsync(int locationId, int userId, CreateReviewDto dto);

    Task<PagedUserReviewsResponseDto> GetUserReviewsAsync(int userId, int skip = 0, int take = 10);
    Task<PagedUserReviewsResponseDto> GetMyReviewsAsync(int userId, int skip = 0, int take = 10);

    Task DeletePlaceReviewAsync(int reviewId, int userId);
    Task DeleteEventReviewAsync(int reviewId, int userId);
    Task DeleteDestinationReviewAsync(int reviewId, int userId);
    Task DeleteLocationReviewAsync(int reviewId, int userId);
    Task<ReviewDto> UpdatePlaceReviewAsync(int reviewId, int userId, CreateReviewDto dto);
    Task<ReviewDto> UpdateEventReviewAsync(int reviewId, int userId, CreateReviewDto dto);
    Task<ReviewDto> UpdateDestinationReviewAsync(int reviewId, int userId, CreateReviewDto dto);
    Task<ReviewDto> UpdateLocationReviewAsync(int reviewId, int userId, CreateReviewDto dto);

    List<ReviewReasonDto> GetReportReasons();
}
