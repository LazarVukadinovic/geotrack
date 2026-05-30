using Geotrack.Application.DTOs.Notifications;
using Geotrack.Application.Interfaces.Repository;
using Geotrack.Application.Interfaces.Service;
using Geotrack.Domain.Entities.Reviews;
using Geotrack.Domain.Entities.Users;

namespace Geotrack.Application.Services;

public class UserNotificationService : IUserNotificationService
{
    private const int MaxTake = 50;
    private const string ContributionApprovedType = "contribution_approved";
    private const string ContributionRejectedType = "contribution_rejected";
    private const string ReviewReportAcceptedType = "review_report_accepted";
    private const string ReviewReportRejectedType = "review_report_rejected";
    private const string ReportedReviewRemovedType = "reported_review_removed";
    private const string ReviewRemovedByModeratorType = "review_removed_by_moderator";

    private readonly IUserNotificationRepository _notificationRepository;

    public UserNotificationService(IUserNotificationRepository notificationRepository)
    {
        _notificationRepository = notificationRepository;
    }

    public async Task<GetUserNotificationsResponseDto> GetMineAsync(int userId, int skip = 0, int take = 25)
    {
        var normalizedSkip = Math.Max(0, skip);
        var normalizedTake = Math.Clamp(take, 1, MaxTake);

        var notifications = await _notificationRepository.GetByUserAsync(
            userId,
            normalizedSkip,
            normalizedTake + 1);

        var hasMore = notifications.Count > normalizedTake;
        var pageItems = hasMore
            ? notifications.Take(normalizedTake).ToList()
            : notifications;

        var unreadCount = await _notificationRepository.CountUnreadAsync(userId);
        var totalCount = await _notificationRepository.CountByUserAsync(userId);
        var mappedItems = pageItems.Select(MapNotification).ToList();

        return new GetUserNotificationsResponseDto
        {
            Items = mappedItems,
            UnreadCount = unreadCount,
            TotalCount = totalCount,
            Skip = normalizedSkip,
            Take = normalizedTake,
            NextSkip = normalizedSkip + pageItems.Count,
            HasMore = hasMore
        };
    }

    public async Task<UserNotificationUnreadCountDto> GetUnreadCountAsync(int userId)
    {
        var unreadCount = await _notificationRepository.CountUnreadAsync(userId);
        return new UserNotificationUnreadCountDto { UnreadCount = unreadCount };
    }

    public async Task MarkReadAsync(int userId, int notificationId)
    {
        var notification = await _notificationRepository.GetByIdForUserAsync(userId, notificationId)
            ?? throw new KeyNotFoundException("Notification not found");

        notification.ReadAt ??= DateTime.UtcNow;
        await _notificationRepository.SaveChangesAsync();
    }

    public async Task MarkAllReadAsync(int userId)
    {
        var now = DateTime.UtcNow;
        var unreadNotifications = await _notificationRepository.GetUnreadByUserAsync(userId);

        foreach (var notification in unreadNotifications)
        {
            notification.ReadAt = now;
        }

        await _notificationRepository.SaveChangesAsync();
    }

    public async Task AddContributionDecisionNotificationAsync(PlaceContribution contribution)
    {
        if (contribution.Status is not (PlaceContributionStatus.Approved or PlaceContributionStatus.Rejected))
        {
            return;
        }

        var isApproved = contribution.Status == PlaceContributionStatus.Approved;
        var type = isApproved ? ContributionApprovedType : ContributionRejectedType;
        const string relatedEntityType = "contribution";

        if (await _notificationRepository.ExistsAsync(
                contribution.UserId,
                type,
                relatedEntityType,
                contribution.Id))
        {
            return;
        }

        _notificationRepository.Add(new UserNotification
        {
            UserId = contribution.UserId,
            Type = type,
            Title = isApproved ? "Contribution approved" : "Contribution rejected",
            Message = isApproved
                ? "Your contribution has been approved and published."
                : "Your contribution was reviewed but was not approved.",
            RelatedEntityType = relatedEntityType,
            RelatedEntityId = contribution.Id,
            ActionUrl = isApproved
                ? BuildApprovedContributionRoute(contribution)
                : $"/contributions/{contribution.Id}",
            CreatedAt = contribution.ReviewedAt ?? DateTime.UtcNow
        });
    }

    public void AddReviewReportReporterDecisionNotification(ReviewReport report)
    {
        if (report.Status is not (ReviewReportStatus.Actioned or ReviewReportStatus.Dismissed))
        {
            return;
        }

        var accepted = report.Status == ReviewReportStatus.Actioned;

        _notificationRepository.Add(new UserNotification
        {
            UserId = report.ReporterUserId,
            Type = accepted ? ReviewReportAcceptedType : ReviewReportRejectedType,
            Title = accepted ? "Review report accepted" : "Review report reviewed",
            Message = accepted
                ? "Your report was reviewed and the reported content was removed from public view."
                : "Your report was reviewed and the content will remain visible.",
            RelatedEntityType = "review_report",
            RelatedEntityId = report.Id,
            ActionUrl = null,
            CreatedAt = report.ReviewedAt ?? DateTime.UtcNow
        });
    }

    public void AddReportedReviewRemovedNotification(ReviewReport report)
    {
        if (report.Status != ReviewReportStatus.Actioned ||
            report.Decision != ReviewReportDecision.RemoveReview)
        {
            return;
        }

        _notificationRepository.Add(new UserNotification
        {
            UserId = report.ReviewAuthorUserId,
            Type = ReportedReviewRemovedType,
            Title = "Review removed",
            Message = "Your review was removed after moderation.",
            RelatedEntityType = "review",
            RelatedEntityId = report.ReviewId,
            ActionUrl = null,
            CreatedAt = report.ReviewedAt ?? DateTime.UtcNow
        });
    }

    public void AddReviewRemovedByModeratorNotification(
        int authorUserId,
        string targetType,
        int reviewId,
        DateTime createdAt)
    {
        if (authorUserId <= 0 || reviewId <= 0)
        {
            return;
        }

        _notificationRepository.Add(new UserNotification
        {
            UserId = authorUserId,
            Type = ReviewRemovedByModeratorType,
            Title = "Review removed",
            Message = "Your review was removed after moderation.",
            RelatedEntityType = string.IsNullOrWhiteSpace(targetType)
                ? "review"
                : $"{targetType.Trim().ToLowerInvariant()}_review",
            RelatedEntityId = reviewId,
            ActionUrl = null,
            CreatedAt = createdAt
        });
    }

    private static UserNotificationDto MapNotification(UserNotification notification) => new()
    {
        Id = notification.Id,
        Type = notification.Type,
        Title = notification.Title,
        Message = notification.Message,
        RelatedEntityType = notification.RelatedEntityType,
        RelatedEntityId = notification.RelatedEntityId,
        ActionUrl = notification.ActionUrl,
        TargetType = notification.RelatedEntityType,
        TargetId = notification.RelatedEntityId,
        TargetRoute = notification.ActionUrl,
        CreatedAt = notification.CreatedAt,
        ReadAt = notification.ReadAt,
        IsRead = notification.ReadAt.HasValue
    };

    private static string BuildApprovedContributionRoute(PlaceContribution contribution)
    {
        if (!contribution.PlaceId.HasValue)
        {
            return $"/contributions/{contribution.Id}";
        }

        return contribution.StayTypeId.HasValue
            ? $"/stays/{contribution.PlaceId.Value}"
            : $"/places/{contribution.PlaceId.Value}";
    }
}
