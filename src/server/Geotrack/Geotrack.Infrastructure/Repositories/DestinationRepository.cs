using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Geotrack.Application.Interfaces;
using Geotrack.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Geotrack.Infrastructure.Repositories
{
    public class DestinationRepository : Repository<Destination>, IDestinationRepository
    {
        public DestinationRepository(AppDbContext context) : base(context) { }

        public new async Task<IEnumerable<Destination>> GetAllAsync()
        {
            return await context.Set<Destination>().Include(d => d.Locations).Include(d => d.DestinationTags).ThenInclude(dt => dt.Tag).ToListAsync();
        }
    }

}
