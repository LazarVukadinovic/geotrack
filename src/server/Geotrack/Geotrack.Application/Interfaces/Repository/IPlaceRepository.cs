using Geotrack.Domain.Entities.Core;

namespace Geotrack.Application.Interfaces.Repository;

public interface IPlaceRepository : IRepository<Place>
{
    Task<IEnumerable<Place>> GetByLocationIdAsync(int locationId);
}