using Geotrack.Domain.Entities.Core;

using Geotrack.Domain.Entities.Reviews;
namespace Geotrack.Application.Interfaces.Repository;
public interface IDestinationRepository : IRepository<Destination>
{
    Task<List<Destination>> GetAllWithExploreDataAsync();
    Task<Destination?> GetWithDetailsAsync(int destinationId);
    Task<bool> IsDestinationSavedByUserAsync(int destinationId, int userId);
    Task<List<int>> GetSavedCollectionIdsByUserAsync(int destinationId, int
    userId);
    Task<List<DestinationReview>> GetReviewsByDestinationIdAsync(int
    destinationId);
    Task<bool> HasUserReviewedDestinationAsync(int destinationId, int
    userId);
    Task AddReviewAsync(DestinationReview review);
}
