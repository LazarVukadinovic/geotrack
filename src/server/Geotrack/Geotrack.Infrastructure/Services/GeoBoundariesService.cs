using System.Text.Json;
using Geotrack.Application.DTOs.Admin;
using Geotrack.Application.Interfaces.Service;
using Geotrack.Domain.Entities.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using NetTopologySuite.Geometries;

namespace Geotrack.Infrastructure.Services;

public sealed class GeoBoundariesService : IGeoBoundariesService
{
    private const string BoundaryGeometryPropertyName = "BoundaryGeometry";
    private const string BoundaryDisplayGeometryPropertyName =
        TerritoryBoundaryDisplayGeometry.BoundaryDisplayGeometryPropertyName;
    private const string GeoBoundariesSource = "geoBoundaries";
    private const string ReleaseType = "gbOpen";
    private const string AdmLevel = "ADM0";
    private const string DefaultLicense = "CC BY 4.0";
    private const string MetadataBaseUrl = "https://www.geoboundaries.org/api/current/gbOpen";

    private static readonly GeometryFactory Wgs84GeometryFactory = new(new PrecisionModel(), 4326);

    private readonly AppDbContext _db;
    private readonly HttpClient _httpClient;
    private readonly ILogger<GeoBoundariesService> _logger;
    private readonly IMemoryCache? _cache;

    public GeoBoundariesService(
        AppDbContext db,
        HttpClient httpClient,
        ILogger<GeoBoundariesService> logger,
        IMemoryCache? cache = null)
    {
        _db = db;
        _httpClient = httpClient;
        _logger = logger;
        _cache = cache;
    }

    public async Task<GeoBoundariesImportResultDto> ImportAdm0BoundaryAsync(
        string iso3,
        int? territoryId = null,
        CancellationToken cancellationToken = default,
        bool forceRefresh = false)
    {
        var normalizedIso3 = CountryRegistry.NormalizeIso3(iso3)
            ?? throw new InvalidOperationException("ISO3 country code is required.");
        var registryCountry = CountryRegistry.FindByIso3(normalizedIso3);
        var iso2 = registryCountry?.Iso2;
        var fallbackName = registryCountry?.Name ?? normalizedIso3;
        var territory = await ResolveTerritoryAsync(territoryId, normalizedIso3, iso2, fallbackName, cancellationToken);

        if (territory is not null)
        {
            EnsureTerritoryMatchesImport(territory, normalizedIso3, iso2);
            var existingBoundary = await GetBoundaryGeometriesAsync(territory.Id, cancellationToken);
            if (!forceRefresh && IsValidAdm0Boundary(existingBoundary.BoundaryGeometry))
            {
                if (!TerritoryBoundaryDisplayGeometry.IsValid(
                    existingBoundary.BoundaryGeometry,
                    existingBoundary.BoundaryDisplayGeometry))
                {
                    var existingDisplayGeometry = TerritoryBoundaryDisplayGeometry.Create(existingBoundary.BoundaryGeometry)
                        ?? throw new InvalidOperationException("Boundary display geometry could not be generated.");
                    _db.Entry(territory)
                        .Property<Geometry?>(BoundaryDisplayGeometryPropertyName)
                        .CurrentValue = existingDisplayGeometry;
                    _db.Entry(territory)
                        .Property(BoundaryDisplayGeometryPropertyName)
                        .IsModified = true;
                    territory.UpdatedAt = DateTime.UtcNow;
                    await _db.SaveChangesAsync(cancellationToken);
                    TerritoryMapBoundaryCache.Invalidate(_cache);
                }

                return BuildResult(territory, existingBoundary.BoundaryGeometry!, skipped: true);
            }
        }

        var metadataUrl = $"{MetadataBaseUrl}/{normalizedIso3}/{AdmLevel}/";
        using var metadata = await GetJsonDocumentAsync(
            metadataUrl,
            $"geoBoundaries metadata could not be loaded for {normalizedIso3}.",
            cancellationToken);

        var downloadUrl = ResolveGeoJsonDownloadUrl(metadata.RootElement);
        using var geoJson = await GetJsonDocumentAsync(
            downloadUrl,
            $"geoBoundaries GeoJSON could not be downloaded for {normalizedIso3}.",
            cancellationToken);

        var geometry = ExtractBoundaryGeometry(geoJson.RootElement);
        EnsureValidBoundary(geometry);
        var displayGeometry = TerritoryBoundaryDisplayGeometry.Create(geometry)
            ?? throw new InvalidOperationException("Boundary display geometry could not be generated.");

        var name = FirstNonEmpty(
            GetString(metadata.RootElement, "boundaryName"),
            GetString(metadata.RootElement, "shapeName"),
            registryCountry?.Name,
            normalizedIso3);
        var license = FirstNonEmpty(
            GetString(metadata.RootElement, "boundaryLicense"),
            GetString(metadata.RootElement, "license"),
            DefaultLicense);
        license = NormalizeLicense(license);
        var now = DateTime.UtcNow;

        if (territory is null)
        {
            territory = new Territory
            {
                Name = name,
                CreatedAt = now
            };
            _db.Territories.Add(territory);
        }

        EnsureTerritoryMatchesImport(territory, normalizedIso3, iso2);

        if (string.IsNullOrWhiteSpace(territory.Name))
        {
            territory.Name = name;
        }

        territory.CountryCode = iso2 ?? territory.CountryCode;
        territory.CountryIso3 = normalizedIso3;
        territory.TerritoryKind = TerritoryKinds.Country;
        territory.BoundaryShapeType = geometry.GeometryType;
        territory.BoundaryShapeGroup = normalizedIso3;
        territory.BoundaryExternalKey = $"{GeoBoundariesSource}:{AdmLevel}:{normalizedIso3}";
        territory.BoundarySource = GeoBoundariesSource;
        territory.BoundarySourceUrl = downloadUrl;
        territory.BoundaryAdmLevel = AdmLevel;
        territory.BoundaryReleaseType = ReleaseType;
        territory.BoundaryLicense = license;
        territory.BoundaryImportedAt = now;
        territory.UpdatedAt = territory.Id == 0 ? null : now;

        _db.Entry(territory)
            .Property<Geometry?>(BoundaryGeometryPropertyName)
            .CurrentValue = geometry;
        _db.Entry(territory)
            .Property<Geometry?>(BoundaryDisplayGeometryPropertyName)
            .CurrentValue = displayGeometry;

        await _db.SaveChangesAsync(cancellationToken);
        TerritoryMapBoundaryCache.Invalidate(_cache);

        _logger.LogInformation(
            "Imported geoBoundaries {ReleaseType} {AdmLevel} boundary for {Iso3} into territory {TerritoryId}.",
            ReleaseType,
            AdmLevel,
            normalizedIso3,
            territory.Id);

        return BuildResult(territory, geometry, skipped: false);
    }

    public async Task<GeoBoundariesBatchImportResultDto> ImportMissingAdm0BoundariesAsync(
        CancellationToken cancellationToken = default,
        bool forceRefresh = false)
    {
        await EnsureCountryTerritoryRecordsAsync(cancellationToken);
        var rows = await LoadTerritoriesWithIso3Async(cancellationToken);
        var missingRows = new List<TerritoryImportRow>();

        foreach (var row in rows)
        {
            if (forceRefresh ||
                !IsValidAdm0Boundary(row.BoundaryGeometry) ||
                !TerritoryBoundaryDisplayGeometry.IsValid(row.BoundaryDisplayGeometry))
            {
                missingRows.Add(row);
            }
        }

        return await ImportRowsAsync(missingRows, cancellationToken, forceRefresh);
    }

    public async Task<GeoBoundariesBatchImportResultDto> ImportAllAdm0BoundariesAsync(
        CancellationToken cancellationToken = default,
        bool forceRefresh = false)
    {
        await EnsureCountryTerritoryRecordsAsync(cancellationToken);
        var rows = await LoadTerritoriesWithIso3Async(cancellationToken);
        return await ImportRowsAsync(rows, cancellationToken, forceRefresh);
    }

    private async Task<GeoBoundariesBatchImportResultDto> ImportRowsAsync(
        IReadOnlyList<TerritoryImportRow> rows,
        CancellationToken cancellationToken,
        bool forceRefresh)
    {
        var startedAt = DateTime.UtcNow;
        var result = new GeoBoundariesBatchImportResultDto
        {
            ReleaseType = ReleaseType,
            AdmLevel = AdmLevel,
            RequestedCount = rows.Count,
            StartedAt = startedAt
        };

        foreach (var row in rows)
        {
            try
            {
                var import = await ImportAdm0BoundaryAsync(
                    row.Iso3,
                    row.TerritoryId,
                    cancellationToken,
                    forceRefresh);

                result.Results.Add(import);
                if (import.Skipped)
                {
                    result.SkippedCount++;
                }
                else
                {
                    result.ImportedCount++;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                result.Failures.Add(new GeoBoundariesImportFailureDto
                {
                    TerritoryId = row.TerritoryId,
                    Iso3 = row.Iso3,
                    Name = row.Name,
                    Error = ex.Message
                });
                result.FailedCount++;

                _logger.LogWarning(
                    ex,
                    "geoBoundaries {ReleaseType} {AdmLevel} import failed for {Iso3} territory {TerritoryId}.",
                    ReleaseType,
                    AdmLevel,
                    row.Iso3,
                    row.TerritoryId);
            }
        }

        result.CompletedAt = DateTime.UtcNow;
        return result;
    }

    private async Task<List<TerritoryImportRow>> LoadTerritoriesWithIso3Async(CancellationToken cancellationToken)
    {
        var rows = await _db.Territories
            .AsNoTracking()
            .Select(territory => new TerritoryImportRow
            {
                TerritoryId = territory.Id,
                Name = territory.Name,
                TerritoryKind = territory.TerritoryKind,
                CountryCode = territory.CountryCode,
                CountryIso3 = territory.CountryIso3,
                BoundaryGeometry = EF.Property<Geometry?>(territory, BoundaryGeometryPropertyName),
                BoundaryDisplayGeometry = EF.Property<Geometry?>(territory, BoundaryDisplayGeometryPropertyName)
            })
            .ToListAsync(cancellationToken);

        return rows
            .Where(row => row.TerritoryKind == TerritoryKinds.Country)
            .Select(row =>
            {
                row.Iso3 = CountryRegistry.NormalizeIso3(row.CountryIso3)
                    ?? CountryRegistry.ToIso3(row.CountryCode)
                    ?? string.Empty;
                return row;
            })
            .Where(row => !string.IsNullOrWhiteSpace(row.Iso3))
            .GroupBy(row => row.Iso3, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderBy(item => item.TerritoryId).First())
            .OrderBy(row => row.Iso3, StringComparer.Ordinal)
            .ToList();
    }

    private async Task EnsureCountryTerritoryRecordsAsync(CancellationToken cancellationToken)
    {
        var territories = await _db.Territories.ToListAsync(cancellationToken);
        var byIso3 = territories
            .Where(territory => !string.IsNullOrWhiteSpace(territory.CountryIso3))
            .Where(territory => territory.TerritoryKind == TerritoryKinds.Country)
            .GroupBy(territory => territory.CountryIso3!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        var byIso2 = territories
            .Where(territory => !string.IsNullOrWhiteSpace(territory.CountryCode))
            .Where(territory => territory.TerritoryKind == TerritoryKinds.Country)
            .GroupBy(territory => territory.CountryCode!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        var byName = territories
            .GroupBy(territory => territory.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        var now = DateTime.UtcNow;
        var changed = false;

        foreach (var country in CountryRegistry.Countries)
        {
            var territoryChanged = false;
            byIso3.TryGetValue(country.Iso3, out var territory);
            territory ??= byIso2.GetValueOrDefault(country.Iso2);
            var sameNameTerritory = byName.GetValueOrDefault(country.Name);
            if (territory is null &&
                sameNameTerritory is not null &&
                string.Equals(sameNameTerritory.TerritoryKind, TerritoryKinds.Country, StringComparison.Ordinal))
            {
                territory = sameNameTerritory;
            }

            if (territory is null)
            {
                if (sameNameTerritory is not null)
                {
                    continue;
                }

                territory = new Territory
                {
                    Name = country.Name,
                    TerritoryKind = TerritoryKinds.Country,
                    CountryCode = country.Iso2,
                    CountryIso3 = country.Iso3,
                    CreatedAt = now
                };

                _db.Territories.Add(territory);
                territories.Add(territory);
                byIso2[country.Iso2] = territory;
                byIso3[country.Iso3] = territory;
                byName[country.Name] = territory;
                changed = true;
                continue;
            }

            if (string.IsNullOrWhiteSpace(territory.CountryCode))
            {
                territory.CountryCode = country.Iso2;
                territoryChanged = true;
                changed = true;
            }

            if (!string.Equals(territory.TerritoryKind, TerritoryKinds.Country, StringComparison.Ordinal))
            {
                territory.TerritoryKind = TerritoryKinds.Country;
                territoryChanged = true;
                changed = true;
            }

            if (string.IsNullOrWhiteSpace(territory.CountryIso3))
            {
                territory.CountryIso3 = country.Iso3;
                territoryChanged = true;
                changed = true;
            }

            if (territoryChanged && territory.Id != 0)
            {
                territory.UpdatedAt ??= now;
            }
        }

        if (changed)
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
    }

    private async Task<BoundaryGeometryRow> GetBoundaryGeometriesAsync(int territoryId, CancellationToken cancellationToken) =>
        await _db.Territories
            .Where(territory => territory.Id == territoryId)
            .Select(territory => new BoundaryGeometryRow
            {
                BoundaryGeometry = EF.Property<Geometry?>(territory, BoundaryGeometryPropertyName),
                BoundaryDisplayGeometry = EF.Property<Geometry?>(territory, BoundaryDisplayGeometryPropertyName)
            })
            .SingleAsync(cancellationToken);

    private static void EnsureTerritoryMatchesImport(Territory territory, string normalizedIso3, string? iso2)
    {
        if (!string.IsNullOrWhiteSpace(territory.CountryIso3) &&
            !string.Equals(territory.CountryIso3, normalizedIso3, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Imported ISO3 code does not match the selected territory.");
        }

        if (!string.IsNullOrWhiteSpace(territory.CountryCode) &&
            !string.IsNullOrWhiteSpace(iso2) &&
            !string.Equals(territory.CountryCode, iso2, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Imported ISO3 code does not match the selected territory country code.");
        }
    }

    private static GeoBoundariesImportResultDto BuildResult(Territory territory, Geometry geometry, bool skipped)
    {
        var normalizedIso3 = CountryRegistry.NormalizeIso3(territory.CountryIso3)
            ?? CountryRegistry.ToIso3(territory.CountryCode)
            ?? string.Empty;

        return new GeoBoundariesImportResultDto
        {
            TerritoryId = territory.Id,
            Iso2 = territory.CountryCode,
            Iso3 = normalizedIso3,
            Name = territory.Name,
            SourceUrl = territory.BoundarySourceUrl ?? string.Empty,
            License = territory.BoundaryLicense,
            ReleaseType = territory.BoundaryReleaseType ?? ReleaseType,
            AdmLevel = territory.BoundaryAdmLevel ?? AdmLevel,
            BoundaryGeoJson = ToGeoJson(geometry)
                ?? throw new InvalidOperationException("Imported boundary geometry could not be serialized."),
            Skipped = skipped
        };
    }

    private async Task<Territory?> ResolveTerritoryAsync(
        int? territoryId,
        string iso3,
        string? iso2,
        string name,
        CancellationToken cancellationToken)
    {
        if (territoryId.HasValue)
        {
            if (territoryId.Value <= 0)
                throw new InvalidOperationException("Territory id is required.");

            var territory = await _db.Territories
                .SingleOrDefaultAsync(item => item.Id == territoryId.Value, cancellationToken)
                ?? throw new KeyNotFoundException("Territory not found.");

            if (!string.Equals(territory.TerritoryKind, TerritoryKinds.Country, StringComparison.Ordinal))
                throw new InvalidOperationException("Disputed territories cannot be imported as ADM0 country boundaries.");

            return territory;
        }

        var normalizedName = name.Trim().ToLowerInvariant();
        var matchedTerritory = await _db.Territories
            .OrderBy(item => item.Id)
            .FirstOrDefaultAsync(
                item =>
                    item.TerritoryKind == TerritoryKinds.Country &&
                    (item.CountryIso3 == iso3 ||
                    (iso2 != null && item.CountryCode == iso2) ||
                    item.Name.ToLower() == normalizedName),
                cancellationToken);

        if (matchedTerritory is not null)
            return matchedTerritory;

        var sameName = await _db.Territories
            .OrderBy(item => item.Id)
            .FirstOrDefaultAsync(item => item.Name.ToLower() == normalizedName, cancellationToken);
        if (sameName is not null)
        {
            throw new InvalidOperationException(
                $"Cannot import ADM0 country boundary because existing {sameName.TerritoryKind} territory '{sameName.Name}' already uses that unique name.");
        }

        return null;
    }

    private async Task<JsonDocument> GetJsonDocumentAsync(
        string url,
        string errorMessage,
        CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            throw new InvalidOperationException(errorMessage);

        using var response = await _httpClient.GetAsync(uri, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning(
                "geoBoundaries request to {Url} failed with status {StatusCode}.",
                uri.Host,
                (int)response.StatusCode);
            throw new InvalidOperationException(errorMessage);
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
    }

    private static string ResolveGeoJsonDownloadUrl(JsonElement metadata)
    {
        var url = FirstNonEmpty(
            GetString(metadata, "gjDownloadURL"),
            GetString(metadata, "geoJsonDownloadUrl"),
            GetString(metadata, "geoJSONDownloadURL"),
            GetString(metadata, "simplifiedGeometryGeoJSON"));

        if (string.IsNullOrWhiteSpace(url))
            throw new InvalidOperationException("geoBoundaries GeoJSON download URL was not present in the metadata response.");

        return url;
    }

    private static Geometry ExtractBoundaryGeometry(JsonElement root)
    {
        var geometries = ExtractGeometries(root).ToList();
        if (geometries.Count == 0)
            throw new InvalidOperationException("geoBoundaries GeoJSON did not contain a Polygon or MultiPolygon boundary.");

        var polygons = geometries.SelectMany(FlattenPolygons).ToArray();
        if (polygons.Length == 0)
            throw new InvalidOperationException("geoBoundaries GeoJSON did not contain a Polygon or MultiPolygon boundary.");

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

        if (type.Equals("FeatureCollection", StringComparison.OrdinalIgnoreCase))
        {
            if (!TryGetProperty(element, "features", out var features) ||
                features.ValueKind != JsonValueKind.Array)
            {
                yield break;
            }

            foreach (var feature in features.EnumerateArray())
            {
                foreach (var geometry in ExtractGeometries(feature))
                {
                    yield return geometry;
                }
            }

            yield break;
        }

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
            throw new InvalidOperationException("geoBoundaries polygon coordinates are invalid.");

        var rings = coordinatesElement.EnumerateArray()
            .Select(ParseRing)
            .ToList();

        if (rings.Count == 0)
            throw new InvalidOperationException("geoBoundaries polygon boundary must include an exterior ring.");

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
            throw new InvalidOperationException("geoBoundaries multipolygon coordinates are invalid.");

        var polygons = coordinatesElement.EnumerateArray()
            .Select(BuildPolygon)
            .ToArray();

        if (polygons.Length == 0)
            throw new InvalidOperationException("geoBoundaries boundary must include at least one polygon.");

        var multiPolygon = Wgs84GeometryFactory.CreateMultiPolygon(polygons);
        multiPolygon.SRID = 4326;
        return multiPolygon;
    }

    private static Coordinate[] ParseRing(JsonElement ringElement)
    {
        if (ringElement.ValueKind != JsonValueKind.Array)
            throw new InvalidOperationException("geoBoundaries ring coordinates are invalid.");

        var coordinates = ringElement.EnumerateArray()
            .Select(ParseCoordinate)
            .ToList();

        if (coordinates.Count < 3)
            throw new InvalidOperationException("geoBoundaries polygon rings must contain at least three points.");

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
            throw new InvalidOperationException("geoBoundaries polygon rings must contain at least three distinct points.");

        return coordinates.ToArray();
    }

    private static Coordinate ParseCoordinate(JsonElement coordinateElement)
    {
        if (coordinateElement.ValueKind != JsonValueKind.Array)
            throw new InvalidOperationException("geoBoundaries coordinates are invalid.");

        var values = coordinateElement.EnumerateArray().ToArray();
        if (values.Length < 2 ||
            !values[0].TryGetDouble(out var longitude) ||
            !values[1].TryGetDouble(out var latitude) ||
            !double.IsFinite(latitude) ||
            !double.IsFinite(longitude) ||
            latitude is < -90d or > 90d ||
            longitude is < -180d or > 180d)
        {
            throw new InvalidOperationException("geoBoundaries coordinates are invalid.");
        }

        return new Coordinate(longitude, latitude);
    }

    private static void EnsureValidBoundary(Geometry geometry)
    {
        if (geometry.IsEmpty || !geometry.IsValid)
            throw new InvalidOperationException("geoBoundaries boundary geometry is invalid.");

        if (geometry is not Polygon and not MultiPolygon)
            throw new InvalidOperationException("geoBoundaries boundary geometry must be a Polygon or MultiPolygon.");

        SetSrid(geometry);
        if (geometry.SRID != 4326)
            throw new InvalidOperationException("geoBoundaries boundary geometry is invalid.");
    }

    private static bool IsValidAdm0Boundary(Geometry? geometry)
    {
        if (geometry is null || geometry.IsEmpty || !geometry.IsValid)
            return false;

        if (geometry is not Polygon and not MultiPolygon)
            return false;

        return geometry.SRID == 4326;
    }

    private static AdminTerritoryGeometryDto? ToGeoJson(Geometry? geometry)
    {
        if (geometry is null || geometry.IsEmpty || !geometry.IsValid)
            return null;

        return geometry switch
        {
            Polygon polygon => new AdminTerritoryGeometryDto
            {
                Type = "Polygon",
                Coordinates = ToPolygonCoordinates(polygon)
            },
            MultiPolygon multiPolygon => new AdminTerritoryGeometryDto
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

    private static string? GetString(JsonElement element, string propertyName)
    {
        if (!TryGetProperty(element, propertyName, out var value))
            return null;

        return value.ValueKind == JsonValueKind.String ? value.GetString() : value.ToString();
    }

    private static string FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim()
        ?? string.Empty;

    private static string NormalizeLicense(string license) =>
        license.Contains("CC BY 4.0", StringComparison.OrdinalIgnoreCase)
            ? DefaultLicense
            : license;

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

    private sealed class TerritoryImportRow
    {
        public int TerritoryId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string TerritoryKind { get; set; } = TerritoryKinds.Country;
        public string? CountryCode { get; set; }
        public string? CountryIso3 { get; set; }
        public string Iso3 { get; set; } = string.Empty;
        public Geometry? BoundaryGeometry { get; set; }
        public Geometry? BoundaryDisplayGeometry { get; set; }
    }

    private sealed class BoundaryGeometryRow
    {
        public Geometry? BoundaryGeometry { get; set; }
        public Geometry? BoundaryDisplayGeometry { get; set; }
    }
}
