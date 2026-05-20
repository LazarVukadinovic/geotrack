using Geotrack.Application.DTOs.Reviews;

namespace Geotrack.Application.Interfaces.Service;

public interface IReviewService
{
    Task<PagedReviewsResponseDto> GetPlaceReviewsAsync(int placeId, int skip = 0, int take = 10, int? currentUserId = null);
    Task<PagedReviewsResponseDto> GetEventReviewsAsync(int eventId, int skip = 0, int take = 10, int? currentUserId = null);
    Task<ReviewDto> CreatePlaceReviewAsync(int placeId, int userId, CreateReviewDto dto);
    Task<ReviewDto> CreateEventReviewAsync(int eventId, int userId, CreateReviewDto dto);

    Task<PagedUserReviewsResponseDto> GetUserReviewsAsync(int userId, int skip = 0, int take = 10);
    Task<PagedUserReviewsResponseDto> GetMyReviewsAsync(int userId, int skip = 0, int take = 10);

    Task DeletePlaceReviewAsync(int reviewId, int userId);
    Task DeleteEventReviewAsync(int reviewId, int userId);
    Task<ReviewDto> UpdatePlaceReviewAsync(int reviewId, int userId, CreateReviewDto dto);
    Task<ReviewDto> UpdateEventReviewAsync(int reviewId, int userId, CreateReviewDto dto);

    List<ReviewReasonDto> GetReportReasons();
}
