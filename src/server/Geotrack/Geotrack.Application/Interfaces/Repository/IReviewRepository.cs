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

    Task<int> CountDestinationReviewsAsync(int destinationId);
    Task<double?> GetDestinationAverageRatingAsync(int destinationId);
    Task<List<DestinationReview>> GetDestinationReviewsPageAsync(int destinationId, int skip, int take);

    Task<int> CountLocationReviewsAsync(int locationId);
    Task<double?> GetLocationAverageRatingAsync(int locationId);
    Task<List<LocationReview>> GetLocationReviewsPageAsync(int locationId, int skip, int take);

    Task<HashSet<int>> GetReportedReviewIdsAsync(
        int reporterUserId,
        ReviewReportTargetType targetType,
        IEnumerable<int> reviewIds);

    Task<bool> PlaceExistsAsync(int placeId);
    Task<bool> IsPlaceOwnedByUserAsync(int placeId, int userId);
    Task<bool> EventExistsAsync(int eventId);
    Task<bool> DestinationExistsAsync(int destinationId);
    Task<bool> LocationExistsAsync(int locationId);
    Task<bool> HasUserReviewedPlaceAsync(int placeId, int userId);
    Task<bool> HasUserReviewedEventAsync(int eventId, int userId);
    Task<bool> HasUserReviewedDestinationAsync(int destinationId, int userId);
    Task<bool> HasUserReviewedLocationAsync(int locationId, int userId);

    void AddPlaceReview(PlaceReview review);
    void AddEventReview(EventReview review);
    void AddDestinationReview(DestinationReview review);
    void AddLocationReview(LocationReview review);

    Task UpdatePlaceRatingStatsAsync(int placeId);

    Task<User?> GetUserByIdAsync(int userId);

    // User review history
    Task<int> CountPlaceReviewsByUserAsync(int userId);
    Task<List<PlaceReview>> GetPlaceReviewsByUserPageAsync(int userId, int skip, int take);
    Task<int> CountEventReviewsByUserAsync(int userId);
    Task<List<EventReview>> GetEventReviewsByUserPageAsync(int userId, int skip, int take);
    Task<int> CountDestinationReviewsByUserAsync(int userId);
    Task<List<DestinationReview>> GetDestinationReviewsByUserPageAsync(int userId, int skip, int take);
    Task<int> CountLocationReviewsByUserAsync(int userId);
    Task<List<LocationReview>> GetLocationReviewsByUserPageAsync(int userId, int skip, int take);

    // Single review lookup
    Task<PlaceReview?> GetPlaceReviewByIdAsync(int reviewId);
    Task<EventReview?> GetEventReviewByIdAsync(int reviewId);
    Task<DestinationReview?> GetDestinationReviewByIdAsync(int reviewId);
    Task<LocationReview?> GetLocationReviewByIdAsync(int reviewId);

    // Delete
    void RemovePlaceReview(PlaceReview review);
    void RemoveEventReview(EventReview review);
    void RemoveDestinationReview(DestinationReview review);
    void RemoveLocationReview(LocationReview review);

    Task SaveChangesAsync();
}
