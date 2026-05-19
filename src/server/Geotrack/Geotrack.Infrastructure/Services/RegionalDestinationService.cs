using Geotrack.Application.DTOs.Admin;
using Geotrack.Application.Interfaces.Service;
using Geotrack.Application.Security;
using Geotrack.Domain.Entities.Core;
using Geotrack.Domain.Entities.Tags;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;

namespace Geotrack.Infrastructure.Services;

public class RegionalDestinationService : IRegionalDestinationService
{
    private const int DefaultTake = 25;
    private const int MaxTake = 50;
    private const string PublishedStatus = "Published";
    private const string DraftStatus = "Draft";
    private const string ArchivedStatus = "Archived";
    private const string ExistingEditRoute = "/destinations/new";
    private const string BoundaryGeometryPropertyName = "BoundaryGeometry";
    private static readonly GeometryFactory Wgs84GeometryFactory = new(new PrecisionModel(), 4326);

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

        if (normalizedStatus != "all")
        {
            var statusName = StatusFromToken(normalizedStatus);
            query = query.Where(destination => destination.Status == statusName);
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
                Summary = destination.Summary,
                InternalReferenceId = destination.InternalReferenceId,
                Description = destination.Description,
                Status = destination.Status,
                RegionId = destination.RegionId,
                RegionName = destination.Region.Name,
                TerritoryName = destination.Region.Territory.Name,
                DestinationTypeId = destination.DestinationTypeId,
                DestinationTypeName = destination.DestinationType.Name,
                CreatedByUserId = destination.CreatedByUserId,
                BoundaryNote = destination.BoundaryNote,
                BoundaryShapeType = destination.BoundaryShapeType,
                CreatedAt = destination.CreatedAt,
                UpdatedAt = destination.UpdatedAt,
                PublishedAt = destination.PublishedAt,
                Latitude = destination.Latitude,
                Longitude = destination.Longitude,
                ImageUrl = destination.Images
                    .Where(image => image.TargetType == EntityImageTargetType.Destination)
                    .OrderBy(image => image.ImageRole == EntityImageRole.Card ? 0 : image.ImageRole == EntityImageRole.Cover ? 1 : 2)
                    .ThenBy(image => image.SortOrder)
                    .ThenBy(image => image.CreatedAt)
                    .ThenBy(image => image.Id)
                    .Select(image => image.Url)
                    .FirstOrDefault(),
                Tags = destination.DestinationTags
                    .OrderBy(destinationTag => destinationTag.Tag.Name)
                    .Select(destinationTag => new RegionalDestinationTagDto
                    {
                        Id = destinationTag.TagId,
                        Name = destinationTag.Tag.Name,
                        Slug = destinationTag.Tag.Slug
                    })
                    .ToList()
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
                Summary = destination.Summary,
                InternalReferenceId = destination.InternalReferenceId,
                Description = destination.Description,
                Status = destination.Status,
                RegionId = destination.RegionId,
                RegionName = destination.Region.Name,
                TerritoryName = destination.Region.Territory.Name,
                DestinationTypeId = destination.DestinationTypeId,
                DestinationTypeName = destination.DestinationType.Name,
                CreatedByUserId = destination.CreatedByUserId,
                BoundaryNote = destination.BoundaryNote,
                BoundaryShapeType = destination.BoundaryShapeType,
                CreatedAt = destination.CreatedAt,
                UpdatedAt = destination.UpdatedAt,
                PublishedAt = destination.PublishedAt,
                Latitude = destination.Latitude,
                Longitude = destination.Longitude,
                ImageUrl = destination.Images
                    .Where(image => image.TargetType == EntityImageTargetType.Destination)
                    .OrderBy(image => image.ImageRole == EntityImageRole.Card ? 0 : image.ImageRole == EntityImageRole.Cover ? 1 : 2)
                    .ThenBy(image => image.SortOrder)
                    .ThenBy(image => image.CreatedAt)
                    .ThenBy(image => image.Id)
                    .Select(image => image.Url)
                    .FirstOrDefault(),
                Tags = destination.DestinationTags
                    .OrderBy(destinationTag => destinationTag.Tag.Name)
                    .Select(destinationTag => new RegionalDestinationTagDto
                    {
                        Id = destinationTag.TagId,
                        Name = destinationTag.Tag.Name,
                        Slug = destinationTag.Tag.Slug
                    })
                    .ToList()
            })
            .SingleOrDefaultAsync()
            ?? throw new KeyNotFoundException("Destination not found.");

        return MapDetail(row);
    }

    public async Task<RegionalDestinationDetailDto> CreateDestinationAsync(
        int userId,
        RegionalDestinationCreateRequestDto request)
    {
        if (!string.IsNullOrWhiteSpace(request.InternalReferenceId))
            throw new InvalidOperationException("Internal reference id is generated by the server.");

        await EnsureAssignedRegionAsync(userId, request.RegionId);

        var name = NormalizeRequiredText(request.Name, "Destination name is required.", 200, "Destination name is too long.");
        var summary = NormalizeOptionalText(request.Summary, 160, "Summary is too long.");
        var description = NormalizeOptionalText(request.Description, 8000, "Description is too long.") ?? string.Empty;
        var status = NormalizeCreateStatus(request.Status);
        var boundaryNote = NormalizeOptionalText(request.BoundaryNote, 500, "Boundary note is too long.");
        var boundary = BuildBoundary(request.Boundary);
        var coordinates = ResolveCoordinates(request, boundary?.Geometry);
        var destinationTypeId = await ResolveDestinationTypeIdAsync(request.DestinationTypeId);
        var tagIds = await ValidateDestinationTagIdsAsync(request.TagIds);
        var now = DateTime.UtcNow;

        await using var transaction = await _db.Database.BeginTransactionAsync();

        var destination = new Destination
        {
            Name = name,
            Summary = summary,
            Description = description,
            Status = status,
            RegionId = request.RegionId,
            DestinationTypeId = destinationTypeId,
            Latitude = coordinates.Latitude,
            Longitude = coordinates.Longitude,
            CreatedByUserId = userId,
            PublishedAt = status == PublishedStatus ? now : null,
            BoundaryNote = boundaryNote,
            BoundaryShapeType = boundary?.ShapeType,
            CreatedAt = now,
            DestinationTags = tagIds
                .Select(tagId => new DestinationTag { TagId = tagId })
                .ToList()
        };

        _db.Destinations.Add(destination);
        if (boundary?.Geometry is not null)
        {
            _db.Entry(destination)
                .Property<Geometry?>(BoundaryGeometryPropertyName)
                .CurrentValue = boundary.Geometry;
        }

        await _db.SaveChangesAsync();

        _db.EntityImages.AddRange(AddRequiredCreateImages(request, destination.Id, destination.Name, now));

        destination.InternalReferenceId = BuildInternalReferenceId(destination.Id);
        await _db.SaveChangesAsync();
        await transaction.CommitAsync();

        return await GetDestinationAsync(userId, request.RegionId, destination.Id);
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

    public async Task<List<RegionalDestinationTagDto>> GetDestinationTagsAsync(int userId, string? search)
    {
        await EnsureRegionalManagerRoleAsync(userId);

        var query = _db.Tags
            .AsNoTracking()
            .Where(tag => tag.ForDestination);

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
            .Select(tag => new RegionalDestinationTagDto
            {
                Id = tag.Id,
                Name = tag.Name,
                Slug = tag.Slug
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

    private async Task<bool> HasBlockingDependenciesAsync(int destinationId)
    {
        var hasLocations = await _db.Locations
            .AsNoTracking()
            .AnyAsync(location => location.DestinationId == destinationId);
        if (hasLocations)
            return true;

        var hasEvents = await _db.Events
            .AsNoTracking()
            .AnyAsync(@event => @event.DestinationId == destinationId);
        if (hasEvents)
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
            (destination.Summary != null && destination.Summary.ToLower().Contains(term)) ||
            destination.Description.ToLower().Contains(term) ||
            (destination.InternalReferenceId != null && destination.InternalReferenceId.ToLower().Contains(term)) ||
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
            destination.Name = NormalizeRequiredText(
                request.Name,
                "Destination name is required.",
                200,
                "Destination name is too long.");
        }

        if (request.Description is not null)
        {
            destination.Description = NormalizeOptionalText(request.Description, 8000, "Description is too long.") ?? string.Empty;
        }

        if (request.DestinationTypeId.HasValue)
        {
            if (request.DestinationTypeId.Value <= 0)
                throw new InvalidOperationException("Destination type is invalid.");

            destination.DestinationTypeId = request.DestinationTypeId.Value;
        }

        if (request.Latitude.HasValue)
        {
            ValidateLatitude(request.Latitude.Value);
            destination.Latitude = request.Latitude.Value;
        }

        if (request.Longitude.HasValue)
        {
            ValidateLongitude(request.Longitude.Value);
            destination.Longitude = request.Longitude.Value;
        }
    }

    private async Task<int> ResolveDestinationTypeIdAsync(int? requestedDestinationTypeId)
    {
        if (requestedDestinationTypeId.HasValue)
        {
            if (requestedDestinationTypeId.Value <= 0)
                throw new InvalidOperationException("Destination type is invalid.");

            var exists = await _db.DestinationTypes
                .AsNoTracking()
                .AnyAsync(item => item.Id == requestedDestinationTypeId.Value);

            if (!exists)
                throw new InvalidOperationException("Destination type is invalid.");

            return requestedDestinationTypeId.Value;
        }

        var defaultType = await _db.DestinationTypes
            .AsNoTracking()
            .OrderBy(item => item.Name == "City" ? 0 : 1)
            .ThenBy(item => item.Id)
            .FirstOrDefaultAsync()
            ?? throw new InvalidOperationException("A destination type must exist before destinations can be created.");

        return defaultType.Id;
    }

    private async Task<List<int>> ValidateDestinationTagIdsAsync(IEnumerable<int>? requestedTagIds)
    {
        var tagIds = (requestedTagIds ?? Array.Empty<int>())
            .Where(id => id > 0)
            .Distinct()
            .ToList();

        if (tagIds.Count == 0)
            return tagIds;

        var compatibleTagIds = await _db.Tags
            .AsNoTracking()
            .Where(tag => tagIds.Contains(tag.Id) && tag.ForDestination)
            .Select(tag => tag.Id)
            .ToListAsync();

        if (compatibleTagIds.Count != tagIds.Count)
            throw new InvalidOperationException("All selected tags must be destination-compatible.");

        return tagIds;
    }

    private static BoundaryResult? BuildBoundary(RegionalDestinationBoundaryDto? boundary)
    {
        if (boundary is null)
            return null;

        var shapeType = NormalizeShapeType(boundary.ShapeType);
        var coordinates = boundary.Coordinates ?? new List<RegionalDestinationCoordinateDto>();

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

    private static DestinationCoordinates ResolveCoordinates(
        RegionalDestinationCreateRequestDto request,
        Geometry? boundaryGeometry)
    {
        if (request.Latitude.HasValue || request.Longitude.HasValue)
        {
            if (!request.Latitude.HasValue || !request.Longitude.HasValue)
                throw new InvalidOperationException("Latitude and longitude must be provided together.");

            ValidateLatitude(request.Latitude.Value);
            ValidateLongitude(request.Longitude.Value);
            return new DestinationCoordinates(request.Latitude.Value, request.Longitude.Value);
        }

        if (boundaryGeometry is null)
            throw new InvalidOperationException("A destination boundary or coordinates are required.");

        var centroid = boundaryGeometry.Centroid;
        ValidateLatitude(centroid.Y);
        ValidateLongitude(centroid.X);
        return new DestinationCoordinates(centroid.Y, centroid.X);
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
            _ => throw new InvalidOperationException("Destination status is invalid.")
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

    private static RegionalDestinationListItemDto MapListItem(DestinationProjection row)
    {
        return new RegionalDestinationListItemDto
        {
            Id = row.Id,
            Name = row.Name,
            Summary = row.Summary ?? BuildSummary(row.Description),
            Description = row.Description,
            Status = string.IsNullOrWhiteSpace(row.Status) ? PublishedStatus : row.Status,
            InternalReferenceId = row.InternalReferenceId,
            RegionId = row.RegionId,
            RegionDisplayName = BuildRegionDisplayName(row.RegionName, row.TerritoryName),
            DestinationTypeId = row.DestinationTypeId,
            DestinationTypeName = row.DestinationTypeName,
            CreatedByUserId = row.CreatedByUserId,
            BoundaryNote = row.BoundaryNote,
            BoundaryShapeType = row.BoundaryShapeType,
            Tags = row.Tags,
            CreatedAt = row.CreatedAt,
            PublishedAt = row.PublishedAt,
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
            InternalReferenceId = item.InternalReferenceId,
            RegionId = item.RegionId,
            RegionDisplayName = item.RegionDisplayName,
            DestinationTypeId = item.DestinationTypeId,
            DestinationTypeName = item.DestinationTypeName,
            CreatedByUserId = item.CreatedByUserId,
            BoundaryNote = item.BoundaryNote,
            BoundaryShapeType = item.BoundaryShapeType,
            Tags = item.Tags,
            CreatedAt = item.CreatedAt,
            PublishedAt = item.PublishedAt,
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


    private static List<EntityImage> AddRequiredCreateImages(RegionalDestinationCreateRequestDto request, int entityId, string entityName, DateTime now)
    {
        var cardUrl = NormalizeRequiredImageUrl(request.CardImageUrl, "Card image is required.");
        var coverUrl = NormalizeRequiredImageUrl(request.CoverImageUrl, "Cover image is required.");
        var images = new List<EntityImage>
        {
            new()
            {
                TargetType = EntityImageTargetType.Destination,
                DestinationId = entityId,
                Url = cardUrl,
                AltText = entityName,
                IsMain = true,
                ImageRole = EntityImageRole.Card,
                SortOrder = 0,
                CreatedAt = now
            },
            new()
            {
                TargetType = EntityImageTargetType.Destination,
                DestinationId = entityId,
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
                TargetType = EntityImageTargetType.Destination,
                DestinationId = entityId,
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

    private static string BuildInternalReferenceId(int destinationId) => $"DEST-{destinationId:D6}";

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

    private sealed record BoundaryResult(string ShapeType, Geometry Geometry);

    private sealed record DestinationCoordinates(double Latitude, double Longitude);

    private sealed class DestinationProjection
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? Summary { get; set; }
        public string? InternalReferenceId { get; set; }
        public string? Description { get; set; }
        public string Status { get; set; } = PublishedStatus;
        public int RegionId { get; set; }
        public string RegionName { get; set; } = string.Empty;
        public string? TerritoryName { get; set; }
        public int DestinationTypeId { get; set; }
        public string? DestinationTypeName { get; set; }
        public int? CreatedByUserId { get; set; }
        public string? BoundaryNote { get; set; }
        public string? BoundaryShapeType { get; set; }
        public List<RegionalDestinationTagDto> Tags { get; set; } = new();
        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
        public DateTime? PublishedAt { get; set; }
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public string? ImageUrl { get; set; }
    }
}
