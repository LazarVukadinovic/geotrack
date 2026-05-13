using Geotrack.Application.DTOs.Admin;
using Geotrack.Application.Interfaces.Service;
using Geotrack.Application.Security;
using Geotrack.Domain.Entities.Core;
using Geotrack.Domain.Entities.Reviews;
using Microsoft.EntityFrameworkCore;

namespace Geotrack.Infrastructure.Services;

public class RegionalPlaceService : IRegionalPlaceService
{
    private const int MaxTake = 50;
    private const string PublishedStatus = "Published";

    private readonly AppDbContext _db;

    public RegionalPlaceService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<RegionalPlaceListResponseDto> GetPlacesAsync(
        int userId,
        int regionId,
        string? search,
        string? status,
        string? placeType,
        string? sort,
        int skip,
        int take)
    {
        await EnsureAssignedRegionAsync(userId, regionId);

        var normalizedSkip = NormalizeSkip(skip);
        var normalizedTake = NormalizeTake(take);
        var normalizedStatus = NormalizeStatus(status);
        var normalizedSort = NormalizeSort(sort);

        var query = _db.Places
            .AsNoTracking()
            .Where(place => place.Location.Destination.RegionId == regionId);

        if (normalizedStatus is "draft" or "archived")
        {
            query = query.Where(_ => false);
        }

        query = ApplySearch(query, search);
        query = ApplyPlaceTypeFilter(query, placeType);
        query = ApplySort(query, normalizedSort);

        var totalCount = await query.CountAsync();
        var rows = await query
            .Skip(normalizedSkip)
            .Take(normalizedTake)
            .Select(place => new PlaceProjection
            {
                Id = place.Id,
                Name = place.Name,
                Subtitle = place.Subtitle,
                Description = place.Description,
                Address = place.Address,
                OpeningHours = place.OpeningHours,
                ContactEmail = place.ContactEmail,
                PhoneNumber = place.Number,
                PlaceTypeId = place.PlaceTypeId,
                PlaceTypeName = place.PlaceType.Name,
                StayTypeId = place.Stay != null ? place.Stay.StayTypeId : null,
                StayTypeName = place.Stay != null ? place.Stay.StayType.Name : null,
                LocationId = place.LocationId,
                LocationName = place.Location.Name,
                DestinationId = place.Location.DestinationId,
                DestinationName = place.Location.Destination.Name,
                RegionId = place.Location.Destination.RegionId,
                RegionName = place.Location.Destination.Region.Name,
                TerritoryName = place.Location.Destination.Region.Territory.Name,
                CreatedAt = place.CreatedAt,
                UpdatedAt = place.UpdatedAt,
                StayUpdatedAt = place.Stay != null ? place.Stay.UpdatedAt : null,
                Latitude = place.Latitude,
                Longitude = place.Longitude,
                ImageUrl = place.Images
                    .Where(image => image.TargetType == EntityImageTargetType.Place)
                    .OrderByDescending(image => image.IsMain)
                    .ThenBy(image => image.SortOrder)
                    .ThenBy(image => image.CreatedAt)
                    .ThenBy(image => image.Id)
                    .Select(image => image.Url)
                    .FirstOrDefault()
            })
            .ToListAsync();

        return new RegionalPlaceListResponseDto
        {
            Items = rows.Select(MapListItem).ToList(),
            TotalCount = totalCount,
            Skip = normalizedSkip,
            Take = normalizedTake
        };
    }

    public async Task<RegionalPlaceDetailDto> GetPlaceAsync(int userId, int regionId, int placeId)
    {
        await EnsureAssignedRegionAsync(userId, regionId);

        var row = await _db.Places
            .AsNoTracking()
            .Where(place => place.Id == placeId && place.Location.Destination.RegionId == regionId)
            .Select(place => new PlaceProjection
            {
                Id = place.Id,
                Name = place.Name,
                Subtitle = place.Subtitle,
                Description = place.Description,
                Address = place.Address,
                OpeningHours = place.OpeningHours,
                ContactEmail = place.ContactEmail,
                PhoneNumber = place.Number,
                PlaceTypeId = place.PlaceTypeId,
                PlaceTypeName = place.PlaceType.Name,
                StayTypeId = place.Stay != null ? place.Stay.StayTypeId : null,
                StayTypeName = place.Stay != null ? place.Stay.StayType.Name : null,
                LocationId = place.LocationId,
                LocationName = place.Location.Name,
                DestinationId = place.Location.DestinationId,
                DestinationName = place.Location.Destination.Name,
                RegionId = place.Location.Destination.RegionId,
                RegionName = place.Location.Destination.Region.Name,
                TerritoryName = place.Location.Destination.Region.Territory.Name,
                CreatedAt = place.CreatedAt,
                UpdatedAt = place.UpdatedAt,
                StayUpdatedAt = place.Stay != null ? place.Stay.UpdatedAt : null,
                Latitude = place.Latitude,
                Longitude = place.Longitude,
                ImageUrl = place.Images
                    .Where(image => image.TargetType == EntityImageTargetType.Place)
                    .OrderByDescending(image => image.IsMain)
                    .ThenBy(image => image.SortOrder)
                    .ThenBy(image => image.CreatedAt)
                    .ThenBy(image => image.Id)
                    .Select(image => image.Url)
                    .FirstOrDefault()
            })
            .SingleOrDefaultAsync()
            ?? throw new KeyNotFoundException("Place not found.");

        return MapDetail(row);
    }

    public async Task<RegionalPlaceDetailDto> UpdatePlaceAsync(
        int userId,
        int regionId,
        int placeId,
        RegionalPlaceUpdateRequestDto request)
    {
        await EnsureAssignedRegionAsync(userId, regionId);

        var place = await _db.Places
            .Include(item => item.Location)
                .ThenInclude(location => location.Destination)
            .Include(item => item.Stay)
            .SingleOrDefaultAsync(item => item.Id == placeId && item.Location.Destination.RegionId == regionId)
            ?? throw new KeyNotFoundException("Place not found.");

        if (request.LocationId.HasValue)
        {
            var location = await _db.Locations
                .SingleOrDefaultAsync(item =>
                    item.Id == request.LocationId.Value &&
                    item.Destination.RegionId == regionId)
                ?? throw new InvalidOperationException("Location is invalid for the selected region.");

            place.LocationId = location.Id;
        }

        if (request.PlaceTypeId.HasValue &&
            !await _db.PlaceTypes.AnyAsync(item => item.Id == request.PlaceTypeId.Value))
        {
            throw new InvalidOperationException("Place type is invalid.");
        }

        if (request.StayTypeId.HasValue)
        {
            if (place.Stay is null)
                throw new InvalidOperationException("Stay type can only be updated for accommodation places.");

            if (!await _db.StayTypes.AnyAsync(item => item.Id == request.StayTypeId.Value))
                throw new InvalidOperationException("Stay type is invalid.");

            place.Stay.StayTypeId = request.StayTypeId.Value;
            place.Stay.UpdatedAt = DateTime.UtcNow;
        }

        ApplyUpdate(place, request);
        place.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();
        return await GetPlaceAsync(userId, regionId, placeId);
    }

    public async Task DeletePlaceAsync(int userId, int regionId, int placeId)
    {
        await EnsureAssignedRegionAsync(userId, regionId);

        var place = await _db.Places
            .SingleOrDefaultAsync(item => item.Id == placeId && item.Location.Destination.RegionId == regionId)
            ?? throw new KeyNotFoundException("Place not found.");

        if (await HasBlockingDependenciesAsync(placeId))
        {
            throw new InvalidOperationException("This place cannot be deleted while it has related content.");
        }

        _db.Places.Remove(place);
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

    private async Task<bool> HasBlockingDependenciesAsync(int placeId)
    {
        if (await _db.Events.AsNoTracking().AnyAsync(@event => @event.PlaceId == placeId))
            return true;

        if (await _db.PlaceReviews.AsNoTracking().AnyAsync(review => review.PlaceId == placeId))
            return true;

        if (await _db.ReviewReports.AsNoTracking().AnyAsync(report =>
                report.TargetType == ReviewReportTargetType.Place && report.EntityId == placeId ||
                report.PlaceReview != null && report.PlaceReview.PlaceId == placeId))
            return true;

        if (await _db.PlaceContributions.AsNoTracking().AnyAsync(contribution => contribution.PlaceId == placeId))
            return true;

        if (await _db.UserPlaces.AsNoTracking().AnyAsync(owner => owner.PlaceId == placeId))
            return true;

        if (await _db.CollectionItems.AsNoTracking().AnyAsync(item => item.PlaceId == placeId))
            return true;

        if (await _db.Favorites.AsNoTracking().AnyAsync(item =>
                item.EntityId == placeId &&
                item.EntityType.ToLower() == "place"))
            return true;

        return await _db.AnalyticsEvents.AsNoTracking().AnyAsync(item =>
            item.EntityId == placeId &&
            item.EntityType != null &&
            (item.EntityType.ToLower() == "place" ||
                item.EntityType.ToLower() == "stay" ||
                item.EntityType.ToLower() == "hotel"));
    }

    private static IQueryable<Place> ApplySearch(IQueryable<Place> query, string? search)
    {
        if (string.IsNullOrWhiteSpace(search))
            return query;

        var term = search.Trim().ToLowerInvariant();
        return query.Where(place =>
            place.Name.ToLower().Contains(term) ||
            place.Description.ToLower().Contains(term) ||
            place.Subtitle != null && place.Subtitle.ToLower().Contains(term) ||
            place.Address != null && place.Address.ToLower().Contains(term) ||
            place.PlaceType.Name.ToLower().Contains(term) ||
            place.Location.Name.ToLower().Contains(term) ||
            place.Location.Destination.Name.ToLower().Contains(term) ||
            place.Location.Destination.Region.Name.ToLower().Contains(term) ||
            place.Location.Destination.Region.Territory.Name.ToLower().Contains(term) ||
            place.Stay != null && place.Stay.StayType.Name.ToLower().Contains(term) ||
            place.Stay != null && place.Stay.StayType.Slug.ToLower().Contains(term) ||
            place.PlaceTags.Any(placeTag =>
                placeTag.Tag.Name.ToLower().Contains(term) ||
                placeTag.Tag.Slug.ToLower().Contains(term)));
    }

    private static IQueryable<Place> ApplyPlaceTypeFilter(IQueryable<Place> query, string? placeType)
    {
        if (string.IsNullOrWhiteSpace(placeType) ||
            string.Equals(placeType.Trim(), "all", StringComparison.OrdinalIgnoreCase))
        {
            return query;
        }

        var term = placeType.Trim().ToLowerInvariant();
        return query.Where(place =>
            place.PlaceType.Name.ToLower() == term ||
            place.Stay != null && place.Stay.StayType.Name.ToLower() == term ||
            place.Stay != null && place.Stay.StayType.Slug.ToLower() == term);
    }

    private static IQueryable<Place> ApplySort(IQueryable<Place> query, string sort)
    {
        return sort switch
        {
            "oldest" => query
                .OrderBy(place => place.UpdatedAt ?? place.CreatedAt)
                .ThenBy(place => place.Id),
            "nameasc" => query
                .OrderBy(place => place.Name)
                .ThenBy(place => place.Id),
            "namedesc" => query
                .OrderByDescending(place => place.Name)
                .ThenByDescending(place => place.Id),
            _ => query
                .OrderByDescending(place => place.UpdatedAt ?? place.CreatedAt)
                .ThenByDescending(place => place.Id),
        };
    }

    private static void ApplyUpdate(Place place, RegionalPlaceUpdateRequestDto request)
    {
        if (request.Name is not null)
        {
            var name = request.Name.Trim();
            if (name.Length == 0)
                throw new InvalidOperationException("Place name is required.");
            if (name.Length > 200)
                throw new InvalidOperationException("Place name is too long.");

            place.Name = name;
        }

        if (request.Subtitle is not null)
        {
            var subtitle = request.Subtitle.Trim();
            if (subtitle.Length > 500)
                throw new InvalidOperationException("Subtitle is too long.");

            place.Subtitle = subtitle.Length == 0 ? null : subtitle;
        }

        if (request.Description is not null)
        {
            var description = request.Description.Trim();
            if (description.Length > 2000)
                throw new InvalidOperationException("Description is too long.");

            place.Description = description;
        }

        if (request.Address is not null)
        {
            var address = request.Address.Trim();
            if (address.Length > 500)
                throw new InvalidOperationException("Address is too long.");

            place.Address = address.Length == 0 ? null : address;
        }

        if (request.OpeningHours is not null)
        {
            var openingHours = request.OpeningHours.Trim();
            if (openingHours.Length > 200)
                throw new InvalidOperationException("Opening hours are too long.");

            place.OpeningHours = openingHours.Length == 0 ? null : openingHours;
        }

        if (request.ContactEmail is not null)
        {
            var contactEmail = request.ContactEmail.Trim();
            if (contactEmail.Length > 200)
                throw new InvalidOperationException("Contact email is too long.");

            place.ContactEmail = contactEmail.Length == 0 ? null : contactEmail;
        }

        if (request.PhoneNumber is not null)
        {
            var phoneNumber = request.PhoneNumber.Trim();
            if (phoneNumber.Length > 50)
                throw new InvalidOperationException("Phone number is too long.");

            place.Number = phoneNumber.Length == 0 ? null : phoneNumber;
        }

        if (request.PlaceTypeId.HasValue)
        {
            if (request.PlaceTypeId.Value <= 0)
                throw new InvalidOperationException("Place type is invalid.");

            place.PlaceTypeId = request.PlaceTypeId.Value;
        }

        if (request.Latitude.HasValue)
        {
            if (request.Latitude.Value is < -90 or > 90)
                throw new InvalidOperationException("Latitude is invalid.");

            place.Latitude = request.Latitude.Value;
        }

        if (request.Longitude.HasValue)
        {
            if (request.Longitude.Value is < -180 or > 180)
                throw new InvalidOperationException("Longitude is invalid.");

            place.Longitude = request.Longitude.Value;
        }
    }

    private static RegionalPlaceListItemDto MapListItem(PlaceProjection row)
    {
        return new RegionalPlaceListItemDto
        {
            Id = row.Id,
            Name = row.Name,
            Summary = BuildSummary(row.Subtitle, row.Description),
            Description = row.Description,
            Subtitle = row.Subtitle,
            Address = row.Address,
            PlaceType = row.PlaceTypeName,
            PlaceTypeId = row.PlaceTypeId,
            StayType = row.StayTypeName,
            StayTypeId = row.StayTypeId,
            Status = PublishedStatus,
            RegionId = row.RegionId,
            RegionDisplayName = BuildRegionDisplayName(row.RegionName, row.TerritoryName),
            DestinationId = row.DestinationId,
            DestinationName = row.DestinationName,
            LocationId = row.LocationId,
            LocationName = row.LocationName,
            CreatedAt = row.CreatedAt,
            LastUpdatedAt = row.UpdatedAt ?? row.StayUpdatedAt ?? row.CreatedAt,
            ImageUrl = row.ImageUrl,
            DetailRoute = "/places/detail/" + row.Id,
            EditRoute = "/places/edit/" + row.Id
        };
    }

    private static RegionalPlaceDetailDto MapDetail(PlaceProjection row)
    {
        var item = MapListItem(row);
        return new RegionalPlaceDetailDto
        {
            Id = item.Id,
            Name = item.Name,
            Summary = item.Summary,
            Description = item.Description,
            Subtitle = item.Subtitle,
            Address = item.Address,
            PlaceType = item.PlaceType,
            PlaceTypeId = item.PlaceTypeId,
            StayType = item.StayType,
            StayTypeId = item.StayTypeId,
            Status = item.Status,
            RegionId = item.RegionId,
            RegionDisplayName = item.RegionDisplayName,
            DestinationId = item.DestinationId,
            DestinationName = item.DestinationName,
            LocationId = item.LocationId,
            LocationName = item.LocationName,
            CreatedAt = item.CreatedAt,
            LastUpdatedAt = item.LastUpdatedAt,
            ImageUrl = item.ImageUrl,
            DetailRoute = item.DetailRoute,
            EditRoute = item.EditRoute,
            Latitude = row.Latitude,
            Longitude = row.Longitude,
            OpeningHours = row.OpeningHours,
            ContactEmail = row.ContactEmail,
            PhoneNumber = row.PhoneNumber
        };
    }

    private static string BuildRegionDisplayName(string regionName, string? territoryName)
    {
        return string.IsNullOrWhiteSpace(territoryName)
            ? regionName
            : regionName + ", " + territoryName;
    }

    private static string? BuildSummary(string? subtitle, string? description)
    {
        var source = string.IsNullOrWhiteSpace(subtitle) ? description : subtitle;
        if (string.IsNullOrWhiteSpace(source))
            return null;

        var normalized = string.Join(" ", source.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
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

    private sealed class PlaceProjection
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? Subtitle { get; set; }
        public string? Description { get; set; }
        public string? Address { get; set; }
        public string? OpeningHours { get; set; }
        public string? ContactEmail { get; set; }
        public string? PhoneNumber { get; set; }
        public int PlaceTypeId { get; set; }
        public string? PlaceTypeName { get; set; }
        public int? StayTypeId { get; set; }
        public string? StayTypeName { get; set; }
        public int LocationId { get; set; }
        public string LocationName { get; set; } = string.Empty;
        public int DestinationId { get; set; }
        public string DestinationName { get; set; } = string.Empty;
        public int RegionId { get; set; }
        public string RegionName { get; set; } = string.Empty;
        public string? TerritoryName { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
        public DateTime? StayUpdatedAt { get; set; }
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public string? ImageUrl { get; set; }
    }
}
