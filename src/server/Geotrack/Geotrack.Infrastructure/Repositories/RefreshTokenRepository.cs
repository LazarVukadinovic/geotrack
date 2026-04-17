using Geotrack.Application.Interfaces.Repository;
using Geotrack.Domain.Entities.Users;
using Microsoft.EntityFrameworkCore;

namespace Geotrack.Infrastructure.Repositories;

public class RefreshTokenRepository : IRefreshTokenRepository
{
    private readonly AppDbContext _context;

    public RefreshTokenRepository(AppDbContext context)
    {
        _context = context;
    }

    public Task<RefreshToken?> GetActiveByTokenAsync(string token)
    {
        return _context.RefreshTokens
            .FirstOrDefaultAsync(rt =>
                rt.Token == token &&
                !rt.IsRevoked &&
                rt.ExpiresAt > DateTime.UtcNow);
    }

    public async Task AddAsync(RefreshToken token)
    {
        await _context.RefreshTokens.AddAsync(token);
    }

    public async Task SaveChangesAsync()
    {
        await _context.SaveChangesAsync();
    }
}
