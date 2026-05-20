using Geotrack.Application.Interfaces.Repository;
using Geotrack.Domain.Entities.Reviews;
using Geotrack.Domain.Entities.Users;
using Microsoft.EntityFrameworkCore;

namespace Geotrack.Infrastructure.Repositories;

public class ReviewRepository : IReviewRepository
{
    private readonly AppDbContext _db;

    public ReviewRepository(AppDbContext db)
    {
        _db = db;
    }

    public async Task<int> CountPlaceReviewsAsync(int placeId)
    {
        return await _db.PlaceReviews
            .AsNoTracking()
            .CountAsync(review => review.PlaceId == placeId);
    }

    public async Task<double?> GetPlaceAverageRatingAsync(int placeId)
    {
        return await _db.PlaceReviews
            .AsNoTracking()
            .Where(review => review.PlaceId == placeId)
            .AverageAsync(review => (double?)review.Rating);
    }

    public async Task<List<PlaceReview>> GetPlaceReviewsPageAsync(int placeId, int skip, int take)
    {
        return await _db.PlaceReviews
            .AsNoTracking()
            .Include(review => review.User)
            .Where(review => review.PlaceId == placeId)
            .OrderByDescending(review => review.CreatedAt)
            .ThenByDescending(review => review.Id)
            .Skip(skip)
            .Take(take)
            .ToListAsync();
    }

    public async Task<int> CountEventReviewsAsync(int eventId)
    {
        return await _db.EventReviews
            .AsNoTracking()
            .CountAsync(review => review.EventId == eventId);
    }

    public async Task<double?> GetEventAverageRatingAsync(int eventId)
    {
        return await _db.EventReviews
            .AsNoTracking()
            .Where(review => review.EventId == eventId)
            .AverageAsync(review => (double?)review.Rating);
    }

    public async Task<List<EventReview>> GetEventReviewsPageAsync(int eventId, int skip, int take)
    {
        return await _db.EventReviews
            .AsNoTracking()
            .Include(review => review.User)
            .Where(review => review.EventId == eventId)
            .OrderByDescending(review => review.CreatedAt)
            .ThenByDescending(review => review.Id)
            .Skip(skip)
            .Take(take)
            .ToListAsync();
    }

    public async Task<HashSet<int>> GetReportedReviewIdsAsync(
        int reporterUserId,
        ReviewReportTargetType targetType,
        IEnumerable<int> reviewIds)
    {
        var ids = reviewIds.Distinct().ToList();
        if (ids.Count == 0)
        {
            return new HashSet<int>();
        }

        var reportedIds = await _db.ReviewReports
            .AsNoTracking()
            .Where(report =>
                report.ReporterUserId == reporterUserId &&
                report.TargetType == targetType &&
                ids.Contains(report.ReviewId))
            .Select(report => report.ReviewId)
            .ToListAsync();

        return reportedIds.ToHashSet();
    }

    public async Task<bool> PlaceExistsAsync(int placeId)
    {
        return await _db.Places.AnyAsync(place => place.Id == placeId);
    }

    public async Task<bool> EventExistsAsync(int eventId)
    {
        return await _db.Events.AnyAsync(evt => evt.Id == eventId);
    }

    public async Task<bool> HasUserReviewedPlaceAsync(int placeId, int userId)
    {
        return await _db.PlaceReviews.AnyAsync(
            review => review.PlaceId == placeId && review.UserId == userId);
    }

    public async Task<bool> HasUserReviewedEventAsync(int eventId, int userId)
    {
        return await _db.EventReviews.AnyAsync(
            review => review.EventId == eventId && review.UserId == userId);
    }

    public void AddPlaceReview(PlaceReview review)
    {
        _db.PlaceReviews.Add(review);
    }

    public void AddEventReview(EventReview review)
    {
        _db.EventReviews.Add(review);
    }

    public async Task UpdatePlaceRatingStatsAsync(int placeId)
    {
        var place = await _db.Places.FindAsync(placeId);
        if (place is null)
        {
            return;
        }

        var stats = await _db.PlaceReviews
            .Where(review => review.PlaceId == placeId)
            .GroupBy(review => review.PlaceId)
            .Select(group => new
            {
                Average = group.Average(review => review.Rating),
                Count = group.Count()
            })
            .FirstOrDefaultAsync();

        if (stats is null)
        {
            return;
        }

        place.AverageRating = Math.Round(stats.Average, 1);
        place.ReviewCount = stats.Count;
    }

    public async Task<User?> GetUserByIdAsync(int userId)
    {
        return await _db.Users.FindAsync(userId);
    }

    // --- User review history ---

    public async Task<int> CountPlaceReviewsByUserAsync(int userId)
    {
        return await _db.PlaceReviews
            .AsNoTracking()
            .CountAsync(r => r.UserId == userId);
    }

    public async Task<List<PlaceReview>> GetPlaceReviewsByUserPageAsync(int userId, int skip, int take)
    {
        return await _db.PlaceReviews
            .AsNoTracking()
            .Include(r => r.Place)
            .Where(r => r.UserId == userId)
            .OrderByDescending(r => r.CreatedAt)
            .ThenByDescending(r => r.Id)
            .Skip(skip)
            .Take(take)
            .ToListAsync();
    }

    public async Task<int> CountEventReviewsByUserAsync(int userId)
    {
        return await _db.EventReviews
            .AsNoTracking()
            .CountAsync(r => r.UserId == userId);
    }

    public async Task<List<EventReview>> GetEventReviewsByUserPageAsync(int userId, int skip, int take)
    {
        return await _db.EventReviews
            .AsNoTracking()
            .Include(r => r.Event)
            .Where(r => r.UserId == userId)
            .OrderByDescending(r => r.CreatedAt)
            .ThenByDescending(r => r.Id)
            .Skip(skip)
            .Take(take)
            .ToListAsync();
    }

    // --- Single review lookup ---

    public async Task<PlaceReview?> GetPlaceReviewByIdAsync(int reviewId)
    {
        return await _db.PlaceReviews
            .Include(r => r.User)
            .FirstOrDefaultAsync(r => r.Id == reviewId);
    }

    public async Task<EventReview?> GetEventReviewByIdAsync(int reviewId)
    {
        return await _db.EventReviews
            .Include(r => r.User)
            .FirstOrDefaultAsync(r => r.Id == reviewId);
    }

    // --- Delete ---

    public void RemovePlaceReview(PlaceReview review)
    {
        _db.PlaceReviews.Remove(review);
    }

    public void RemoveEventReview(EventReview review)
    {
        _db.EventReviews.Remove(review);
    }

    public async Task SaveChangesAsync()
    {
        await _db.SaveChangesAsync();
    }
}
