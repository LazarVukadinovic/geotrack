using Geotrack.Application.DTOs.Reviews;
using Geotrack.Application.Interfaces.Repository;
using Geotrack.Application.Interfaces.Service;
using Geotrack.Domain.Entities.Reviews;
using Geotrack.Domain.Entities.Users;

namespace Geotrack.Application.Services;

public class ReviewService : IReviewService
{
    private readonly IReviewRepository _reviewRepository;
    private readonly IReviewContentModerationService _contentModeration;

    public ReviewService(
        IReviewRepository reviewRepository,
        IReviewContentModerationService contentModeration)
    {
        _reviewRepository = reviewRepository;
        _contentModeration = contentModeration;
    }

    public async Task<PagedReviewsResponseDto> GetPlaceReviewsAsync(
        int placeId,
        int skip = 0,
        int take = ReviewPaging.DefaultTake,
        int? currentUserId = null)
    {
        var normalizedSkip = ReviewPaging.NormalizeSkip(skip);
        var normalizedTake = ReviewPaging.NormalizeTake(take);

        var totalCount = await _reviewRepository.CountPlaceReviewsAsync(placeId);
        var averageRating = totalCount > 0
            ? await _reviewRepository.GetPlaceAverageRatingAsync(placeId)
            : null;

        var reviews = await _reviewRepository.GetPlaceReviewsPageAsync(
            placeId,
            normalizedSkip,
            normalizedTake);

        var reportedReviewIds = await GetReportedReviewIdsAsync(
            currentUserId,
            ReviewReportTargetType.Place,
            reviews.Select(review => review.Id));

        var items = reviews
            .Select(review => MapReview(review, reportedReviewIds.Contains(review.Id)))
            .ToList();

        return BuildPage(items, averageRating, totalCount, normalizedSkip, normalizedTake);
    }

    public async Task<PagedReviewsResponseDto> GetEventReviewsAsync(
        int eventId,
        int skip = 0,
        int take = ReviewPaging.DefaultTake,
        int? currentUserId = null)
    {
        var normalizedSkip = ReviewPaging.NormalizeSkip(skip);
        var normalizedTake = ReviewPaging.NormalizeTake(take);

        var totalCount = await _reviewRepository.CountEventReviewsAsync(eventId);
        var averageRating = totalCount > 0
            ? await _reviewRepository.GetEventAverageRatingAsync(eventId)
            : null;

        var reviews = await _reviewRepository.GetEventReviewsPageAsync(
            eventId,
            normalizedSkip,
            normalizedTake);

        var reportedReviewIds = await GetReportedReviewIdsAsync(
            currentUserId,
            ReviewReportTargetType.Event,
            reviews.Select(review => review.Id));

        var items = reviews
            .Select(review => new ReviewDto
            {
                Id = review.Id,
                AuthorName = GetAuthorName(review.User),
                AvatarUrl = review.User.AvatarUrl,
                Rating = (int)review.Rating,
                TimeAgo = FormatTimeAgo(review.CreatedAt),
                Body = review.Comment,
                HelpfulCount = 0,
                ReportedByCurrentUser = reportedReviewIds.Contains(review.Id)
            })
            .ToList();

        return BuildPage(items, averageRating, totalCount, normalizedSkip, normalizedTake);
    }

    public async Task<ReviewDto> CreatePlaceReviewAsync(int placeId, int userId, CreateReviewDto dto)
    {
        await ValidateReviewAsync(dto);

        if (!await _reviewRepository.PlaceExistsAsync(placeId))
        {
            throw new KeyNotFoundException("Place not found");
        }

        if (await _reviewRepository.HasUserReviewedPlaceAsync(placeId, userId))
        {
            throw new InvalidOperationException("User has already reviewed this place");
        }

        var review = new PlaceReview
        {
            PlaceId = placeId,
            UserId = userId,
            Rating = dto.Rating,
            Comment = dto.Comment?.Trim() ?? string.Empty,
            CreatedAt = DateTime.UtcNow
        };

        _reviewRepository.AddPlaceReview(review);
        await _reviewRepository.SaveChangesAsync();

        await _reviewRepository.UpdatePlaceRatingStatsAsync(placeId);
        await _reviewRepository.SaveChangesAsync();

        var user = await _reviewRepository.GetUserByIdAsync(userId)
            ?? throw new KeyNotFoundException("User not found");

        review.User = user;

        return MapReview(review, false);
    }

    public async Task<ReviewDto> CreateEventReviewAsync(int eventId, int userId, CreateReviewDto dto)
    {
        await ValidateReviewAsync(dto);

        if (!await _reviewRepository.EventExistsAsync(eventId))
        {
            throw new KeyNotFoundException("Event not found");
        }

        if (await _reviewRepository.HasUserReviewedEventAsync(eventId, userId))
        {
            throw new InvalidOperationException("User has already reviewed this event");
        }

        var review = new EventReview
        {
            EventId = eventId,
            UserId = userId,
            Rating = dto.Rating,
            Comment = dto.Comment?.Trim() ?? string.Empty,
            CreatedAt = DateTime.UtcNow
        };

        _reviewRepository.AddEventReview(review);
        await _reviewRepository.SaveChangesAsync();

        var user = await _reviewRepository.GetUserByIdAsync(userId)
            ?? throw new KeyNotFoundException("User not found");

        review.User = user;

        return new ReviewDto
        {
            Id = review.Id,
            AuthorName = GetAuthorName(user),
            AvatarUrl = user.AvatarUrl,
            Rating = (int)review.Rating,
            TimeAgo = "just now",
            Body = review.Comment,
            HelpfulCount = 0,
            ReportedByCurrentUser = false
        };
    }

    private async Task ValidateReviewAsync(CreateReviewDto dto)
    {
        if (dto.Rating < 1 || dto.Rating > 5)
        {
            throw new InvalidOperationException("Rating must be between 1 and 5");
        }

        if ((dto.Comment ?? string.Empty).Length > 1000)
        {
            throw new InvalidOperationException("Comment is too long");
        }

        await _contentModeration.EnsureAllowedReviewTextAsync(dto.Comment);
    }

    private async Task<HashSet<int>> GetReportedReviewIdsAsync(
        int? currentUserId,
        ReviewReportTargetType targetType,
        IEnumerable<int> reviewIds)
    {
        if (!currentUserId.HasValue)
        {
            return new HashSet<int>();
        }

        return await _reviewRepository.GetReportedReviewIdsAsync(
            currentUserId.Value,
            targetType,
            reviewIds);
    }

    private static ReviewDto MapReview(PlaceReview review, bool reportedByCurrentUser) => new()
    {
        Id = review.Id,
        AuthorName = GetAuthorName(review.User),
        AvatarUrl = review.User.AvatarUrl,
        Rating = (int)review.Rating,
        TimeAgo = FormatTimeAgo(review.CreatedAt),
        Body = review.Comment,
        HelpfulCount = 0,
        ReportedByCurrentUser = reportedByCurrentUser
    };

    private static PagedReviewsResponseDto BuildPage(
        List<ReviewDto> items,
        double? averageRating,
        int totalCount,
        int skip,
        int take) => new()
    {
        Items = items,
        AverageRating = averageRating.HasValue ? Math.Round(averageRating.Value, 1) : null,
        TotalCount = totalCount,
        Skip = skip,
        Take = take,
        NextSkip = skip + items.Count,
        HasMore = totalCount > skip + items.Count
    };

    private static string GetAuthorName(User user)
    {
        if (!string.IsNullOrEmpty(user.FirstName))
        {
            return $"{user.FirstName} {user.LastName}".Trim();
        }

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
