using Geotrack.Application.Interfaces.Repository;
using Geotrack.Domain.Entities.Core;
using Microsoft.EntityFrameworkCore;

namespace Geotrack.Infrastructure.Repositories;

public class TerritoryRepository : Repository<Territory>, ITerritoryRepository
{
    private readonly AppDbContext _context;

    public TerritoryRepository(AppDbContext context) : base(context)
    {
        _context = context;
    }

    public async Task<bool> ExistsAsync(string name)
    {
        return await _context.Set<Territory>()
            .AnyAsync(t => t.Name == name);
    }

    public async Task<Territory?> GetByIdWithRegionsAsync(int territoryId)
    {
        return await _context.Set<Territory>()
            .Include(t => t.Images)
            .Include(t => t.Regions)
                .ThenInclude(r => r.RegionalManagerRegions)
            .Include(t => t.Regions)
                .ThenInclude(r => r.Destinations)
            .AsSplitQuery()
            .FirstOrDefaultAsync(t => t.Id == territoryId);
    }

    public Task DeleteAsync(Territory territory)
    {
        _context.Set<Territory>().Remove(territory);
        return Task.CompletedTask;
    }
}
