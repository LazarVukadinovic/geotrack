using Geotrack.Domain.Entities.Core;

namespace Geotrack.Application.Interfaces.Repository;

public interface IDestinationRepository : IRepository<Destination>
{
    Task<IEnumerable<Destination>> GetAllWithDetailsAsync();
}