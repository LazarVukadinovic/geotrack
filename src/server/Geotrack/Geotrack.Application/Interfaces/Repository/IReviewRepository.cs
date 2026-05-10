using Geotrack.Domain.Entities.Reviews;
using Geotrack.Domain.Entities.Users;

namespace Geotrack.Application.Interfaces.Repository;

public interface IReviewRepository
{
    Task<int> CountPlaceReviewsAsync(int placeId);
    Task<double?> GetPlaceAverageRatingAsync(int placeId);
    Task<List<PlaceReview>> GetPlaceReviewsPageAsync(int placeId, int skip, int take);

    Task<int> CountEventReviewsAsync(int eventId);
    Task<double?> GetEventAverageRatingAsync(int eventId);
    Task<List<EventReview>> GetEventReviewsPageAsync(int eventId, int skip, int take);

    Task<HashSet<int>> GetReportedReviewIdsAsync(
        int reporterUserId,
        ReviewReportTargetType targetType,
        IEnumerable<int> reviewIds);

    Task<bool> PlaceExistsAsync(int placeId);
    Task<bool> EventExistsAsync(int eventId);
    Task<bool> HasUserReviewedPlaceAsync(int placeId, int userId);
    Task<bool> HasUserReviewedEventAsync(int eventId, int userId);

    void AddPlaceReview(PlaceReview review);
    void AddEventReview(EventReview review);

    Task UpdatePlaceRatingStatsAsync(int placeId);

    Task<User?> GetUserByIdAsync(int userId);

    Task SaveChangesAsync();
}
