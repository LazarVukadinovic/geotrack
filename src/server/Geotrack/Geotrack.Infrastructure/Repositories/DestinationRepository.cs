using Geotrack.Application.Interfaces.Repository;
using Geotrack.Domain.Entities.Core;
using Geotrack.Domain.Entities.Reviews;
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
        .Include(d => d.DestinationReviews)
        .ToListAsync();
    }

    public async Task<IEnumerable<DestinationReview>> GetReviewsAsync(int destinationId)
    {
        return await _context.DestinationReviews.Where(r => r.DestinationId == destinationId).ToListAsync();
    }

    public async Task<Destination?> GetWithDetailsAsync(int destinationId)
    {
        return await _context.Destinations
            .Include(d => d.DestinationTags)
                .ThenInclude(dt => dt.Tag)
            .Include(d => d.Locations)
                .ThenInclude(l => l.Places)
            .Include(d => d.DestinationReviews)
    .FirstOrDefaultAsync(d => d.Id == destinationId);
    }
}
