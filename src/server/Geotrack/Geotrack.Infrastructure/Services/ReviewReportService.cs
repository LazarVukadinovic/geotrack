using Geotrack.Application.DTOs.Reviews;
using Geotrack.Application.Interfaces.Service;
using Geotrack.Domain.Entities.Reviews;
using Geotrack.Domain.Entities.Users;
using Microsoft.EntityFrameworkCore;

namespace Geotrack.Infrastructure.Services;

public class ReviewReportService : IReviewReportService
{
    private readonly AppDbContext _db;

    public ReviewReportService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<ReportReviewResponseDto> ReportReviewAsync(
        int reporterUserId,
        string targetType,
        int reviewId,
        ReportReviewRequestDto request)
    {
        if (reviewId <= 0)
            throw new InvalidOperationException("Invalid review id.");

        var parsedTargetType = ParseTargetType(targetType);
        var existingReport = await _db.ReviewReports
            .AsNoTracking()
            .FirstOrDefaultAsync(report =>
                report.ReporterUserId == reporterUserId &&
                report.TargetType == parsedTargetType &&
                report.ReviewId == reviewId);

        if (existingReport is not null)
        {
            return new ReportReviewResponseDto
            {
                ReportId = existingReport.Id,
                AlreadyReported = true,
                Status = existingReport.Status.ToString(),
                Message = "You have already reported this review."
            };
        }

        var report = parsedTargetType switch
        {
            ReviewReportTargetType.Place => await BuildPlaceReviewReportAsync(reporterUserId, reviewId, request),
            ReviewReportTargetType.Event => await BuildEventReviewReportAsync(reporterUserId, reviewId, request),
            _ => throw new InvalidOperationException("Invalid review target.")
        };

        await _db.ReviewReports.AddAsync(report);

        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            _db.ChangeTracker.Clear();
            if (!await HasDuplicateReportAsync(reporterUserId, parsedTargetType, reviewId))
                throw;

            var duplicate = await _db.ReviewReports
                .AsNoTracking()
                .SingleAsync(existing =>
                    existing.ReporterUserId == reporterUserId &&
                    existing.TargetType == parsedTargetType &&
                    existing.ReviewId == reviewId);

            return new ReportReviewResponseDto
            {
                ReportId = duplicate.Id,
                AlreadyReported = true,
                Status = duplicate.Status.ToString(),
                Message = "You have already reported this review."
            };
        }

        return new ReportReviewResponseDto
        {
            ReportId = report.Id,
            AlreadyReported = false,
            Status = report.Status.ToString(),
            Message = "Thanks. This review has been queued for regional manager moderation."
        };
    }

    public async Task<GetReviewReportsResponseDto> GetReviewQueueAsync(int reviewerUserId, string? status)
    {
        var reviewer = await EnsureRegionalManagerAsync(reviewerUserId);
        var assignedRegionIds = GetAssignedRegionIds(reviewer);

        if (assignedRegionIds.Count == 0)
            return new GetReviewReportsResponseDto();

        var query = _db.ReviewReports
            .AsNoTracking()
            .Include(report => report.ReporterUser)
            .Where(report => assignedRegionIds.Contains(report.RegionId));

        if (!string.Equals(status?.Trim(), "all", StringComparison.OrdinalIgnoreCase))
        {
            query = query.Where(report => report.Status == ParseStatus(status));
        }

        var reports = await query
            .OrderByDescending(report => report.CreatedAt)
            .ToListAsync();

        return new GetReviewReportsResponseDto
        {
            Reports = reports.Select(MapReport).ToList()
        };
    }

    public async Task<ReviewReportItemDto> DismissReportAsync(
        int reviewerUserId,
        int reportId,
        ModerateReviewReportRequestDto request)
    {
        var report = await LoadReportForModerationAsync(reportId);
        await EnsureRegionalManagerCanReviewAsync(reviewerUserId, report);

        if (report.Status != ReviewReportStatus.Pending)
            throw new InvalidOperationException("This report has already been reviewed.");

        report.Status = ReviewReportStatus.Dismissed;
        report.Decision = ReviewReportDecision.KeepReview;
        report.ReviewedByUserId = reviewerUserId;
        report.ReviewedAt = DateTime.UtcNow;
        report.DecisionNote = NormalizeDecisionNote(request.Note);

        await _db.SaveChangesAsync();

        return MapReport(report);
    }

    public async Task<ReviewReportItemDto> RemoveReportedReviewAsync(
        int reviewerUserId,
        int reportId,
        ModerateReviewReportRequestDto request)
    {
        var report = await LoadReportForModerationAsync(reportId);
        await EnsureRegionalManagerCanReviewAsync(reviewerUserId, report);

        if (report.Status != ReviewReportStatus.Pending)
            throw new InvalidOperationException("This report has already been reviewed.");

        var now = DateTime.UtcNow;
        await using var transaction = await _db.Database.BeginTransactionAsync();

        switch (report.TargetType)
        {
            case ReviewReportTargetType.Place:
                await RemovePlaceReviewAsync(report);
                break;
            case ReviewReportTargetType.Event:
                await RemoveEventReviewAsync(report);
                break;
            default:
                throw new InvalidOperationException("Invalid review target.");
        }

        var pendingReportsForReview = await _db.ReviewReports
            .Where(existing =>
                existing.TargetType == report.TargetType &&
                existing.ReviewId == report.ReviewId &&
                existing.Status == ReviewReportStatus.Pending)
            .ToListAsync();

        foreach (var pendingReport in pendingReportsForReview)
        {
            pendingReport.Status = ReviewReportStatus.Actioned;
            pendingReport.Decision = ReviewReportDecision.RemoveReview;
            pendingReport.ReviewedByUserId = reviewerUserId;
            pendingReport.ReviewedAt = now;
            pendingReport.DecisionNote = NormalizeDecisionNote(request.Note);
        }

        await _db.SaveChangesAsync();
        await transaction.CommitAsync();

        return MapReport(report);
    }

    private async Task<ReviewReport> BuildPlaceReviewReportAsync(
        int reporterUserId,
        int reviewId,
        ReportReviewRequestDto request)
    {
        var review = await _db.PlaceReviews
            .Include(item => item.User)
            .Include(item => item.Place)
                .ThenInclude(place => place.Location)
                .ThenInclude(location => location.Destination)
                .ThenInclude(destination => destination.Region)
            .FirstOrDefaultAsync(item => item.Id == reviewId)
            ?? throw new KeyNotFoundException("Review not found.");

        if (review.UserId == reporterUserId)
            throw new InvalidOperationException("You cannot report your own review.");

        return new ReviewReport
        {
            TargetType = ReviewReportTargetType.Place,
            ReviewId = review.Id,
            PlaceReviewId = review.Id,
            ReporterUserId = reporterUserId,
            Reason = ParseReason(request.Reason),
            Note = NormalizeReportNote(request.Note),
            Status = ReviewReportStatus.Pending,
            ReviewAuthorUserId = review.UserId,
            ReviewAuthorName = GetAuthorName(review.User),
            EntityId = review.PlaceId,
            EntityName = review.Place.Name,
            RegionId = review.Place.Location.Destination.RegionId,
            RegionName = review.Place.Location.Destination.Region.Name,
            ReviewRating = review.Rating,
            ReviewCommentSnapshot = review.Comment,
            ReviewCreatedAt = review.CreatedAt,
            CreatedAt = DateTime.UtcNow
        };
    }

    private async Task<ReviewReport> BuildEventReviewReportAsync(
        int reporterUserId,
        int reviewId,
        ReportReviewRequestDto request)
    {
        var review = await _db.EventReviews
            .Include(item => item.User)
            .Include(item => item.Event)
                .ThenInclude(evt => evt.Location)
                .ThenInclude(location => location.Destination)
                .ThenInclude(destination => destination.Region)
            .FirstOrDefaultAsync(item => item.Id == reviewId)
            ?? throw new KeyNotFoundException("Review not found.");

        if (review.UserId == reporterUserId)
            throw new InvalidOperationException("You cannot report your own review.");

        return new ReviewReport
        {
            TargetType = ReviewReportTargetType.Event,
            ReviewId = review.Id,
            EventReviewId = review.Id,
            ReporterUserId = reporterUserId,
            Reason = ParseReason(request.Reason),
            Note = NormalizeReportNote(request.Note),
            Status = ReviewReportStatus.Pending,
            ReviewAuthorUserId = review.UserId,
            ReviewAuthorName = GetAuthorName(review.User),
            EntityId = review.EventId,
            EntityName = review.Event.Name,
            RegionId = review.Event.Location.Destination.RegionId,
            RegionName = review.Event.Location.Destination.Region.Name,
            ReviewRating = review.Rating,
            ReviewCommentSnapshot = review.Comment,
            ReviewCreatedAt = review.CreatedAt,
            CreatedAt = DateTime.UtcNow
        };
    }

    private async Task RemovePlaceReviewAsync(ReviewReport report)
    {
        var review = await _db.PlaceReviews
            .FirstOrDefaultAsync(item => item.Id == (report.PlaceReviewId ?? report.ReviewId));

        if (review is null)
            return;

        var place = await _db.Places.FindAsync(review.PlaceId);
        var stats = await _db.PlaceReviews
            .Where(item => item.PlaceId == review.PlaceId && item.Id != review.Id)
            .GroupBy(item => item.PlaceId)
            .Select(group => new { Average = group.Average(item => item.Rating), Count = group.Count() })
            .FirstOrDefaultAsync();

        _db.PlaceReviews.Remove(review);

        if (place is not null)
        {
            place.AverageRating = stats is null ? null : Math.Round(stats.Average, 1);
            place.ReviewCount = stats?.Count ?? 0;
        }
    }

    private async Task RemoveEventReviewAsync(ReviewReport report)
    {
        var review = await _db.EventReviews
            .FirstOrDefaultAsync(item => item.Id == (report.EventReviewId ?? report.ReviewId));

        if (review is not null)
            _db.EventReviews.Remove(review);
    }

    private async Task<ReviewReport> LoadReportForModerationAsync(int reportId)
    {
        if (reportId <= 0)
            throw new InvalidOperationException("Invalid report id.");

        return await _db.ReviewReports
            .Include(report => report.ReporterUser)
            .FirstOrDefaultAsync(report => report.Id == reportId)
            ?? throw new KeyNotFoundException("Review report not found.");
    }

    private async Task EnsureRegionalManagerCanReviewAsync(int reviewerUserId, ReviewReport report)
    {
        var reviewer = await EnsureRegionalManagerAsync(reviewerUserId);
        var assignedRegionIds = GetAssignedRegionIds(reviewer);

        if (assignedRegionIds.Count == 0)
            throw new UnauthorizedAccessException("Regional manager has no assigned regions.");

        if (!assignedRegionIds.Contains(report.RegionId))
            throw new UnauthorizedAccessException("Regional manager is not assigned to this review's region.");
    }

    private async Task<User> EnsureRegionalManagerAsync(int reviewerUserId)
    {
        var reviewer = await _db.Users
            .Include(user => user.UserRoles).ThenInclude(userRole => userRole.Role)
            .Include(user => user.RegionalOrganization)
                .ThenInclude(organization => organization!.RegionalOrganizationRegions)
                .ThenInclude(link => link.Region)
            .FirstOrDefaultAsync(user => user.Id == reviewerUserId)
            ?? throw new UnauthorizedAccessException("Invalid user token.");

        if (!reviewer.UserRoles.Any(userRole => userRole.Role.Name == "RegionalManager"))
            throw new UnauthorizedAccessException("Regional manager role is required.");

        return reviewer;
    }

    private async Task<bool> HasDuplicateReportAsync(
        int reporterUserId,
        ReviewReportTargetType targetType,
        int reviewId) =>
        await _db.ReviewReports.AnyAsync(report =>
            report.ReporterUserId == reporterUserId &&
            report.TargetType == targetType &&
            report.ReviewId == reviewId);

    private static List<int> GetAssignedRegionIds(User reviewer) =>
        reviewer.RegionalOrganization?.RegionalOrganizationRegions
            .Select(link => link.RegionId)
            .Distinct()
            .ToList() ?? new List<int>();

    private static ReviewReportTargetType ParseTargetType(string value)
    {
        var normalized = NormalizeToken(value);
        return normalized switch
        {
            "place" or "places" or "stay" or "stays" => ReviewReportTargetType.Place,
            "event" or "events" => ReviewReportTargetType.Event,
            _ => throw new InvalidOperationException("Invalid review target.")
        };
    }

    private static ReviewReportReason ParseReason(string? value)
    {
        var normalized = NormalizeToken(value ?? "offensive_content");
        return normalized switch
        {
            "" or "offensive" or "offensivecontent" or "inappropriate" => ReviewReportReason.OffensiveContent,
            "spam" => ReviewReportReason.Spam,
            "harassment" or "abuse" => ReviewReportReason.Harassment,
            "falseinformation" or "misinformation" or "false" => ReviewReportReason.FalseInformation,
            "other" => ReviewReportReason.Other,
            _ => throw new InvalidOperationException("Invalid report reason.")
        };
    }

    private static ReviewReportStatus ParseStatus(string? value)
    {
        var normalized = NormalizeToken(value ?? "pending");
        return normalized switch
        {
            "" or "pending" => ReviewReportStatus.Pending,
            "dismissed" or "kept" or "keep" => ReviewReportStatus.Dismissed,
            "actioned" or "removed" or "deleted" => ReviewReportStatus.Actioned,
            _ => throw new InvalidOperationException("Invalid report status.")
        };
    }

    private static string NormalizeToken(string value) =>
        value.Trim()
            .Replace("-", "", StringComparison.Ordinal)
            .Replace("_", "", StringComparison.Ordinal)
            .Replace(" ", "", StringComparison.Ordinal)
            .ToLowerInvariant();

    private static string NormalizeReportNote(string? value)
    {
        var trimmed = value?.Trim() ?? string.Empty;
        return trimmed.Length > 1000 ? trimmed[..1000] : trimmed;
    }

    private static string NormalizeDecisionNote(string? value)
    {
        var trimmed = value?.Trim() ?? string.Empty;
        return trimmed.Length > 1000 ? trimmed[..1000] : trimmed;
    }

    private static ReviewReportItemDto MapReport(ReviewReport report) => new()
    {
        Id = report.Id,
        TargetType = report.TargetType.ToString(),
        ReviewId = report.ReviewId,
        ReporterUserId = report.ReporterUserId,
        ReporterName = report.ReporterUser is null ? string.Empty : GetAuthorName(report.ReporterUser),
        Reason = report.Reason.ToString(),
        Note = report.Note,
        Status = report.Status.ToString(),
        CreatedAt = report.CreatedAt,
        ReviewAuthorUserId = report.ReviewAuthorUserId,
        ReviewAuthorName = report.ReviewAuthorName,
        EntityId = report.EntityId,
        EntityName = report.EntityName,
        RegionId = report.RegionId,
        RegionName = report.RegionName,
        ReviewRating = report.ReviewRating,
        ReviewComment = report.ReviewCommentSnapshot,
        ReviewCreatedAt = report.ReviewCreatedAt,
        ReviewedByUserId = report.ReviewedByUserId,
        ReviewedAt = report.ReviewedAt,
        Decision = report.Decision?.ToString(),
        DecisionNote = report.DecisionNote
    };

    private static string GetAuthorName(User user)
    {
        if (!string.IsNullOrEmpty(user.FirstName))
            return $"{user.FirstName} {user.LastName}".Trim();

        return user.Username;
    }
}
