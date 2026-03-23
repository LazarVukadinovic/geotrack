using Geotrack.Domain.Entities;

namespace Geotrack.Application.Interfaces;

public interface IRoleRepository
{
    Task<List<Role>> GetAllAsync();
    Task<Role?> GetByIdAsync(int id);
    Task AddAsync(Role role);
    void Delete(Role role);
    Task SaveChangesAsync();
}