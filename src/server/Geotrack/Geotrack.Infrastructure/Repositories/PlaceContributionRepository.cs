using Geotrack.Application.Interfaces.Repository;
using Geotrack.Domain.Entities.Users;
using Microsoft.EntityFrameworkCore;

namespace Geotrack.Infrastructure.Repositories;

public class PlaceContributionRepository : Repository<PlaceContribution>, IPlaceContributionRepository
{
    private readonly AppDbContext _context;

    public PlaceContributionRepository(AppDbContext context) : base(context)
    {
        _context = context;
    }

    public async Task<PlaceContribution?> GetByIdWithDetailsAsync(int id)
    {
        return await _context.PlaceContributions
            .Include(x => x.User)
            .Include(x => x.ReviewedByUser)
            .Include(x => x.Place)
                .ThenInclude(p => p!.Images)
            .Include(x => x.Place)
                .ThenInclude(p => p!.Stay)
                    .ThenInclude(s => s!.StayType)
            .Include(x => x.PlaceType)
            .Include(x => x.Location)
                .ThenInclude(l => l!.Destination)
            .Include(x => x.RestaurantType)
            .Include(x => x.StayType)
            .Include(x => x.Images)
            .AsSplitQuery()
            .FirstOrDefaultAsync(x => x.Id == id);
    }

    public async Task<List<PlaceContribution>> GetPendingAsync(int skip = 0, int take = 50)
    {
        return await _context.PlaceContributions
            .AsNoTracking()
            .Where(x => x.Status == PlaceContributionStatus.PendingReview)
            .Include(x => x.User)
            .Include(x => x.Place)
            .Include(x => x.PlaceType)
            .Include(x => x.Location)
                .ThenInclude(l => l!.Destination)
            .Include(x => x.RestaurantType)
            .Include(x => x.StayType)
            .Include(x => x.Images)
            .OrderBy(x => x.SubmittedAt)
            .Skip(Math.Max(0, skip))
            .Take(Math.Clamp(take, 1, 100))
            .ToListAsync();
    }

    public async Task<List<PlaceContribution>> GetByUserIdAsync(int userId)
    {
        return await _context.PlaceContributions
            .AsNoTracking()
            .Where(x => x.UserId == userId)
            .Include(x => x.Place)
            .Include(x => x.PlaceType)
            .Include(x => x.Location)
            .Include(x => x.Images)
            .OrderByDescending(x => x.SubmittedAt)
            .ToListAsync();
    }

    public async Task<bool> HasPendingUpdateForPlaceAsync(int placeId)
    {
        return await _context.PlaceContributions
            .AnyAsync(x =>
                x.PlaceId == placeId &&
                x.Type == PlaceContributionType.UpdatePlace &&
                x.Status == PlaceContributionStatus.PendingReview);
    }
}
