using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Geotrack.Application.Interfaces.Repository;
using Geotrack.Domain.Entities.Core;
using Microsoft.EntityFrameworkCore;

namespace Geotrack.Infrastructure.Repositories
{
    public class RegionRepository : Repository<Region>, IRegionRepository
    {
        private readonly AppDbContext _context;
        public RegionRepository(AppDbContext context) : base(context)
        {
            _context = context;
        }

        public async Task<bool> ExistsAsync(string name)
        {
            return await _context.Regions
                .AnyAsync(r => r.Name == name);
        }

        public async Task<bool> HasDestinationsAsync(int regionId)
        {
            return await _context.Destinations
                .AnyAsync(d => d.RegionId == regionId);
        }

        public Task DeleteAsync(Region region)
        {
            _context.Regions.Remove(region);
            return Task.CompletedTask;
        }

        public async Task<Region?> GetByIdWithManagersAsync(int regionId)
        {
            return await _context.Regions
                .Include(r => r.Territory)
                .Include(r => r.RegionalManagerRegions)
                    .ThenInclude(ror => ror.User)
                .Include(r => r.Destinations)
                .AsSplitQuery()
                .FirstOrDefaultAsync(r => r.Id == regionId);
        }
        public async Task<bool> ExistsInTerritoryAsync(int territoryId, string name)
        {
            return await _context.Regions
                .AnyAsync(r => r.TerritoryId == territoryId && r.Name == name);
        }
    }
}
