using Geotrack.Domain.Entities.Users;

namespace Geotrack.Application.Interfaces.Repository;

public interface IRefreshTokenRepository
{
    Task<RefreshToken?> GetActiveByTokenHashAsync(string tokenHash);
    Task AddAsync(RefreshToken token);
    Task RevokeActiveForUserAsync(int userId, DateTime revokedAt);
    Task SaveChangesAsync();
}
