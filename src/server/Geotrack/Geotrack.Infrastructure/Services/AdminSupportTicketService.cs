using Geotrack.Application.DTOs.Admin;
using Geotrack.Application.Interfaces.Service;
using Geotrack.Application.Security;
using Geotrack.Domain.Entities.Users;
using Microsoft.EntityFrameworkCore;

namespace Geotrack.Infrastructure.Services;

public sealed class AdminSupportTicketService : IAdminSupportTicketService
{
    private const int DefaultTake = 25;
    private const int MaxTake = 50;
    private const int MessagePreviewLength = 180;
    private const int NotePreviewLength = 140;
    private const string LegacyReceivedStatus = "Received";
    private const string LegacyInProgressStatus = "InProgress";

    private readonly AppDbContext _db;

    public AdminSupportTicketService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<AdminSupportTicketListResponseDto> GetTicketsAsync(
        int currentUserId,
        AdminSupportTicketListRequestDto request,
        CancellationToken cancellationToken = default)
    {
        _ = await EnsureCurrentAdminAsync(currentUserId, cancellationToken);

        var skip = Math.Max(0, request.Skip);
        var take = Math.Clamp(request.Take <= 0 ? DefaultTake : request.Take, 1, MaxTake);
        var status = NormalizeStatusFilter(request.Status);
        var category = NormalizeCategoryFilter(request.Category ?? request.Type);
        var priority = NormalizePriorityFilter(request.Priority);

        var query = _db.SupportRequests
            .AsNoTracking()
            .Include(ticket => ticket.User)
            .AsQueryable();

        query = ApplyStatusFilter(query, status);
        query = ApplyCategoryFilter(query, category);
        query = ApplyPriorityFilter(query, priority);
        query = ApplySearch(query, request.Search);

        var totalCount = await query.CountAsync(cancellationToken);
        var rows = await ApplySort(query, NormalizeSort(request.Sort))
            .Skip(skip)
            .Take(take)
            .ToListAsync(cancellationToken);

        return new AdminSupportTicketListResponseDto
        {
            Items = rows.Select(MapListItem).ToList(),
            TotalCount = totalCount,
            Skip = skip,
            Take = take,
            NextSkip = skip + take < totalCount ? skip + take : null,
            HasMore = skip + take < totalCount,
            Counts = await BuildCountsAsync(cancellationToken)
        };
    }

    public async Task<AdminSupportTicketDetailDto> GetTicketAsync(
        int currentUserId,
        int ticketId,
        CancellationToken cancellationToken = default)
    {
        _ = await EnsureCurrentAdminAsync(currentUserId, cancellationToken);

        if (ticketId <= 0)
            throw new InvalidOperationException("ticketId must be positive.");

        var ticket = await _db.SupportRequests
            .AsNoTracking()
            .Include(item => item.User)
            .Include(item => item.ResolvedByUser)
            .SingleOrDefaultAsync(item => item.Id == ticketId, cancellationToken);

        return ticket is null
            ? throw new KeyNotFoundException("Support ticket was not found.")
            : MapDetail(ticket);
    }

    public async Task<AdminSupportTicketDetailDto> UpdateStatusAsync(
        int currentUserId,
        int ticketId,
        AdminSupportTicketStatusUpdateRequestDto request,
        CancellationToken cancellationToken = default)
    {
        var currentAdmin = await EnsureCurrentAdminAsync(currentUserId, cancellationToken);

        if (ticketId <= 0)
            throw new InvalidOperationException("ticketId must be positive.");

        var nextStatus = NormalizeRequestedStatus(request.Status);
        var ticket = await _db.SupportRequests
            .SingleOrDefaultAsync(item => item.Id == ticketId, cancellationToken);

        if (ticket is null)
            throw new KeyNotFoundException("Support ticket was not found.");

        var currentStatus = NormalizeStoredStatus(ticket.Status);
        ValidateStatusTransition(currentStatus, nextStatus);

        var now = DateTime.UtcNow;
        ticket.Status = nextStatus;
        ticket.UpdatedAt = now;

        if (request.InternalNote is not null)
        {
            ticket.InternalNote = NormalizeOptionalText(request.InternalNote, 2000);
        }

        if (request.ResolutionMessage is not null)
        {
            ticket.ResolutionMessage = NormalizeOptionalText(request.ResolutionMessage, 2000);
        }

        if (nextStatus == SupportRequestStatuses.Open)
        {
            ticket.ResolvedAt = null;
            ticket.ResolvedByUserId = null;
        }
        else
        {
            ticket.ResolvedAt = now;
            ticket.ResolvedByUserId = currentAdmin.UserId;
        }

        await _db.SaveChangesAsync(cancellationToken);

        return await GetTicketAsync(currentUserId, ticketId, cancellationToken);
    }

    public async Task<AdminSupportTicketDetailDto> AddNoteAsync(
        int currentUserId,
        int ticketId,
        AdminSupportTicketNoteRequestDto request,
        CancellationToken cancellationToken = default)
    {
        _ = await EnsureCurrentAdminAsync(currentUserId, cancellationToken);

        if (ticketId <= 0)
            throw new InvalidOperationException("ticketId must be positive.");

        var ticket = await _db.SupportRequests
            .SingleOrDefaultAsync(item => item.Id == ticketId, cancellationToken);

        if (ticket is null)
            throw new KeyNotFoundException("Support ticket was not found.");

        ticket.InternalNote = NormalizeOptionalText(request.InternalNote, 2000);
        ticket.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);

        return await GetTicketAsync(currentUserId, ticketId, cancellationToken);
    }

    private async Task<CurrentAdmin> EnsureCurrentAdminAsync(
        int currentUserId,
        CancellationToken cancellationToken)
    {
        var currentAdmin = await _db.Users
            .AsNoTracking()
            .Where(user => user.Id == currentUserId)
            .Select(user => new CurrentAdmin(
                user.Id,
                user.Email,
                BuildDisplayName(user.DisplayName, user.FirstName, user.LastName, user.Username, user.Email),
                user.UserRoles.Select(userRole => userRole.Role.Name).ToList()))
            .SingleOrDefaultAsync(cancellationToken);

        if (currentAdmin is null)
            throw new KeyNotFoundException("Admin user was not found.");

        if (!currentAdmin.Roles.Any(role => RoleNames.Normalize(role) == RoleNames.Admin))
            throw new UnauthorizedAccessException("Admin role is required.");

        return currentAdmin;
    }

    private async Task<AdminSupportTicketListCountsDto> BuildCountsAsync(CancellationToken cancellationToken)
    {
        var query = _db.SupportRequests.AsNoTracking();

        return new AdminSupportTicketListCountsDto
        {
            OpenCount = await query.CountAsync(IsOpenStatusExpression(), cancellationToken),
            ResolvedCount = await query.CountAsync(
                ticket => ticket.Status == SupportRequestStatuses.Resolved,
                cancellationToken),
            DismissedCount = await query.CountAsync(
                ticket => ticket.Status == SupportRequestStatuses.Dismissed,
                cancellationToken),
            AllCount = await query.CountAsync(cancellationToken)
        };
    }

    private static IQueryable<SupportRequest> ApplyStatusFilter(
        IQueryable<SupportRequest> query,
        string status)
    {
        return status switch
        {
            "open" => query.Where(IsOpenStatusExpression()),
            "resolved" => query.Where(ticket => ticket.Status == SupportRequestStatuses.Resolved),
            "dismissed" => query.Where(ticket => ticket.Status == SupportRequestStatuses.Dismissed),
            _ => query
        };
    }

    private static IQueryable<SupportRequest> ApplyCategoryFilter(
        IQueryable<SupportRequest> query,
        string? category)
    {
        return category is null ? query : query.Where(ticket => ticket.Category == category);
    }

    private static IQueryable<SupportRequest> ApplyPriorityFilter(
        IQueryable<SupportRequest> query,
        string? priority)
    {
        return priority is null ? query : query.Where(ticket => ticket.Priority == priority);
    }

    private static IQueryable<SupportRequest> ApplySearch(
        IQueryable<SupportRequest> query,
        string? search)
    {
        if (string.IsNullOrWhiteSpace(search))
            return query;

        var pattern = $"%{EscapeLike(search.Trim())}%";
        return query.Where(ticket =>
            EF.Functions.ILike(ticket.Subject, pattern) ||
            EF.Functions.ILike(ticket.Message, pattern) ||
            EF.Functions.ILike(ticket.Category, pattern) ||
            (ticket.PageUrl != null && EF.Functions.ILike(ticket.PageUrl, pattern)) ||
            (ticket.User != null && (
                EF.Functions.ILike(ticket.User.Email, pattern) ||
                EF.Functions.ILike(ticket.User.Username, pattern) ||
                EF.Functions.ILike(ticket.User.FirstName, pattern) ||
                EF.Functions.ILike(ticket.User.LastName, pattern) ||
                (ticket.User.DisplayName != null && EF.Functions.ILike(ticket.User.DisplayName, pattern)))));
    }

    private static IOrderedQueryable<SupportRequest> ApplySort(
        IQueryable<SupportRequest> query,
        string sort)
    {
        return sort switch
        {
            "oldest" => query
                .OrderBy(ticket => ticket.CreatedAt)
                .ThenBy(ticket => ticket.Id),
            "priorityDesc" => query
                .OrderByDescending(ticket =>
                    ticket.Priority == SupportRequestPriorities.Critical ? 4 :
                    ticket.Priority == SupportRequestPriorities.High ? 3 :
                    ticket.Priority == SupportRequestPriorities.Normal ? 2 :
                    ticket.Priority == SupportRequestPriorities.Low ? 1 :
                    2)
                .ThenByDescending(ticket => ticket.CreatedAt)
                .ThenByDescending(ticket => ticket.Id),
            "updatedDesc" => query
                .OrderByDescending(ticket => ticket.UpdatedAt ?? ticket.CreatedAt)
                .ThenByDescending(ticket => ticket.Id),
            _ => query
                .OrderByDescending(ticket => ticket.CreatedAt)
                .ThenByDescending(ticket => ticket.Id)
        };
    }

    private static AdminSupportTicketListItemDto MapListItem(SupportRequest ticket)
    {
        var status = NormalizeStoredStatus(ticket.Status);
        return new AdminSupportTicketListItemDto
        {
            TicketId = ticket.Id,
            ReferenceCode = BuildSupportReferenceCode(ticket.Id),
            Reporter = MapReporter(ticket.User),
            Category = ticket.Category,
            CategoryLabel = CategoryLabel(ticket.Category),
            Subject = ticket.Subject,
            MessagePreview = TrimForPreview(ticket.Message, MessagePreviewLength),
            Status = status,
            Priority = NormalizeStoredPriority(ticket.Priority),
            PageUrl = ticket.PageUrl,
            AppRoute = ticket.PageUrl,
            CreatedAt = ticket.CreatedAt,
            UpdatedAt = ticket.UpdatedAt,
            ResolvedAt = ticket.ResolvedAt,
            HasAttachment = !string.IsNullOrWhiteSpace(ticket.AttachmentUrl),
            InternalNotePreview = TrimNullableForPreview(ticket.InternalNote, NotePreviewLength),
            Permissions = BuildPermissions(status)
        };
    }

    private static AdminSupportTicketDetailDto MapDetail(SupportRequest ticket)
    {
        var status = NormalizeStoredStatus(ticket.Status);
        var resolvedBy = MapAdminSummary(ticket.ResolvedByUser);

        return new AdminSupportTicketDetailDto
        {
            TicketId = ticket.Id,
            ReferenceCode = BuildSupportReferenceCode(ticket.Id),
            Reporter = MapReporter(ticket.User),
            Category = ticket.Category,
            CategoryLabel = CategoryLabel(ticket.Category),
            Subject = ticket.Subject,
            Message = ticket.Message,
            Status = status,
            Priority = NormalizeStoredPriority(ticket.Priority),
            AppRoute = ticket.PageUrl,
            PageUrl = ticket.PageUrl,
            UserAgent = ticket.UserAgent,
            DeviceInfo = ticket.DeviceInfo,
            Browser = ticket.Browser,
            Platform = ticket.Platform,
            AttachmentUrl = ticket.AttachmentUrl,
            CreatedAt = ticket.CreatedAt,
            UpdatedAt = ticket.UpdatedAt,
            ResolvedAt = ticket.ResolvedAt,
            ResolvedBy = resolvedBy,
            InternalNote = ticket.InternalNote,
            ResolutionMessage = ticket.ResolutionMessage,
            StatusHistory = BuildStatusHistory(ticket, status, resolvedBy),
            Permissions = BuildPermissions(status)
        };
    }

    private static List<AdminSupportTicketHistoryItemDto> BuildStatusHistory(
        SupportRequest ticket,
        string status,
        AdminSupportTicketAdminSummaryDto? resolvedBy)
    {
        var history = new List<AdminSupportTicketHistoryItemDto>
        {
            new()
            {
                Action = "Submitted",
                CreatedAt = ticket.CreatedAt,
                Note = "Support report submitted by user."
            }
        };

        if (ticket.ResolvedAt.HasValue)
        {
            history.Add(new AdminSupportTicketHistoryItemDto
            {
                Action = status == SupportRequestStatuses.Dismissed ? "Dismissed" : "Resolved",
                Actor = resolvedBy,
                CreatedAt = ticket.ResolvedAt.Value,
                Note = ticket.InternalNote
            });
        }
        else if (!string.IsNullOrWhiteSpace(ticket.InternalNote) && ticket.UpdatedAt.HasValue)
        {
            history.Add(new AdminSupportTicketHistoryItemDto
            {
                Action = "Internal Note Updated",
                CreatedAt = ticket.UpdatedAt.Value,
                Note = ticket.InternalNote
            });
        }

        return history
            .OrderBy(item => item.CreatedAt)
            .ToList();
    }

    private static AdminSupportTicketReporterDto MapReporter(User? user)
    {
        if (user is null)
        {
            return new AdminSupportTicketReporterDto
            {
                DisplayName = "Anonymous user",
                Initials = "AU",
                IsAnonymous = true
            };
        }

        var displayName = BuildDisplayName(user.DisplayName, user.FirstName, user.LastName, user.Username, user.Email);
        return new AdminSupportTicketReporterDto
        {
            UserId = user.Id,
            DisplayName = displayName,
            Email = user.Email,
            AvatarUrl = user.AvatarUrl,
            Initials = BuildInitials(displayName, user.Email),
            IsAnonymous = false
        };
    }

    private static AdminSupportTicketAdminSummaryDto? MapAdminSummary(User? user)
    {
        if (user is null)
            return null;

        return new AdminSupportTicketAdminSummaryDto
        {
            UserId = user.Id,
            DisplayName = BuildDisplayName(user.DisplayName, user.FirstName, user.LastName, user.Username, user.Email),
            Email = user.Email
        };
    }

    private static AdminSupportTicketPermissionsDto BuildPermissions(string status)
    {
        return new AdminSupportTicketPermissionsDto
        {
            CanView = true,
            CanResolve = status == SupportRequestStatuses.Open,
            CanDismiss = status == SupportRequestStatuses.Open,
            CanReopen = status is SupportRequestStatuses.Resolved or SupportRequestStatuses.Dismissed,
            CanAddNote = true
        };
    }

    private static string NormalizeStatusFilter(string? value)
    {
        return NormalizeToken(value) switch
        {
            "open" or "received" or "inprogress" or "in-progress" or "in_progress" or "unresolved" => "open",
            "resolved" => "resolved",
            "dismissed" => "dismissed",
            _ => "all"
        };
    }

    private static string NormalizeRequestedStatus(string? value)
    {
        return NormalizeToken(value) switch
        {
            "open" or "reopen" => SupportRequestStatuses.Open,
            "resolved" or "resolve" => SupportRequestStatuses.Resolved,
            "dismissed" or "dismiss" => SupportRequestStatuses.Dismissed,
            _ => throw new InvalidOperationException("Invalid support ticket status. Allowed statuses are Open, Resolved, and Dismissed.")
        };
    }

    private static string? NormalizeCategoryFilter(string? value)
    {
        return NormalizeToken(value) switch
        {
            "" or "all" => null,
            "account" or "accountaccess" or "account-access" => "account-access",
            "savedplacesandcollections" or "saved-places-and-collections" or "savedplaces" => "saved-places-and-collections",
            "locationandmaps" or "location-and-maps" or "maps" or "technicalissue" or "technical-issue" => "location-and-maps",
            "askagent" or "ask-agent" => "ask-agent",
            "eventsandstays" or "events-and-stays" or "content" => "events-and-stays",
            "privacyanddata" or "privacy-and-data" => "privacy-and-data",
            "bug" or "other" => null,
            _ => throw new InvalidOperationException("Invalid support ticket category filter.")
        };
    }

    private static string? NormalizePriorityFilter(string? value)
    {
        return NormalizeToken(value) switch
        {
            "" or "all" => null,
            "low" => SupportRequestPriorities.Low,
            "normal" => SupportRequestPriorities.Normal,
            "high" => SupportRequestPriorities.High,
            "critical" => SupportRequestPriorities.Critical,
            _ => throw new InvalidOperationException("Invalid support ticket priority filter.")
        };
    }

    private static string NormalizeSort(string? sort)
    {
        return sort?.Trim() switch
        {
            "oldest" => "oldest",
            "priorityDesc" => "priorityDesc",
            "updatedDesc" => "updatedDesc",
            _ => "newest"
        };
    }

    private static string NormalizeStoredStatus(string? status)
    {
        return status?.Trim() switch
        {
            LegacyReceivedStatus or LegacyInProgressStatus => SupportRequestStatuses.Open,
            SupportRequestStatuses.Resolved => SupportRequestStatuses.Resolved,
            SupportRequestStatuses.Dismissed => SupportRequestStatuses.Dismissed,
            _ => SupportRequestStatuses.Open
        };
    }

    private static string NormalizeStoredPriority(string? priority)
    {
        return priority?.Trim() switch
        {
            SupportRequestPriorities.Low => SupportRequestPriorities.Low,
            SupportRequestPriorities.High => SupportRequestPriorities.High,
            SupportRequestPriorities.Critical => SupportRequestPriorities.Critical,
            _ => SupportRequestPriorities.Normal
        };
    }

    private static string? NormalizeOptionalText(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var trimmed = value.Trim();
        return trimmed.Length > maxLength ? trimmed[..maxLength] : trimmed;
    }

    private static string CategoryLabel(string category)
    {
        return category switch
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

    private static string TrimForPreview(string value, int maxLength)
    {
        var trimmed = value.Trim();
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength].TrimEnd() + "...";
    }

    private static string? TrimNullableForPreview(string? value, int maxLength)
    {
        return string.IsNullOrWhiteSpace(value) ? null : TrimForPreview(value, maxLength);
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

        return string.IsNullOrWhiteSpace(email) ? "Unknown user" : email.Trim();
    }

    private static string BuildInitials(string name, string email)
    {
        var source = string.IsNullOrWhiteSpace(name) ? email : name;
        var parts = source.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (parts.Length >= 2)
            return string.Concat(parts[0][0], parts[1][0]).ToUpperInvariant();

        var compact = new string(source.Where(char.IsLetterOrDigit).Take(2).ToArray());
        return string.IsNullOrWhiteSpace(compact) ? "?" : compact.ToUpperInvariant();
    }

    private static string BuildSupportReferenceCode(int id) => $"GT-{id:D6}";

    private static string NormalizeToken(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Trim().ToLowerInvariant();
    }

    private static string EscapeLike(string value)
    {
        return value
            .Replace(@"\", @"\\", StringComparison.Ordinal)
            .Replace("%", @"\%", StringComparison.Ordinal)
            .Replace("_", @"\_", StringComparison.Ordinal);
    }

    private static void ValidateStatusTransition(string currentStatus, string nextStatus)
    {
        if (currentStatus == nextStatus)
            return;

        var isValid =
            (currentStatus == SupportRequestStatuses.Open &&
             nextStatus is SupportRequestStatuses.Resolved or SupportRequestStatuses.Dismissed) ||
            (currentStatus is SupportRequestStatuses.Resolved or SupportRequestStatuses.Dismissed &&
             nextStatus == SupportRequestStatuses.Open);

        if (!isValid)
            throw new InvalidOperationException($"Cannot change support ticket status from {currentStatus} to {nextStatus}. Reopen a closed ticket before closing it with a different status.");
    }

    private static System.Linq.Expressions.Expression<Func<SupportRequest, bool>> IsOpenStatusExpression()
    {
        return ticket =>
            ticket.Status == SupportRequestStatuses.Open ||
            ticket.Status == LegacyReceivedStatus ||
            ticket.Status == LegacyInProgressStatus;
    }

    private sealed record CurrentAdmin(
        int UserId,
        string Email,
        string DisplayName,
        IReadOnlyCollection<string> Roles);
}
