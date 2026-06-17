using System.Text.Json;
using Geotrack.Application.DTOs.Admin;
using Geotrack.Application.Interfaces.Service;
using Geotrack.Application.Security;
using Geotrack.Domain.Entities.Core;
using Geotrack.Domain.Entities.Reviews;
using Geotrack.Domain.Entities.Users;
using Microsoft.EntityFrameworkCore;

namespace Geotrack.Infrastructure.Services;

public sealed class AdminNotificationService : IAdminNotificationService
{
    private const int DefaultTake = 25;
    private const int MaxTake = 50;
    private const int MessageSummaryLength = 120;

    private const string AdminAudienceRole = RoleNames.Admin;
    private const string ContentModeratorAudienceRole = RoleNames.ContentModerator;
    private const string TerritoryAudienceRole = RoleNames.TerritoryManager;
    private const string RegionalAudienceRole = RoleNames.RegionalManager;

    private const string GlobalScopeType = "Global";
    private const string TerritoryScopeType = "Territory";
    private const string RegionScopeType = "Region";

    private const string SupportTicketCreatedType = "SupportTicketCreated";
    private const string ReviewReportedType = "ReviewReported";
    private const string ContributionPendingReviewType = "ContributionPendingReview";
    private const string RegionCreatedType = "RegionCreated";
    private const string RegionalManagerAssignedType = "RegionalManagerAssigned";
    private const string RegionalManagerRemovedType = "RegionalManagerRemoved";

    private const string InfoSeverity = "Info";
    private const string WarningSeverity = "Warning";

    private const string SupportTicketTargetType = "SupportTicket";
    private const string ReviewReportNotificationTargetType = "ReviewReport";
    private const string ContributionTargetType = "Contribution";
    private const string RegionTargetType = "Region";
    private const string UserTargetType = "User";

    private static readonly HashSet<string> SupportedAudienceRoles = new(StringComparer.Ordinal)
    {
        RoleNames.Admin,
        RoleNames.ContentModerator,
        RoleNames.TerritoryManager,
        RoleNames.TerritoryAssociate,
        RoleNames.RegionalManager
    };

    private readonly AppDbContext _db;

    public AdminNotificationService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<AdminNotificationListDto> GetNotificationsAsync(
        int currentUserId,
        AdminNotificationListRequestDto request,
        CancellationToken cancellationToken = default)
    {
        var context = await ResolveAudienceContextAsync(
            currentUserId,
            request.AudienceRole,
            cancellationToken,
            request.RegionId);

        var skip = Math.Max(0, request.Skip);
        var take = Math.Clamp(request.Take <= 0 ? DefaultTake : request.Take, 1, MaxTake);
        var readCutoff = DateTime.UtcNow.AddMonths(-2);

        var query = BuildVisibleNotificationRows(currentUserId, context);
        query = request.IncludeRead
            ? query.Where(row => row.ReadAt == null || row.ReadAt >= readCutoff)
            : query.Where(row => row.ReadAt == null);

        var totalCount = await query.CountAsync(cancellationToken);
        var rows = await query
            .OrderByDescending(row => row.Notification.CreatedAt)
            .ThenByDescending(row => row.Notification.Id)
            .Skip(skip)
            .Take(take)
            .ToListAsync(cancellationToken);

        return new AdminNotificationListDto
        {
            Items = rows
                .Select(row => MapNotification(row.Notification, row.ReadAt))
                .ToList(),
            UnreadCount = await CountUnreadAsync(currentUserId, context, cancellationToken),
            TotalCount = totalCount,
            Skip = skip,
            Take = take,
            NextSkip = skip + take < totalCount ? skip + take : null,
            HasMore = skip + take < totalCount
        };
    }

    public async Task<AdminNotificationUnreadCountDto> GetUnreadCountAsync(
        int currentUserId,
        string? audienceRole = null,
        int? regionId = null,
        CancellationToken cancellationToken = default)
    {
        var context = await ResolveAudienceContextAsync(currentUserId, audienceRole, cancellationToken, regionId);

        return new AdminNotificationUnreadCountDto
        {
            UnreadCount = await CountUnreadAsync(currentUserId, context, cancellationToken)
        };
    }

    public async Task<AdminNotificationMutationDto> MarkReadAsync(
        int currentUserId,
        int notificationId,
        string? audienceRole = null,
        int? regionId = null,
        CancellationToken cancellationToken = default)
    {
        var context = await ResolveAudienceContextAsync(currentUserId, audienceRole, cancellationToken, regionId);

        if (notificationId <= 0)
            throw new InvalidOperationException("notificationId must be positive.");

        var exists = await BuildVisibleNotificationRows(currentUserId, context)
            .AnyAsync(row => row.Notification.Id == notificationId, cancellationToken);

        if (!exists)
            throw new KeyNotFoundException("Notification was not found.");

        var readState = await _db.AdminNotificationReadStates
            .SingleOrDefaultAsync(
                state =>
                    state.AdminNotificationId == notificationId &&
                    state.UserId == currentUserId,
                cancellationToken);

        var readAt = readState?.ReadAt ?? DateTime.UtcNow;
        if (readState is null)
        {
            _db.AdminNotificationReadStates.Add(new AdminNotificationReadState
            {
                AdminNotificationId = notificationId,
                UserId = currentUserId,
                ReadAt = readAt
            });
        }

        await _db.SaveChangesAsync(cancellationToken);

        return new AdminNotificationMutationDto
        {
            ReadAt = readAt,
            UnreadCount = await CountUnreadAsync(currentUserId, context, cancellationToken)
        };
    }

    public async Task<AdminNotificationMutationDto> MarkAllReadAsync(
        int currentUserId,
        string? audienceRole = null,
        int? regionId = null,
        CancellationToken cancellationToken = default)
    {
        var context = await ResolveAudienceContextAsync(currentUserId, audienceRole, cancellationToken, regionId);

        var unreadNotificationIds = await BuildVisibleNotificationRows(currentUserId, context)
            .Where(row => row.ReadAt == null)
            .Select(row => row.Notification.Id)
            .ToListAsync(cancellationToken);

        var readAt = DateTime.UtcNow;
        foreach (var notificationId in unreadNotificationIds)
        {
            _db.AdminNotificationReadStates.Add(new AdminNotificationReadState
            {
                AdminNotificationId = notificationId,
                UserId = currentUserId,
                ReadAt = readAt
            });
        }

        await _db.SaveChangesAsync(cancellationToken);

        return new AdminNotificationMutationDto
        {
            ReadAt = readAt,
            UnreadCount = 0
        };
    }

    public async Task CreateSupportTicketCreatedNotificationAsync(
        SupportRequest ticket,
        CancellationToken cancellationToken = default)
    {
        if (ticket.Id <= 0)
            return;

        var categoryLabel = CategoryLabel(ticket.Category);
        var subject = TrimForMessage(ticket.Subject, MessageSummaryLength);
        var message = string.IsNullOrWhiteSpace(subject)
            ? $"{categoryLabel} support report submitted."
            : $"{categoryLabel}: {subject}";

        await AddNotificationIfMissingAsync(
            audienceRole: AdminAudienceRole,
            type: SupportTicketCreatedType,
            title: "New support report",
            message: message,
            severity: ticket.Priority is SupportRequestPriorities.High or SupportRequestPriorities.Critical
                ? WarningSeverity
                : InfoSeverity,
            targetType: SupportTicketTargetType,
            targetId: ticket.Id,
            targetRoute: $"/admin-support-tickets?ticketId={ticket.Id}",
            metadataSummary: categoryLabel,
            createdAt: ticket.CreatedAt == default ? DateTime.UtcNow : ticket.CreatedAt,
            scopeType: GlobalScopeType,
            scopeId: null,
            scopeLabel: null,
            cancellationToken);
    }

    public async Task CreateReviewReportedNotificationAsync(
        ReviewReport report,
        CancellationToken cancellationToken = default)
    {
        if (report.Id <= 0)
            return;

        var reviewKey = $"{report.TargetType}:{report.ReviewId}";
        var reason = ReasonLabel(report.Reason);
        var entityName = TrimForMessage(report.EntityName, 80);
        var targetLabel = report.TargetType.ToString().ToLowerInvariant();
        var message = string.IsNullOrWhiteSpace(entityName)
            ? $"{TitleCase(targetLabel)} review reported for {reason}."
            : $"{TitleCase(targetLabel)} review reported for {reason} on {entityName}.";

        await AddNotificationIfMissingAsync(
            audienceRole: ContentModeratorAudienceRole,
            type: ReviewReportedType,
            title: "New reported review",
            message: message,
            severity: WarningSeverity,
            targetType: ReviewReportNotificationTargetType,
            targetId: report.Id,
            targetRoute: $"/content-moderator/review-management?status=reported&highlight={Uri.EscapeDataString(reviewKey)}",
            metadataSummary: reason,
            createdAt: report.CreatedAt == default ? DateTime.UtcNow : report.CreatedAt,
            scopeType: GlobalScopeType,
            scopeId: null,
            scopeLabel: null,
            cancellationToken);
    }

    public async Task CreateRegionalContributionPendingReviewNotificationAsync(
        PlaceContribution contribution,
        CancellationToken cancellationToken = default)
    {
        if (contribution.Id <= 0)
            return;

        var row = await _db.PlaceContributions
            .AsNoTracking()
            .Where(item =>
                item.Id == contribution.Id &&
                item.LocationId.HasValue &&
                item.Location != null &&
                (item.Type != PlaceContributionType.UpdatePlace ||
                 (item.PlaceId.HasValue &&
                  item.Place != null &&
                  item.Place.Location.Destination.RegionId == item.Location.Destination.RegionId)))
            .Select(item => new
            {
                item.Id,
                item.Name,
                item.Type,
                item.SubmittedAt,
                RegionId = item.Location!.Destination.RegionId,
                RegionName = item.Location.Destination.Region.Name,
                TerritoryName = item.Location.Destination.Region.Territory.Name
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (row is null)
            return;

        var hasActiveManager = await _db.RegionalManagerRegions
            .AsNoTracking()
            .AnyAsync(r => r.RegionId == row.RegionId && r.IsActive, cancellationToken);

        if (!hasActiveManager)
        {
            var cmTitle = "New request pending review";
            var cmMessage = $"{ContributionTypeLabel(row.Type)} request for {TrimForMessage(row.Name, 80)} — region {row.RegionName} has no assigned manager.";
            await AddNotificationIfMissingAsync(
                audienceRole: ContentModeratorAudienceRole,
                type: ContributionPendingReviewType,
                title: cmTitle,
                message: cmMessage,
                severity: InfoSeverity,
                targetType: ContributionTargetType,
                targetId: row.Id,
                targetRoute: $"/content-moderator/contributions/{row.Id}",
                metadataSummary: row.RegionName,
                createdAt: row.SubmittedAt == default ? DateTime.UtcNow : row.SubmittedAt,
                scopeType: GlobalScopeType,
                scopeId: null,
                scopeLabel: null,
                cancellationToken);
            return;
        }

        var title = "New request pending review";
        var message = $"{ContributionTypeLabel(row.Type)} request for {TrimForMessage(row.Name, 80)} was submitted in {row.RegionName}.";

        await AddNotificationIfMissingAsync(
            audienceRole: RegionalAudienceRole,
            type: ContributionPendingReviewType,
            title: title,
            message: message,
            severity: InfoSeverity,
            targetType: ContributionTargetType,
            targetId: row.Id,
            targetRoute: $"/request/detail/contribution/{row.Id}?regionId={row.RegionId}",
            metadataSummary: row.RegionName,
            createdAt: row.SubmittedAt == default ? DateTime.UtcNow : row.SubmittedAt,
            scopeType: RegionScopeType,
            scopeId: row.RegionId,
            scopeLabel: BuildScopeLabel(row.RegionName, row.TerritoryName),
            cancellationToken);
    }

    public async Task CreateContentManagerContributionPendingReviewNotificationAsync(
        PlaceContribution contribution,
        CancellationToken cancellationToken = default)
    {
        if (contribution.Id <= 0)
            return;

        var row = await _db.PlaceContributions
            .AsNoTracking()
            .Where(item => item.Id == contribution.Id && !item.LocationId.HasValue)
            .Select(item => new { item.Id, item.Name, item.Type, item.SubmittedAt })
            .SingleOrDefaultAsync(cancellationToken);

        if (row is null)
            return;

        var title = "New request pending review";
        var message = $"{ContributionTypeLabel(row.Type)} request for {TrimForMessage(row.Name, 80)} — location is outside any mapped region.";

        await AddNotificationIfMissingAsync(
            audienceRole: ContentModeratorAudienceRole,
            type: ContributionPendingReviewType,
            title: title,
            message: message,
            severity: InfoSeverity,
            targetType: ContributionTargetType,
            targetId: row.Id,
            targetRoute: $"/content-moderator/contributions/{row.Id}",
            metadataSummary: string.Empty,
            createdAt: row.SubmittedAt == default ? DateTime.UtcNow : row.SubmittedAt,
            scopeType: GlobalScopeType,
            scopeId: null,
            scopeLabel: null,
            cancellationToken);
    }

    public async Task CreateTerritoryRegionCreatedNotificationAsync(
        int territoryId,
        int regionId,
        string regionName,
        DateTime createdAt,
        CancellationToken cancellationToken = default)
    {
        if (territoryId <= 0 || regionId <= 0)
            return;

        var row = await _db.Regions
            .AsNoTracking()
            .Where(region => region.Id == regionId && region.TerritoryId == territoryId)
            .Select(region => new
            {
                RegionName = region.Name,
                TerritoryName = region.Territory.Name
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (row is null)
            return;

        var safeRegionName = string.IsNullOrWhiteSpace(regionName) ? row.RegionName : regionName.Trim();

        await AddNotificationIfMissingAsync(
            audienceRole: TerritoryAudienceRole,
            type: RegionCreatedType,
            title: "Region created",
            message: $"{TrimForMessage(safeRegionName, 80)} was added to {row.TerritoryName}.",
            severity: InfoSeverity,
            targetType: RegionTargetType,
            targetId: regionId,
            targetRoute: $"/territory/regions/{regionId}",
            metadataSummary: row.TerritoryName,
            createdAt: createdAt == default ? DateTime.UtcNow : createdAt,
            scopeType: RegionScopeType,
            scopeId: regionId,
            scopeLabel: BuildScopeLabel(safeRegionName, row.TerritoryName),
            cancellationToken);
    }

    public async Task CreateTerritoryRegionalManagerAssignedNotificationAsync(
        int territoryId,
        int regionId,
        int managerUserId,
        DateTime createdAt,
        CancellationToken cancellationToken = default)
    {
        await CreateTerritoryRegionalManagerChangedNotificationAsync(
            territoryId,
            regionId,
            managerUserId,
            RegionalManagerAssignedType,
            "Regional manager assigned",
            "assigned to",
            createdAt,
            cancellationToken);
    }

    public async Task CreateTerritoryRegionalManagerRemovedNotificationAsync(
        int territoryId,
        int regionId,
        int managerUserId,
        DateTime createdAt,
        CancellationToken cancellationToken = default)
    {
        await CreateTerritoryRegionalManagerChangedNotificationAsync(
            territoryId,
            regionId,
            managerUserId,
            RegionalManagerRemovedType,
            "Regional manager removed",
            "removed from",
            createdAt,
            cancellationToken);
    }

    private async Task CreateTerritoryRegionalManagerChangedNotificationAsync(
        int territoryId,
        int regionId,
        int managerUserId,
        string type,
        string title,
        string action,
        DateTime createdAt,
        CancellationToken cancellationToken)
    {
        if (territoryId <= 0 || regionId <= 0 || managerUserId <= 0)
            return;

        var region = await _db.Regions
            .AsNoTracking()
            .Where(region => region.Id == regionId && region.TerritoryId == territoryId)
            .Select(region => new
            {
                RegionName = region.Name,
                TerritoryName = region.Territory.Name
            })
            .SingleOrDefaultAsync(cancellationToken);

        var manager = await _db.Users
            .AsNoTracking()
            .Where(user => user.Id == managerUserId)
            .Select(user => new
            {
                user.DisplayName,
                user.FirstName,
                user.LastName,
                user.Username,
                user.Email
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (region is null || manager is null)
            return;

        var managerName = BuildDisplayName(
            manager.DisplayName,
            manager.FirstName,
            manager.LastName,
            manager.Username,
            manager.Email);

        await AddNotificationIfMissingAsync(
            audienceRole: TerritoryAudienceRole,
            type: type,
            title: title,
            message: $"{TrimForMessage(managerName, 80)} was {action} {region.RegionName}.",
            severity: InfoSeverity,
            targetType: UserTargetType,
            targetId: managerUserId,
            targetRoute: $"/territory/users?regionId={regionId}&userId={managerUserId}",
            metadataSummary: region.RegionName,
            createdAt: createdAt == default ? DateTime.UtcNow : createdAt,
            scopeType: RegionScopeType,
            scopeId: regionId,
            scopeLabel: BuildScopeLabel(region.RegionName, region.TerritoryName),
            cancellationToken);

        if (type == RegionalManagerAssignedType)
        {
            await AddNotificationIfMissingAsync(
                audienceRole: RegionalAudienceRole,
                type: RegionalManagerAssignedType,
                title: "Region assigned",
                message: $"You were assigned to {region.RegionName}.",
                severity: InfoSeverity,
                targetType: UserTargetType,
                targetId: managerUserId,
                targetRoute: $"/dashboard?regionId={regionId}",
                metadataSummary: region.RegionName,
                createdAt: createdAt == default ? DateTime.UtcNow : createdAt,
                scopeType: RegionScopeType,
                scopeId: regionId,
                scopeLabel: BuildScopeLabel(region.RegionName, region.TerritoryName),
                cancellationToken);
        }
    }

    private IQueryable<NotificationVisibilityRow> BuildVisibleNotificationRows(
        int currentUserId,
        NotificationAudienceContext context)
    {
        var query =
            from notification in _db.AdminNotifications.AsNoTracking()
            where notification.AudienceRole == context.StoredAudienceRole
            join state in _db.AdminNotificationReadStates.AsNoTracking()
                    .Where(readState => readState.UserId == currentUserId)
                on notification.Id equals state.AdminNotificationId into readStates
            from readState in readStates.DefaultIfEmpty()
            select new NotificationVisibilityRow
            {
                Notification = notification,
                ReadAt = readState == null ? null : (DateTime?)readState.ReadAt
            };

        if (context.RequestedRole == RoleNames.Admin)
            return query;

        if (context.RequestedRole == RoleNames.ContentModerator)
        {
            return query.Where(row =>
                row.Notification.ScopeType == GlobalScopeType &&
                (row.Notification.Type == ReviewReportedType ||
                 row.Notification.Type == ContributionPendingReviewType ||
                 row.Notification.TargetType == ReviewReportNotificationTargetType ||
                 row.Notification.TargetType == "Review" ||
                 row.Notification.TargetType == ContributionTargetType));
        }

        if (context.RequestedRole == RoleNames.RegionalManager)
        {
            if (!context.RegionId.HasValue)
                return query.Where(_ => false);

            var assignedRegionIds = _db.RegionalManagerRegions
                .AsNoTracking()
                .Where(assignment =>
                    assignment.UserId == currentUserId &&
                    assignment.IsActive)
                .Select(assignment => assignment.RegionId);

            return query.Where(row =>
                row.Notification.ScopeType == RegionScopeType &&
                row.Notification.ScopeId.HasValue &&
                row.Notification.ScopeId == context.RegionId.Value &&
                assignedRegionIds.Contains(row.Notification.ScopeId.Value) &&
                row.Notification.Type != ReviewReportedType &&
                row.Notification.TargetType != ReviewReportNotificationTargetType &&
                (row.Notification.Type != RegionalManagerAssignedType ||
                 (row.Notification.TargetType == UserTargetType &&
                  row.Notification.TargetId == currentUserId)));
        }

        var assignedTerritoryIds = _db.TerritoryManagerTerritories
            .AsNoTracking()
            .Where(assignment => assignment.UserId == currentUserId)
            .Select(assignment => assignment.TerritoryId);

        query = query.Where(row =>
            (row.Notification.ScopeType == TerritoryScopeType &&
             row.Notification.ScopeId.HasValue &&
             assignedTerritoryIds.Contains(row.Notification.ScopeId.Value)) ||
            (row.Notification.ScopeType == RegionScopeType &&
             row.Notification.ScopeId.HasValue &&
             _db.Regions.Any(region =>
                 region.Id == row.Notification.ScopeId.Value &&
                 assignedTerritoryIds.Contains(region.TerritoryId))));

        if (context.RequestedRole == RoleNames.TerritoryAssociate)
        {
            query = query.Where(row =>
                row.Notification.Type != RegionalManagerAssignedType &&
                row.Notification.Type != RegionalManagerRemovedType);
        }

        // Territory Managers/Associates oversee regions, not the individual contribution/ownership
        // request queue. Those requests are handled by Regional Managers (and the Content Moderator
        // fallback), so exclude reported reviews and contribution requests from the territory feed.
        query = query.Where(row =>
            row.Notification.Type != ReviewReportedType &&
            row.Notification.TargetType != ReviewReportNotificationTargetType &&
            row.Notification.Type != ContributionPendingReviewType &&
            row.Notification.TargetType != ContributionTargetType);

        return query;
    }

    private async Task<NotificationAudienceContext> ResolveAudienceContextAsync(
        int currentUserId,
        string? requestedAudienceRole,
        CancellationToken cancellationToken,
        int? regionId = null)
    {
        var userExists = await _db.Users
            .AsNoTracking()
            .AnyAsync(user => user.Id == currentUserId, cancellationToken);

        if (!userExists)
            throw new KeyNotFoundException("Admin app user was not found.");

        var roles = await _db.UserRoles
            .AsNoTracking()
            .Where(userRole => userRole.UserId == currentUserId)
            .Select(userRole => userRole.Role.Name)
            .ToListAsync(cancellationToken);

        var normalizedRoles = roles
            .Select(RoleNames.Normalize)
            .Where(role => !string.IsNullOrWhiteSpace(role))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var requestedRole = ResolveRequestedAudienceRole(requestedAudienceRole, normalizedRoles);
        if (!SupportedAudienceRoles.Contains(requestedRole))
            throw new UnauthorizedAccessException("Notification audience is not available.");

        if (!normalizedRoles.Contains(requestedRole, StringComparer.Ordinal))
            throw new UnauthorizedAccessException("Notification audience is not assigned to the current user.");

        var storedAudienceRole = requestedRole == RoleNames.TerritoryAssociate
            ? TerritoryAudienceRole
            : requestedRole;

        return new NotificationAudienceContext(requestedRole, storedAudienceRole, NormalizeRegionId(regionId));
    }

    private static string ResolveRequestedAudienceRole(
        string? requestedAudienceRole,
        IReadOnlyCollection<string> normalizedRoles)
    {
        var requestedRole = RoleNames.Normalize(requestedAudienceRole);
        if (!string.IsNullOrWhiteSpace(requestedRole))
            return requestedRole;

        if (normalizedRoles.Contains(RoleNames.Admin, StringComparer.Ordinal))
            return RoleNames.Admin;

        if (normalizedRoles.Contains(RoleNames.ContentModerator, StringComparer.Ordinal))
            return RoleNames.ContentModerator;

        if (normalizedRoles.Contains(RoleNames.TerritoryManager, StringComparer.Ordinal))
            return RoleNames.TerritoryManager;

        if (normalizedRoles.Contains(RoleNames.TerritoryAssociate, StringComparer.Ordinal))
            return RoleNames.TerritoryAssociate;

        if (normalizedRoles.Contains(RoleNames.RegionalManager, StringComparer.Ordinal))
            return RoleNames.RegionalManager;

        return string.Empty;
    }

    private async Task<int> CountUnreadAsync(
        int currentUserId,
        NotificationAudienceContext context,
        CancellationToken cancellationToken)
    {
        return await BuildVisibleNotificationRows(currentUserId, context)
            .CountAsync(row => row.ReadAt == null, cancellationToken);
    }

    private async Task AddNotificationIfMissingAsync(
        string audienceRole,
        string type,
        string title,
        string message,
        string severity,
        string targetType,
        int targetId,
        string targetRoute,
        string metadataSummary,
        DateTime createdAt,
        string scopeType,
        int? scopeId,
        string? scopeLabel,
        CancellationToken cancellationToken)
    {
        var normalizedAudienceRole = RoleNames.Normalize(audienceRole);
        var normalizedScopeType = NormalizeScopeType(scopeType);

        var exists = await _db.AdminNotifications.AnyAsync(
            notification =>
                notification.AudienceRole == normalizedAudienceRole &&
                notification.Type == type &&
                notification.TargetType == targetType &&
                notification.TargetId == targetId &&
                notification.ScopeType == normalizedScopeType &&
                notification.ScopeId == scopeId,
            cancellationToken);

        if (exists)
            return;

        _db.AdminNotifications.Add(new AdminNotification
        {
            AudienceRole = normalizedAudienceRole,
            Type = type,
            Title = TrimForMessage(title, 160),
            Message = TrimForMessage(message, 1000),
            Severity = NormalizeSeverity(severity),
            TargetType = targetType,
            TargetId = targetId,
            TargetRoute = NormalizeTargetRoute(targetRoute),
            ScopeType = normalizedScopeType,
            ScopeId = scopeId,
            ScopeLabel = NormalizeOptionalText(scopeLabel, 160),
            MetadataJson = JsonSerializer.Serialize(new { summary = metadataSummary }),
            CreatedAt = createdAt
        });

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            _db.ChangeTracker.Clear();
            var wasCreatedConcurrently = await _db.AdminNotifications.AsNoTracking().AnyAsync(
                notification =>
                    notification.AudienceRole == normalizedAudienceRole &&
                    notification.Type == type &&
                    notification.TargetType == targetType &&
                    notification.TargetId == targetId &&
                    notification.ScopeType == normalizedScopeType &&
                    notification.ScopeId == scopeId,
                cancellationToken);

            if (!wasCreatedConcurrently)
                throw;
        }
    }

    private static AdminNotificationDto MapNotification(AdminNotification notification, DateTime? readAt)
    {
        return new AdminNotificationDto
        {
            Id = notification.Id,
            Type = notification.Type,
            Title = notification.Title,
            Message = notification.Message,
            Severity = NormalizeSeverity(notification.Severity),
            IsRead = readAt.HasValue,
            ReadAt = readAt,
            CreatedAt = notification.CreatedAt,
            TargetType = notification.TargetType,
            TargetId = notification.TargetId,
            TargetRoute = notification.TargetRoute,
            ScopeType = NormalizeScopeType(notification.ScopeType),
            ScopeId = notification.ScopeId,
            ScopeLabel = notification.ScopeLabel,
            MetadataSummary = ExtractMetadataSummary(notification.MetadataJson)
        };
    }

    private static string? ExtractMetadataSummary(string? metadataJson)
    {
        if (string.IsNullOrWhiteSpace(metadataJson))
            return null;

        try
        {
            using var document = JsonDocument.Parse(metadataJson);
            return document.RootElement.TryGetProperty("summary", out var summary) &&
                summary.ValueKind == JsonValueKind.String
                ? summary.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string NormalizeTargetRoute(string route)
    {
        var trimmed = route.Trim();
        return string.IsNullOrWhiteSpace(trimmed) || !trimmed.StartsWith("/", StringComparison.Ordinal) || trimmed.StartsWith("//", StringComparison.Ordinal)
            ? "/admin-dashboard"
            : trimmed.Length > 500
                ? trimmed[..500]
                : trimmed;
    }

    private static string NormalizeSeverity(string? value)
    {
        return value?.Trim() switch
        {
            "Critical" => "Critical",
            "Warning" => "Warning",
            _ => "Info"
        };
    }

    private static string NormalizeScopeType(string? value)
    {
        return value?.Trim() switch
        {
            TerritoryScopeType => TerritoryScopeType,
            RegionScopeType => RegionScopeType,
            _ => GlobalScopeType
        };
    }

    private static int? NormalizeRegionId(int? value) =>
        value.HasValue && value.Value > 0 ? value.Value : null;

    private static string CategoryLabel(string? category)
    {
        return category?.Trim() switch
        {
            "account-access" => "Account access",
            "saved-places-and-collections" => "Saved places",
            "location-and-maps" => "Location and maps",
            "ask-agent" => "Ask Agent",
            "events-and-stays" => "Events and stays",
            "privacy-and-data" => "Privacy and data",
            _ => "Support request"
        };
    }

    private static string ReasonLabel(ReviewReportReason reason)
    {
        return reason switch
        {
            ReviewReportReason.OffensiveContent => "offensive content",
            ReviewReportReason.FalseInformation => "false information",
            ReviewReportReason.Harassment => "harassment",
            ReviewReportReason.Spam => "spam",
            _ => "moderation"
        };
    }

    private static string ContributionTypeLabel(PlaceContributionType type)
    {
        return type switch
        {
            PlaceContributionType.UpdatePlace => "Update",
            PlaceContributionType.ClaimOwnership => "Ownership claim",
            _ => "New place"
        };
    }

    private static string TitleCase(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "Content";

        return char.ToUpperInvariant(value[0]) + value[1..];
    }

    private static string TrimForMessage(string? value, int maxLength)
    {
        var trimmed = value?.Trim() ?? string.Empty;
        if (trimmed.Length <= maxLength)
            return trimmed;

        return trimmed[..maxLength].TrimEnd() + "...";
    }

    private static string? NormalizeOptionalText(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var trimmed = value.Trim();
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength].TrimEnd();
    }

    private static string BuildScopeLabel(string regionName, string? territoryName)
    {
        return string.IsNullOrWhiteSpace(territoryName)
            ? regionName
            : $"{regionName}, {territoryName}";
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

        return email;
    }

    private sealed record NotificationAudienceContext(string RequestedRole, string StoredAudienceRole, int? RegionId);

    private sealed class NotificationVisibilityRow
    {
        public AdminNotification Notification { get; set; } = null!;
        public DateTime? ReadAt { get; set; }
    }
}
