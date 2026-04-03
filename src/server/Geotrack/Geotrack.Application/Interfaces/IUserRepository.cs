using Geotrack.Domain.Entities;

namespace Geotrack.Application.Interfaces;

public interface IUserRepository : IRepository<User>
{
    Task<bool> ExistsAsync(string username, string email);
    Task<User?> GetByUsernameAsync(string username);
}