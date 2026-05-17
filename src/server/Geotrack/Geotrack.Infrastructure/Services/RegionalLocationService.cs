using Geotrack.Application.DTOs.Admin;
using Geotrack.Application.Interfaces.Service;
using Geotrack.Application.Security;
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
    private const string BoundaryGeometryPropertyName = "BoundaryGeometry";
    private static readonly GeometryFactory Wgs84GeometryFactory = new(new PrecisionModel(), 4326);

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
                    .OrderByDescending(image => image.IsMain)
                    .ThenBy(image => image.SortOrder)
                    .ThenBy(image => image.CreatedAt)
                    .ThenBy(image => image.Id)
                    .Select(image => image.Url)
                    .FirstOrDefault(),
                Images = location.Images
                    .Where(image => image.TargetType == EntityImageTargetType.Location)
                    .OrderByDescending(image => image.IsMain)
                    .ThenBy(image => image.SortOrder)
                    .ThenBy(image => image.CreatedAt)
                    .ThenBy(image => image.Id)
                    .Select(image => new RegionalEntityImageDto
                    {
                        Id = image.Id,
                        Url = image.Url,
                        AltText = image.AltText,
                        Caption = image.Caption,
                        IsMain = image.IsMain,
                        SortOrder = image.SortOrder
                    })
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
                    .OrderByDescending(image => image.IsMain)
                    .ThenBy(image => image.SortOrder)
                    .ThenBy(image => image.CreatedAt)
                    .ThenBy(image => image.Id)
                    .Select(image => image.Url)
                    .FirstOrDefault(),
                Images = location.Images
                    .Where(image => image.TargetType == EntityImageTargetType.Location)
                    .OrderByDescending(image => image.IsMain)
                    .ThenBy(image => image.SortOrder)
                    .ThenBy(image => image.CreatedAt)
                    .ThenBy(image => image.Id)
                    .Select(image => new RegionalEntityImageDto
                    {
                        Id = image.Id,
                        Url = image.Url,
                        AltText = image.AltText,
                        Caption = image.Caption,
                        IsMain = image.IsMain,
                        SortOrder = image.SortOrder
                    })
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

        return MapDetail(row);
    }

    public async Task<RegionalLocationDetailDto> CreateLocationAsync(
        int userId,
        RegionalLocationCreateRequestDto request)
    {
        if (!string.IsNullOrWhiteSpace(request.InternalReferenceId))
            throw new InvalidOperationException("Internal reference id is generated by the server.");

        await EnsureAssignedRegionAsync(userId, request.RegionId);

        var destination = await GetDestinationBoundaryAsync(request.DestinationId, request.RegionId);

        var name = NormalizeRequiredText(request.Name, "Location name is required.", 200, "Location name is too long.");
        var summary = NormalizeOptionalText(request.Summary, 160, "Summary is too long.");
        var description = NormalizeOptionalText(request.Description, 8000, "Description is too long.") ?? string.Empty;
        var status = NormalizeCreateStatus(request.Status);
        var boundaryNote = NormalizeOptionalText(request.BoundaryNote, 500, "Boundary note is too long.");
        var boundary = BuildBoundary(request.Boundary);
        ValidateLocationBoundaryWithinDestination(boundary?.Geometry, destination.BoundaryGeometry);
        var coordinates = ResolveCoordinates(request, boundary?.Geometry);
        var locationTypeId = await ResolveLocationTypeIdAsync(request.LocationTypeId);
        var tagIds = await ValidateLocationTagIdsAsync(request.TagIds);
        var images = RegionalEntityImageRequestNormalizer.Normalize(request.Images, name);
        var now = DateTime.UtcNow;

        await using var transaction = await _db.Database.BeginTransactionAsync();

        var location = new Location
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
            BoundaryNote = boundaryNote,
            BoundaryShapeType = boundary?.ShapeType,
            CreatedAt = now,
            Images = images
                .Select(image => new EntityImage
                {
                    TargetType = EntityImageTargetType.Location,
                    Url = image.Url,
                    AltText = image.AltText,
                    Caption = image.Caption,
                    IsMain = image.IsMain,
                    SortOrder = image.SortOrder,
                    CreatedAt = now
                })
                .ToList(),
            LocationTags = tagIds
                .Select(tagId => new LocationTag { TagId = tagId })
                .ToList()
        };

        _db.Locations.Add(location);
        if (boundary?.Geometry is not null)
        {
            _db.Entry(location)
                .Property<Geometry?>(BoundaryGeometryPropertyName)
                .CurrentValue = boundary.Geometry;
        }

        await _db.SaveChangesAsync();

        location.InternalReferenceId = BuildInternalReferenceId(location.Id);
        await _db.SaveChangesAsync();
        await transaction.CommitAsync();

        return await GetLocationAsync(userId, request.RegionId, location.Id);
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
            .Include(item => item.Images)
            .Include(item => item.LocationTags)
            .SingleOrDefaultAsync(item => item.Id == locationId && item.Destination.RegionId == regionId)
            ?? throw new KeyNotFoundException("Location not found.");

        var boundary = BuildBoundary(request.Boundary);
        var destinationBoundary = await GetDestinationBoundaryAsync(location.DestinationId, regionId);

        if (request.DestinationId.HasValue)
        {
            destinationBoundary = await GetDestinationBoundaryAsync(request.DestinationId.Value, regionId);

            location.DestinationId = destinationBoundary.Id;
        }

        if (request.LocationTypeId.HasValue &&
            !await _db.LocationTypes.AnyAsync(item => item.Id == request.LocationTypeId.Value))
        {
            throw new InvalidOperationException("Location type is invalid.");
        }

        var effectiveBoundary = boundary?.Geometry ??
            _db.Entry(location)
                .Property<Geometry?>(BoundaryGeometryPropertyName)
                .CurrentValue;
        ValidateLocationBoundaryWithinDestination(effectiveBoundary, destinationBoundary.BoundaryGeometry);

        ApplyUpdate(location, request);

        if (boundary is not null)
        {
            _db.Entry(location)
                .Property<Geometry?>(BoundaryGeometryPropertyName)
                .CurrentValue = boundary.Geometry;
            location.BoundaryShapeType = boundary.ShapeType;
        }

        if (request.BoundaryNote is not null)
        {
            location.BoundaryNote = NormalizeOptionalText(request.BoundaryNote, 500, "Boundary note is too long.");
        }

        if (request.Images is not null)
        {
            ReplaceImages(location, request.Images, location.Name);
        }

        if (request.TagIds is not null)
        {
            var tagIds = await ValidateLocationTagIdsAsync(request.TagIds);
            ReplaceTags(location, tagIds);
        }

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

    private async Task<DestinationBoundaryProjection> GetDestinationBoundaryAsync(int destinationId, int regionId)
    {
        if (destinationId <= 0)
            throw new InvalidOperationException("Destination is invalid for the selected region.");

        return await _db.Destinations
            .AsNoTracking()
            .Where(destination => destination.Id == destinationId && destination.RegionId == regionId)
            .Select(destination => new DestinationBoundaryProjection
            {
                Id = destination.Id,
                BoundaryGeometry = EF.Property<Geometry?>(destination, BoundaryGeometryPropertyName)
            })
            .SingleOrDefaultAsync()
            ?? throw new InvalidOperationException("Destination is invalid for the selected region.");
    }

    private static void ValidateLocationBoundaryWithinDestination(
        Geometry? locationGeometry,
        Geometry? destinationGeometry)
    {
        if (locationGeometry is null)
            return;

        EnsureWgs84(locationGeometry, "Location boundary geometry is invalid.");

        if (destinationGeometry is null || destinationGeometry.IsEmpty)
            throw new InvalidOperationException("Parent destination must have a boundary before adding location zones.");

        EnsureWgs84(destinationGeometry, "Parent destination boundary is invalid.");

        if (!destinationGeometry.Covers(locationGeometry))
            throw new InvalidOperationException("Location zone must be inside the selected destination boundary.");
    }

    private static void EnsureWgs84(Geometry geometry, string errorMessage)
    {
        if (geometry.IsEmpty || !geometry.IsValid)
            throw new InvalidOperationException(errorMessage);

        if (geometry.SRID == 0)
        {
            geometry.SRID = 4326;
            return;
        }

        if (geometry.SRID != 4326)
            throw new InvalidOperationException(errorMessage);
    }

    private void ReplaceImages(
        Location location,
        IEnumerable<RegionalEntityImageInputDto> imageRequests,
        string fallbackAltText)
    {
        var images = RegionalEntityImageRequestNormalizer.Normalize(imageRequests, fallbackAltText);
        var now = DateTime.UtcNow;

        _db.EntityImages.RemoveRange(location.Images.Where(image => image.TargetType == EntityImageTargetType.Location));

        foreach (var image in images)
        {
            location.Images.Add(new EntityImage
            {
                TargetType = EntityImageTargetType.Location,
                LocationId = location.Id,
                Url = image.Url,
                AltText = image.AltText,
                Caption = image.Caption,
                IsMain = image.IsMain,
                SortOrder = image.SortOrder,
                CreatedAt = now
            });
        }
    }

    private static void ReplaceTags(Location location, IEnumerable<int> tagIds)
    {
        location.LocationTags.Clear();
        foreach (var tagId in tagIds)
        {
            location.LocationTags.Add(new LocationTag
            {
                LocationId = location.Id,
                TagId = tagId
            });
        }
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

        if (request.Summary is not null)
        {
            location.Summary = NormalizeOptionalText(request.Summary, 160, "Summary is too long.");
        }

        if (request.Description is not null)
        {
            location.Description = NormalizeOptionalText(request.Description, 8000, "Description is too long.") ?? string.Empty;
        }

        if (request.Status is not null)
        {
            location.Status = NormalizeUpdateStatus(request.Status);
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
        Geometry? boundaryGeometry)
    {
        if (request.Latitude.HasValue || request.Longitude.HasValue)
        {
            if (!request.Latitude.HasValue || !request.Longitude.HasValue)
                throw new InvalidOperationException("Latitude and longitude must be provided together.");

            ValidateLatitude(request.Latitude.Value);
            ValidateLongitude(request.Longitude.Value);
            return new LocationCoordinates(request.Latitude.Value, request.Longitude.Value);
        }

        if (boundaryGeometry is null)
            throw new InvalidOperationException("A location boundary or coordinates are required.");

        var centroid = boundaryGeometry.Centroid;
        ValidateLatitude(centroid.Y);
        ValidateLongitude(centroid.X);
        return new LocationCoordinates(centroid.Y, centroid.X);
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
            Images = row.Images,
            DetailRoute = "/locations/detail/" + row.Id,
            EditRoute = "/locations/edit/" + row.Id
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
            Images = item.Images,
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

    private static string BuildInternalReferenceId(int locationId) => $"LOC-{locationId:D6}";

    private sealed record BoundaryResult(string ShapeType, Geometry Geometry);

    private sealed record LocationCoordinates(double Latitude, double Longitude);

    private sealed class DestinationBoundaryProjection
    {
        public int Id { get; set; }
        public Geometry? BoundaryGeometry { get; set; }
    }

    private static string NormalizeUpdateStatus(string value)
    {
        var normalized = NormalizeToken(value);
        return normalized switch
        {
            "published" or "publish" => PublishedStatus,
            "draft" => DraftStatus,
            "archived" => ArchivedStatus,
            _ => throw new InvalidOperationException("Location status is invalid.")
        };
    }

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
        public List<RegionalEntityImageDto> Images { get; set; } = new();
    }
}
