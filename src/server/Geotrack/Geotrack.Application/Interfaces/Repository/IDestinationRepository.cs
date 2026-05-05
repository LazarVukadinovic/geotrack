using Geotrack.Domain.Entities.Core;

using Geotrack.Domain.Entities.Reviews;
namespace Geotrack.Application.Interfaces.Repository;
public interface IDestinationRepository : IRepository<Destination>
{
    Task<Destination?> GetWithDetailsAsync(int destinationId);
    Task<bool> IsDestinationSavedByUserAsync(int destinationId, int userId);
    Task<List<int>> GetSavedCollectionIdsByUserAsync(int destinationId, int
    userId);
    Task<List<DestinationReview>> GetReviewsByDestinationIdAsync(int destinationId, int skip, int take);
    Task<int> CountReviewsByDestinationIdAsync(int destinationId);
    Task<double?> GetAverageRatingByDestinationIdAsync(int destinationId);
    Task<bool> HasUserReviewedDestinationAsync(int destinationId, int
    userId);
    Task AddReviewAsync(DestinationReview review);
    Task<List<Destination>> SearchByNameAsync(string query);
    Task<bool> ExistsAsync(int destinationId);
    Task<List<Location>> SearchLocationsAsync(int destinationId,string query);
    Task<List<Place>> SearchPlacesAsync(int destinationId,string query);
    Task<List<Event>> SearchEventsAsync(int destinationId,string query);
}
