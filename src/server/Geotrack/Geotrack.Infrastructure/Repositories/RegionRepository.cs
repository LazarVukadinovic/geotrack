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

        public async Task<List<Region>> GetAllAsync()
        {
            return await _context.Regions.OrderBy(r => r.Name).ToListAsync();
        }
    }
}
