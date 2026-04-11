using Geotrack.Domain.Entities.Core;
using Geotrack.Domain.Entities.Reviews;

namespace Geotrack.Application.Interfaces.Repository;

public interface IDestinationRepository : IRepository<Destination>
{
    Task<IEnumerable<Destination>> GetAllWithDetailsAsync();
    Task<Destination?> GetWithDetailsAsync(int destinationId);
    Task<IEnumerable<DestinationReview>> GetReviewsAsync(int destinationId);
}