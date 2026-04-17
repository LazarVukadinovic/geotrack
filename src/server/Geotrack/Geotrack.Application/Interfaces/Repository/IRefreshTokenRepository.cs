using Geotrack.Domain.Entities.Users;

namespace Geotrack.Application.Interfaces.Repository;

public interface IRefreshTokenRepository
{
    Task<RefreshToken?> GetActiveByTokenAsync(string token);
    Task AddAsync(RefreshToken token);
    Task SaveChangesAsync();
}
