using Geotrack.Domain.Entities;

namespace Geotrack.Application.Interfaces;

public interface IDestinationRepository
{
    Task<List<Destination>> GetAllAsync();
    Task<Destination?> GetByIdAsync(int id);
    Task AddAsync(Destination destination);
    void Delete(Destination destination);
    Task SaveChangesAsync();
}