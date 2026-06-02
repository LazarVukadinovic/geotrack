using Geotrack.Application.DTOs.Admin;
using Geotrack.Application.Interfaces.Service;
using Geotrack.Application.Security;
using Geotrack.Domain.Entities.Reviews;
using Geotrack.Domain.Entities.Users;
using Microsoft.EntityFrameworkCore;

namespace Geotrack.Infrastructure.Services;

public sealed class AdminReviewManagementService : IAdminReviewManagementService
{
    private const int DefaultTake = 25;
    private const int MaxTake = 50;
    private const string DefaultModerationNote = "Content moderation action.";

    private readonly AppDbContext _db;
    private readonly IUserNotificationService _notificationService;
    private readonly IModerationAuditService _auditService;

    public AdminReviewManagementService(
        AppDbContext db,
        IUserNotificationService notificationService,
        IModerationAuditService auditService)
    {
        _db = db;
        _notificationService = notificationService;
        _auditService = auditService;
    }

    public async Task<AdminReviewListResponseDto> GetReviewsAsync(
        int currentUserId,
        AdminReviewListRequestDto request,
        CancellationToken cancellationToken = default)
    {
        var currentAdmin = await EnsureCurrentAdminAsync(currentUserId, cancellationToken);
        var skip = Math.Max(0, request.Skip);
        var take = Math.Clamp(request.Take <= 0 ? DefaultTake : request.Take, 1, MaxTake);

        var query = BuildLiveReviewRows();
        query = ApplyStatusFilter(query, NormalizeStatus(request.Status));
        query = ApplyTypeFilter(query, NormalizeTypes(request.Type));
        query = ApplySearch(query, request.Search);

        var totalCount = await query.CountAsync(cancellationToken);
        var rows = await ApplySort(query, NormalizeSort(request.Sort))
            .Skip(skip)
            .Take(take)
            .ToListAsync(cancellationToken);

        var items = await BuildListItemsAsync(
            currentAdmin,
            rows,
            cancellationToken);

        return new AdminReviewListResponseDto
        {
            Items = items,
            TotalCount = totalCount,
            Skip = skip,
            Take = take,
            NextSkip = skip + take < totalCount ? skip + take : null,
            HasMore = skip + take < totalCount,
            Counts = await BuildCountsAsync(cancellationToken)
        };
    }

    public async Task<AdminReviewDetailDto> GetReviewAsync(
        int currentUserId,
        string reviewKey,
        CancellationToken cancellationToken = default)
    {
        var currentAdmin = await EnsureCurrentAdminAsync(currentUserId, cancellationToken);
        var reference = ParseReviewKey(reviewKey);
        var row = await BuildLiveReviewRows()
            .Where(item => item.ReviewKind == reference.Kind && item.ReviewId == reference.ReviewId)
            .SingleOrDefaultAsync(cancellationToken);

        if (row is null)
        {
            return await BuildRemovedReviewDetailAsync(
                currentAdmin,
                reference,
                cancellationToken);
        }

        var reports = await LoadReportsForReviewAsync(reference, cancellationToken);
        var roles = await LoadAuthorRolesAsync(new[] { row.AuthorUserId }, cancellationToken);

        return BuildDetailDto(
            currentAdmin,
            row,
            reports,
            roles.GetValueOrDefault(row.AuthorUserId, new List<string>()));
    }

    public async Task<AdminReviewActionResultDto> DeleteReviewAsync(
        int currentUserId,
        string reviewKey,
        AdminReviewModerationRequestDto? request,
        CancellationToken cancellationToken = default)
    {
        var currentAdmin = await EnsureCurrentAdminAsync(currentUserId, cancellationToken);
        var reference = ParseReviewKey(reviewKey);
        var note = NormalizeModerationNote(request?.Note);

        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
        var target = await LoadReviewTargetAsync(reference, cancellationToken);
        await RemoveReviewEntityAsync(target, cancellationToken);
        await ResolvePendingReportsForRemovedReviewAsync(
            target,
            currentAdmin.UserId,
            note,
            DateTime.UtcNow,
            cancellationToken);
        await RecordReviewRemovalAuditAsync(
            currentAdmin.UserId,
            target,
            "DeleteReview",
            note,
            cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return await BuildActionResultAsync(target.ReviewKey, "Review removed.", cancellationToken);
    }

    public async Task<AdminReviewActionResultDto> AcceptReportAsync(
        int currentUserId,
        string reviewKey,
        int reportId,
        AdminReviewModerationRequestDto? request,
        CancellationToken cancellationToken = default)
    {
        if (reportId <= 0)
            throw new InvalidOperationException("reportId must be positive.");

        var reference = ParseReviewKey(reviewKey);
        await EnsureReportMatchesReviewAsync(reference, reportId, requirePending: true, cancellationToken);

        return await DeleteReviewAsync(currentUserId, reviewKey, request, cancellationToken);
    }

    public async Task<AdminReviewDetailDto> RejectReportAsync(
        int currentUserId,
        string reviewKey,
        int reportId,
        AdminReviewModerationRequestDto? request,
        CancellationToken cancellationToken = default)
    {
        var currentAdmin = await EnsureCurrentAdminAsync(currentUserId, cancellationToken);
        if (reportId <= 0)
            throw new InvalidOperationException("reportId must be positive.");

        var reference = ParseReviewKey(reviewKey);
        var report = await EnsureReportMatchesReviewAsync(
            reference,
            reportId,
            requirePending: true,
            cancellationToken);

        var previousStatus = report.Status.ToString();
        var now = DateTime.UtcNow;

        report.Status = ReviewReportStatus.Dismissed;
        report.Decision = ReviewReportDecision.KeepReview;
        report.ReviewedByUserId = currentAdmin.UserId;
        report.ReviewedAt = now;
        report.DecisionNote = NormalizeModerationNote(request?.Note);

        await _auditService.RecordAsync(
            currentAdmin.UserId,
            "ReviewReport",
            report.Id,
            "RejectReport",
            previousStatus,
            report.Status.ToString(),
            report.DecisionNote,
            sourceArea: "ReviewManagement",
            cancellationToken: cancellationToken);

        _notificationService.AddReviewReportReporterDecisionNotification(report);
        await _db.SaveChangesAsync(cancellationToken);

        return await GetReviewAsync(currentUserId, reviewKey, cancellationToken);
    }

    public async Task<AdminReviewActionResultDto> RemoveReviewAndSuspendAuthorAsync(
        int currentUserId,
        string reviewKey,
        AdminReviewModerationRequestDto? request,
        CancellationToken cancellationToken = default)
    {
        var currentAdmin = await EnsureCurrentAdminAsync(currentUserId, cancellationToken);
        var reference = ParseReviewKey(reviewKey);
        var note = NormalizeModerationNote(request?.Note);
        var now = DateTime.UtcNow;

        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
        var target = await LoadReviewTargetAsync(reference, cancellationToken);
        EnsureCanMutateAuthor(currentAdmin, target.Author, "suspend");

        if (target.Author.IsDeleted)
            throw new InvalidOperationException("Deleted users cannot be suspended.");

        await RemoveReviewEntityAsync(target, cancellationToken);
        await ResolvePendingReportsForRemovedReviewAsync(
            target,
            currentAdmin.UserId,
            note,
            now,
            cancellationToken);
        await RecordReviewRemovalAuditAsync(
            currentAdmin.UserId,
            target,
            "RemoveReviewAndSuspendAuthor",
            note,
            cancellationToken);

        if (!target.Author.IsSuspended)
        {
            var previousAuthorStatus = ResolveAccountStatus(target.Author.IsSuspended, target.Author.IsDeleted);
            target.Author.IsSuspended = true;
            target.Author.SuspendedAt = now;
            target.Author.SuspendedByUserId = currentAdmin.UserId;
            target.Author.UpdatedAt = now;
            await RevokeRefreshTokensAsync(target.Author.Id, now, cancellationToken);
            await _auditService.RecordAsync(
                currentAdmin.UserId,
                "User",
                target.Author.Id,
                "SuspendUser",
                previousAuthorStatus,
                ResolveAccountStatus(target.Author.IsSuspended, target.Author.IsDeleted),
                note,
                sourceArea: "ReviewManagement",
                cancellationToken: cancellationToken);
        }

        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return await BuildActionResultAsync(target.ReviewKey, "Review removed and author suspended.", cancellationToken);
    }

    public async Task<AdminReviewActionResultDto> RemoveReviewAndDeleteAuthorAsync(
        int currentUserId,
        string reviewKey,
        AdminReviewModerationRequestDto? request,
        CancellationToken cancellationToken = default)
    {
        var currentAdmin = await EnsureCurrentAdminAsync(currentUserId, cancellationToken);
        var reference = ParseReviewKey(reviewKey);
        var note = NormalizeModerationNote(request?.Note);
        var now = DateTime.UtcNow;

        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
        var target = await LoadReviewTargetAsync(reference, cancellationToken);
        EnsureCanMutateAuthor(currentAdmin, target.Author, "delete");

        await RemoveReviewEntityAsync(target, cancellationToken);
        await ResolvePendingReportsForRemovedReviewAsync(
            target,
            currentAdmin.UserId,
            note,
            now,
            cancellationToken);
        await RecordReviewRemovalAuditAsync(
            currentAdmin.UserId,
            target,
            "RemoveReviewAndDeleteAuthor",
            note,
            cancellationToken);

        if (!target.Author.IsDeleted)
        {
            var previousAuthorStatus = ResolveAccountStatus(target.Author.IsSuspended, target.Author.IsDeleted);
            target.Author.IsDeleted = true;
            target.Author.DeletedAt = now;
            target.Author.DeletedByUserId = currentAdmin.UserId;
            target.Author.IsSuspended = false;
            target.Author.SuspendedAt = null;
            target.Author.SuspendedByUserId = null;
            target.Author.UpdatedAt = now;
            await RevokeRefreshTokensAsync(target.Author.Id, now, cancellationToken);
            await _auditService.RecordAsync(
                currentAdmin.UserId,
                "User",
                target.Author.Id,
                "DeleteUser",
                previousAuthorStatus,
                ResolveAccountStatus(target.Author.IsSuspended, target.Author.IsDeleted),
                note,
                sourceArea: "ReviewManagement",
                cancellationToken: cancellationToken);
        }

        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return await BuildActionResultAsync(target.ReviewKey, "Review removed and author deactivated.", cancellationToken);
    }

    private IQueryable<ReviewListRow> BuildLiveReviewRows()
    {
        var placeReviews = _db.PlaceReviews
            .AsNoTracking()
            .Select(review => new ReviewListRow
            {
                ReviewKey = "Place:" + review.Id,
                ReviewKind = AdminReviewKind.Place,
                ReviewId = review.Id,
                AuthorUserId = review.UserId,
                AuthorDisplayName = review.User.DisplayName,
                AuthorFirstName = review.User.FirstName,
                AuthorLastName = review.User.LastName,
                AuthorUsername = review.User.Username,
                AuthorEmail = review.User.Email,
                AuthorAvatarUrl = review.User.AvatarUrl,
                AuthorIsSuspended = review.User.IsSuspended,
                AuthorIsDeleted = review.User.IsDeleted,
                Rating = review.Rating,
                Content = review.Comment,
                CreatedAt = review.CreatedAt,
                UpdatedAt = review.UpdatedAt,
                EntityId = review.PlaceId,
                EntityType = review.Place.Stay == null ? "Place" : "Stay",
                EntityTypeNormalized = review.Place.Stay == null ? "place" : "stay",
                EntityName = review.Place.Name,
                LocationName = review.Place.Location.Name,
                DestinationName = review.Place.Location.Destination.Name,
                RegionName = review.Place.Location.Destination.Region.Name,
                ReportTargetType = ReviewReportTargetType.Place,
                ReportCount = _db.ReviewReports.Count(report =>
                    report.TargetType == ReviewReportTargetType.Place &&
                    report.ReviewId == review.Id),
                PendingReportCount = _db.ReviewReports.Count(report =>
                    report.TargetType == ReviewReportTargetType.Place &&
                    report.ReviewId == review.Id &&
                    report.Status == ReviewReportStatus.Pending)
            });

        var eventReviews = _db.EventReviews
            .AsNoTracking()
            .Select(review => new ReviewListRow
            {
                ReviewKey = "Event:" + review.Id,
                ReviewKind = AdminReviewKind.Event,
                ReviewId = review.Id,
                AuthorUserId = review.UserId,
                AuthorDisplayName = review.User.DisplayName,
                AuthorFirstName = review.User.FirstName,
                AuthorLastName = review.User.LastName,
                AuthorUsername = review.User.Username,
                AuthorEmail = review.User.Email,
                AuthorAvatarUrl = review.User.AvatarUrl,
                AuthorIsSuspended = review.User.IsSuspended,
                AuthorIsDeleted = review.User.IsDeleted,
                Rating = review.Rating,
                Content = review.Comment,
                CreatedAt = review.CreatedAt,
                UpdatedAt = review.UpdatedAt,
                EntityId = review.EventId,
                EntityType = "Event",
                EntityTypeNormalized = "event",
                EntityName = review.Event.Name,
                LocationName = review.Event.Location == null ? review.Event.VenueName : review.Event.Location.Name,
                DestinationName = review.Event.Destination.Name,
                RegionName = review.Event.Destination.Region.Name,
                ReportTargetType = ReviewReportTargetType.Event,
                ReportCount = _db.ReviewReports.Count(report =>
                    report.TargetType == ReviewReportTargetType.Event &&
                    report.ReviewId == review.Id),
                PendingReportCount = _db.ReviewReports.Count(report =>
                    report.TargetType == ReviewReportTargetType.Event &&
                    report.ReviewId == review.Id &&
                    report.Status == ReviewReportStatus.Pending)
            });

        var destinationReviews = _db.DestinationReviews
            .AsNoTracking()
            .Select(review => new ReviewListRow
            {
                ReviewKey = "Destination:" + review.Id,
                ReviewKind = AdminReviewKind.Destination,
                ReviewId = review.Id,
                AuthorUserId = review.UserId,
                AuthorDisplayName = review.User.DisplayName,
                AuthorFirstName = review.User.FirstName,
                AuthorLastName = review.User.LastName,
                AuthorUsername = review.User.Username,
                AuthorEmail = review.User.Email,
                AuthorAvatarUrl = review.User.AvatarUrl,
                AuthorIsSuspended = review.User.IsSuspended,
                AuthorIsDeleted = review.User.IsDeleted,
                Rating = review.Rating,
                Content = review.Comment,
                CreatedAt = review.CreatedAt,
                UpdatedAt = review.UpdatedAt,
                EntityId = review.DestinationId,
                EntityType = "Destination",
                EntityTypeNormalized = "destination",
                EntityName = review.Destination.Name,
                LocationName = null,
                DestinationName = review.Destination.Name,
                RegionName = review.Destination.Region.Name,
                ReportTargetType = null,
                ReportCount = 0,
                PendingReportCount = 0
            });

        var locationReviews = _db.LocationReviews
            .AsNoTracking()
            .Select(review => new ReviewListRow
            {
                ReviewKey = "Location:" + review.Id,
                ReviewKind = AdminReviewKind.Location,
                ReviewId = review.Id,
                AuthorUserId = review.UserId,
                AuthorDisplayName = review.User.DisplayName,
                AuthorFirstName = review.User.FirstName,
                AuthorLastName = review.User.LastName,
                AuthorUsername = review.User.Username,
                AuthorEmail = review.User.Email,
                AuthorAvatarUrl = review.User.AvatarUrl,
                AuthorIsSuspended = review.User.IsSuspended,
                AuthorIsDeleted = review.User.IsDeleted,
                Rating = review.Rating,
                Content = review.Comment,
                CreatedAt = review.CreatedAt,
                UpdatedAt = review.UpdatedAt,
                EntityId = review.LocationId,
                EntityType = "Location",
                EntityTypeNormalized = "location",
                EntityName = review.Location.Name,
                LocationName = review.Location.Name,
                DestinationName = review.Location.Destination.Name,
                RegionName = review.Location.Destination.Region.Name,
                ReportTargetType = null,
                ReportCount = 0,
                PendingReportCount = 0
            });

        return placeReviews
            .Concat(eventReviews)
            .Concat(destinationReviews)
            .Concat(locationReviews);
    }

    private async Task<List<AdminReviewListItemDto>> BuildListItemsAsync(
        UserSecurityRow currentAdmin,
        IReadOnlyCollection<ReviewListRow> rows,
        CancellationToken cancellationToken)
    {
        if (rows.Count == 0)
            return new List<AdminReviewListItemDto>();

        var reports = await LoadReportsForRowsAsync(rows, cancellationToken);
        var authorRoles = await LoadAuthorRolesAsync(
            rows.Select(row => row.AuthorUserId).Distinct().ToList(),
            cancellationToken);

        return rows
            .Select(row =>
            {
                reports.TryGetValue(row.ReportLookupKey, out var rowReports);
                var latest = rowReports?.OrderByDescending(report => report.CreatedAt).FirstOrDefault();
                var permissions = BuildPermissions(
                    currentAdmin,
                    row.AuthorUserId,
                    row.AuthorIsSuspended,
                    row.AuthorIsDeleted,
                    authorRoles.GetValueOrDefault(row.AuthorUserId, new List<string>()),
                    canDeleteReview: true);

                return new AdminReviewListItemDto
                {
                    ReviewKey = row.ReviewKey,
                    ReviewId = row.ReviewId,
                    Author = BuildAuthorDto(row),
                    Rating = row.Rating,
                    MaxRating = 5,
                    Content = row.Content,
                    CreatedAt = row.CreatedAt,
                    UpdatedAt = row.UpdatedAt,
                    Status = row.PendingReportCount > 0 ? "Reported" : "Active",
                    ReviewedEntity = BuildEntityDto(row),
                    Reports = new AdminReviewReportSummaryDto
                    {
                        ReportCount = row.ReportCount,
                        PendingReportCount = row.PendingReportCount,
                        LatestReportReason = latest is null ? null : FormatEnum(latest.Reason),
                        LatestReportCreatedAt = latest?.CreatedAt,
                        LatestReportStatus = latest?.Status.ToString()
                    },
                    Permissions = permissions
                };
            })
            .ToList();
    }

    private AdminReviewDetailDto BuildDetailDto(
        UserSecurityRow currentAdmin,
        ReviewListRow row,
        IReadOnlyCollection<ReportRow> reports,
        IReadOnlyCollection<string> authorRoles)
    {
        var permissions = BuildPermissions(
            currentAdmin,
            row.AuthorUserId,
            row.AuthorIsSuspended,
            row.AuthorIsDeleted,
            authorRoles,
            canDeleteReview: true);

        return new AdminReviewDetailDto
        {
            ReviewKey = row.ReviewKey,
            ReviewId = row.ReviewId,
            Author = BuildAuthorDto(row),
            Rating = row.Rating,
            MaxRating = 5,
            Content = row.Content,
            CreatedAt = row.CreatedAt,
            UpdatedAt = row.UpdatedAt,
            Status = reports.Any(report => report.Status == ReviewReportStatus.Pending) ? "Reported" : "Active",
            ReviewedEntity = BuildEntityDto(row),
            AllReports = reports
                .OrderByDescending(report => report.CreatedAt)
                .Select(MapReportDto)
                .ToList(),
            ReportSummary = BuildReportSummary(reports),
            ModerationHistory = reports
                .Where(report => report.ReviewedAt.HasValue)
                .OrderByDescending(report => report.ReviewedAt)
                .Select(MapHistoryDto)
                .ToList(),
            Permissions = permissions
        };
    }

    private async Task<AdminReviewDetailDto> BuildRemovedReviewDetailAsync(
        UserSecurityRow currentAdmin,
        ReviewReference reference,
        CancellationToken cancellationToken)
    {
        if (reference.ReportTargetType is null)
            throw new KeyNotFoundException("Review not found.");

        var reports = await LoadReportsForReviewAsync(reference, cancellationToken);
        if (reports.Count == 0)
            throw new KeyNotFoundException("Review not found.");

        var latestSnapshot = reports
            .OrderByDescending(report => report.CreatedAt)
            .First();
        var author = await _db.Users
            .AsNoTracking()
            .Where(user => user.Id == latestSnapshot.ReviewAuthorUserId)
            .Select(user => new
            {
                user.Id,
                user.DisplayName,
                user.FirstName,
                user.LastName,
                user.Username,
                user.Email,
                user.AvatarUrl,
                user.IsSuspended,
                user.IsDeleted,
                Roles = user.UserRoles.Select(userRole => userRole.Role.Name).ToList()
            })
            .SingleOrDefaultAsync(cancellationToken);

        var displayName = author is null
            ? latestSnapshot.ReviewAuthorName
            : BuildDisplayName(author.DisplayName, author.FirstName, author.LastName, author.Username, author.Email);
        var authorStatus = author is null
            ? "Unknown"
            : ResolveAccountStatus(author.IsSuspended, author.IsDeleted);
        var permissions = author is null
            ? new AdminReviewPermissionsDto()
            : BuildPermissions(
                currentAdmin,
                author.Id,
                author.IsSuspended,
                author.IsDeleted,
                NormalizeRoles(author.Roles),
                canDeleteReview: false);

        return new AdminReviewDetailDto
        {
            ReviewKey = ReviewKeyFor(reference.Kind, reference.ReviewId),
            ReviewId = reference.ReviewId,
            Author = new AdminReviewAuthorDto
            {
                UserId = author?.Id ?? latestSnapshot.ReviewAuthorUserId,
                DisplayName = displayName,
                Email = author?.Email ?? string.Empty,
                AvatarUrl = author?.AvatarUrl,
                Initials = BuildInitials(displayName, author?.Email ?? string.Empty),
                Status = authorStatus
            },
            Rating = latestSnapshot.ReviewRating,
            MaxRating = 5,
            Content = latestSnapshot.ReviewCommentSnapshot,
            CreatedAt = latestSnapshot.ReviewCreatedAt,
            UpdatedAt = null,
            Status = "Removed",
            ReviewedEntity = new AdminReviewedEntityDto
            {
                EntityId = latestSnapshot.EntityId,
                EntityType = reference.Kind.ToString(),
                Name = latestSnapshot.EntityName,
                Region = latestSnapshot.RegionName
            },
            AllReports = reports
                .OrderByDescending(report => report.CreatedAt)
                .Select(MapReportDto)
                .ToList(),
            ReportSummary = BuildReportSummary(reports),
            ModerationHistory = reports
                .Where(report => report.ReviewedAt.HasValue)
                .OrderByDescending(report => report.ReviewedAt)
                .Select(MapHistoryDto)
                .ToList(),
            Permissions = permissions
        };
    }

    private async Task<Dictionary<string, List<ReportRow>>> LoadReportsForRowsAsync(
        IReadOnlyCollection<ReviewListRow> rows,
        CancellationToken cancellationToken)
    {
        var placeReviewIds = rows
            .Where(row => row.ReportTargetType == ReviewReportTargetType.Place)
            .Select(row => row.ReviewId)
            .Distinct()
            .ToList();
        var eventReviewIds = rows
            .Where(row => row.ReportTargetType == ReviewReportTargetType.Event)
            .Select(row => row.ReviewId)
            .Distinct()
            .ToList();

        var reports = new List<ReportRow>();

        if (placeReviewIds.Count > 0)
        {
            reports.AddRange(await BuildReportRowsQuery()
                .Where(report =>
                    report.TargetType == ReviewReportTargetType.Place &&
                    placeReviewIds.Contains(report.ReviewId))
                .ToListAsync(cancellationToken));
        }

        if (eventReviewIds.Count > 0)
        {
            reports.AddRange(await BuildReportRowsQuery()
                .Where(report =>
                    report.TargetType == ReviewReportTargetType.Event &&
                    eventReviewIds.Contains(report.ReviewId))
                .ToListAsync(cancellationToken));
        }

        return reports
            .GroupBy(report => ReportLookupKey(report.TargetType, report.ReviewId))
            .ToDictionary(group => group.Key, group => group.ToList());
    }

    private async Task<List<ReportRow>> LoadReportsForReviewAsync(
        ReviewReference reference,
        CancellationToken cancellationToken)
    {
        if (reference.ReportTargetType is null)
            return new List<ReportRow>();

        return await BuildReportRowsQuery()
            .Where(report =>
                report.TargetType == reference.ReportTargetType.Value &&
                report.ReviewId == reference.ReviewId)
            .OrderByDescending(report => report.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    private IQueryable<ReportRow> BuildReportRowsQuery() =>
        _db.ReviewReports
            .AsNoTracking()
            .Select(report => new ReportRow
            {
                ReportId = report.Id,
                TargetType = report.TargetType,
                ReviewId = report.ReviewId,
                ReporterUserId = report.ReporterUserId,
                ReporterDisplayName = report.ReporterUser.DisplayName,
                ReporterFirstName = report.ReporterUser.FirstName,
                ReporterLastName = report.ReporterUser.LastName,
                ReporterUsername = report.ReporterUser.Username,
                ReporterEmail = report.ReporterUser.Email,
                Reason = report.Reason,
                Note = report.Note,
                Status = report.Status,
                CreatedAt = report.CreatedAt,
                ReviewedAt = report.ReviewedAt,
                ReviewedByUserId = report.ReviewedByUserId,
                ReviewerDisplayName = report.ReviewedByUser == null ? null : report.ReviewedByUser.DisplayName,
                ReviewerFirstName = report.ReviewedByUser == null ? null : report.ReviewedByUser.FirstName,
                ReviewerLastName = report.ReviewedByUser == null ? null : report.ReviewedByUser.LastName,
                ReviewerUsername = report.ReviewedByUser == null ? null : report.ReviewedByUser.Username,
                ReviewerEmail = report.ReviewedByUser == null ? null : report.ReviewedByUser.Email,
                Decision = report.Decision,
                DecisionNote = report.DecisionNote,
                ReviewAuthorUserId = report.ReviewAuthorUserId,
                ReviewAuthorName = report.ReviewAuthorName,
                EntityId = report.EntityId,
                EntityName = report.EntityName,
                RegionId = report.RegionId,
                RegionName = report.RegionName,
                ReviewRating = report.ReviewRating,
                ReviewCommentSnapshot = report.ReviewCommentSnapshot,
                ReviewCreatedAt = report.ReviewCreatedAt
            });

    private async Task<Dictionary<int, List<string>>> LoadAuthorRolesAsync(
        IReadOnlyCollection<int> authorUserIds,
        CancellationToken cancellationToken)
    {
        if (authorUserIds.Count == 0)
            return new Dictionary<int, List<string>>();

        var rows = await _db.UserRoles
            .AsNoTracking()
            .Where(userRole => authorUserIds.Contains(userRole.UserId))
            .Select(userRole => new
            {
                userRole.UserId,
                userRole.Role.Name
            })
            .ToListAsync(cancellationToken);

        return rows
            .GroupBy(row => row.UserId)
            .ToDictionary(
                group => group.Key,
                group => NormalizeRoles(group.Select(row => row.Name)));
    }

    private async Task<AdminReviewListCountsDto> BuildCountsAsync(CancellationToken cancellationToken)
    {
        var liveRows = BuildLiveReviewRows();
        return new AdminReviewListCountsDto
        {
            ReportedCount = await _db.ReviewReports
                .AsNoTracking()
                .CountAsync(report => report.Status == ReviewReportStatus.Pending, cancellationToken),
            ActiveCount = await liveRows
                .CountAsync(row => row.PendingReportCount == 0, cancellationToken),
            AllCount = await liveRows.CountAsync(cancellationToken)
        };
    }

    private async Task<AdminReviewActionResultDto> BuildActionResultAsync(
        string reviewKey,
        string message,
        CancellationToken cancellationToken) =>
        new()
        {
            ReviewKey = reviewKey,
            Success = true,
            Message = message,
            Counts = await BuildCountsAsync(cancellationToken)
        };

    private async Task<ReviewMutationTarget> LoadReviewTargetAsync(
        ReviewReference reference,
        CancellationToken cancellationToken)
    {
        switch (reference.Kind)
        {
            case AdminReviewKind.Place:
            {
                var review = await _db.PlaceReviews
                    .Include(item => item.User)
                        .ThenInclude(user => user.UserRoles)
                            .ThenInclude(userRole => userRole.Role)
                    .Include(item => item.Place)
                        .ThenInclude(place => place.Stay)
                    .SingleOrDefaultAsync(item => item.Id == reference.ReviewId, cancellationToken)
                    ?? throw new KeyNotFoundException("Review not found.");

                return ReviewMutationTarget.ForPlace(review);
            }
            case AdminReviewKind.Event:
            {
                var review = await _db.EventReviews
                    .Include(item => item.User)
                        .ThenInclude(user => user.UserRoles)
                            .ThenInclude(userRole => userRole.Role)
                    .SingleOrDefaultAsync(item => item.Id == reference.ReviewId, cancellationToken)
                    ?? throw new KeyNotFoundException("Review not found.");

                return ReviewMutationTarget.ForEvent(review);
            }
            case AdminReviewKind.Destination:
            {
                var review = await _db.DestinationReviews
                    .Include(item => item.User)
                        .ThenInclude(user => user.UserRoles)
                            .ThenInclude(userRole => userRole.Role)
                    .SingleOrDefaultAsync(item => item.Id == reference.ReviewId, cancellationToken)
                    ?? throw new KeyNotFoundException("Review not found.");

                return ReviewMutationTarget.ForDestination(review);
            }
            case AdminReviewKind.Location:
            {
                var review = await _db.LocationReviews
                    .Include(item => item.User)
                        .ThenInclude(user => user.UserRoles)
                            .ThenInclude(userRole => userRole.Role)
                    .SingleOrDefaultAsync(item => item.Id == reference.ReviewId, cancellationToken)
                    ?? throw new KeyNotFoundException("Review not found.");

                return ReviewMutationTarget.ForLocation(review);
            }
            default:
                throw new InvalidOperationException("Invalid review type.");
        }
    }

    private async Task RemoveReviewEntityAsync(
        ReviewMutationTarget target,
        CancellationToken cancellationToken)
    {
        switch (target.Kind)
        {
            case AdminReviewKind.Place:
            {
                var review = target.PlaceReview!;
                var place = await _db.Places.FindAsync(new object[] { review.PlaceId }, cancellationToken);
                var stats = await _db.PlaceReviews
                    .Where(item => item.PlaceId == review.PlaceId && item.Id != review.Id)
                    .GroupBy(item => item.PlaceId)
                    .Select(group => new
                    {
                        Average = group.Average(item => item.Rating),
                        Count = group.Count()
                    })
                    .FirstOrDefaultAsync(cancellationToken);

                _db.PlaceReviews.Remove(review);

                if (place is not null)
                {
                    place.AverageRating = stats is null ? null : Math.Round(stats.Average, 1);
                    place.ReviewCount = stats?.Count ?? 0;
                }

                break;
            }
            case AdminReviewKind.Event:
                _db.EventReviews.Remove(target.EventReview!);
                break;
            case AdminReviewKind.Destination:
                _db.DestinationReviews.Remove(target.DestinationReview!);
                break;
            case AdminReviewKind.Location:
                _db.LocationReviews.Remove(target.LocationReview!);
                break;
        }
    }

    private async Task ResolvePendingReportsForRemovedReviewAsync(
        ReviewMutationTarget target,
        int moderatorUserId,
        string note,
        DateTime now,
        CancellationToken cancellationToken)
    {
        if (target.ReportTargetType is null)
        {
            _notificationService.AddReviewRemovedByModeratorNotification(
                target.Author.Id,
                target.Kind.ToString(),
                target.ReviewId,
                now);
            return;
        }

        var pendingReports = await _db.ReviewReports
            .Where(report =>
                report.TargetType == target.ReportTargetType.Value &&
                report.ReviewId == target.ReviewId &&
                report.Status == ReviewReportStatus.Pending)
            .OrderBy(report => report.CreatedAt)
            .ToListAsync(cancellationToken);

        foreach (var pendingReport in pendingReports)
        {
            var previousStatus = pendingReport.Status.ToString();
            pendingReport.Status = ReviewReportStatus.Actioned;
            pendingReport.Decision = ReviewReportDecision.RemoveReview;
            pendingReport.ReviewedByUserId = moderatorUserId;
            pendingReport.ReviewedAt = now;
            pendingReport.DecisionNote = note;
            await _auditService.RecordAsync(
                moderatorUserId,
                "ReviewReport",
                pendingReport.Id,
                "AcceptReport",
                previousStatus,
                pendingReport.Status.ToString(),
                note,
                sourceArea: "ReviewManagement",
                cancellationToken: cancellationToken);
            _notificationService.AddReviewReportReporterDecisionNotification(pendingReport);
        }

        if (pendingReports.Count > 0)
        {
            _notificationService.AddReportedReviewRemovedNotification(pendingReports[0]);
            return;
        }

        _notificationService.AddReviewRemovedByModeratorNotification(
            target.Author.Id,
            target.Kind.ToString(),
            target.ReviewId,
            now);
    }

    private Task RecordReviewRemovalAuditAsync(
        int moderatorUserId,
        ReviewMutationTarget target,
        string action,
        string note,
        CancellationToken cancellationToken)
    {
        return _auditService.RecordAsync(
            moderatorUserId,
            target.Kind + "Review",
            target.ReviewId,
            action,
            "Active",
            "Removed",
            note,
            sourceArea: "ReviewManagement",
            cancellationToken: cancellationToken);
    }

    private async Task<ReviewReport> EnsureReportMatchesReviewAsync(
        ReviewReference reference,
        int reportId,
        bool requirePending,
        CancellationToken cancellationToken)
    {
        if (reference.ReportTargetType is null)
            throw new InvalidOperationException("This review type does not support reports.");

        var report = await _db.ReviewReports
            .SingleOrDefaultAsync(item => item.Id == reportId, cancellationToken)
            ?? throw new KeyNotFoundException("Review report not found.");

        if (report.TargetType != reference.ReportTargetType.Value || report.ReviewId != reference.ReviewId)
            throw new InvalidOperationException("Report does not belong to this review.");

        if (requirePending && report.Status != ReviewReportStatus.Pending)
            throw new InvalidOperationException("This report has already been reviewed.");

        return report;
    }

    private static IQueryable<ReviewListRow> ApplyStatusFilter(
        IQueryable<ReviewListRow> query,
        string status)
    {
        return status switch
        {
            "reported" => query.Where(row => row.PendingReportCount > 0),
            "active" => query.Where(row => row.PendingReportCount == 0),
            _ => query
        };
    }

    private static IQueryable<ReviewListRow> ApplyTypeFilter(
        IQueryable<ReviewListRow> query,
        IReadOnlyCollection<string>? types)
    {
        if (types is null || types.Count == 0)
            return query;

        return query.Where(row => types.Contains(row.EntityTypeNormalized));
    }

    private static IQueryable<ReviewListRow> ApplySearch(
        IQueryable<ReviewListRow> query,
        string? search)
    {
        if (string.IsNullOrWhiteSpace(search))
            return query;

        var term = search.Trim().ToLowerInvariant();
        return query.Where(row =>
            row.AuthorEmail.ToLower().Contains(term) ||
            (row.AuthorDisplayName != null && row.AuthorDisplayName.ToLower().Contains(term)) ||
            row.AuthorFirstName.ToLower().Contains(term) ||
            row.AuthorLastName.ToLower().Contains(term) ||
            row.AuthorUsername.ToLower().Contains(term) ||
            row.Content.ToLower().Contains(term) ||
            row.EntityName.ToLower().Contains(term));
    }

    private static IQueryable<ReviewListRow> ApplySort(
        IQueryable<ReviewListRow> query,
        string sort)
    {
        return sort switch
        {
            "oldest" => query
                .OrderBy(row => row.CreatedAt)
                .ThenBy(row => row.ReviewKind)
                .ThenBy(row => row.ReviewId),
            "reportsDesc" => query
                .OrderByDescending(row => row.ReportCount)
                .ThenByDescending(row => row.PendingReportCount)
                .ThenByDescending(row => row.CreatedAt)
                .ThenBy(row => row.ReviewKind)
                .ThenByDescending(row => row.ReviewId),
            "ratingAsc" => query
                .OrderBy(row => row.Rating)
                .ThenBy(row => row.CreatedAt)
                .ThenBy(row => row.ReviewKind)
                .ThenBy(row => row.ReviewId),
            "ratingDesc" => query
                .OrderByDescending(row => row.Rating)
                .ThenByDescending(row => row.CreatedAt)
                .ThenBy(row => row.ReviewKind)
                .ThenByDescending(row => row.ReviewId),
            _ => query
                .OrderByDescending(row => row.CreatedAt)
                .ThenBy(row => row.ReviewKind)
                .ThenByDescending(row => row.ReviewId)
        };
    }

    private static AdminReviewAuthorDto BuildAuthorDto(ReviewListRow row)
    {
        var displayName = BuildDisplayName(
            row.AuthorDisplayName,
            row.AuthorFirstName,
            row.AuthorLastName,
            row.AuthorUsername,
            row.AuthorEmail);

        return new AdminReviewAuthorDto
        {
            UserId = row.AuthorUserId,
            DisplayName = displayName,
            Email = row.AuthorEmail,
            AvatarUrl = row.AuthorAvatarUrl,
            Initials = BuildInitials(displayName, row.AuthorEmail),
            Status = ResolveAccountStatus(row.AuthorIsSuspended, row.AuthorIsDeleted)
        };
    }

    private static AdminReviewedEntityDto BuildEntityDto(ReviewListRow row) => new()
    {
        EntityId = row.EntityId,
        EntityType = row.EntityType,
        Name = row.EntityName,
        Region = row.RegionName,
        Location = row.LocationName,
        Destination = row.DestinationName
    };

    private static AdminReviewReportDto MapReportDto(ReportRow report)
    {
        var reporterName = BuildDisplayName(
            report.ReporterDisplayName,
            report.ReporterFirstName,
            report.ReporterLastName,
            report.ReporterUsername,
            report.ReporterEmail);
        var reviewerName = report.ReviewedByUserId.HasValue
            ? BuildDisplayName(
                report.ReviewerDisplayName,
                report.ReviewerFirstName,
                report.ReviewerLastName,
                report.ReviewerUsername,
                report.ReviewerEmail)
            : null;

        return new AdminReviewReportDto
        {
            ReportId = report.ReportId,
            Reporter = new AdminReviewReporterDto
            {
                UserId = report.ReporterUserId,
                DisplayName = reporterName
            },
            Reason = FormatEnum(report.Reason),
            Details = string.IsNullOrWhiteSpace(report.Note) ? null : report.Note,
            Status = report.Status.ToString(),
            CreatedAt = report.CreatedAt,
            DecidedAt = report.ReviewedAt,
            DecidedBy = report.ReviewedByUserId.HasValue && reviewerName is not null
                ? new AdminReviewModeratorDto
                {
                    UserId = report.ReviewedByUserId.Value,
                    DisplayName = reviewerName
                }
                : null,
            Decision = report.Decision?.ToString(),
            ModeratorNote = string.IsNullOrWhiteSpace(report.DecisionNote) ? null : report.DecisionNote
        };
    }

    private static AdminReviewModerationHistoryDto MapHistoryDto(ReportRow report)
    {
        var reviewerName = report.ReviewedByUserId.HasValue
            ? BuildDisplayName(
                report.ReviewerDisplayName,
                report.ReviewerFirstName,
                report.ReviewerLastName,
                report.ReviewerUsername,
                report.ReviewerEmail)
            : null;

        return new AdminReviewModerationHistoryDto
        {
            Action = report.Decision?.ToString() ?? report.Status.ToString(),
            Moderator = report.ReviewedByUserId.HasValue && reviewerName is not null
                ? new AdminReviewModeratorDto
                {
                    UserId = report.ReviewedByUserId.Value,
                    DisplayName = reviewerName
                }
                : null,
            CreatedAt = report.ReviewedAt ?? report.CreatedAt,
            Note = string.IsNullOrWhiteSpace(report.DecisionNote) ? null : report.DecisionNote,
            ReportId = report.ReportId
        };
    }

    private static AdminReviewDetailReportSummaryDto BuildReportSummary(
        IReadOnlyCollection<ReportRow> reports) => new()
        {
            TotalReports = reports.Count,
            PendingReports = reports.Count(report => report.Status == ReviewReportStatus.Pending),
            AcceptedReports = reports.Count(report => report.Status == ReviewReportStatus.Actioned),
            RejectedReports = reports.Count(report => report.Status == ReviewReportStatus.Dismissed)
        };

    private static AdminReviewPermissionsDto BuildPermissions(
        UserSecurityRow currentAdmin,
        int authorUserId,
        bool authorIsSuspended,
        bool authorIsDeleted,
        IReadOnlyCollection<string> authorRoles,
        bool canDeleteReview)
    {
        var isAdministrator = currentAdmin.Roles.Contains(RoleNames.Admin, StringComparer.Ordinal);
        var isContentModerator = currentAdmin.Roles.Contains(RoleNames.ContentModerator, StringComparer.Ordinal);
        var canMutateAuthor =
            currentAdmin.UserId != authorUserId &&
            !authorIsDeleted &&
            !authorRoles.Any(IsProtectedAdminAppRole);
        var canStandaloneSuspend = isAdministrator && canMutateAuthor && !authorIsSuspended;
        var canUnsuspend = isAdministrator && canMutateAuthor && authorIsSuspended;
        var canDeleteAuthor = isAdministrator && canMutateAuthor;
        var canRemoveReviewAndSuspendAuthor =
            canDeleteReview &&
            canMutateAuthor &&
            !authorIsSuspended &&
            (isAdministrator || isContentModerator);

        return new AdminReviewPermissionsDto
        {
            CanDeleteReview = canDeleteReview,
            CanSuspendAuthor = canStandaloneSuspend,
            CanUnsuspendAuthor = canUnsuspend,
            CanDeleteAuthor = canDeleteAuthor,
            CanRemoveReviewAndSuspendAuthor = canRemoveReviewAndSuspendAuthor,
            CanRemoveReviewAndDeleteAuthor = canDeleteReview && canDeleteAuthor
        };
    }

    private static bool IsProtectedAdminAppRole(string role) =>
        role is RoleNames.Admin
            or RoleNames.ContentModerator
            or RoleNames.TerritoryManager
            or RoleNames.TerritoryAssociate
            or RoleNames.RegionalManager;

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
        if (!roles.Contains(RoleNames.Admin, StringComparer.Ordinal) &&
            !roles.Contains(RoleNames.ContentModerator, StringComparer.Ordinal))
            throw new UnauthorizedAccessException("Review moderator role is required.");

        if (currentUser.IsDeleted || currentUser.IsSuspended)
            throw new UnauthorizedAccessException("Admin account is not active.");

        currentUser.Roles = roles;
        return currentUser;
    }

    private static void EnsureCanMutateAuthor(
        UserSecurityRow currentAdmin,
        User author,
        string action)
    {
        if (currentAdmin.UserId == author.Id)
            throw new InvalidOperationException($"Review moderators cannot {action} their own account.");

        var roles = NormalizeRoles(author.UserRoles.Select(userRole => userRole.Role.Name));
        if (action == "delete" && !currentAdmin.Roles.Contains(RoleNames.Admin, StringComparer.Ordinal))
            throw new InvalidOperationException("Content Moderators cannot deactivate author accounts.");

        if (roles.Any(IsProtectedAdminAppRole))
            throw new InvalidOperationException("Admin app author accounts cannot be changed from Review Management.");
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

    private static ReviewReference ParseReviewKey(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException("reviewKey is required.");

        var parts = value.Trim().Split(new[] { ':', '-' }, 2, StringSplitOptions.TrimEntries);
        if (parts.Length != 2 || !int.TryParse(parts[1], out var reviewId) || reviewId <= 0)
            throw new InvalidOperationException("Invalid review key.");

        var kind = parts[0].Trim().ToLowerInvariant() switch
        {
            "place" or "stay" => AdminReviewKind.Place,
            "event" => AdminReviewKind.Event,
            "destination" => AdminReviewKind.Destination,
            "location" => AdminReviewKind.Location,
            _ => throw new InvalidOperationException("Invalid review type.")
        };

        return new ReviewReference(kind, reviewId);
    }

    private static string ReviewKeyFor(AdminReviewKind kind, int reviewId) => $"{kind}:{reviewId}";

    private static string ReportLookupKey(ReviewReportTargetType targetType, int reviewId) =>
        $"{targetType}:{reviewId}";

    private static string NormalizeStatus(string? value)
    {
        return value?.Trim().ToLowerInvariant() switch
        {
            "reported" or "pending" => "reported",
            "active" => "active",
            _ => "all"
        };
    }

    private static IReadOnlyCollection<string>? NormalizeTypes(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var rawTypes = value
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(type => type.ToLowerInvariant())
            .Where(type => type.Length > 0 && type != "all")
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (rawTypes.Count == 0)
            return null;

        var supported = new HashSet<string>(StringComparer.Ordinal)
        {
            "destination",
            "location",
            "place",
            "stay",
            "event",
            "comment"
        };

        if (rawTypes.Any(type => !supported.Contains(type)))
            throw new InvalidOperationException("Invalid review type filter.");

        return rawTypes;
    }

    private static string NormalizeSort(string? sort)
    {
        return sort?.Trim() switch
        {
            "oldest" => "oldest",
            "reportsDesc" => "reportsDesc",
            "ratingAsc" => "ratingAsc",
            "ratingDesc" => "ratingDesc",
            _ => "newest"
        };
    }

    private static string NormalizeModerationNote(string? value)
    {
        var trimmed = string.IsNullOrWhiteSpace(value)
            ? DefaultModerationNote
            : value.Trim();
        return trimmed.Length > 1000 ? trimmed[..1000] : trimmed;
    }

    private static string ResolveAccountStatus(bool isSuspended, bool isDeleted)
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
        string? email)
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

        return string.IsNullOrWhiteSpace(email)
            ? "Unknown user"
            : email.Trim();
    }

    private static string BuildInitials(string displayName, string email)
    {
        var parts = displayName
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Take(2)
            .ToList();

        if (parts.Count > 0)
            return string.Concat(parts.Select(part => char.ToUpperInvariant(part[0])));

        return string.IsNullOrWhiteSpace(email)
            ? "?"
            : char.ToUpperInvariant(email.Trim()[0]).ToString();
    }

    private static string FormatEnum(Enum value)
    {
        var text = value.ToString();
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        var chars = new List<char>(text.Length + 8) { text[0] };
        for (var index = 1; index < text.Length; index++)
        {
            var current = text[index];
            var previous = text[index - 1];
            if (char.IsUpper(current) && !char.IsUpper(previous))
                chars.Add(' ');

            chars.Add(current);
        }

        return new string(chars.ToArray());
    }

    private enum AdminReviewKind
    {
        Place,
        Event,
        Destination,
        Location
    }

    private sealed record ReviewReference(AdminReviewKind Kind, int ReviewId)
    {
        public ReviewReportTargetType? ReportTargetType => Kind switch
        {
            AdminReviewKind.Place => ReviewReportTargetType.Place,
            AdminReviewKind.Event => ReviewReportTargetType.Event,
            _ => null
        };
    }

    private sealed class ReviewListRow
    {
        public string ReviewKey { get; set; } = string.Empty;
        public AdminReviewKind ReviewKind { get; set; }
        public int ReviewId { get; set; }
        public int AuthorUserId { get; set; }
        public string? AuthorDisplayName { get; set; }
        public string AuthorFirstName { get; set; } = string.Empty;
        public string AuthorLastName { get; set; } = string.Empty;
        public string AuthorUsername { get; set; } = string.Empty;
        public string AuthorEmail { get; set; } = string.Empty;
        public string? AuthorAvatarUrl { get; set; }
        public bool AuthorIsSuspended { get; set; }
        public bool AuthorIsDeleted { get; set; }
        public double Rating { get; set; }
        public string Content { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
        public int EntityId { get; set; }
        public string EntityType { get; set; } = string.Empty;
        public string EntityTypeNormalized { get; set; } = string.Empty;
        public string EntityName { get; set; } = string.Empty;
        public string? LocationName { get; set; }
        public string? DestinationName { get; set; }
        public string? RegionName { get; set; }
        public ReviewReportTargetType? ReportTargetType { get; set; }
        public int ReportCount { get; set; }
        public int PendingReportCount { get; set; }

        public string ReportLookupKey =>
            ReportTargetType.HasValue ? AdminReviewManagementService.ReportLookupKey(ReportTargetType.Value, ReviewId) : string.Empty;
    }

    private sealed class ReportRow
    {
        public int ReportId { get; set; }
        public ReviewReportTargetType TargetType { get; set; }
        public int ReviewId { get; set; }
        public int ReporterUserId { get; set; }
        public string? ReporterDisplayName { get; set; }
        public string ReporterFirstName { get; set; } = string.Empty;
        public string ReporterLastName { get; set; } = string.Empty;
        public string ReporterUsername { get; set; } = string.Empty;
        public string ReporterEmail { get; set; } = string.Empty;
        public ReviewReportReason Reason { get; set; }
        public string Note { get; set; } = string.Empty;
        public ReviewReportStatus Status { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? ReviewedAt { get; set; }
        public int? ReviewedByUserId { get; set; }
        public string? ReviewerDisplayName { get; set; }
        public string? ReviewerFirstName { get; set; }
        public string? ReviewerLastName { get; set; }
        public string? ReviewerUsername { get; set; }
        public string? ReviewerEmail { get; set; }
        public ReviewReportDecision? Decision { get; set; }
        public string DecisionNote { get; set; } = string.Empty;
        public int ReviewAuthorUserId { get; set; }
        public string ReviewAuthorName { get; set; } = string.Empty;
        public int EntityId { get; set; }
        public string EntityName { get; set; } = string.Empty;
        public int RegionId { get; set; }
        public string RegionName { get; set; } = string.Empty;
        public double ReviewRating { get; set; }
        public string ReviewCommentSnapshot { get; set; } = string.Empty;
        public DateTime ReviewCreatedAt { get; set; }
    }

    private sealed class UserSecurityRow
    {
        public int UserId { get; set; }
        public bool IsSuspended { get; set; }
        public bool IsDeleted { get; set; }
        public List<string> Roles { get; set; } = new();
    }

    private sealed class ReviewMutationTarget
    {
        public AdminReviewKind Kind { get; private init; }
        public string ReviewKey { get; private init; } = string.Empty;
        public int ReviewId { get; private init; }
        public int EntityId { get; private init; }
        public User Author { get; private init; } = null!;
        public ReviewReportTargetType? ReportTargetType { get; private init; }
        public PlaceReview? PlaceReview { get; private init; }
        public EventReview? EventReview { get; private init; }
        public DestinationReview? DestinationReview { get; private init; }
        public LocationReview? LocationReview { get; private init; }

        public static ReviewMutationTarget ForPlace(PlaceReview review) => new()
        {
            Kind = AdminReviewKind.Place,
            ReviewKey = ReviewKeyFor(AdminReviewKind.Place, review.Id),
            ReviewId = review.Id,
            EntityId = review.PlaceId,
            Author = review.User,
            ReportTargetType = ReviewReportTargetType.Place,
            PlaceReview = review
        };

        public static ReviewMutationTarget ForEvent(EventReview review) => new()
        {
            Kind = AdminReviewKind.Event,
            ReviewKey = ReviewKeyFor(AdminReviewKind.Event, review.Id),
            ReviewId = review.Id,
            EntityId = review.EventId,
            Author = review.User,
            ReportTargetType = ReviewReportTargetType.Event,
            EventReview = review
        };

        public static ReviewMutationTarget ForDestination(DestinationReview review) => new()
        {
            Kind = AdminReviewKind.Destination,
            ReviewKey = ReviewKeyFor(AdminReviewKind.Destination, review.Id),
            ReviewId = review.Id,
            EntityId = review.DestinationId,
            Author = review.User,
            DestinationReview = review
        };

        public static ReviewMutationTarget ForLocation(LocationReview review) => new()
        {
            Kind = AdminReviewKind.Location,
            ReviewKey = ReviewKeyFor(AdminReviewKind.Location, review.Id),
            ReviewId = review.Id,
            EntityId = review.LocationId,
            Author = review.User,
            LocationReview = review
        };
    }
}
