using Geotrack.Domain.Entities.Users;

namespace Geotrack.Application.Interfaces.Repository;

public interface IPlaceContributionRepository : IRepository<PlaceContribution>
{
    Task<PlaceContribution?> GetByIdWithDetailsAsync(int id);
    Task<List<PlaceContribution>> GetPendingAsync(int skip = 0, int take = 50);
    Task<List<PlaceContribution>> GetByUserIdAsync(int userId);
    Task<bool> HasPendingUpdateForPlaceAsync(int placeId);
}
