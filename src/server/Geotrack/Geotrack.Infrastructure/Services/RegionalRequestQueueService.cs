using Geotrack.Domain.Entities.Core;
using System.Text.Json;
using Geotrack.Application.DTOs.Admin;
using Geotrack.Application.DTOs.Contributions;
using Geotrack.Application.Interfaces.Service;
using Geotrack.Application.Security;
using Geotrack.Application.Services;
using Geotrack.Domain.Entities.Users;
using Microsoft.EntityFrameworkCore;

namespace Geotrack.Infrastructure.Services;

public class RegionalRequestQueueService : IRegionalRequestQueueService
{
    private const int DefaultTake = 10;
    private const int MaxTake = 50;
    private const string ContributionSource = "Contribution";

    private readonly AppDbContext _db;
    private readonly IPlaceContributionService _placeContributionService;

    public RegionalRequestQueueService(
        AppDbContext db,
        IPlaceContributionService placeContributionService)
    {
        _db = db;
        _placeContributionService = placeContributionService;
    }

    public async Task<RegionalRequestQueueResponseDto> GetQueueAsync(
        int userId,
        int regionId,
        string? type,
        string? status,
        string? search,
        string? sort,
        int skip,
        int take)
    {
        await EnsureAssignedRegionAsync(userId, regionId);

        var normalizedType = NormalizeType(type);
        var normalizedStatus = NormalizeStatus(status);
        var normalizedSort = NormalizeSort(sort);
        var normalizedSkip = Math.Max(0, skip);
        var normalizedTake = Math.Clamp(take <= 0 ? DefaultTake : take, 1, MaxTake);
        var fetchLimit = normalizedSkip + normalizedTake;

        var includeContributions = normalizedType is null or "all" or "place";

        var contributionQuery = BuildContributionQuery(regionId);
        contributionQuery = ApplyContributionStatus(contributionQuery, normalizedStatus);
        contributionQuery = ApplyContributionSearch(contributionQuery, search);
        if (!includeContributions)
            contributionQuery = contributionQuery.Where(_ => false);

        var totalContributionCount = await contributionQuery.CountAsync();

        var contributionRows = includeContributions
            ? await ProjectContributions(ApplyContributionSort(contributionQuery, normalizedSort), fetchLimit)
            : new List<RegionalRequestQueueItemDto>();

        var items = SortItems(contributionRows, normalizedSort)
            .Skip(normalizedSkip)
            .Take(normalizedTake)
            .ToList();

        return new RegionalRequestQueueResponseDto
        {
            Items = items,
            TotalCount = totalContributionCount,
            PendingCount = await CountByStatusAsync(regionId, "pending", search),
            ApprovedCount = await CountByStatusAsync(regionId, "approved", search),
            RejectedCount = await CountByStatusAsync(regionId, "rejected", search),
            CountsByType = new RegionalRequestQueueCountsByTypeDto
            {
                Places = await CountContributionsAsync(regionId, "pending", search)
            },
            Skip = normalizedSkip,
            Take = normalizedTake
        };
    }

    public async Task<RegionalRequestQueueDetailDto> GetRequestAsync(
        int userId,
        int regionId,
        string source,
        int requestId)
    {
        await EnsureAssignedRegionAsync(userId, regionId);
        var normalizedSource = NormalizeSource(source);

        if (normalizedSource == ContributionSource)
            return await GetContributionDetailAsync(regionId, requestId);

        throw new InvalidOperationException("Invalid request source.");
    }

    public async Task<RegionalRequestActionResultDto> ApproveAsync(
        int userId,
        int regionId,
        string source,
        int requestId,
        string? note)
    {
        await EnsureAssignedRegionAsync(userId, regionId);
        var normalizedSource = NormalizeSource(source);

        if (normalizedSource == ContributionSource)
        {
            await EnsureContributionExistsInRegionAsync(regionId, requestId);
            await _placeContributionService.ApproveAsync(requestId, userId, note);
            return await BuildSuccessResultAsync(regionId, ContributionSource, requestId);
        }

        throw new InvalidOperationException("Invalid request source.");
    }

    public async Task<RegionalRequestActionResultDto> RejectAsync(
        int userId,
        int regionId,
        string source,
        int requestId,
        string? reason,
        string? note)
    {
        await EnsureAssignedRegionAsync(userId, regionId);
        var normalizedSource = NormalizeSource(source);
        var decisionNote = BuildDecisionNote(reason, note);

        if (normalizedSource == ContributionSource)
        {
            await EnsureContributionExistsInRegionAsync(regionId, requestId);
            await _placeContributionService.RejectAsync(requestId, userId, decisionNote);
            return await BuildSuccessResultAsync(regionId, ContributionSource, requestId);
        }

        throw new InvalidOperationException("Invalid request source.");
    }

    public async Task<RegionalRequestBulkActionResponseDto> BulkApproveAsync(
        int userId,
        RegionalRequestBulkActionRequestDto request)
    {
        return await BulkProcessAsync(
            userId,
            request,
            (source, id) => ApproveAsync(userId, request.RegionId, source, id, request.Note));
    }

    public async Task<RegionalRequestBulkActionResponseDto> BulkRejectAsync(
        int userId,
        RegionalRequestBulkActionRequestDto request)
    {
        return await BulkProcessAsync(
            userId,
            request,
            (source, id) => RejectAsync(userId, request.RegionId, source, id, request.Reason, request.Note));
    }

    private async Task<RegionalRequestBulkActionResponseDto> BulkProcessAsync(
        int userId,
        RegionalRequestBulkActionRequestDto request,
        Func<string, int, Task<RegionalRequestActionResultDto>> action)
    {
        if (request.RegionId <= 0)
            throw new InvalidOperationException("regionId is required.");

        await EnsureAssignedRegionAsync(userId, request.RegionId);

        if (request.RequestKeys.Count == 0)
            throw new InvalidOperationException("At least one request is required.");

        var response = new RegionalRequestBulkActionResponseDto();

        foreach (var requestKey in request.RequestKeys.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!TryParseRequestKey(requestKey, out var source, out var id))
            {
                response.Results.Add(new RegionalRequestActionResultDto
                {
                    RequestKey = requestKey,
                    Success = false,
                    Message = "Invalid request id."
                });
                continue;
            }

            try
            {
                response.Results.Add(await action(source, id));
            }
            catch (Exception ex) when (ex is KeyNotFoundException or InvalidOperationException or UnauthorizedAccessException)
            {
                response.Results.Add(new RegionalRequestActionResultDto
                {
                    RequestKey = requestKey,
                    Success = false,
                    Message = SafeActionError(ex)
                });
            }
        }

        response.SucceededCount = response.Results.Count(result => result.Success);
        response.FailedCount = response.Results.Count - response.SucceededCount;
        return response;
    }

    private async Task<RegionalRequestActionResultDto> BuildSuccessResultAsync(
        int regionId,
        string source,
        int requestId)
    {
        var item = await GetContributionItemAsync(regionId, requestId);

        return new RegionalRequestActionResultDto
        {
            RequestKey = item.RequestKey,
            Success = true,
            Status = item.Status,
            Item = item
        };
    }

    private IQueryable<PlaceContribution> BuildContributionQuery(int regionId)
    {
        return _db.PlaceContributions
            .AsNoTracking()
            .Where(contribution =>
                contribution.LocationId.HasValue &&
                contribution.Location != null &&
                contribution.Location.Destination.RegionId == regionId &&
                (contribution.Type != PlaceContributionType.UpdatePlace ||
                 (contribution.PlaceId.HasValue &&
                  contribution.Place != null &&
                  contribution.Place.Location.Destination.RegionId == regionId &&
                  contribution.Place.Location.Destination.RegionId == contribution.Location.Destination.RegionId)));
    }

    private static IQueryable<PlaceContribution> ApplyContributionStatus(
        IQueryable<PlaceContribution> query,
        string? status)
    {
        return status switch
        {
            null or "pending" => query.Where(contribution => contribution.Status == PlaceContributionStatus.PendingReview),
            "approved" => query.Where(contribution => contribution.Status == PlaceContributionStatus.Approved),
            "rejected" => query.Where(contribution => contribution.Status == PlaceContributionStatus.Rejected),
            "all" => query,
            _ => throw new InvalidOperationException("Invalid status filter.")
        };
    }

    private static IQueryable<PlaceContribution> ApplyContributionSearch(
        IQueryable<PlaceContribution> query,
        string? search)
    {
        var q = search?.Trim();
        if (string.IsNullOrWhiteSpace(q))
            return query;

        var like = "%" + q + "%";
        var normalized = NormalizeToken(q);
        var numericId = normalized.StartsWith("rep", StringComparison.Ordinal)
            ? null
            : ExtractNumericSearchTerm(q);

        var matchesCreate = normalized.Contains("create", StringComparison.Ordinal);
        var matchesUpdate = normalized.Contains("update", StringComparison.Ordinal);
        var matchesClaim = normalized.Contains("claim", StringComparison.Ordinal);

        return query.Where(contribution =>
            EF.Functions.ILike(contribution.Name, like) ||
            EF.Functions.ILike(contribution.Description, like) ||
            (contribution.User.DisplayName != null && EF.Functions.ILike(contribution.User.DisplayName, like)) ||
            (contribution.User.FirstName != null && EF.Functions.ILike(contribution.User.FirstName, like)) ||
            (contribution.User.LastName != null && EF.Functions.ILike(contribution.User.LastName, like)) ||
            EF.Functions.ILike(contribution.User.Username, like) ||
            (numericId.HasValue && contribution.Id == numericId.Value) ||
            (matchesCreate && contribution.Type == PlaceContributionType.CreatePlace) ||
            (matchesUpdate && contribution.Type == PlaceContributionType.UpdatePlace) ||
            (matchesClaim && contribution.Type == PlaceContributionType.ClaimOwnership));
    }

    private static IQueryable<PlaceContribution> ApplyContributionSort(
        IQueryable<PlaceContribution> query,
        string sort)
    {
        return sort switch
        {
            "oldest" => query.OrderBy(contribution => contribution.SubmittedAt).ThenBy(contribution => contribution.Id),
            "status" => query.OrderBy(contribution => contribution.Status).ThenByDescending(contribution => contribution.SubmittedAt),
            "type" => query.OrderBy(contribution => contribution.Type).ThenByDescending(contribution => contribution.SubmittedAt),
            _ => query.OrderByDescending(contribution => contribution.SubmittedAt).ThenByDescending(contribution => contribution.Id)
        };
    }

    private async Task<List<RegionalRequestQueueItemDto>> ProjectContributions(
        IQueryable<PlaceContribution> query,
        int take)
    {
        var rows = await query
            .Take(Math.Max(1, take))
            .Select(contribution => new ContributionQueueRow
            {
                Id = contribution.Id,
                Title = contribution.Name,
                ContributionType = contribution.Type,
                Status = contribution.Status,
                RegionId = contribution.Location!.Destination.RegionId,
                RegionName = contribution.Location.Destination.Region.Name,
                TerritoryName = contribution.Location.Destination.Region.Territory.Name,
                UserId = contribution.UserId,
                UserDisplayName = contribution.User.DisplayName,
                UserFirstName = contribution.User.FirstName,
                UserLastName = contribution.User.LastName,
                Username = contribution.User.Username,
                SubmittedAt = contribution.SubmittedAt,
                ThumbnailImageUrl = contribution.Images
                    .OrderBy(image => image.ImageRole == EntityImageRole.Card ? 0 : image.ImageRole == EntityImageRole.Cover ? 1 : 2)
                    .ThenBy(image => image.SortOrder)
                    .ThenBy(image => image.Id)
                    .Select(image => image.Url)
                    .FirstOrDefault(),
                PreviewText = contribution.Description
            })
            .ToListAsync();

        return rows.Select(MapContributionRow).ToList();
    }

    private async Task<RegionalRequestQueueDetailDto> GetContributionDetailAsync(int regionId, int requestId)
    {
        var contribution = await _db.PlaceContributions
            .AsNoTracking()
            .Include(item => item.User)
            .Include(item => item.PlaceType)
            .Include(item => item.StayType)
            .Include(item => item.Images)
            .Include(item => item.Location)
                .ThenInclude(location => location!.Destination)
                    .ThenInclude(destination => destination.Region)
                        .ThenInclude(region => region.Territory)
            .Include(item => item.Place)
            .Where(item =>
                item.Id == requestId &&
                item.LocationId.HasValue &&
                item.Location != null &&
                item.Location.Destination.RegionId == regionId &&
                (item.Type != PlaceContributionType.UpdatePlace ||
                 (item.PlaceId.HasValue &&
                  item.Place != null &&
                  item.Place.Location.Destination.RegionId == regionId &&
                  item.Place.Location.Destination.RegionId == item.Location.Destination.RegionId)))
            .SingleOrDefaultAsync()
            ?? throw new KeyNotFoundException("Request not found.");

        var baseItem = MapContributionEntity(contribution);

        return new RegionalRequestQueueDetailDto
        {
            RequestKey = baseItem.RequestKey,
            RequestId = baseItem.RequestId,
            DisplayRequestId = baseItem.DisplayRequestId,
            Source = baseItem.Source,
            Title = baseItem.Title,
            EntityType = baseItem.EntityType,
            ContributionType = baseItem.ContributionType,
            RegionId = baseItem.RegionId,
            RegionDisplayName = baseItem.RegionDisplayName,
            SubmittedByUserId = baseItem.SubmittedByUserId,
            SubmittedByDisplayName = baseItem.SubmittedByDisplayName,
            SubmittedByInitials = baseItem.SubmittedByInitials,
            SubmittedAt = baseItem.SubmittedAt,
            Status = baseItem.Status,
            ThumbnailImageUrl = baseItem.ThumbnailImageUrl,
            PreviewText = baseItem.PreviewText,
            DetailRoute = baseItem.DetailRoute,
            Description = DisplayTextSafety.CleanOptionalRichMultiline(contribution.Description) ?? string.Empty,
            Address = contribution.Address,
            OpeningHours = contribution.OpeningHours,
            OperatingHoursEnabled = contribution.OperatingHoursEnabled,
            OperatingHours = DeserializeOperatingHours(contribution.OperatingHoursJson),
            LocationName = contribution.Location?.Name,
            DestinationName = contribution.Location?.Destination?.Name,
            Country = contribution.Location?.Destination?.Region?.Territory?.Name,
            Latitude = contribution.Latitude,
            Longitude = contribution.Longitude,
            ContactEmail = contribution.ContactEmail,
            PhoneNumber = contribution.PhoneNumber,
            Website = contribution.Website,
            PlaceType = contribution.PlaceType?.Name,
            StayType = contribution.StayType?.Name,
            TargetEntityId = contribution.PlaceId,
            TargetEntityName = contribution.Place?.Name,
            Tags = DeserializeStringList(contribution.TagsJson),
            Amenities = DeserializeStringList(contribution.AmenitiesJson),
            StayAmenities = DeserializeStringList(contribution.StayAmenitiesJson),
            BookingPartners = DeserializeBookingPartners(contribution.BookingPartnersJson),
            Images = contribution.Images
                .OrderBy(image => image.SortOrder)
                .ThenBy(image => image.Id)
                .Select(image => new PlaceContributionImageDto
                {
                    Id = image.Id,
                    Url = image.Url,
                    AltText = image.AltText,
                    Caption = image.Caption,
                    IsMain = image.IsMain,
                    ImageRole = image.ImageRole,
                    SortOrder = image.SortOrder,
                    ImagePosition = image.ImagePosition,
                    ImageSize = image.ImageSize
                })
                .ToList(),
            HasOwnershipProofDocument = !string.IsNullOrWhiteSpace(contribution.OwnershipProofDocumentUrl),
            OwnershipProofDocumentUrl = contribution.OwnershipProofDocumentUrl,
            OwnershipProofOriginalFileName = contribution.OwnershipProofOriginalFileName,
            OwnershipProofContentType = contribution.OwnershipProofContentType,
            OwnershipProofUploadedAt = contribution.OwnershipProofUploadedAt,
            DecisionNote = contribution.ReviewNote
        };
    }

    private async Task<RegionalRequestQueueItemDto> GetContributionItemAsync(int regionId, int requestId)
    {
        var rows = await ProjectContributions(
            BuildContributionQuery(regionId).Where(contribution => contribution.Id == requestId),
            1);

        return rows.SingleOrDefault() ?? throw new KeyNotFoundException("Request not found.");
    }

    private async Task EnsureContributionExistsInRegionAsync(int regionId, int requestId)
    {
        var exists = await BuildContributionQuery(regionId)
            .AnyAsync(contribution => contribution.Id == requestId);

        if (!exists)
            throw new KeyNotFoundException("Request not found.");
    }

    private async Task EnsureAssignedRegionAsync(int userId, int regionId)
    {
        if (regionId <= 0)
            throw new InvalidOperationException("regionId is required.");

        await EnsureRegionalManagerRoleAsync(userId);

        var assigned = await _db.RegionalManagerRegions
            .AsNoTracking()
            .AnyAsync(assignment =>
                assignment.UserId == userId &&
                assignment.RegionId == regionId &&
                assignment.IsActive);

        if (!assigned)
            throw new UnauthorizedAccessException("Region is not assigned to the current regional manager.");
    }

    private async Task EnsureRegionalManagerRoleAsync(int userId)
    {
        var roles = await _db.UserRoles
            .AsNoTracking()
            .Where(userRole => userRole.UserId == userId)
            .Select(userRole => userRole.Role.Name)
            .ToListAsync();

        if (!roles.Any(role => RoleNames.Normalize(role) == RoleNames.RegionalManager))
            throw new UnauthorizedAccessException("Regional manager role is required.");
    }

    private async Task<int> CountByStatusAsync(int regionId, string status, string? search)
    {
        return await CountContributionsAsync(regionId, status, search);
    }

    private async Task<int> CountContributionsAsync(int regionId, string status, string? search)
    {
        var query = ApplyContributionStatus(BuildContributionQuery(regionId), status);
        return await ApplyContributionSearch(query, search).CountAsync();
    }

    private static RegionalRequestQueueItemDto MapContributionRow(ContributionQueueRow row)
    {
        var displayName = BuildDisplayName(
            row.UserDisplayName,
            row.UserFirstName,
            row.UserLastName,
            row.Username);

        return new RegionalRequestQueueItemDto
        {
            RequestKey = BuildContributionRequestKey(row.Id),
            RequestId = row.Id,
            DisplayRequestId = BuildContributionDisplayId(row.Id),
            Source = ContributionSource,
            Title = row.Title,
            EntityType = "Place",
            ContributionType = row.ContributionType.ToString(),
            RegionId = row.RegionId,
            RegionDisplayName = BuildRegionDisplayName(row.RegionName, row.TerritoryName),
            SubmittedByUserId = row.UserId,
            SubmittedByDisplayName = displayName,
            SubmittedByInitials = BuildInitials(displayName),
            SubmittedAt = row.SubmittedAt,
            Status = MapContributionStatus(row.Status),
            ThumbnailImageUrl = row.ThumbnailImageUrl,
            PreviewText = row.PreviewText,
            DetailRoute = BuildDetailRoute(row.Id)
        };
    }

    private static RegionalRequestQueueItemDto MapContributionEntity(PlaceContribution contribution)
    {
        var displayName = BuildDisplayName(
            contribution.User.DisplayName,
            contribution.User.FirstName,
            contribution.User.LastName,
            contribution.User.Username);

        return new RegionalRequestQueueItemDto
        {
            RequestKey = BuildContributionRequestKey(contribution.Id),
            RequestId = contribution.Id,
            DisplayRequestId = BuildContributionDisplayId(contribution.Id),
            Source = ContributionSource,
            Title = contribution.Name,
            EntityType = "Place",
            ContributionType = contribution.Type.ToString(),
            RegionId = contribution.Location!.Destination.RegionId,
            RegionDisplayName = BuildRegionDisplayName(
                contribution.Location.Destination.Region.Name,
                contribution.Location.Destination.Region.Territory.Name),
            SubmittedByUserId = contribution.UserId,
            SubmittedByDisplayName = displayName,
            SubmittedByInitials = BuildInitials(displayName),
            SubmittedAt = contribution.SubmittedAt,
            Status = MapContributionStatus(contribution.Status),
            ThumbnailImageUrl = contribution.Images
                .OrderBy(image => image.ImageRole == EntityImageRole.Card ? 0 : image.ImageRole == EntityImageRole.Cover ? 1 : 2)
                .ThenBy(image => image.SortOrder)
                .ThenBy(image => image.Id)
                .Select(image => image.Url)
                .FirstOrDefault(),
            PreviewText = contribution.Description,
            DetailRoute = BuildDetailRoute(contribution.Id)
        };
    }

    private static IOrderedEnumerable<RegionalRequestQueueItemDto> SortItems(
        IEnumerable<RegionalRequestQueueItemDto> items,
        string sort)
    {
        return sort switch
        {
            "oldest" => items.OrderBy(item => item.SubmittedAt).ThenBy(item => item.RequestKey, StringComparer.Ordinal),
            "status" => items.OrderBy(item => item.Status, StringComparer.Ordinal)
                .ThenByDescending(item => item.SubmittedAt),
            "type" => items.OrderBy(item => item.EntityType, StringComparer.Ordinal)
                .ThenByDescending(item => item.SubmittedAt),
            _ => items.OrderByDescending(item => item.SubmittedAt).ThenByDescending(item => item.RequestKey, StringComparer.Ordinal)
        };
    }

    private static string? NormalizeType(string? value)
    {
        var normalized = NormalizeToken(value ?? "all");
        return normalized switch
        {
            "" or "all" or "allrequests" => "all",
            "destination" or "destinations" => "destination",
            "location" or "locations" => "location",
            "place" or "places" => "place",
            "event" or "events" => "event",
            _ => throw new InvalidOperationException("Invalid type filter.")
        };
    }

    private static string? NormalizeStatus(string? value)
    {
        var normalized = NormalizeToken(value ?? "pending");
        return normalized switch
        {
            "" or "pending" or "allrequests" => "pending",
            "approved" => "approved",
            "rejected" => "rejected",
            "all" => "all",
            _ => throw new InvalidOperationException("Invalid status filter.")
        };
    }

    private static string NormalizeSort(string? value)
    {
        var normalized = NormalizeToken(value ?? "newest");
        return normalized switch
        {
            "" or "newest" or "newestfirst" => "newest",
            "oldest" or "oldestfirst" => "oldest",
            "status" => "status",
            "type" => "type",
            _ => "newest"
        };
    }

    private static string NormalizeSource(string value)
    {
        var normalized = NormalizeToken(value);
        return normalized switch
        {
            "contribution" or "contributions" => ContributionSource,
            _ => throw new InvalidOperationException("Invalid request source.")
        };
    }

    private static string NormalizeToken(string value) =>
        value.Trim()
            .Replace("-", string.Empty, StringComparison.Ordinal)
            .Replace("_", string.Empty, StringComparison.Ordinal)
            .Replace(" ", string.Empty, StringComparison.Ordinal)
            .ToLowerInvariant();

    private static int? ExtractNumericSearchTerm(string value)
    {
        var digits = new string(value.Where(char.IsDigit).ToArray());
        return int.TryParse(digits, out var parsed) ? parsed : null;
    }

    private static bool TryParseRequestKey(string requestKey, out string source, out int id)
    {
        source = string.Empty;
        id = 0;

        var parts = requestKey.Split(':', 2, StringSplitOptions.TrimEntries);
        if (parts.Length != 2 || !int.TryParse(parts[1], out id) || id <= 0)
            return false;

        try
        {
            source = NormalizeSource(parts[0]);
            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private static string BuildDecisionNote(string? reason, string? note)
    {
        var parts = new List<string>();

        if (!string.IsNullOrWhiteSpace(reason))
            parts.Add("Reason: " + reason.Trim());

        if (!string.IsNullOrWhiteSpace(note))
            parts.Add(note.Trim());

        return string.Join(Environment.NewLine, parts);
    }

    private static string SafeActionError(Exception ex)
    {
        return ex switch
        {
            UnauthorizedAccessException => "Forbidden.",
            KeyNotFoundException => "Request not found.",
            InvalidOperationException => ex.Message,
            _ => "Unable to process request."
        };
    }

    private static string BuildDisplayName(string? displayName, string? firstName, string? lastName, string? username)
    {
        if (!string.IsNullOrWhiteSpace(displayName))
            return displayName.Trim();

        var fullName = string.Join(" ", new[] { firstName, lastName }
            .Where(part => !string.IsNullOrWhiteSpace(part))
            .Select(part => part!.Trim()));

        return string.IsNullOrWhiteSpace(fullName)
            ? username?.Trim() ?? "User"
            : fullName;
    }

    private static string? BuildInitials(string displayName)
    {
        var parts = displayName.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
            return null;

        return string.Concat(parts.Take(2).Select(part => char.ToUpperInvariant(part[0])));
    }

    private static string BuildRegionDisplayName(string regionName, string? territoryName)
    {
        return string.IsNullOrWhiteSpace(territoryName)
            ? regionName
            : regionName + ", " + territoryName;
    }

    private static string MapContributionStatus(PlaceContributionStatus status) =>
        status == PlaceContributionStatus.PendingReview ? "Pending" : status.ToString();

    private static string BuildContributionRequestKey(int id) => "contribution:" + id;
    private static string BuildContributionDisplayId(int id) => "CON-" + id.ToString("D4");

    private static string BuildDetailRoute(int id)
    {
        return $"/request/detail/contribution/{id}";
    }

    private static List<string> DeserializeStringList(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return new List<string>();

        try
        {
            return JsonSerializer.Deserialize<List<string>>(json) ?? new List<string>();
        }
        catch (JsonException)
        {
            return new List<string>();
        }
    }

    private static List<ContributionBookingPartnerDto> DeserializeBookingPartners(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return new List<ContributionBookingPartnerDto>();

        try
        {
            return JsonSerializer.Deserialize<List<ContributionBookingPartnerDto>>(json)
                ?? new List<ContributionBookingPartnerDto>();
        }
        catch (JsonException)
        {
            return new List<ContributionBookingPartnerDto>();
        }
    }

    private static List<ContributionOperatingHourDto> DeserializeOperatingHours(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return new List<ContributionOperatingHourDto>();

        try
        {
            return JsonSerializer.Deserialize<List<ContributionOperatingHourDto>>(json,
                new JsonSerializerOptions(JsonSerializerDefaults.Web))
                ?? new List<ContributionOperatingHourDto>();
        }
        catch (JsonException)
        {
            return new List<ContributionOperatingHourDto>();
        }
    }

    private sealed class ContributionQueueRow
    {
        public int Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public PlaceContributionType ContributionType { get; set; }
        public PlaceContributionStatus Status { get; set; }
        public int RegionId { get; set; }
        public string RegionName { get; set; } = string.Empty;
        public string? TerritoryName { get; set; }
        public int UserId { get; set; }
        public string? UserDisplayName { get; set; }
        public string? UserFirstName { get; set; }
        public string? UserLastName { get; set; }
        public string Username { get; set; } = string.Empty;
        public DateTime SubmittedAt { get; set; }
        public string? ThumbnailImageUrl { get; set; }
        public string? PreviewText { get; set; }
    }
}
