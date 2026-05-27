using Geotrack.Application.DTOs.Admin;
using Geotrack.Application.Interfaces.Service;
using Geotrack.Application.Security;
using Geotrack.Domain.Entities.Reviews;
using Microsoft.EntityFrameworkCore;

namespace Geotrack.Infrastructure.Services;

public sealed class ContentModeratorDashboardService : IContentModeratorDashboardService
{
    private const string SuspendedStatus = "Draft";
    private const string DeletedStatus = "Archived";

    private readonly AppDbContext _db;

    public ContentModeratorDashboardService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<ContentModeratorDashboardDto> GetDashboardAsync(
        int currentUserId,
        CancellationToken cancellationToken = default)
    {
        var currentUser = await _db.Users
            .AsNoTracking()
            .Where(user => user.Id == currentUserId)
            .Select(user => new
            {
                user.Id,
                user.Email,
                user.DisplayName,
                user.FirstName,
                user.LastName,
                user.Username,
                user.AvatarUrl,
                user.IsSuspended,
                user.IsDeleted,
                Roles = user.UserRoles.Select(userRole => userRole.Role.Name).ToList()
            })
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new KeyNotFoundException("Content Moderator user was not found.");

        var roles = currentUser.Roles
            .Select(RoleNames.Normalize)
            .Where(role => !string.IsNullOrWhiteSpace(role))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (!roles.Contains(RoleNames.ContentModerator, StringComparer.Ordinal))
            throw new UnauthorizedAccessException("Content Moderator role is required.");

        if (currentUser.IsDeleted || currentUser.IsSuspended)
            throw new UnauthorizedAccessException("Content Moderator account is not active.");

        var pendingReportsCount = await _db.ReviewReports
            .AsNoTracking()
            .CountAsync(report => report.Status == ReviewReportStatus.Pending, cancellationToken);

        var reportedReviewsCount = await CountLiveReviewsWithPendingReportsAsync(cancellationToken);
        var activeReviewsCount = await CountLiveReviewsWithoutPendingReportsAsync(cancellationToken);
        var removedReviewsCount = await _db.ReviewReports
            .AsNoTracking()
            .CountAsync(report =>
                report.Status == ReviewReportStatus.Actioned &&
                report.Decision == ReviewReportDecision.RemoveReview,
                cancellationToken);
        var resolvedReportsCount = await _db.ReviewReports
            .AsNoTracking()
            .CountAsync(report => report.Status != ReviewReportStatus.Pending, cancellationToken);

        var destinationsCount = await _db.Destinations
            .AsNoTracking()
            .CountAsync(entity => entity.Status != DeletedStatus, cancellationToken);
        var locationsCount = await _db.Locations
            .AsNoTracking()
            .CountAsync(entity => entity.Status != DeletedStatus, cancellationToken);
        var placesCount = await _db.Places
            .AsNoTracking()
            .CountAsync(entity => entity.Status != DeletedStatus, cancellationToken);
        var eventsCount = await _db.Events
            .AsNoTracking()
            .CountAsync(entity => entity.Status != DeletedStatus, cancellationToken);

        var suspendedDestinationsCount = await _db.Destinations
            .AsNoTracking()
            .CountAsync(entity => entity.Status == SuspendedStatus, cancellationToken);
        var suspendedLocationsCount = await _db.Locations
            .AsNoTracking()
            .CountAsync(entity => entity.Status == SuspendedStatus, cancellationToken);
        var suspendedPlacesCount = await _db.Places
            .AsNoTracking()
            .CountAsync(entity => entity.Status == SuspendedStatus, cancellationToken);
        var suspendedEventsCount = await _db.Events
            .AsNoTracking()
            .CountAsync(entity => entity.Status == SuspendedStatus, cancellationToken);

        var deletedDestinationsCount = await _db.Destinations
            .AsNoTracking()
            .CountAsync(entity => entity.Status == DeletedStatus, cancellationToken);
        var deletedLocationsCount = await _db.Locations
            .AsNoTracking()
            .CountAsync(entity => entity.Status == DeletedStatus, cancellationToken);
        var deletedPlacesCount = await _db.Places
            .AsNoTracking()
            .CountAsync(entity => entity.Status == DeletedStatus, cancellationToken);
        var deletedEventsCount = await _db.Events
            .AsNoTracking()
            .CountAsync(entity => entity.Status == DeletedStatus, cancellationToken);

        return new ContentModeratorDashboardDto
        {
            CurrentUser = new AdminDashboardCurrentUserDto
            {
                Id = currentUser.Id,
                DisplayName = BuildDisplayName(
                    currentUser.DisplayName,
                    currentUser.FirstName,
                    currentUser.LastName,
                    currentUser.Username,
                    currentUser.Email),
                Email = currentUser.Email,
                AvatarUrl = currentUser.AvatarUrl,
                RoleDisplayName = "Content Moderator"
            },
            Stats = new ContentModeratorDashboardStatsDto
            {
                DestinationsCount = destinationsCount,
                LocationsCount = locationsCount,
                PlacesCount = placesCount,
                EventsCount = eventsCount,
                SuspendedDestinationsCount = suspendedDestinationsCount,
                SuspendedLocationsCount = suspendedLocationsCount,
                SuspendedPlacesCount = suspendedPlacesCount,
                SuspendedEventsCount = suspendedEventsCount,
                DeletedDestinationsCount = deletedDestinationsCount,
                DeletedLocationsCount = deletedLocationsCount,
                DeletedPlacesCount = deletedPlacesCount,
                DeletedEventsCount = deletedEventsCount,
                PendingReviewReportsCount = pendingReportsCount,
                ReportedReviewsCount = reportedReviewsCount,
                ActiveReviewsCount = activeReviewsCount,
                ResolvedReportsCount = resolvedReportsCount,
                RemovedReviewsCount = removedReviewsCount
            },
            QuickLinks = new ContentModeratorDashboardQuickLinksDto(),
            Badges = new ContentModeratorDashboardBadgesDto
            {
                PendingReviewReportsCount = pendingReportsCount
            }
        };
    }

    private async Task<int> CountLiveReviewsWithPendingReportsAsync(CancellationToken cancellationToken)
    {
        var reportedPlaceReviews = await _db.PlaceReviews
            .AsNoTracking()
            .CountAsync(review => _db.ReviewReports.Any(report =>
                report.TargetType == ReviewReportTargetType.Place &&
                report.ReviewId == review.Id &&
                report.Status == ReviewReportStatus.Pending),
                cancellationToken);

        var reportedEventReviews = await _db.EventReviews
            .AsNoTracking()
            .CountAsync(review => _db.ReviewReports.Any(report =>
                report.TargetType == ReviewReportTargetType.Event &&
                report.ReviewId == review.Id &&
                report.Status == ReviewReportStatus.Pending),
                cancellationToken);

        return reportedPlaceReviews + reportedEventReviews;
    }

    private async Task<int> CountLiveReviewsWithoutPendingReportsAsync(CancellationToken cancellationToken)
    {
        var activePlaceReviews = await _db.PlaceReviews
            .AsNoTracking()
            .CountAsync(review => !_db.ReviewReports.Any(report =>
                report.TargetType == ReviewReportTargetType.Place &&
                report.ReviewId == review.Id &&
                report.Status == ReviewReportStatus.Pending),
                cancellationToken);

        var activeEventReviews = await _db.EventReviews
            .AsNoTracking()
            .CountAsync(review => !_db.ReviewReports.Any(report =>
                report.TargetType == ReviewReportTargetType.Event &&
                report.ReviewId == review.Id &&
                report.Status == ReviewReportStatus.Pending),
                cancellationToken);

        var destinationReviews = await _db.DestinationReviews
            .AsNoTracking()
            .CountAsync(cancellationToken);

        var locationReviews = await _db.LocationReviews
            .AsNoTracking()
            .CountAsync(cancellationToken);

        return activePlaceReviews + activeEventReviews + destinationReviews + locationReviews;
    }

    private static string BuildDisplayName(
        string? displayName,
        string? firstName,
        string? lastName,
        string? username,
        string email)
    {
        if (!string.IsNullOrWhiteSpace(displayName))
            return displayName.Trim();

        var fullName = string.Join(" ", new[] { firstName, lastName }
            .Where(part => !string.IsNullOrWhiteSpace(part))
            .Select(part => part!.Trim()));

        if (!string.IsNullOrWhiteSpace(fullName))
            return fullName;

        if (!string.IsNullOrWhiteSpace(username))
            return username.Trim();

        return email.Trim();
    }
}
