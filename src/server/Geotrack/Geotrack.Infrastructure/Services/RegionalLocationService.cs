using Geotrack.Application.DTOs.Admin;
using Geotrack.Application.Interfaces.Service;
using Geotrack.Application.Security;
using Geotrack.Application.Services;
using Geotrack.Domain.Entities.Core;
using Geotrack.Domain.Entities.Tags;
using Microsoft.EntityFrameworkCore;
using Coordinate = NetTopologySuite.Geometries.Coordinate;
using Geometry = NetTopologySuite.Geometries.Geometry;
using GeometryFactory = NetTopologySuite.Geometries.GeometryFactory;
using PrecisionModel = NetTopologySuite.Geometries.PrecisionModel;

namespace Geotrack.Infrastructure.Services;

public class RegionalLocationService : IRegionalLocationService
{
    private const int DefaultTake = 25;
    private const int MaxTake = 50;
    private const string PublishedStatus = "Published";
    private const string DraftStatus = "Draft";
    private const string ArchivedStatus = "Archived";
    private const string ManagedRegionalImagePrefix = "/media/uploads/regional/";
    private static string ExistingEditRoute(int locationId) => $"/locations/edit/{locationId}";
    private const string BoundaryGeometryPropertyName = "BoundaryGeometry";
    private static readonly GeometryFactory Wgs84GeometryFactory = new(new PrecisionModel(), 4326);

    private readonly AppDbContext _db;
    private readonly RegionalScopeValidationService _scopeValidation;
    private readonly IContentHtmlSanitizer _htmlSanitizer;

    public RegionalLocationService(
        AppDbContext db,
        RegionalScopeValidationService scopeValidation,
        IContentHtmlSanitizer htmlSanitizer)
    {
        _db = db;
        _scopeValidation = scopeValidation;
        _htmlSanitizer = htmlSanitizer;
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

        if (normalizedStatus != "all")
        {
            var statusName = StatusFromToken(normalizedStatus);
            query = query.Where(location => location.Status == statusName);
        }

        query = ApplySearch(query, search);
        query = ApplySort(query, normalizedSort);

        var totalCount = await query.CountAsync();
        var rows = await query
            .Skip(normalizedSkip)
            .Take(normalizedTake)
            .AsSplitQuery()
            .Select(location => new LocationProjection
            {
                Id = location.Id,
                Name = location.Name,
                Summary = location.Summary,
                InternalReferenceId = location.InternalReferenceId,
                Description = location.Description,
                Status = location.Status,
                DestinationId = location.DestinationId,
                DestinationName = location.Destination.Name,
                RegionId = location.Destination.RegionId,
                RegionName = location.Destination.Region.Name,
                TerritoryName = location.Destination.Region.Territory.Name,
                LocationTypeId = location.LocationTypeId,
                LocationTypeName = location.LocationType.Name,
                CreatedByUserId = location.CreatedByUserId,
                BoundaryNote = location.BoundaryNote,
                BoundaryShapeType = location.BoundaryShapeType,
                CreatedAt = location.CreatedAt,
                UpdatedAt = location.UpdatedAt,
                PublishedAt = location.PublishedAt,
                Latitude = location.Latitude,
                Longitude = location.Longitude,
                ImageUrl = location.Images
                    .Where(image => image.TargetType == EntityImageTargetType.Location)
                    .OrderBy(image => image.ImageRole == EntityImageRole.Card ? 0 : image.ImageRole == EntityImageRole.Cover ? 1 : 2)
                    .ThenBy(image => image.SortOrder)
                    .ThenBy(image => image.CreatedAt)
                    .ThenBy(image => image.Id)
                    .Select(image => image.Url)
                    .FirstOrDefault(),
                CardImageUrl = location.Images
                    .Where(image => image.TargetType == EntityImageTargetType.Location && image.ImageRole == EntityImageRole.Card)
                    .OrderBy(image => image.SortOrder)
                    .ThenBy(image => image.CreatedAt)
                    .ThenBy(image => image.Id)
                    .Select(image => image.Url)
                    .FirstOrDefault(),
                CoverImageUrl = location.Images
                    .Where(image => image.TargetType == EntityImageTargetType.Location && image.ImageRole == EntityImageRole.Cover)
                    .OrderBy(image => image.SortOrder)
                    .ThenBy(image => image.CreatedAt)
                    .ThenBy(image => image.Id)
                    .Select(image => image.Url)
                    .FirstOrDefault(),
                GalleryImageUrls = location.Images
                    .Where(image => image.TargetType == EntityImageTargetType.Location && image.ImageRole == EntityImageRole.Gallery)
                    .OrderBy(image => image.SortOrder)
                    .ThenBy(image => image.CreatedAt)
                    .ThenBy(image => image.Id)
                    .Select(image => image.Url)
                    .ToList(),
                Tags = location.LocationTags
                    .OrderBy(locationTag => locationTag.Tag.Name)
                    .Select(locationTag => new RegionalLocationTagDto
                    {
                        Id = locationTag.TagId,
                        Name = locationTag.Tag.Name,
                        Slug = locationTag.Tag.Slug
                    })
                    .ToList()
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
            .AsSplitQuery()
            .Select(location => new LocationProjection
            {
                Id = location.Id,
                Name = location.Name,
                Summary = location.Summary,
                InternalReferenceId = location.InternalReferenceId,
                Description = location.Description,
                Status = location.Status,
                DestinationId = location.DestinationId,
                DestinationName = location.Destination.Name,
                RegionId = location.Destination.RegionId,
                RegionName = location.Destination.Region.Name,
                TerritoryName = location.Destination.Region.Territory.Name,
                LocationTypeId = location.LocationTypeId,
                LocationTypeName = location.LocationType.Name,
                CreatedByUserId = location.CreatedByUserId,
                BoundaryNote = location.BoundaryNote,
                BoundaryShapeType = location.BoundaryShapeType,
                CreatedAt = location.CreatedAt,
                UpdatedAt = location.UpdatedAt,
                PublishedAt = location.PublishedAt,
                Latitude = location.Latitude,
                Longitude = location.Longitude,
                ImageUrl = location.Images
                    .Where(image => image.TargetType == EntityImageTargetType.Location)
                    .OrderBy(image => image.ImageRole == EntityImageRole.Card ? 0 : image.ImageRole == EntityImageRole.Cover ? 1 : 2)
                    .ThenBy(image => image.SortOrder)
                    .ThenBy(image => image.CreatedAt)
                    .ThenBy(image => image.Id)
                    .Select(image => image.Url)
                    .FirstOrDefault(),
                CardImageUrl = location.Images
                    .Where(image => image.TargetType == EntityImageTargetType.Location && image.ImageRole == EntityImageRole.Card)
                    .OrderBy(image => image.SortOrder)
                    .ThenBy(image => image.CreatedAt)
                    .ThenBy(image => image.Id)
                    .Select(image => image.Url)
                    .FirstOrDefault(),
                CoverImageUrl = location.Images
                    .Where(image => image.TargetType == EntityImageTargetType.Location && image.ImageRole == EntityImageRole.Cover)
                    .OrderBy(image => image.SortOrder)
                    .ThenBy(image => image.CreatedAt)
                    .ThenBy(image => image.Id)
                    .Select(image => image.Url)
                    .FirstOrDefault(),
                GalleryImageUrls = location.Images
                    .Where(image => image.TargetType == EntityImageTargetType.Location && image.ImageRole == EntityImageRole.Gallery)
                    .OrderBy(image => image.SortOrder)
                    .ThenBy(image => image.CreatedAt)
                    .ThenBy(image => image.Id)
                    .Select(image => image.Url)
                    .ToList(),
                Tags = location.LocationTags
                    .OrderBy(locationTag => locationTag.Tag.Name)
                    .Select(locationTag => new RegionalLocationTagDto
                    {
                        Id = locationTag.TagId,
                        Name = locationTag.Tag.Name,
                        Slug = locationTag.Tag.Slug
                    })
                    .ToList()
            })
            .SingleOrDefaultAsync()
            ?? throw new KeyNotFoundException("Location not found.");

        var dto = MapDetail(row);
        dto.CanDelete = !await HasBlockingDependenciesAsync(row.Id);
        return dto;
    }

    public async Task<RegionalLocationDetailDto> CreateLocationAsync(
        int userId,
        RegionalLocationCreateRequestDto request)
    {
        if (!string.IsNullOrWhiteSpace(request.InternalReferenceId))
            throw new InvalidOperationException("Internal reference id is generated by the server.");

        await EnsureAssignedRegionAsync(userId, request.RegionId);

        var destination = await _db.Destinations
            .AsNoTracking()
            .Select(item => new
            {
                item.Id,
                item.RegionId,
                BoundaryGeometry = EF.Property<Geometry?>(item, BoundaryGeometryPropertyName)
            })
            .SingleOrDefaultAsync(item =>
                item.Id == request.DestinationId &&
                item.RegionId == request.RegionId)
            ?? throw new InvalidOperationException("Destination is invalid for the selected region.");

        var name = NormalizeRequiredText(request.Name, "Location name is required.", 200, "Location name is too long.");
        var summary = NormalizeOptionalText(request.Summary, 160, "Summary is too long.");
        var description = _htmlSanitizer.SanitizeRichText(request.Description);
        var status = NormalizeCreateStatus(request.Status);
        var boundary = BuildBoundary(request.Boundary);
        if (boundary is not null)
        {
            EnsureLocationBoundaryInsideParentDestination(boundary.Geometry, destination.BoundaryGeometry);
        }
        var coordinates = ResolveCoordinates(request, boundary);
        await _scopeValidation.EnsurePointAllowedAsync(
            userId,
            request.RegionId,
            "Location center",
            coordinates.Latitude,
            coordinates.Longitude,
            "Create");
        EnsurePointInsideParentDestination(
            coordinates.Latitude,
            coordinates.Longitude,
            destination.BoundaryGeometry);
        if (boundary is not null)
        {
            await _scopeValidation.EnsureGeometryAllowedAsync(
                userId,
                request.RegionId,
                "Location",
                boundary.Geometry,
                "Create");
        }
        var locationTypeId = await ResolveLocationTypeIdAsync(request.LocationTypeId);
        var tagIds = await ValidateLocationTagIdsAsync(request.TagIds);
        var now = DateTime.UtcNow;

        Location? location = null;
        await _db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var transaction = await _db.Database.BeginTransactionAsync();

            location = new Location
            {
                Name = name,
                Summary = summary,
                Description = description,
                Status = status,
                DestinationId = destination.Id,
                LocationTypeId = locationTypeId,
                Latitude = coordinates.Latitude,
                Longitude = coordinates.Longitude,
                CreatedByUserId = userId,
                PublishedAt = status == PublishedStatus ? now : null,
                BoundaryNote = NormalizeOptionalText(request.BoundaryNote, 500, "Boundary note is too long."),
                BoundaryShapeType = boundary?.ShapeType,
                CreatedAt = now,
                LocationTags = tagIds
                    .Select(tagId => new LocationTag { TagId = tagId })
                    .ToList()
            };

            _db.Locations.Add(location);
            if (boundary is not null)
            {
                _db.Entry(location).Property<Geometry?>(BoundaryGeometryPropertyName).CurrentValue = boundary.Geometry;
            }

            await _db.SaveChangesAsync();

            _db.EntityImages.AddRange(AddRequiredCreateImages(request, location.Id, location.Name, now));

            location.InternalReferenceId = BuildInternalReferenceId(location.Id);
            await _db.SaveChangesAsync();
            await transaction.CommitAsync();
        });

        return await GetLocationAsync(userId, request.RegionId, location!.Id);
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
            .Include(item => item.LocationTags)
            .Include(item => item.Images)
            .SingleOrDefaultAsync(item => item.Id == locationId && item.Destination.RegionId == regionId)
            ?? throw new KeyNotFoundException("Location not found.");

        var parentDestinationChanged = request.DestinationId.HasValue;
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
        if (parentDestinationChanged || request.Latitude.HasValue || request.Longitude.HasValue)
        {
            await _scopeValidation.EnsurePointAllowedAsync(
                userId,
                regionId,
                "Location center",
                location.Latitude,
                location.Longitude,
                "Update");
            var parentDestinationBoundary = await _db.Destinations
                .AsNoTracking()
                .Where(item => item.Id == location.DestinationId)
                .Select(item => EF.Property<Geometry?>(item, BoundaryGeometryPropertyName))
                .SingleAsync();
            EnsurePointInsideParentDestination(
                location.Latitude,
                location.Longitude,
                parentDestinationBoundary);
        }

        if (request.TagIds is not null)
        {
            var tagIds = await ValidateLocationTagIdsAsync(request.TagIds);
            location.LocationTags.Clear();
            foreach (var tagId in tagIds)
                location.LocationTags.Add(new LocationTag { LocationId = location.Id, TagId = tagId });
        }

        var now = DateTime.UtcNow;
        ApplyImageUpdates(location, request, now);
        location.UpdatedAt = now;

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

    public async Task<List<RegionalLocationParentDestinationDto>> GetParentDestinationsAsync(
        int userId,
        int regionId,
        string? search,
        int take)
    {
        await EnsureAssignedRegionAsync(userId, regionId);

        var normalizedTake = Math.Clamp(take <= 0 ? MaxTake : take, 1, MaxTake);
        var query = _db.Destinations
            .AsNoTracking()
            .Where(destination => destination.RegionId == regionId);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLowerInvariant();
            query = query.Where(destination =>
                destination.Name.ToLower().Contains(term) ||
                (destination.InternalReferenceId != null && destination.InternalReferenceId.ToLower().Contains(term)));
        }

        return await query
            .OrderBy(destination => destination.Name)
            .ThenBy(destination => destination.Id)
            .Take(normalizedTake)
            .Select(destination => new RegionalLocationParentDestinationDto
            {
                Id = destination.Id,
                Name = destination.Name,
                DisplayName = destination.Name,
                Status = destination.Status
            })
            .ToListAsync();
    }

    public async Task<List<RegionalLocationTagDto>> GetLocationTagsAsync(int userId, string? search)
    {
        await EnsureRegionalManagerRoleAsync(userId);

        var query = _db.Tags
            .AsNoTracking()
            .Where(tag => tag.ForLocation);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLowerInvariant();
            query = query.Where(tag =>
                tag.Name.ToLower().Contains(term) ||
                tag.Slug.ToLower().Contains(term));
        }

        return await query
            .OrderBy(tag => tag.Name)
            .Take(50)
            .Select(tag => new RegionalLocationTagDto
            {
                Id = tag.Id,
                Name = tag.Name,
                Slug = tag.Slug
            })
            .ToListAsync();
    }

    public async Task<RegionalLocationTagDto> CreateLocationTagAsync(
        int userId,
        RegionalTagCreateRequestDto request)
    {
        await EnsureRegionalManagerRoleAsync(userId);

        var tag = await RegionalTagWriter.CreateScopedTagAsync(
            _db,
            request.Name,
            RegionalTagScope.Location);

        return new RegionalLocationTagDto
        {
            Id = tag.Id,
            Name = tag.Name,
            Slug = tag.Slug
        };
    }

    public async Task<List<RegionalLocationTypeDto>> GetLocationTypesAsync(int userId)
    {
        await EnsureRegionalManagerRoleAsync(userId);

        return await _db.LocationTypes
            .AsNoTracking()
            .OrderBy(type => type.Name)
            .ThenBy(type => type.Id)
            .Select(type => new RegionalLocationTypeDto
            {
                Id = type.Id,
                Name = type.Name,
                Description = type.Description
            })
            .ToListAsync();
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
            (location.Summary != null && location.Summary.ToLower().Contains(term)) ||
            location.Description.ToLower().Contains(term) ||
            (location.InternalReferenceId != null && location.InternalReferenceId.ToLower().Contains(term)) ||
            location.Status.ToLower().Contains(term) ||
            location.LocationType.Name.ToLower().Contains(term) ||
            location.Destination.Name.ToLower().Contains(term) ||
            location.Destination.Description.ToLower().Contains(term) ||
            location.Destination.Region.Name.ToLower().Contains(term) ||
            location.Destination.Region.Territory.Name.ToLower().Contains(term) ||
            location.LocationTags.Any(locationTag =>
                locationTag.Tag.Name.ToLower().Contains(term) ||
                locationTag.Tag.Slug.ToLower().Contains(term)));
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

    private void ApplyUpdate(Location location, RegionalLocationUpdateRequestDto request)
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

        if (request.Summary is not null)
        {
            var summary = request.Summary.Trim();
            if (summary.Length > 160)
                throw new InvalidOperationException("Summary is too long.");

            location.Summary = summary.Length == 0 ? null : summary;
        }

        if (request.Description is not null)
        {
            location.Description = _htmlSanitizer.SanitizeRichText(request.Description);
        }

        if (request.LocationTypeId.HasValue)
        {
            if (request.LocationTypeId.Value <= 0)
                throw new InvalidOperationException("Location type is invalid.");

            location.LocationTypeId = request.LocationTypeId.Value;
        }

        if (request.Latitude.HasValue)
        {
            ValidateLatitude(request.Latitude.Value);
            location.Latitude = request.Latitude.Value;
        }

        if (request.Longitude.HasValue)
        {
            ValidateLongitude(request.Longitude.Value);
            location.Longitude = request.Longitude.Value;
        }
    }

    private async Task<int> ResolveLocationTypeIdAsync(int? requestedLocationTypeId)
    {
        if (requestedLocationTypeId.HasValue)
        {
            if (requestedLocationTypeId.Value <= 0)
                throw new InvalidOperationException("Location type is invalid.");

            var exists = await _db.LocationTypes
                .AsNoTracking()
                .AnyAsync(item => item.Id == requestedLocationTypeId.Value);

            if (!exists)
                throw new InvalidOperationException("Location type is invalid.");

            return requestedLocationTypeId.Value;
        }

        var defaultType = await _db.LocationTypes
            .AsNoTracking()
            .OrderBy(item => item.Name == "Neighborhood" ? 0 : 1)
            .ThenBy(item => item.Id)
            .FirstOrDefaultAsync()
            ?? throw new InvalidOperationException("A location type must exist before locations can be created.");

        return defaultType.Id;
    }

    private async Task<List<int>> ValidateLocationTagIdsAsync(IEnumerable<int>? requestedTagIds)
    {
        var tagIds = (requestedTagIds ?? Array.Empty<int>())
            .Where(id => id > 0)
            .Distinct()
            .ToList();

        if (tagIds.Count == 0)
            return tagIds;

        var compatibleTagIds = await _db.Tags
            .AsNoTracking()
            .Where(tag => tagIds.Contains(tag.Id) && tag.ForLocation)
            .Select(tag => tag.Id)
            .ToListAsync();

        if (compatibleTagIds.Count != tagIds.Count)
            throw new InvalidOperationException("All selected tags must be location-compatible.");

        return tagIds;
    }

    private static void EnsurePointInsideParentDestination(
        double latitude,
        double longitude,
        Geometry? parentDestinationBoundary)
    {
        if (parentDestinationBoundary is null || parentDestinationBoundary.IsEmpty)
            return;

        var point = Wgs84GeometryFactory.CreatePoint(new Coordinate(longitude, latitude));
        point.SRID = 4326;

        if (!parentDestinationBoundary.Covers(point))
            throw new InvalidOperationException("Location coordinates must be inside parent destination boundary.");
    }

    private static void EnsureLocationBoundaryInsideParentDestination(
        Geometry locationBoundary,
        Geometry? parentDestinationBoundary)
    {
        if (parentDestinationBoundary is null || parentDestinationBoundary.IsEmpty || !parentDestinationBoundary.IsValid)
            throw new InvalidOperationException("Parent destination must have a boundary before adding location zones.");

        if (!parentDestinationBoundary.Covers(locationBoundary))
            throw new InvalidOperationException("Location boundary must be inside parent destination boundary.");
    }

    private static BoundaryResult? BuildBoundary(RegionalLocationBoundaryDto? boundary)
    {
        if (boundary is null)
            return null;

        var shapeType = NormalizeShapeType(boundary.ShapeType);
        var coordinates = boundary.Coordinates ?? new List<RegionalLocationCoordinateDto>();

        if (coordinates.Count < 3)
            throw new InvalidOperationException("Boundary geometry must contain at least three points.");

        var ringCoordinates = coordinates
            .Select(point =>
            {
                ValidateLatitude(point.Latitude);
                ValidateLongitude(point.Longitude);
                return new Coordinate(point.Longitude, point.Latitude);
            })
            .ToList();

        var first = ringCoordinates[0];
        var last = ringCoordinates[^1];
        if (!CoordinatesEqual(first, last))
        {
            ringCoordinates.Add(new Coordinate(first.X, first.Y));
        }

        var uniquePointCount = ringCoordinates
            .Take(ringCoordinates.Count - 1)
            .Select(coordinate => $"{Math.Round(coordinate.X, 7)}:{Math.Round(coordinate.Y, 7)}")
            .Distinct(StringComparer.Ordinal)
            .Count();

        if (uniquePointCount < 3 || ringCoordinates.Count < 4)
            throw new InvalidOperationException("Boundary geometry must contain at least three distinct points.");

        var polygon = Wgs84GeometryFactory.CreatePolygon(ringCoordinates.ToArray());
        polygon.SRID = 4326;

        if (polygon.IsEmpty || !polygon.IsValid || polygon.Area <= 0)
            throw new InvalidOperationException("Boundary geometry is invalid.");

        return new BoundaryResult(shapeType, polygon);
    }

    private static LocationCoordinates ResolveCoordinates(
        RegionalLocationCreateRequestDto request,
        BoundaryResult? boundary)
    {
        if (!request.Latitude.HasValue || !request.Longitude.HasValue)
        {
            if (boundary is not null)
            {
                var centroid = boundary.Geometry.Centroid;
                return new LocationCoordinates(centroid.Y, centroid.X);
            }

            throw new InvalidOperationException("A location center point is required.");
        }

        ValidateLatitude(request.Latitude.Value);
        ValidateLongitude(request.Longitude.Value);
        return new LocationCoordinates(request.Latitude.Value, request.Longitude.Value);
    }

    private static string NormalizeCreateStatus(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return PublishedStatus;

        var normalized = NormalizeToken(value);
        return normalized switch
        {
            "published" or "publish" => PublishedStatus,
            "draft" => DraftStatus,
            _ => throw new InvalidOperationException("Location status is invalid.")
        };
    }

    private static string NormalizeShapeType(string? value)
    {
        var normalized = NormalizeToken(value ?? string.Empty);
        return normalized switch
        {
            "polygon" => "Polygon",
            "rectangle" => "Rectangle",
            "circle" => "Circle",
            _ => throw new InvalidOperationException("Boundary shape type is invalid.")
        };
    }

    private static string NormalizeRequiredText(string? value, string requiredMessage, int maxLength, string tooLongMessage)
    {
        var normalized = value?.Trim() ?? string.Empty;
        if (normalized.Length == 0)
            throw new InvalidOperationException(requiredMessage);
        if (normalized.Length > maxLength)
            throw new InvalidOperationException(tooLongMessage);

        return normalized;
    }

    private static string? NormalizeOptionalText(string? value, int maxLength, string tooLongMessage)
    {
        if (value is null)
            return null;

        var normalized = value.Trim();
        if (normalized.Length == 0)
            return null;
        if (normalized.Length > maxLength)
            throw new InvalidOperationException(tooLongMessage);

        return normalized;
    }

    private static void ValidateLatitude(double latitude)
    {
        if (!double.IsFinite(latitude) || latitude is < -90 or > 90)
            throw new InvalidOperationException("Latitude is invalid.");
    }

    private static void ValidateLongitude(double longitude)
    {
        if (!double.IsFinite(longitude) || longitude is < -180 or > 180)
            throw new InvalidOperationException("Longitude is invalid.");
    }

    private static bool CoordinatesEqual(Coordinate first, Coordinate second) =>
        Math.Abs(first.X - second.X) < 0.0000001 &&
        Math.Abs(first.Y - second.Y) < 0.0000001;

    private static RegionalLocationListItemDto MapListItem(LocationProjection row)
    {
        return new RegionalLocationListItemDto
        {
            Id = row.Id,
            Name = row.Name,
            Summary = row.Summary ?? BuildSummary(row.LocationTypeName, row.DestinationName),
            Description = row.Description,
            LocationType = row.LocationTypeName,
            LocationTypeId = row.LocationTypeId,
            Status = string.IsNullOrWhiteSpace(row.Status) ? PublishedStatus : row.Status,
            InternalReferenceId = row.InternalReferenceId,
            RegionId = row.RegionId,
            RegionDisplayName = BuildRegionDisplayName(row.RegionName, row.TerritoryName),
            DestinationId = row.DestinationId,
            DestinationName = row.DestinationName,
            CreatedByUserId = row.CreatedByUserId,
            BoundaryNote = row.BoundaryNote,
            BoundaryShapeType = row.BoundaryShapeType,
            Tags = row.Tags,
            CreatedAt = row.CreatedAt,
            PublishedAt = row.PublishedAt,
            LastUpdatedAt = row.UpdatedAt ?? row.CreatedAt,
            ImageUrl = row.ImageUrl,
            CardImageUrl = row.CardImageUrl,
            CoverImageUrl = row.CoverImageUrl,
            GalleryImageUrls = row.GalleryImageUrls,
            DetailRoute = "/locations/detail/" + row.Id,
            EditRoute = ExistingEditRoute(row.Id)
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
            InternalReferenceId = item.InternalReferenceId,
            RegionId = item.RegionId,
            RegionDisplayName = item.RegionDisplayName,
            DestinationId = item.DestinationId,
            DestinationName = item.DestinationName,
            CreatedByUserId = item.CreatedByUserId,
            BoundaryNote = item.BoundaryNote,
            BoundaryShapeType = item.BoundaryShapeType,
            Tags = item.Tags,
            CreatedAt = item.CreatedAt,
            PublishedAt = item.PublishedAt,
            LastUpdatedAt = item.LastUpdatedAt,
            ImageUrl = item.ImageUrl,
            CardImageUrl = item.CardImageUrl,
            CoverImageUrl = item.CoverImageUrl,
            GalleryImageUrls = item.GalleryImageUrls,
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

    private static string StatusFromToken(string status) =>
        status switch
        {
            "published" => PublishedStatus,
            "draft" => DraftStatus,
            "archived" => ArchivedStatus,
            _ => PublishedStatus
        };

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

    private static void ApplyImageUpdates(
        Location location,
        RegionalLocationUpdateRequestDto request,
        DateTime now)
    {
        if (!string.IsNullOrWhiteSpace(request.CardImageUrl))
        {
            UpsertImage(
                location.Images,
                location.Id,
                EntityImageRole.Card,
                NormalizeManagedImageUrl(request.CardImageUrl, "Card image must be uploaded before it can be assigned."),
                location.Name,
                isMain: true,
                sortOrder: 0,
                now);
        }

        if (!string.IsNullOrWhiteSpace(request.CoverImageUrl))
        {
            UpsertImage(
                location.Images,
                location.Id,
                EntityImageRole.Cover,
                NormalizeManagedImageUrl(request.CoverImageUrl, "Cover image must be uploaded before it can be assigned."),
                location.Name,
                isMain: false,
                sortOrder: 1,
                now);
        }

        if (request.GalleryImageUrls is not null)
        {
            var existingGalleryImages = location.Images
                .Where(item => item.TargetType == EntityImageTargetType.Location &&
                               item.LocationId == location.Id &&
                               item.ImageRole == EntityImageRole.Gallery)
                .ToList();

            foreach (var image in existingGalleryImages)
            {
                location.Images.Remove(image);
            }

            var galleryUrls = request.GalleryImageUrls
                .Select(NormalizeOptionalImageUrl)
                .Where(url => !string.IsNullOrWhiteSpace(url))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            for (var i = 0; i < galleryUrls.Count; i++)
            {
                location.Images.Add(new EntityImage
                {
                    TargetType = EntityImageTargetType.Location,
                    LocationId = location.Id,
                    Url = galleryUrls[i]!,
                    AltText = location.Name,
                    IsMain = false,
                    ImageRole = EntityImageRole.Gallery,
                    SortOrder = i + 2,
                    CreatedAt = now
                });
            }
        }
    }

    private static void UpsertImage(
        ICollection<EntityImage> images,
        int locationId,
        string role,
        string url,
        string altText,
        bool isMain,
        int sortOrder,
        DateTime now)
    {
        var image = images.FirstOrDefault(item =>
            item.TargetType == EntityImageTargetType.Location &&
            item.LocationId == locationId &&
            item.ImageRole == role);

        if (image is null)
        {
            images.Add(new EntityImage
            {
                TargetType = EntityImageTargetType.Location,
                LocationId = locationId,
                Url = url,
                AltText = altText,
                IsMain = isMain,
                ImageRole = role,
                SortOrder = sortOrder,
                CreatedAt = now
            });
            return;
        }

        image.Url = url;
        image.AltText = altText;
        image.IsMain = isMain;
        image.SortOrder = sortOrder;
        image.UpdatedAt = now;
    }

    private static List<EntityImage> AddRequiredCreateImages(RegionalLocationCreateRequestDto request, int entityId, string entityName, DateTime now)
    {
        var cardUrl = NormalizeRequiredImageUrl(request.CardImageUrl, "Card image is required.");
        var coverUrl = NormalizeRequiredImageUrl(request.CoverImageUrl, "Cover image is required.");
        var images = new List<EntityImage>
        {
            new()
            {
                TargetType = EntityImageTargetType.Location,
                LocationId = entityId,
                Url = cardUrl,
                AltText = entityName,
                IsMain = true,
                ImageRole = EntityImageRole.Card,
                SortOrder = 0,
                CreatedAt = now
            },
            new()
            {
                TargetType = EntityImageTargetType.Location,
                LocationId = entityId,
                Url = coverUrl,
                AltText = entityName,
                IsMain = false,
                ImageRole = EntityImageRole.Cover,
                SortOrder = 1,
                CreatedAt = now
            }
        };

        var galleryUrls = request.GalleryImageUrls
            .Select(url => NormalizeOptionalImageUrl(url))
            .Where(url => !string.IsNullOrWhiteSpace(url))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        for (var i = 0; i < galleryUrls.Count; i++)
        {
            images.Add(new EntityImage
            {
                TargetType = EntityImageTargetType.Location,
                LocationId = entityId,
                Url = galleryUrls[i]!,
                AltText = entityName,
                IsMain = false,
                ImageRole = EntityImageRole.Gallery,
                SortOrder = i + 2,
                CreatedAt = now
            });
        }

        return images;
    }

    private static string NormalizeRequiredImageUrl(string? value, string message)
    {
        var normalized = NormalizeOptionalImageUrl(value);
        if (string.IsNullOrWhiteSpace(normalized))
            throw new InvalidOperationException(message);

        return normalized;
    }

    private static string? NormalizeOptionalImageUrl(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var normalized = value.Trim();
        if (normalized.Length > 1000)
            throw new InvalidOperationException("Image URL is too long.");

        return normalized;
    }

    private static string NormalizeManagedImageUrl(string? value, string message)
    {
        var normalized = NormalizeRequiredImageUrl(value, message);
        if (!normalized.StartsWith(ManagedRegionalImagePrefix, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Image must be uploaded before it can be assigned.");

        return normalized;
    }

    private static string BuildInternalReferenceId(int locationId) => $"LOC-{locationId:D6}";

    private sealed record BoundaryResult(string ShapeType, Geometry Geometry);

    private sealed record LocationCoordinates(double Latitude, double Longitude);

    private sealed class LocationProjection
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? Summary { get; set; }
        public string? InternalReferenceId { get; set; }
        public string? Description { get; set; }
        public string Status { get; set; } = PublishedStatus;
        public int DestinationId { get; set; }
        public string DestinationName { get; set; } = string.Empty;
        public int RegionId { get; set; }
        public string RegionName { get; set; } = string.Empty;
        public string? TerritoryName { get; set; }
        public int LocationTypeId { get; set; }
        public string? LocationTypeName { get; set; }
        public int? CreatedByUserId { get; set; }
        public string? BoundaryNote { get; set; }
        public string? BoundaryShapeType { get; set; }
        public List<RegionalLocationTagDto> Tags { get; set; } = new();
        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
        public DateTime? PublishedAt { get; set; }
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public string? ImageUrl { get; set; }
        public string? CardImageUrl { get; set; }
        public string? CoverImageUrl { get; set; }
        public List<string> GalleryImageUrls { get; set; } = new();
    }
}
