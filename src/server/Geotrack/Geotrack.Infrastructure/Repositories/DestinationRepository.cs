using Geotrack.Application.Interfaces.Repository;
using Geotrack.Domain.Entities.Core;
using Microsoft.EntityFrameworkCore;

namespace Geotrack.Infrastructure.Repositories;

public class DestinationRepository : Repository<Destination>, IDestinationRepository
{
    private readonly AppDbContext _context;

    public DestinationRepository(AppDbContext context) : base(context)
    {
        _context = context;
    }

    public async Task<List<Destination>> GetAllWithExploreDataAsync()
    {
        return await _context.Destinations
            .Include(d => d.DestinationTags)
                .ThenInclude(dt => dt.Tag)

            .Include(d => d.Locations)
                .ThenInclude(l => l.LocationTags)
                    .ThenInclude(lt => lt.Tag)

            .Include(d => d.Locations)
                .ThenInclude(l => l.LocationReviews)

            .Include(d => d.Locations)
                .ThenInclude(l => l.Places)
                    .ThenInclude(p => p.PlaceTags)
                        .ThenInclude(pt => pt.Tag)

            .Include(d => d.Locations)
                .ThenInclude(l => l.Places)
                    .ThenInclude(p => p.PlaceReviews)

            .Include(d => d.Locations)
                .ThenInclude(l => l.Events)
                    .ThenInclude(e => e.EventTags)
                        .ThenInclude(et => et.Tag)

            .Include(d => d.Locations)
                .ThenInclude(l => l.Events)
                    .ThenInclude(e => e.EventReviews)

            .Include(d => d.Locations)
                .ThenInclude(l => l.Events)
                    .ThenInclude(e => e.Place)

            .ToListAsync();
    }
}