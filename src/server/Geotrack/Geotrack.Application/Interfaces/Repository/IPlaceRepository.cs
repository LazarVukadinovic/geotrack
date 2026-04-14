using Geotrack.Domain.Entities.Core;
using Geotrack.Domain.Entities.Reviews;

namespace Geotrack.Application.Interfaces.Repository;

public interface IPlaceRepository : IRepository<Place>
{
    Task<IEnumerable<Place>> GetByLocationIdAsync(int locationId);
    Task<Place?> GetWithDetailsAsync(int placeId);
    Task<bool> IsPlaceSavedByUserAsync(int placeId, int userId);
    Task<List<int>> GetSavedCollectionIdsByUserAsync(int placeId, int userId);
    Task<List<Event>> GetEventsByPlaceIdAsync(int placeId);
    Task<List<PlaceReview>> GetReviewsByPlaceIdAsync(int placeId);
}