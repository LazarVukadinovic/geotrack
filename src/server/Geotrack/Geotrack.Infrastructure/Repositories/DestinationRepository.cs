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
    public async Task<Destination?> GetWithDetailsAsync(int destinationId)
    {
        return await _context.Destinations
            .Include(d => d.DestinationType)
            .Include(d => d.Region)
            .Include(d => d.DestinationTags)
                .ThenInclude(dt => dt.Tag)
            .Include(d => d.DestinationReviews)
            .Include(d => d.Locations)
                .ThenInclude(l => l.LocationTags)
                .ThenInclude(lt => lt.Tag)
            .Include(d => d.Locations)
                .ThenInclude(l => l.Places)
                .ThenInclude(p => p.PlaceTags)
                .ThenInclude(pt => pt.Tag)
            .FirstOrDefaultAsync(d => d.Id == destinationId);
    }
    public async Task<bool> IsDestinationSavedByUserAsync(int destinationId, int userId)
    {
        return await _context.CollectionItems
            .AnyAsync(ci => ci.DestinationId == destinationId &&
            ci.Collection.UserId == userId);
    }
    public async Task<List<int>> GetSavedCollectionIdsByUserAsync(int destinationId, int userId)
    {
        return await _context.CollectionItems
            .Where(ci => ci.DestinationId == destinationId &&
            ci.Collection.UserId == userId)
            .Select(ci => ci.CollectionId)
            .Distinct()
            .ToListAsync();
    }
    public async Task<List<DestinationReview>> GetReviewsByDestinationIdAsync(int destinationId)
    {
        return await _context.DestinationReviews
            .Where(r => r.DestinationId == destinationId)
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync();
    }
    public async Task<bool> HasUserReviewedDestinationAsync(int destinationId, int userId)
    {
        return await _context.DestinationReviews
            .AnyAsync(r => r.DestinationId == destinationId && r.UserId ==
            userId);
    }
    public async Task AddReviewAsync(DestinationReview review)
    {
        await _context.DestinationReviews.AddAsync(review);
    }

    public async Task<List<Destination>> SearchByNameAsync(string query)
    {
        return await _context.Destinations.Where(d => d.Name.ToLower().Contains(query.ToLower())).ToListAsync();
    }

    public async Task<bool> ExistsAsync(int destinationId)
    {
        return await _context.Destinations.AnyAsync(d => d.Id == destinationId);
    }

    public async Task<List<Location>> SearchLocationsAsync(int destinationId, string query)
    {
        return await _context.Locations.Where(l => l.DestinationId == destinationId && l.Name.ToLower().Contains(query.ToLower())).ToListAsync();
    }

    public async Task<List<Place>> SearchPlacesAsync(int destinationId, string query)
    {
        return await _context.Places.Include(p => p.Location).Where(p => p.Location.DestinationId == destinationId && p.Name.ToLower().Contains(query.ToLower())).ToListAsync();
    }

    public async Task<List<Event>> SearchEventsAsync(int destinationId, string query)
    {
        var normalizedQuery = query.ToLower();
        return await _context.Events
            .Include(e => e.Location)
            .Include(e => e.Place)
            .Where(e => e.Location.DestinationId == destinationId && e.Name.ToLower().Contains(normalizedQuery))
            .ToListAsync();
    }
}
