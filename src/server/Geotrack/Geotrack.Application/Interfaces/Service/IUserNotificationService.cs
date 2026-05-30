using Geotrack.Application.DTOs.Notifications;
using Geotrack.Domain.Entities.Reviews;
using Geotrack.Domain.Entities.Users;

namespace Geotrack.Application.Interfaces.Service;

public interface IUserNotificationService
{
    Task<GetUserNotificationsResponseDto> GetMineAsync(int userId, int skip = 0, int take = 25);
    Task<UserNotificationUnreadCountDto> GetUnreadCountAsync(int userId);
    Task MarkReadAsync(int userId, int notificationId);
    Task MarkAllReadAsync(int userId);
    Task AddContributionDecisionNotificationAsync(PlaceContribution contribution);
    void AddReviewReportReporterDecisionNotification(ReviewReport report);
    void AddReportedReviewRemovedNotification(ReviewReport report);
    void AddReviewRemovedByModeratorNotification(
        int authorUserId,
        string targetType,
        int reviewId,
        DateTime createdAt);
}
