using Geotrack.Application.DTOs.Reviews;
using Geotrack.Application.Interfaces.Service;
using Geotrack.Domain.Entities.Reviews;
using Geotrack.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Geotrack.Infrastructure.Services;

public class ReviewService : IReviewService
{
    private readonly AppDbContext _db;
    private readonly IReviewContentModerationService _contentModeration;

    public ReviewService(AppDbContext db, IReviewContentModerationService contentModeration)
    {
        _db = db;
        _contentModeration = contentModeration;
    }

    public async Task<List<ReviewDto>> GetPlaceReviewsAsync(int placeId, int? currentUserId = null)
    {
        var reviews = await _db.PlaceReviews
            .Include(r => r.User)
            .Where(r => r.PlaceId == placeId)
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync();

        var reportedReviewIds = await GetReportedReviewIdsAsync(
            currentUserId,
            ReviewReportTargetType.Place,
            reviews.Select(review => review.Id));

        return reviews.Select(review => MapReview(review, reportedReviewIds.Contains(review.Id))).ToList();
    }

    public async Task<List<ReviewDto>> GetEventReviewsAsync(int eventId, int? currentUserId = null)
    {
        var reviews = await _db.EventReviews
            .Include(r => r.User)
            .Where(r => r.EventId == eventId)
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync();

        var reportedReviewIds = await GetReportedReviewIdsAsync(
            currentUserId,
            ReviewReportTargetType.Event,
            reviews.Select(review => review.Id));

        return reviews.Select(r => new ReviewDto
        {
            Id = r.Id,
            AuthorName = GetAuthorName(r.User),
            AvatarUrl = r.User.AvatarUrl,
            Rating = (int)r.Rating,
            TimeAgo = FormatTimeAgo(r.CreatedAt),
            Body = r.Comment,
            HelpfulCount = 0,
            ReportedByCurrentUser = reportedReviewIds.Contains(r.Id)
        }).ToList();
    }

    public async Task<ReviewDto> CreatePlaceReviewAsync(int placeId, int userId, CreateReviewDto dto)
    {
        await ValidateReviewAsync(dto);

        if (!await _db.Places.AnyAsync(p => p.Id == placeId))
            throw new KeyNotFoundException("Place not found");

        if (await _db.PlaceReviews.AnyAsync(r => r.PlaceId == placeId && r.UserId == userId))
            throw new InvalidOperationException("User has already reviewed this place");

        var review = new PlaceReview
        {
            PlaceId = placeId,
            UserId = userId,
            Rating = dto.Rating,
            Comment = dto.Comment?.Trim() ?? string.Empty,
            CreatedAt = DateTime.UtcNow
        };

        _db.PlaceReviews.Add(review);
        await _db.SaveChangesAsync();

        // Update denormalized fields
        var place = await _db.Places.FindAsync(placeId);
        if (place != null)
        {
            var stats = await _db.PlaceReviews
                .Where(r => r.PlaceId == placeId)
                .GroupBy(r => r.PlaceId)
                .Select(g => new { Avg = g.Average(r => r.Rating), Count = g.Count() })
                .FirstOrDefaultAsync();

            if (stats != null)
            {
                place.AverageRating = Math.Round(stats.Avg, 1);
                place.ReviewCount = stats.Count;
                await _db.SaveChangesAsync();
            }
        }

        var user = await _db.Users.FindAsync(userId);
        review.User = user!;
        return MapReview(review, false);
    }

    public async Task<ReviewDto> CreateEventReviewAsync(int eventId, int userId, CreateReviewDto dto)
    {
        await ValidateReviewAsync(dto);

        if (!await _db.Events.AnyAsync(e => e.Id == eventId))
            throw new KeyNotFoundException("Event not found");

        if (await _db.EventReviews.AnyAsync(r => r.EventId == eventId && r.UserId == userId))
            throw new InvalidOperationException("User has already reviewed this event");

        var review = new EventReview
        {
            EventId = eventId,
            UserId = userId,
            Rating = dto.Rating,
            Comment = dto.Comment?.Trim() ?? string.Empty,
            CreatedAt = DateTime.UtcNow
        };

        _db.EventReviews.Add(review);
        await _db.SaveChangesAsync();

        var user = await _db.Users.FindAsync(userId);
        review.User = user!;

        return new ReviewDto
        {
            Id = review.Id,
            AuthorName = GetAuthorName(user!),
            AvatarUrl = user?.AvatarUrl,
            Rating = (int)review.Rating,
            TimeAgo = "just now",
            Body = review.Comment,
            HelpfulCount = 0,
            ReportedByCurrentUser = false
        };
    }

    private static ReviewDto MapReview(PlaceReview r, bool reportedByCurrentUser) => new()
    {
        Id = r.Id,
        AuthorName = GetAuthorName(r.User),
        AvatarUrl = r.User.AvatarUrl,
        Rating = (int)r.Rating,
        TimeAgo = FormatTimeAgo(r.CreatedAt),
        Body = r.Comment,
        HelpfulCount = 0,
        ReportedByCurrentUser = reportedByCurrentUser
    };

    private async Task ValidateReviewAsync(CreateReviewDto dto)
    {
        if (dto.Rating < 1 || dto.Rating > 5)
            throw new InvalidOperationException("Rating must be between 1 and 5");

        if ((dto.Comment ?? string.Empty).Length > 1000)
            throw new InvalidOperationException("Comment is too long");

        await _contentModeration.EnsureAllowedReviewTextAsync(dto.Comment);
    }

    private async Task<HashSet<int>> GetReportedReviewIdsAsync(
        int? currentUserId,
        ReviewReportTargetType targetType,
        IEnumerable<int> reviewIds)
    {
        if (!currentUserId.HasValue)
            return new HashSet<int>();

        var ids = reviewIds.Distinct().ToList();
        if (ids.Count == 0)
            return new HashSet<int>();

        var reportedIds = await _db.ReviewReports
            .AsNoTracking()
            .Where(report =>
                report.ReporterUserId == currentUserId.Value &&
                report.TargetType == targetType &&
                ids.Contains(report.ReviewId))
            .Select(report => report.ReviewId)
            .ToListAsync();

        return reportedIds.ToHashSet();
    }

    private static string GetAuthorName(Domain.Entities.Users.User user)
    {
        if (!string.IsNullOrEmpty(user.FirstName))
            return $"{user.FirstName} {user.LastName}".Trim();
        return user.Username;
    }

    private static string FormatTimeAgo(DateTime date)
    {
        var diff = DateTime.UtcNow - date;
        if (diff.TotalMinutes < 60) return $"{(int)diff.TotalMinutes}m ago";
        if (diff.TotalHours < 24) return $"{(int)diff.TotalHours}h ago";
        if (diff.TotalDays < 30) return $"{(int)diff.TotalDays}d ago";
        if (diff.TotalDays < 365) return $"{(int)(diff.TotalDays / 30)}mo ago";
        return $"{(int)(diff.TotalDays / 365)}y ago";
    }
}
