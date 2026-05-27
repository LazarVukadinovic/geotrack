using Geotrack.Application.DTOs.Admin;
using Geotrack.Domain.Entities.Core;
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
        string? audienceRole = null,
        int? regionId = null,
        CancellationToken cancellationToken = default);

    Task<AdminNotificationMutationDto> MarkReadAsync(
        int currentUserId,
        int notificationId,
        string? audienceRole = null,
        int? regionId = null,
        CancellationToken cancellationToken = default);

    Task<AdminNotificationMutationDto> MarkAllReadAsync(
        int currentUserId,
        string? audienceRole = null,
        int? regionId = null,
        CancellationToken cancellationToken = default);

    Task CreateSupportTicketCreatedNotificationAsync(
        SupportRequest ticket,
        CancellationToken cancellationToken = default);

    Task CreateReviewReportedNotificationAsync(
        ReviewReport report,
        CancellationToken cancellationToken = default);

    Task CreateTerritoryContributionPendingReviewNotificationAsync(
        PlaceContribution contribution,
        CancellationToken cancellationToken = default);

    Task CreateRegionalContributionPendingReviewNotificationAsync(
        PlaceContribution contribution,
        CancellationToken cancellationToken = default);

    Task CreateTerritoryRegionCreatedNotificationAsync(
        int territoryId,
        int regionId,
        string regionName,
        DateTime createdAt,
        CancellationToken cancellationToken = default);

    Task CreateTerritoryRegionalManagerAssignedNotificationAsync(
        int territoryId,
        int regionId,
        int managerUserId,
        DateTime createdAt,
        CancellationToken cancellationToken = default);

    Task CreateTerritoryRegionalManagerRemovedNotificationAsync(
        int territoryId,
        int regionId,
        int managerUserId,
        DateTime createdAt,
        CancellationToken cancellationToken = default);
}
