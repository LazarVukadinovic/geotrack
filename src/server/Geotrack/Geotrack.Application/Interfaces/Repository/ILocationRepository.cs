using Geotrack.Application.DTOs.Locations;
using Geotrack.Domain.Entities.Core;
using Geotrack.Domain.Entities.Reviews;

namespace Geotrack.Application.Interfaces.Repository;

public interface ILocationRepository : IRepository<Location>
{
    Task<Location?> GetWithDetailsAsync(int locationId);
    Task<List<LocationRelatedItemDto>> GetSimilarPublishedLocationsInTerritoryAsync(int locationId, int take);
    Task<List<Event>> GetPublishedEventsForLocationAsync(int locationId, int take);
    Task<bool> IsLocationSavedByUserAsync(int locationId, int userId);
    Task<List<int>> GetSavedCollectionIdsByUserAsync(int locationId, int userId);
    Task<List<LocationReview>> GetReviewsByLocationIdAsync(int locationId, int skip, int take);
    Task<int> CountReviewsByLocationIdAsync(int locationId);
    Task<double?> GetAverageRatingByLocationIdAsync(int locationId);
    Task<bool> HasUserReviewedLocationAsync(int locationId, int userId);
    Task AddReviewAsync(LocationReview review);
    Task<List<Location>> SearchByNameAsync(string query);
}
