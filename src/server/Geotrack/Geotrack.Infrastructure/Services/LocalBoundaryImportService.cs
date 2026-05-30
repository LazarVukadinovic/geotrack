using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Geotrack.Application.DTOs.Admin;
using Geotrack.Application.Interfaces.Service;
using Geotrack.Application.Options;
using Geotrack.Domain.Entities.Core;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NetTopologySuite.Geometries;
using NetTopologySuite.Operation.Union;

namespace Geotrack.Infrastructure.Services;

public sealed class LocalBoundaryImportService : ILocalBoundaryImportService
{
    private const string BoundaryGeometryPropertyName = "BoundaryGeometry";
    private const string BoundaryDisplayGeometryPropertyName =
        TerritoryBoundaryDisplayGeometry.BoundaryDisplayGeometryPropertyName;
    private const string GeoBoundariesSource = "geoBoundaries";
    private const string ReleaseType = "gbOpen";
    private const string DefaultLicense = "CC BY 4.0";
    private const string DisputedAdmLevel = "DISP";
    private const int DefaultMaxSummaryItems = 100;
    private const int DefaultSaveBatchSize = 10;
    private const int DefaultProgressLogInterval = 100;

    private static readonly GeometryFactory Wgs84GeometryFactory = new(new PrecisionModel(), 4326);

    private readonly AppDbContext _db;
    private readonly BoundaryImportOptions _options;
    private readonly IHostEnvironment _environment;
    private readonly ILogger<LocalBoundaryImportService> _logger;
    private readonly IMemoryCache? _cache;
    private readonly Dictionary<string, int?> _candidateCountryTerritoryIds = new(StringComparer.OrdinalIgnoreCase);

    public LocalBoundaryImportService(
        AppDbContext db,
        IOptions<BoundaryImportOptions> options,
        IHostEnvironment environment,
        ILogger<LocalBoundaryImportService> logger,
        IMemoryCache? cache = null)
    {
        _db = db;
        _options = options.Value;
        _environment = environment;
        _logger = logger;
        _cache = cache;
    }

    public async Task<BoundaryImportSummaryDto> ImportLocalBoundariesAsync(
        string admLevel,
        bool forceRefresh = false,
        string? countryIso3 = null,
        int? take = null,
        CancellationToken cancellationToken = default)
    {
        var normalizedLevel = NormalizeAdmLevel(admLevel);
        var normalizedCountryIso3 = NormalizeOptionalCountryFilter(countryIso3);
        var normalizedTake = NormalizeTakeLimit(take);

        return normalizedLevel switch
        {
            "ADM0" => await ImportAdm0Async(forceRefresh, cancellationToken),
            "ADM1" or "ADM2" => await ImportBoundaryCandidatesAsync(
                normalizedLevel,
                forceRefresh,
                normalizedCountryIso3,
                normalizedTake,
                cancellationToken),
            _ => throw new InvalidOperationException("Boundary ADM level must be ADM0, ADM1, or ADM2.")
        };
    }

    public async Task<BoundaryRegionCandidateResponseDto> GetRegionCandidatesAsync(
        string admLevel,
        string iso3,
        Geometry assignedTerritoryBoundary,
        string? search = null,
        int skip = 0,
        int? take = null,
        CancellationToken cancellationToken = default)
    {
        var normalizedLevel = NormalizeRegionAdmLevel(admLevel);
        var normalizedIso3 = CountryRegistry.NormalizeIso3ForBoundaryImport(iso3)
            ?? throw new InvalidOperationException("ISO3 country code is required.");
        var territoryBoundary = ToValidPolygonalGeometry(assignedTerritoryBoundary)
            ?? throw new InvalidOperationException("Assigned territory boundary geometry is invalid.");
        var normalizedSearch = search?.Trim();
        var totalCandidatesForLevel = await _db.AdministrativeBoundaryCandidates
            .AsNoTracking()
            .CountAsync(candidate =>
                candidate.Source == GeoBoundariesSource &&
                candidate.AdmLevel == normalizedLevel,
                cancellationToken);

        var rows = await _db.AdministrativeBoundaryCandidates
            .AsNoTracking()
            .Where(candidate =>
                candidate.Source == GeoBoundariesSource &&
                candidate.AdmLevel == normalizedLevel &&
                candidate.CountryIso3 == normalizedIso3)
            .OrderBy(candidate => candidate.ShapeName)
            .ThenBy(candidate => candidate.ShapeId)
            .Select(candidate => new BoundaryRegionCandidateDbRow
            {
                Id = candidate.Id,
                Source = candidate.Source,
                ReleaseType = candidate.ReleaseType,
                AdmLevel = candidate.AdmLevel,
                ShapeGroup = candidate.ShapeGroup,
                ShapeId = candidate.ShapeId,
                ShapeName = candidate.ShapeName,
                ShapeType = candidate.ShapeType,
                ExternalKey = candidate.ExternalKey,
                CountryIso3 = candidate.CountryIso3,
                CountryTerritoryId = candidate.CountryTerritoryId,
                BoundaryGeometry = EF.Property<Geometry?>(candidate, BoundaryGeometryPropertyName),
                BoundaryDisplayGeometry = EF.Property<Geometry?>(candidate, BoundaryDisplayGeometryPropertyName)
            })
            .ToListAsync(cancellationToken);

        var response = new BoundaryRegionCandidateResponseDto
        {
            AdmLevel = normalizedLevel,
            Iso3 = normalizedIso3,
            ScannedFeatures = totalCandidatesForLevel
        };

        foreach (var row in rows)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!string.IsNullOrWhiteSpace(normalizedSearch) &&
                !row.ShapeName.Contains(normalizedSearch, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            response.MatchedFeatures++;

            var geometry = ToValidPolygonalGeometry(row.BoundaryGeometry);
            var isInside = CandidateIntersectsAssignedTerritory(territoryBoundary, geometry);

            response.Items.Add(BuildRegionCandidateDto(
                row,
                geometry,
                isInsideAssignedTerritory: isInside,
                status: isInside ? "Available" : "InvalidOutsideTerritory",
                existingRegionId: null));
        }

        response.ReturnedCount = response.Items.Count;
        return response;
    }

    public async Task<BoundaryRegionCandidateDto?> FindRegionCandidateAsync(
        string admLevel,
        string iso3,
        string shapeId,
        Geometry assignedTerritoryBoundary,
        CancellationToken cancellationToken = default)
    {
        var result = await FindRegionCandidateGeometryAsync(
            admLevel,
            iso3,
            candidateId: null,
            shapeId,
            externalKey: null,
            assignedTerritoryBoundary,
            cancellationToken);

        return result?.Candidate;
    }

    internal async Task<BoundaryRegionCandidateGeometry?> FindRegionCandidateGeometryAsync(
        string admLevel,
        string iso3,
        int? candidateId,
        string? shapeId,
        string? externalKey,
        Geometry assignedTerritoryBoundary,
        CancellationToken cancellationToken = default)
    {
        var normalizedLevel = NormalizeRegionAdmLevel(admLevel);
        var normalizedIso3 = CountryRegistry.NormalizeIso3ForBoundaryImport(iso3)
            ?? throw new InvalidOperationException("ISO3 country code is required.");
        var normalizedShapeId = shapeId?.Trim();
        var normalizedExternalKey = externalKey?.Trim();
        if (!candidateId.HasValue &&
            string.IsNullOrWhiteSpace(normalizedShapeId) &&
            string.IsNullOrWhiteSpace(normalizedExternalKey))
        {
            throw new InvalidOperationException("Boundary candidate id is required.");
        }

        var territoryBoundary = ToValidPolygonalGeometry(assignedTerritoryBoundary)
            ?? throw new InvalidOperationException("Assigned territory boundary geometry is invalid.");

        var query = _db.AdministrativeBoundaryCandidates
            .AsNoTracking()
            .Where(candidate =>
                candidate.Source == GeoBoundariesSource &&
                candidate.AdmLevel == normalizedLevel &&
                candidate.CountryIso3 == normalizedIso3);

        query = candidateId.HasValue
            ? query.Where(candidate => candidate.Id == candidateId.Value)
            : !string.IsNullOrWhiteSpace(normalizedExternalKey)
            ? query.Where(candidate => candidate.ExternalKey == normalizedExternalKey)
            : query.Where(candidate => candidate.ShapeId == normalizedShapeId);

        var row = await query
            .OrderBy(candidate => candidate.Id)
            .Select(candidate => new BoundaryRegionCandidateDbRow
            {
                Id = candidate.Id,
                Source = candidate.Source,
                ReleaseType = candidate.ReleaseType,
                AdmLevel = candidate.AdmLevel,
                ShapeGroup = candidate.ShapeGroup,
                ShapeId = candidate.ShapeId,
                ShapeName = candidate.ShapeName,
                ShapeType = candidate.ShapeType,
                ExternalKey = candidate.ExternalKey,
                CountryIso3 = candidate.CountryIso3,
                CountryTerritoryId = candidate.CountryTerritoryId,
                BoundaryGeometry = EF.Property<Geometry?>(candidate, BoundaryGeometryPropertyName),
                BoundaryDisplayGeometry = EF.Property<Geometry?>(candidate, BoundaryDisplayGeometryPropertyName)
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (row is null)
            return null;

        var geometry = ToValidPolygonalGeometry(row.BoundaryGeometry);
        if (geometry is null || !CandidateIntersectsAssignedTerritory(territoryBoundary, geometry))
            return null;

        return new BoundaryRegionCandidateGeometry(
            BuildRegionCandidateDto(
                row,
                geometry,
                isInsideAssignedTerritory: true,
                status: "Available",
            existingRegionId: null),
            geometry);
    }

    private static BoundaryRegionCandidateDto BuildRegionCandidateDto(
        BoundaryRegionCandidateDbRow source,
        Geometry? geometry,
        bool isInsideAssignedTerritory,
        string status,
        int? existingRegionId)
    {
        var hasBoundary = geometry is not null;
        var displayGeometry = ToValidPolygonalGeometry(source.BoundaryDisplayGeometry);
        var hasDisplayBoundary = displayGeometry is not null;
        var externalKey = source.ExternalKey;
        var canSelect = hasBoundary &&
            hasDisplayBoundary &&
            isInsideAssignedTerritory &&
            string.Equals(status, "Available", StringComparison.OrdinalIgnoreCase);

        return new BoundaryRegionCandidateDto
        {
            CandidateId = source.Id.ToString(CultureInfo.InvariantCulture),
            BoundaryCandidateId = source.Id,
            ExternalKey = externalKey,
            BoundaryExternalKey = externalKey,
            AdmLevel = source.AdmLevel,
            Iso3 = source.CountryIso3,
            CountryIso3 = source.CountryIso3,
            ShapeGroup = source.ShapeGroup,
            ShapeId = source.ShapeId,
            ShapeName = source.ShapeName,
            ShapeType = source.ShapeType ?? source.AdmLevel,
            Name = source.ShapeName,
            DisplayName = source.ShapeName,
            Status = status,
            HasBoundary = hasBoundary,
            HasDisplayBoundary = hasDisplayBoundary,
            CanSelect = canSelect,
            ExistingRegionId = existingRegionId,
            IsInsideAssignedTerritory = isInsideAssignedTerritory,
            ValidationMessage = ResolveCandidateValidationMessage(
                hasBoundary,
                hasDisplayBoundary,
                isInsideAssignedTerritory),
            Viewport = BuildRegionMapViewport(displayGeometry),
            GeometryGeoJson = ToRegionGeoJson(displayGeometry)
        };
    }

    private static string? ResolveCandidateValidationMessage(
        bool hasBoundary,
        bool hasDisplayBoundary,
        bool isInsideAssignedTerritory)
    {
        if (!hasBoundary)
            return "Boundary candidate geometry is unavailable.";

        if (!hasDisplayBoundary)
            return "Boundary candidate display geometry is unavailable.";

        return isInsideAssignedTerritory
            ? null
            : "Boundary candidate is outside the assigned country/territory.";
    }

    private static bool CandidateIntersectsAssignedTerritory(Geometry territoryBoundary, Geometry? candidateGeometry)
    {
        if (candidateGeometry is null)
            return false;

        return territoryBoundary.Intersects(candidateGeometry);
    }

    public async Task<BoundaryCandidateDiagnosticsDto> GetRegionCandidateDiagnosticsAsync(
        string countryIso3,
        string admLevel,
        CancellationToken cancellationToken = default)
    {
        var normalizedLevel = NormalizeRegionAdmLevel(admLevel);
        var normalizedIso3 = CountryRegistry.NormalizeIso3ForBoundaryImport(countryIso3)
            ?? throw new InvalidOperationException("ISO3 country code is required.");
        var aliases = CountryRegistry.BoundaryImportIsoAliases(normalizedIso3);

        var territory = await _db.Territories
            .AsNoTracking()
            .Where(item =>
                item.TerritoryKind == TerritoryKinds.Country &&
                ((item.CountryIso3 != null && aliases.Contains(item.CountryIso3)) ||
                 (item.CountryCode != null && aliases.Contains(item.CountryCode)) ||
                 (item.BoundaryShapeGroup != null && aliases.Contains(item.BoundaryShapeGroup))))
            .OrderBy(item => item.Name)
            .ThenBy(item => item.Id)
            .Select(item => new BoundaryDbRow
            {
                TerritoryId = item.Id,
                Name = item.Name,
                CountryCode = item.CountryCode,
                CountryIso3 = item.CountryIso3,
                BoundaryShapeGroup = item.BoundaryShapeGroup,
                BoundaryGeometry = EF.Property<Geometry?>(item, BoundaryGeometryPropertyName),
                BoundaryDisplayGeometry = EF.Property<Geometry?>(item, BoundaryDisplayGeometryPropertyName)
            })
            .FirstOrDefaultAsync(cancellationToken);

        var territoryBoundary = ToValidPolygonalGeometry(territory?.BoundaryGeometry);
        var rows = await _db.AdministrativeBoundaryCandidates
            .AsNoTracking()
            .Where(candidate =>
                candidate.Source == GeoBoundariesSource &&
                candidate.AdmLevel == normalizedLevel &&
                candidate.CountryIso3 == normalizedIso3)
            .Select(candidate => new BoundaryRegionCandidateDbRow
            {
                Id = candidate.Id,
                Source = candidate.Source,
                ReleaseType = candidate.ReleaseType,
                AdmLevel = candidate.AdmLevel,
                ShapeGroup = candidate.ShapeGroup,
                ShapeId = candidate.ShapeId,
                ShapeName = candidate.ShapeName,
                ShapeType = candidate.ShapeType,
                ExternalKey = candidate.ExternalKey,
                CountryIso3 = candidate.CountryIso3,
                CountryTerritoryId = candidate.CountryTerritoryId,
                BoundaryGeometry = EF.Property<Geometry?>(candidate, BoundaryGeometryPropertyName),
                BoundaryDisplayGeometry = EF.Property<Geometry?>(candidate, BoundaryDisplayGeometryPropertyName)
            })
            .ToListAsync(cancellationToken);

        var candidateIds = rows.Select(row => row.Id).ToHashSet();
        var externalKeys = rows
            .Select(row => row.ExternalKey)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var shapeKeys = rows
            .Where(row => !string.IsNullOrWhiteSpace(row.ShapeGroup) && !string.IsNullOrWhiteSpace(row.ShapeId))
            .Select(row => BuildShapeCandidateKey(row.ShapeGroup, row.ShapeId, row.AdmLevel))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var existingRows = territory is null
            ? new List<BoundaryExistingRegionRow>()
            : await _db.Regions
                .AsNoTracking()
                .Where(region => region.TerritoryId == territory.TerritoryId)
                .Select(region => new BoundaryExistingRegionRow
                {
                    Id = region.Id,
                    BoundaryCandidateId = region.BoundaryCandidateId,
                    BoundaryExternalKey = region.BoundaryExternalKey,
                    BoundaryShapeGroup = region.BoundaryShapeGroup,
                    BoundaryShapeId = region.BoundaryShapeId,
                    BoundaryAdmLevel = region.BoundaryAdmLevel
                })
                .ToListAsync(cancellationToken);

        var passingIntersects = territoryBoundary is null
            ? 0
            : rows.Count(row => CandidateIntersectsAssignedTerritory(
                territoryBoundary,
                ToValidPolygonalGeometry(row.BoundaryGeometry)));
        var passingStrictCovers = territoryBoundary is null
            ? 0
            : rows.Count(row =>
            {
                var geometry = ToValidPolygonalGeometry(row.BoundaryGeometry);
                return geometry is not null && territoryBoundary.Covers(geometry);
            });

        return new BoundaryCandidateDiagnosticsDto
        {
            AdmLevel = normalizedLevel,
            CountryIso3 = normalizedIso3,
            TerritoryId = territory?.TerritoryId,
            TerritoryName = territory?.Name,
            TotalCandidatesForIso3 = rows.Count,
            CandidatesWithBoundaryGeometry = rows.Count(row => ToValidPolygonalGeometry(row.BoundaryGeometry) is not null),
            CandidatesWithDisplayGeometry = rows.Count(row => ToValidPolygonalGeometry(row.BoundaryDisplayGeometry) is not null),
            CandidatesPassingStrictCovers = passingStrictCovers,
            CandidatesPassingIntersects = passingIntersects,
            CandidatesFilteredOutBySpatialCheck = territoryBoundary is null
                ? rows.Count
                : rows.Count - passingIntersects,
            CandidatesReturned = rows.Count,
            ExistingRegionMatches = existingRows.Count(row =>
                (row.BoundaryCandidateId.HasValue && candidateIds.Contains(row.BoundaryCandidateId.Value)) ||
                (!string.IsNullOrWhiteSpace(row.BoundaryExternalKey) &&
                 externalKeys.Contains(row.BoundaryExternalKey)) ||
                (!string.IsNullOrWhiteSpace(row.BoundaryShapeGroup) &&
                 !string.IsNullOrWhiteSpace(row.BoundaryShapeId) &&
                 !string.IsNullOrWhiteSpace(row.BoundaryAdmLevel) &&
                 shapeKeys.Contains(BuildShapeCandidateKey(
                     row.BoundaryShapeGroup,
                     row.BoundaryShapeId,
                     row.BoundaryAdmLevel))))
        };
    }

    public async Task<BoundaryReconciliationReportDto> GetAdm0ReconciliationAsync(
        CancellationToken cancellationToken = default)
    {
        var path = ResolveBoundaryPath("ADM0");
        var sourceReference = BuildSourceReference(path);
        var report = new BoundaryReconciliationReportDto
        {
            Source = GeoBoundariesSource,
            AdmLevel = "ADM0",
            SourcePath = sourceReference,
            GeneratedAt = DateTime.UtcNow
        };

        var sourceFeatures = new List<BoundaryFeature>();
        await ScanFeatureElementsAsync(
            path,
            (featureElement, _, token) =>
            {
                token.ThrowIfCancellationRequested();
                var metadata = TryReadBoundaryFeatureMetadata(featureElement);
                try
                {
                    sourceFeatures.Add(ParseBoundaryFeature(featureElement, "ADM0"));
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    report.SourceFeaturesMissingInDb.Add(new BoundarySourceFeatureReconciliationDto
                    {
                        Kind = IsDisputedShapeType(metadata)
                            ? TerritoryKinds.DisputedTerritory
                            : TerritoryKinds.Country,
                        Iso3 = metadata.Iso3,
                        ShapeGroup = metadata.ShapeGroup,
                        Name = metadata.Name ?? metadata.ShapeGroup ?? "Unknown source feature",
                        ShapeId = metadata.ShapeId,
                        ShapeType = metadata.RawShapeType,
                        Status = "InvalidSourceFeature",
                        Message = ex.Message
                    });
                }

                return ValueTask.FromResult(true);
            },
            cancellationToken);

        report.SourceCountryFeatureCount = sourceFeatures.Count(feature => !IsDisputedFeature(feature));
        report.SourceDisputedFeatureCount = sourceFeatures.Count(IsDisputedFeature);

        var dbRows = await _db.Territories
            .AsNoTracking()
            .Select(territory => new BoundaryDbRow
            {
                TerritoryId = territory.Id,
                Name = territory.Name,
                TerritoryKind = territory.TerritoryKind,
                CountryCode = territory.CountryCode,
                CountryIso3 = territory.CountryIso3,
                BoundaryShapeType = territory.BoundaryShapeType,
                BoundaryShapeGroup = territory.BoundaryShapeGroup,
                BoundaryShapeId = territory.BoundaryShapeId,
                BoundaryExternalKey = territory.BoundaryExternalKey,
                BoundaryAdmLevel = territory.BoundaryAdmLevel,
                BoundaryGeometry = EF.Property<Geometry?>(territory, BoundaryGeometryPropertyName),
                BoundaryDisplayGeometry = EF.Property<Geometry?>(territory, BoundaryDisplayGeometryPropertyName)
            })
            .ToListAsync(cancellationToken);

        var dbByName = dbRows
            .GroupBy(row => row.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        var countryRowsByIso3 = dbRows
            .Where(row => IsCountryKind(row.TerritoryKind) && !string.IsNullOrWhiteSpace(row.CountryIso3))
            .GroupBy(row => row.CountryIso3!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        var countryRowsByIso2 = dbRows
            .Where(row => IsCountryKind(row.TerritoryKind) && !string.IsNullOrWhiteSpace(row.CountryCode))
            .GroupBy(row => row.CountryCode!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        var sourceCountryByIso3 = sourceFeatures
            .Where(feature => !IsDisputedFeature(feature) && !string.IsNullOrWhiteSpace(feature.Iso3))
            .GroupBy(feature => feature.Iso3!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        var sourceDisputed = sourceFeatures
            .Where(IsDisputedFeature)
            .ToList();

        foreach (var country in CountryRegistry.Countries)
        {
            countryRowsByIso3.TryGetValue(country.Iso3, out var row);
            row ??= countryRowsByIso2.GetValueOrDefault(country.Iso2);

            var hasBoundary = IsValidBoundary(row?.BoundaryGeometry);
            var hasDisplayBoundary = TerritoryBoundaryDisplayGeometry.IsValid(
                row?.BoundaryGeometry,
                row?.BoundaryDisplayGeometry);
            if (hasBoundary)
            {
                report.CountriesWithFullBoundary++;
                if (!hasDisplayBoundary)
                {
                    report.CountriesWithFullBoundaryMissingDisplayGeometry++;
                    report.MissingDisplayGeometry.Add(MapReconciliationItem(
                        row,
                        country.Name,
                        TerritoryKinds.Country,
                        country.Iso2,
                        country.Iso3,
                        "MissingDisplayGeometry",
                        "Boundary is available for validation, but display geometry must be rebuilt."));
                }
            }
            else
            {
                report.CountriesMissingFullBoundary++;
                var reason = sourceCountryByIso3.ContainsKey(country.Iso3)
                    ? "ADM0 country feature exists in local source, but no valid full boundary is stored in the database."
                    : "No ADM0 country feature found in local source. Related disputed features may exist separately.";
                report.MissingFullBoundary.Add(MapReconciliationItem(
                    row,
                    country.Name,
                    TerritoryKinds.Country,
                    country.Iso2,
                    country.Iso3,
                    "MissingBoundary",
                    reason));
            }
        }

        foreach (var row in dbRows.Where(row => IsDisputedKind(row.TerritoryKind)))
        {
            var hasBoundary = IsValidBoundary(row.BoundaryGeometry);
            var hasDisplayBoundary = TerritoryBoundaryDisplayGeometry.IsValid(
                row.BoundaryGeometry,
                row.BoundaryDisplayGeometry);
            report.DisputedTerritoriesImported++;
            report.DisputedTerritories.Add(MapReconciliationItem(
                row,
                row.Name,
                TerritoryKinds.DisputedTerritory,
                null,
                null,
                hasBoundary
                    ? hasDisplayBoundary ? "DisputedViewOnly" : "MissingDisplayGeometry"
                    : "MissingBoundary",
                hasBoundary
                    ? hasDisplayBoundary ? DisputedViewOnlyMessage() : "Boundary is available for validation, but display geometry must be rebuilt."
                    : "Disputed boundary geometry is missing."));
        }

        foreach (var feature in sourceFeatures)
        {
            var matchedRow = FindMatchingSourceFeatureRow(feature, dbRows);
            if (matchedRow is null)
            {
                report.SourceFeaturesMissingInDb.Add(new BoundarySourceFeatureReconciliationDto
                {
                    Kind = feature.Kind,
                    Iso3 = feature.Iso3,
                    ShapeGroup = feature.ShapeGroup,
                    Name = feature.Name,
                    ShapeId = feature.ShapeId,
                    ShapeType = feature.ShapeType,
                    Status = "MissingInDb",
                    Message = IsDisputedFeature(feature)
                        ? "Source disputed feature is not represented by a disputed territory row."
                        : "Source ADM0 country feature is not represented by a country territory row."
                });
            }

            if (dbByName.TryGetValue(feature.Name, out var sameNameRow) &&
                !FeatureMatchesRow(feature, sameNameRow))
            {
                report.DuplicateNameConflicts.Add(new BoundaryReconciliationConflictDto
                {
                    Name = feature.Name,
                    SourceKind = feature.Kind,
                    SourceIso3 = feature.Iso3,
                    SourceShapeGroup = feature.ShapeGroup,
                    SourceShapeType = feature.ShapeType,
                    TerritoryId = sameNameRow.TerritoryId,
                    TerritoryKind = sameNameRow.TerritoryKind,
                    TerritoryIso3 = sameNameRow.CountryIso3,
                    TerritoryShapeGroup = sameNameRow.BoundaryShapeGroup,
                    Message = "A source feature name matches an existing territory row with different source identity or territory kind."
                });
            }
        }

        var sourceCountryIso3 = sourceCountryByIso3.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var sourceDisputedKeys = sourceDisputed
            .Select(BuildBoundaryExternalKey)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var row in dbRows)
        {
            if (IsCountryKind(row.TerritoryKind))
            {
                var normalizedIso3 = CountryRegistry.NormalizeIso3ForBoundaryImport(row.CountryIso3)
                    ?? CountryRegistry.NormalizeIso3ForBoundaryImport(row.CountryCode)
                    ?? CountryRegistry.ToIso3(row.CountryCode);
                if (string.IsNullOrWhiteSpace(normalizedIso3) ||
                    !sourceCountryIso3.Any(sourceIso3 => BoundaryImportIsoCodesMatch(sourceIso3, normalizedIso3)))
                {
                    report.DbTerritoriesNotFoundInSource.Add(MapReconciliationItem(
                        row,
                        row.Name,
                        TerritoryKinds.Country,
                        row.CountryCode,
                        normalizedIso3,
                        "SourceMissing",
                        "No ADM0 country feature found in local source. Related disputed features may exist separately."));
                }

                continue;
            }

            if (IsDisputedKind(row.TerritoryKind) &&
                (string.IsNullOrWhiteSpace(row.BoundaryExternalKey) ||
                 !sourceDisputedKeys.Contains(row.BoundaryExternalKey)))
            {
                report.DbTerritoriesNotFoundInSource.Add(MapReconciliationItem(
                    row,
                    row.Name,
                    TerritoryKinds.DisputedTerritory,
                    null,
                    null,
                    "SourceMissing",
                    "No matching DISP feature found in local ADM0 source."));
            }
        }

        return report;
    }

    private async Task<BoundaryImportSummaryDto> ImportAdm0Async(
        bool forceRefresh,
        CancellationToken cancellationToken)
    {
        var path = ResolveBoundaryPath("ADM0");
        var sourceReference = BuildSourceReference(path);
        var summary = CreateSummary("ADM0", sourceReference);
        var maxSummaryItems = GetMaxSummaryItems();
        var saveBatchSize = GetSaveBatchSize();
        var progressLogInterval = GetProgressLogInterval();
        var batch = new BoundaryImportBatchState();

        _db.ChangeTracker.Clear();
        LogImportStarted(
            "ADM0",
            path,
            forceRefresh,
            saveBatchSize,
            maxSummaryItems,
            progressLogInterval,
            countryIso3: null,
            takeLimit: null);

        await ScanFeatureElementsAsync(
            path,
            async (featureElement, _, token) =>
            {
                token.ThrowIfCancellationRequested();
                summary.TotalFeatures++;

                var stopwatch = Stopwatch.StartNew();
                var metadata = TryReadBoundaryFeatureMetadata(featureElement);
                var trackedBeforeFeature = CaptureTrackedEntities();

                try
                {
                    var result = await TryCreateAdm0SkipResultAsync(
                        metadata,
                        forceRefresh,
                        stopwatch,
                        token);

                    if (result is null)
                    {
                        var feature = ParseBoundaryFeature(featureElement, "ADM0");
                        result = await ImportAdm0FeatureAsync(
                            feature,
                            sourceReference,
                            stopwatch,
                            token);
                    }

                    RecordSummaryItem(summary, result.Item, maxSummaryItems);
                    TrackPendingChange(batch, result);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    DetachTrackedEntriesAddedAfter(trackedBeforeFeature);
                    var item = CreateFailedImportItem(metadata, ex.Message, stopwatch.ElapsedMilliseconds);
                    RecordSummaryItem(summary, item, maxSummaryItems);
                    LogBoundaryFeatureFailure(
                        LogLevel.Warning,
                        ex,
                        "Local ADM0 boundary import failed",
                        "ADM0",
                        path,
                        metadata,
                        featureElement);
                }

                await SaveImportBatchIfNeededAsync(batch, summary, saveBatchSize, token);
                LogImportProgressIfNeeded(summary, progressLogInterval);
                return true;
            },
            cancellationToken);

        await SaveImportBatchAsync(batch, summary, cancellationToken);
        CompleteSummary(summary);
        LogImportCompleted(summary);
        return summary;
    }

    private async Task<BoundaryImportFeatureResult?> TryCreateAdm0SkipResultAsync(
        BoundaryFeatureMetadata metadata,
        bool forceRefresh,
        Stopwatch stopwatch,
        CancellationToken cancellationToken)
    {
        ValidateBoundaryAdmLevel(metadata, "ADM0");
        if (forceRefresh)
            return null;

        if (IsDisputedShapeType(metadata))
        {
            var externalKey = BuildBoundaryExternalKey(metadata);
            var disputedResolution = await ResolveDisputedTerritoryAsync(metadata, externalKey, cancellationToken);
            if (disputedResolution.ConflictMessage is not null)
            {
                return new BoundaryImportFeatureResult(
                    CreateSkippedImportItem(
                        metadata,
                        TerritoryKinds.DisputedTerritory,
                        disputedResolution.ConflictMessage,
                        stopwatch.ElapsedMilliseconds),
                    Changed: false,
                    InvalidatesMapCache: false);
            }

            if (disputedResolution.Territory?.HasValidBoundary == true &&
                disputedResolution.Territory.HasValidDisplayBoundary)
            {
                return new BoundaryImportFeatureResult(
                    CreateSkippedImportItem(
                        metadata,
                        TerritoryKinds.DisputedTerritory,
                        "Existing disputed boundary geometry is already valid.",
                        stopwatch.ElapsedMilliseconds),
                    Changed: false,
                    InvalidatesMapCache: false);
            }

            return null;
        }

        var countryIso3 = metadata.Iso3;
        if (string.IsNullOrWhiteSpace(countryIso3))
            return null;

        var resolution = await ResolveTerritoryAsync(metadata, cancellationToken);
        if (resolution.ConflictMessage is not null)
        {
            return new BoundaryImportFeatureResult(
                CreateSkippedImportItem(
                    metadata,
                    TerritoryKinds.Country,
                    resolution.ConflictMessage,
                    stopwatch.ElapsedMilliseconds),
                Changed: false,
                InvalidatesMapCache: false);
        }

        if (resolution.Territory is null ||
            !TerritoryMetadataMatchesFeature(resolution.Territory, metadata))
        {
            return null;
        }

        return resolution.Territory.HasValidBoundary &&
            resolution.Territory.HasValidDisplayBoundary
            ? new BoundaryImportFeatureResult(
                CreateSkippedImportItem(
                    metadata,
                    TerritoryKinds.Country,
                    "Existing ADM0 boundary geometry is already valid.",
                    stopwatch.ElapsedMilliseconds),
                Changed: false,
                InvalidatesMapCache: false)
            : null;
    }

    private async Task<BoundaryImportFeatureResult?> TryCreateBoundaryCandidateSkipResultAsync(
        BoundaryFeatureMetadata metadata,
        string admLevel,
        bool forceRefresh,
        Stopwatch stopwatch,
        CancellationToken cancellationToken)
    {
        var normalizedLevel = NormalizeRegionAdmLevel(admLevel);
        ValidateBoundaryAdmLevel(metadata, normalizedLevel);
        if (forceRefresh)
            return null;

        var countryIso3 = CountryRegistry.NormalizeIso3ForBoundaryImport(metadata.Iso3);
        if (string.IsNullOrWhiteSpace(countryIso3))
            return null;

        var shapeGroup = CountryRegistry.NormalizeIso3ForBoundaryImport(metadata.ShapeGroup)
            ?? countryIso3;
        if (string.IsNullOrWhiteSpace(metadata.ShapeId) ||
            string.IsNullOrWhiteSpace(metadata.Name))
        {
            return null;
        }

        var shapeId = NormalizeRequiredBoundaryCandidateShapeId(metadata.ShapeId);
        var shapeName = NormalizeRequiredBoundaryCandidateShapeName(metadata.Name);
        var externalKey = BuildRegionCandidateExternalKey(normalizedLevel, shapeGroup, shapeId, shapeName);
        var countryTerritoryId = await ResolveCandidateCountryTerritoryIdAsync(countryIso3, cancellationToken);

        var existing = await _db.AdministrativeBoundaryCandidates
            .AsNoTracking()
            .Where(candidate =>
                candidate.ExternalKey == externalKey ||
                (candidate.Source == GeoBoundariesSource &&
                 candidate.AdmLevel == normalizedLevel &&
                 candidate.ShapeGroup == shapeGroup &&
                 candidate.ShapeId == shapeId))
            .OrderBy(candidate => candidate.Id)
            .Select(candidate => new BoundaryCandidateImportRow
            {
                Id = candidate.Id,
                Source = candidate.Source,
                ReleaseType = candidate.ReleaseType,
                AdmLevel = candidate.AdmLevel,
                ShapeGroup = candidate.ShapeGroup,
                ShapeId = candidate.ShapeId,
                ShapeName = candidate.ShapeName,
                ShapeType = candidate.ShapeType,
                ExternalKey = candidate.ExternalKey,
                CountryIso3 = candidate.CountryIso3,
                CountryTerritoryId = candidate.CountryTerritoryId,
                CreatedAt = candidate.CreatedAt,
                HasValidBoundary = EF.Property<Geometry?>(candidate, BoundaryGeometryPropertyName) != null &&
                    !EF.Property<Geometry?>(candidate, BoundaryGeometryPropertyName)!.IsEmpty &&
                    EF.Property<Geometry?>(candidate, BoundaryGeometryPropertyName)!.IsValid,
                HasValidDisplayBoundary = EF.Property<Geometry?>(candidate, BoundaryGeometryPropertyName) != null &&
                    !EF.Property<Geometry?>(candidate, BoundaryGeometryPropertyName)!.IsEmpty &&
                    EF.Property<Geometry?>(candidate, BoundaryGeometryPropertyName)!.IsValid &&
                    EF.Property<Geometry?>(candidate, BoundaryDisplayGeometryPropertyName) != null &&
                    !EF.Property<Geometry?>(candidate, BoundaryDisplayGeometryPropertyName)!.IsEmpty &&
                    EF.Property<Geometry?>(candidate, BoundaryDisplayGeometryPropertyName)!.IsValid &&
                    (EF.Property<Geometry?>(candidate, BoundaryGeometryPropertyName)!.Area <= 0d ||
                     EF.Property<Geometry?>(candidate, BoundaryDisplayGeometryPropertyName)!.Area >=
                     EF.Property<Geometry?>(candidate, BoundaryGeometryPropertyName)!.Area * 0.85d)
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (existing is null ||
            !BoundaryCandidateMetadataMatches(
                existing,
                normalizedLevel,
                shapeGroup,
                shapeId,
                shapeName,
                metadata.RawShapeType,
                externalKey,
                countryIso3,
                countryTerritoryId) ||
            !existing.HasValidBoundary ||
            !existing.HasValidDisplayBoundary)
        {
            return null;
        }

        return new BoundaryImportFeatureResult(
            new BoundaryImportItemDto
            {
                Kind = TerritoryKinds.Country,
                Iso3 = countryIso3,
                ShapeGroup = shapeGroup,
                Name = shapeName,
                ShapeId = shapeId,
                ShapeType = metadata.RawShapeType,
                Status = "Skipped",
                Message = $"Existing {normalizedLevel} boundary candidate is already valid.",
                DurationMs = stopwatch.ElapsedMilliseconds
            },
            Changed: false,
            InvalidatesMapCache: false);
    }

    private async Task<BoundaryImportSummaryDto> ImportBoundaryCandidatesAsync(
        string admLevel,
        bool forceRefresh,
        string? countryIso3,
        int? takeLimit,
        CancellationToken cancellationToken)
    {
        var path = ResolveBoundaryPath(admLevel);
        var summary = CreateSummary(admLevel, BuildSourceReference(path), countryIso3, takeLimit);
        var maxSummaryItems = GetMaxSummaryItems();
        var saveBatchSize = GetSaveBatchSize();
        var progressLogInterval = GetProgressLogInterval();
        var batch = new BoundaryImportBatchState();

        _db.ChangeTracker.Clear();
        LogImportStarted(
            admLevel,
            path,
            forceRefresh,
            saveBatchSize,
            maxSummaryItems,
            progressLogInterval,
            countryIso3,
            takeLimit);

        await ScanFeatureElementsAsync(
            path,
            async (featureElement, _, token) =>
            {
                token.ThrowIfCancellationRequested();
                summary.TotalFeatures++;

                var stopwatch = Stopwatch.StartNew();
                var metadata = TryReadBoundaryFeatureMetadata(featureElement);
                var trackedBeforeFeature = CaptureTrackedEntities();

                if (!BoundaryFeatureMatchesCountryFilter(metadata, countryIso3))
                {
                    summary.FilteredOutCount++;
                    return true;
                }

                if (TryCreateIgnoredLevelMismatchImportItem(
                    metadata,
                    admLevel,
                    stopwatch.ElapsedMilliseconds,
                    out var ignoredLevelMismatch))
                {
                    RecordFilteredSourceItem(summary, ignoredLevelMismatch, maxSummaryItems);
                    LogBoundaryFeatureLevelMismatchFiltered(admLevel, path, metadata, featureElement);
                    return true;
                }

                summary.MatchedFilterCount++;
                if (takeLimit.HasValue && ReachedTakeLimit(summary, takeLimit.Value))
                {
                    summary.HasMore = true;
                    summary.CompletedSourceScan = false;
                    return false;
                }

                try
                {
                    var result = await TryCreateBoundaryCandidateSkipResultAsync(
                        metadata,
                        admLevel,
                        forceRefresh,
                        stopwatch,
                        token);

                    if (result is null)
                    {
                        var feature = ParseBoundaryFeature(featureElement, admLevel);
                        result = await ImportBoundaryCandidateFeatureAsync(
                            feature,
                            admLevel,
                            stopwatch,
                            token);
                    }

                    RecordSummaryItem(summary, result.Item, maxSummaryItems);
                    TrackPendingChange(batch, result);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    DetachTrackedEntriesAddedAfter(trackedBeforeFeature);
                    RecordSummaryItem(
                        summary,
                        CreateFailedImportItem(metadata, ex.Message, stopwatch.ElapsedMilliseconds),
                        maxSummaryItems);
                    LogBoundaryFeatureFailure(
                        LogLevel.Warning,
                        ex,
                        $"Local {admLevel} candidate source item failed validation",
                        admLevel,
                        path,
                        metadata,
                        featureElement);
                }

                await SaveImportBatchIfNeededAsync(batch, summary, saveBatchSize, token);
                LogImportProgressIfNeeded(summary, progressLogInterval);
                return true;
            },
            cancellationToken);

        await SaveImportBatchAsync(batch, summary, cancellationToken);
        CompleteSummary(summary);
        LogImportCompleted(summary);
        return summary;
    }

    private async Task<BoundaryImportFeatureResult> ImportBoundaryCandidateFeatureAsync(
        BoundaryFeature feature,
        string admLevel,
        Stopwatch stopwatch,
        CancellationToken cancellationToken)
    {
        var normalizedLevel = NormalizeRegionAdmLevel(admLevel);
        var countryIso3 = CountryRegistry.NormalizeIso3ForBoundaryImport(feature.Iso3)
            ?? throw new InvalidOperationException("Boundary candidate ISO3 country code is missing or invalid.");
        var shapeGroup = CountryRegistry.NormalizeIso3ForBoundaryImport(feature.ShapeGroup)
            ?? countryIso3;
        var shapeId = NormalizeRequiredBoundaryCandidateShapeId(feature.ShapeId);
        var shapeName = NormalizeRequiredBoundaryCandidateShapeName(feature.Name);
        var externalKey = BuildRegionCandidateExternalKey(normalizedLevel, shapeGroup, shapeId, shapeName);
        var countryTerritoryId = await ResolveCandidateCountryTerritoryIdAsync(countryIso3, cancellationToken);

        var existing = await _db.AdministrativeBoundaryCandidates
            .AsNoTracking()
            .Where(candidate =>
                candidate.ExternalKey == externalKey ||
                (candidate.Source == GeoBoundariesSource &&
                 candidate.AdmLevel == normalizedLevel &&
                 candidate.ShapeGroup == shapeGroup &&
                 candidate.ShapeId == shapeId))
            .OrderBy(candidate => candidate.Id)
            .Select(candidate => new BoundaryCandidateImportRow
            {
                Id = candidate.Id,
                Source = candidate.Source,
                ReleaseType = candidate.ReleaseType,
                AdmLevel = candidate.AdmLevel,
                ShapeGroup = candidate.ShapeGroup,
                ShapeId = candidate.ShapeId,
                ShapeName = candidate.ShapeName,
                ShapeType = candidate.ShapeType,
                ExternalKey = candidate.ExternalKey,
                CountryIso3 = candidate.CountryIso3,
                CountryTerritoryId = candidate.CountryTerritoryId,
                CreatedAt = candidate.CreatedAt
            })
            .FirstOrDefaultAsync(cancellationToken);

        var now = DateTime.UtcNow;
        var isNew = existing is null;
        var candidate = new AdministrativeBoundaryCandidate
        {
            Id = existing?.Id ?? 0,
            CreatedAt = existing?.CreatedAt ?? now
        };

        if (isNew)
        {
            _db.AdministrativeBoundaryCandidates.Add(candidate);
        }
        else
        {
            _db.AdministrativeBoundaryCandidates.Attach(candidate);
        }

        candidate.Source = GeoBoundariesSource;
        candidate.ReleaseType = ReleaseType;
        candidate.AdmLevel = normalizedLevel;
        candidate.ShapeGroup = shapeGroup;
        candidate.ShapeId = shapeId;
        candidate.ShapeName = shapeName;
        candidate.ShapeType = feature.ShapeType;
        candidate.ExternalKey = externalKey;
        candidate.CountryIso3 = countryIso3;
        candidate.CountryTerritoryId = countryTerritoryId;
        candidate.ImportedAt = now;
        candidate.UpdatedAt = isNew ? null : now;

        var entry = _db.Entry(candidate);
        if (!isNew)
        {
            MarkBoundaryCandidateImportPropertiesModified(entry);
        }

        var displayGeometry = TerritoryBoundaryDisplayGeometry.Create(feature.Geometry);
        entry.Property<Geometry?>(BoundaryGeometryPropertyName).CurrentValue = feature.Geometry;
        entry.Property<Geometry?>(BoundaryGeometryPropertyName).IsModified = !isNew;
        entry.Property<Geometry?>(BoundaryDisplayGeometryPropertyName).CurrentValue = displayGeometry;
        entry.Property<Geometry?>(BoundaryDisplayGeometryPropertyName).IsModified = !isNew;

        var status = ResolveImportStatus(isNew, displayGeometry is not null);
        return new BoundaryImportFeatureResult(
            new BoundaryImportItemDto
            {
                Kind = feature.Kind,
                Iso3 = countryIso3,
                ShapeGroup = shapeGroup,
                Name = shapeName,
                ShapeId = shapeId,
                ShapeType = feature.ShapeType,
                Status = status,
                Message = displayGeometry is null
                    ? $"Stored {normalizedLevel} boundary candidate for validation, but display geometry could not be generated and must be rebuilt."
                    : isNew
                        ? $"Stored {normalizedLevel} boundary candidate."
                        : $"Updated {normalizedLevel} boundary candidate.",
                DurationMs = stopwatch.ElapsedMilliseconds
            },
            Changed: true,
            InvalidatesMapCache: false);
    }

    private async Task<BoundaryImportFeatureResult> ImportAdm0FeatureAsync(
        BoundaryFeature feature,
        string sourceReference,
        Stopwatch stopwatch,
        CancellationToken cancellationToken)
    {
        if (IsDisputedFeature(feature))
        {
            return await ImportDisputedFeatureAsync(
                feature,
                sourceReference,
                stopwatch,
                cancellationToken);
        }

        var territoryResolution = await ResolveTerritoryAsync(feature, cancellationToken);
        if (territoryResolution.ConflictMessage is not null)
        {
            return new BoundaryImportFeatureResult(
                CreateSkippedImportItem(feature, territoryResolution.ConflictMessage, stopwatch.ElapsedMilliseconds),
                Changed: false,
                InvalidatesMapCache: false);
        }

        var existing = territoryResolution.Territory;
        if (existing is not null)
            EnsureTerritoryMatchesFeature(existing, feature);

        var now = DateTime.UtcNow;
        var isNewTerritory = existing is null;

        var territory = new Territory
        {
            Id = existing?.Id ?? 0,
            Name = existing?.Name ?? feature.Name,
            TerritoryKind = existing?.TerritoryKind ?? TerritoryKinds.Country,
            CountryCode = existing?.CountryCode,
            CountryIso3 = existing?.CountryIso3,
            CreatedAt = existing?.CreatedAt ?? now
        };

        if (isNewTerritory)
        {
            _db.Territories.Add(territory);
        }
        else
        {
            _db.Territories.Attach(territory);
        }

        territory.CountryCode = ResolveCountryIso2ForImport(territory, feature);
        territory.CountryIso3 = ResolveCountryIso3ForImport(territory, feature);
        territory.TerritoryKind = TerritoryKinds.Country;
        territory.BoundaryShapeType = feature.Geometry.GeometryType;
        territory.BoundaryShapeGroup = feature.ShapeGroup;
        territory.BoundaryShapeId = feature.ShapeId;
        territory.BoundaryExternalKey = BuildBoundaryExternalKey(feature);
        territory.BoundarySource = GeoBoundariesSource;
        territory.BoundarySourceUrl = sourceReference;
        territory.BoundaryAdmLevel = "ADM0";
        territory.BoundaryReleaseType = ReleaseType;
        territory.BoundaryLicense = DefaultLicense;
        territory.BoundaryImportedAt = now;

        if (territory.Id != 0)
        {
            territory.UpdatedAt = now;
        }

        if (string.IsNullOrWhiteSpace(territory.Name))
        {
            territory.Name = feature.Name;
        }

        var entry = _db.Entry(territory);
        if (!isNewTerritory)
        {
            MarkTerritoryImportPropertiesModified(entry);
        }

        var displayGeometry = TerritoryBoundaryDisplayGeometry.Create(feature.Geometry);

        entry.Property<Geometry?>(BoundaryGeometryPropertyName).CurrentValue = feature.Geometry;
        entry.Property<Geometry?>(BoundaryGeometryPropertyName).IsModified = !isNewTerritory;
        entry.Property<Geometry?>(BoundaryDisplayGeometryPropertyName).CurrentValue = displayGeometry;
        entry.Property<Geometry?>(BoundaryDisplayGeometryPropertyName).IsModified = !isNewTerritory;

        var status = ResolveImportStatus(isNewTerritory, displayGeometry is not null);
        return new BoundaryImportFeatureResult(
            new BoundaryImportItemDto
            {
                Kind = TerritoryKinds.Country,
                Iso3 = feature.Iso3,
                ShapeGroup = feature.ShapeGroup,
                Name = feature.Name,
                ShapeId = feature.ShapeId,
                ShapeType = feature.ShapeType,
                Status = status,
                Message = displayGeometry is null
                    ? "Stored ADM0 boundary geometry for validation, but display geometry could not be generated and must be rebuilt."
                    : isNewTerritory
                        ? "Created territory and stored ADM0 boundary geometry."
                        : "Updated territory ADM0 boundary geometry.",
                DurationMs = stopwatch.ElapsedMilliseconds
            },
            Changed: true,
            InvalidatesMapCache: true);
    }

    private async Task<BoundaryImportFeatureResult> ImportDisputedFeatureAsync(
        BoundaryFeature feature,
        string sourceReference,
        Stopwatch stopwatch,
        CancellationToken cancellationToken)
    {
        var externalKey = BuildBoundaryExternalKey(feature);
        var territoryResolution = await ResolveDisputedTerritoryAsync(feature, externalKey, cancellationToken);
        if (territoryResolution.ConflictMessage is not null)
        {
            return new BoundaryImportFeatureResult(
                CreateSkippedImportItem(feature, territoryResolution.ConflictMessage, stopwatch.ElapsedMilliseconds),
                Changed: false,
                InvalidatesMapCache: false);
        }

        var existing = territoryResolution.Territory;

        var now = DateTime.UtcNow;
        var isNewTerritory = existing is null;

        var territory = new Territory
        {
            Id = existing?.Id ?? 0,
            Name = existing?.Name ?? feature.Name,
            TerritoryKind = existing?.TerritoryKind ?? TerritoryKinds.DisputedTerritory,
            CountryCode = existing?.CountryCode,
            CountryIso3 = existing?.CountryIso3,
            CreatedAt = existing?.CreatedAt ?? now
        };

        if (isNewTerritory)
        {
            _db.Territories.Add(territory);
        }
        else
        {
            _db.Territories.Attach(territory);
        }

        territory.TerritoryKind = TerritoryKinds.DisputedTerritory;
        territory.CountryCode = null;
        territory.CountryIso3 = null;
        territory.BoundaryShapeType = NormalizeDisputedShapeType(feature.ShapeType);
        territory.BoundaryShapeGroup = feature.ShapeGroup;
        territory.BoundaryShapeId = feature.ShapeId;
        territory.BoundaryExternalKey = externalKey;
        territory.BoundarySource = GeoBoundariesSource;
        territory.BoundarySourceUrl = sourceReference;
        territory.BoundaryAdmLevel = DisputedAdmLevel;
        territory.BoundaryReleaseType = ReleaseType;
        territory.BoundaryLicense = DefaultLicense;
        territory.BoundaryImportedAt = now;

        if (territory.Id != 0)
        {
            territory.UpdatedAt = now;
        }

        if (string.IsNullOrWhiteSpace(territory.Name))
        {
            territory.Name = feature.Name;
        }

        var entry = _db.Entry(territory);
        if (!isNewTerritory)
        {
            MarkTerritoryImportPropertiesModified(entry);
        }

        var displayGeometry = TerritoryBoundaryDisplayGeometry.Create(feature.Geometry);

        entry.Property<Geometry?>(BoundaryGeometryPropertyName).CurrentValue = feature.Geometry;
        entry.Property<Geometry?>(BoundaryGeometryPropertyName).IsModified = !isNewTerritory;
        entry.Property<Geometry?>(BoundaryDisplayGeometryPropertyName).CurrentValue = displayGeometry;
        entry.Property<Geometry?>(BoundaryDisplayGeometryPropertyName).IsModified = !isNewTerritory;

        var status = ResolveImportStatus(isNewTerritory, displayGeometry is not null);
        return new BoundaryImportFeatureResult(
            new BoundaryImportItemDto
            {
                Kind = TerritoryKinds.DisputedTerritory,
                Iso3 = null,
                ShapeGroup = feature.ShapeGroup,
                Name = feature.Name,
                ShapeId = feature.ShapeId,
                ShapeType = feature.ShapeType,
                Status = status,
                Message = displayGeometry is null
                    ? "Stored disputed boundary geometry for validation, but display geometry could not be generated and must be rebuilt."
                    : isNewTerritory
                        ? "Created disputed territory and stored DISP boundary geometry."
                        : "Updated disputed territory DISP boundary geometry.",
                DurationMs = stopwatch.ElapsedMilliseconds
            },
            Changed: true,
            InvalidatesMapCache: true);
    }

    private async Task<TerritoryResolution> ResolveTerritoryAsync(
        BoundaryFeatureMetadata metadata,
        CancellationToken cancellationToken)
    {
        var iso3 = metadata.Iso3;
        var iso2 = CountryRegistry.ToIso2ForBoundaryImport(iso3);
        var name = ResolveCountryNameForMetadata(metadata);
        var normalizedName = name.ToLowerInvariant();

        var territory = !string.IsNullOrWhiteSpace(iso3)
            ? await SelectTerritoryImportRows(_db.Territories.AsNoTracking()
                    .Where(item =>
                        item.TerritoryKind == TerritoryKinds.Country &&
                        item.CountryIso3 == iso3))
                .OrderBy(item => item.Id)
                .FirstOrDefaultAsync(cancellationToken)
            : null;

        if (territory is not null)
            return new TerritoryResolution(territory, null);

        var aliasCandidates = CountryRegistry.BoundaryImportIsoAliases(iso3);
        if (aliasCandidates.Count > 1)
        {
            var candidates = await SelectTerritoryImportRows(_db.Territories.AsNoTracking()
                    .Where(item =>
                        item.TerritoryKind == TerritoryKinds.Country &&
                        ((item.CountryIso3 != null && aliasCandidates.Contains(item.CountryIso3)) ||
                         item.CountryCode == "XK" ||
                         item.Name.ToLower().Contains("kosov"))))
                .OrderBy(item => item.Id)
                .ToListAsync(cancellationToken);

            territory = candidates.FirstOrDefault(item => IsKnownKosovoTerritoryMetadataMatch(item, metadata));
            if (territory is not null)
                return new TerritoryResolution(territory, null);
        }

        if (!string.IsNullOrWhiteSpace(iso2))
        {
            territory = await SelectTerritoryImportRows(_db.Territories.AsNoTracking()
                    .Where(item =>
                        item.TerritoryKind == TerritoryKinds.Country &&
                        item.CountryCode == iso2))
                .OrderBy(item => item.Id)
                .FirstOrDefaultAsync(cancellationToken);

            if (territory is not null)
                return new TerritoryResolution(territory, null);
        }

        territory = await SelectTerritoryImportRows(_db.Territories.AsNoTracking()
                .Where(item =>
                    item.TerritoryKind == TerritoryKinds.Country &&
                    item.Name.ToLower() == normalizedName))
            .OrderBy(item => item.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (territory is not null)
            return new TerritoryResolution(territory, null);

        var sameName = await SelectTerritoryImportRows(_db.Territories.AsNoTracking()
                .Where(item => item.Name.ToLower() == normalizedName))
            .OrderBy(item => item.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (sameName is null)
            return new TerritoryResolution(null, null);

        if (IsSafeNameFallback(sameName))
            return new TerritoryResolution(sameName, null);

        return new TerritoryResolution(
            null,
            $"Skipped because an existing {sameName.TerritoryKind} territory already uses the name '{sameName.Name}'.");
    }

    private async Task<TerritoryResolution> ResolveTerritoryAsync(
        BoundaryFeature feature,
        CancellationToken cancellationToken)
    {
        var normalizedName = feature.Name.ToLowerInvariant();

        var territory = !string.IsNullOrWhiteSpace(feature.Iso3)
            ? await _db.Territories
                .AsNoTracking()
                .Where(item =>
                    item.TerritoryKind == TerritoryKinds.Country &&
                    item.CountryIso3 == feature.Iso3)
                .OrderBy(item => item.Id)
                .Select(item => new TerritoryImportRow
                {
                    Id = item.Id,
                    Name = item.Name,
                    TerritoryKind = item.TerritoryKind,
                    CountryCode = item.CountryCode,
                    CountryIso3 = item.CountryIso3,
                    CreatedAt = item.CreatedAt
                })
                .FirstOrDefaultAsync(cancellationToken)
            : null;

        if (territory is not null)
            return new TerritoryResolution(territory, null);

        var aliasCandidates = CountryRegistry.BoundaryImportIsoAliases(feature.Iso3);
        if (aliasCandidates.Count > 1)
        {
            var candidates = await _db.Territories
                .AsNoTracking()
                .Where(item =>
                    item.TerritoryKind == TerritoryKinds.Country &&
                    ((item.CountryIso3 != null && aliasCandidates.Contains(item.CountryIso3)) ||
                     item.CountryCode == "XK" ||
                     item.Name.ToLower().Contains("kosov")))
                .OrderBy(item => item.Id)
                .Select(item => new TerritoryImportRow
                {
                    Id = item.Id,
                    Name = item.Name,
                    TerritoryKind = item.TerritoryKind,
                    CountryCode = item.CountryCode,
                    CountryIso3 = item.CountryIso3,
                    CreatedAt = item.CreatedAt
                })
                .ToListAsync(cancellationToken);

            territory = candidates.FirstOrDefault(item => IsKnownKosovoTerritoryFeatureMatch(item, feature));
            if (territory is not null)
                return new TerritoryResolution(territory, null);
        }

        if (!string.IsNullOrWhiteSpace(feature.Iso2))
        {
            territory = await _db.Territories
                .AsNoTracking()
                .Where(item =>
                    item.TerritoryKind == TerritoryKinds.Country &&
                    item.CountryCode == feature.Iso2)
                .OrderBy(item => item.Id)
                .Select(item => new TerritoryImportRow
                {
                    Id = item.Id,
                    Name = item.Name,
                    TerritoryKind = item.TerritoryKind,
                    CountryCode = item.CountryCode,
                    CountryIso3 = item.CountryIso3,
                    CreatedAt = item.CreatedAt
                })
                .FirstOrDefaultAsync(cancellationToken);

            if (territory is not null)
                return new TerritoryResolution(territory, null);
        }

        territory = await _db.Territories
            .AsNoTracking()
            .Where(item =>
                item.TerritoryKind == TerritoryKinds.Country &&
                item.Name.ToLower() == normalizedName)
            .OrderBy(item => item.Id)
            .Select(item => new TerritoryImportRow
            {
                Id = item.Id,
                Name = item.Name,
                TerritoryKind = item.TerritoryKind,
                CountryCode = item.CountryCode,
                CountryIso3 = item.CountryIso3,
                CreatedAt = item.CreatedAt
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (territory is not null)
            return new TerritoryResolution(territory, null);

        var sameName = await _db.Territories
            .AsNoTracking()
            .Where(item => item.Name.ToLower() == normalizedName)
            .OrderBy(item => item.Id)
            .Select(item => new TerritoryImportRow
            {
                Id = item.Id,
                Name = item.Name,
                TerritoryKind = item.TerritoryKind,
                CountryCode = item.CountryCode,
                CountryIso3 = item.CountryIso3,
                CreatedAt = item.CreatedAt,
                RegionCount = item.Regions.Count,
                TerritoryManagerAssignmentCount = item.TerritoryManagerTerritories.Count
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (sameName is null)
            return new TerritoryResolution(null, null);

        if (IsSafeNameFallback(sameName))
            return new TerritoryResolution(sameName, null);

        return new TerritoryResolution(
            null,
            $"Skipped because an existing {sameName.TerritoryKind} territory already uses the name '{sameName.Name}'.");
    }

    private async Task<TerritoryResolution> ResolveDisputedTerritoryAsync(
        BoundaryFeatureMetadata metadata,
        string externalKey,
        CancellationToken cancellationToken)
    {
        var name = FirstNonEmpty(
            metadata.Name,
            metadata.ShapeGroup,
            metadata.ShapeId,
            "Disputed territory");
        var normalizedName = name.ToLowerInvariant();
        var shapeGroup = metadata.ShapeGroup;
        var shapeType = NormalizeDisputedShapeType(metadata.RawShapeType);

        var territory = await SelectTerritoryImportRows(_db.Territories.AsNoTracking()
                .Where(item =>
                    item.TerritoryKind == TerritoryKinds.DisputedTerritory &&
                    item.BoundaryExternalKey == externalKey))
            .OrderBy(item => item.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (territory is not null)
            return new TerritoryResolution(territory, null);

        if (!string.IsNullOrWhiteSpace(shapeGroup))
        {
            territory = await SelectTerritoryImportRows(_db.Territories.AsNoTracking()
                    .Where(item =>
                        item.TerritoryKind == TerritoryKinds.DisputedTerritory &&
                        item.BoundaryShapeGroup == shapeGroup &&
                        (item.BoundaryShapeType == shapeType || item.BoundaryAdmLevel == DisputedAdmLevel)))
                .OrderBy(item => item.Id)
                .FirstOrDefaultAsync(cancellationToken);

            if (territory is not null)
                return new TerritoryResolution(territory, null);
        }

        territory = await SelectTerritoryImportRows(_db.Territories.AsNoTracking()
                .Where(item =>
                    item.TerritoryKind == TerritoryKinds.DisputedTerritory &&
                    item.Name.ToLower() == normalizedName))
            .OrderBy(item => item.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (territory is not null)
            return new TerritoryResolution(territory, null);

        var sameName = await SelectTerritoryImportRows(_db.Territories.AsNoTracking()
                .Where(item => item.Name.ToLower() == normalizedName))
            .OrderBy(item => item.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (sameName is null)
            return new TerritoryResolution(null, null);

        if (IsSafeNameFallback(sameName))
            return new TerritoryResolution(sameName, null);

        return new TerritoryResolution(
            null,
            $"Skipped because an existing {sameName.TerritoryKind} territory already uses the name '{sameName.Name}'.");
    }

    private IQueryable<TerritoryImportRow> SelectTerritoryImportRows(IQueryable<Territory> query) =>
        query.Select(item => new TerritoryImportRow
        {
            Id = item.Id,
            Name = item.Name,
            TerritoryKind = item.TerritoryKind,
            CountryCode = item.CountryCode,
            CountryIso3 = item.CountryIso3,
            BoundaryShapeType = item.BoundaryShapeType,
            BoundaryShapeGroup = item.BoundaryShapeGroup,
            BoundaryShapeId = item.BoundaryShapeId,
            BoundaryExternalKey = item.BoundaryExternalKey,
            BoundaryAdmLevel = item.BoundaryAdmLevel,
            CreatedAt = item.CreatedAt,
            RegionCount = item.Regions.Count,
            TerritoryManagerAssignmentCount = item.TerritoryManagerTerritories.Count,
            HasValidBoundary = EF.Property<Geometry?>(item, BoundaryGeometryPropertyName) != null &&
                !EF.Property<Geometry?>(item, BoundaryGeometryPropertyName)!.IsEmpty &&
                EF.Property<Geometry?>(item, BoundaryGeometryPropertyName)!.IsValid,
            HasValidDisplayBoundary = EF.Property<Geometry?>(item, BoundaryGeometryPropertyName) != null &&
                !EF.Property<Geometry?>(item, BoundaryGeometryPropertyName)!.IsEmpty &&
                EF.Property<Geometry?>(item, BoundaryGeometryPropertyName)!.IsValid &&
                EF.Property<Geometry?>(item, BoundaryDisplayGeometryPropertyName) != null &&
                !EF.Property<Geometry?>(item, BoundaryDisplayGeometryPropertyName)!.IsEmpty &&
                EF.Property<Geometry?>(item, BoundaryDisplayGeometryPropertyName)!.IsValid &&
                (EF.Property<Geometry?>(item, BoundaryGeometryPropertyName)!.Area <= 0d ||
                 EF.Property<Geometry?>(item, BoundaryDisplayGeometryPropertyName)!.Area >=
                 EF.Property<Geometry?>(item, BoundaryGeometryPropertyName)!.Area * 0.85d)
        });

    private async Task<TerritoryResolution> ResolveDisputedTerritoryAsync(
        BoundaryFeature feature,
        string externalKey,
        CancellationToken cancellationToken)
    {
        var normalizedName = feature.Name.ToLowerInvariant();
        var shapeGroup = feature.ShapeGroup;
        var shapeType = NormalizeDisputedShapeType(feature.ShapeType);

        var territory = await _db.Territories
            .AsNoTracking()
            .Where(item =>
                item.TerritoryKind == TerritoryKinds.DisputedTerritory &&
                item.BoundaryExternalKey == externalKey)
            .OrderBy(item => item.Id)
            .Select(item => new TerritoryImportRow
            {
                Id = item.Id,
                Name = item.Name,
                TerritoryKind = item.TerritoryKind,
                CountryCode = item.CountryCode,
                CountryIso3 = item.CountryIso3,
                CreatedAt = item.CreatedAt,
                BoundaryShapeGroup = item.BoundaryShapeGroup,
                BoundaryShapeType = item.BoundaryShapeType,
                BoundaryAdmLevel = item.BoundaryAdmLevel,
                BoundaryExternalKey = item.BoundaryExternalKey
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (territory is not null)
            return new TerritoryResolution(territory, null);

        if (!string.IsNullOrWhiteSpace(shapeGroup))
        {
            territory = await _db.Territories
                .AsNoTracking()
                .Where(item =>
                    item.TerritoryKind == TerritoryKinds.DisputedTerritory &&
                    item.BoundaryShapeGroup == shapeGroup &&
                    (item.BoundaryShapeType == shapeType || item.BoundaryAdmLevel == DisputedAdmLevel))
                .OrderBy(item => item.Id)
                .Select(item => new TerritoryImportRow
                {
                    Id = item.Id,
                    Name = item.Name,
                    TerritoryKind = item.TerritoryKind,
                    CountryCode = item.CountryCode,
                    CountryIso3 = item.CountryIso3,
                    CreatedAt = item.CreatedAt,
                    BoundaryShapeGroup = item.BoundaryShapeGroup,
                    BoundaryShapeType = item.BoundaryShapeType,
                    BoundaryAdmLevel = item.BoundaryAdmLevel,
                    BoundaryExternalKey = item.BoundaryExternalKey
                })
                .FirstOrDefaultAsync(cancellationToken);

            if (territory is not null)
                return new TerritoryResolution(territory, null);
        }

        territory = await _db.Territories
            .AsNoTracking()
            .Where(item =>
                item.TerritoryKind == TerritoryKinds.DisputedTerritory &&
                item.Name.ToLower() == normalizedName)
            .OrderBy(item => item.Id)
            .Select(item => new TerritoryImportRow
            {
                Id = item.Id,
                Name = item.Name,
                TerritoryKind = item.TerritoryKind,
                CountryCode = item.CountryCode,
                CountryIso3 = item.CountryIso3,
                CreatedAt = item.CreatedAt,
                BoundaryShapeGroup = item.BoundaryShapeGroup,
                BoundaryShapeType = item.BoundaryShapeType,
                BoundaryAdmLevel = item.BoundaryAdmLevel,
                BoundaryExternalKey = item.BoundaryExternalKey
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (territory is not null)
            return new TerritoryResolution(territory, null);

        var sameName = await _db.Territories
            .AsNoTracking()
            .Where(item => item.Name.ToLower() == normalizedName)
            .OrderBy(item => item.Id)
            .Select(item => new TerritoryImportRow
            {
                Id = item.Id,
                Name = item.Name,
                TerritoryKind = item.TerritoryKind,
                CountryCode = item.CountryCode,
                CountryIso3 = item.CountryIso3,
                CreatedAt = item.CreatedAt,
                BoundaryShapeGroup = item.BoundaryShapeGroup,
                BoundaryShapeType = item.BoundaryShapeType,
                BoundaryAdmLevel = item.BoundaryAdmLevel,
                BoundaryExternalKey = item.BoundaryExternalKey,
                RegionCount = item.Regions.Count,
                TerritoryManagerAssignmentCount = item.TerritoryManagerTerritories.Count
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (sameName is null)
            return new TerritoryResolution(null, null);

        if (IsSafeNameFallback(sameName))
            return new TerritoryResolution(sameName, null);

        return new TerritoryResolution(
            null,
            $"Skipped because an existing {sameName.TerritoryKind} territory already uses the name '{sameName.Name}'.");
    }

    private static void EnsureTerritoryMatchesFeature(TerritoryImportRow territory, BoundaryFeature feature)
    {
        if (!string.IsNullOrWhiteSpace(territory.CountryIso3) &&
            !BoundaryImportIsoCodesMatch(territory.CountryIso3, feature.Iso3) &&
            !IsKnownKosovoTerritoryFeatureMatch(territory, feature))
        {
            throw new InvalidOperationException("Local boundary ISO3 code does not match the existing territory.");
        }

        if (!string.IsNullOrWhiteSpace(territory.CountryCode) &&
            !string.IsNullOrWhiteSpace(feature.Iso2) &&
            !BoundaryImportIsoCodesMatch(territory.CountryCode, feature.Iso2) &&
            !IsKnownKosovoTerritoryFeatureMatch(territory, feature))
        {
            throw new InvalidOperationException("Local boundary ISO2 code does not match the existing territory.");
        }
    }

    private static bool TerritoryMetadataMatchesFeature(
        TerritoryImportRow territory,
        BoundaryFeatureMetadata metadata)
    {
        var iso2 = CountryRegistry.ToIso2ForBoundaryImport(metadata.Iso3);

        if (!string.IsNullOrWhiteSpace(territory.CountryIso3) &&
            !BoundaryImportIsoCodesMatch(territory.CountryIso3, metadata.Iso3) &&
            !IsKnownKosovoTerritoryMetadataMatch(territory, metadata))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(territory.CountryCode) &&
            !string.IsNullOrWhiteSpace(iso2) &&
            !BoundaryImportIsoCodesMatch(territory.CountryCode, iso2) &&
            !IsKnownKosovoTerritoryMetadataMatch(territory, metadata))
        {
            return false;
        }

        return true;
    }

    private static string ResolveCountryNameForMetadata(BoundaryFeatureMetadata metadata)
    {
        var iso3 = metadata.Iso3;
        return FirstNonEmpty(
            metadata.Name,
            CountryRegistry.IsKosovoIsoAlias(iso3) ? "Kosovo" : null,
            CountryRegistry.FindByIso3(iso3)?.Name,
            iso3);
    }

    private static string? ResolveCountryIso3ForImport(Territory territory, BoundaryFeature feature)
    {
        if (IsKnownKosovoTerritoryFeatureMatch(territory, feature) &&
            CountryRegistry.IsKosovoIsoAlias(territory.CountryIso3))
        {
            return territory.CountryIso3!.Trim().ToUpperInvariant();
        }

        return feature.Iso3;
    }

    private static string? ResolveCountryIso2ForImport(Territory territory, BoundaryFeature feature)
    {
        if (IsKnownKosovoTerritoryFeatureMatch(territory, feature))
        {
            var existingIso2 = CountryRegistry.NormalizeIso2(territory.CountryCode);
            return CountryRegistry.IsKosovoIsoAlias(existingIso2)
                ? existingIso2
                : CountryRegistry.ToIso2ForBoundaryImport(feature.Iso3) ?? feature.Iso2 ?? territory.CountryCode;
        }

        return feature.Iso2 ?? territory.CountryCode;
    }

    private static bool IsKnownKosovoTerritoryFeatureMatch(Territory territory, BoundaryFeature feature)
    {
        if (!CountryRegistry.IsKosovoIsoAlias(feature.Iso3) &&
            !CountryRegistry.IsKosovoIsoAlias(feature.ShapeGroup) &&
            !CountryRegistry.IsKosovoName(feature.Name))
        {
            return false;
        }

        return CountryRegistry.IsKosovoIsoAlias(territory.CountryIso3) ||
            CountryRegistry.IsKosovoIsoAlias(territory.CountryCode) ||
            CountryRegistry.IsKosovoName(territory.Name);
    }

    private static bool IsKnownKosovoTerritoryFeatureMatch(TerritoryImportRow territory, BoundaryFeature feature)
    {
        if (!CountryRegistry.IsKosovoIsoAlias(feature.Iso3) &&
            !CountryRegistry.IsKosovoIsoAlias(feature.ShapeGroup) &&
            !CountryRegistry.IsKosovoName(feature.Name))
        {
            return false;
        }

        return CountryRegistry.IsKosovoIsoAlias(territory.CountryIso3) ||
            CountryRegistry.IsKosovoIsoAlias(territory.CountryCode) ||
            CountryRegistry.IsKosovoName(territory.Name);
    }

    private static bool IsKnownKosovoTerritoryMetadataMatch(
        TerritoryImportRow territory,
        BoundaryFeatureMetadata metadata)
    {
        if (!CountryRegistry.IsKosovoIsoAlias(metadata.Iso3) &&
            !CountryRegistry.IsKosovoIsoAlias(metadata.ShapeGroup) &&
            !CountryRegistry.IsKosovoName(metadata.Name))
        {
            return false;
        }

        return CountryRegistry.IsKosovoIsoAlias(territory.CountryIso3) ||
            CountryRegistry.IsKosovoIsoAlias(territory.CountryCode) ||
            CountryRegistry.IsKosovoName(territory.Name);
    }

    private static bool IsSafeNameFallback(TerritoryImportRow territory) =>
        territory.RegionCount == 0 &&
        territory.TerritoryManagerAssignmentCount == 0;

    private static string ResolveImportStatus(bool isNewTerritory, bool hasDisplayGeometry) =>
        isNewTerritory
            ? hasDisplayGeometry ? "Imported" : "ImportedValidationOnly"
            : hasDisplayGeometry ? "Updated" : "UpdatedValidationOnly";

    private static BoundaryImportItemDto CreateSkippedImportItem(
        BoundaryFeature feature,
        string message,
        long durationMs) =>
        new()
        {
            Kind = feature.Kind,
            Iso3 = feature.Iso3,
            ShapeGroup = feature.ShapeGroup,
            Name = feature.Name,
            ShapeId = feature.ShapeId,
            ShapeType = feature.ShapeType,
            Status = "Skipped",
            Message = message,
            DurationMs = durationMs
        };

    private static BoundaryImportItemDto CreateSkippedImportItem(
        BoundaryFeatureMetadata metadata,
        string kind,
        string message,
        long durationMs) =>
        new()
        {
            Kind = kind,
            Iso3 = IsDisputedKind(kind) ? null : metadata.Iso3,
            ShapeGroup = metadata.ShapeGroup,
            Name = IsDisputedKind(kind)
                ? FirstNonEmpty(metadata.Name, metadata.ShapeGroup, metadata.ShapeId)
                : ResolveCountryNameForMetadata(metadata),
            ShapeId = metadata.ShapeId,
            ShapeType = metadata.RawShapeType,
            Status = "Skipped",
            Message = message,
            DurationMs = durationMs
        };

    private static BoundaryReconciliationItemDto MapReconciliationItem(
        BoundaryDbRow? row,
        string fallbackName,
        string territoryKind,
        string? iso2,
        string? iso3,
        string status,
        string? boundaryMissingReason)
    {
        var hasBoundary = IsValidBoundary(row?.BoundaryGeometry);
        var hasDisplayBoundary = TerritoryBoundaryDisplayGeometry.IsValid(
            row?.BoundaryGeometry,
            row?.BoundaryDisplayGeometry);

        return new BoundaryReconciliationItemDto
        {
            TerritoryId = row?.TerritoryId,
            Name = row?.Name ?? fallbackName,
            TerritoryKind = row?.TerritoryKind ?? territoryKind,
            Iso2 = row?.CountryCode ?? iso2,
            Iso3 = row?.CountryIso3 ?? iso3,
            ShapeGroup = row?.BoundaryShapeGroup,
            ShapeType = row?.BoundaryShapeType ?? row?.BoundaryAdmLevel,
            ShapeId = row?.BoundaryShapeId,
            HasBoundary = hasBoundary,
            HasValidationBoundary = hasBoundary,
            HasDisplayBoundary = hasDisplayBoundary,
            Status = status,
            BoundaryMissingReason = boundaryMissingReason
        };
    }

    private static BoundaryDbRow? FindMatchingSourceFeatureRow(
        BoundaryFeature feature,
        IReadOnlyCollection<BoundaryDbRow> rows)
    {
        if (IsDisputedFeature(feature))
        {
            var externalKey = BuildBoundaryExternalKey(feature);
            var shapeType = NormalizeDisputedShapeType(feature.ShapeType);
            return rows
                .Where(row => IsDisputedKind(row.TerritoryKind))
                .FirstOrDefault(row =>
                    string.Equals(row.BoundaryExternalKey, externalKey, StringComparison.OrdinalIgnoreCase) ||
                    (!string.IsNullOrWhiteSpace(feature.ShapeGroup) &&
                     string.Equals(row.BoundaryShapeGroup, feature.ShapeGroup, StringComparison.OrdinalIgnoreCase) &&
                     (string.Equals(row.BoundaryShapeType, shapeType, StringComparison.OrdinalIgnoreCase) ||
                      string.Equals(row.BoundaryAdmLevel, DisputedAdmLevel, StringComparison.OrdinalIgnoreCase))) ||
                    string.Equals(row.Name, feature.Name, StringComparison.OrdinalIgnoreCase));
        }

        return rows
            .Where(row => IsCountryKind(row.TerritoryKind))
            .FirstOrDefault(row =>
                (!string.IsNullOrWhiteSpace(feature.Iso3) &&
                 BoundaryImportIsoCodesMatch(row.CountryIso3, feature.Iso3)) ||
                (!string.IsNullOrWhiteSpace(feature.Iso2) &&
                 BoundaryImportIsoCodesMatch(row.CountryCode, feature.Iso2)) ||
                IsKnownKosovoRowFeatureMatch(row, feature) ||
                string.Equals(row.Name, feature.Name, StringComparison.OrdinalIgnoreCase));
    }

    private static bool FeatureMatchesRow(BoundaryFeature feature, BoundaryDbRow row)
    {
        if (IsDisputedFeature(feature))
            return IsDisputedKind(row.TerritoryKind) &&
                FindMatchingSourceFeatureRow(feature, new[] { row }) is not null;

        return IsCountryKind(row.TerritoryKind) &&
            FindMatchingSourceFeatureRow(feature, new[] { row }) is not null;
    }

    private static bool IsCountryKind(string? value) =>
        !string.Equals(value, TerritoryKinds.DisputedTerritory, StringComparison.Ordinal);

    private static bool BoundaryImportIsoCodesMatch(string? first, string? second) =>
        (!string.IsNullOrWhiteSpace(first) &&
         !string.IsNullOrWhiteSpace(second) &&
         string.Equals(first, second, StringComparison.OrdinalIgnoreCase)) ||
        CountryRegistry.AreBoundaryImportIsoAliases(first, second);

    private static bool IsKnownKosovoRowFeatureMatch(BoundaryDbRow row, BoundaryFeature feature)
    {
        if (!CountryRegistry.IsKosovoIsoAlias(feature.Iso3) &&
            !CountryRegistry.IsKosovoIsoAlias(feature.ShapeGroup) &&
            !CountryRegistry.IsKosovoName(feature.Name))
        {
            return false;
        }

        return CountryRegistry.IsKosovoIsoAlias(row.CountryIso3) ||
            CountryRegistry.IsKosovoIsoAlias(row.CountryCode) ||
            CountryRegistry.IsKosovoName(row.Name);
    }

    private static string DisputedViewOnlyMessage() =>
        "This disputed boundary is shown for map completeness and cannot be assigned as a country territory.";

    private static BoundaryFeature ParseBoundaryFeature(JsonElement featureElement, string expectedAdmLevel)
    {
        var normalizedExpectedAdmLevel = NormalizeAdmLevel(expectedAdmLevel);
        if (string.IsNullOrWhiteSpace(normalizedExpectedAdmLevel))
        {
            throw new InvalidOperationException("Boundary ADM level must be ADM0, ADM1, or ADM2.");
        }

        if (!TryGetProperty(featureElement, "type", out var typeElement) ||
            !string.Equals(GetString(typeElement), "Feature", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Boundary GeoJSON item is not a Feature.");
        }

        if (!TryGetProperty(featureElement, "properties", out var properties) ||
            properties.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException("Boundary feature properties are missing.");
        }

        var metadata = ReadBoundaryFeatureMetadata(properties);
        ValidateBoundaryAdmLevel(metadata, normalizedExpectedAdmLevel);

        if (!TryGetProperty(featureElement, "geometry", out var geometryElement) ||
            geometryElement.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            throw new InvalidOperationException("Boundary feature geometry is missing.");
        }

        var geometry = ExtractGeometry(geometryElement);
        geometry = ToValidPolygonalGeometry(geometry, copyWhenAlreadyValid: false)
            ?? throw new InvalidOperationException("Boundary feature geometry is invalid.");

        if (normalizedExpectedAdmLevel == "ADM0" && IsDisputedShapeType(metadata))
        {
            var disputedName = FirstNonEmpty(
                metadata.Name,
                metadata.ShapeGroup,
                metadata.ShapeId,
                "Disputed territory");

            return new BoundaryFeature(
                TerritoryKinds.DisputedTerritory,
                metadata.ShapeGroup,
                null,
                null,
                disputedName,
                metadata.ShapeId,
                NormalizeDisputedShapeType(metadata.RawShapeType),
                DisputedAdmLevel,
                geometry);
        }

        var iso3 = metadata.Iso3
            ?? throw new InvalidOperationException("Boundary feature ISO3 country code is missing or invalid.");
        var iso2 = CountryRegistry.ToIso2ForBoundaryImport(iso3);
        var name = FirstNonEmpty(
            metadata.Name,
            CountryRegistry.IsKosovoIsoAlias(iso3) ? "Kosovo" : null,
            CountryRegistry.FindByIso3(iso3)?.Name,
            iso3);

        return new BoundaryFeature(
            TerritoryKinds.Country,
            metadata.ShapeGroup,
            iso3,
            iso2,
            name,
            metadata.ShapeId,
            metadata.RawShapeType,
            normalizedExpectedAdmLevel,
            geometry);
    }

    private static BoundaryFeatureMetadata TryReadBoundaryFeatureMetadata(JsonElement featureElement)
    {
        return TryGetProperty(featureElement, "properties", out var properties) &&
            properties.ValueKind == JsonValueKind.Object
            ? ReadBoundaryFeatureMetadata(properties)
            : BoundaryFeatureMetadata.Empty;
    }

    private static BoundaryFeatureMetadata ReadBoundaryFeatureMetadata(JsonElement properties)
    {
        var shapeGroup = FirstNonEmpty(
            GetString(properties, "shapeGroup"),
            GetString(properties, "ISO3"),
            GetString(properties, "iso3"),
            GetString(properties, "adm0_a3"),
            GetString(properties, "ADM0_A3"),
            GetString(properties, "countryIso3"));
        var iso3 = CountryRegistry.NormalizeIso3ForBoundaryImport(shapeGroup);
        var name = FirstNonEmpty(
            GetString(properties, "shapeName"),
            GetString(properties, "name"),
            iso3 is null ? null : CountryRegistry.FindByIso3(iso3)?.Name,
            iso3);
        var shapeId = FirstNonEmpty(
            GetString(properties, "shapeID"),
            GetString(properties, "shapeId"),
            GetString(properties, "id"));
        var rawShapeType = FirstNonEmptyRaw(
            GetString(properties, "shapeType"),
            GetString(properties, "admLevel"),
            GetString(properties, "ADM_LEVEL"));

        return new BoundaryFeatureMetadata(
            string.IsNullOrWhiteSpace(shapeGroup) ? null : shapeGroup,
            iso3,
            string.IsNullOrWhiteSpace(name) ? null : name,
            string.IsNullOrWhiteSpace(shapeId) ? null : shapeId,
            string.IsNullOrWhiteSpace(rawShapeType) ? null : rawShapeType,
            NormalizeAdmLevel(rawShapeType));
    }

    private static void ValidateBoundaryAdmLevel(
        BoundaryFeatureMetadata metadata,
        string expectedAdmLevel)
    {
        if (metadata.NormalizedShapeType == expectedAdmLevel)
            return;

        if (expectedAdmLevel == "ADM0")
        {
            if (IsDisputedShapeType(metadata))
                return;

            if (IsExplicitDifferentAdmLevel(metadata, expectedAdmLevel))
            {
                throw new InvalidOperationException(BuildAdmMismatchMessage(metadata, expectedAdmLevel));
            }

            return;
        }

        if (string.IsNullOrWhiteSpace(metadata.RawShapeType))
        {
            throw new InvalidOperationException($"Boundary feature shapeType is missing; expected {expectedAdmLevel}.");
        }

        throw new InvalidOperationException(BuildAdmMismatchMessage(metadata, expectedAdmLevel));
    }

    private static bool IsExplicitDifferentAdmLevel(
        BoundaryFeatureMetadata metadata,
        string expectedAdmLevel)
    {
        if (string.IsNullOrWhiteSpace(metadata.RawShapeType))
            return false;

        if (!string.IsNullOrWhiteSpace(metadata.NormalizedShapeType))
            return metadata.NormalizedShapeType != expectedAdmLevel;

        var compact = CompactAdmLevelValue(metadata.RawShapeType);
        return compact.StartsWith("ADM", StringComparison.Ordinal);
    }

    private static bool IsDisputedShapeType(BoundaryFeatureMetadata metadata)
    {
        if (string.IsNullOrWhiteSpace(metadata.RawShapeType))
            return false;

        var compact = CompactAdmLevelValue(metadata.RawShapeType);
        return compact is "DISP" or "DISPUTED";
    }

    private static bool IsDisputedFeature(BoundaryFeature feature) =>
        string.Equals(feature.Kind, TerritoryKinds.DisputedTerritory, StringComparison.Ordinal);

    private static string NormalizeDisputedShapeType(string? rawShapeType)
    {
        var compact = string.IsNullOrWhiteSpace(rawShapeType)
            ? DisputedAdmLevel
            : CompactAdmLevelValue(rawShapeType);

        return compact.Length > 32
            ? compact[..32]
            : compact;
    }

    private static string BuildBoundaryExternalKey(BoundaryFeature feature)
    {
        var sourceKey = IsDisputedFeature(feature)
            ? FirstNonEmpty(feature.ShapeGroup, feature.ShapeId, feature.Name)
            : feature.Iso3 ?? FirstNonEmpty(feature.ShapeGroup, feature.Name);
        var level = IsDisputedFeature(feature) ? DisputedAdmLevel : "ADM0";
        var key = $"{GeoBoundariesSource}:{level}:{sourceKey}";

        return key.Length > 300 ? key[..300] : key;
    }

    private static string BuildBoundaryExternalKey(BoundaryFeatureMetadata metadata)
    {
        var sourceKey = IsDisputedShapeType(metadata)
            ? FirstNonEmpty(metadata.ShapeGroup, metadata.ShapeId, metadata.Name)
            : metadata.Iso3 ?? FirstNonEmpty(metadata.ShapeGroup, metadata.Name);
        var level = IsDisputedShapeType(metadata) ? DisputedAdmLevel : "ADM0";
        var key = $"{GeoBoundariesSource}:{level}:{sourceKey}";

        return key.Length > 300 ? key[..300] : key;
    }

    internal static string BuildRegionCandidateExternalKey(
        string admLevel,
        string shapeGroup,
        string? shapeId,
        string name)
    {
        var normalizedLevel = NormalizeRegionAdmLevel(admLevel);
        var normalizedShapeGroup = CountryRegistry.NormalizeIso3ForBoundaryImport(shapeGroup)
            ?? throw new InvalidOperationException("Boundary candidate shapeGroup must be a valid ISO3 country code.");
        var sourceKey = FirstNonEmpty(shapeId, name, normalizedShapeGroup)
            .Replace(':', '_');
        var key = $"{GeoBoundariesSource}:{normalizedLevel}:{normalizedShapeGroup}:{sourceKey}";

        return key.Length > 300 ? key[..300] : key;
    }

    private async Task<int?> ResolveCandidateCountryTerritoryIdAsync(
        string countryIso3,
        CancellationToken cancellationToken)
    {
        var normalizedIso3 = CountryRegistry.NormalizeIso3ForBoundaryImport(countryIso3)
            ?? throw new InvalidOperationException("Boundary candidate ISO3 country code is missing or invalid.");
        if (_candidateCountryTerritoryIds.TryGetValue(normalizedIso3, out var cachedTerritoryId))
            return cachedTerritoryId;

        var normalizedIso2 = CountryRegistry.ToIso2ForBoundaryImport(normalizedIso3);
        var isoAliases = CountryRegistry.BoundaryImportIsoAliases(normalizedIso3);

        var territoryId = await _db.Territories
            .AsNoTracking()
            .Where(territory => territory.TerritoryKind == TerritoryKinds.Country)
            .Where(territory =>
                (territory.CountryIso3 != null && isoAliases.Contains(territory.CountryIso3)) ||
                (normalizedIso2 != null && territory.CountryCode == normalizedIso2))
            .OrderBy(territory => territory.Id)
            .Select(territory => (int?)territory.Id)
            .FirstOrDefaultAsync(cancellationToken);

        _candidateCountryTerritoryIds[normalizedIso3] = territoryId;
        return territoryId;
    }

    private static bool BoundaryCandidateMetadataMatches(
        BoundaryCandidateImportRow candidate,
        string admLevel,
        string shapeGroup,
        string shapeId,
        string shapeName,
        string? shapeType,
        string externalKey,
        string countryIso3,
        int? countryTerritoryId) =>
        string.Equals(candidate.Source, GeoBoundariesSource, StringComparison.Ordinal) &&
        string.Equals(candidate.ReleaseType, ReleaseType, StringComparison.Ordinal) &&
        string.Equals(candidate.AdmLevel, admLevel, StringComparison.Ordinal) &&
        string.Equals(candidate.ShapeGroup, shapeGroup, StringComparison.Ordinal) &&
        string.Equals(candidate.ShapeId, shapeId, StringComparison.Ordinal) &&
        string.Equals(candidate.ShapeName, shapeName, StringComparison.Ordinal) &&
        string.Equals(candidate.ShapeType, shapeType, StringComparison.Ordinal) &&
        string.Equals(candidate.ExternalKey, externalKey, StringComparison.Ordinal) &&
        string.Equals(candidate.CountryIso3, countryIso3, StringComparison.Ordinal) &&
        candidate.CountryTerritoryId == countryTerritoryId;

    private static string NormalizeRequiredBoundaryCandidateShapeId(string? value)
    {
        var shapeId = value?.Trim() ?? string.Empty;
        if (shapeId.Length == 0)
            throw new InvalidOperationException("Boundary candidate shape id is required.");

        return shapeId.Length > 128 ? shapeId[..128] : shapeId;
    }

    private static string NormalizeRequiredBoundaryCandidateShapeName(string? value)
    {
        var name = value?.Trim() ?? string.Empty;
        if (name.Length == 0)
            throw new InvalidOperationException("Boundary candidate name is required.");

        return name.Length > 200 ? name[..200] : name;
    }

    private static string BuildAdmMismatchMessage(BoundaryFeatureMetadata metadata, string expectedAdmLevel)
    {
        var shapeType = string.IsNullOrWhiteSpace(metadata.RawShapeType)
            ? "(missing)"
            : metadata.RawShapeType;
        var normalized = string.IsNullOrWhiteSpace(metadata.NormalizedShapeType)
            ? "(unrecognized)"
            : metadata.NormalizedShapeType;

        return $"Boundary feature shapeType '{shapeType}' normalized as '{normalized}' does not match expected {expectedAdmLevel}.";
    }

    private static Geometry ExtractGeometry(JsonElement element)
    {
        var geometries = ExtractGeometries(element).ToList();
        if (geometries.Count == 0)
            throw new InvalidOperationException("Boundary GeoJSON did not contain a Polygon or MultiPolygon geometry.");

        var polygons = geometries.SelectMany(FlattenPolygons).ToArray();
        if (polygons.Length == 0)
            throw new InvalidOperationException("Boundary GeoJSON did not contain polygonal geometry.");

        Geometry geometry = polygons.Length == 1
            ? polygons[0]
            : Wgs84GeometryFactory.CreateMultiPolygon(polygons);

        SetSrid(geometry);
        return geometry;
    }

    private static IEnumerable<Geometry> ExtractGeometries(JsonElement element)
    {
        var type = GetString(element, "type");
        if (string.IsNullOrWhiteSpace(type))
            yield break;

        if (type.Equals("Feature", StringComparison.OrdinalIgnoreCase))
        {
            if (TryGetProperty(element, "geometry", out var geometryElement))
            {
                foreach (var geometry in ExtractGeometries(geometryElement))
                {
                    yield return geometry;
                }
            }

            yield break;
        }

        if (type.Equals("GeometryCollection", StringComparison.OrdinalIgnoreCase))
        {
            if (!TryGetProperty(element, "geometries", out var geometries) ||
                geometries.ValueKind != JsonValueKind.Array)
            {
                yield break;
            }

            foreach (var geometryElement in geometries.EnumerateArray())
            {
                foreach (var geometry in ExtractGeometries(geometryElement))
                {
                    yield return geometry;
                }
            }

            yield break;
        }

        if (!TryGetProperty(element, "coordinates", out var coordinates))
            yield break;

        if (type.Equals("Polygon", StringComparison.OrdinalIgnoreCase))
        {
            yield return BuildPolygon(coordinates);
        }
        else if (type.Equals("MultiPolygon", StringComparison.OrdinalIgnoreCase))
        {
            yield return BuildMultiPolygon(coordinates);
        }
    }

    private static IEnumerable<Polygon> FlattenPolygons(Geometry geometry)
    {
        if (geometry is Polygon polygon)
        {
            yield return polygon;
            yield break;
        }

        if (geometry is MultiPolygon multiPolygon)
        {
            for (var index = 0; index < multiPolygon.NumGeometries; index++)
            {
                if (multiPolygon.GetGeometryN(index) is Polygon item)
                    yield return item;
            }
        }
    }

    private static Polygon BuildPolygon(JsonElement coordinatesElement)
    {
        if (coordinatesElement.ValueKind != JsonValueKind.Array)
            throw new InvalidOperationException("Boundary polygon coordinates are invalid.");

        var rings = new List<Coordinate[]>();
        var ringIndex = 0;
        foreach (var ringElement in coordinatesElement.EnumerateArray())
        {
            try
            {
                rings.Add(ParseRing(ringElement));
            }
            catch (InvalidOperationException) when (ringIndex > 0)
            {
                // Invalid interior rings should not discard an otherwise usable source polygon.
            }

            ringIndex++;
        }

        if (rings.Count == 0)
            throw new InvalidOperationException("Boundary polygon must include an exterior ring.");

        var shell = Wgs84GeometryFactory.CreateLinearRing(rings[0]);
        var holes = rings
            .Skip(1)
            .Select(ring => Wgs84GeometryFactory.CreateLinearRing(ring))
            .ToArray();

        var polygon = Wgs84GeometryFactory.CreatePolygon(shell, holes);
        polygon.SRID = 4326;
        return polygon;
    }

    private static MultiPolygon BuildMultiPolygon(JsonElement coordinatesElement)
    {
        if (coordinatesElement.ValueKind != JsonValueKind.Array)
            throw new InvalidOperationException("Boundary multipolygon coordinates are invalid.");

        var polygons = new List<Polygon>();
        foreach (var polygonElement in coordinatesElement.EnumerateArray())
        {
            try
            {
                polygons.Add(BuildPolygon(polygonElement));
            }
            catch (InvalidOperationException)
            {
                // Some source MultiPolygons contain degenerate island slivers. Keep valid members.
            }
        }

        if (polygons.Count == 0)
            throw new InvalidOperationException("Boundary multipolygon must include at least one polygon.");

        var multiPolygon = Wgs84GeometryFactory.CreateMultiPolygon(polygons.ToArray());
        multiPolygon.SRID = 4326;
        return multiPolygon;
    }

    private static Coordinate[] ParseRing(JsonElement ringElement)
    {
        if (ringElement.ValueKind != JsonValueKind.Array)
            throw new InvalidOperationException("Boundary ring coordinates are invalid.");

        var coordinates = ringElement.EnumerateArray()
            .Select(ParseCoordinate)
            .ToList();

        if (coordinates.Count < 3)
            throw new InvalidOperationException("Boundary polygon rings must contain at least three points.");

        var first = coordinates[0];
        var last = coordinates[^1];
        if (!CoordinatesEqual(first, last))
        {
            coordinates.Add(new Coordinate(first.X, first.Y));
        }

        var uniqueCount = coordinates
            .Take(coordinates.Count - 1)
            .Select(coordinate => $"{Math.Round(coordinate.X, 7)}:{Math.Round(coordinate.Y, 7)}")
            .Distinct(StringComparer.Ordinal)
            .Count();

        if (uniqueCount < 3 || coordinates.Count < 4)
            throw new InvalidOperationException("Boundary polygon rings must contain at least three distinct points.");

        return coordinates.ToArray();
    }

    private static Coordinate ParseCoordinate(JsonElement coordinateElement)
    {
        if (coordinateElement.ValueKind != JsonValueKind.Array)
            throw new InvalidOperationException("Boundary coordinates are invalid.");

        var values = coordinateElement.EnumerateArray().ToArray();
        if (values.Length < 2 ||
            !values[0].TryGetDouble(out var longitude) ||
            !values[1].TryGetDouble(out var latitude) ||
            !double.IsFinite(latitude) ||
            !double.IsFinite(longitude) ||
            latitude is < -90d or > 90d ||
            longitude is < -180d or > 180d)
        {
            throw new InvalidOperationException("Boundary coordinates are invalid.");
        }

        return new Coordinate(longitude, latitude);
    }

    private static Geometry? ToValidPolygonalGeometry(
        Geometry? geometry,
        bool copyWhenAlreadyValid = true)
    {
        if (geometry is null || geometry.IsEmpty)
            return null;

        SetSrid(geometry);
        if (geometry.IsValid && geometry is Polygon or MultiPolygon)
            return copyWhenAlreadyValid ? geometry.Copy() : geometry;

        var copy = geometry.Copy();
        SetSrid(copy);

        if (!copy.IsValid)
        {
            copy = copy.Buffer(0);
            SetSrid(copy);
        }

        if (copy.IsEmpty || !copy.IsValid)
            return null;

        if (copy is Polygon or MultiPolygon)
            return copy;

        if (copy is GeometryCollection collection)
        {
            var polygons = Enumerable.Range(0, collection.NumGeometries)
                .Select(collection.GetGeometryN)
                .SelectMany(FlattenPolygons)
                .Cast<Geometry>()
                .ToArray();

            if (polygons.Length == 0)
                return null;

            var union = UnaryUnionOp.Union(polygons);
            SetSrid(union);
            return union is Polygon or MultiPolygon && !union.IsEmpty && union.IsValid
                ? union
                : null;
        }

        return null;
    }

    private static bool IsValidBoundary(Geometry? geometry) =>
        ToValidPolygonalGeometry(geometry) is not null;

    private static TerritoryRegionGeometryDto? ToRegionGeoJson(Geometry? geometry)
    {
        var polygonal = ToValidPolygonalGeometry(geometry);
        if (polygonal is null)
            return null;

        return polygonal switch
        {
            Polygon polygon => new TerritoryRegionGeometryDto
            {
                Type = "Polygon",
                Coordinates = ToPolygonCoordinates(polygon)
            },
            MultiPolygon multiPolygon => new TerritoryRegionGeometryDto
            {
                Type = "MultiPolygon",
                Coordinates = Enumerable.Range(0, multiPolygon.NumGeometries)
                    .Select(index => ToPolygonCoordinates((Polygon)multiPolygon.GetGeometryN(index)))
                    .ToArray()
            },
            _ => null
        };
    }

    private static TerritoryRegionMapViewportDto BuildRegionMapViewport(Geometry? geometry)
    {
        var polygonal = ToValidPolygonalGeometry(geometry);
        if (polygonal is null)
        {
            return new TerritoryRegionMapViewportDto { IsEmpty = true };
        }

        var envelope = polygonal.EnvelopeInternal;
        var centroid = polygonal.Centroid;

        return new TerritoryRegionMapViewportDto
        {
            CenterLatitude = centroid.Y,
            CenterLongitude = centroid.X,
            Zoom = 7,
            Bounds = new TerritoryRegionMapBoundsDto
            {
                North = envelope.MaxY,
                South = envelope.MinY,
                East = envelope.MaxX,
                West = envelope.MinX
            },
            IsEmpty = false
        };
    }

    private static double[][][] ToPolygonCoordinates(Polygon polygon)
    {
        var rings = new List<double[][]>
        {
            ToRingCoordinates(polygon.ExteriorRing)
        };

        for (var index = 0; index < polygon.NumInteriorRings; index++)
        {
            rings.Add(ToRingCoordinates(polygon.GetInteriorRingN(index)));
        }

        return rings.ToArray();
    }

    private static double[][] ToRingCoordinates(LineString ring) =>
        ring.Coordinates
            .Select(coordinate => new[]
            {
                Math.Round(coordinate.X, 7, MidpointRounding.AwayFromZero),
                Math.Round(coordinate.Y, 7, MidpointRounding.AwayFromZero)
            })
            .ToArray();

    private async Task ScanFeatureElementsAsync(
        string path,
        Func<JsonElement, int, CancellationToken, ValueTask<bool>> onFeature,
        CancellationToken cancellationToken)
    {
        await using var stream = OpenBoundaryStream(path);
        using var reader = new StreamReader(
            stream,
            Encoding.UTF8,
            detectEncodingFromByteOrderMarks: true,
            bufferSize: 1024 * 64,
            leaveOpen: false);

        var buffer = new char[1024 * 64];
        var tokenBuilder = new StringBuilder();
        var featureBuilder = new StringBuilder();
        var sawFeaturesProperty = false;
        var insideFeaturesArray = false;
        var capturingFeature = false;
        var inString = false;
        var escaped = false;
        var depth = 0;
        var featureIndex = 0;

        while (true)
        {
            var read = await reader.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
            if (read == 0)
                break;

            for (var index = 0; index < read; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var ch = buffer[index];

                if (!insideFeaturesArray)
                {
                    if (inString)
                    {
                        if (escaped)
                        {
                            tokenBuilder.Append(ch);
                            escaped = false;
                        }
                        else if (ch == '\\')
                        {
                            escaped = true;
                        }
                        else if (ch == '"')
                        {
                            inString = false;
                            if (tokenBuilder.ToString().Equals("features", StringComparison.OrdinalIgnoreCase))
                            {
                                sawFeaturesProperty = true;
                            }
                            tokenBuilder.Clear();
                        }
                        else
                        {
                            tokenBuilder.Append(ch);
                        }
                    }
                    else if (ch == '"')
                    {
                        inString = true;
                        escaped = false;
                        tokenBuilder.Clear();
                    }
                    else if (sawFeaturesProperty && ch == '[')
                    {
                        insideFeaturesArray = true;
                        sawFeaturesProperty = false;
                    }

                    continue;
                }

                if (!capturingFeature)
                {
                    if (ch == '{')
                    {
                        capturingFeature = true;
                        inString = false;
                        escaped = false;
                        depth = 1;
                        featureBuilder.Clear();
                        featureBuilder.Append(ch);
                    }
                    else if (ch == ']')
                    {
                        return;
                    }

                    continue;
                }

                featureBuilder.Append(ch);

                if (inString)
                {
                    if (escaped)
                    {
                        escaped = false;
                    }
                    else if (ch == '\\')
                    {
                        escaped = true;
                    }
                    else if (ch == '"')
                    {
                        inString = false;
                    }

                    continue;
                }

                if (ch == '"')
                {
                    inString = true;
                    escaped = false;
                    continue;
                }

                if (ch == '{')
                {
                    depth++;
                }
                else if (ch == '}')
                {
                    depth--;
                    if (depth == 0)
                    {
                        featureIndex++;
                        using var document = JsonDocument.Parse(featureBuilder.ToString());
                        var shouldContinue = await onFeature(document.RootElement, featureIndex, cancellationToken);
                        capturingFeature = false;
                        featureBuilder.Clear();

                        if (!shouldContinue)
                            return;
                    }
                }
            }
        }

        if (!insideFeaturesArray)
            throw new InvalidOperationException("Boundary GeoJSON FeatureCollection does not contain a features array.");

        if (capturingFeature)
            throw new InvalidOperationException("Boundary GeoJSON ended before the current feature was complete.");
    }

    private Stream OpenBoundaryStream(string path)
    {
        if (!File.Exists(path))
            throw new InvalidOperationException($"Boundary file was not found: {BuildSourceReference(path)}");

        var fileStream = File.OpenRead(path);
        return path.EndsWith(".gz", StringComparison.OrdinalIgnoreCase)
            ? new GZipStream(fileStream, CompressionMode.Decompress)
            : fileStream;
    }

    private string ResolveBoundaryPath(string admLevel)
    {
        var configuredPath = admLevel switch
        {
            "ADM0" => _options.Adm0GeoJsonPath,
            "ADM1" => _options.Adm1GeoJsonPath,
            "ADM2" => _options.Adm2GeoJsonPath,
            _ => throw new InvalidOperationException("Boundary ADM level must be ADM0, ADM1, or ADM2.")
        };

        if (string.IsNullOrWhiteSpace(configuredPath))
            throw new InvalidOperationException($"BoundaryImport:{admLevel}GeoJsonPath is required.");

        var expanded = Environment.ExpandEnvironmentVariables(configuredPath.Trim());
        var fullPath = Path.IsPathRooted(expanded)
            ? expanded
            : Path.Combine(_environment.ContentRootPath, expanded);

        return Path.GetFullPath(fullPath);
    }

    private string BuildSourceReference(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var contentRoot = Path.GetFullPath(_environment.ContentRootPath);
        var relative = Path.GetRelativePath(contentRoot, fullPath)
            .Replace('\\', '/');

        return relative.StartsWith("..", StringComparison.Ordinal)
            ? $"local://{Path.GetFileName(fullPath)}"
            : $"local://{relative}";
    }

    private BoundaryImportSummaryDto CreateSummary(
        string admLevel,
        string sourcePath,
        string? countryIso3 = null,
        int? takeLimit = null) =>
        new()
        {
            Source = GeoBoundariesSource,
            AdmLevel = admLevel,
            SourcePath = sourcePath,
            CountryIso3 = countryIso3,
            TakeLimit = takeLimit,
            CompletedSourceScan = true,
            StartedAt = DateTime.UtcNow
        };

    private void CompleteSummary(BoundaryImportSummaryDto summary)
    {
        if (summary.MatchedFilterCount == 0 &&
            summary.FilteredOutCount == 0 &&
            summary.ProcessedCount > 0)
        {
            summary.MatchedFilterCount = summary.ProcessedCount;
        }

        summary.CompletedAt = DateTime.UtcNow;
        summary.DurationMs = Math.Max(
            0,
            (long)(summary.CompletedAt - summary.StartedAt).TotalMilliseconds);
    }

    private void LogImportStarted(
        string admLevel,
        string path,
        bool forceRefresh,
        int saveBatchSize,
        int maxSummaryItems,
        int progressLogInterval,
        string? countryIso3,
        int? takeLimit)
    {
        var fileSize = TryGetBoundaryFileSize(path);
        _logger.LogInformation(
            "Local {AdmLevel} boundary import started. Source={Source}; bytes={Bytes}; force={Force}; countryIso3={CountryIso3}; take={TakeLimit}; saveBatchSize={SaveBatchSize}; maxSummaryItems={MaxSummaryItems}; progressLogInterval={ProgressLogInterval}",
            admLevel,
            BuildSourceReference(path),
            fileSize,
            forceRefresh,
            countryIso3 ?? "(all)",
            takeLimit,
            saveBatchSize,
            maxSummaryItems,
            progressLogInterval);
    }

    private void LogImportProgressIfNeeded(
        BoundaryImportSummaryDto summary,
        int progressLogInterval)
    {
        if (progressLogInterval <= 0 ||
            summary.ProcessedCount <= 0 ||
            summary.ProcessedCount % progressLogInterval != 0)
        {
            return;
        }

        _logger.LogInformation(
            "Local {AdmLevel} boundary import progress. scanned={Scanned}; matched={Matched}; filteredOut={FilteredOut}; ignoredLevelMismatch={IgnoredLevelMismatch}; processed={Processed}; imported={Imported}; updated={Updated}; skipped={Skipped}; failed={Failed}; elapsedMs={ElapsedMs}",
            summary.AdmLevel,
            summary.TotalFeatures,
            summary.MatchedFilterCount,
            summary.FilteredOutCount,
            summary.IgnoredLevelMismatchCount,
            summary.ProcessedCount,
            summary.ImportedCount,
            summary.UpdatedCount,
            summary.SkippedCount,
            summary.FailedCount,
            (long)(DateTime.UtcNow - summary.StartedAt).TotalMilliseconds);
    }

    private void LogImportCompleted(BoundaryImportSummaryDto summary)
    {
        _logger.LogInformation(
            "Local {AdmLevel} boundary import completed. scanned={Scanned}; matched={Matched}; filteredOut={FilteredOut}; ignoredLevelMismatch={IgnoredLevelMismatch}; processed={Processed}; imported={Imported}; updated={Updated}; skipped={Skipped}; failed={Failed}; completedSourceScan={CompletedSourceScan}; hasMore={HasMore}; durationMs={DurationMs}; itemsTruncated={ItemsTruncated}",
            summary.AdmLevel,
            summary.TotalFeatures,
            summary.MatchedFilterCount,
            summary.FilteredOutCount,
            summary.IgnoredLevelMismatchCount,
            summary.ProcessedCount,
            summary.ImportedCount,
            summary.UpdatedCount,
            summary.SkippedCount,
            summary.FailedCount,
            summary.CompletedSourceScan,
            summary.HasMore,
            summary.DurationMs,
            summary.ItemsTruncated);
    }

    private async Task SaveImportBatchIfNeededAsync(
        BoundaryImportBatchState batch,
        BoundaryImportSummaryDto summary,
        int saveBatchSize,
        CancellationToken cancellationToken)
    {
        if (batch.PendingChanges < saveBatchSize)
            return;

        await SaveImportBatchAsync(batch, summary, cancellationToken);
    }

    private async Task SaveImportBatchAsync(
        BoundaryImportBatchState batch,
        BoundaryImportSummaryDto summary,
        CancellationToken cancellationToken)
    {
        if (batch.PendingChanges <= 0)
            return;

        var changedRows = batch.PendingChanges;
        await _db.SaveChangesAsync(cancellationToken);
        _db.ChangeTracker.Clear();

        if (batch.InvalidatesMapCache)
        {
            TerritoryMapBoundaryCache.Invalidate(_cache);
        }

        _logger.LogInformation(
            "Local {AdmLevel} boundary import saved batch. changedRows={ChangedRows}; processed={Processed}; imported={Imported}; updated={Updated}; failed={Failed}",
            summary.AdmLevel,
            changedRows,
            summary.ProcessedCount,
            summary.ImportedCount,
            summary.UpdatedCount,
            summary.FailedCount);

        batch.PendingChanges = 0;
        batch.InvalidatesMapCache = false;
    }

    private static void TrackPendingChange(
        BoundaryImportBatchState batch,
        BoundaryImportFeatureResult result)
    {
        if (!result.Changed)
            return;

        batch.PendingChanges++;
        batch.InvalidatesMapCache |= result.InvalidatesMapCache;
    }

    private HashSet<object> CaptureTrackedEntities() =>
        _db.ChangeTracker
            .Entries()
            .Select(entry => entry.Entity)
            .ToHashSet(ReferenceEqualityComparer.Instance);

    private void DetachTrackedEntriesAddedAfter(HashSet<object> trackedBeforeFeature)
    {
        foreach (var entry in _db.ChangeTracker.Entries().ToList())
        {
            if (!trackedBeforeFeature.Contains(entry.Entity))
            {
                entry.State = EntityState.Detached;
            }
        }
    }

    private static void MarkTerritoryImportPropertiesModified(EntityEntry<Territory> entry)
    {
        entry.Property(item => item.Name).IsModified = true;
        entry.Property(item => item.TerritoryKind).IsModified = true;
        entry.Property(item => item.CountryCode).IsModified = true;
        entry.Property(item => item.CountryIso3).IsModified = true;
        entry.Property(item => item.BoundaryShapeType).IsModified = true;
        entry.Property(item => item.BoundaryShapeGroup).IsModified = true;
        entry.Property(item => item.BoundaryShapeId).IsModified = true;
        entry.Property(item => item.BoundaryExternalKey).IsModified = true;
        entry.Property(item => item.BoundarySource).IsModified = true;
        entry.Property(item => item.BoundarySourceUrl).IsModified = true;
        entry.Property(item => item.BoundaryAdmLevel).IsModified = true;
        entry.Property(item => item.BoundaryReleaseType).IsModified = true;
        entry.Property(item => item.BoundaryLicense).IsModified = true;
        entry.Property(item => item.BoundaryImportedAt).IsModified = true;
        entry.Property(item => item.UpdatedAt).IsModified = true;
    }

    private static void MarkBoundaryCandidateImportPropertiesModified(
        EntityEntry<AdministrativeBoundaryCandidate> entry)
    {
        entry.Property(item => item.Source).IsModified = true;
        entry.Property(item => item.ReleaseType).IsModified = true;
        entry.Property(item => item.AdmLevel).IsModified = true;
        entry.Property(item => item.ShapeGroup).IsModified = true;
        entry.Property(item => item.ShapeId).IsModified = true;
        entry.Property(item => item.ShapeName).IsModified = true;
        entry.Property(item => item.ShapeType).IsModified = true;
        entry.Property(item => item.ExternalKey).IsModified = true;
        entry.Property(item => item.CountryIso3).IsModified = true;
        entry.Property(item => item.CountryTerritoryId).IsModified = true;
        entry.Property(item => item.ImportedAt).IsModified = true;
        entry.Property(item => item.UpdatedAt).IsModified = true;
    }

    private static long? TryGetBoundaryFileSize(string path)
    {
        try
        {
            return File.Exists(path) ? new FileInfo(path).Length : null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    private static BoundaryImportItemDto CreateFailedImportItem(
        BoundaryFeatureMetadata metadata,
        string message,
        long durationMs) =>
        new()
        {
            Kind = IsDisputedShapeType(metadata)
                ? TerritoryKinds.DisputedTerritory
                : TerritoryKinds.Country,
            Iso3 = metadata.Iso3,
            ShapeGroup = metadata.ShapeGroup,
            Name = metadata.Name,
            ShapeId = metadata.ShapeId,
            ShapeType = metadata.RawShapeType,
            Status = "Failed",
            Message = message,
            DurationMs = durationMs
        };

    private static bool TryCreateIgnoredLevelMismatchImportItem(
        BoundaryFeatureMetadata metadata,
        string expectedAdmLevel,
        long durationMs,
        out BoundaryImportItemDto item)
    {
        var normalizedExpectedAdmLevel = NormalizeRegionAdmLevel(expectedAdmLevel);
        if (!IsExpectedSourceLevelMismatch(metadata, normalizedExpectedAdmLevel))
        {
            item = default!;
            return false;
        }

        var shapeType = string.IsNullOrWhiteSpace(metadata.RawShapeType)
            ? "(missing)"
            : metadata.RawShapeType;

        item = new BoundaryImportItemDto
        {
            Kind = TerritoryKinds.Country,
            Iso3 = metadata.Iso3,
            ShapeGroup = metadata.ShapeGroup,
            Name = metadata.Name,
            ShapeId = metadata.ShapeId,
            ShapeType = metadata.RawShapeType,
            Status = "IgnoredLevelMismatch",
            Message = $"Source feature shapeType '{shapeType}' does not match expected {normalizedExpectedAdmLevel}; ignored as source level mismatch.",
            DurationMs = durationMs
        };

        return true;
    }

    private void LogBoundaryFeatureFailure(
        LogLevel logLevel,
        Exception exception,
        string operation,
        string expectedAdmLevel,
        string path,
        BoundaryFeatureMetadata metadata,
        JsonElement featureElement)
    {
        _logger.Log(
            logLevel,
            exception,
            "{Operation}. Source={Source}; expectedAdmLevel={ExpectedAdmLevel}; iso3={Iso3}; shapeGroup={ShapeGroup}; shapeName={ShapeName}; shapeID={ShapeId}; shapeType={ShapeType}; geometryType={GeometryType}; reason={Reason}",
            operation,
            Path.GetFileName(path),
            expectedAdmLevel,
            metadata.Iso3 ?? "(unknown)",
            metadata.ShapeGroup ?? "(unknown)",
            metadata.Name ?? "(unknown)",
            metadata.ShapeId ?? "(none)",
            metadata.RawShapeType ?? "(missing)",
            GetFeatureGeometryType(featureElement) ?? "(unknown)",
            exception.Message);
    }

    private void LogBoundaryFeatureLevelMismatchFiltered(
        string expectedAdmLevel,
        string path,
        BoundaryFeatureMetadata metadata,
        JsonElement featureElement)
    {
        _logger.LogDebug(
            "Local {AdmLevel} boundary import ignored source feature because shapeType does not match expected level. Source={Source}; expectedAdmLevel={ExpectedAdmLevel}; iso3={Iso3}; shapeGroup={ShapeGroup}; shapeName={ShapeName}; shapeID={ShapeId}; shapeType={ShapeType}; geometryType={GeometryType}",
            expectedAdmLevel,
            Path.GetFileName(path),
            expectedAdmLevel,
            metadata.Iso3 ?? "(unknown)",
            metadata.ShapeGroup ?? "(unknown)",
            metadata.Name ?? "(unknown)",
            metadata.ShapeId ?? "(none)",
            metadata.RawShapeType ?? "(missing)",
            GetFeatureGeometryType(featureElement) ?? "(unknown)");
    }

    private static string? GetFeatureGeometryType(JsonElement featureElement)
    {
        if (!TryGetProperty(featureElement, "geometry", out var geometryElement) ||
            geometryElement.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var geometryType = FirstNonEmpty(GetString(geometryElement, "type"));
        return string.IsNullOrWhiteSpace(geometryType) ? null : geometryType;
    }

    private void RecordSummaryItem(
        BoundaryImportSummaryDto summary,
        BoundaryImportItemDto item,
        int maxSummaryItems)
    {
        summary.ProcessedCount++;

        switch (item.Status)
        {
            case "Imported":
            case "ImportedValidationOnly":
                summary.ImportedCount++;
                IncrementImportedKind(summary, item.Kind);
                break;
            case "Updated":
            case "UpdatedValidationOnly":
                summary.UpdatedCount++;
                IncrementUpdatedKind(summary, item.Kind);
                break;
            case "Failed":
                summary.FailedCount++;
                IncrementFailedKind(summary, item.Kind);
                break;
            default:
                summary.SkippedCount++;
                IncrementSkippedKind(summary, item.Kind);
                break;
        }

        if (string.Equals(item.Status, "Failed", StringComparison.Ordinal))
        {
            RecordFailedSummaryItem(summary, item, maxSummaryItems);
        }

        if (maxSummaryItems <= 0)
        {
            summary.ItemsTruncated = true;
            return;
        }

        if (summary.Items.Count < maxSummaryItems)
        {
            summary.Items.Add(item);
            return;
        }

        summary.ItemsTruncated = true;
        if (string.Equals(item.Status, "Failed", StringComparison.Ordinal))
        {
            var replaceIndex = summary.Items.FindLastIndex(existing =>
                !string.Equals(existing.Status, "Failed", StringComparison.Ordinal));
            if (replaceIndex >= 0)
            {
                summary.Items[replaceIndex] = item;
            }
        }
    }

    private static void RecordFilteredSourceItem(
        BoundaryImportSummaryDto summary,
        BoundaryImportItemDto item,
        int maxSummaryItems)
    {
        summary.FilteredOutCount++;
        if (string.Equals(item.Status, "IgnoredLevelMismatch", StringComparison.Ordinal))
        {
            summary.IgnoredLevelMismatchCount++;
        }

        if (maxSummaryItems <= 0)
        {
            summary.ItemsTruncated = true;
            return;
        }

        if (summary.Items.Count < maxSummaryItems)
        {
            summary.Items.Add(item);
            return;
        }

        summary.ItemsTruncated = true;
    }

    private static void RecordFailedSummaryItem(
        BoundaryImportSummaryDto summary,
        BoundaryImportItemDto item,
        int maxSummaryItems)
    {
        if (maxSummaryItems <= 0)
            return;

        if (summary.FailedItems.Count < maxSummaryItems)
        {
            summary.FailedItems.Add(item);
            return;
        }

        summary.ItemsTruncated = true;
    }

    private static void IncrementImportedKind(BoundaryImportSummaryDto summary, string? kind)
    {
        if (IsBoundaryCandidateSummary(summary))
            summary.ImportedCandidatesCount++;
        else if (IsDisputedKind(kind))
            summary.ImportedDisputedCount++;
        else
            summary.ImportedCountriesCount++;
    }

    private static void IncrementUpdatedKind(BoundaryImportSummaryDto summary, string? kind)
    {
        if (IsBoundaryCandidateSummary(summary))
            summary.UpdatedCandidatesCount++;
        else if (IsDisputedKind(kind))
            summary.UpdatedDisputedCount++;
        else
            summary.UpdatedCountriesCount++;
    }

    private static void IncrementSkippedKind(BoundaryImportSummaryDto summary, string? kind)
    {
        if (IsBoundaryCandidateSummary(summary))
            summary.SkippedCandidatesCount++;
        else if (IsDisputedKind(kind))
            summary.SkippedDisputedCount++;
        else
            summary.SkippedCountriesCount++;
    }

    private static void IncrementFailedKind(BoundaryImportSummaryDto summary, string? kind)
    {
        if (IsBoundaryCandidateSummary(summary))
            summary.FailedCandidatesCount++;
        else if (IsDisputedKind(kind))
            summary.FailedDisputedCount++;
        else
            summary.FailedCountriesCount++;
    }

    private static bool IsBoundaryCandidateSummary(BoundaryImportSummaryDto summary) =>
        string.Equals(summary.AdmLevel, "ADM1", StringComparison.Ordinal) ||
        string.Equals(summary.AdmLevel, "ADM2", StringComparison.Ordinal);

    private static bool IsDisputedKind(string? kind) =>
        string.Equals(kind, TerritoryKinds.DisputedTerritory, StringComparison.Ordinal);

    private int GetMaxSummaryItems() =>
        _options.MaxSummaryItems <= 0 ? DefaultMaxSummaryItems : _options.MaxSummaryItems;

    private int GetSaveBatchSize() =>
        Math.Clamp(
            _options.SaveBatchSize <= 0 ? DefaultSaveBatchSize : _options.SaveBatchSize,
            1,
            100);

    private int GetProgressLogInterval() =>
        _options.ProgressLogInterval <= 0
            ? DefaultProgressLogInterval
            : _options.ProgressLogInterval;

    private static string? NormalizeOptionalCountryFilter(string? countryIso3)
    {
        if (string.IsNullOrWhiteSpace(countryIso3))
            return null;

        return CountryRegistry.NormalizeIso3ForBoundaryImport(countryIso3)
            ?? throw new InvalidOperationException("countryIso3 must be a valid ISO3 country code.");
    }

    private static int? NormalizeTakeLimit(int? take)
    {
        if (!take.HasValue)
            return null;

        if (take.Value <= 0)
            throw new InvalidOperationException("take must be greater than zero.");

        return take.Value;
    }

    private static bool BoundaryFeatureMatchesCountryFilter(
        BoundaryFeatureMetadata metadata,
        string? countryIso3)
    {
        if (string.IsNullOrWhiteSpace(countryIso3))
            return true;

        var shapeGroup = CountryRegistry.NormalizeIso3ForBoundaryImport(metadata.ShapeGroup);
        var iso3 = CountryRegistry.NormalizeIso3ForBoundaryImport(metadata.Iso3);
        return string.Equals(shapeGroup, countryIso3, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(iso3, countryIso3, StringComparison.OrdinalIgnoreCase);
    }

    private static bool ReachedTakeLimit(BoundaryImportSummaryDto summary, int takeLimit) =>
        summary.ImportedCount + summary.UpdatedCount + summary.FailedCount >= takeLimit;

    private static bool IsExpectedSourceLevelMismatch(
        BoundaryFeatureMetadata metadata,
        string expectedAdmLevel)
    {
        if (metadata.NormalizedShapeType == expectedAdmLevel)
            return false;

        if (string.IsNullOrWhiteSpace(metadata.RawShapeType))
            return false;

        if (IsDisputedShapeType(metadata))
            return true;

        var normalizedShapeType = NormalizeAdmLevel(metadata.RawShapeType);
        return normalizedShapeType is "ADM0" or "ADM1" or "ADM2" &&
            normalizedShapeType != expectedAdmLevel;
    }

    private static string NormalizeAdmLevel(string? admLevel)
    {
        if (string.IsNullOrWhiteSpace(admLevel))
            return string.Empty;

        var normalized = CompactAdmLevelValue(admLevel);
        normalized = normalized
            .Replace("ADMINISTRATIVELEVEL", "ADM")
            .Replace("ADMINLEVEL", "ADM")
            .Replace("ADMLEVEL", "ADM")
            .Replace("ADMIN", "ADM");

        return normalized switch
        {
            "ADM0" => "ADM0",
            "ADM1" => "ADM1",
            "ADM2" => "ADM2",
            _ => string.Empty
        };
    }

    private static string CompactAdmLevelValue(string admLevel) =>
        admLevel
            .Trim()
            .ToUpperInvariant()
            .Replace(" ", string.Empty)
            .Replace("-", string.Empty)
            .Replace("_", string.Empty);

    private static string NormalizeRegionAdmLevel(string? admLevel)
    {
        var normalized = NormalizeAdmLevel(admLevel);
        return normalized is "ADM1" or "ADM2"
            ? normalized
            : throw new InvalidOperationException("Region boundary candidate ADM level must be ADM1 or ADM2.");
    }

    private static bool TryGetProperty(JsonElement element, string propertyName, out JsonElement value)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (property.NameEquals(propertyName) ||
                property.Name.Equals(propertyName, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    private static string? GetString(JsonElement element, string propertyName) =>
        TryGetProperty(element, propertyName, out var value) ? GetString(value) : null;

    private static string? GetString(JsonElement element) =>
        element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Null or JsonValueKind.Undefined => null,
            _ => element.ToString()
        };

    private static string FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim()
        ?? string.Empty;

    private static string? FirstNonEmptyRaw(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

    private static string BuildShapeCandidateKey(string? shapeGroup, string? shapeId, string? admLevel) =>
        $"{shapeGroup?.Trim().ToUpperInvariant()}::{shapeId?.Trim().ToUpperInvariant()}::{admLevel?.Trim().ToUpperInvariant()}";

    private static void SetSrid(Geometry geometry)
    {
        geometry.SRID = 4326;
        if (geometry is GeometryCollection collection)
        {
            for (var index = 0; index < collection.NumGeometries; index++)
            {
                SetSrid(collection.GetGeometryN(index));
            }
        }
    }

    private static bool CoordinatesEqual(Coordinate first, Coordinate second) =>
        Math.Abs(first.X - second.X) < 0.0000001 &&
        Math.Abs(first.Y - second.Y) < 0.0000001;

    private sealed record BoundaryFeature(
        string Kind,
        string? ShapeGroup,
        string? Iso3,
        string? Iso2,
        string Name,
        string? ShapeId,
        string? ShapeType,
        string AdmLevel,
        Geometry Geometry);

    private sealed record BoundaryFeatureMetadata(
        string? ShapeGroup,
        string? Iso3,
        string? Name,
        string? ShapeId,
        string? RawShapeType,
        string NormalizedShapeType)
    {
        public static BoundaryFeatureMetadata Empty { get; } =
            new(null, null, null, null, null, string.Empty);
    }

    private sealed record BoundaryImportFeatureResult(
        BoundaryImportItemDto Item,
        bool Changed,
        bool InvalidatesMapCache);

    private sealed class BoundaryImportBatchState
    {
        public int PendingChanges { get; set; }
        public bool InvalidatesMapCache { get; set; }
    }

    private sealed class TerritoryImportRow
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string TerritoryKind { get; set; } = TerritoryKinds.Country;
        public string? CountryCode { get; set; }
        public string? CountryIso3 { get; set; }
        public string? BoundaryShapeType { get; set; }
        public string? BoundaryShapeGroup { get; set; }
        public string? BoundaryShapeId { get; set; }
        public string? BoundaryExternalKey { get; set; }
        public string? BoundaryAdmLevel { get; set; }
        public DateTime CreatedAt { get; set; }
        public int RegionCount { get; set; }
        public int TerritoryManagerAssignmentCount { get; set; }
        public bool HasValidBoundary { get; set; }
        public bool HasValidDisplayBoundary { get; set; }
    }

    private sealed class BoundaryCandidateImportRow
    {
        public int Id { get; set; }
        public string Source { get; set; } = string.Empty;
        public string ReleaseType { get; set; } = string.Empty;
        public string AdmLevel { get; set; } = string.Empty;
        public string ShapeGroup { get; set; } = string.Empty;
        public string ShapeId { get; set; } = string.Empty;
        public string ShapeName { get; set; } = string.Empty;
        public string? ShapeType { get; set; }
        public string ExternalKey { get; set; } = string.Empty;
        public string CountryIso3 { get; set; } = string.Empty;
        public int? CountryTerritoryId { get; set; }
        public DateTime CreatedAt { get; set; }
        public bool HasValidBoundary { get; set; }
        public bool HasValidDisplayBoundary { get; set; }
    }

    private sealed class BoundaryRegionCandidateDbRow
    {
        public int Id { get; set; }
        public string Source { get; set; } = string.Empty;
        public string ReleaseType { get; set; } = string.Empty;
        public string AdmLevel { get; set; } = string.Empty;
        public string ShapeGroup { get; set; } = string.Empty;
        public string ShapeId { get; set; } = string.Empty;
        public string ShapeName { get; set; } = string.Empty;
        public string? ShapeType { get; set; }
        public string ExternalKey { get; set; } = string.Empty;
        public string CountryIso3 { get; set; } = string.Empty;
        public int? CountryTerritoryId { get; set; }
        public Geometry? BoundaryGeometry { get; set; }
        public Geometry? BoundaryDisplayGeometry { get; set; }
    }

    private sealed class BoundaryExistingRegionRow
    {
        public int Id { get; set; }
        public int? BoundaryCandidateId { get; set; }
        public string? BoundaryExternalKey { get; set; }
        public string? BoundaryShapeGroup { get; set; }
        public string? BoundaryShapeId { get; set; }
        public string? BoundaryAdmLevel { get; set; }
    }

    internal sealed record BoundaryRegionCandidateGeometry(
        BoundaryRegionCandidateDto Candidate,
        Geometry Geometry);

    private sealed class BoundaryDbRow
    {
        public int TerritoryId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string TerritoryKind { get; set; } = TerritoryKinds.Country;
        public string? CountryCode { get; set; }
        public string? CountryIso3 { get; set; }
        public string? BoundaryShapeType { get; set; }
        public string? BoundaryShapeGroup { get; set; }
        public string? BoundaryShapeId { get; set; }
        public string? BoundaryExternalKey { get; set; }
        public string? BoundaryAdmLevel { get; set; }
        public Geometry? BoundaryGeometry { get; set; }
        public Geometry? BoundaryDisplayGeometry { get; set; }
    }

    private sealed record TerritoryResolution(TerritoryImportRow? Territory, string? ConflictMessage);
}
