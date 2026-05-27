using System.Globalization;
using System.Text;
using Geotrack.Application.DTOs.Admin;
using Geotrack.Application.Interfaces.Service;
using Geotrack.Application.Options;
using Geotrack.Domain.Entities.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NetTopologySuite.Geometries;
using NetTopologySuite.Operation.Union;

namespace Geotrack.Infrastructure.Services;

public sealed class RegionBoundaryBackfillService : IRegionBoundaryBackfillService
{
    private const string BoundaryGeometryPropertyName = "BoundaryGeometry";
    private const string BoundaryDisplayGeometryPropertyName = "BoundaryDisplayGeometry";
    private const string GeoBoundariesSource = "geoBoundaries";
    private const string GeoBoundariesReleaseType = "gbOpen";
    private const string GeoBoundariesLicense = "CC BY 4.0";
    private const string Adm1Level = "ADM1";

    private const string StatusMatched = "Matched";
    private const string StatusUpdated = "Updated";
    private const string StatusSkippedAlreadyLinked = "SkippedAlreadyLinked";
    private const string StatusAmbiguous = "Ambiguous";
    private const string StatusNoMatch = "NoMatch";
    private const string StatusFailed = "Failed";

    private readonly AppDbContext _db;
    private readonly RegionBoundaryBackfillOptions _options;
    private readonly ILogger<RegionBoundaryBackfillService> _logger;

    public RegionBoundaryBackfillService(
        AppDbContext db,
        IOptions<RegionBoundaryBackfillOptions> options,
        ILogger<RegionBoundaryBackfillService> logger)
    {
        _db = db;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<RegionBoundaryBackfillSummaryDto> BackfillBoundaryCandidatesAsync(
        string? countryIso3 = null,
        bool dryRun = true,
        bool force = false,
        CancellationToken cancellationToken = default)
    {
        var normalizedCountryIso3 = NormalizeCountryIso3(countryIso3);
        if (!string.IsNullOrWhiteSpace(countryIso3) && string.IsNullOrWhiteSpace(normalizedCountryIso3))
            throw new InvalidOperationException("countryIso3 must be a valid ISO3 country code.");

        var regions = await LoadRegionsAsync(normalizedCountryIso3, cancellationToken);
        var candidates = await LoadCandidatesAsync(normalizedCountryIso3, cancellationToken);
        var candidateLookup = BuildCandidateLookup(candidates);
        var plannedAssignments = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var items = new List<RegionBoundaryBackfillItemDto>();

        foreach (var region in regions)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var item = BuildBaseItem(region);
            items.Add(item);

            var match = ResolveCandidate(region, candidateLookup);
            if (match.Status != StatusMatched || match.Candidate is null)
            {
                item.Status = match.Status;
                item.Message = match.Message;
                continue;
            }

            var assignmentKey = BuildAssignmentKey(region.TerritoryId, match.Candidate.Id);
            if (plannedAssignments.Contains(assignmentKey))
            {
                item.Status = StatusAmbiguous;
                item.Message = "Another region in this territory is already matched to the same administrative boundary candidate.";
                PopulateMatch(item, match.Candidate);
                continue;
            }

            plannedAssignments.Add(assignmentKey);
            PopulateMatch(item, match.Candidate);

            if (!force && IsFullyLinked(region, match.Candidate))
            {
                item.Status = StatusSkippedAlreadyLinked;
                item.Message = "Region is already linked to this ADM1 boundary candidate.";
                continue;
            }

            if (dryRun)
            {
                item.Status = StatusMatched;
                item.Message = match.Message;
                continue;
            }

            try
            {
                await ApplyMatchAsync(region, match.Candidate, force, cancellationToken);
                item.Status = StatusUpdated;
                item.Message = "Region boundary metadata was updated from the matched ADM1 candidate.";
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Failed to backfill boundary candidate {CandidateId} for region {RegionId}.",
                    match.Candidate.Id,
                    region.Id);

                _db.ChangeTracker.Clear();
                item.Status = StatusFailed;
                item.Message = "Failed to update region boundary metadata.";
            }
        }

        return BuildSummary(items);
    }

    private async Task<List<RegionBackfillRow>> LoadRegionsAsync(
        string? countryIso3,
        CancellationToken cancellationToken)
    {
        var rows = await _db.Regions
            .AsNoTracking()
            .OrderBy(region => region.Territory.Name)
            .ThenBy(region => region.Name)
            .ThenBy(region => region.Id)
            .Select(region => new RegionBackfillRow
            {
                Id = region.Id,
                Name = region.Name,
                TerritoryId = region.TerritoryId,
                TerritoryCountryCode = region.Territory.CountryCode,
                TerritoryCountryIso3 = region.Territory.CountryIso3,
                BoundaryCandidateId = region.BoundaryCandidateId,
                BoundaryExternalKey = region.BoundaryExternalKey,
                BoundaryShapeGroup = region.BoundaryShapeGroup,
                BoundaryShapeId = region.BoundaryShapeId,
                BoundaryShapeType = region.BoundaryShapeType,
                BoundaryAdmLevel = region.BoundaryAdmLevel,
                BoundarySource = region.BoundarySource,
                BoundaryReleaseType = region.BoundaryReleaseType,
                BoundaryLicense = region.BoundaryLicense,
                BoundaryGeometry = EF.Property<Geometry?>(region, BoundaryGeometryPropertyName)
            })
            .ToListAsync(cancellationToken);

        foreach (var row in rows)
        {
            row.CountryIso3 = NormalizeCountryIso3(row.TerritoryCountryIso3)
                ?? NormalizeCountryIso3(row.TerritoryCountryCode);
        }

        return string.IsNullOrWhiteSpace(countryIso3)
            ? rows
            : rows
                .Where(row => string.Equals(row.CountryIso3, countryIso3, StringComparison.OrdinalIgnoreCase))
                .ToList();
    }

    private async Task<List<CandidateBackfillRow>> LoadCandidatesAsync(
        string? countryIso3,
        CancellationToken cancellationToken)
    {
        var query = _db.AdministrativeBoundaryCandidates
            .AsNoTracking()
            .Where(candidate =>
                candidate.Source == GeoBoundariesSource &&
                candidate.AdmLevel == Adm1Level);

        if (!string.IsNullOrWhiteSpace(countryIso3))
        {
            query = query.Where(candidate => candidate.CountryIso3 == countryIso3);
        }

        return await query
            .OrderBy(candidate => candidate.CountryIso3)
            .ThenBy(candidate => candidate.ShapeName)
            .ThenBy(candidate => candidate.ShapeId)
            .Select(candidate => new CandidateBackfillRow
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
                BoundaryGeometry = EF.Property<Geometry?>(candidate, BoundaryGeometryPropertyName),
                BoundaryDisplayGeometry = EF.Property<Geometry?>(candidate, BoundaryDisplayGeometryPropertyName)
            })
            .ToListAsync(cancellationToken);
    }

    private CandidateLookup BuildCandidateLookup(List<CandidateBackfillRow> candidates)
    {
        return new CandidateLookup
        {
            ById = candidates
                .GroupBy(candidate => candidate.Id)
                .ToDictionary(group => group.Key, group => group.First()),
            ByExternalKey = candidates
                .Where(candidate => !string.IsNullOrWhiteSpace(candidate.ExternalKey))
                .GroupBy(candidate => candidate.ExternalKey, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.OrdinalIgnoreCase),
            ByShapeKey = candidates
                .Where(candidate =>
                    !string.IsNullOrWhiteSpace(candidate.ShapeGroup) &&
                    !string.IsNullOrWhiteSpace(candidate.ShapeId))
                .GroupBy(
                    candidate => BuildShapeKey(candidate.ShapeGroup, candidate.ShapeId, candidate.AdmLevel),
                    StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.OrdinalIgnoreCase),
            ByCountry = candidates
                .GroupBy(candidate => candidate.CountryIso3, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.OrdinalIgnoreCase)
        };
    }

    private MatchResult ResolveCandidate(RegionBackfillRow region, CandidateLookup lookup)
    {
        if (string.IsNullOrWhiteSpace(region.CountryIso3))
        {
            return MatchResult.NoMatch("Region territory has no ISO3 country code.");
        }

        if (region.BoundaryCandidateId.HasValue)
        {
            return lookup.ById.TryGetValue(region.BoundaryCandidateId.Value, out var candidate) &&
                BoundaryCountryMatches(region, candidate)
                ? MatchResult.Matched(candidate, "Matched by existing boundary candidate id.")
                : MatchResult.NoMatch("Existing BoundaryCandidateId does not match any ADM1 candidate in the region country.");
        }

        if (!string.IsNullOrWhiteSpace(region.BoundaryExternalKey))
        {
            var candidates = lookup.ByExternalKey.GetValueOrDefault(region.BoundaryExternalKey)
                ?.Where(candidate => BoundaryCountryMatches(region, candidate))
                .ToList() ?? new List<CandidateBackfillRow>();

            return ResolveCandidateSet(
                candidates,
                "Matched by existing boundary external key.",
                "Existing BoundaryExternalKey matches more than one ADM1 candidate.",
                "Existing BoundaryExternalKey does not match any ADM1 candidate in the region country.");
        }

        if (string.Equals(region.BoundaryAdmLevel, Adm1Level, StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(region.BoundaryShapeGroup) &&
            !string.IsNullOrWhiteSpace(region.BoundaryShapeId))
        {
            var candidates = lookup.ByShapeKey
                .GetValueOrDefault(BuildShapeKey(region.BoundaryShapeGroup, region.BoundaryShapeId, Adm1Level))
                ?.Where(candidate => BoundaryCountryMatches(region, candidate))
                .ToList() ?? new List<CandidateBackfillRow>();

            return ResolveCandidateSet(
                candidates,
                "Matched by existing ADM1 shape group and shape id.",
                "Existing ADM1 shape metadata matches more than one candidate.",
                "Existing ADM1 shape metadata does not match any candidate in the region country.");
        }

        var countryCandidates = lookup.ByCountry.GetValueOrDefault(region.CountryIso3)
            ?? new List<CandidateBackfillRow>();
        var normalizedRegionName = NormalizeName(region.Name);
        var exactNameCandidates = countryCandidates
            .Where(candidate => NormalizeName(candidate.ShapeName) == normalizedRegionName)
            .ToList();

        var exactNameMatch = ResolveCandidateSet(
            exactNameCandidates,
            "Matched by exact normalized region and ADM1 candidate name.",
            "Region name matches multiple ADM1 candidates after normalization.",
            null);

        if (exactNameMatch.Status is StatusMatched or StatusAmbiguous)
            return exactNameMatch;

        var aliasTarget = ResolveAliasTarget(region.CountryIso3, region.Name);
        if (!string.IsNullOrWhiteSpace(aliasTarget))
        {
            var normalizedAliasTarget = NormalizeName(aliasTarget);
            var aliasCandidates = countryCandidates
                .Where(candidate =>
                    string.Equals(candidate.ExternalKey, aliasTarget, StringComparison.OrdinalIgnoreCase) ||
                    NormalizeName(candidate.ShapeName) == normalizedAliasTarget)
                .ToList();

            return ResolveCandidateSet(
                aliasCandidates,
                $"Matched by explicit alias '{region.Name}' -> '{aliasTarget}'.",
                $"Explicit alias '{region.Name}' -> '{aliasTarget}' is ambiguous.",
                $"Explicit alias '{region.Name}' -> '{aliasTarget}' did not match any ADM1 candidate.");
        }

        return MatchResult.NoMatch("No exact metadata, exact name, or configured alias match was found.");
    }

    private static MatchResult ResolveCandidateSet(
        List<CandidateBackfillRow> candidates,
        string matchedMessage,
        string ambiguousMessage,
        string? noMatchMessage)
    {
        return candidates.Count switch
        {
            1 => MatchResult.Matched(candidates[0], matchedMessage),
            > 1 => MatchResult.Ambiguous(ambiguousMessage),
            _ => noMatchMessage is null
                ? MatchResult.NoMatch(string.Empty)
                : MatchResult.NoMatch(noMatchMessage)
        };
    }

    private string? ResolveAliasTarget(string? countryIso3, string regionName)
    {
        if (string.IsNullOrWhiteSpace(countryIso3) || string.IsNullOrWhiteSpace(regionName))
            return null;

        var countryAliases = _options.Aliases
            .FirstOrDefault(pair => string.Equals(pair.Key, countryIso3, StringComparison.OrdinalIgnoreCase))
            .Value;
        if (countryAliases is null)
            return null;

        var normalizedRegionName = NormalizeName(regionName);
        return countryAliases
            .FirstOrDefault(pair => NormalizeName(pair.Key) == normalizedRegionName)
            .Value;
    }

    private async Task ApplyMatchAsync(
        RegionBackfillRow regionRow,
        CandidateBackfillRow candidate,
        bool force,
        CancellationToken cancellationToken)
    {
        var boundaryGeometry = ToValidPolygonalGeometry(candidate.BoundaryGeometry)
            ?? throw new InvalidOperationException("Candidate boundary geometry is invalid.");

        var region = await _db.Regions
            .SingleAsync(existing => existing.Id == regionRow.Id, cancellationToken);

        region.BoundaryCandidateId = candidate.Id;
        region.BoundaryExternalKey = candidate.ExternalKey;
        region.BoundaryShapeGroup = candidate.ShapeGroup;
        region.BoundaryShapeId = candidate.ShapeId;
        region.BoundaryShapeType = boundaryGeometry.GeometryType;
        region.BoundaryAdmLevel = Adm1Level;
        region.BoundarySource = GeoBoundariesSource;
        region.BoundaryReleaseType = string.IsNullOrWhiteSpace(candidate.ReleaseType)
            ? GeoBoundariesReleaseType
            : candidate.ReleaseType;
        region.BoundaryLicense = GeoBoundariesLicense;
        region.BoundaryImportedAt = DateTime.UtcNow;
        region.UpdatedAt = DateTime.UtcNow;

        var entry = _db.Entry(region);
        entry.Property<Geometry?>(BoundaryGeometryPropertyName).CurrentValue = boundaryGeometry;

        if (RegionHasDisplayGeometryProperty())
        {
            var displayGeometry = ToValidPolygonalGeometry(candidate.BoundaryDisplayGeometry);
            if (displayGeometry is not null)
            {
                entry.Property<Geometry?>(BoundaryDisplayGeometryPropertyName).CurrentValue = displayGeometry;
            }
        }

        await _db.SaveChangesAsync(cancellationToken);
    }

    private bool RegionHasDisplayGeometryProperty() =>
        _db.Model
            .FindEntityType(typeof(Region))
            ?.FindProperty(BoundaryDisplayGeometryPropertyName) is not null;

    private static bool IsFullyLinked(RegionBackfillRow region, CandidateBackfillRow candidate)
    {
        return region.BoundaryCandidateId == candidate.Id &&
            string.Equals(region.BoundaryExternalKey, candidate.ExternalKey, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(region.BoundaryShapeGroup, candidate.ShapeGroup, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(region.BoundaryShapeId, candidate.ShapeId, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(region.BoundaryAdmLevel, Adm1Level, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(region.BoundarySource, GeoBoundariesSource, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(region.BoundaryReleaseType, candidate.ReleaseType, StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(region.BoundaryLicense) &&
            RegionBoundaryMatchesCandidate(region.BoundaryGeometry, candidate.BoundaryGeometry);
    }

    private static bool RegionBoundaryMatchesCandidate(Geometry? regionGeometry, Geometry? candidateGeometry)
    {
        var normalizedRegionGeometry = ToValidPolygonalGeometry(regionGeometry);
        var normalizedCandidateGeometry = ToValidPolygonalGeometry(candidateGeometry);

        return normalizedRegionGeometry is not null &&
            normalizedCandidateGeometry is not null &&
            normalizedRegionGeometry.EqualsTopologically(normalizedCandidateGeometry);
    }

    private static bool BoundaryCountryMatches(RegionBackfillRow region, CandidateBackfillRow candidate) =>
        string.Equals(region.CountryIso3, candidate.CountryIso3, StringComparison.OrdinalIgnoreCase);

    private static void PopulateMatch(RegionBoundaryBackfillItemDto item, CandidateBackfillRow candidate)
    {
        item.MatchedCandidateId = candidate.Id;
        item.MatchedCandidateName = candidate.ShapeName;
        item.MatchedExternalKey = candidate.ExternalKey;
    }

    private static RegionBoundaryBackfillItemDto BuildBaseItem(RegionBackfillRow region)
    {
        return new RegionBoundaryBackfillItemDto
        {
            RegionId = region.Id,
            RegionName = region.Name,
            CountryIso3 = region.CountryIso3,
            Status = StatusNoMatch
        };
    }

    private static RegionBoundaryBackfillSummaryDto BuildSummary(List<RegionBoundaryBackfillItemDto> items)
    {
        return new RegionBoundaryBackfillSummaryDto
        {
            TotalRegionsChecked = items.Count,
            MatchedCount = items.Count(item =>
                item.Status is StatusMatched or StatusUpdated or StatusSkippedAlreadyLinked),
            UpdatedCount = items.Count(item => item.Status == StatusUpdated),
            SkippedCount = items.Count(item => item.Status is StatusSkippedAlreadyLinked or StatusNoMatch),
            AmbiguousCount = items.Count(item => item.Status == StatusAmbiguous),
            FailedCount = items.Count(item => item.Status == StatusFailed),
            Items = items
        };
    }

    private static string? NormalizeCountryIso3(string? value)
    {
        return CountryRegistry.NormalizeIso3ForBoundaryImport(value)
            ?? CountryRegistry.ToIso3(value);
    }

    private static string NormalizeName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var decomposed = value.Trim().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        var previousWasSpace = false;

        foreach (var ch in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark)
                continue;

            if (char.IsWhiteSpace(ch) || ch is '-' or '_' or '‐' or '‑' or '‒' or '–' or '—')
            {
                if (!previousWasSpace)
                {
                    builder.Append(' ');
                    previousWasSpace = true;
                }

                continue;
            }

            builder.Append(char.ToLowerInvariant(ch));
            previousWasSpace = false;
        }

        return builder.ToString().Normalize(NormalizationForm.FormC).Trim();
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
                if (multiPolygon.GetGeometryN(index) is Polygon child)
                {
                    yield return child;
                }
            }

            yield break;
        }

        if (geometry is GeometryCollection collection)
        {
            for (var index = 0; index < collection.NumGeometries; index++)
            {
                foreach (var childPolygon in FlattenPolygons(collection.GetGeometryN(index)))
                {
                    yield return childPolygon;
                }
            }
        }
    }

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

    private static string BuildShapeKey(string? shapeGroup, string? shapeId, string? admLevel) =>
        $"{shapeGroup?.Trim().ToUpperInvariant()}::{shapeId?.Trim().ToUpperInvariant()}::{admLevel?.Trim().ToUpperInvariant()}";

    private static string BuildAssignmentKey(int territoryId, int candidateId) =>
        $"{territoryId}:{candidateId}";

    private sealed class CandidateLookup
    {
        public Dictionary<int, CandidateBackfillRow> ById { get; set; } = new();
        public Dictionary<string, List<CandidateBackfillRow>> ByExternalKey { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, List<CandidateBackfillRow>> ByShapeKey { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, List<CandidateBackfillRow>> ByCountry { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private sealed class RegionBackfillRow
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public int TerritoryId { get; set; }
        public string? TerritoryCountryCode { get; set; }
        public string? TerritoryCountryIso3 { get; set; }
        public string? CountryIso3 { get; set; }
        public int? BoundaryCandidateId { get; set; }
        public string? BoundaryExternalKey { get; set; }
        public string? BoundaryShapeGroup { get; set; }
        public string? BoundaryShapeId { get; set; }
        public string? BoundaryShapeType { get; set; }
        public string? BoundaryAdmLevel { get; set; }
        public string? BoundarySource { get; set; }
        public string? BoundaryReleaseType { get; set; }
        public string? BoundaryLicense { get; set; }
        public Geometry? BoundaryGeometry { get; set; }
    }

    private sealed class CandidateBackfillRow
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
        public Geometry? BoundaryGeometry { get; set; }
        public Geometry? BoundaryDisplayGeometry { get; set; }
    }

    private sealed record MatchResult(string Status, CandidateBackfillRow? Candidate, string Message)
    {
        public static MatchResult Matched(CandidateBackfillRow candidate, string message) =>
            new(StatusMatched, candidate, message);

        public static MatchResult Ambiguous(string message) =>
            new(StatusAmbiguous, null, message);

        public static MatchResult NoMatch(string message) =>
            new(StatusNoMatch, null, message);
    }
}
