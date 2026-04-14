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
    public class PlaceRepository : Repository<Place>, IPlaceRepository
    {
        private readonly AppDbContext _context;

        public PlaceRepository(AppDbContext context) : base(context)
        { 
            _context = context;
        }
        public async Task<IEnumerable<Place>> GetByLocationIdAsync(int locationId)
        {
            return await _context.Places.Where(p => p.LocationId == locationId).ToListAsync();
        }
    }
}
