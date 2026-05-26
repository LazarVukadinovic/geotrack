using Geotrack.Application.DTOs.Admin;
using Geotrack.Application.Interfaces.Service;
using Geotrack.Application.Security;
using Geotrack.Domain.Entities.Reviews;
using Geotrack.Domain.Entities.Users;
using Microsoft.EntityFrameworkCore;

namespace Geotrack.Infrastructure.Services;

public class AdminDashboardService : IAdminDashboardService
{
    private readonly AppDbContext _db;

    public AdminDashboardService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<AdminDashboardDto> GetDashboardAsync(int currentUserId, CancellationToken cancellationToken = default)
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
                Roles = user.UserRoles.Select(userRole => userRole.Role.Name).ToList()
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (currentUser is null)
            throw new KeyNotFoundException("Admin user was not found.");

        if (!currentUser.Roles.Any(role => RoleNames.Normalize(role) == RoleNames.Admin))
            throw new UnauthorizedAccessException("Admin role is required.");

        var territoriesCount = await _db.Territories
            .AsNoTracking()
            .CountAsync(cancellationToken);

        var assignedTerritoriesCount = await _db.TerritoryManagerTerritories
            .AsNoTracking()
            .Select(assignment => assignment.TerritoryId)
            .Distinct()
            .CountAsync(cancellationToken);

        var appUsersCount = await _db.Users
            .AsNoTracking()
            .CountAsync(user => !user.IsDeleted, cancellationToken);

        var suspendedUsersCount = await _db.Users
            .AsNoTracking()
            .CountAsync(user => !user.IsDeleted && user.IsSuspended, cancellationToken);

        var reviewsCount =
            await _db.PlaceReviews.AsNoTracking().CountAsync(cancellationToken) +
            await _db.EventReviews.AsNoTracking().CountAsync(cancellationToken) +
            await _db.DestinationReviews.AsNoTracking().CountAsync(cancellationToken) +
            await _db.LocationReviews.AsNoTracking().CountAsync(cancellationToken);

        var reportedReviewsCount = await _db.ReviewReports
            .AsNoTracking()
            .CountAsync(cancellationToken);

        var pendingReportsCount = await _db.ReviewReports
            .AsNoTracking()
            .CountAsync(report => report.Status == ReviewReportStatus.Pending, cancellationToken);

        var supportTicketsCount = await _db.SupportRequests
            .AsNoTracking()
            .CountAsync(cancellationToken);

        var openSupportTicketsCount = await _db.SupportRequests
            .AsNoTracking()
            .CountAsync(ticket =>
                ticket.Status == SupportRequestStatuses.Open ||
                ticket.Status == "Received",
                cancellationToken);

        var inProgressSupportTicketsCount = await _db.SupportRequests
            .AsNoTracking()
            .CountAsync(ticket => ticket.Status == SupportRequestStatuses.InProgress, cancellationToken);

        var resolvedSupportTicketsCount = await _db.SupportRequests
            .AsNoTracking()
            .CountAsync(ticket => ticket.Status == SupportRequestStatuses.Resolved, cancellationToken);

        var dismissedSupportTicketsCount = await _db.SupportRequests
            .AsNoTracking()
            .CountAsync(ticket => ticket.Status == SupportRequestStatuses.Dismissed, cancellationToken);

        var unresolvedSupportTicketsCount = openSupportTicketsCount + inProgressSupportTicketsCount;

        var highPrioritySupportTicketsCount = await _db.SupportRequests
            .AsNoTracking()
            .CountAsync(ticket =>
                (
                    ticket.Status == SupportRequestStatuses.Open ||
                    ticket.Status == "Received" ||
                    ticket.Status == SupportRequestStatuses.InProgress
                ) &&
                (
                    ticket.Priority == SupportRequestPriorities.High ||
                    ticket.Priority == SupportRequestPriorities.Critical
                ),
                cancellationToken);

        return new AdminDashboardDto
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
                RoleDisplayName = "Administrator"
            },
            Stats = new AdminDashboardStatsDto
            {
                TerritoriesCount = territoriesCount,
                AssignedTerritoriesCount = assignedTerritoriesCount,
                AppUsersCount = appUsersCount,
                SuspendedUsersCount = suspendedUsersCount,
                ReviewsCount = reviewsCount,
                ReportedReviewsCount = reportedReviewsCount,
                PendingReportsCount = pendingReportsCount,
                SupportTicketsCount = supportTicketsCount,
                OpenSupportTicketsCount = openSupportTicketsCount,
                InProgressSupportTicketsCount = inProgressSupportTicketsCount,
                ResolvedSupportTicketsCount = resolvedSupportTicketsCount,
                DismissedSupportTicketsCount = dismissedSupportTicketsCount,
                UnresolvedSupportTicketsCount = unresolvedSupportTicketsCount,
                HighPrioritySupportTicketsCount = highPrioritySupportTicketsCount
            },
            QuickLinks = new AdminDashboardQuickLinksDto(),
            Badges = new AdminDashboardBadgesDto
            {
                PendingReportsCount = pendingReportsCount,
                SupportReportsCount = unresolvedSupportTicketsCount
            }
        };
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
