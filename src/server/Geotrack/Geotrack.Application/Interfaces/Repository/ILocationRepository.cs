using Geotrack.Domain.Entities.Core;
using Geotrack.Domain.Entities.Reviews;

namespace Geotrack.Application.Interfaces.Repository;

public interface ILocationRepository : IRepository<Location>
{
    Task<Location?> GetWithDetailsAsync(int locationId);
    Task<bool> IsLocationSavedByUserAsync(int locationId, int userId);
    Task<List<int>> GetSavedCollectionIdsByUserAsync(int locationId, int
    userId);
    Task<List<LocationReview>> GetReviewsByLocationIdAsync(int locationId);
    Task<bool> HasUserReviewedLocationAsync(int locationId, int userId);
    Task AddReviewAsync(LocationReview review);
    Task<List<Location>> SearchByNameAsync(string query);
}
