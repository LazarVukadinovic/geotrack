using Geotrack.Application.Interfaces.Repository;
using Geotrack.Domain.Entities.Core;
using Geotrack.Domain.Entities.Reviews;
using Microsoft.EntityFrameworkCore;

namespace Geotrack.Infrastructure.Repositories;

public class EventRepository : Repository<Event>, IEventRepository
{
    private readonly AppDbContext _context;

    public EventRepository(AppDbContext context) : base(context)
    {
        _context = context;
    }

    public async Task<Event?> GetWithDetailsAsync(int eventId)
    {
        return await _context.Events
            .Include(e => e.EventType)
            .Include(e => e.Location)
            .Include(e => e.Place)
            .Include(e => e.EventReviews)
            .Include(e => e.EventTags)
                .ThenInclude(et => et.Tag)
            .FirstOrDefaultAsync(e => e.Id == eventId);
    }

    public async Task<bool> IsEventSavedByUserAsync(int eventId, int userId)
    {
        return await _context.CollectionItems
            .AnyAsync(ci => ci.EventId == eventId && ci.Collection.UserId == userId);
    }

    public async Task<List<int>> GetSavedCollectionIdsByUserAsync(int eventId, int userId)
    {
        return await _context.CollectionItems
            .Where(ci => ci.EventId == eventId && ci.Collection.UserId == userId)
            .Select(ci => ci.CollectionId)
            .Distinct()
            .ToListAsync();
    }

    public async Task<List<EventReview>> GetReviewsByEventIdAsync(int eventId)
    {
        return await _context.EventReviews
            .Where(r => r.EventId == eventId)
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync();
    }

    public async Task<bool> HasUserReviewedEventAsync(int eventId, int userId)
    {
        return await _context.EventReviews
            .AnyAsync(r => r.EventId == eventId && r.UserId == userId);
    }

    public async Task AddReviewAsync(EventReview review)
    {
        await _context.EventReviews.AddAsync(review);
    }
}