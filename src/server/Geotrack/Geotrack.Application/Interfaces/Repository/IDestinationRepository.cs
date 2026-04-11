using Geotrack.Domain.Entities;

namespace Geotrack.Application.Interfaces.Repository;

public interface IDestinationRepository : IRepository<Destination>
{
    Task<IEnumerable<Destination>> GetAllWithDetailsAsync();
}