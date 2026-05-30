using Geotrack.Application.DTOs.Admin;
using Geotrack.Application.Interfaces.Service;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NetTopologySuite.Geometries;
using Geometry = NetTopologySuite.Geometries.Geometry;

namespace Geotrack.Infrastructure.Services;

public sealed class RegionContentAssignmentService : IRegionContentAssignmentService
{
    private const string BoundaryGeometryPropertyName = "BoundaryGeometry";
    private const string GeoBoundariesSource = "geoBoundaries";
    private const string Adm1Level = "ADM1";

    private const string StatusAlreadyAssigned = "AlreadyAssigned";
    private const string StatusWouldUpdate = "WouldUpdate";
    private const string StatusUpdated = "Updated";
    private const string StatusSkipped = "Skipped";
    private const string StatusAmbiguous = "Ambiguous";
    private const string StatusFailed = "Failed";

    private static readonly GeometryFactory Wgs84GeometryFactory = new(new PrecisionModel(), 4326);

    private readonly AppDbContext _db;
    private readonly ILogger<RegionContentAssignmentService> _logger;

    public RegionContentAssignmentService(
        AppDbContext db,
        ILogger<RegionContentAssignmentService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<RegionContentAssignmentSummaryDto> AssignContentByBoundaryAsync(
        int territoryId,
        bool dryRun = true,
        CancellationToken cancellationToken = default)
    {
        if (territoryId <= 0)
            throw new InvalidOperationException("territoryId must be positive.");

        var territory = await _db.Territories
            .AsNoTracking()
            .Where(item => item.Id == territoryId)
            .Select(item => new TerritoryAssignmentRow
            {
                Id = item.Id,
                Name = item.Name
            })
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new KeyNotFoundException("Territory was not found.");

        var regions = await LoadRegionsAsync(territoryId, cancellationToken);
        if (regions.Count == 0)
            throw new InvalidOperationException("Territory has no synced ADM1 region boundaries.");

        var destinations = await _db.Destinations
            .AsNoTracking()
            .Where(destination => destination.Region.TerritoryId == territoryId)
            .OrderBy(destination => destination.Name)
            .ThenBy(destination => destination.Id)
            .Select(destination => new DestinationAssignmentRow
            {
                Id = destination.Id,
                Name = destination.Name,
                Latitude = destination.Latitude,
                Longitude = destination.Longitude,
                CurrentRegionId = destination.RegionId,
                CurrentRegionName = destination.Region.Name
            })
            .ToListAsync(cancellationToken);

        var items = new List<RegionContentAssignmentItemDto>();
        foreach (var destination in destinations)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var item = BuildBaseItem(destination);
            items.Add(item);

            var matchedRegion = MatchRegion(destination.Latitude, destination.Longitude, regions);
            if (matchedRegion.Status == StatusAmbiguous)
            {
                item.Status = StatusAmbiguous;
                item.Message = matchedRegion.Message;
                continue;
            }

            if (matchedRegion.Region is null)
            {
                item.Status = StatusSkipped;
                item.Message = "No synced ADM1 region boundary covers this destination point.";
                continue;
            }

            item.MatchedRegionId = matchedRegion.Region.Id;
            item.MatchedRegionName = matchedRegion.Region.Name;

            if (destination.CurrentRegionId == matchedRegion.Region.Id)
            {
                item.Status = StatusAlreadyAssigned;
                item.Message = "Destination is already assigned to the ADM1 region that covers its coordinates.";
                continue;
            }

            item.Status = dryRun ? StatusWouldUpdate : StatusUpdated;
            item.Message = dryRun
                ? "Destination RegionId would be updated to the ADM1 region that covers its coordinates."
                : "Destination RegionId was updated to the ADM1 region that covers its coordinates.";

            if (dryRun)
            {
                continue;
            }

            try
            {
                var tracked = await _db.Destinations.SingleAsync(
                    existing => existing.Id == destination.Id,
                    cancellationToken);
                tracked.RegionId = matchedRegion.Region.Id;
                tracked.UpdatedAt = DateTime.UtcNow;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Failed to assign Destination {DestinationId} to Region {RegionId}.",
                    destination.Id,
                    matchedRegion.Region.Id);

                item.Status = StatusFailed;
                item.Message = "Failed to update destination region assignment.";
            }
        }

        if (!dryRun && _db.ChangeTracker.HasChanges())
        {
            await _db.SaveChangesAsync(cancellationToken);
        }

        return BuildSummary(territory, dryRun, items);
    }

    private async Task<List<RegionBoundaryRow>> LoadRegionsAsync(
        int territoryId,
        CancellationToken cancellationToken)
    {
        var rows = await _db.Regions
            .AsNoTracking()
            .Where(region =>
                region.TerritoryId == territoryId &&
                region.BoundaryCandidateId != null &&
                region.BoundarySource == GeoBoundariesSource &&
                region.BoundaryAdmLevel == Adm1Level)
            .OrderBy(region => region.Name)
            .ThenBy(region => region.Id)
            .Select(region => new RegionBoundaryRow
            {
                Id = region.Id,
                Name = region.Name,
                BoundaryGeometry = EF.Property<Geometry?>(region, BoundaryGeometryPropertyName)
            })
            .ToListAsync(cancellationToken);

        return rows
            .Where(row => ToUsableBoundary(row.BoundaryGeometry) is not null)
            .ToList();
    }

    private static RegionMatchResult MatchRegion(
        double latitude,
        double longitude,
        IReadOnlyList<RegionBoundaryRow> regions)
    {
        var point = Wgs84GeometryFactory.CreatePoint(new Coordinate(longitude, latitude));
        point.SRID = 4326;

        var matches = regions
            .Where(region =>
            {
                var boundary = ToUsableBoundary(region.BoundaryGeometry);
                return boundary is not null && boundary.Covers(point);
            })
            .Take(2)
            .ToList();

        return matches.Count switch
        {
            1 => RegionMatchResult.Matched(matches[0]),
            > 1 => RegionMatchResult.Ambiguous("More than one synced ADM1 region boundary covers this destination point."),
            _ => RegionMatchResult.NoMatch()
        };
    }

    private static Geometry? ToUsableBoundary(Geometry? geometry)
    {
        if (geometry is null || geometry.IsEmpty)
            return null;

        var copy = geometry.Copy();
        copy.SRID = 4326;

        if (!copy.IsValid)
        {
            copy = copy.Buffer(0);
            copy.SRID = 4326;
        }

        return copy is Polygon or MultiPolygon && !copy.IsEmpty && copy.IsValid
            ? copy
            : null;
    }

    private static RegionContentAssignmentItemDto BuildBaseItem(DestinationAssignmentRow destination)
    {
        return new RegionContentAssignmentItemDto
        {
            ContentType = "Destination",
            ContentId = destination.Id,
            ContentName = destination.Name,
            Latitude = destination.Latitude,
            Longitude = destination.Longitude,
            CurrentRegionId = destination.CurrentRegionId,
            CurrentRegionName = destination.CurrentRegionName,
            Status = StatusSkipped
        };
    }

    private static RegionContentAssignmentSummaryDto BuildSummary(
        TerritoryAssignmentRow territory,
        bool dryRun,
        List<RegionContentAssignmentItemDto> items)
    {
        return new RegionContentAssignmentSummaryDto
        {
            TerritoryId = territory.Id,
            TerritoryName = territory.Name,
            TotalContentChecked = items.Count,
            AlreadyAssignedCount = items.Count(item => item.Status == StatusAlreadyAssigned),
            WouldUpdateCount = items.Count(item => item.Status == StatusWouldUpdate),
            UpdatedCount = items.Count(item => item.Status == StatusUpdated),
            SkippedCount = items.Count(item => item.Status == StatusSkipped),
            AmbiguousCount = items.Count(item => item.Status == StatusAmbiguous),
            FailedCount = items.Count(item => item.Status == StatusFailed),
            DryRun = dryRun,
            Items = items
        };
    }

    private sealed class TerritoryAssignmentRow
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
    }

    private sealed class RegionBoundaryRow
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public Geometry? BoundaryGeometry { get; set; }
    }

    private sealed class DestinationAssignmentRow
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public int CurrentRegionId { get; set; }
        public string CurrentRegionName { get; set; } = string.Empty;
    }

    private sealed record RegionMatchResult(string Status, RegionBoundaryRow? Region, string Message)
    {
        public static RegionMatchResult Matched(RegionBoundaryRow region) =>
            new("Matched", region, string.Empty);

        public static RegionMatchResult Ambiguous(string message) =>
            new(StatusAmbiguous, null, message);

        public static RegionMatchResult NoMatch() =>
            new("NoMatch", null, string.Empty);
    }
}
