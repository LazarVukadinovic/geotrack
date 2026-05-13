using Geotrack.Application.DTOs.Admin;
using Geotrack.Application.Interfaces.Service;
using Geotrack.Application.Security;
using Geotrack.Domain.Entities.Core;
using Microsoft.EntityFrameworkCore;

namespace Geotrack.Infrastructure.Services;

public class RegionalLocationService : IRegionalLocationService
{
    private const int DefaultTake = 25;
    private const int MaxTake = 50;
    private const string PublishedStatus = "Published";
    private const string ExistingEditRoute = "/locations/new";

    private readonly AppDbContext _db;

    public RegionalLocationService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<RegionalLocationListResponseDto> GetLocationsAsync(
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

        var query = _db.Locations
            .AsNoTracking()
            .Where(location => location.Destination.RegionId == regionId);

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
            .Select(location => new LocationProjection
            {
                Id = location.Id,
                Name = location.Name,
                DestinationId = location.DestinationId,
                DestinationName = location.Destination.Name,
                RegionId = location.Destination.RegionId,
                RegionName = location.Destination.Region.Name,
                TerritoryName = location.Destination.Region.Territory.Name,
                LocationTypeId = location.LocationTypeId,
                LocationTypeName = location.LocationType.Name,
                CreatedAt = location.CreatedAt,
                UpdatedAt = location.UpdatedAt,
                Latitude = location.Latitude,
                Longitude = location.Longitude,
                ImageUrl = location.Images
                    .Where(image => image.TargetType == EntityImageTargetType.Location)
                    .OrderByDescending(image => image.IsMain)
                    .ThenBy(image => image.SortOrder)
                    .ThenBy(image => image.CreatedAt)
                    .ThenBy(image => image.Id)
                    .Select(image => image.Url)
                    .FirstOrDefault()
            })
            .ToListAsync();

        return new RegionalLocationListResponseDto
        {
            Items = rows.Select(MapListItem).ToList(),
            TotalCount = totalCount,
            Skip = normalizedSkip,
            Take = normalizedTake
        };
    }

    public async Task<RegionalLocationDetailDto> GetLocationAsync(int userId, int regionId, int locationId)
    {
        await EnsureAssignedRegionAsync(userId, regionId);

        var row = await _db.Locations
            .AsNoTracking()
            .Where(location => location.Id == locationId && location.Destination.RegionId == regionId)
            .Select(location => new LocationProjection
            {
                Id = location.Id,
                Name = location.Name,
                DestinationId = location.DestinationId,
                DestinationName = location.Destination.Name,
                RegionId = location.Destination.RegionId,
                RegionName = location.Destination.Region.Name,
                TerritoryName = location.Destination.Region.Territory.Name,
                LocationTypeId = location.LocationTypeId,
                LocationTypeName = location.LocationType.Name,
                CreatedAt = location.CreatedAt,
                UpdatedAt = location.UpdatedAt,
                Latitude = location.Latitude,
                Longitude = location.Longitude,
                ImageUrl = location.Images
                    .Where(image => image.TargetType == EntityImageTargetType.Location)
                    .OrderByDescending(image => image.IsMain)
                    .ThenBy(image => image.SortOrder)
                    .ThenBy(image => image.CreatedAt)
                    .ThenBy(image => image.Id)
                    .Select(image => image.Url)
                    .FirstOrDefault()
            })
            .SingleOrDefaultAsync()
            ?? throw new KeyNotFoundException("Location not found.");

        return MapDetail(row);
    }

    public async Task<RegionalLocationDetailDto> UpdateLocationAsync(
        int userId,
        int regionId,
        int locationId,
        RegionalLocationUpdateRequestDto request)
    {
        await EnsureAssignedRegionAsync(userId, regionId);

        var location = await _db.Locations
            .Include(item => item.Destination)
            .SingleOrDefaultAsync(item => item.Id == locationId && item.Destination.RegionId == regionId)
            ?? throw new KeyNotFoundException("Location not found.");

        if (request.DestinationId.HasValue)
        {
            var destination = await _db.Destinations
                .SingleOrDefaultAsync(item =>
                    item.Id == request.DestinationId.Value &&
                    item.RegionId == regionId)
                ?? throw new InvalidOperationException("Destination is invalid for the selected region.");

            location.DestinationId = destination.Id;
        }

        if (request.LocationTypeId.HasValue &&
            !await _db.LocationTypes.AnyAsync(item => item.Id == request.LocationTypeId.Value))
        {
            throw new InvalidOperationException("Location type is invalid.");
        }

        ApplyUpdate(location, request);
        location.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();
        return await GetLocationAsync(userId, regionId, locationId);
    }

    public async Task DeleteLocationAsync(int userId, int regionId, int locationId)
    {
        await EnsureAssignedRegionAsync(userId, regionId);

        var location = await _db.Locations
            .SingleOrDefaultAsync(item => item.Id == locationId && item.Destination.RegionId == regionId)
            ?? throw new KeyNotFoundException("Location not found.");

        if (await HasBlockingDependenciesAsync(locationId))
        {
            throw new InvalidOperationException("This location cannot be deleted while it has related content.");
        }

        _db.Locations.Remove(location);
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

    private async Task<bool> HasBlockingDependenciesAsync(int locationId)
    {
        if (await _db.Places.AsNoTracking().AnyAsync(place => place.LocationId == locationId))
            return true;

        if (await _db.Events.AsNoTracking().AnyAsync(@event => @event.LocationId == locationId))
            return true;

        if (await _db.LocationReviews.AsNoTracking().AnyAsync(review => review.LocationId == locationId))
            return true;

        if (await _db.PlaceContributions.AsNoTracking().AnyAsync(contribution => contribution.LocationId == locationId))
            return true;

        return await _db.CollectionItems
            .AsNoTracking()
            .AnyAsync(item => item.LocationId == locationId);
    }

    private static IQueryable<Location> ApplySearch(IQueryable<Location> query, string? search)
    {
        if (string.IsNullOrWhiteSpace(search))
            return query;

        var term = search.Trim().ToLowerInvariant();
        return query.Where(location =>
            location.Name.ToLower().Contains(term) ||
            location.LocationType.Name.ToLower().Contains(term) ||
            location.Destination.Name.ToLower().Contains(term) ||
            location.Destination.Description.ToLower().Contains(term) ||
            location.Destination.Region.Name.ToLower().Contains(term) ||
            location.Destination.Region.Territory.Name.ToLower().Contains(term));
    }

    private static IQueryable<Location> ApplySort(IQueryable<Location> query, string sort)
    {
        return sort switch
        {
            "oldest" => query
                .OrderBy(location => location.UpdatedAt ?? location.CreatedAt)
                .ThenBy(location => location.Id),
            "nameasc" => query
                .OrderBy(location => location.Name)
                .ThenBy(location => location.Id),
            "namedesc" => query
                .OrderByDescending(location => location.Name)
                .ThenByDescending(location => location.Id),
            _ => query
                .OrderByDescending(location => location.UpdatedAt ?? location.CreatedAt)
                .ThenByDescending(location => location.Id),
        };
    }

    private static void ApplyUpdate(Location location, RegionalLocationUpdateRequestDto request)
    {
        if (request.Name is not null)
        {
            var name = request.Name.Trim();
            if (name.Length == 0)
                throw new InvalidOperationException("Location name is required.");
            if (name.Length > 200)
                throw new InvalidOperationException("Location name is too long.");

            location.Name = name;
        }

        if (request.LocationTypeId.HasValue)
        {
            if (request.LocationTypeId.Value <= 0)
                throw new InvalidOperationException("Location type is invalid.");

            location.LocationTypeId = request.LocationTypeId.Value;
        }

        if (request.Latitude.HasValue)
        {
            if (request.Latitude.Value is < -90 or > 90)
                throw new InvalidOperationException("Latitude is invalid.");

            location.Latitude = request.Latitude.Value;
        }

        if (request.Longitude.HasValue)
        {
            if (request.Longitude.Value is < -180 or > 180)
                throw new InvalidOperationException("Longitude is invalid.");

            location.Longitude = request.Longitude.Value;
        }
    }

    private static RegionalLocationListItemDto MapListItem(LocationProjection row)
    {
        return new RegionalLocationListItemDto
        {
            Id = row.Id,
            Name = row.Name,
            Summary = BuildSummary(row.LocationTypeName, row.DestinationName),
            Description = null,
            LocationType = row.LocationTypeName,
            LocationTypeId = row.LocationTypeId,
            Status = PublishedStatus,
            RegionId = row.RegionId,
            RegionDisplayName = BuildRegionDisplayName(row.RegionName, row.TerritoryName),
            DestinationId = row.DestinationId,
            DestinationName = row.DestinationName,
            CreatedAt = row.CreatedAt,
            LastUpdatedAt = row.UpdatedAt ?? row.CreatedAt,
            ImageUrl = row.ImageUrl,
            DetailRoute = "/locations/detail/" + row.Id,
            EditRoute = ExistingEditRoute
        };
    }

    private static RegionalLocationDetailDto MapDetail(LocationProjection row)
    {
        var item = MapListItem(row);
        return new RegionalLocationDetailDto
        {
            Id = item.Id,
            Name = item.Name,
            Summary = item.Summary,
            Description = item.Description,
            LocationType = item.LocationType,
            LocationTypeId = item.LocationTypeId,
            Status = item.Status,
            RegionId = item.RegionId,
            RegionDisplayName = item.RegionDisplayName,
            DestinationId = item.DestinationId,
            DestinationName = item.DestinationName,
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

    private static string? BuildSummary(string? locationTypeName, string destinationName)
    {
        if (string.IsNullOrWhiteSpace(destinationName))
            return locationTypeName;

        if (string.IsNullOrWhiteSpace(locationTypeName))
            return "Part of " + destinationName;

        return locationTypeName + " in " + destinationName;
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

    private sealed class LocationProjection
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public int DestinationId { get; set; }
        public string DestinationName { get; set; } = string.Empty;
        public int RegionId { get; set; }
        public string RegionName { get; set; } = string.Empty;
        public string? TerritoryName { get; set; }
        public int LocationTypeId { get; set; }
        public string? LocationTypeName { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public string? ImageUrl { get; set; }
    }
}
