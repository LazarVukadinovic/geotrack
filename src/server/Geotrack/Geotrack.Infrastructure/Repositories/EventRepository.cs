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
    public class EventRepository : Repository<Event>, IEventRepository
    {
        private readonly AppDbContext _context;
        public EventRepository(AppDbContext context) : base(context)
        {
            _context = context;
        }

        public async Task<IEnumerable<Event>> GetByLocationIdAsync(int locationId)
        {
            return await _context.Events.Where(e => e.LocationId == locationId).ToListAsync();
        }

        public async Task<List<int>> GetSavedCollectionIdsByUserAsync(int eventId, int userId)
        {
            return await _context.CollectionItems.Where(ci => ci.EventId == eventId && ci.Collection.UserId == userId).Select(ci => ci.CollectionId).ToListAsync();
        }

        public async Task<Event?> GetWithDetailsAsync(int eventId)
        {
            return await _context.Events.Include(e => e.EventType).Include(e => e.Location).Include(e => e.Place).Include(e => e.EventReviews).Include(e => e.EventTags).FirstOrDefaultAsync(e => e.Id == eventId);
        }

        public async Task<bool> IsEventSavedByUserAsync(int eventId, int userId)
        {
            return await _context.CollectionItems.AnyAsync(ci => ci.EventId == eventId && ci.Collection.UserId == userId);
        }
    }
}
