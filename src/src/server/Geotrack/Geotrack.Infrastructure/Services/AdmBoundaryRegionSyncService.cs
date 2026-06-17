using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Geotrack.Application.DTOs.Admin;
using Geotrack.Application.Interfaces.Service;
using Geotrack.Application.Options;
using Geotrack.Application.Security;
using Geotrack.Domain.Entities.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NetTopologySuite.Geometries;
using NetTopologySuite.Operation.Union;

namespace Geotrack.Infrastructure.Services;

public sealed class AdmBoundaryRegionSyncService : IAdmBoundaryRegionSyncService
{
    private const string BoundaryGeometryPropertyName = "BoundaryGeometry";
    private const string BoundaryDisplayGeometryPropertyName = "BoundaryDisplayGeometry";
    private const string GeoBoundariesSource = "geoBoundaries";
    private const string GeoBoundariesReleaseType = "gbOpen";
    private const string GeoBoundariesLicense = "CC BY 4.0";
    private const string Adm1Level = "ADM1";

    private const string StatusAlreadyLinked = "AlreadyLinked";
    private const string StatusLinkedExisting = "LinkedExisting";
    private const string StatusCreated = "Created";
    private const string StatusUpdated = "Updated";
    private const string StatusSkipped = "Skipped";
    private const string StatusAmbiguous = "Ambiguous";
    private const string StatusFailed = "Failed";
    private const int MaxSummaryItems = 100;

    private readonly AppDbContext _db;
    private readonly RegionBoundaryBackfillOptions _options;
    private readonly ILogger<AdmBoundaryRegionSyncService> _logger;

    public AdmBoundaryRegionSyncService(
        AppDbContext db,
        IOptions<RegionBoundaryBackfillOptions> options,
        ILogger<AdmBoundaryRegionSyncService> logger)
    {
        _db = db;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<TerritoryRegionSetupStatusDto> GetRegionSetupStatusForTerritoryManagerAsync(
        int userId,
        int? territoryId = null,
        CancellationToken cancellationToken = default)
    {
        var resolvedTerritoryId = await ResolveTerritoryManagerTerritoryAsync(userId, territoryId, cancellationToken);
        var territory = await LoadTerritoryAsync(resolvedTerritoryId, cancellationToken)
            ?? throw new KeyNotFoundException("Territory was not found.");
        var countryIso3 = ResolveCountryIso3(territory)
            ?? throw new InvalidOperationException("Territory must have an ISO3 country code before syncing ADM1 regions.");

        var candidates = await LoadCandidatesAsync(countryIso3, cancellationToken);
        var regions = await LoadRegionsAsync(territory.Id, cancellationToken);
        var syncedRegions = regions
            .Where(IsSyncedAdm1Region)
            .ToList();

        var hasCandidateData = candidates.Count > 0;
        var missingCandidateData = !hasCandidateData;
        var regionSetupCompletedAt = await GetRegionSetupCompletedAtAsync(userId, territory.Id, cancellationToken);
        if (regionSetupCompletedAt is null && syncedRegions.Count > 0)
        {
            regionSetupCompletedAt = await MarkRegionSetupCompletedAsync(
                userId,
                territory.Id,
                DateTime.UtcNow,
                cancellationToken);
        }

        var onboardingCompleted = regionSetupCompletedAt.HasValue || syncedRegions.Count > 0;
        var requiresInitialSync = !onboardingCompleted && syncedRegions.Count == 0;
        RegionSyncSummaryDto? dryRunSummary = null;

        if (hasCandidateData)
        {
            dryRunSummary = await SyncRegionsFromAdm1CandidatesAsync(
                territory.Id,
                dryRun: true,
                forceUpdateGeometry: false,
                createMissingRegions: true,
                linkExistingRegions: true,
                cancellationToken);
        }

        var updateAvailable = dryRunSummary is not null && HasSyncWork(dryRunSummary);
        var canSync = hasCandidateData;
        var canUpdate = hasCandidateData && !requiresInitialSync && updateAvailable;

        return new TerritoryRegionSetupStatusDto
        {
            TerritoryId = territory.Id,
            TerritoryName = territory.Name,
            CountryIso3 = countryIso3,
            RequiresInitialSync = requiresInitialSync,
            OnboardingCompleted = onboardingCompleted,
            HasSyncedRegions = syncedRegions.Count > 0,
            SyncedRegionCount = syncedRegions.Count,
            CandidateCount = candidates.Count,
            HasCandidateData = hasCandidateData,
            CanSync = canSync,
            CanUpdate = canUpdate,
            UpdateAvailable = updateAvailable,
            MissingCandidateData = missingCandidateData,
            RegionSetupCompletedAt = regionSetupCompletedAt,
            LastSyncedAt = GetLastSyncedAt(syncedRegions),
            CandidateVersionHash = BuildCandidateVersionHash(candidates),
            SyncedVersionHash = BuildSyncedRegionVersionHash(syncedRegions),
            Message = BuildSetupStatusMessage(missingCandidateData, requiresInitialSync, updateAvailable, dryRunSummary)
        };
    }

    public async Task<RegionSyncSummaryDto> SyncRegionsFromAdm1CandidatesAsync(
        int territoryId,
        bool dryRun = true,
        bool forceUpdateGeometry = false,
        bool createMissingRegions = true,
        bool linkExistingRegions = true,
        CancellationToken cancellationToken = default)
    {
        if (territoryId <= 0)
            throw new InvalidOperationException("territoryId must be positive.");

        if (dryRun)
        {
            return await SyncRegionsFromAdm1CandidatesCoreAsync(
                territoryId,
                true,
                forceUpdateGeometry,
                createMissingRegions,
                linkExistingRegions,
                cancellationToken);
        }

        // EnableRetryOnFailure makes manually-started transactions invalid unless the
        // complete transaction is executed through the DbContext execution strategy.
        // Keep all reads and writes in the same retriable unit and clear tracked state
        // before a possible retry so a repeated attempt starts cleanly.
        var strategy = _db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            _db.ChangeTracker.Clear();

            return await SyncRegionsFromAdm1CandidatesCoreAsync(
                territoryId,
                false,
                forceUpdateGeometry,
                createMissingRegions,
                linkExistingRegions,
                cancellationToken);
        });
    }

    private async Task<RegionSyncSummaryDto> SyncRegionsFromAdm1CandidatesCoreAsync(
        int territoryId,
        bool dryRun,
        bool forceUpdateGeometry,
        bool createMissingRegions,
        bool linkExistingRegions,
        CancellationToken cancellationToken)
    {
        var territory = await LoadTerritoryAsync(territoryId, cancellationToken)
            ?? throw new KeyNotFoundException("Territory was not found.");
        var countryIso3 = ResolveCountryIso3(territory)
            ?? throw new InvalidOperationException("Territory must have an ISO3 country code before syncing ADM1 regions.");

        var candidates = await LoadCandidatesAsync(countryIso3, cancellationToken);
        var regions = await LoadRegionsAsync(territory.Id, cancellationToken);
        var lookup = BuildRegionLookup(regions);
        var ambiguousCandidateNames = candidates
            .GroupBy(candidate => NormalizeName(candidate.ShapeName), StringComparer.OrdinalIgnoreCase)
            .Where(group => !string.IsNullOrWhiteSpace(group.Key) && group.Count() > 1)
            .Select(group => group.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var plannedRegionIds = new HashSet<int>();
        var items = new List<RegionSyncItemDto>();
        var transaction = dryRun
            ? null
            : await _db.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            foreach (var candidate in candidates)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var item = BuildBaseItem(candidate);
                items.Add(item);

                var boundaryGeometry = ToValidPolygonalGeometry(candidate.BoundaryGeometry);
                if (boundaryGeometry is null)
                {
                    item.Status = StatusFailed;
                    item.Message = "ADM1 candidate boundary geometry is missing or invalid.";
                    continue;
                }

                var displayGeometry = ToValidPolygonalGeometry(candidate.BoundaryDisplayGeometry);
                var match = ResolveRegion(candidate, lookup, countryIso3);
                if (match.Status == StatusAmbiguous)
                {
                    item.Status = StatusAmbiguous;
                    item.Message = match.Message;
                    continue;
                }

                if (match.Region is not null)
                {
                    item.RegionId = match.Region.Id;
                    item.RegionName = match.Region.Name;

                    if (!plannedRegionIds.Add(match.Region.Id))
                    {
                        item.Status = StatusAmbiguous;
                        item.Message = "This existing region was already matched to another ADM1 candidate in the same sync run.";
                        continue;
                    }

                    if (!linkExistingRegions)
                    {
                        item.Status = StatusSkipped;
                        item.Message = "Existing region linking is disabled for this sync run.";
                        continue;
                    }

                    var plan = BuildUpdatePlan(match.Region, candidate, boundaryGeometry, displayGeometry, forceUpdateGeometry);
                    if (!plan.HasChanges)
                    {
                        item.Status = StatusAlreadyLinked;
                        item.Message = "Region is already linked to this ADM1 boundary candidate.";
                        continue;
                    }

                    var status = match.Region.BoundaryCandidateId == candidate.Id
                        ? StatusUpdated
                        : StatusLinkedExisting;
                    item.Status = status;
                    item.Message = status == StatusLinkedExisting
                        ? $"{match.Message} Boundary metadata will be linked to the existing region."
                        : "Existing region boundary metadata or geometry will be refreshed from the ADM1 candidate.";

                    if (dryRun)
                        continue;

                    try
                    {
                        await ApplyUpdateAsync(
                            match.Region.Id,
                            candidate,
                            boundaryGeometry,
                            displayGeometry,
                            plan,
                            cancellationToken);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(
                            ex,
                            "Failed to sync ADM1 candidate {CandidateId} to existing region {RegionId}.",
                            candidate.Id,
                            match.Region.Id);

                        _db.ChangeTracker.Clear();
                        item.Status = StatusFailed;
                        item.Message = "Failed to update the existing region from the ADM1 candidate.";
                    }

                    continue;
                }

                if (!createMissingRegions)
                {
                    item.Status = StatusSkipped;
                    item.Message = "No existing region matched this ADM1 candidate and creation is disabled.";
                    continue;
                }

                var regionName = candidate.ShapeName.Trim();
                if (string.IsNullOrWhiteSpace(regionName))
                {
                    item.Status = StatusFailed;
                    item.Message = "ADM1 candidate has no shape name to use as a region name.";
                    continue;
                }

                if (ambiguousCandidateNames.Contains(NormalizeName(regionName)))
                {
                    item.Status = StatusAmbiguous;
                    item.Message = "Multiple ADM1 candidates share this name, so a unique region cannot be created safely.";
                    continue;
                }

                item.RegionName = regionName;
                item.Status = StatusCreated;
                item.Message = "A new unassigned region will be created from this ADM1 candidate.";

                if (dryRun)
                    continue;

                try
                {
                    var created = await CreateRegionAsync(
                        territory.Id,
                        regionName,
                        candidate,
                        boundaryGeometry,
                        displayGeometry,
                        cancellationToken);

                    item.RegionId = created.Id;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(
                        ex,
                        "Failed to create region from ADM1 candidate {CandidateId}.",
                        candidate.Id);

                    _db.ChangeTracker.Clear();
                    item.Status = StatusFailed;
                    item.Message = "Failed to create a region from this ADM1 candidate.";
                }
            }

            var summary = BuildSummary(territory, countryIso3, dryRun, candidates.Count, items);

            if (transaction is not null)
            {
                if (summary.FailedCount > 0 || summary.AmbiguousCount > 0)
                {
                    await transaction.RollbackAsync(cancellationToken);
                }
                else
                {
                    await transaction.CommitAsync(cancellationToken);
                }
            }

            return summary;
        }
        catch
        {
            if (transaction is not null)
            {
                await transaction.RollbackAsync(cancellationToken);
            }

            throw;
        }
        finally
        {
            if (transaction is not null)
            {
                await transaction.DisposeAsync();
            }
        }
    }

    public async Task<RegionSyncSummaryDto> SyncRegionsFromAdm1CandidatesForTerritoryManagerAsync(
        int userId,
        int? territoryId = null,
        bool dryRun = true,
        bool forceUpdateGeometry = false,
        bool createMissingRegions = true,
        bool linkExistingRegions = true,
        CancellationToken cancellationToken = default)
    {
        var resolvedTerritoryId = await ResolveTerritoryManagerTerritoryAsync(userId, territoryId, cancellationToken);
        var summary = await SyncRegionsFromAdm1CandidatesAsync(
            resolvedTerritoryId,
            dryRun,
            forceUpdateGeometry,
            createMissingRegions,
            linkExistingRegions,
            cancellationToken);

        if (!dryRun && summary.FailedCount == 0 && summary.AmbiguousCount == 0)
        {
            await MarkRegionSetupCompletedAsync(userId, resolvedTerritoryId, summary.CompletedAt, cancellationToken);
        }

        return summary;
    }

    private async Task<TerritorySyncRow?> LoadTerritoryAsync(
        int territoryId,
        CancellationToken cancellationToken)
    {
        return await _db.Territories
            .AsNoTracking()
            .Where(territory => territory.Id == territoryId)
            .Select(territory => new TerritorySyncRow
            {
                Id = territory.Id,
                Name = territory.Name,
                CountryCode = territory.CountryCode,
                CountryIso3 = territory.CountryIso3
            })
            .SingleOrDefaultAsync(cancellationToken);
    }

    private async Task<List<CandidateSyncRow>> LoadCandidatesAsync(
        string countryIso3,
        CancellationToken cancellationToken)
    {
        return await _db.AdministrativeBoundaryCandidates
            .AsNoTracking()
            .Where(candidate =>
                candidate.Source == GeoBoundariesSource &&
                candidate.AdmLevel == Adm1Level &&
                candidate.CountryIso3 == countryIso3)
            .OrderBy(candidate => candidate.ShapeName)
            .ThenBy(candidate => candidate.ShapeId)
            .Select(candidate => new CandidateSyncRow
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
                ImportedAt = candidate.ImportedAt,
                UpdatedAt = candidate.UpdatedAt,
                BoundaryGeometry = EF.Property<Geometry?>(candidate, BoundaryGeometryPropertyName),
                BoundaryDisplayGeometry = EF.Property<Geometry?>(candidate, BoundaryDisplayGeometryPropertyName)
            })
            .ToListAsync(cancellationToken);
    }

    private async Task<List<RegionSyncRow>> LoadRegionsAsync(
        int territoryId,
        CancellationToken cancellationToken)
    {
        return await _db.Regions
            .AsNoTracking()
            .Where(region => region.TerritoryId == territoryId)
            .OrderBy(region => region.Name)
            .ThenBy(region => region.Id)
            .Select(region => new RegionSyncRow
            {
                Id = region.Id,
                Name = region.Name,
                TerritoryId = region.TerritoryId,
                BoundaryCandidateId = region.BoundaryCandidateId,
                BoundaryExternalKey = region.BoundaryExternalKey,
                BoundaryShapeGroup = region.BoundaryShapeGroup,
                BoundaryShapeId = region.BoundaryShapeId,
                BoundaryShapeType = region.BoundaryShapeType,
                BoundaryAdmLevel = region.BoundaryAdmLevel,
                BoundarySource = region.BoundarySource,
                BoundaryReleaseType = region.BoundaryReleaseType,
                BoundaryLicense = region.BoundaryLicense,
                BoundaryImportedAt = region.BoundaryImportedAt,
                BoundaryGeometry = EF.Property<Geometry?>(region, BoundaryGeometryPropertyName),
                BoundaryDisplayGeometry = EF.Property<Geometry?>(region, BoundaryDisplayGeometryPropertyName)
            })
            .ToListAsync(cancellationToken);
    }

    private RegionLookup BuildRegionLookup(List<RegionSyncRow> regions)
    {
        return new RegionLookup
        {
            ByCandidateId = regions
                .Where(region => region.BoundaryCandidateId.HasValue)
                .GroupBy(region => region.BoundaryCandidateId!.Value)
                .ToDictionary(group => group.Key, group => group.ToList()),
            ByExternalKey = regions
                .Where(region => !string.IsNullOrWhiteSpace(region.BoundaryExternalKey))
                .GroupBy(region => region.BoundaryExternalKey!, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.OrdinalIgnoreCase),
            ByShapeKey = regions
                .Where(region =>
                    string.Equals(region.BoundaryAdmLevel, Adm1Level, StringComparison.OrdinalIgnoreCase) &&
                    !string.IsNullOrWhiteSpace(region.BoundaryShapeGroup) &&
                    !string.IsNullOrWhiteSpace(region.BoundaryShapeId))
                .GroupBy(
                    region => BuildShapeKey(region.BoundaryShapeGroup, region.BoundaryShapeId, Adm1Level),
                    StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.OrdinalIgnoreCase),
            ByNormalizedName = regions
                .GroupBy(region => NormalizeName(region.Name), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.OrdinalIgnoreCase)
        };
    }

    private RegionMatchResult ResolveRegion(
        CandidateSyncRow candidate,
        RegionLookup lookup,
        string countryIso3)
    {
        if (lookup.ByCandidateId.TryGetValue(candidate.Id, out var byCandidateId))
        {
            return ResolveRegionSet(
                byCandidateId,
                "Matched by existing boundary candidate id.",
                "More than one region is already linked to this ADM1 candidate.");
        }

        if (!string.IsNullOrWhiteSpace(candidate.ExternalKey) &&
            lookup.ByExternalKey.TryGetValue(candidate.ExternalKey, out var byExternalKey))
        {
            return ResolveRegionSet(
                byExternalKey,
                "Matched by existing boundary external key.",
                "More than one region has this ADM1 boundary external key.");
        }

        var shapeKey = BuildShapeKey(candidate.ShapeGroup, candidate.ShapeId, Adm1Level);
        if (lookup.ByShapeKey.TryGetValue(shapeKey, out var byShapeKey))
        {
            return ResolveRegionSet(
                byShapeKey,
                "Matched by existing ADM1 shape group and shape id.",
                "More than one region has this ADM1 shape group and shape id.");
        }

        var normalizedCandidateName = NormalizeName(candidate.ShapeName);
        if (lookup.ByNormalizedName.TryGetValue(normalizedCandidateName, out var byName))
        {
            return ResolveRegionSet(
                byName,
                "Matched by exact normalized region and ADM1 candidate name.",
                "More than one region has the same normalized ADM1 candidate name.");
        }

        var aliasMatches = ResolveAliasRegionMatches(candidate, lookup, countryIso3);
        if (aliasMatches.Count > 0)
        {
            return ResolveRegionSet(
                aliasMatches,
                "Matched by explicit configured seed alias.",
                "Configured seed alias matches more than one existing region.");
        }

        return RegionMatchResult.NoMatch();
    }

    private List<RegionSyncRow> ResolveAliasRegionMatches(
        CandidateSyncRow candidate,
        RegionLookup lookup,
        string countryIso3)
    {
        var countryAliases = _options.Aliases
            .FirstOrDefault(pair => string.Equals(pair.Key, countryIso3, StringComparison.OrdinalIgnoreCase))
            .Value;
        if (countryAliases is null || countryAliases.Count == 0)
            return new List<RegionSyncRow>();

        var normalizedCandidateName = NormalizeName(candidate.ShapeName);
        var matches = new Dictionary<int, RegionSyncRow>();

        foreach (var alias in countryAliases)
        {
            if (string.IsNullOrWhiteSpace(alias.Key) || string.IsNullOrWhiteSpace(alias.Value))
                continue;

            var aliasTargetMatches =
                string.Equals(alias.Value, candidate.ExternalKey, StringComparison.OrdinalIgnoreCase) ||
                NormalizeName(alias.Value) == normalizedCandidateName;
            if (!aliasTargetMatches)
                continue;

            var normalizedAliasName = NormalizeName(alias.Key);
            if (!lookup.ByNormalizedName.TryGetValue(normalizedAliasName, out var rows))
                continue;

            foreach (var row in rows)
            {
                matches.TryAdd(row.Id, row);
            }
        }

        return matches.Values.ToList();
    }

    private static RegionMatchResult ResolveRegionSet(
        List<RegionSyncRow> regions,
        string matchedMessage,
        string ambiguousMessage)
    {
        return regions.Count switch
        {
            1 => RegionMatchResult.Matched(regions[0], matchedMessage),
            > 1 => RegionMatchResult.Ambiguous(ambiguousMessage),
            _ => RegionMatchResult.NoMatch()
        };
    }

    private RegionUpdatePlan BuildUpdatePlan(
        RegionSyncRow region,
        CandidateSyncRow candidate,
        Geometry boundaryGeometry,
        Geometry? displayGeometry,
        bool forceUpdateGeometry)
    {
        var expectedReleaseType = string.IsNullOrWhiteSpace(candidate.ReleaseType)
            ? GeoBoundariesReleaseType
            : candidate.ReleaseType;

        var metadataChanged =
            region.BoundaryCandidateId != candidate.Id ||
            !EqualsOrdinalIgnoreCase(region.BoundaryExternalKey, candidate.ExternalKey) ||
            !EqualsOrdinalIgnoreCase(region.BoundaryShapeGroup, candidate.ShapeGroup) ||
            !EqualsOrdinalIgnoreCase(region.BoundaryShapeId, candidate.ShapeId) ||
            !EqualsOrdinalIgnoreCase(region.BoundaryShapeType, boundaryGeometry.GeometryType) ||
            !EqualsOrdinalIgnoreCase(region.BoundaryAdmLevel, Adm1Level) ||
            !EqualsOrdinalIgnoreCase(region.BoundarySource, GeoBoundariesSource) ||
            !EqualsOrdinalIgnoreCase(region.BoundaryReleaseType, expectedReleaseType) ||
            !EqualsOrdinalIgnoreCase(region.BoundaryLicense, GeoBoundariesLicense) ||
            !SameInstant(region.BoundaryImportedAt, candidate.ImportedAt);

        var regionBoundary = ToValidPolygonalGeometry(region.BoundaryGeometry);
        var updateBoundaryGeometry = regionBoundary is null ||
            (forceUpdateGeometry && !GeometriesMatch(regionBoundary, boundaryGeometry));

        var updateDisplayGeometry = false;
        if (RegionHasDisplayGeometryProperty() && displayGeometry is not null)
        {
            var regionDisplayGeometry = ToValidPolygonalGeometry(region.BoundaryDisplayGeometry);
            updateDisplayGeometry = regionDisplayGeometry is null ||
                (forceUpdateGeometry && !GeometriesMatch(regionDisplayGeometry, displayGeometry));
        }

        return new RegionUpdatePlan(metadataChanged, updateBoundaryGeometry, updateDisplayGeometry);
    }

    private async Task ApplyUpdateAsync(
        int regionId,
        CandidateSyncRow candidate,
        Geometry boundaryGeometry,
        Geometry? displayGeometry,
        RegionUpdatePlan plan,
        CancellationToken cancellationToken)
    {
        var region = await _db.Regions
            .SingleAsync(existing => existing.Id == regionId, cancellationToken);

        ApplyMetadata(region, candidate, boundaryGeometry, updateTimestamp: true);

        var entry = _db.Entry(region);
        if (plan.UpdateBoundaryGeometry)
        {
            entry.Property<Geometry?>(BoundaryGeometryPropertyName).CurrentValue = boundaryGeometry;
        }

        if (plan.UpdateDisplayGeometry && displayGeometry is not null)
        {
            entry.Property<Geometry?>(BoundaryDisplayGeometryPropertyName).CurrentValue = displayGeometry;
        }

        await _db.SaveChangesAsync(cancellationToken);
    }

    private async Task<Region> CreateRegionAsync(
        int territoryId,
        string regionName,
        CandidateSyncRow candidate,
        Geometry boundaryGeometry,
        Geometry? displayGeometry,
        CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var region = new Region
        {
            Name = regionName,
            TerritoryId = territoryId,
            CreatedAt = now
        };

        ApplyMetadata(region, candidate, boundaryGeometry, updateTimestamp: false);

        _db.Regions.Add(region);
        var entry = _db.Entry(region);
        entry.Property<Geometry?>(BoundaryGeometryPropertyName).CurrentValue = boundaryGeometry;

        if (RegionHasDisplayGeometryProperty() && displayGeometry is not null)
        {
            entry.Property<Geometry?>(BoundaryDisplayGeometryPropertyName).CurrentValue = displayGeometry;
        }

        await _db.SaveChangesAsync(cancellationToken);
        return region;
    }

    private static void ApplyMetadata(
        Region region,
        CandidateSyncRow candidate,
        Geometry boundaryGeometry,
        bool updateTimestamp)
    {
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
        region.BoundaryImportedAt = candidate.ImportedAt;

        if (updateTimestamp)
        {
            region.UpdatedAt = DateTime.UtcNow;
        }
    }

    private async Task<int> ResolveTerritoryManagerTerritoryAsync(
        int userId,
        int? territoryId,
        CancellationToken cancellationToken)
    {
        if (userId <= 0)
            throw new UnauthorizedAccessException("Invalid user token.");

        var roles = await _db.UserRoles
            .AsNoTracking()
            .Where(userRole => userRole.UserId == userId)
            .Select(userRole => userRole.Role.Name)
            .ToListAsync(cancellationToken);

        var normalizedRoles = roles.Select(RoleNames.Normalize).ToList();
        if (!normalizedRoles.Contains(RoleNames.TerritoryManager, StringComparer.Ordinal) &&
            !normalizedRoles.Contains(RoleNames.TerritoryAssociate, StringComparer.Ordinal))
        {
            throw new UnauthorizedAccessException("Territory Manager or Territory Associate role is required.");
        }

        var assignedTerritoryIds = await _db.TerritoryManagerTerritories
            .AsNoTracking()
            .Where(assignment => assignment.UserId == userId)
            .OrderBy(assignment => assignment.Territory.Name)
            .ThenBy(assignment => assignment.TerritoryId)
            .Select(assignment => assignment.TerritoryId)
            .ToListAsync(cancellationToken);

        if (assignedTerritoryIds.Count == 0)
            throw new UnauthorizedAccessException("Territory is not assigned to the current territory manager.");

        if (territoryId.HasValue)
        {
            if (territoryId.Value <= 0)
                throw new InvalidOperationException("territoryId must be positive.");

            if (!assignedTerritoryIds.Contains(territoryId.Value))
                throw new UnauthorizedAccessException("Territory is not assigned to the current territory manager.");

            return territoryId.Value;
        }

        return assignedTerritoryIds[0];
    }

    private async Task<DateTime?> GetRegionSetupCompletedAtAsync(
        int userId,
        int territoryId,
        CancellationToken cancellationToken)
    {
        return await _db.TerritoryManagerTerritories
            .AsNoTracking()
            .Where(assignment =>
                assignment.UserId == userId &&
                assignment.TerritoryId == territoryId)
            .Select(assignment => assignment.RegionSetupCompletedAt)
            .SingleOrDefaultAsync(cancellationToken);
    }

    private async Task<DateTime> MarkRegionSetupCompletedAsync(
        int userId,
        int territoryId,
        DateTime completedAt,
        CancellationToken cancellationToken)
    {
        var assignment = await _db.TerritoryManagerTerritories
            .SingleOrDefaultAsync(item =>
                item.UserId == userId &&
                item.TerritoryId == territoryId,
                cancellationToken)
            ?? throw new UnauthorizedAccessException("Territory is not assigned to the current territory manager.");

        if (assignment.RegionSetupCompletedAt.HasValue)
            return NormalizeDateTime(assignment.RegionSetupCompletedAt.Value);

        var normalizedCompletedAt = NormalizeDateTime(completedAt);
        assignment.RegionSetupCompletedAt = normalizedCompletedAt;
        assignment.UpdatedAt = normalizedCompletedAt;
        await _db.SaveChangesAsync(cancellationToken);
        return normalizedCompletedAt;
    }

    private bool RegionHasDisplayGeometryProperty() =>
        _db.Model
            .FindEntityType(typeof(Region))
            ?.FindProperty(BoundaryDisplayGeometryPropertyName) is not null;

    private static RegionSyncItemDto BuildBaseItem(CandidateSyncRow candidate)
    {
        return new RegionSyncItemDto
        {
            CandidateId = candidate.Id,
            CandidateName = candidate.ShapeName,
            CandidateExternalKey = candidate.ExternalKey,
            Status = StatusSkipped
        };
    }

    private static RegionSyncSummaryDto BuildSummary(
        TerritorySyncRow territory,
        string countryIso3,
        bool dryRun,
        int totalCandidates,
        List<RegionSyncItemDto> items)
    {
        return new RegionSyncSummaryDto
        {
            TerritoryId = territory.Id,
            TerritoryName = territory.Name,
            CountryIso3 = countryIso3,
            TotalCandidates = totalCandidates,
            ExistingLinkedCount = items.Count(item => item.Status == StatusAlreadyLinked),
            LinkedExistingCount = items.Count(item => item.Status == StatusLinkedExisting),
            CreatedCount = items.Count(item => item.Status == StatusCreated),
            UpdatedCount = items.Count(item => item.Status == StatusUpdated),
            SkippedCount = items.Count(item => item.Status == StatusSkipped),
            AmbiguousCount = items.Count(item => item.Status == StatusAmbiguous),
            FailedCount = items.Count(item => item.Status == StatusFailed),
            DryRun = dryRun,
            CompletedAt = DateTime.UtcNow,
            Items = items.Take(MaxSummaryItems).ToList()
        };
    }

    private static bool HasSyncWork(RegionSyncSummaryDto summary) =>
        summary.CreatedCount > 0 ||
        summary.LinkedExistingCount > 0 ||
        summary.UpdatedCount > 0 ||
        summary.AmbiguousCount > 0 ||
        summary.FailedCount > 0;

    private static bool IsSyncedAdm1Region(RegionSyncRow region) =>
        region.BoundaryCandidateId.HasValue ||
        (!string.IsNullOrWhiteSpace(region.BoundaryExternalKey) &&
         string.Equals(region.BoundaryAdmLevel, Adm1Level, StringComparison.OrdinalIgnoreCase)) ||
        (string.Equals(region.BoundarySource, GeoBoundariesSource, StringComparison.OrdinalIgnoreCase) &&
         string.Equals(region.BoundaryAdmLevel, Adm1Level, StringComparison.OrdinalIgnoreCase));

    private static DateTime? GetLastSyncedAt(List<RegionSyncRow> syncedRegions)
    {
        var values = syncedRegions
            .Where(region => region.BoundaryImportedAt.HasValue)
            .Select(region => NormalizeDateTime(region.BoundaryImportedAt!.Value))
            .ToList();

        return values.Count == 0 ? null : values.Max();
    }

    private static string BuildSetupStatusMessage(
        bool missingCandidateData,
        bool requiresInitialSync,
        bool updateAvailable,
        RegionSyncSummaryDto? dryRunSummary)
    {
        if (missingCandidateData)
            return "ADM1 boundary data is missing for this territory. Ask an administrator to import boundary data.";

        if (requiresInitialSync)
            return "Initialize official ADM1 regions before managing assignments for this territory.";

        if (dryRunSummary is { FailedCount: > 0 } or { AmbiguousCount: > 0 })
            return "ADM1 boundary data needs attention before all regions can be synchronized safely.";

        if (updateAvailable)
            return "ADM1 boundary data has changed. Update regions to apply the latest boundaries.";

        return "Regions are up to date.";
    }

    private static string? BuildCandidateVersionHash(List<CandidateSyncRow> candidates)
    {
        if (candidates.Count == 0)
            return null;

        return ComputeVersionHash(candidates
            .OrderBy(candidate => candidate.ExternalKey, StringComparer.OrdinalIgnoreCase)
            .ThenBy(candidate => candidate.Id)
            .Select(candidate => string.Join('|',
                candidate.Id,
                candidate.ExternalKey,
                candidate.ShapeGroup,
                candidate.ShapeId,
                candidate.ShapeName,
                candidate.ShapeType,
                candidate.AdmLevel,
                NormalizeDateTime(candidate.UpdatedAt ?? candidate.ImportedAt).ToString("O"),
                candidate.BoundaryGeometry is null ? "no-boundary" : "has-boundary",
                candidate.BoundaryDisplayGeometry is null ? "no-display" : "has-display")));
    }

    private static string? BuildSyncedRegionVersionHash(List<RegionSyncRow> regions)
    {
        if (regions.Count == 0)
            return null;

        return ComputeVersionHash(regions
            .OrderBy(region => region.BoundaryExternalKey ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .ThenBy(region => region.Id)
            .Select(region => string.Join('|',
                region.Id,
                region.BoundaryCandidateId,
                region.BoundaryExternalKey,
                region.BoundaryShapeGroup,
                region.BoundaryShapeId,
                region.BoundaryShapeType,
                region.BoundaryAdmLevel,
                region.BoundarySource,
                region.BoundaryReleaseType,
                region.BoundaryImportedAt.HasValue
                    ? NormalizeDateTime(region.BoundaryImportedAt.Value).ToString("O")
                    : string.Empty,
                region.BoundaryGeometry is null ? "no-boundary" : "has-boundary",
                region.BoundaryDisplayGeometry is null ? "no-display" : "has-display")));
    }

    private static string ComputeVersionHash(IEnumerable<string> rows)
    {
        var payload = string.Join("\n", rows);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(payload));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string? ResolveCountryIso3(TerritorySyncRow territory)
    {
        return CountryRegistry.NormalizeIso3ForBoundaryImport(territory.CountryIso3)
            ?? CountryRegistry.NormalizeIso3ForBoundaryImport(territory.CountryCode)
            ?? CountryRegistry.ToIso3(territory.CountryCode);
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

            if (char.IsWhiteSpace(ch) ||
                ch is '-' or '_' ||
                CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.DashPunctuation)
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

    private static bool GeometriesMatch(Geometry? left, Geometry? right)
    {
        var normalizedLeft = ToValidPolygonalGeometry(left);
        var normalizedRight = ToValidPolygonalGeometry(right);

        return normalizedLeft is not null &&
            normalizedRight is not null &&
            normalizedLeft.EqualsTopologically(normalizedRight);
    }

    private static bool EqualsOrdinalIgnoreCase(string? left, string? right) =>
        string.Equals(
            NullIfWhiteSpace(left),
            NullIfWhiteSpace(right),
            StringComparison.OrdinalIgnoreCase);

    private static string? NullIfWhiteSpace(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static bool SameInstant(DateTime? left, DateTime right)
    {
        if (!left.HasValue)
            return false;

        var normalizedLeft = NormalizeDateTime(left.Value);
        var normalizedRight = NormalizeDateTime(right);
        return normalizedLeft == normalizedRight;
    }

    private static DateTime NormalizeDateTime(DateTime value) =>
        value.Kind == DateTimeKind.Unspecified
            ? DateTime.SpecifyKind(value, DateTimeKind.Utc)
            : value.ToUniversalTime();

    private static string BuildShapeKey(string? shapeGroup, string? shapeId, string? admLevel) =>
        $"{shapeGroup?.Trim().ToUpperInvariant()}::{shapeId?.Trim().ToUpperInvariant()}::{admLevel?.Trim().ToUpperInvariant()}";

    private sealed class TerritorySyncRow
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? CountryCode { get; set; }
        public string? CountryIso3 { get; set; }
    }

    private sealed class CandidateSyncRow
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
        public DateTime ImportedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
        public Geometry? BoundaryGeometry { get; set; }
        public Geometry? BoundaryDisplayGeometry { get; set; }
    }

    private sealed class RegionSyncRow
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public int TerritoryId { get; set; }
        public int? BoundaryCandidateId { get; set; }
        public string? BoundaryExternalKey { get; set; }
        public string? BoundaryShapeGroup { get; set; }
        public string? BoundaryShapeId { get; set; }
        public string? BoundaryShapeType { get; set; }
        public string? BoundaryAdmLevel { get; set; }
        public string? BoundarySource { get; set; }
        public string? BoundaryReleaseType { get; set; }
        public string? BoundaryLicense { get; set; }
        public DateTime? BoundaryImportedAt { get; set; }
        public Geometry? BoundaryGeometry { get; set; }
        public Geometry? BoundaryDisplayGeometry { get; set; }
    }

    private sealed class RegionLookup
    {
        public Dictionary<int, List<RegionSyncRow>> ByCandidateId { get; set; } = new();
        public Dictionary<string, List<RegionSyncRow>> ByExternalKey { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, List<RegionSyncRow>> ByShapeKey { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, List<RegionSyncRow>> ByNormalizedName { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private sealed record RegionUpdatePlan(
        bool MetadataChanged,
        bool UpdateBoundaryGeometry,
        bool UpdateDisplayGeometry)
    {
        public bool HasChanges => MetadataChanged || UpdateBoundaryGeometry || UpdateDisplayGeometry;
    }

    private sealed record RegionMatchResult(string Status, RegionSyncRow? Region, string Message)
    {
        public static RegionMatchResult Matched(RegionSyncRow region, string message) =>
            new("Matched", region, message);

        public static RegionMatchResult Ambiguous(string message) =>
            new(StatusAmbiguous, null, message);

        public static RegionMatchResult NoMatch() =>
            new("NoMatch", null, string.Empty);
    }
}
