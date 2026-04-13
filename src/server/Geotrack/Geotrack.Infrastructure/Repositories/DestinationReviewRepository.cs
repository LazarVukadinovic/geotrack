using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Geotrack.Application.Interfaces.Repository;
using Geotrack.Domain.Entities.Reviews;
using Microsoft.EntityFrameworkCore;

namespace Geotrack.Infrastructure.Repositories
{
    public class DestinationReviewRepository : IDestinationReviewRepository
    {

        private readonly AppDbContext _context;

        public DestinationReviewRepository(AppDbContext context)
        {
            _context = context;
        }

        public async Task<DestinationReview> CreateAsync(DestinationReview review)
        {
            _context.DestinationReviews.Add(review);
            await _context.SaveChangesAsync();
            return review;
        }

        public async Task<bool> ExistsByDestinationAndUserAsync(int destinationId, int userId) => await _context.DestinationReviews.AnyAsync(r => r.DestinationId == destinationId && r.UserId == userId);


    }
}
