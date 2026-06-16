using Geotrack.Application.DTOs.Admin;
using Geotrack.Application.Interfaces.Service;
using Geotrack.Application.Security;
using Geotrack.Application.Services;
using Geotrack.Domain.Entities.Core;
using Geotrack.Domain.Entities.Tags;
using Geotrack.Infrastructure.Spatial;
using System.Globalization;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;
using Microsoft.Extensions.Logging;

namespace Geotrack.Infrastructure.Services;

public class RegionalDestinationService : IRegionalDestinationService
{
    private const int DefaultTake = 25;
    private const int MaxTake = 50;
    private const string PublishedStatus = "Published";
    private const string DraftStatus = "Draft";
    private const string ArchivedStatus = "Archived";
    private const string ManagedRegionalImagePrefix = "/media/uploads/regional/";
    private const string NominatimBaseUrl = "https://nominatim.openstreetmap.org";
    private const int MaxBoundaryUploadBytes = 5 * 1024 * 1024;
    private static string ExistingEditRoute(int destinationId) => $"/destinations/edit/{destinationId}";
    private const string BoundaryGeometryPropertyName = "BoundaryGeometry";
    private static readonly GeometryFactory Wgs84GeometryFactory = new(new PrecisionModel(), 4326);
    private static readonly SemaphoreSlim NominatimRequestGate = new(1, 1);
    private static readonly TimeSpan NominatimMinimumRequestInterval = TimeSpan.FromMilliseconds(1100);
    private static DateTimeOffset NominatimLastRequestStartedAt = DateTimeOffset.MinValue;

    private readonly AppDbContext _db;
    private readonly RegionalScopeValidationService _scopeValidation;
    private readonly IContentHtmlSanitizer _htmlSanitizer;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly DestinationBoundaryResolver _destinationBoundaryResolver;
    private readonly ILogger<RegionalDestinationService> _logger;

    public RegionalDestinationService(
        AppDbContext db,
        RegionalScopeValidationService scopeValidation,
        IContentHtmlSanitizer htmlSanitizer,
        IHttpClientFactory httpClientFactory,
        DestinationBoundaryResolver destinationBoundaryResolver,
        ILogger<RegionalDestinationService> logger)
    {
        _db = db;
        _scopeValidation = scopeValidation;
        _htmlSanitizer = htmlSanitizer;
        _httpClientFactory = httpClientFactory;
        _destinationBoundaryResolver = destinationBoundaryResolver;
        _logger = logger;
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
            .AsSplitQuery()
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
                HasBoundary = EF.Property<Geometry?>(destination, BoundaryGeometryPropertyName) != null,
                BoundarySource = destination.BoundarySource,
                BoundaryExternalKey = destination.BoundaryExternalKey,
                BoundaryDisplayName = destination.BoundaryDisplayName,
                BoundaryImportedAt = destination.BoundaryImportedAt,
                CreatedAt = destination.CreatedAt,
                UpdatedAt = destination.UpdatedAt,
                PublishedAt = destination.PublishedAt,
                Latitude = destination.Latitude,
                Longitude = destination.Longitude,
                CardImageUrl = destination.Images
                    .Where(image => image.TargetType == EntityImageTargetType.Destination && image.ImageRole == EntityImageRole.Card)
                    .OrderBy(image => image.SortOrder)
                    .ThenBy(image => image.CreatedAt)
                    .ThenBy(image => image.Id)
                    .Select(image => image.Url)
                    .FirstOrDefault(),
                CoverImageUrl = destination.Images
                    .Where(image => image.TargetType == EntityImageTargetType.Destination && image.ImageRole == EntityImageRole.Cover)
                    .OrderBy(image => image.SortOrder)
                    .ThenBy(image => image.CreatedAt)
                    .ThenBy(image => image.Id)
                    .Select(image => image.Url)
                    .FirstOrDefault(),
                GalleryImageUrls = destination.Images
                    .Where(image => image.TargetType == EntityImageTargetType.Destination && image.ImageRole == EntityImageRole.Gallery)
                    .OrderBy(image => image.SortOrder)
                    .ThenBy(image => image.CreatedAt)
                    .ThenBy(image => image.Id)
                    .Select(image => image.Url)
                    .ToList(),
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
            .AsSplitQuery()
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
                HasBoundary = EF.Property<Geometry?>(destination, BoundaryGeometryPropertyName) != null,
                BoundarySource = destination.BoundarySource,
                BoundaryExternalKey = destination.BoundaryExternalKey,
                BoundaryDisplayName = destination.BoundaryDisplayName,
                BoundaryImportedAt = destination.BoundaryImportedAt,
                CreatedAt = destination.CreatedAt,
                UpdatedAt = destination.UpdatedAt,
                PublishedAt = destination.PublishedAt,
                Latitude = destination.Latitude,
                Longitude = destination.Longitude,
                CardImageUrl = destination.Images
                    .Where(image => image.TargetType == EntityImageTargetType.Destination && image.ImageRole == EntityImageRole.Card)
                    .OrderBy(image => image.SortOrder)
                    .ThenBy(image => image.CreatedAt)
                    .ThenBy(image => image.Id)
                    .Select(image => image.Url)
                    .FirstOrDefault(),
                CoverImageUrl = destination.Images
                    .Where(image => image.TargetType == EntityImageTargetType.Destination && image.ImageRole == EntityImageRole.Cover)
                    .OrderBy(image => image.SortOrder)
                    .ThenBy(image => image.CreatedAt)
                    .ThenBy(image => image.Id)
                    .Select(image => image.Url)
                    .FirstOrDefault(),
                GalleryImageUrls = destination.Images
                    .Where(image => image.TargetType == EntityImageTargetType.Destination && image.ImageRole == EntityImageRole.Gallery)
                    .OrderBy(image => image.SortOrder)
                    .ThenBy(image => image.CreatedAt)
                    .ThenBy(image => image.Id)
                    .Select(image => image.Url)
                    .ToList(),
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

        var dto = MapDetail(row);
        dto.CanDelete = !await HasBlockingDependenciesAsync(row.Id);
        return dto;
    }

    public async Task<RegionalDestinationBoundarySuggestionsDto> SearchBoundarySuggestionsAsync(
        int userId,
        int regionId,
        string query,
        double? latitude = null,
        double? longitude = null,
        CancellationToken cancellationToken = default)
    {
        await EnsureAssignedRegionAsync(userId, regionId);

        var normalizedQuery = (query ?? string.Empty).Trim();
        if (normalizedQuery.Length < 2)
            throw new InvalidOperationException("Enter at least two characters to search for a boundary.");
        if (normalizedQuery.Length > 200)
            throw new InvalidOperationException("Boundary search query is too long.");
        if (latitude.HasValue != longitude.HasValue)
            throw new InvalidOperationException("Latitude and longitude must be provided together.");

        Point? requestedCenter = null;
        if (latitude.HasValue && longitude.HasValue)
        {
            ValidateLatitude(latitude.Value);
            ValidateLongitude(longitude.Value);
            requestedCenter = Wgs84GeometryFactory.CreatePoint(new Coordinate(longitude.Value, latitude.Value));
            requestedCenter.SRID = 4326;
        }

        var context = await _db.Regions
            .AsNoTracking()
            .Where(region => region.Id == regionId)
            .Select(region => new BoundarySearchContext
            {
                RegionName = region.Name,
                TerritoryName = region.Territory.Name,
                CountryCode = region.Territory.CountryCode,
                RegionBoundary = EF.Property<Geometry?>(region, BoundaryGeometryPropertyName)
            })
            .SingleAsync(cancellationToken);

        var regionBoundary = context.RegionBoundary;
        if (regionBoundary is null || regionBoundary.IsEmpty || !regionBoundary.IsValid)
            throw new InvalidOperationException("The assigned region does not have a valid boundary.");

        // A selected center is a strong disambiguation signal, but never trust a point outside
        // the manager's region when looking for candidate boundaries.
        if (requestedCenter is not null && !regionBoundary.Covers(requestedCenter))
            requestedCenter = null;

        List<NominatimBoundaryResult> providerResults;
        try
        {
            providerResults = await SearchNominatimBoundariesAsync(
                context,
                normalizedQuery,
                cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            _logger.LogWarning(ex, "Destination boundary provider search failed for region {RegionId}.", regionId);
            throw new InvalidOperationException(
                "Boundary suggestions are temporarily unavailable. You can upload a GeoJSON boundary instead.");
        }

        var candidates = new Dictionary<string, BoundarySuggestionCandidate>(StringComparer.OrdinalIgnoreCase);
        foreach (var providerResult in providerResults)
        {
            AddNominatimBoundaryCandidate(
                candidates,
                providerResult,
                normalizedQuery,
                regionBoundary,
                requestedCenter,
                isReverseLookup: false);
        }

        // Nominatim often stores a city both as a place node and as an administrative relation.
        // Always add one center-based city-level lookup when a center is available, even if the
        // text search found some polygons. This prevents a minor similarly named polygon from
        // suppressing the actual municipality/city boundary. A town-level lookup is only needed
        // when nothing usable was found at all.
        if (requestedCenter is not null)
        {
            foreach (var zoom in candidates.Count == 0 ? new[] { 10, 12 } : new[] { 10 })
            {
                var countBeforeLookup = candidates.Count;
                try
                {
                    var reverseResult = await ReverseNominatimBoundaryAsync(
                        requestedCenter.Y,
                        requestedCenter.X,
                        context.CountryCode,
                        zoom,
                        cancellationToken);

                    if (reverseResult is not null)
                    {
                        AddNominatimBoundaryCandidate(
                            candidates,
                            reverseResult,
                            normalizedQuery,
                            regionBoundary,
                            requestedCenter,
                            isReverseLookup: true);
                    }
                }
                catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
                {
                    _logger.LogWarning(
                        ex,
                        "Destination boundary reverse lookup failed for region {RegionId} at zoom {Zoom}.",
                        regionId,
                        zoom);
                }

                if (candidates.Count > countBeforeLookup || candidates.Count > 0)
                    break;
            }
        }

        return new RegionalDestinationBoundarySuggestionsDto
        {
            Items = candidates.Values
                .OrderByDescending(candidate => candidate.Score)
                .ThenBy(candidate => candidate.Dto.DisplayName, StringComparer.OrdinalIgnoreCase)
                .Take(8)
                .Select(candidate => candidate.Dto)
                .ToList()
        };
    }

    public async Task<RegionalDestinationBoundarySuggestionDto> ParseUploadedBoundaryAsync(
        int userId,
        int regionId,
        string fileName,
        byte[] content,
        CancellationToken cancellationToken = default)
    {
        await EnsureAssignedRegionAsync(userId, regionId);

        if (content is null || content.Length == 0)
            throw new InvalidOperationException("GeoJSON file is required.");
        if (content.Length > MaxBoundaryUploadBytes)
            throw new InvalidOperationException("GeoJSON boundary file must be 5 MB or smaller.");

        var extension = Path.GetExtension(fileName ?? string.Empty);
        if (!extension.Equals(".geojson", StringComparison.OrdinalIgnoreCase) &&
            !extension.Equals(".json", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Boundary upload must be a .geojson or .json file.");
        }

        string geoJson;
        try
        {
            geoJson = new System.Text.UTF8Encoding(false, true).GetString(content);
        }
        catch (DecoderFallbackException)
        {
            throw new InvalidOperationException("Boundary file must use UTF-8 encoding.");
        }

        var geometry = DestinationBoundaryGeometry.ParseGeoJson(geoJson);
        await EnsureBoundaryInsideRegionAsync(regionId, geometry, cancellationToken);

        var center = geometry.InteriorPoint;
        return new RegionalDestinationBoundarySuggestionDto
        {
            Source = "UploadedGeoJson",
            DisplayName = Path.GetFileName(fileName),
            GeometryType = geometry.GeometryType,
            GeoJson = DestinationBoundaryGeometry.ToGeoJson(geometry),
            CenterLatitude = center.Y,
            CenterLongitude = center.X
        };
    }

    public async Task<DestinationCoordinateResolutionDto> ResolveDestinationAsync(
        int userId,
        int regionId,
        double latitude,
        double longitude,
        CancellationToken cancellationToken = default)
    {
        await EnsureAssignedRegionAsync(userId, regionId);
        var resolution = await _destinationBoundaryResolver.ResolveInRegionAsync(
            regionId,
            latitude,
            longitude,
            cancellationToken);
        return DestinationBoundaryResolver.ToDto(resolution);
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
        var description = NormalizeRichDescription(request.Description, 8000, "Description is too long.");
        var status = NormalizeCreateStatus(request.Status);
        var boundary = BuildBoundary(request.Boundary)
            ?? throw new InvalidOperationException("A destination boundary is required. Select a suggested boundary or upload a GeoJSON file.");
        var coordinates = ResolveCoordinates(request, boundary);
        var centerPoint = Wgs84GeometryFactory.CreatePoint(new Coordinate(coordinates.Longitude, coordinates.Latitude));
        centerPoint.SRID = 4326;
        if (!boundary.Geometry.Covers(centerPoint))
            throw new InvalidOperationException("Destination center must be inside the selected boundary.");
        await _scopeValidation.EnsurePointAllowedAsync(
            userId,
            request.RegionId,
            "Destination center",
            coordinates.Latitude,
            coordinates.Longitude,
            "Create");
        await EnsureBoundaryInsideRegionAsync(request.RegionId, boundary.Geometry);
        await EnsureBoundaryDoesNotOverlapDestinationsAsync(request.RegionId, boundary.Geometry);
        var destinationTypeId = await ResolveDestinationTypeIdAsync(request.DestinationTypeId);
        var tagIds = await ValidateDestinationTagIdsAsync(request.TagIds);
        var now = DateTime.UtcNow;

        Destination? destination = null;
        await _db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var transaction = await _db.Database.BeginTransactionAsync();

            destination = new Destination
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
                BoundaryNote = NormalizeOptionalText(request.BoundaryNote, 500, "Boundary note is too long."),
                BoundaryShapeType = boundary.ShapeType,
                BoundarySource = boundary.Source,
                BoundaryExternalKey = boundary.ExternalKey,
                BoundaryDisplayName = boundary.DisplayName,
                BoundaryImportedAt = now,
                CreatedAt = now,
                DestinationTags = tagIds
                    .Select(tagId => new DestinationTag { TagId = tagId })
                    .ToList()
            };

            if (!string.IsNullOrWhiteSpace(boundary.ExternalKey) &&
                await _db.Destinations.AnyAsync(item => item.BoundaryExternalKey == boundary.ExternalKey))
            {
                throw new InvalidOperationException("This boundary is already assigned to another destination.");
            }

            _db.Destinations.Add(destination);
            _db.Entry(destination).Property<Geometry?>(BoundaryGeometryPropertyName).CurrentValue = boundary.Geometry;

            await _db.SaveChangesAsync();

            _db.EntityImages.AddRange(AddRequiredCreateImages(request, destination.Id, destination.Name, now));

            destination.InternalReferenceId = BuildInternalReferenceId(destination.Id);
            await _db.SaveChangesAsync();
            await transaction.CommitAsync();
        });

        return await GetDestinationAsync(userId, request.RegionId, destination!.Id);
    }

    public async Task<RegionalDestinationDetailDto> UpdateDestinationAsync(
        int userId,
        int regionId,
        int destinationId,
        RegionalDestinationUpdateRequestDto request)
    {
        await EnsureAssignedRegionAsync(userId, regionId);

        var destination = await _db.Destinations
            .Include(item => item.DestinationTags)
            .Include(item => item.Images)
            .SingleOrDefaultAsync(item => item.Id == destinationId && item.RegionId == regionId)
            ?? throw new KeyNotFoundException("Destination not found.");

        if (request.DestinationTypeId.HasValue &&
            !await _db.DestinationTypes.AnyAsync(item => item.Id == request.DestinationTypeId.Value))
        {
            throw new InvalidOperationException("Destination type is invalid.");
        }

        ApplyUpdate(destination, request);

        var replacementBoundary = BuildBoundary(request.Boundary);
        var effectiveBoundary = replacementBoundary?.Geometry
            ?? _db.Entry(destination).Property<Geometry?>(BoundaryGeometryPropertyName).CurrentValue;

        if (replacementBoundary is not null)
        {
            await EnsureBoundaryInsideRegionAsync(regionId, replacementBoundary.Geometry);
            await EnsureBoundaryDoesNotOverlapDestinationsAsync(
                regionId,
                replacementBoundary.Geometry,
                destinationId);

            if (!string.IsNullOrWhiteSpace(replacementBoundary.ExternalKey) &&
                await _db.Destinations.AnyAsync(item =>
                    item.Id != destinationId &&
                    item.BoundaryExternalKey == replacementBoundary.ExternalKey))
            {
                throw new InvalidOperationException("This boundary is already assigned to another destination.");
            }

            _db.Entry(destination).Property<Geometry?>(BoundaryGeometryPropertyName).CurrentValue = replacementBoundary.Geometry;
            destination.BoundaryShapeType = replacementBoundary.ShapeType;
            destination.BoundarySource = replacementBoundary.Source;
            destination.BoundaryExternalKey = replacementBoundary.ExternalKey;
            destination.BoundaryDisplayName = replacementBoundary.DisplayName;
            destination.BoundaryImportedAt = DateTime.UtcNow;
            destination.BoundaryNote = NormalizeOptionalText(request.BoundaryNote, 500, "Boundary note is too long.");
        }
        else if (request.BoundaryNote is not null)
        {
            destination.BoundaryNote = NormalizeOptionalText(request.BoundaryNote, 500, "Boundary note is too long.");
        }

        if (request.Latitude.HasValue || request.Longitude.HasValue || replacementBoundary is not null)
        {
            await _scopeValidation.EnsurePointAllowedAsync(
                userId,
                regionId,
                "Destination center",
                destination.Latitude,
                destination.Longitude,
                "Update");

            if (effectiveBoundary is not null && !effectiveBoundary.IsEmpty)
            {
                var centerPoint = Wgs84GeometryFactory.CreatePoint(new Coordinate(destination.Longitude, destination.Latitude));
                centerPoint.SRID = 4326;
                if (!effectiveBoundary.Covers(centerPoint))
                    throw new InvalidOperationException("Destination center must be inside the selected boundary.");
            }
        }

        if (request.TagIds is not null)
        {
            var tagIds = await ValidateDestinationTagIdsAsync(request.TagIds);
            destination.DestinationTags.Clear();
            destination.DestinationTags = tagIds
                .Select(tagId => new DestinationTag { DestinationId = destination.Id, TagId = tagId })
                .ToList();
        }

        var now = DateTime.UtcNow;
        ApplyImageUpdates(destination, request, now);
        destination.UpdatedAt = now;

        await _db.SaveChangesAsync();
        return await GetDestinationAsync(userId, regionId, destinationId);
    }

    public async Task DeleteDestinationAsync(int userId, int regionId, int destinationId)
    {
        await EnsureAssignedRegionAsync(userId, regionId);

        var destination = await _db.Destinations
            .Include(item => item.DestinationTags)
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

    public async Task<RegionalDestinationTagDto> CreateDestinationTagAsync(
        int userId,
        RegionalTagCreateRequestDto request)
    {
        await EnsureRegionalManagerRoleAsync(userId);

        var tag = await RegionalTagWriter.CreateScopedTagAsync(
            _db,
            request.Name,
            RegionalTagScope.Destination);

        return new RegionalDestinationTagDto
        {
            Id = tag.Id,
            Name = tag.Name,
            Slug = tag.Slug
        };
    }

    private async Task EnsureBoundaryInsideRegionAsync(
        int regionId,
        Geometry boundary,
        CancellationToken cancellationToken = default)
    {
        var regionBoundary = await _db.Regions
            .AsNoTracking()
            .Where(region => region.Id == regionId)
            .Select(region => EF.Property<Geometry?>(region, BoundaryGeometryPropertyName))
            .SingleOrDefaultAsync(cancellationToken);

        if (regionBoundary is null || regionBoundary.IsEmpty || !regionBoundary.IsValid)
            throw new InvalidOperationException("The assigned region does not have a valid boundary.");

        if (!regionBoundary.Covers(boundary))
        {
            // Suggested boundaries are clipped to the region and then serialized to GeoJSON with
            // seven decimal places. Allow only a tiny round-trip tolerance so points lying exactly
            // on the region edge do not fail after serialization.
            const double serializationToleranceDegrees = 0.000001d;
            if (!regionBoundary.Buffer(serializationToleranceDegrees).Covers(boundary))
                throw new InvalidOperationException("Destination boundary must be completely inside the assigned region.");
        }
    }

    private async Task EnsureBoundaryDoesNotOverlapDestinationsAsync(
        int regionId,
        Geometry boundary,
        int? excludedDestinationId = null,
        CancellationToken cancellationToken = default)
    {
        var query = _db.Destinations
            .AsNoTracking()
            .Where(destination => destination.RegionId == regionId)
            .Where(destination => EF.Property<Geometry?>(destination, BoundaryGeometryPropertyName) != null)
            .Where(destination => EF.Property<Geometry>(destination, BoundaryGeometryPropertyName).Intersects(boundary));

        if (excludedDestinationId.HasValue)
            query = query.Where(destination => destination.Id != excludedDestinationId.Value);

        var candidates = await query
            .Select(destination => new DestinationBoundaryConflictCandidate
            {
                Id = destination.Id,
                Name = destination.Name,
                Boundary = EF.Property<Geometry>(destination, BoundaryGeometryPropertyName)
            })
            .ToListAsync(cancellationToken);

        foreach (var candidate in candidates)
        {
            var intersection = candidate.Boundary.Intersection(boundary);
            if (!intersection.IsEmpty && intersection.Area > 0d)
            {
                throw new InvalidOperationException(
                    $"Destination boundary overlaps destination '{candidate.Name}'. Destination boundaries may share an edge, but their areas cannot overlap.");
            }
        }
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

    private void ApplyUpdate(Destination destination, RegionalDestinationUpdateRequestDto request)
    {
        if (request.Name is not null)
        {
            destination.Name = NormalizeRequiredText(
                request.Name,
                "Destination name is required.",
                200,
                "Destination name is too long.");
        }

        if (request.Summary is not null)
        {
            destination.Summary = NormalizeOptionalText(request.Summary, 160, "Summary is too long.");
        }

        if (request.Description is not null)
        {
            destination.Description = NormalizeRichDescription(request.Description, 8000, "Description is too long.");
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

        if (!string.IsNullOrWhiteSpace(boundary.GeoJson))
        {
            var geometry = DestinationBoundaryGeometry.ParseGeoJson(boundary.GeoJson);
            var source = NormalizeBoundarySource(boundary.Source);
            var externalKey = NormalizeOptionalText(boundary.ExternalKey, 160, "Boundary external key is too long.");
            var displayName = NormalizeOptionalText(boundary.DisplayName, 500, "Boundary display name is too long.");

            if (source == "OpenStreetMap" && string.IsNullOrWhiteSpace(externalKey))
                throw new InvalidOperationException("OpenStreetMap boundary is missing its external key.");

            return new BoundaryResult(geometry.GeometryType, geometry, source, externalKey, displayName);
        }

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
            ringCoordinates.Add(new Coordinate(first.X, first.Y));

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

        var normalized = DestinationBoundaryGeometry.NormalizePolygonal(new Geometry[] { polygon })
            ?? throw new InvalidOperationException("Boundary geometry is invalid.");
        return new BoundaryResult(shapeType, normalized, "Manual", null, "Manually drawn boundary");
    }

    private static string NormalizeBoundarySource(string? value)
    {
        var normalized = NormalizeToken(value ?? string.Empty);
        return normalized switch
        {
            "openstreetmap" or "osm" => "OpenStreetMap",
            "uploadedgeojson" or "uploaded" or "geojson" => "UploadedGeoJson",
            "manual" => "Manual",
            _ => throw new InvalidOperationException("Boundary source is invalid.")
        };
    }

    private static DestinationCoordinates ResolveCoordinates(
        RegionalDestinationCreateRequestDto request,
        BoundaryResult? boundary)
    {
        if (!request.Latitude.HasValue || !request.Longitude.HasValue)
        {
            if (boundary is not null)
            {
                var center = boundary.Geometry.InteriorPoint;
                return new DestinationCoordinates(center.Y, center.X);
            }

            throw new InvalidOperationException("Destination center coordinates are required.");
        }

        ValidateLatitude(request.Latitude.Value);
        ValidateLongitude(request.Longitude.Value);
        return new DestinationCoordinates(request.Latitude.Value, request.Longitude.Value);
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

    private string NormalizeRichDescription(string? value, int maxLength, string tooLongMessage)
    {
        var normalized = _htmlSanitizer.SanitizeRichText(value);
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
            HasBoundary = row.HasBoundary,
            BoundarySource = row.BoundarySource,
            BoundaryExternalKey = row.BoundaryExternalKey,
            BoundaryDisplayName = row.BoundaryDisplayName,
            BoundaryImportedAt = row.BoundaryImportedAt,
            Tags = row.Tags,
            CreatedAt = row.CreatedAt,
            PublishedAt = row.PublishedAt,
            LastUpdatedAt = row.UpdatedAt ?? row.CreatedAt,
            ImageUrl = row.ImageUrl,
            CardImageUrl = row.CardImageUrl,
            CoverImageUrl = row.CoverImageUrl,
            GalleryImageUrls = row.GalleryImageUrls,
            DetailRoute = "/destinations/detail/" + row.Id,
            EditRoute = ExistingEditRoute(row.Id)
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
            HasBoundary = item.HasBoundary,
            BoundarySource = item.BoundarySource,
            BoundaryExternalKey = item.BoundaryExternalKey,
            BoundaryDisplayName = item.BoundaryDisplayName,
            BoundaryImportedAt = item.BoundaryImportedAt,
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

    private static string? BuildSummary(string? description)
    {
        if (string.IsNullOrWhiteSpace(description))
            return null;

        var normalized = string.Join(" ", description.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return normalized.Length <= 160 ? normalized : normalized[..157] + "...";
    }

    private static void ApplyImageUpdates(
        Destination destination,
        RegionalDestinationUpdateRequestDto request,
        DateTime now)
    {
        if (!string.IsNullOrWhiteSpace(request.CardImageUrl))
        {
            UpsertImage(
                destination.Images,
                destination.Id,
                EntityImageRole.Card,
                NormalizeManagedImageUrl(request.CardImageUrl, "Card image must be uploaded before it can be assigned."),
                destination.Name,
                isMain: true,
                sortOrder: 0,
                now);
        }

        if (!string.IsNullOrWhiteSpace(request.CoverImageUrl))
        {
            UpsertImage(
                destination.Images,
                destination.Id,
                EntityImageRole.Cover,
                NormalizeManagedImageUrl(request.CoverImageUrl, "Cover image must be uploaded before it can be assigned."),
                destination.Name,
                isMain: false,
                sortOrder: 1,
                now);
        }

        if (request.GalleryImageUrls is not null)
        {
            var existingGalleryImages = destination.Images
                .Where(item => item.TargetType == EntityImageTargetType.Destination &&
                               item.DestinationId == destination.Id &&
                               item.ImageRole == EntityImageRole.Gallery)
                .ToList();

            foreach (var image in existingGalleryImages)
            {
                destination.Images.Remove(image);
            }

            var galleryUrls = request.GalleryImageUrls
                .Select(NormalizeOptionalImageUrl)
                .Where(url => !string.IsNullOrWhiteSpace(url))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            for (var i = 0; i < galleryUrls.Count; i++)
            {
                destination.Images.Add(new EntityImage
                {
                    TargetType = EntityImageTargetType.Destination,
                    DestinationId = destination.Id,
                    Url = galleryUrls[i]!,
                    AltText = destination.Name,
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
        int destinationId,
        string role,
        string url,
        string altText,
        bool isMain,
        int sortOrder,
        DateTime now)
    {
        var image = images.FirstOrDefault(item =>
            item.TargetType == EntityImageTargetType.Destination &&
            item.DestinationId == destinationId &&
            item.ImageRole == role);

        if (image is null)
        {
            images.Add(new EntityImage
            {
                TargetType = EntityImageTargetType.Destination,
                DestinationId = destinationId,
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

    private static string NormalizeManagedImageUrl(string? value, string message)
    {
        var normalized = NormalizeRequiredImageUrl(value, message);
        if (!normalized.StartsWith(ManagedRegionalImagePrefix, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Image must be uploaded before it can be assigned.");

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

    private sealed record BoundaryResult(string ShapeType, Geometry Geometry, string Source, string? ExternalKey, string? DisplayName);

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
        public bool HasBoundary { get; set; }
        public string? BoundarySource { get; set; }
        public string? BoundaryExternalKey { get; set; }
        public string? BoundaryDisplayName { get; set; }
        public DateTime? BoundaryImportedAt { get; set; }
        public List<RegionalDestinationTagDto> Tags { get; set; } = new();
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
    private async Task<List<NominatimBoundaryResult>> SearchNominatimBoundariesAsync(
        BoundarySearchContext context,
        string query,
        CancellationToken cancellationToken)
    {
        var regionBoundary = context.RegionBoundary
            ?? throw new InvalidOperationException("The assigned region does not have a valid boundary.");
        var envelope = regionBoundary.EnvelopeInternal;
        var language = string.IsNullOrWhiteSpace(context.CountryCode)
            ? "en"
            : $"en,{context.CountryCode.Trim().ToLowerInvariant()}";

        // Keep the text query broad. The country code is a hard filter and the region envelope is
        // only a ranking hint. bounded=1 was intentionally removed because OSM and geoBoundaries
        // coastlines/admin lines often differ slightly and were excluding valid city relations.
        var url = $"{NominatimBaseUrl}/search?format=jsonv2" +
                  "&polygon_geojson=1&polygon_threshold=0.00005" +
                  "&addressdetails=1&namedetails=1&extratags=1" +
                  "&dedupe=0&limit=40" +
                  $"&q={Uri.EscapeDataString(query)}" +
                  $"&viewbox={FormatCoordinate(envelope.MinX)},{FormatCoordinate(envelope.MaxY)},{FormatCoordinate(envelope.MaxX)},{FormatCoordinate(envelope.MinY)}" +
                  $"&accept-language={Uri.EscapeDataString(language)}";

        if (!string.IsNullOrWhiteSpace(context.CountryCode))
            url += $"&countrycodes={Uri.EscapeDataString(context.CountryCode.Trim().ToLowerInvariant())}";

        return await GetNominatimJsonAsync<List<NominatimBoundaryResult>>(url, cancellationToken)
            ?? new List<NominatimBoundaryResult>();
    }

    private async Task<NominatimBoundaryResult?> ReverseNominatimBoundaryAsync(
        double latitude,
        double longitude,
        string? countryCode,
        int zoom,
        CancellationToken cancellationToken)
    {
        var language = string.IsNullOrWhiteSpace(countryCode)
            ? "en"
            : $"en,{countryCode.Trim().ToLowerInvariant()}";
        var url = $"{NominatimBaseUrl}/reverse?format=jsonv2" +
                  $"&lat={FormatCoordinate(latitude)}&lon={FormatCoordinate(longitude)}" +
                  $"&zoom={zoom}&layer=address" +
                  "&polygon_geojson=1&polygon_threshold=0.00005" +
                  "&addressdetails=1&namedetails=1&extratags=1" +
                  $"&accept-language={Uri.EscapeDataString(language)}";

        return await GetNominatimJsonAsync<NominatimBoundaryResult>(url, cancellationToken);
    }

    private async Task<T?> GetNominatimJsonAsync<T>(string url, CancellationToken cancellationToken)
    {
        await NominatimRequestGate.WaitAsync(cancellationToken);
        try
        {
            var elapsed = DateTimeOffset.UtcNow - NominatimLastRequestStartedAt;
            var remainingDelay = NominatimMinimumRequestInterval - elapsed;
            if (remainingDelay > TimeSpan.Zero)
                await Task.Delay(remainingDelay, cancellationToken);

            NominatimLastRequestStartedAt = DateTimeOffset.UtcNow;
            var client = _httpClientFactory.CreateClient("DestinationBoundaryProvider");
            return await client.GetFromJsonAsync<T>(url, cancellationToken);
        }
        finally
        {
            NominatimRequestGate.Release();
        }
    }

    private static void AddNominatimBoundaryCandidate(
        IDictionary<string, BoundarySuggestionCandidate> candidates,
        NominatimBoundaryResult providerResult,
        string query,
        Geometry regionBoundary,
        Point? requestedCenter,
        bool isReverseLookup)
    {
        if (providerResult.GeoJson.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
            return;

        var externalKey = TryBuildOsmExternalKey(providerResult.OsmType, providerResult.OsmId);
        if (externalKey is null)
            return;

        Geometry providerGeometry;
        try
        {
            providerGeometry = DestinationBoundaryGeometry.ParseGeoJson(providerResult.GeoJson.GetRawText());
        }
        catch (InvalidOperationException)
        {
            // Point/line search results are expected and are not destination boundaries.
            return;
        }

        if (!TryConstrainBoundaryToRegion(
                providerGeometry,
                regionBoundary,
                requestedCenter,
                out var geometry,
                out var overlapRatio))
        {
            return;
        }

        var score = CalculateBoundaryCandidateScore(
            providerResult,
            query,
            geometry,
            regionBoundary,
            overlapRatio,
            requestedCenter is not null,
            isReverseLookup);
        if (score < 35d)
            return;

        Point center;
        if (requestedCenter is not null && geometry.Covers(requestedCenter))
            center = requestedCenter;
        else
            center = geometry.InteriorPoint;

        var dto = new RegionalDestinationBoundarySuggestionDto
        {
            Source = "OpenStreetMap",
            ExternalKey = externalKey,
            DisplayName = NormalizeProviderDisplayName(providerResult.DisplayName ?? providerResult.Name, query),
            Category = providerResult.Category,
            ProviderType = providerResult.AddressType ?? providerResult.Type,
            GeometryType = geometry.GeometryType,
            GeoJson = DestinationBoundaryGeometry.ToGeoJson(geometry),
            CenterLatitude = center.Y,
            CenterLongitude = center.X
        };

        if (!candidates.TryGetValue(externalKey, out var existing) || score > existing.Score)
            candidates[externalKey] = new BoundarySuggestionCandidate(dto, score);
    }

    private static bool TryConstrainBoundaryToRegion(
        Geometry candidate,
        Geometry regionBoundary,
        Point? requestedCenter,
        out Geometry constrained,
        out double overlapRatio)
    {
        constrained = null!;
        overlapRatio = 0d;

        var normalizedCandidate = DestinationBoundaryGeometry.NormalizePolygonal(new[] { candidate });
        if (normalizedCandidate is null || normalizedCandidate.IsEmpty || !normalizedCandidate.IsValid)
            return false;

        try
        {
            // If the manager selected a center, only keep polygons that actually contain it.
            // This removes similarly named places elsewhere in the same county/country.
            if (requestedCenter is not null && !normalizedCandidate.Covers(requestedCenter))
                return false;

            if (regionBoundary.Covers(normalizedCandidate))
            {
                constrained = normalizedCandidate;
                overlapRatio = 1d;
                return true;
            }

            if (!regionBoundary.Intersects(normalizedCandidate))
                return false;

            var intersection = normalizedCandidate.Intersection(regionBoundary);
            var normalizedIntersection = DestinationBoundaryGeometry.NormalizePolygonal(new[] { intersection });
            if (normalizedIntersection is null || normalizedIntersection.IsEmpty || !normalizedIntersection.IsValid)
                return false;

            var candidateArea = normalizedCandidate.Area;
            if (candidateArea <= 0d)
                return false;

            overlapRatio = normalizedIntersection.Area / candidateArea;

            // OSM administrative polygons and geoBoundaries regions often disagree along coasts
            // and may include different water areas. Keep a conservative minimum overlap, then
            // clip the candidate to the assigned region. A selected center makes the match safer.
            var minimumOverlap = requestedCenter is null ? 0.10d : 0.05d;
            if (!double.IsFinite(overlapRatio) || overlapRatio < minimumOverlap)
                return false;

            if (requestedCenter is not null && !normalizedIntersection.Covers(requestedCenter))
                return false;

            constrained = normalizedIntersection;
            return true;
        }
        catch (TopologyException)
        {
            return false;
        }
    }

    private static double CalculateBoundaryCandidateScore(
        NominatimBoundaryResult result,
        string query,
        Geometry geometry,
        Geometry regionBoundary,
        double overlapRatio,
        bool hasRequestedCenter,
        bool isReverseLookup)
    {
        var names = new List<string?>
        {
            result.Name,
            result.DisplayName?.Split(',', 2)[0]
        };

        if (result.NameDetails is not null)
            names.AddRange(result.NameDetails.Values);
        if (result.Address is not null)
            names.AddRange(result.Address.Values);

        var nameScore = names
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => CalculateBoundaryNameMatchScore(value!, query))
            .DefaultIfEmpty(0d)
            .Max();

        var score = nameScore;
        var category = NormalizeBoundarySearchText(result.Category ?? string.Empty);
        var type = NormalizeBoundarySearchText(result.Type ?? string.Empty);
        var addressType = NormalizeBoundarySearchText(result.AddressType ?? string.Empty);

        if (category == "boundary" && type == "administrative")
            score += 24d;
        if (addressType is "city" or "town" or "village" or "municipality" or "borough")
            score += 28d;
        if (type is "city" or "town" or "village" or "municipality" or "protected area" or "national park" or "nature reserve" or "mountain range")
            score += 20d;
        if (category is "natural" or "leisure")
            score += 12d;
        if (string.Equals(result.OsmType, "relation", StringComparison.OrdinalIgnoreCase))
            score += 6d;
        if (hasRequestedCenter)
            score += 14d;
        if (isReverseLookup)
            score += 8d;
        if (result.Importance.HasValue && double.IsFinite(result.Importance.Value))
            score += Math.Clamp(result.Importance.Value, 0d, 1d) * 10d;

        score += Math.Clamp(overlapRatio, 0d, 1d) * 8d;

        if (regionBoundary.Area > 0d)
        {
            var regionCoverage = geometry.Area / regionBoundary.Area;
            if (regionCoverage > 0.85d)
                score -= 42d;
            else if (regionCoverage > 0.50d)
                score -= 20d;
        }

        return score;
    }

    private static double CalculateBoundaryNameMatchScore(string candidate, string query)
    {
        var normalizedCandidate = NormalizeBoundarySearchText(candidate);
        var normalizedQuery = NormalizeBoundarySearchText(query);
        if (normalizedCandidate.Length == 0 || normalizedQuery.Length == 0)
            return 0d;
        if (normalizedCandidate == normalizedQuery)
            return 120d;

        var paddedCandidate = $" {normalizedCandidate} ";
        var paddedQuery = $" {normalizedQuery} ";
        if (paddedCandidate.Contains(paddedQuery, StringComparison.Ordinal))
            return 96d;
        if (normalizedCandidate.StartsWith(normalizedQuery + " ", StringComparison.Ordinal) ||
            normalizedCandidate.EndsWith(" " + normalizedQuery, StringComparison.Ordinal))
        {
            return 92d;
        }
        if (normalizedCandidate.Contains(normalizedQuery, StringComparison.Ordinal))
            return 82d;

        var queryTokens = normalizedQuery.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var candidateTokens = normalizedCandidate
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.Ordinal);
        if (queryTokens.Length == 0)
            return 0d;

        var matchedTokens = queryTokens.Count(candidateTokens.Contains);
        if (matchedTokens == queryTokens.Length)
            return 74d;

        return 55d * matchedTokens / queryTokens.Length;
    }

    private static string NormalizeBoundarySearchText(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var decomposed = value.Trim().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        var previousWasSpace = true;

        foreach (var character in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark)
                continue;

            if (char.IsLetterOrDigit(character))
            {
                builder.Append(char.ToLowerInvariant(character));
                previousWasSpace = false;
            }
            else if (!previousWasSpace)
            {
                builder.Append(' ');
                previousWasSpace = true;
            }
        }

        return builder.ToString().Trim();
    }

    private static string FormatCoordinate(double value) => value.ToString("0.#######", CultureInfo.InvariantCulture);

    private static string? TryBuildOsmExternalKey(string? osmType, long osmId)
    {
        var normalizedType = (osmType ?? string.Empty).Trim().ToLowerInvariant();
        if (osmId <= 0 ||
            (normalizedType != "node" && normalizedType != "way" && normalizedType != "relation"))
        {
            return null;
        }

        return $"osm:{normalizedType}:{osmId}";
    }

    private static string NormalizeProviderDisplayName(string? displayName, string fallback)
    {
        var normalized = (displayName ?? string.Empty).Trim();
        if (normalized.Length == 0)
            normalized = fallback;
        return normalized.Length <= 500 ? normalized : normalized[..500];
    }

    private sealed record BoundarySuggestionCandidate(
        RegionalDestinationBoundarySuggestionDto Dto,
        double Score);

    private sealed class BoundarySearchContext
    {
        public string RegionName { get; set; } = string.Empty;
        public string TerritoryName { get; set; } = string.Empty;
        public string? CountryCode { get; set; }
        public Geometry? RegionBoundary { get; set; }
    }

    private sealed class DestinationBoundaryConflictCandidate
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public Geometry Boundary { get; set; } = null!;
    }

    private sealed class NominatimBoundaryResult
    {
        [JsonPropertyName("osm_type")]
        public string? OsmType { get; set; }

        [JsonPropertyName("osm_id")]
        [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
        public long OsmId { get; set; }

        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("display_name")]
        public string? DisplayName { get; set; }

        [JsonPropertyName("category")]
        public string? Category { get; set; }

        [JsonPropertyName("type")]
        public string? Type { get; set; }

        [JsonPropertyName("addresstype")]
        public string? AddressType { get; set; }

        [JsonPropertyName("importance")]
        [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
        public double? Importance { get; set; }

        [JsonPropertyName("address")]
        public Dictionary<string, string?>? Address { get; set; }

        [JsonPropertyName("namedetails")]
        public Dictionary<string, string?>? NameDetails { get; set; }

        [JsonPropertyName("geojson")]
        public JsonElement GeoJson { get; set; }
    }

}
