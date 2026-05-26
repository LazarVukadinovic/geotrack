using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Geotrack.Application.DTOs.Admin;
using Geotrack.Application.Interfaces.Service;
using Geotrack.Application.Options;
using Geotrack.Domain.Entities.Core;
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
    private const int DefaultMaxSummaryItems = 500;
    private const int MaxCandidateTake = 100;

    private static readonly GeometryFactory Wgs84GeometryFactory = new(new PrecisionModel(), 4326);

    private readonly AppDbContext _db;
    private readonly BoundaryImportOptions _options;
    private readonly IHostEnvironment _environment;
    private readonly ILogger<LocalBoundaryImportService> _logger;
    private readonly IMemoryCache? _cache;

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
        CancellationToken cancellationToken = default)
    {
        var normalizedLevel = NormalizeAdmLevel(admLevel);
        return normalizedLevel switch
        {
            "ADM0" => await ImportAdm0Async(forceRefresh, cancellationToken),
            "ADM1" or "ADM2" => await ScanCandidateSourceAsync(normalizedLevel, cancellationToken),
            _ => throw new InvalidOperationException("Boundary ADM level must be ADM0, ADM1, or ADM2.")
        };
    }

    public async Task<BoundaryRegionCandidateResponseDto> GetRegionCandidatesAsync(
        string admLevel,
        string iso3,
        Geometry assignedTerritoryBoundary,
        string? search = null,
        int skip = 0,
        int take = 50,
        CancellationToken cancellationToken = default)
    {
        var normalizedLevel = NormalizeRegionAdmLevel(admLevel);
        var normalizedIso3 = CountryRegistry.NormalizeIso3ForBoundaryImport(iso3)
            ?? throw new InvalidOperationException("ISO3 country code is required.");
        var territoryBoundary = ToValidPolygonalGeometry(assignedTerritoryBoundary)
            ?? throw new InvalidOperationException("Assigned territory boundary geometry is invalid.");
        var normalizedSkip = Math.Max(0, skip);
        var normalizedTake = Math.Clamp(take <= 0 ? 50 : take, 1, MaxCandidateTake);
        var normalizedSearch = search?.Trim();
        var path = ResolveBoundaryPath(normalizedLevel);

        var response = new BoundaryRegionCandidateResponseDto
        {
            AdmLevel = normalizedLevel,
            Iso3 = normalizedIso3
        };

        await ScanFeatureElementsAsync(
            path,
            async (featureElement, _, token) =>
            {
                token.ThrowIfCancellationRequested();
                response.ScannedFeatures++;

                var metadata = TryReadBoundaryFeatureMetadata(featureElement);
                BoundaryFeature feature;
                try
                {
                    feature = ParseBoundaryFeature(featureElement, normalizedLevel);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    LogBoundaryFeatureFailure(
                        LogLevel.Debug,
                        ex,
                        "Skipping invalid local boundary candidate",
                        normalizedLevel,
                        path,
                        metadata,
                        featureElement);
                    return true;
                }

                if (!BoundaryImportIsoCodesMatch(feature.Iso3, normalizedIso3))
                    return true;

                if (!string.IsNullOrWhiteSpace(normalizedSearch) &&
                    !feature.Name.Contains(normalizedSearch, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                response.MatchedFeatures++;
                if (response.MatchedFeatures <= normalizedSkip)
                    return true;

                var isInside = territoryBoundary.Covers(feature.Geometry);
                if (!isInside)
                    return true;

                response.Items.Add(new BoundaryRegionCandidateDto
                {
                    AdmLevel = normalizedLevel,
                    Iso3 = feature.Iso3!,
                    ShapeId = feature.ShapeId,
                    Name = feature.Name,
                    IsInsideAssignedTerritory = true,
                    GeometryGeoJson = ToRegionGeoJson(feature.Geometry)
                });

                return response.Items.Count < normalizedTake;
            },
            cancellationToken);

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
        var normalizedLevel = NormalizeRegionAdmLevel(admLevel);
        var normalizedIso3 = CountryRegistry.NormalizeIso3ForBoundaryImport(iso3)
            ?? throw new InvalidOperationException("ISO3 country code is required.");
        var normalizedShapeId = shapeId?.Trim();
        if (string.IsNullOrWhiteSpace(normalizedShapeId))
            throw new InvalidOperationException("Boundary candidate shape id is required.");
        var territoryBoundary = ToValidPolygonalGeometry(assignedTerritoryBoundary)
            ?? throw new InvalidOperationException("Assigned territory boundary geometry is invalid.");
        var path = ResolveBoundaryPath(normalizedLevel);
        BoundaryRegionCandidateDto? candidate = null;

        await ScanFeatureElementsAsync(
            path,
            (featureElement, _, token) =>
            {
                token.ThrowIfCancellationRequested();

                var metadata = TryReadBoundaryFeatureMetadata(featureElement);
                BoundaryFeature feature;
                try
                {
                    feature = ParseBoundaryFeature(featureElement, normalizedLevel);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    LogBoundaryFeatureFailure(
                        LogLevel.Debug,
                        ex,
                        "Skipping invalid local boundary candidate lookup item",
                        normalizedLevel,
                        path,
                        metadata,
                        featureElement);
                    return ValueTask.FromResult(true);
                }

                if (!BoundaryImportIsoCodesMatch(feature.Iso3, normalizedIso3) ||
                    !string.Equals(feature.ShapeId, normalizedShapeId, StringComparison.OrdinalIgnoreCase) ||
                    !territoryBoundary.Covers(feature.Geometry))
                {
                    return ValueTask.FromResult(true);
                }

                candidate = new BoundaryRegionCandidateDto
                {
                    AdmLevel = normalizedLevel,
                    Iso3 = feature.Iso3!,
                    ShapeId = feature.ShapeId,
                    Name = feature.Name,
                    IsInsideAssignedTerritory = true,
                    GeometryGeoJson = ToRegionGeoJson(feature.Geometry)
                };

                return ValueTask.FromResult(false);
            },
            cancellationToken);

        return candidate;
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

        await ScanFeatureElementsAsync(
            path,
            async (featureElement, _, token) =>
            {
                token.ThrowIfCancellationRequested();
                summary.TotalFeatures++;

                var stopwatch = Stopwatch.StartNew();
                var metadata = TryReadBoundaryFeatureMetadata(featureElement);

                try
                {
                    var feature = ParseBoundaryFeature(featureElement, "ADM0");

                    var item = await ImportAdm0FeatureAsync(
                        feature,
                        sourceReference,
                        forceRefresh,
                        stopwatch,
                        token);
                    RecordSummaryItem(summary, item, maxSummaryItems);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _db.ChangeTracker.Clear();
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

                return true;
            },
            cancellationToken);

        summary.CompletedAt = DateTime.UtcNow;
        return summary;
    }

    private async Task<BoundaryImportSummaryDto> ScanCandidateSourceAsync(
        string admLevel,
        CancellationToken cancellationToken)
    {
        var path = ResolveBoundaryPath(admLevel);
        var summary = CreateSummary(admLevel, BuildSourceReference(path));
        var maxSummaryItems = GetMaxSummaryItems();

        await ScanFeatureElementsAsync(
            path,
            (featureElement, _, token) =>
            {
                token.ThrowIfCancellationRequested();
                summary.TotalFeatures++;

                var stopwatch = Stopwatch.StartNew();
                var metadata = TryReadBoundaryFeatureMetadata(featureElement);

                try
                {
                    var feature = ParseBoundaryFeature(featureElement, admLevel);
                    RecordSummaryItem(
                        summary,
                        new BoundaryImportItemDto
                        {
                            Kind = feature.Kind,
                            Iso3 = feature.Iso3,
                            ShapeGroup = feature.ShapeGroup,
                            Name = feature.Name,
                            ShapeId = feature.ShapeId,
                            ShapeType = feature.ShapeType,
                            Status = "Skipped",
                            Message = $"{admLevel} local candidate source parsed. Candidate regions are selected by Territory Manager workflows before creating Regions.",
                            DurationMs = stopwatch.ElapsedMilliseconds
                        },
                        maxSummaryItems);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    RecordSummaryItem(
                        summary,
                        CreateFailedImportItem(metadata, ex.Message, stopwatch.ElapsedMilliseconds),
                        maxSummaryItems);
                    LogBoundaryFeatureFailure(
                        LogLevel.Debug,
                        ex,
                        $"Local {admLevel} candidate source item failed validation",
                        admLevel,
                        path,
                        metadata,
                        featureElement);
                }

                return ValueTask.FromResult(true);
            },
            cancellationToken);

        summary.CompletedAt = DateTime.UtcNow;
        return summary;
    }

    private async Task<BoundaryImportItemDto> ImportAdm0FeatureAsync(
        BoundaryFeature feature,
        string sourceReference,
        bool forceRefresh,
        Stopwatch stopwatch,
        CancellationToken cancellationToken)
    {
        if (IsDisputedFeature(feature))
        {
            return await ImportDisputedFeatureAsync(
                feature,
                sourceReference,
                forceRefresh,
                stopwatch,
                cancellationToken);
        }

        var territoryResolution = await ResolveTerritoryAsync(feature, cancellationToken);
        if (territoryResolution.ConflictMessage is not null)
        {
            _db.ChangeTracker.Clear();
            return CreateSkippedImportItem(feature, territoryResolution.ConflictMessage, stopwatch.ElapsedMilliseconds);
        }

        var territory = territoryResolution.Territory;

        if (territory is not null)
        {
            EnsureTerritoryMatchesFeature(territory, feature);
            var existingGeometry = await _db.Territories
                .Where(item => item.Id == territory.Id)
                .Select(item => new
                {
                    BoundaryGeometry = EF.Property<Geometry?>(item, BoundaryGeometryPropertyName),
                    BoundaryDisplayGeometry = EF.Property<Geometry?>(item, BoundaryDisplayGeometryPropertyName)
                })
                .SingleAsync(cancellationToken);

            if (!forceRefresh &&
                IsValidBoundary(existingGeometry.BoundaryGeometry) &&
                TerritoryBoundaryDisplayGeometry.IsValid(
                    existingGeometry.BoundaryGeometry,
                    existingGeometry.BoundaryDisplayGeometry))
            {
                _db.ChangeTracker.Clear();
                return new BoundaryImportItemDto
                {
                    Kind = TerritoryKinds.Country,
                    Iso3 = feature.Iso3,
                    ShapeGroup = feature.ShapeGroup,
                    Name = feature.Name,
                    ShapeId = feature.ShapeId,
                    ShapeType = feature.ShapeType,
                    Status = "Skipped",
                    Message = "Existing ADM0 boundary geometry is already valid.",
                    DurationMs = stopwatch.ElapsedMilliseconds
                };
            }
        }

        var now = DateTime.UtcNow;
        var isNewTerritory = territory is null;

        if (territory is null)
        {
            territory = new Territory
            {
                Name = feature.Name,
                TerritoryKind = TerritoryKinds.Country,
                CreatedAt = now
            };
            _db.Territories.Add(territory);
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

        var displayGeometry = TerritoryBoundaryDisplayGeometry.Create(feature.Geometry);

        _db.Entry(territory)
            .Property<Geometry?>(BoundaryGeometryPropertyName)
            .CurrentValue = feature.Geometry;
        _db.Entry(territory)
            .Property<Geometry?>(BoundaryDisplayGeometryPropertyName)
            .CurrentValue = displayGeometry;

        await _db.SaveChangesAsync(cancellationToken);
        _db.ChangeTracker.Clear();
        TerritoryMapBoundaryCache.Invalidate(_cache);

        var status = ResolveImportStatus(isNewTerritory, displayGeometry is not null);
        return new BoundaryImportItemDto
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
        };
    }

    private async Task<BoundaryImportItemDto> ImportDisputedFeatureAsync(
        BoundaryFeature feature,
        string sourceReference,
        bool forceRefresh,
        Stopwatch stopwatch,
        CancellationToken cancellationToken)
    {
        var externalKey = BuildBoundaryExternalKey(feature);
        var territoryResolution = await ResolveDisputedTerritoryAsync(feature, externalKey, cancellationToken);
        if (territoryResolution.ConflictMessage is not null)
        {
            _db.ChangeTracker.Clear();
            return CreateSkippedImportItem(feature, territoryResolution.ConflictMessage, stopwatch.ElapsedMilliseconds);
        }

        var territory = territoryResolution.Territory;

        if (territory is not null)
        {
            var existingGeometry = await _db.Territories
                .Where(item => item.Id == territory.Id)
                .Select(item => new
                {
                    BoundaryGeometry = EF.Property<Geometry?>(item, BoundaryGeometryPropertyName),
                    BoundaryDisplayGeometry = EF.Property<Geometry?>(item, BoundaryDisplayGeometryPropertyName)
                })
                .SingleAsync(cancellationToken);

            if (!forceRefresh &&
                IsValidBoundary(existingGeometry.BoundaryGeometry) &&
                TerritoryBoundaryDisplayGeometry.IsValid(
                    existingGeometry.BoundaryGeometry,
                    existingGeometry.BoundaryDisplayGeometry))
            {
                _db.ChangeTracker.Clear();
                return new BoundaryImportItemDto
                {
                    Kind = TerritoryKinds.DisputedTerritory,
                    Iso3 = null,
                    ShapeGroup = feature.ShapeGroup,
                    Name = feature.Name,
                    ShapeId = feature.ShapeId,
                    ShapeType = feature.ShapeType,
                    Status = "Skipped",
                    Message = "Existing disputed boundary geometry is already valid.",
                    DurationMs = stopwatch.ElapsedMilliseconds
                };
            }
        }

        var now = DateTime.UtcNow;
        var isNewTerritory = territory is null;

        if (territory is null)
        {
            territory = new Territory
            {
                Name = feature.Name,
                TerritoryKind = TerritoryKinds.DisputedTerritory,
                CreatedAt = now
            };
            _db.Territories.Add(territory);
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

        var displayGeometry = TerritoryBoundaryDisplayGeometry.Create(feature.Geometry);

        _db.Entry(territory)
            .Property<Geometry?>(BoundaryGeometryPropertyName)
            .CurrentValue = feature.Geometry;
        _db.Entry(territory)
            .Property<Geometry?>(BoundaryDisplayGeometryPropertyName)
            .CurrentValue = displayGeometry;

        await _db.SaveChangesAsync(cancellationToken);
        _db.ChangeTracker.Clear();
        TerritoryMapBoundaryCache.Invalidate(_cache);

        var status = ResolveImportStatus(isNewTerritory, displayGeometry is not null);
        return new BoundaryImportItemDto
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
        };
    }

    private async Task<TerritoryResolution> ResolveTerritoryAsync(
        BoundaryFeature feature,
        CancellationToken cancellationToken)
    {
        var normalizedName = feature.Name.ToLowerInvariant();

        var territory = !string.IsNullOrWhiteSpace(feature.Iso3)
            ? await _db.Territories
                .OrderBy(item => item.Id)
                .FirstOrDefaultAsync(
                    item =>
                        item.TerritoryKind == TerritoryKinds.Country &&
                        item.CountryIso3 == feature.Iso3,
                    cancellationToken)
            : null;

        if (territory is not null)
            return new TerritoryResolution(territory, null);

        var aliasCandidates = CountryRegistry.BoundaryImportIsoAliases(feature.Iso3);
        if (aliasCandidates.Count > 1)
        {
            var candidates = await _db.Territories
                .OrderBy(item => item.Id)
                .Where(item =>
                    item.TerritoryKind == TerritoryKinds.Country &&
                    ((item.CountryIso3 != null && aliasCandidates.Contains(item.CountryIso3)) ||
                     item.CountryCode == "XK" ||
                     item.Name.ToLower().Contains("kosov")))
                .ToListAsync(cancellationToken);

            territory = candidates.FirstOrDefault(item => IsKnownKosovoTerritoryFeatureMatch(item, feature));
            if (territory is not null)
                return new TerritoryResolution(territory, null);
        }

        if (!string.IsNullOrWhiteSpace(feature.Iso2))
        {
            territory = await _db.Territories
                .OrderBy(item => item.Id)
                .FirstOrDefaultAsync(
                    item =>
                        item.TerritoryKind == TerritoryKinds.Country &&
                        item.CountryCode == feature.Iso2,
                    cancellationToken);

            if (territory is not null)
                return new TerritoryResolution(territory, null);
        }

        territory = await _db.Territories
            .OrderBy(item => item.Id)
            .FirstOrDefaultAsync(
                item =>
                    item.TerritoryKind == TerritoryKinds.Country &&
                    item.Name.ToLower() == normalizedName,
                cancellationToken);

        if (territory is not null)
            return new TerritoryResolution(territory, null);

        var sameName = await _db.Territories
            .Include(item => item.Regions)
            .Include(item => item.TerritoryManagerTerritories)
            .OrderBy(item => item.Id)
            .FirstOrDefaultAsync(item => item.Name.ToLower() == normalizedName, cancellationToken);

        if (sameName is null)
            return new TerritoryResolution(null, null);

        if (IsSafeNameFallback(sameName))
            return new TerritoryResolution(sameName, null);

        return new TerritoryResolution(
            null,
            $"Skipped because an existing {sameName.TerritoryKind} territory already uses the name '{sameName.Name}'.");
    }

    private async Task<TerritoryResolution> ResolveDisputedTerritoryAsync(
        BoundaryFeature feature,
        string externalKey,
        CancellationToken cancellationToken)
    {
        var normalizedName = feature.Name.ToLowerInvariant();
        var shapeGroup = feature.ShapeGroup;
        var shapeType = NormalizeDisputedShapeType(feature.ShapeType);

        var territory = await _db.Territories
            .OrderBy(item => item.Id)
            .FirstOrDefaultAsync(
                item =>
                    item.TerritoryKind == TerritoryKinds.DisputedTerritory &&
                    item.BoundaryExternalKey == externalKey,
                cancellationToken);

        if (territory is not null)
            return new TerritoryResolution(territory, null);

        if (!string.IsNullOrWhiteSpace(shapeGroup))
        {
            territory = await _db.Territories
                .OrderBy(item => item.Id)
                .FirstOrDefaultAsync(
                    item =>
                        item.TerritoryKind == TerritoryKinds.DisputedTerritory &&
                        item.BoundaryShapeGroup == shapeGroup &&
                        (item.BoundaryShapeType == shapeType || item.BoundaryAdmLevel == DisputedAdmLevel),
                    cancellationToken);

            if (territory is not null)
                return new TerritoryResolution(territory, null);
        }

        territory = await _db.Territories
            .OrderBy(item => item.Id)
            .FirstOrDefaultAsync(
                item =>
                    item.TerritoryKind == TerritoryKinds.DisputedTerritory &&
                    item.Name.ToLower() == normalizedName,
                cancellationToken);

        if (territory is not null)
            return new TerritoryResolution(territory, null);

        var sameName = await _db.Territories
            .Include(item => item.Regions)
            .Include(item => item.TerritoryManagerTerritories)
            .OrderBy(item => item.Id)
            .FirstOrDefaultAsync(item => item.Name.ToLower() == normalizedName, cancellationToken);

        if (sameName is null)
            return new TerritoryResolution(null, null);

        if (IsSafeNameFallback(sameName))
            return new TerritoryResolution(sameName, null);

        return new TerritoryResolution(
            null,
            $"Skipped because an existing {sameName.TerritoryKind} territory already uses the name '{sameName.Name}'.");
    }

    private static void EnsureTerritoryMatchesFeature(Territory territory, BoundaryFeature feature)
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

    private static bool IsSafeNameFallback(Territory territory) =>
        territory.Regions.Count == 0 &&
        territory.TerritoryManagerTerritories.Count == 0;

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
        geometry = ToValidPolygonalGeometry(geometry)
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

    private static Geometry? ToValidPolygonalGeometry(Geometry? geometry)
    {
        if (geometry is null || geometry.IsEmpty)
            return null;

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

    private BoundaryImportSummaryDto CreateSummary(string admLevel, string sourcePath) =>
        new()
        {
            Source = GeoBoundariesSource,
            AdmLevel = admLevel,
            SourcePath = sourcePath,
            StartedAt = DateTime.UtcNow
        };

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

        if (summary.Items.Count < maxSummaryItems)
        {
            summary.Items.Add(item);
        }
    }

    private static void IncrementImportedKind(BoundaryImportSummaryDto summary, string? kind)
    {
        if (IsDisputedKind(kind))
            summary.ImportedDisputedCount++;
        else
            summary.ImportedCountriesCount++;
    }

    private static void IncrementUpdatedKind(BoundaryImportSummaryDto summary, string? kind)
    {
        if (IsDisputedKind(kind))
            summary.UpdatedDisputedCount++;
        else
            summary.UpdatedCountriesCount++;
    }

    private static void IncrementSkippedKind(BoundaryImportSummaryDto summary, string? kind)
    {
        if (IsDisputedKind(kind))
            summary.SkippedDisputedCount++;
        else
            summary.SkippedCountriesCount++;
    }

    private static void IncrementFailedKind(BoundaryImportSummaryDto summary, string? kind)
    {
        if (IsDisputedKind(kind))
            summary.FailedDisputedCount++;
        else
            summary.FailedCountriesCount++;
    }

    private static bool IsDisputedKind(string? kind) =>
        string.Equals(kind, TerritoryKinds.DisputedTerritory, StringComparison.Ordinal);

    private int GetMaxSummaryItems() =>
        _options.MaxSummaryItems <= 0 ? DefaultMaxSummaryItems : _options.MaxSummaryItems;

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

    private sealed record TerritoryResolution(Territory? Territory, string? ConflictMessage);
}
