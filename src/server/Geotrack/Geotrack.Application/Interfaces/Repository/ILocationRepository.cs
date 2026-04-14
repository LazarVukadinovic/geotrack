using Geotrack.Domain.Entities.Core;
using Geotrack.Domain.Entities.Reviews;

namespace Geotrack.Application.Interfaces.Repository;

public interface ILocationRepository : IRepository<Location>
{
    Task<Location?> GetWithDetailsAsync(int locationId);
    Task<IEnumerable<LocationReview>> GetReviewsByLocationIdAsync(int locationId);
    Task<bool> HasUserReviewedLocationAsync(int locationId, int userId);
    Task<LocationReview> CreateReviewAsync(LocationReview review);
}