using System.Text.Json;
using Geotrack.Application.DTOs.Admin;
using Geotrack.Application.Interfaces.Service;
using Geotrack.Application.Security;
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

    private const string SupportTicketCreatedType = "SupportTicketCreated";
    private const string ReviewReportedType = "ReviewReported";

    private const string InfoSeverity = "Info";
    private const string WarningSeverity = "Warning";

    private const string SupportTicketTargetType = "SupportTicket";
    private const string ReviewReportNotificationTargetType = "ReviewReport";

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
        await EnsureCurrentAdminAsync(currentUserId, cancellationToken);

        var skip = Math.Max(0, request.Skip);
        var take = Math.Clamp(request.Take <= 0 ? DefaultTake : request.Take, 1, MaxTake);
        var readCutoff = DateTime.UtcNow.AddMonths(-2);

        var query =
            from notification in _db.AdminNotifications.AsNoTracking()
            where notification.AudienceRole == AdminAudienceRole
            join state in _db.AdminNotificationReadStates.AsNoTracking()
                    .Where(readState => readState.UserId == currentUserId)
                on notification.Id equals state.AdminNotificationId into readStates
            from readState in readStates.DefaultIfEmpty()
            select new
            {
                Notification = notification,
                ReadAt = readState == null ? null : (DateTime?)readState.ReadAt
            };

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
            UnreadCount = await CountUnreadAsync(currentUserId, cancellationToken),
            TotalCount = totalCount,
            Skip = skip,
            Take = take,
            NextSkip = skip + take < totalCount ? skip + take : null,
            HasMore = skip + take < totalCount
        };
    }

    public async Task<AdminNotificationUnreadCountDto> GetUnreadCountAsync(
        int currentUserId,
        CancellationToken cancellationToken = default)
    {
        await EnsureCurrentAdminAsync(currentUserId, cancellationToken);

        return new AdminNotificationUnreadCountDto
        {
            UnreadCount = await CountUnreadAsync(currentUserId, cancellationToken)
        };
    }

    public async Task<AdminNotificationMutationDto> MarkReadAsync(
        int currentUserId,
        int notificationId,
        CancellationToken cancellationToken = default)
    {
        await EnsureCurrentAdminAsync(currentUserId, cancellationToken);

        if (notificationId <= 0)
            throw new InvalidOperationException("notificationId must be positive.");

        var exists = await _db.AdminNotifications
            .AsNoTracking()
            .AnyAsync(
                notification =>
                    notification.Id == notificationId &&
                    notification.AudienceRole == AdminAudienceRole,
                cancellationToken);

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
            UnreadCount = await CountUnreadAsync(currentUserId, cancellationToken)
        };
    }

    public async Task<AdminNotificationMutationDto> MarkAllReadAsync(
        int currentUserId,
        CancellationToken cancellationToken = default)
    {
        await EnsureCurrentAdminAsync(currentUserId, cancellationToken);

        var unreadNotificationIds = await _db.AdminNotifications
            .AsNoTracking()
            .Where(notification =>
                notification.AudienceRole == AdminAudienceRole &&
                !_db.AdminNotificationReadStates.Any(state =>
                    state.AdminNotificationId == notification.Id &&
                    state.UserId == currentUserId))
            .Select(notification => notification.Id)
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
            type: ReviewReportedType,
            title: "New reported review",
            message: message,
            severity: WarningSeverity,
            targetType: ReviewReportNotificationTargetType,
            targetId: report.Id,
            targetRoute: $"/admin-reviews?status=reported&highlight={Uri.EscapeDataString(reviewKey)}",
            metadataSummary: reason,
            createdAt: report.CreatedAt == default ? DateTime.UtcNow : report.CreatedAt,
            cancellationToken);
    }

    private async Task AddNotificationIfMissingAsync(
        string type,
        string title,
        string message,
        string severity,
        string targetType,
        int targetId,
        string targetRoute,
        string metadataSummary,
        DateTime createdAt,
        CancellationToken cancellationToken)
    {
        var exists = await _db.AdminNotifications.AnyAsync(
            notification =>
                notification.Type == type &&
                notification.TargetType == targetType &&
                notification.TargetId == targetId,
            cancellationToken);

        if (exists)
            return;

        _db.AdminNotifications.Add(new AdminNotification
        {
            AudienceRole = AdminAudienceRole,
            Type = type,
            Title = title,
            Message = TrimForMessage(message, 1000),
            Severity = NormalizeSeverity(severity),
            TargetType = targetType,
            TargetId = targetId,
            TargetRoute = NormalizeTargetRoute(targetRoute),
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
                    notification.Type == type &&
                    notification.TargetType == targetType &&
                    notification.TargetId == targetId,
                cancellationToken);

            if (!wasCreatedConcurrently)
                throw;
        }
    }

    private async Task<int> CountUnreadAsync(int currentUserId, CancellationToken cancellationToken)
    {
        return await _db.AdminNotifications
            .AsNoTracking()
            .CountAsync(
                notification =>
                    notification.AudienceRole == AdminAudienceRole &&
                    !_db.AdminNotificationReadStates.Any(state =>
                        state.AdminNotificationId == notification.Id &&
                        state.UserId == currentUserId),
                cancellationToken);
    }

    private async Task EnsureCurrentAdminAsync(int currentUserId, CancellationToken cancellationToken)
    {
        var userExists = await _db.Users
            .AsNoTracking()
            .AnyAsync(user => user.Id == currentUserId, cancellationToken);

        if (!userExists)
            throw new KeyNotFoundException("Admin user was not found.");

        var isAdmin = await _db.UserRoles
            .AsNoTracking()
            .AnyAsync(
                userRole =>
                    userRole.UserId == currentUserId &&
                    userRole.Role.Name == RoleNames.Admin,
                cancellationToken);

        if (!isAdmin)
            throw new UnauthorizedAccessException("Admin role is required.");
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
}
