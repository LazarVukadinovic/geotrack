using Geotrack.Application.DTOs.Admin;
using Geotrack.Application.Interfaces.Service;
using Geotrack.Application.Security;
using Geotrack.Domain.Entities.Core;
using Microsoft.EntityFrameworkCore;

namespace Geotrack.Infrastructure.Services;

public class RegionalDestinationService : IRegionalDestinationService
{
    private const int DefaultTake = 25;
    private const int MaxTake = 50;
    private const string PublishedStatus = "Published";
    private const string ExistingEditRoute = "/destinations/new";

    private readonly AppDbContext _db;

    public RegionalDestinationService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<RegionalDestinationListResponseDto> GetDestinationsAsync(
        int userId,
        int regionId,
        string? search,
        string? status,
        string? sort,
        int skip,
        int take)
    {
        await EnsureAssignedRegionAsync(userId, regionId);

        var normalizedSkip = NormalizeSkip(skip);
        var normalizedTake = NormalizeTake(take);
        var normalizedStatus = NormalizeStatus(status);
        var normalizedSort = NormalizeSort(sort);

        var query = _db.Destinations
            .AsNoTracking()
            .Where(destination => destination.RegionId == regionId);

        if (normalizedStatus is "draft" or "archived")
        {
            query = query.Where(_ => false);
        }

        query = ApplySearch(query, search);
        query = ApplySort(query, normalizedSort);

        var totalCount = await query.CountAsync();
        var rows = await query
            .Skip(normalizedSkip)
            .Take(normalizedTake)
            .Select(destination => new DestinationProjection
            {
                Id = destination.Id,
                Name = destination.Name,
                Description = destination.Description,
                RegionId = destination.RegionId,
                RegionName = destination.Region.Name,
                TerritoryName = destination.Region.Territory.Name,
                DestinationTypeId = destination.DestinationTypeId,
                DestinationTypeName = destination.DestinationType.Name,
                CreatedAt = destination.CreatedAt,
                UpdatedAt = destination.UpdatedAt,
                Latitude = destination.Latitude,
                Longitude = destination.Longitude,
                ImageUrl = destination.Images
                    .Where(image => image.TargetType == EntityImageTargetType.Destination)
                    .OrderByDescending(image => image.IsMain)
                    .ThenBy(image => image.SortOrder)
                    .ThenBy(image => image.CreatedAt)
                    .ThenBy(image => image.Id)
                    .Select(image => image.Url)
                    .FirstOrDefault()
            })
            .ToListAsync();

        return new RegionalDestinationListResponseDto
        {
            Items = rows.Select(MapListItem).ToList(),
            TotalCount = totalCount,
            Skip = normalizedSkip,
            Take = normalizedTake
        };
    }

    public async Task<RegionalDestinationDetailDto> GetDestinationAsync(int userId, int regionId, int destinationId)
    {
        await EnsureAssignedRegionAsync(userId, regionId);

        var row = await _db.Destinations
            .AsNoTracking()
            .Where(destination => destination.Id == destinationId && destination.RegionId == regionId)
            .Select(destination => new DestinationProjection
            {
                Id = destination.Id,
                Name = destination.Name,
                Description = destination.Description,
                RegionId = destination.RegionId,
                RegionName = destination.Region.Name,
                TerritoryName = destination.Region.Territory.Name,
                DestinationTypeId = destination.DestinationTypeId,
                DestinationTypeName = destination.DestinationType.Name,
                CreatedAt = destination.CreatedAt,
                UpdatedAt = destination.UpdatedAt,
                Latitude = destination.Latitude,
                Longitude = destination.Longitude,
                ImageUrl = destination.Images
                    .Where(image => image.TargetType == EntityImageTargetType.Destination)
                    .OrderByDescending(image => image.IsMain)
                    .ThenBy(image => image.SortOrder)
                    .ThenBy(image => image.CreatedAt)
                    .ThenBy(image => image.Id)
                    .Select(image => image.Url)
                    .FirstOrDefault()
            })
            .SingleOrDefaultAsync()
            ?? throw new KeyNotFoundException("Destination not found.");

        return MapDetail(row);
    }

    public async Task<RegionalDestinationDetailDto> UpdateDestinationAsync(
        int userId,
        int regionId,
        int destinationId,
        RegionalDestinationUpdateRequestDto request)
    {
        await EnsureAssignedRegionAsync(userId, regionId);

        var destination = await _db.Destinations
            .SingleOrDefaultAsync(item => item.Id == destinationId && item.RegionId == regionId)
            ?? throw new KeyNotFoundException("Destination not found.");

        if (request.DestinationTypeId.HasValue &&
            !await _db.DestinationTypes.AnyAsync(item => item.Id == request.DestinationTypeId.Value))
        {
            throw new InvalidOperationException("Destination type is invalid.");
        }

        ApplyUpdate(destination, request);
        destination.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();
        return await GetDestinationAsync(userId, regionId, destinationId);
    }

    public async Task DeleteDestinationAsync(int userId, int regionId, int destinationId)
    {
        await EnsureAssignedRegionAsync(userId, regionId);

        var destination = await _db.Destinations
            .SingleOrDefaultAsync(item => item.Id == destinationId && item.RegionId == regionId)
            ?? throw new KeyNotFoundException("Destination not found.");

        if (await HasBlockingDependenciesAsync(destinationId))
        {
            throw new InvalidOperationException("This destination cannot be deleted while it has related content.");
        }

        _db.Destinations.Remove(destination);
        await _db.SaveChangesAsync();
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

    private async Task<bool> HasBlockingDependenciesAsync(int destinationId)
    {
        var hasLocations = await _db.Locations
            .AsNoTracking()
            .AnyAsync(location => location.DestinationId == destinationId);
        if (hasLocations)
            return true;

        var hasReviews = await _db.DestinationReviews
            .AsNoTracking()
            .AnyAsync(review => review.DestinationId == destinationId);
        if (hasReviews)
            return true;

        return await _db.CollectionItems
            .AsNoTracking()
            .AnyAsync(item => item.DestinationId == destinationId);
    }

    private static IQueryable<Destination> ApplySearch(IQueryable<Destination> query, string? search)
    {
        if (string.IsNullOrWhiteSpace(search))
            return query;

        var term = search.Trim().ToLowerInvariant();
        return query.Where(destination =>
            destination.Name.ToLower().Contains(term) ||
            destination.Description.ToLower().Contains(term) ||
            destination.Region.Name.ToLower().Contains(term) ||
            destination.Region.Territory.Name.ToLower().Contains(term) ||
            destination.DestinationType.Name.ToLower().Contains(term));
    }

    private static IQueryable<Destination> ApplySort(IQueryable<Destination> query, string sort)
    {
        return sort switch
        {
            "oldest" => query
                .OrderBy(destination => destination.UpdatedAt ?? destination.CreatedAt)
                .ThenBy(destination => destination.Id),
            "nameasc" => query
                .OrderBy(destination => destination.Name)
                .ThenBy(destination => destination.Id),
            "namedesc" => query
                .OrderByDescending(destination => destination.Name)
                .ThenByDescending(destination => destination.Id),
            _ => query
                .OrderByDescending(destination => destination.UpdatedAt ?? destination.CreatedAt)
                .ThenByDescending(destination => destination.Id),
        };
    }

    private static void ApplyUpdate(Destination destination, RegionalDestinationUpdateRequestDto request)
    {
        if (request.Name is not null)
        {
            var name = request.Name.Trim();
            if (name.Length == 0)
                throw new InvalidOperationException("Destination name is required.");
            if (name.Length > 200)
                throw new InvalidOperationException("Destination name is too long.");

            destination.Name = name;
        }

        if (request.Description is not null)
        {
            var description = request.Description.Trim();
            if (description.Length > 2000)
                throw new InvalidOperationException("Description is too long.");

            destination.Description = description;
        }

        if (request.DestinationTypeId.HasValue)
        {
            if (request.DestinationTypeId.Value <= 0)
                throw new InvalidOperationException("Destination type is invalid.");

            destination.DestinationTypeId = request.DestinationTypeId.Value;
        }

        if (request.Latitude.HasValue)
        {
            if (request.Latitude.Value is < -90 or > 90)
                throw new InvalidOperationException("Latitude is invalid.");

            destination.Latitude = request.Latitude.Value;
        }

        if (request.Longitude.HasValue)
        {
            if (request.Longitude.Value is < -180 or > 180)
                throw new InvalidOperationException("Longitude is invalid.");

            destination.Longitude = request.Longitude.Value;
        }
    }

    private static RegionalDestinationListItemDto MapListItem(DestinationProjection row)
    {
        return new RegionalDestinationListItemDto
        {
            Id = row.Id,
            Name = row.Name,
            Summary = BuildSummary(row.Description),
            Description = row.Description,
            Status = PublishedStatus,
            RegionId = row.RegionId,
            RegionDisplayName = BuildRegionDisplayName(row.RegionName, row.TerritoryName),
            DestinationTypeId = row.DestinationTypeId,
            DestinationTypeName = row.DestinationTypeName,
            CreatedAt = row.CreatedAt,
            LastUpdatedAt = row.UpdatedAt ?? row.CreatedAt,
            ImageUrl = row.ImageUrl,
            DetailRoute = "/destinations/detail/" + row.Id,
            EditRoute = ExistingEditRoute
        };
    }

    private static RegionalDestinationDetailDto MapDetail(DestinationProjection row)
    {
        var item = MapListItem(row);
        return new RegionalDestinationDetailDto
        {
            Id = item.Id,
            Name = item.Name,
            Summary = item.Summary,
            Description = item.Description,
            Status = item.Status,
            RegionId = item.RegionId,
            RegionDisplayName = item.RegionDisplayName,
            DestinationTypeId = item.DestinationTypeId,
            DestinationTypeName = item.DestinationTypeName,
            CreatedAt = item.CreatedAt,
            LastUpdatedAt = item.LastUpdatedAt,
            ImageUrl = item.ImageUrl,
            DetailRoute = item.DetailRoute,
            EditRoute = item.EditRoute,
            Latitude = row.Latitude,
            Longitude = row.Longitude
        };
    }

    private static string BuildRegionDisplayName(string regionName, string? territoryName)
    {
        return string.IsNullOrWhiteSpace(territoryName)
            ? regionName
            : regionName + ", " + territoryName;
    }

    private static string? BuildSummary(string? description)
    {
        if (string.IsNullOrWhiteSpace(description))
            return null;

        var normalized = string.Join(" ", description.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return normalized.Length <= 160 ? normalized : normalized[..157] + "...";
    }

    private static int NormalizeSkip(int skip)
    {
        if (skip < 0)
            throw new InvalidOperationException("skip must be zero or greater.");

        return skip;
    }

    private static int NormalizeTake(int take)
    {
        if (take <= 0)
            throw new InvalidOperationException("take must be greater than zero.");

        return Math.Clamp(take, 1, MaxTake);
    }

    private static string NormalizeStatus(string? status)
    {
        var normalized = NormalizeToken(status ?? "all");
        return normalized switch
        {
            "all" or "published" or "draft" or "archived" => normalized,
            _ => throw new InvalidOperationException("Invalid status filter.")
        };
    }

    private static string NormalizeSort(string? sort)
    {
        var normalized = NormalizeToken(sort ?? "newest");
        return normalized switch
        {
            "oldest" => "oldest",
            "nameaz" or "nameasc" => "nameasc",
            "nameza" or "namedesc" => "namedesc",
            _ => "newest"
        };
    }

    private static string NormalizeToken(string value) =>
        value.Replace(" ", string.Empty, StringComparison.Ordinal)
            .Replace("-", string.Empty, StringComparison.Ordinal)
            .Replace("_", string.Empty, StringComparison.Ordinal)
            .ToLowerInvariant();

    private sealed class DestinationProjection
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public int RegionId { get; set; }
        public string RegionName { get; set; } = string.Empty;
        public string? TerritoryName { get; set; }
        public int DestinationTypeId { get; set; }
        public string? DestinationTypeName { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public string? ImageUrl { get; set; }
    }
}
