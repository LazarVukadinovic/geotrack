using System.Globalization;
using Geotrack.Application.DTOs.Admin;
using Geotrack.Application.Interfaces.Service;
using Geotrack.Application.Security;
using Geotrack.Domain.Entities.Reviews;
using Geotrack.Domain.Entities.Users;
using Microsoft.EntityFrameworkCore;

namespace Geotrack.Infrastructure.Services;

public sealed class AdminUserManagementService : IAdminUserManagementService
{
    private const int DefaultTake = 25;
    private const int MaxTake = 50;
    private const int PreviewSectionTake = 10;
    private const int PreviewExcerptLength = 220;

    private readonly AppDbContext _db;
    private readonly IAdminTerritoryManagementService _territories;

    public AdminUserManagementService(
        AppDbContext db,
        IAdminTerritoryManagementService territories)
    {
        _db = db;
        _territories = territories;
    }

    public async Task<AdminUserListResponseDto> GetUsersAsync(
        int currentUserId,
        AdminUserListRequestDto request,
        CancellationToken cancellationToken = default)
    {
        await EnsureCurrentAdminAsync(currentUserId, cancellationToken);

        var skip = Math.Max(0, request.Skip);
        var take = Math.Clamp(request.Take <= 0 ? DefaultTake : request.Take, 1, MaxTake);
        var tab = NormalizeTab(request.Tab);
        var status = NormalizeStatus(request.Status);

        var query = _db.Users.AsNoTracking().AsQueryable();

        if (tab == "territory-managers")
        {
            query = query.Where(user =>
                user.UserRoles.Any(userRole => userRole.Role.Name == RoleNames.TerritoryManager));
        }

        query = ApplyStatusFilter(query, status);
        query = ApplySearch(query, request.Search);

        var totalCount = await query.CountAsync(cancellationToken);
        var userIds = await ApplySort(query, request.Sort)
            .Skip(skip)
            .Take(take)
            .Select(user => user.Id)
            .ToListAsync(cancellationToken);

        var items = await BuildItemsAsync(currentUserId, userIds, cancellationToken);
        var order = userIds
            .Select((userId, index) => new { userId, index })
            .ToDictionary(item => item.userId, item => item.index);

        return new AdminUserListResponseDto
        {
            Items = items.OrderBy(item => order[item.UserId]).ToList(),
            TotalCount = totalCount,
            Skip = skip,
            Take = take,
            NextSkip = skip + take < totalCount ? skip + take : null,
            HasMore = skip + take < totalCount,
            Counts = await BuildCountsAsync(cancellationToken)
        };
    }

    public async Task<AdminUserListItemDto> GetUserAsync(
        int currentUserId,
        int targetUserId,
        CancellationToken cancellationToken = default)
    {
        await EnsureCurrentAdminAsync(currentUserId, cancellationToken);

        if (targetUserId <= 0)
            throw new InvalidOperationException("userId must be positive.");

        return await BuildSingleItemAsync(currentUserId, targetUserId, cancellationToken);
    }

    public async Task<AdminUserPreviewDto> GetUserPreviewAsync(
        int currentUserId,
        int targetUserId,
        CancellationToken cancellationToken = default)
    {
        await EnsureCurrentAdminAsync(currentUserId, cancellationToken);

        if (targetUserId <= 0)
            throw new InvalidOperationException("userId must be positive.");

        var listItem = await BuildSingleItemAsync(currentUserId, targetUserId, cancellationToken);
        var user = await GetPreviewUserRowAsync(targetUserId, cancellationToken);
        var assignedRegions = await BuildAssignedRegionsAsync(targetUserId, cancellationToken);
        var roles = await BuildPreviewRolesAsync(listItem.Roles, targetUserId, assignedRegions, cancellationToken);
        var summary = await BuildAccountSummaryAsync(targetUserId, user, listItem.ReviewsCount, cancellationToken);
        var reportsMade = await BuildReportsSectionAsync(targetUserId, reportsMadeByUser: true, cancellationToken);
        var reportsAgainstUser = await BuildReportsSectionAsync(targetUserId, reportsMadeByUser: false, cancellationToken);
        var recentReviews = await BuildRecentReviewsSectionAsync(targetUserId, cancellationToken);
        var moderationHistory = await BuildModerationHistorySectionAsync(targetUserId, user, cancellationToken);

        return new AdminUserPreviewDto
        {
            User = new AdminUserPreviewProfileDto
            {
                UserId = listItem.UserId,
                Id = listItem.Id,
                DisplayName = listItem.DisplayName,
                FirstName = listItem.FirstName,
                LastName = listItem.LastName,
                Email = listItem.Email,
                AvatarUrl = listItem.AvatarUrl,
                Initials = listItem.Initials,
                Status = listItem.Status,
                Roles = listItem.Roles,
                IsCurrentUser = listItem.IsCurrentUser,
                IsAdmin = listItem.IsAdmin,
                HasElevatedAccess = HasElevatedAccess(listItem.Roles),
                AccountType = ResolveAccountType(listItem.Roles),
                JoinedAt = listItem.JoinedAt,
                LastActiveAt = listItem.LastActiveAt,
                LastLoginAt = user.LastLoginAt,
                AssignedTerritories = listItem.AssignedTerritories,
                AssignedRegions = assignedRegions
            },
            Roles = roles,
            AccountSummary = summary,
            ReportsMade = reportsMade,
            ReportsAgainstUser = reportsAgainstUser,
            RecentReviews = recentReviews,
            ModerationHistory = moderationHistory,
            Permissions = new AdminUserPreviewPermissionsDto
            {
                CanAssignTerritoryManager = listItem.CanAssignTerritoryManager,
                CanSuspend = listItem.CanSuspend,
                CanUnsuspend = listItem.CanUnsuspend,
                CanDelete = listItem.CanDelete,
                CanViewReports = true,
                CanViewModerationHistory = true
            }
        };
    }

    public async Task<AdminUserListItemDto> SuspendUserAsync(
        int currentUserId,
        int targetUserId,
        CancellationToken cancellationToken = default)
    {
        var currentAdmin = await EnsureCurrentAdminAsync(currentUserId, cancellationToken);
        var target = await GetTargetUserForMutationAsync(targetUserId, cancellationToken);
        EnsureCanMutateAccount(currentAdmin, target, "suspend");

        if (target.IsDeleted)
            throw new InvalidOperationException("Deleted users cannot be suspended.");

        if (!target.IsSuspended)
        {
            var now = DateTime.UtcNow;
            target.IsSuspended = true;
            target.SuspendedAt = now;
            target.SuspendedByUserId = currentUserId;
            target.UpdatedAt = now;
            await RevokeRefreshTokensAsync(target.Id, now, cancellationToken);
            await _db.SaveChangesAsync(cancellationToken);
        }

        return await BuildSingleItemAsync(currentUserId, targetUserId, cancellationToken);
    }

    public async Task<AdminUserListItemDto> UnsuspendUserAsync(
        int currentUserId,
        int targetUserId,
        CancellationToken cancellationToken = default)
    {
        var currentAdmin = await EnsureCurrentAdminAsync(currentUserId, cancellationToken);
        var target = await GetTargetUserForMutationAsync(targetUserId, cancellationToken);
        EnsureCanMutateAccount(currentAdmin, target, "unsuspend");

        if (target.IsDeleted)
            throw new InvalidOperationException("Deleted users cannot be unsuspended.");

        if (target.IsSuspended)
        {
            target.IsSuspended = false;
            target.SuspendedAt = null;
            target.SuspendedByUserId = null;
            target.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(cancellationToken);
        }

        return await BuildSingleItemAsync(currentUserId, targetUserId, cancellationToken);
    }

    public async Task DeleteUserAsync(
        int currentUserId,
        int targetUserId,
        CancellationToken cancellationToken = default)
    {
        var currentAdmin = await EnsureCurrentAdminAsync(currentUserId, cancellationToken);
        var target = await GetTargetUserForMutationAsync(targetUserId, cancellationToken);
        EnsureCanMutateAccount(currentAdmin, target, "delete");

        if (!target.IsDeleted)
        {
            var now = DateTime.UtcNow;
            target.IsDeleted = true;
            target.DeletedAt = now;
            target.DeletedByUserId = currentUserId;
            target.IsSuspended = false;
            target.SuspendedAt = null;
            target.SuspendedByUserId = null;
            target.UpdatedAt = now;
            await RevokeRefreshTokensAsync(target.Id, now, cancellationToken);
            await _db.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task<AdminUserListItemDto> AssignTerritoryManagerAsync(
        int currentUserId,
        int targetUserId,
        AdminUserAssignTerritoryManagerRequestDto request,
        CancellationToken cancellationToken = default)
    {
        await EnsureCurrentAdminAsync(currentUserId, cancellationToken);

        if (request.TerritoryId <= 0)
            throw new InvalidOperationException("Territory id is required.");

        var target = await GetTargetUserForMutationAsync(targetUserId, cancellationToken);
        var roles = NormalizeRoles(target.UserRoles.Select(userRole => userRole.Role.Name));

        if (target.IsDeleted)
            throw new InvalidOperationException("Deleted users cannot be assigned as Territory Managers.");

        if (target.IsSuspended)
            throw new InvalidOperationException("Suspended users cannot be assigned as Territory Managers.");

        if (roles.Contains(RoleNames.Admin, StringComparer.Ordinal))
            throw new InvalidOperationException("Administrators cannot be assigned as Territory Managers.");

        if (roles.Contains(RoleNames.TerritoryManager, StringComparer.Ordinal))
            throw new InvalidOperationException("This user already has the Territory Manager role.");

        await _territories.AssignManagerAsync(
            request.TerritoryId,
            new AdminAssignTerritoryManagerRequestDto { ManagerUserId = targetUserId },
            cancellationToken);

        return await BuildSingleItemAsync(currentUserId, targetUserId, cancellationToken);
    }

    private async Task<UserPreviewRow> GetPreviewUserRowAsync(
        int targetUserId,
        CancellationToken cancellationToken)
    {
        return await _db.Users
            .AsNoTracking()
            .Where(user => user.Id == targetUserId)
            .Select(user => new UserPreviewRow
            {
                UserId = user.Id,
                DisplayName = user.DisplayName,
                FirstName = user.FirstName,
                LastName = user.LastName,
                Username = user.Username,
                Email = user.Email,
                CreatedAt = user.CreatedAt,
                UpdatedAt = user.UpdatedAt,
                LastLoginAt = user.LastLoginAt,
                IsSuspended = user.IsSuspended,
                SuspendedAt = user.SuspendedAt,
                SuspendedByUserId = user.SuspendedByUserId,
                IsDeleted = user.IsDeleted,
                DeletedAt = user.DeletedAt,
                DeletedByUserId = user.DeletedByUserId
            })
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new KeyNotFoundException("User not found.");
    }

    private async Task<List<AdminUserAssignedRegionDto>> BuildAssignedRegionsAsync(
        int userId,
        CancellationToken cancellationToken)
    {
        return await _db.RegionalManagerRegions
            .AsNoTracking()
            .Where(assignment => assignment.UserId == userId && assignment.IsActive)
            .OrderBy(assignment => assignment.Region.Territory.Name)
            .ThenBy(assignment => assignment.Region.Name)
            .Select(assignment => new AdminUserAssignedRegionDto
            {
                RegionId = assignment.RegionId,
                RegionName = assignment.Region.Name,
                TerritoryId = assignment.Region.TerritoryId,
                TerritoryName = assignment.Region.Territory.Name,
                CountryCode = assignment.Region.Territory.CountryCode,
                AssignedAt = assignment.CreatedAt
            })
            .ToListAsync(cancellationToken);
    }

    private async Task<List<AdminUserRoleDto>> BuildPreviewRolesAsync(
        IReadOnlyCollection<string> roles,
        int userId,
        IReadOnlyCollection<AdminUserAssignedRegionDto> assignedRegions,
        CancellationToken cancellationToken)
    {
        var territoryScopes = await _db.TerritoryManagerTerritories
            .AsNoTracking()
            .Where(assignment => assignment.UserId == userId)
            .OrderBy(assignment => assignment.Territory.Name)
            .ThenBy(assignment => assignment.TerritoryId)
            .Select(assignment => new AdminUserAccessScopeDto
            {
                ScopeType = "Territory",
                ScopeId = assignment.TerritoryId,
                DisplayName = assignment.Territory.Name,
                CountryCode = assignment.Territory.CountryCode,
                AssignedAt = assignment.CreatedAt
            })
            .ToListAsync(cancellationToken);

        var regionScopes = assignedRegions
            .OrderBy(region => region.TerritoryName)
            .ThenBy(region => region.RegionName)
            .Select(region => new AdminUserAccessScopeDto
            {
                ScopeType = "Region",
                ScopeId = region.RegionId,
                DisplayName = $"{region.RegionName}, {region.TerritoryName}",
                CountryCode = region.CountryCode,
                AssignedAt = region.AssignedAt
            })
            .ToList();

        return roles
            .Select(role => RoleNames.Normalize(role))
            .Where(role => !string.IsNullOrWhiteSpace(role))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(role => role, StringComparer.Ordinal)
            .Select(role =>
            {
                var scopes = role switch
                {
                    RoleNames.Admin => new List<AdminUserAccessScopeDto>
                    {
                        new()
                        {
                            ScopeType = "Global",
                            DisplayName = "All administrator areas"
                        }
                    },
                    RoleNames.ContentModerator => new List<AdminUserAccessScopeDto>
                    {
                        new()
                        {
                            ScopeType = "Global",
                            DisplayName = "Review moderation"
                        }
                    },
                    RoleNames.TerritoryManager => territoryScopes,
                    RoleNames.RegionalManager => regionScopes,
                    RoleNames.TerritoryAssociate => new List<AdminUserAccessScopeDto>
                    {
                        new()
                        {
                            ScopeType = "Territory",
                            DisplayName = "Territory workspace"
                        }
                    },
                    _ => new List<AdminUserAccessScopeDto>()
                };

                return new AdminUserRoleDto
                {
                    RoleName = role,
                    DisplayName = RoleDisplayName(role),
                    AssignedScope = BuildScopeSummary(role, scopes),
                    Scopes = scopes
                };
            })
            .ToList();
    }

    private async Task<AdminUserAccountSummaryDto> BuildAccountSummaryAsync(
        int userId,
        UserPreviewRow user,
        int reviewsCount,
        CancellationToken cancellationToken)
    {
        var reportsMadeCount = await _db.ReviewReports
            .AsNoTracking()
            .CountAsync(report => report.ReporterUserId == userId, cancellationToken);

        var reportStatusCounts = await _db.ReviewReports
            .AsNoTracking()
            .Where(report => report.ReviewAuthorUserId == userId)
            .GroupBy(report => report.Status)
            .Select(group => new ReportStatusCountRow
            {
                Status = group.Key,
                Count = group.Count()
            })
            .ToListAsync(cancellationToken);

        var reportedContentCount = reportStatusCounts.Sum(row => row.Count);
        var pendingReports = reportStatusCounts
            .Where(row => row.Status == ReviewReportStatus.Pending)
            .Sum(row => row.Count);
        var acceptedReports = reportStatusCounts
            .Where(row => row.Status == ReviewReportStatus.Actioned)
            .Sum(row => row.Count);
        var rejectedReports = reportStatusCounts
            .Where(row => row.Status == ReviewReportStatus.Dismissed)
            .Sum(row => row.Count);

        var contributionCounts = await _db.PlaceContributions
            .AsNoTracking()
            .Where(contribution => contribution.UserId == userId)
            .GroupBy(contribution => contribution.Status)
            .Select(group => new ContributionStatusCountRow
            {
                Status = group.Key,
                Count = group.Count()
            })
            .ToListAsync(cancellationToken);

        var contributionsCount = contributionCounts.Sum(row => row.Count);
        var pendingContributionsCount = contributionCounts
            .Where(row => row.Status == PlaceContributionStatus.PendingReview)
            .Sum(row => row.Count);

        var favoritesCount = await _db.Favorites
            .AsNoTracking()
            .CountAsync(favorite => favorite.UserId == userId, cancellationToken);

        var collectionsCount = await _db.Collections
            .AsNoTracking()
            .CountAsync(collection => collection.UserId == userId, cancellationToken);

        var savedItemsCount = await _db.CollectionItems
            .AsNoTracking()
            .CountAsync(item => item.Collection.UserId == userId, cancellationToken);

        var ownedPlacesCount = await _db.UserPlaces
            .AsNoTracking()
            .CountAsync(place => place.UserId == userId, cancellationToken);

        var activityEventsCount = await _db.AnalyticsEvents
            .AsNoTracking()
            .CountAsync(activity => activity.UserId == userId, cancellationToken);

        var lastKnownActivityAt = await BuildLastKnownActivityAsync(userId, user, cancellationToken);
        var (riskLevel, riskSummary) = ResolveRiskSummary(pendingReports, acceptedReports, reportedContentCount);

        return new AdminUserAccountSummaryDto
        {
            ReviewsCount = reviewsCount,
            ReportsMadeCount = reportsMadeCount,
            ReportedContentCount = reportedContentCount,
            PendingReportsAgainstUserCount = pendingReports,
            AcceptedReportsAgainstUserCount = acceptedReports,
            RejectedReportsAgainstUserCount = rejectedReports,
            ContributionsCount = contributionsCount,
            PendingContributionsCount = pendingContributionsCount,
            FavoritesCount = favoritesCount,
            CollectionsCount = collectionsCount,
            SavedItemsCount = savedItemsCount,
            OwnedPlacesCount = ownedPlacesCount,
            ActivityEventsCount = activityEventsCount,
            KnownSuspensionCount = user.SuspendedAt.HasValue ? 1 : 0,
            LastKnownActivityAt = lastKnownActivityAt,
            RiskLevel = riskLevel,
            RiskSummary = riskSummary
        };
    }

    private async Task<DateTime?> BuildLastKnownActivityAsync(
        int userId,
        UserPreviewRow user,
        CancellationToken cancellationToken)
    {
        var lastAnalytics = await _db.AnalyticsEvents
            .AsNoTracking()
            .Where(activity => activity.UserId == userId)
            .Select(activity => (DateTime?)activity.OccurredAt)
            .MaxAsync(cancellationToken);

        var lastPlaceReview = await _db.PlaceReviews
            .AsNoTracking()
            .Where(review => review.UserId == userId)
            .Select(review => (DateTime?)review.CreatedAt)
            .MaxAsync(cancellationToken);

        var lastEventReview = await _db.EventReviews
            .AsNoTracking()
            .Where(review => review.UserId == userId)
            .Select(review => (DateTime?)review.CreatedAt)
            .MaxAsync(cancellationToken);

        var lastDestinationReview = await _db.DestinationReviews
            .AsNoTracking()
            .Where(review => review.UserId == userId)
            .Select(review => (DateTime?)review.CreatedAt)
            .MaxAsync(cancellationToken);

        var lastLocationReview = await _db.LocationReviews
            .AsNoTracking()
            .Where(review => review.UserId == userId)
            .Select(review => (DateTime?)review.CreatedAt)
            .MaxAsync(cancellationToken);

        var lastReport = await _db.ReviewReports
            .AsNoTracking()
            .Where(report => report.ReporterUserId == userId || report.ReviewAuthorUserId == userId)
            .Select(report => (DateTime?)report.CreatedAt)
            .MaxAsync(cancellationToken);

        var lastContribution = await _db.PlaceContributions
            .AsNoTracking()
            .Where(contribution => contribution.UserId == userId)
            .Select(contribution => (DateTime?)contribution.SubmittedAt)
            .MaxAsync(cancellationToken);

        var lastFavorite = await _db.Favorites
            .AsNoTracking()
            .Where(favorite => favorite.UserId == userId)
            .Select(favorite => (DateTime?)favorite.CreatedAt)
            .MaxAsync(cancellationToken);

        var lastCollectionItem = await _db.CollectionItems
            .AsNoTracking()
            .Where(item => item.Collection.UserId == userId)
            .Select(item => (DateTime?)item.CreatedAt)
            .MaxAsync(cancellationToken);

        return new[]
            {
                (DateTime?)user.CreatedAt,
                user.UpdatedAt,
                user.LastLoginAt,
                lastAnalytics,
                lastPlaceReview,
                lastEventReview,
                lastDestinationReview,
                lastLocationReview,
                lastReport,
                lastContribution,
                lastFavorite,
                lastCollectionItem
            }
            .Where(value => value.HasValue)
            .Max();
    }

    private async Task<AdminUserPreviewSectionDto<AdminUserReportPreviewDto>> BuildReportsSectionAsync(
        int userId,
        bool reportsMadeByUser,
        CancellationToken cancellationToken)
    {
        var query = _db.ReviewReports
            .AsNoTracking()
            .Where(report => reportsMadeByUser
                ? report.ReporterUserId == userId
                : report.ReviewAuthorUserId == userId);

        var totalCount = await query.CountAsync(cancellationToken);
        var rows = await query
            .OrderByDescending(report => report.CreatedAt)
            .ThenByDescending(report => report.Id)
            .Take(PreviewSectionTake)
            .Select(report => new ReportPreviewRow
            {
                ReportId = report.Id,
                TargetType = report.TargetType,
                ReviewId = report.ReviewId,
                EntityId = report.EntityId,
                EntityName = report.EntityName,
                RegionName = report.RegionName,
                Reason = report.Reason,
                Status = report.Status,
                Decision = report.Decision,
                ContentExcerpt = report.ReviewCommentSnapshot,
                ReporterUserId = report.ReporterUserId,
                ReporterDisplayName = report.ReporterUser.DisplayName,
                ReporterFirstName = report.ReporterUser.FirstName,
                ReporterLastName = report.ReporterUser.LastName,
                ReporterUsername = report.ReporterUser.Username,
                ReporterEmail = report.ReporterUser.Email,
                ReviewAuthorUserId = report.ReviewAuthorUserId,
                ReviewAuthorName = report.ReviewAuthorName,
                CreatedAt = report.CreatedAt,
                ModerationDecisionDate = report.ReviewedAt
            })
            .ToListAsync(cancellationToken);

        return new AdminUserPreviewSectionDto<AdminUserReportPreviewDto>
        {
            Items = rows.Select(MapReportPreview).ToList(),
            TotalCount = totalCount,
            Skip = 0,
            Take = PreviewSectionTake,
            HasMore = totalCount > PreviewSectionTake
        };
    }

    private async Task<AdminUserPreviewSectionDto<AdminUserRecentReviewDto>> BuildRecentReviewsSectionAsync(
        int userId,
        CancellationToken cancellationToken)
    {
        var placeCount = await _db.PlaceReviews
            .AsNoTracking()
            .CountAsync(review => review.UserId == userId, cancellationToken);
        var eventCount = await _db.EventReviews
            .AsNoTracking()
            .CountAsync(review => review.UserId == userId, cancellationToken);
        var destinationCount = await _db.DestinationReviews
            .AsNoTracking()
            .CountAsync(review => review.UserId == userId, cancellationToken);
        var locationCount = await _db.LocationReviews
            .AsNoTracking()
            .CountAsync(review => review.UserId == userId, cancellationToken);

        var rows = new List<RecentReviewRow>();
        rows.AddRange(await _db.PlaceReviews
            .AsNoTracking()
            .Where(review => review.UserId == userId)
            .OrderByDescending(review => review.CreatedAt)
            .ThenByDescending(review => review.Id)
            .Take(PreviewSectionTake)
            .Select(review => new RecentReviewRow
            {
                ReviewId = review.Id,
                TargetType = "Place",
                EntityId = review.PlaceId,
                EntityName = review.Place.Name,
                Rating = review.Rating,
                Comment = review.Comment,
                CreatedAt = review.CreatedAt,
                UpdatedAt = review.UpdatedAt
            })
            .ToListAsync(cancellationToken));

        rows.AddRange(await _db.EventReviews
            .AsNoTracking()
            .Where(review => review.UserId == userId)
            .OrderByDescending(review => review.CreatedAt)
            .ThenByDescending(review => review.Id)
            .Take(PreviewSectionTake)
            .Select(review => new RecentReviewRow
            {
                ReviewId = review.Id,
                TargetType = "Event",
                EntityId = review.EventId,
                EntityName = review.Event.Name,
                Rating = review.Rating,
                Comment = review.Comment,
                CreatedAt = review.CreatedAt,
                UpdatedAt = review.UpdatedAt
            })
            .ToListAsync(cancellationToken));

        rows.AddRange(await _db.DestinationReviews
            .AsNoTracking()
            .Where(review => review.UserId == userId)
            .OrderByDescending(review => review.CreatedAt)
            .ThenByDescending(review => review.Id)
            .Take(PreviewSectionTake)
            .Select(review => new RecentReviewRow
            {
                ReviewId = review.Id,
                TargetType = "Destination",
                EntityId = review.DestinationId,
                EntityName = review.Destination.Name,
                Rating = review.Rating,
                Comment = review.Comment,
                CreatedAt = review.CreatedAt,
                UpdatedAt = review.UpdatedAt
            })
            .ToListAsync(cancellationToken));

        rows.AddRange(await _db.LocationReviews
            .AsNoTracking()
            .Where(review => review.UserId == userId)
            .OrderByDescending(review => review.CreatedAt)
            .ThenByDescending(review => review.Id)
            .Take(PreviewSectionTake)
            .Select(review => new RecentReviewRow
            {
                ReviewId = review.Id,
                TargetType = "Location",
                EntityId = review.LocationId,
                EntityName = review.Location.Name,
                Rating = review.Rating,
                Comment = review.Comment,
                CreatedAt = review.CreatedAt,
                UpdatedAt = review.UpdatedAt
            })
            .ToListAsync(cancellationToken));

        var selectedRows = rows
            .OrderByDescending(row => row.CreatedAt)
            .ThenByDescending(row => row.ReviewId)
            .Take(PreviewSectionTake)
            .ToList();

        var placeReviewIds = selectedRows
            .Where(row => row.TargetType == "Place")
            .Select(row => row.ReviewId)
            .ToList();
        var eventReviewIds = selectedRows
            .Where(row => row.TargetType == "Event")
            .Select(row => row.ReviewId)
            .ToList();

        var reportCounts = await _db.ReviewReports
            .AsNoTracking()
            .Where(report =>
                (report.TargetType == ReviewReportTargetType.Place && placeReviewIds.Contains(report.ReviewId)) ||
                (report.TargetType == ReviewReportTargetType.Event && eventReviewIds.Contains(report.ReviewId)))
            .GroupBy(report => new { report.TargetType, report.ReviewId })
            .Select(group => new ReportCountRow
            {
                TargetType = group.Key.TargetType,
                ReviewId = group.Key.ReviewId,
                Count = group.Count()
            })
            .ToListAsync(cancellationToken);

        var reportCountByReview = reportCounts.ToDictionary(
            row => $"{row.TargetType}:{row.ReviewId}",
            row => row.Count);

        var items = selectedRows
            .Select(row =>
            {
                var reportKey = row.TargetType switch
                {
                    "Place" => $"{ReviewReportTargetType.Place}:{row.ReviewId}",
                    "Event" => $"{ReviewReportTargetType.Event}:{row.ReviewId}",
                    _ => string.Empty
                };

                return new AdminUserRecentReviewDto
                {
                    ReviewId = row.ReviewId,
                    TargetType = row.TargetType,
                    EntityId = row.EntityId,
                    EntityName = row.EntityName,
                    Rating = row.Rating,
                    CommentExcerpt = BuildExcerpt(row.Comment),
                    ReportCount = string.IsNullOrEmpty(reportKey) ? 0 : reportCountByReview.GetValueOrDefault(reportKey),
                    Status = "Visible",
                    CreatedAt = row.CreatedAt,
                    UpdatedAt = row.UpdatedAt,
                    DetailRoute = null
                };
            })
            .ToList();

        var totalCount = placeCount + eventCount + destinationCount + locationCount;

        return new AdminUserPreviewSectionDto<AdminUserRecentReviewDto>
        {
            Items = items,
            TotalCount = totalCount,
            Skip = 0,
            Take = PreviewSectionTake,
            HasMore = totalCount > PreviewSectionTake
        };
    }

    private async Task<AdminUserPreviewSectionDto<AdminUserModerationHistoryItemDto>> BuildModerationHistorySectionAsync(
        int userId,
        UserPreviewRow user,
        CancellationToken cancellationToken)
    {
        var accountItems = await BuildAccountModerationHistoryItemsAsync(user, cancellationToken);
        var reportDecisionCount = await _db.ReviewReports
            .AsNoTracking()
            .CountAsync(report =>
                (report.ReporterUserId == userId || report.ReviewAuthorUserId == userId) &&
                report.ReviewedAt.HasValue,
                cancellationToken);
        var contributionDecisionCount = await _db.PlaceContributions
            .AsNoTracking()
            .CountAsync(contribution =>
                contribution.UserId == userId &&
                contribution.ReviewedAt.HasValue,
                cancellationToken);

        var items = new List<AdminUserModerationHistoryItemDto>(accountItems);

        var reportRows = await _db.ReviewReports
            .AsNoTracking()
            .Where(report =>
                (report.ReporterUserId == userId || report.ReviewAuthorUserId == userId) &&
                report.ReviewedAt.HasValue)
            .OrderByDescending(report => report.ReviewedAt)
            .ThenByDescending(report => report.Id)
            .Take(PreviewSectionTake)
            .Select(report => new ModerationReportRow
            {
                ReportId = report.Id,
                TargetType = report.TargetType,
                EntityName = report.EntityName,
                Status = report.Status,
                Decision = report.Decision,
                DecisionNote = report.DecisionNote,
                ReviewedAt = report.ReviewedAt!.Value,
                ReviewedByUserId = report.ReviewedByUserId,
                ReviewerDisplayName = report.ReviewedByUser == null ? null : report.ReviewedByUser.DisplayName,
                ReviewerFirstName = report.ReviewedByUser == null ? null : report.ReviewedByUser.FirstName,
                ReviewerLastName = report.ReviewedByUser == null ? null : report.ReviewedByUser.LastName,
                ReviewerUsername = report.ReviewedByUser == null ? null : report.ReviewedByUser.Username,
                ReviewerEmail = report.ReviewedByUser == null ? null : report.ReviewedByUser.Email,
                ReporterUserId = report.ReporterUserId,
                ReviewAuthorUserId = report.ReviewAuthorUserId
            })
            .ToListAsync(cancellationToken);

        items.AddRange(reportRows.Select(row => MapReportHistoryItem(row, userId)));

        var contributionRows = await _db.PlaceContributions
            .AsNoTracking()
            .Where(contribution =>
                contribution.UserId == userId &&
                contribution.ReviewedAt.HasValue)
            .OrderByDescending(contribution => contribution.ReviewedAt)
            .ThenByDescending(contribution => contribution.Id)
            .Take(PreviewSectionTake)
            .Select(contribution => new ModerationContributionRow
            {
                ContributionId = contribution.Id,
                Name = contribution.Name,
                Status = contribution.Status,
                ReviewNote = contribution.ReviewNote,
                ReviewedAt = contribution.ReviewedAt!.Value,
                ReviewedByUserId = contribution.ReviewedByUserId,
                ReviewerDisplayName = contribution.ReviewedByUser == null ? null : contribution.ReviewedByUser.DisplayName,
                ReviewerFirstName = contribution.ReviewedByUser == null ? null : contribution.ReviewedByUser.FirstName,
                ReviewerLastName = contribution.ReviewedByUser == null ? null : contribution.ReviewedByUser.LastName,
                ReviewerUsername = contribution.ReviewedByUser == null ? null : contribution.ReviewedByUser.Username,
                ReviewerEmail = contribution.ReviewedByUser == null ? null : contribution.ReviewedByUser.Email
            })
            .ToListAsync(cancellationToken);

        items.AddRange(contributionRows.Select(MapContributionHistoryItem));

        var selectedItems = items
            .OrderByDescending(item => item.CreatedAt)
            .ThenByDescending(item => item.SourceId ?? 0)
            .Take(PreviewSectionTake)
            .ToList();
        var totalCount = accountItems.Count + reportDecisionCount + contributionDecisionCount;

        return new AdminUserPreviewSectionDto<AdminUserModerationHistoryItemDto>
        {
            Items = selectedItems,
            TotalCount = totalCount,
            Skip = 0,
            Take = PreviewSectionTake,
            HasMore = totalCount > selectedItems.Count
        };
    }

    private async Task<List<AdminUserModerationHistoryItemDto>> BuildAccountModerationHistoryItemsAsync(
        UserPreviewRow user,
        CancellationToken cancellationToken)
    {
        var actorIds = new[] { user.SuspendedByUserId, user.DeletedByUserId }
            .Where(id => id.HasValue)
            .Select(id => id!.Value)
            .Distinct()
            .ToList();

        var actorNames = actorIds.Count == 0
            ? new Dictionary<int, string>()
            : await _db.Users
                .AsNoTracking()
                .Where(actor => actorIds.Contains(actor.Id))
                .Select(actor => new ActorNameRow
                {
                    UserId = actor.Id,
                    DisplayName = actor.DisplayName,
                    FirstName = actor.FirstName,
                    LastName = actor.LastName,
                    Username = actor.Username,
                    Email = actor.Email
                })
                .ToDictionaryAsync(
                    actor => actor.UserId,
                    actor => BuildDisplayName(
                        actor.DisplayName,
                        actor.FirstName,
                        actor.LastName,
                        actor.Username,
                        actor.Email),
                    cancellationToken);

        var items = new List<AdminUserModerationHistoryItemDto>();

        if (user.SuspendedAt.HasValue)
        {
            items.Add(new AdminUserModerationHistoryItemDto
            {
                EventId = $"account-suspended:{user.UserId}",
                EventType = "Account suspended",
                Summary = "Account was suspended.",
                Status = "Suspended",
                ActorUserId = user.SuspendedByUserId,
                ActorDisplayName = ResolveActorName(actorNames, user.SuspendedByUserId),
                CreatedAt = user.SuspendedAt.Value,
                Source = "Account",
                SourceId = user.UserId
            });
        }

        if (user.DeletedAt.HasValue)
        {
            items.Add(new AdminUserModerationHistoryItemDto
            {
                EventId = $"account-deleted:{user.UserId}",
                EventType = "Account deactivated",
                Summary = "Account was soft-deactivated.",
                Status = "Deleted",
                ActorUserId = user.DeletedByUserId,
                ActorDisplayName = ResolveActorName(actorNames, user.DeletedByUserId),
                CreatedAt = user.DeletedAt.Value,
                Source = "Account",
                SourceId = user.UserId
            });
        }

        return items;
    }

    private async Task<AdminUserListItemDto> BuildSingleItemAsync(
        int currentUserId,
        int targetUserId,
        CancellationToken cancellationToken)
    {
        var items = await BuildItemsAsync(currentUserId, [targetUserId], cancellationToken);
        return items.SingleOrDefault()
            ?? throw new KeyNotFoundException("User not found.");
    }

    private async Task<List<AdminUserListItemDto>> BuildItemsAsync(
        int currentUserId,
        IReadOnlyCollection<int> userIds,
        CancellationToken cancellationToken)
    {
        var ids = userIds.Distinct().ToList();
        if (ids.Count == 0)
            return new List<AdminUserListItemDto>();

        var users = await _db.Users
            .AsNoTracking()
            .Where(user => ids.Contains(user.Id))
            .Select(user => new UserRow
            {
                UserId = user.Id,
                DisplayName = user.DisplayName,
                FirstName = user.FirstName,
                LastName = user.LastName,
                Username = user.Username,
                Email = user.Email,
                AvatarUrl = user.AvatarUrl,
                CreatedAt = user.CreatedAt,
                LastLoginAt = user.LastLoginAt,
                IsSuspended = user.IsSuspended,
                IsDeleted = user.IsDeleted
            })
            .ToListAsync(cancellationToken);

        var roles = await _db.UserRoles
            .AsNoTracking()
            .Where(userRole => ids.Contains(userRole.UserId))
            .Select(userRole => new RoleRow
            {
                UserId = userRole.UserId,
                RoleName = userRole.Role.Name
            })
            .ToListAsync(cancellationToken);

        var assignedTerritories = await _db.TerritoryManagerTerritories
            .AsNoTracking()
            .Where(assignment => ids.Contains(assignment.UserId))
            .Select(assignment => new TerritoryRow
            {
                UserId = assignment.UserId,
                TerritoryId = assignment.TerritoryId,
                TerritoryName = assignment.Territory.Name,
                CountryCode = assignment.Territory.CountryCode
            })
            .ToListAsync(cancellationToken);

        var reviewCounts = await BuildReviewCountsAsync(ids, cancellationToken);
        var reportedCounts = await _db.ReviewReports
            .AsNoTracking()
            .Where(report =>
                ids.Contains(report.ReviewAuthorUserId) &&
                report.Status == ReviewReportStatus.Pending)
            .GroupBy(report => report.ReviewAuthorUserId)
            .Select(group => new CountRow { UserId = group.Key, Count = group.Count() })
            .ToDictionaryAsync(row => row.UserId, row => row.Count, cancellationToken);

        var rolesByUser = roles
            .GroupBy(role => role.UserId)
            .ToDictionary(
                group => group.Key,
                group => NormalizeRoles(group.Select(role => role.RoleName)));

        var territoriesByUser = assignedTerritories
            .GroupBy(territory => territory.UserId)
            .ToDictionary(
                group => group.Key,
                group => group
                    .OrderBy(territory => territory.TerritoryName)
                    .ThenBy(territory => territory.TerritoryId)
                    .Select(territory => new AdminUserAssignedTerritoryDto
                    {
                        TerritoryId = territory.TerritoryId,
                        TerritoryName = territory.TerritoryName,
                        DisplayName = territory.TerritoryName,
                        CountryCode = territory.CountryCode
                    })
                    .ToList());

        return users.Select(user =>
        {
            var displayName = BuildDisplayName(
                user.DisplayName,
                user.FirstName,
                user.LastName,
                user.Username,
                user.Email);
            var userRoles = rolesByUser.GetValueOrDefault(user.UserId, new List<string>());
            var isAdmin = userRoles.Contains(RoleNames.Admin, StringComparer.Ordinal);
            var isTerritoryManager = userRoles.Contains(RoleNames.TerritoryManager, StringComparer.Ordinal);
            var isCurrentUser = user.UserId == currentUserId;
            var status = ResolveStatus(user.IsDeleted, user.IsSuspended);
            var territories = territoriesByUser.GetValueOrDefault(user.UserId, new List<AdminUserAssignedTerritoryDto>());

            return new AdminUserListItemDto
            {
                UserId = user.UserId,
                Id = user.UserId.ToString(CultureInfo.InvariantCulture),
                DisplayName = displayName,
                FirstName = user.FirstName,
                LastName = user.LastName,
                Email = user.Email,
                AvatarUrl = user.AvatarUrl,
                Initials = BuildInitials(displayName, user.Email),
                Status = status,
                Roles = userRoles,
                IsAdmin = isAdmin,
                IsCurrentUser = isCurrentUser,
                ReviewsCount = reviewCounts.GetValueOrDefault(user.UserId),
                ReportedCount = reportedCounts.GetValueOrDefault(user.UserId),
                JoinedAt = user.CreatedAt,
                LastActiveAt = user.LastLoginAt,
                AssignedTerritories = territories,
                AssignedTerritoryCount = territories.Count,
                CanAssignTerritoryManager =
                    status == "Active" &&
                    !isAdmin &&
                    !isTerritoryManager,
                CanSuspend =
                    status == "Active" &&
                    !isCurrentUser &&
                    !isAdmin,
                CanUnsuspend =
                    status == "Suspended" &&
                    !isCurrentUser &&
                    !isAdmin,
                CanDelete =
                    status != "Deleted" &&
                    !isCurrentUser &&
                    !isAdmin
            };
        }).ToList();
    }

    private static AdminUserReportPreviewDto MapReportPreview(ReportPreviewRow row) => new()
    {
        ReportId = row.ReportId,
        ContentType = "Review",
        TargetType = row.TargetType.ToString(),
        ReviewId = row.ReviewId,
        EntityId = row.EntityId,
        EntityName = row.EntityName,
        RegionName = row.RegionName,
        Reason = row.Reason.ToString(),
        Status = MapReportStatus(row.Status),
        Decision = MapReportDecision(row.Decision),
        ActionTaken = ResolveReportActionTaken(row.Status, row.Decision),
        ContentExcerpt = BuildExcerpt(row.ContentExcerpt),
        ReporterUserId = row.ReporterUserId,
        ReporterName = BuildDisplayName(
            row.ReporterDisplayName,
            row.ReporterFirstName,
            row.ReporterLastName,
            row.ReporterUsername,
            row.ReporterEmail),
        ReviewAuthorUserId = row.ReviewAuthorUserId,
        ReviewAuthorName = row.ReviewAuthorName,
        CreatedAt = row.CreatedAt,
        ModerationDecisionDate = row.ModerationDecisionDate,
        DetailRoute = null
    };

    private static AdminUserModerationHistoryItemDto MapReportHistoryItem(
        ModerationReportRow row,
        int targetUserId)
    {
        var status = MapReportStatus(row.Status);
        var perspective = row.ReviewAuthorUserId == targetUserId
            ? "against this user's review"
            : "submitted by this user";

        return new AdminUserModerationHistoryItemDto
        {
            EventId = $"review-report:{row.ReportId}",
            EventType = "Review report decision",
            Summary = $"{status}: report {perspective} for {row.EntityName}.",
            Status = status,
            ActorUserId = row.ReviewedByUserId,
            ActorDisplayName = BuildDisplayName(
                row.ReviewerDisplayName,
                row.ReviewerFirstName,
                row.ReviewerLastName,
                row.ReviewerUsername,
                row.ReviewerEmail ?? string.Empty),
            CreatedAt = row.ReviewedAt,
            Note = string.IsNullOrWhiteSpace(row.DecisionNote) ? null : row.DecisionNote,
            Source = "ReviewReport",
            SourceId = row.ReportId
        };
    }

    private static AdminUserModerationHistoryItemDto MapContributionHistoryItem(
        ModerationContributionRow row)
    {
        var status = row.Status.ToString();
        return new AdminUserModerationHistoryItemDto
        {
            EventId = $"place-contribution:{row.ContributionId}",
            EventType = "Contribution decision",
            Summary = $"{status}: place contribution \"{row.Name}\".",
            Status = status,
            ActorUserId = row.ReviewedByUserId,
            ActorDisplayName = BuildDisplayName(
                row.ReviewerDisplayName,
                row.ReviewerFirstName,
                row.ReviewerLastName,
                row.ReviewerUsername,
                row.ReviewerEmail ?? string.Empty),
            CreatedAt = row.ReviewedAt,
            Note = string.IsNullOrWhiteSpace(row.ReviewNote) ? null : row.ReviewNote,
            Source = "PlaceContribution",
            SourceId = row.ContributionId
        };
    }

    private static (string RiskLevel, string RiskSummary) ResolveRiskSummary(
        int pendingReportsAgainstUser,
        int acceptedReportsAgainstUser,
        int totalReportsAgainstUser)
    {
        if (acceptedReportsAgainstUser > 0 || pendingReportsAgainstUser >= 3)
        {
            return ("High", "Accepted or multiple pending reports exist for this user's content.");
        }

        if (pendingReportsAgainstUser > 0 || totalReportsAgainstUser > 0)
        {
            return ("Medium", "This user's content has report activity to review.");
        }

        return ("Low", "No pending moderation risk.");
    }

    private static string MapReportStatus(ReviewReportStatus status) => status switch
    {
        ReviewReportStatus.Pending => "Pending",
        ReviewReportStatus.Actioned => "Accepted",
        ReviewReportStatus.Dismissed => "Rejected",
        _ => status.ToString()
    };

    private static string? MapReportDecision(ReviewReportDecision? decision) => decision switch
    {
        ReviewReportDecision.KeepReview => "Keep review",
        ReviewReportDecision.RemoveReview => "Remove review",
        _ => null
    };

    private static string ResolveReportActionTaken(
        ReviewReportStatus status,
        ReviewReportDecision? decision)
    {
        if (status == ReviewReportStatus.Pending)
            return "Pending moderation";

        if (decision == ReviewReportDecision.RemoveReview)
            return "Review removed";

        if (decision == ReviewReportDecision.KeepReview)
            return "Content kept";

        return status == ReviewReportStatus.Actioned ? "Action taken" : "No action";
    }

    private static string BuildExcerpt(string? value)
    {
        var trimmed = value?.Trim() ?? string.Empty;
        if (trimmed.Length <= PreviewExcerptLength)
            return trimmed;

        return trimmed[..PreviewExcerptLength].TrimEnd() + "...";
    }

    private static bool HasElevatedAccess(IReadOnlyCollection<string> roles) =>
        roles.Any(role =>
        {
            var normalized = RoleNames.Normalize(role);
            return normalized == RoleNames.Admin ||
                   normalized == RoleNames.ContentModerator ||
                   normalized == RoleNames.TerritoryManager ||
                   normalized == RoleNames.TerritoryAssociate ||
                   normalized == RoleNames.RegionalManager;
        });

    private static string ResolveAccountType(IReadOnlyCollection<string> roles)
    {
        var normalizedRoles = NormalizeRoles(roles);
        if (normalizedRoles.Contains(RoleNames.Admin, StringComparer.Ordinal))
            return "Administrator";

        if (normalizedRoles.Contains(RoleNames.ContentModerator, StringComparer.Ordinal))
            return "Content Moderator";

        if (normalizedRoles.Contains(RoleNames.TerritoryManager, StringComparer.Ordinal))
            return "Territory Manager";

        if (normalizedRoles.Contains(RoleNames.TerritoryAssociate, StringComparer.Ordinal))
            return "Territory Associate";

        if (normalizedRoles.Contains(RoleNames.RegionalManager, StringComparer.Ordinal))
            return "Regional Manager";

        return "App User";
    }

    private static string RoleDisplayName(string role) => RoleNames.Normalize(role) switch
    {
        RoleNames.Admin => "Administrator",
        RoleNames.ContentModerator => "Content Moderator",
        RoleNames.TerritoryManager => "Territory Manager",
        RoleNames.TerritoryAssociate => "Territory Associate",
        RoleNames.RegionalManager => "Regional Manager",
        RoleNames.RegisteredUser => "Registered User",
        var value when !string.IsNullOrWhiteSpace(value) => value,
        _ => "Role"
    };

    private static string BuildScopeSummary(
        string role,
        IReadOnlyCollection<AdminUserAccessScopeDto> scopes)
    {
        if (role == RoleNames.Admin)
            return "Global";

        if (role == RoleNames.ContentModerator)
            return "Review moderation";

        if (scopes.Count == 0)
            return "No assigned scope";

        if (scopes.Count == 1)
            return scopes.First().DisplayName;

        return $"{scopes.First().DisplayName} +{scopes.Count - 1}";
    }

    private static string? ResolveActorName(
        IReadOnlyDictionary<int, string> actorNames,
        int? actorUserId)
    {
        if (!actorUserId.HasValue)
            return null;

        return actorNames.GetValueOrDefault(actorUserId.Value);
    }

    private async Task<Dictionary<int, int>> BuildReviewCountsAsync(
        IReadOnlyCollection<int> userIds,
        CancellationToken cancellationToken)
    {
        var counts = new Dictionary<int, int>();

        async Task AddCountsAsync(IQueryable<CountRow> query)
        {
            var rows = await query.ToListAsync(cancellationToken);
            foreach (var row in rows)
            {
                counts[row.UserId] = counts.GetValueOrDefault(row.UserId) + row.Count;
            }
        }

        await AddCountsAsync(_db.PlaceReviews
            .AsNoTracking()
            .Where(review => userIds.Contains(review.UserId))
            .GroupBy(review => review.UserId)
            .Select(group => new CountRow { UserId = group.Key, Count = group.Count() }));

        await AddCountsAsync(_db.EventReviews
            .AsNoTracking()
            .Where(review => userIds.Contains(review.UserId))
            .GroupBy(review => review.UserId)
            .Select(group => new CountRow { UserId = group.Key, Count = group.Count() }));

        await AddCountsAsync(_db.DestinationReviews
            .AsNoTracking()
            .Where(review => userIds.Contains(review.UserId))
            .GroupBy(review => review.UserId)
            .Select(group => new CountRow { UserId = group.Key, Count = group.Count() }));

        await AddCountsAsync(_db.LocationReviews
            .AsNoTracking()
            .Where(review => userIds.Contains(review.UserId))
            .GroupBy(review => review.UserId)
            .Select(group => new CountRow { UserId = group.Key, Count = group.Count() }));

        return counts;
    }

    private async Task<AdminUserListCountsDto> BuildCountsAsync(CancellationToken cancellationToken)
    {
        return new AdminUserListCountsDto
        {
            AppUsersCount = await _db.Users
                .AsNoTracking()
                .CountAsync(user => !user.IsDeleted, cancellationToken),
            TerritoryManagersCount = await _db.Users
                .AsNoTracking()
                .CountAsync(user =>
                    !user.IsDeleted &&
                    user.UserRoles.Any(userRole => userRole.Role.Name == RoleNames.TerritoryManager),
                    cancellationToken),
            ActiveCount = await _db.Users
                .AsNoTracking()
                .CountAsync(user => !user.IsDeleted && !user.IsSuspended, cancellationToken),
            SuspendedCount = await _db.Users
                .AsNoTracking()
                .CountAsync(user => !user.IsDeleted && user.IsSuspended, cancellationToken),
            DeletedCount = await _db.Users
                .AsNoTracking()
                .CountAsync(user => user.IsDeleted, cancellationToken)
        };
    }

    private async Task<UserSecurityRow> EnsureCurrentAdminAsync(
        int currentUserId,
        CancellationToken cancellationToken)
    {
        if (currentUserId <= 0)
            throw new UnauthorizedAccessException("Invalid admin session.");

        var currentUser = await _db.Users
            .AsNoTracking()
            .Where(user => user.Id == currentUserId)
            .Select(user => new UserSecurityRow
            {
                UserId = user.Id,
                IsSuspended = user.IsSuspended,
                IsDeleted = user.IsDeleted,
                Roles = user.UserRoles.Select(userRole => userRole.Role.Name).ToList()
            })
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new UnauthorizedAccessException("Invalid admin session.");

        var roles = NormalizeRoles(currentUser.Roles);
        if (!roles.Contains(RoleNames.Admin, StringComparer.Ordinal))
            throw new UnauthorizedAccessException("Admin role is required.");

        if (currentUser.IsDeleted || currentUser.IsSuspended)
            throw new UnauthorizedAccessException("Admin account is not active.");

        currentUser.Roles = roles;
        return currentUser;
    }

    private async Task<User> GetTargetUserForMutationAsync(
        int targetUserId,
        CancellationToken cancellationToken)
    {
        if (targetUserId <= 0)
            throw new InvalidOperationException("userId must be positive.");

        return await _db.Users
            .Include(user => user.UserRoles)
                .ThenInclude(userRole => userRole.Role)
            .SingleOrDefaultAsync(user => user.Id == targetUserId, cancellationToken)
            ?? throw new KeyNotFoundException("User not found.");
    }

    private static void EnsureCanMutateAccount(
        UserSecurityRow currentAdmin,
        User target,
        string action)
    {
        if (currentAdmin.UserId == target.Id)
            throw new InvalidOperationException($"Administrators cannot {action} their own account.");

        var roles = NormalizeRoles(target.UserRoles.Select(userRole => userRole.Role.Name));
        if (roles.Contains(RoleNames.Admin, StringComparer.Ordinal))
            throw new InvalidOperationException("Administrator accounts cannot be changed from User Management.");
    }

    private async Task RevokeRefreshTokensAsync(
        int userId,
        DateTime now,
        CancellationToken cancellationToken)
    {
        await _db.RefreshTokens
            .Where(token => token.UserId == userId && !token.IsRevoked)
            .ExecuteUpdateAsync(updates => updates
                .SetProperty(token => token.IsRevoked, true)
                .SetProperty(token => token.RevokedAt, now),
                cancellationToken);
    }

    private static IQueryable<User> ApplySearch(
        IQueryable<User> query,
        string? search)
    {
        if (string.IsNullOrWhiteSpace(search))
            return query;

        var term = search.Trim().ToLowerInvariant();
        return query.Where(user =>
            user.Email.ToLower().Contains(term) ||
            user.Username.ToLower().Contains(term) ||
            (user.DisplayName != null && user.DisplayName.ToLower().Contains(term)) ||
            user.FirstName.ToLower().Contains(term) ||
            user.LastName.ToLower().Contains(term));
    }

    private static IQueryable<User> ApplyStatusFilter(
        IQueryable<User> query,
        string status)
    {
        return status switch
        {
            "active" => query.Where(user => !user.IsDeleted && !user.IsSuspended),
            "suspended" => query.Where(user => !user.IsDeleted && user.IsSuspended),
            "deleted" => query.Where(user => user.IsDeleted),
            _ => query.Where(user => !user.IsDeleted)
        };
    }

    private IQueryable<User> ApplySort(
        IQueryable<User> query,
        string? sort)
    {
        return NormalizeSort(sort) switch
        {
            "oldest" => query.OrderBy(user => user.CreatedAt).ThenBy(user => user.Id),
            "lastActiveDesc" => query.OrderByDescending(user => user.LastLoginAt).ThenByDescending(user => user.Id),
            "nameAsc" => query
                .OrderBy(user => user.DisplayName ?? user.FirstName)
                .ThenBy(user => user.LastName)
                .ThenBy(user => user.Email)
                .ThenBy(user => user.Id),
            "reviewsDesc" => query
                .OrderByDescending(user =>
                    _db.PlaceReviews.Count(review => review.UserId == user.Id) +
                    _db.EventReviews.Count(review => review.UserId == user.Id) +
                    _db.DestinationReviews.Count(review => review.UserId == user.Id) +
                    _db.LocationReviews.Count(review => review.UserId == user.Id))
                .ThenBy(user => user.Email)
                .ThenBy(user => user.Id),
            "reportedDesc" => query
                .OrderByDescending(user =>
                    _db.ReviewReports.Count(report =>
                        report.ReviewAuthorUserId == user.Id &&
                        report.Status == ReviewReportStatus.Pending))
                .ThenBy(user => user.Email)
                .ThenBy(user => user.Id),
            _ => query.OrderByDescending(user => user.CreatedAt).ThenByDescending(user => user.Id)
        };
    }

    private static string NormalizeTab(string? tab)
    {
        return string.Equals(tab, "territory-managers", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(tab, "managers", StringComparison.OrdinalIgnoreCase)
            ? "territory-managers"
            : "app-users";
    }

    private static string NormalizeStatus(string? status)
    {
        return status?.Trim().ToLowerInvariant() switch
        {
            "active" => "active",
            "suspended" => "suspended",
            "deleted" or "inactive" => "deleted",
            _ => "all"
        };
    }

    private static string NormalizeSort(string? sort)
    {
        return sort?.Trim() switch
        {
            "oldest" => "oldest",
            "lastActiveDesc" => "lastActiveDesc",
            "nameAsc" => "nameAsc",
            "reviewsDesc" => "reviewsDesc",
            "reportedDesc" => "reportedDesc",
            _ => "newest"
        };
    }

    private static string ResolveStatus(bool isDeleted, bool isSuspended)
    {
        if (isDeleted)
            return "Deleted";

        return isSuspended ? "Suspended" : "Active";
    }

    private static List<string> NormalizeRoles(IEnumerable<string?> roles)
    {
        return roles
            .Select(RoleNames.Normalize)
            .Where(role => !string.IsNullOrWhiteSpace(role))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(role => role, StringComparer.Ordinal)
            .ToList();
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

        return string.IsNullOrWhiteSpace(username)
            ? email.Trim()
            : username.Trim();
    }

    private static string BuildInitials(string displayName, string email)
    {
        var parts = displayName
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(part => part.Length > 0)
            .Take(2)
            .ToList();

        if (parts.Count > 0)
            return string.Concat(parts.Select(part => char.ToUpperInvariant(part[0])));

        return string.IsNullOrWhiteSpace(email)
            ? "?"
            : char.ToUpperInvariant(email.Trim()[0]).ToString();
    }

    private sealed class UserRow
    {
        public int UserId { get; set; }
        public string? DisplayName { get; set; }
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public string Username { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string? AvatarUrl { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? LastLoginAt { get; set; }
        public bool IsSuspended { get; set; }
        public bool IsDeleted { get; set; }
    }

    private sealed class RoleRow
    {
        public int UserId { get; set; }
        public string RoleName { get; set; } = string.Empty;
    }

    private sealed class TerritoryRow
    {
        public int UserId { get; set; }
        public int TerritoryId { get; set; }
        public string TerritoryName { get; set; } = string.Empty;
        public string? CountryCode { get; set; }
    }

    private sealed class CountRow
    {
        public int UserId { get; set; }
        public int Count { get; set; }
    }

    private sealed class UserSecurityRow
    {
        public int UserId { get; set; }
        public bool IsSuspended { get; set; }
        public bool IsDeleted { get; set; }
        public List<string> Roles { get; set; } = new();
    }

    private sealed class UserPreviewRow
    {
        public int UserId { get; set; }
        public string? DisplayName { get; set; }
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public string Username { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
        public DateTime? LastLoginAt { get; set; }
        public bool IsSuspended { get; set; }
        public DateTime? SuspendedAt { get; set; }
        public int? SuspendedByUserId { get; set; }
        public bool IsDeleted { get; set; }
        public DateTime? DeletedAt { get; set; }
        public int? DeletedByUserId { get; set; }
    }

    private sealed class ReportStatusCountRow
    {
        public ReviewReportStatus Status { get; set; }
        public int Count { get; set; }
    }

    private sealed class ContributionStatusCountRow
    {
        public PlaceContributionStatus Status { get; set; }
        public int Count { get; set; }
    }

    private sealed class ReportPreviewRow
    {
        public int ReportId { get; set; }
        public ReviewReportTargetType TargetType { get; set; }
        public int ReviewId { get; set; }
        public int EntityId { get; set; }
        public string EntityName { get; set; } = string.Empty;
        public string RegionName { get; set; } = string.Empty;
        public ReviewReportReason Reason { get; set; }
        public ReviewReportStatus Status { get; set; }
        public ReviewReportDecision? Decision { get; set; }
        public string ContentExcerpt { get; set; } = string.Empty;
        public int ReporterUserId { get; set; }
        public string? ReporterDisplayName { get; set; }
        public string ReporterFirstName { get; set; } = string.Empty;
        public string ReporterLastName { get; set; } = string.Empty;
        public string ReporterUsername { get; set; } = string.Empty;
        public string ReporterEmail { get; set; } = string.Empty;
        public int ReviewAuthorUserId { get; set; }
        public string ReviewAuthorName { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public DateTime? ModerationDecisionDate { get; set; }
    }

    private sealed class RecentReviewRow
    {
        public int ReviewId { get; set; }
        public string TargetType { get; set; } = string.Empty;
        public int EntityId { get; set; }
        public string EntityName { get; set; } = string.Empty;
        public double Rating { get; set; }
        public string Comment { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
    }

    private sealed class ReportCountRow
    {
        public ReviewReportTargetType TargetType { get; set; }
        public int ReviewId { get; set; }
        public int Count { get; set; }
    }

    private sealed class ActorNameRow
    {
        public int UserId { get; set; }
        public string? DisplayName { get; set; }
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public string Username { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
    }

    private sealed class ModerationReportRow
    {
        public int ReportId { get; set; }
        public ReviewReportTargetType TargetType { get; set; }
        public string EntityName { get; set; } = string.Empty;
        public ReviewReportStatus Status { get; set; }
        public ReviewReportDecision? Decision { get; set; }
        public string DecisionNote { get; set; } = string.Empty;
        public DateTime ReviewedAt { get; set; }
        public int? ReviewedByUserId { get; set; }
        public string? ReviewerDisplayName { get; set; }
        public string? ReviewerFirstName { get; set; }
        public string? ReviewerLastName { get; set; }
        public string? ReviewerUsername { get; set; }
        public string? ReviewerEmail { get; set; }
        public int ReporterUserId { get; set; }
        public int ReviewAuthorUserId { get; set; }
    }

    private sealed class ModerationContributionRow
    {
        public int ContributionId { get; set; }
        public string Name { get; set; } = string.Empty;
        public PlaceContributionStatus Status { get; set; }
        public string? ReviewNote { get; set; }
        public DateTime ReviewedAt { get; set; }
        public int? ReviewedByUserId { get; set; }
        public string? ReviewerDisplayName { get; set; }
        public string? ReviewerFirstName { get; set; }
        public string? ReviewerLastName { get; set; }
        public string? ReviewerUsername { get; set; }
        public string? ReviewerEmail { get; set; }
    }
}
