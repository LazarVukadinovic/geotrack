using Geotrack.Domain.Entities;

namespace Geotrack.Application.Interfaces.Repository;

public interface IUserRepository : IRepository<User>
{
    Task<bool> ExistsAsync(string username, string email);
    Task<User?> GetByUsernameAsync(string username);
}