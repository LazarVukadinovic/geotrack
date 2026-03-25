using Geotrack.Domain.Entities;

namespace Geotrack.Application.Interfaces;

public interface IUserRepository
{
    Task<List<User>> GetAllAsync();
    Task<User?> GetByIdAsync(int id);
    Task AddAsync(User user);
    void Delete(User user);
    Task SaveChangesAsync();
}