using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Geotrack.Application.Interfaces.Repository;
using Geotrack.Domain.Entities.Core;
using Geotrack.Domain.Entities.Reviews;
using Microsoft.EntityFrameworkCore;

namespace Geotrack.Infrastructure.Repositories
{
    public class LocationRepository : Repository<Location>, ILocationRepository
    {

        private readonly AppDbContext _context;

        public LocationRepository(AppDbContext context) : base(context)
        {
            _context = context;
        }

        public async Task<Location?> GetWithDetailsAsync(int locationId) => await _context.Locations.Include(l => l.Places).Include(l => l.Events).Include(l => l.LocationReviews).Include(l => l.LocationTags).FirstOrDefaultAsync(l => l.Id == locationId);

        public async Task<IEnumerable<LocationReview>> GetReviewsByLocationIdAsync(int locationId)
        {
            return await _context.LocationReviews.Where(r => r.LocationId == locationId).OrderByDescending(r => r.CreatedAt).ToListAsync();
        }

        public async Task<bool> HasUserReviewedLocationAsync(int locationId, int userId)
        {
            return await _context.LocationReviews.AnyAsync(r => r.LocationId == locationId && r.UserId == userId);
        }

        public async Task<LocationReview> CreateReviewAsync(LocationReview review)
        {
            _context.LocationReviews.Add(review);
            await _context.SaveChangesAsync();
            return review;
        }
    }
}
