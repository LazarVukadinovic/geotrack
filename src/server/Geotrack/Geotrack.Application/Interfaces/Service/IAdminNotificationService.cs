using Geotrack.Application.DTOs.Admin;
using Geotrack.Domain.Entities.Reviews;
using Geotrack.Domain.Entities.Users;

namespace Geotrack.Application.Interfaces.Service;

public interface IAdminNotificationService
{
    Task<AdminNotificationListDto> GetNotificationsAsync(
        int currentUserId,
        AdminNotificationListRequestDto request,
        CancellationToken cancellationToken = default);

    Task<AdminNotificationUnreadCountDto> GetUnreadCountAsync(
        int currentUserId,
        CancellationToken cancellationToken = default);

    Task<AdminNotificationMutationDto> MarkReadAsync(
        int currentUserId,
        int notificationId,
        CancellationToken cancellationToken = default);

    Task<AdminNotificationMutationDto> MarkAllReadAsync(
        int currentUserId,
        CancellationToken cancellationToken = default);

    Task CreateSupportTicketCreatedNotificationAsync(
        SupportRequest ticket,
        CancellationToken cancellationToken = default);

    Task CreateReviewReportedNotificationAsync(
        ReviewReport report,
        CancellationToken cancellationToken = default);
}
