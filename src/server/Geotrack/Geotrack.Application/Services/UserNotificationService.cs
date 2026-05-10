using Geotrack.Application.DTOs.Notifications;
using Geotrack.Application.Interfaces.Repository;
using Geotrack.Application.Interfaces.Service;
using Geotrack.Domain.Entities.Users;

namespace Geotrack.Application.Services;

public class UserNotificationService : IUserNotificationService
{
    private const int MaxTake = 50;
    private const string ContributionApprovedType = "contribution_approved";
    private const string ContributionRejectedType = "contribution_rejected";

    private readonly IUserNotificationRepository _notificationRepository;

    public UserNotificationService(IUserNotificationRepository notificationRepository)
    {
        _notificationRepository = notificationRepository;
    }

    public async Task<GetUserNotificationsResponseDto> GetMineAsync(int userId, int skip = 0, int take = 30)
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

        return new GetUserNotificationsResponseDto
        {
            Notifications = pageItems.Select(MapNotification).ToList(),
            UnreadCount = unreadCount,
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

    public void AddContributionDecisionNotification(PlaceContribution contribution)
    {
        if (contribution.Status is not (PlaceContributionStatus.Approved or PlaceContributionStatus.Rejected))
        {
            return;
        }

        var isApproved = contribution.Status == PlaceContributionStatus.Approved;

        _notificationRepository.Add(new UserNotification
        {
            UserId = contribution.UserId,
            Type = isApproved ? ContributionApprovedType : ContributionRejectedType,
            Title = isApproved ? "Contribution approved" : "Contribution rejected",
            Message = isApproved
                ? $"Your place contribution \"{contribution.Name}\" was approved and is now published."
                : $"Your place contribution \"{contribution.Name}\" was rejected. Review the feedback and update it when you are ready.",
            RelatedEntityType = "contribution",
            RelatedEntityId = contribution.Id,
            ActionUrl = $"/contributions/{contribution.Id}",
            CreatedAt = contribution.ReviewedAt ?? DateTime.UtcNow
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
        CreatedAt = notification.CreatedAt,
        ReadAt = notification.ReadAt,
        IsRead = notification.ReadAt.HasValue
    };
}
