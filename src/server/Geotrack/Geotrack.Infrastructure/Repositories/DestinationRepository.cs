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
    public async Task<Destination?> GetWithDetailsAsync(int destinationId)
    {
        return await _context.Destinations
            .AsNoTracking()
            .Include(d => d.DestinationType)
            .Include(d => d.Region)
            .Include(d => d.Images)
            .Include(d => d.DestinationTags)
                .ThenInclude(dt => dt.Tag)
                    .ThenInclude(tag => tag.TagType)
                        .ThenInclude(tagType => tagType!.PlaceTypes)
            .Include(d => d.Locations)
                .ThenInclude(l => l.Images)
            .Include(d => d.Locations)
                .ThenInclude(l => l.LocationTags)
                .ThenInclude(lt => lt.Tag)
                    .ThenInclude(tag => tag.TagType)
                        .ThenInclude(tagType => tagType!.PlaceTypes)
            .Include(d => d.Locations)
                .ThenInclude(l => l.Places)
                    .ThenInclude(p => p.Images)
            .Include(d => d.Locations)
                .ThenInclude(l => l.Places)
                .ThenInclude(p => p.PlaceTags)
                .ThenInclude(pt => pt.Tag)
                    .ThenInclude(tag => tag.TagType)
                        .ThenInclude(tagType => tagType!.PlaceTypes)
            .AsSplitQuery()
            .FirstOrDefaultAsync(d => d.Id == destinationId);
    }

    public async Task<List<Destination>> GetSimilarPublishedDestinationsInTerritoryAsync(int destinationId, int take)
    {
        var territoryId = await _context.Destinations
            .AsNoTracking()
            .Where(d => d.Id == destinationId)
            .Select(d => (int?)d.Region.TerritoryId)
            .FirstOrDefaultAsync();

        if (!territoryId.HasValue)
        {
            return new List<Destination>();
        }

        return await _context.Destinations
            .AsNoTracking()
            .Include(d => d.Images)
            .Where(d => d.Id != destinationId &&
                        d.Status == "Published" &&
                        d.Region.TerritoryId == territoryId.Value)
            .OrderBy(d => d.Name)
            .Take(take)
            .ToListAsync();
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
    public async Task<List<DestinationReview>> GetReviewsByDestinationIdAsync(int destinationId, int skip, int take)
    {
        return await _context.DestinationReviews
            .AsNoTracking()
            .Where(r => r.DestinationId == destinationId)
            .OrderByDescending(r => r.CreatedAt)
            .ThenByDescending(r => r.Id)
            .Skip(skip)
            .Take(take)
            .ToListAsync();
    }
    public async Task<int> CountReviewsByDestinationIdAsync(int destinationId)
    {
        return await _context.DestinationReviews
            .AsNoTracking()
            .CountAsync(r => r.DestinationId == destinationId);
    }

    public async Task<double?> GetAverageRatingByDestinationIdAsync(int destinationId)
    {
        return await _context.DestinationReviews
            .AsNoTracking()
            .Where(r => r.DestinationId == destinationId)
            .Select(r => (double?)r.Rating)
            .AverageAsync();
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
            .Include(e => e.Destination)
            .Include(e => e.Location)
            .Include(e => e.Place)
            .Where(e => e.DestinationId == destinationId && e.Name.ToLower().Contains(normalizedQuery))
            .ToListAsync();
    }
}
