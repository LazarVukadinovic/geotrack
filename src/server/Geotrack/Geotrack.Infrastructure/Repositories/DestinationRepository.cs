using Geotrack.Application.Interfaces;
using Geotrack.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Geotrack.Infrastructure.Repositories;

public class DestinationRepository : Repository<Destination>, IDestinationRepository
{
    private readonly AppDbContext _context;

    public DestinationRepository(AppDbContext context) : base(context)
    {
        _context = context;
    }

    public async Task<IEnumerable<Destination>> GetAllWithDetailsAsync()
    {
        return await _context.Destinations
            .Include(d => d.DestinationTags)
                .ThenInclude(dt => dt.Tag)
            .Include(d => d.Locations)
                .ThenInclude(l => l.Places)
            .ToListAsync();
    }
}