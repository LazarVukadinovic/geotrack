using Geotrack.Application.Interfaces.Repository;
using Geotrack.Domain.Entities.Core;
using Geotrack.Domain.Entities.Reviews;
using Microsoft.EntityFrameworkCore;

namespace Geotrack.Infrastructure.Repositories;

public class PlaceRepository : Repository<Place>, IPlaceRepository
{
    private readonly AppDbContext _context;

    public PlaceRepository(AppDbContext context) : base(context)
    {
        _context = context;
    }

    public async Task<Place?> GetWithDetailsAsync(int placeId)
    {
        return await _context.Places
            .Include(p => p.PlaceType)
            .Include(p => p.Location)
            .Include(p => p.Events)
            .Include(p => p.PlaceReviews)
            .Include(p => p.PlaceTags)
                .ThenInclude(pt => pt.Tag)
            .FirstOrDefaultAsync(p => p.Id == placeId);
    }

    public async Task<bool> IsPlaceSavedByUserAsync(int placeId, int userId)
    {
        return await _context.CollectionItems
            .AnyAsync(ci => ci.PlaceId == placeId && ci.Collection.UserId == userId);
    }

    public async Task<List<int>> GetSavedCollectionIdsByUserAsync(int placeId, int userId)
    {
        return await _context.CollectionItems
            .Where(ci => ci.PlaceId == placeId && ci.Collection.UserId == userId)
            .Select(ci => ci.CollectionId)
            .Distinct()
            .ToListAsync();
    }

    public async Task<List<Event>> GetEventsByPlaceIdAsync(int placeId)
    {
        return await _context.Events
            .Where(e => e.PlaceId == placeId)
            .OrderBy(e => e.StartDate)
            .ToListAsync();
    }

    public async Task<List<PlaceReview>> GetReviewsByPlaceIdAsync(int placeId)
    {
        return await _context.PlaceReviews
            .Where(r => r.PlaceId == placeId)
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync();
    }

    public async Task<bool> HasUserReviewedPlaceAsync(int placeId, int userId)
    {
        return await _context.PlaceReviews
            .AnyAsync(r => r.PlaceId == placeId && r.UserId == userId);
    }

    public async Task AddReviewAsync(PlaceReview review)
    {
        await _context.PlaceReviews.AddAsync(review);
    }

    public async Task<List<Place>> SearchByNameAsync(string query)
    {
        return await _context.Places
        .Include(p => p.Location)
        .Where(p => p.Name.ToLower().Contains(query.ToLower()))
        .ToListAsync();
    }
}
