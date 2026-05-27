using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;
using NetTopologySuite.Operation.Union;

namespace Geotrack.Infrastructure.Services;

public sealed class RegionalBoundaryGuard
{
    private const string BoundaryGeometryPropertyName = "BoundaryGeometry";
    private const string MissingTerritoryBoundaryMessage =
        "Assigned country/territory must have a boundary before managing regional content.";
    private static readonly GeometryFactory Wgs84GeometryFactory = new(new PrecisionModel(), 4326);

    private readonly AppDbContext _db;

    public RegionalBoundaryGuard(AppDbContext db)
    {
        _db = db;
    }

    internal static async Task<RegionalBoundaryScopeInfo> GetScopeAsync(AppDbContext db, int regionId)
    {
        if (regionId <= 0)
            throw new InvalidOperationException("regionId is required.");

        var scope = await db.Regions
            .AsNoTracking()
            .Where(region => region.Id == regionId)
            .Select(region => new RegionalBoundaryScopeInfo
            {
                RegionId = region.Id,
                RegionName = region.Name,
                TerritoryId = region.TerritoryId,
                TerritoryName = region.Territory.Name,
                RegionBoundary = EF.Property<Geometry?>(region, BoundaryGeometryPropertyName),
                TerritoryBoundary = EF.Property<Geometry?>(region.Territory, BoundaryGeometryPropertyName)
            })
            .SingleOrDefaultAsync()
            ?? throw new KeyNotFoundException("Region not found.");

        scope.RegionBoundary = ToPolygonalGeometry(scope.RegionBoundary);
        scope.TerritoryBoundary = ToPolygonalGeometry(scope.TerritoryBoundary);
        return scope;
    }

    internal static void EnsurePointInsideScope(
        double latitude,
        double longitude,
        RegionalBoundaryScopeInfo scope,
        string subject)
    {
        if (!double.IsFinite(latitude) || latitude is < -90d or > 90d ||
            !double.IsFinite(longitude) || longitude is < -180d or > 180d)
        {
            throw new InvalidOperationException($"{subject} coordinates are invalid.");
        }

        var point = Wgs84GeometryFactory.CreatePoint(new Coordinate(longitude, latitude));
        point.SRID = 4326;

        if (scope.TerritoryBoundary is not null && !scope.TerritoryBoundary.Covers(point))
            throw new InvalidOperationException($"{subject} must be inside the assigned country/territory.");

        if (scope.RegionBoundary is not null && !scope.RegionBoundary.Covers(point))
            throw new InvalidOperationException($"{subject} must be inside the assigned region boundary.");
    }

    public async Task ValidatePointInsideAssignedScopeAsync(
        int regionId,
        double latitude,
        double longitude,
        string contentLabel,
        CancellationToken cancellationToken = default)
    {
        var scope = await GetScopeAsync(_db, regionId, cancellationToken);
        EnsurePointInsideScope(latitude, longitude, scope, contentLabel);
    }

    public async Task ValidateGeometryInsideAssignedScopeAsync(
        int regionId,
        Geometry geometry,
        string contentLabel,
        CancellationToken cancellationToken = default)
    {
        if (geometry.IsEmpty || !geometry.IsValid)
            throw new InvalidOperationException($"{contentLabel} boundary geometry is invalid.");

        var scope = await GetScopeAsync(_db, regionId, cancellationToken);
        var polygonalGeometry = ToPolygonalGeometry(geometry)
            ?? throw new InvalidOperationException($"{contentLabel} boundary geometry is invalid.");

        EnsureGeometryInsideScope(polygonalGeometry, scope, contentLabel);
    }

    internal static void EnsureGeometryInsideScope(
        Geometry geometry,
        RegionalBoundaryScopeInfo scope,
        string subject)
    {
        var polygonalGeometry = ToPolygonalGeometry(geometry)
            ?? throw new InvalidOperationException($"{subject} boundary geometry is invalid.");

        if (scope.TerritoryBoundary is not null && !scope.TerritoryBoundary.Covers(polygonalGeometry))
            throw new InvalidOperationException($"{subject} boundary must be inside the assigned country/territory.");

        if (scope.RegionBoundary is not null && !scope.RegionBoundary.Covers(polygonalGeometry))
            throw new InvalidOperationException($"{subject} boundary must be inside the assigned region boundary.");
    }

    public static async Task<RegionalBoundaryScope> GetScopeAsync(
        AppDbContext db,
        int regionId,
        CancellationToken cancellationToken = default)
    {
        var scope = await db.Regions
            .AsNoTracking()
            .Where(region => region.Id == regionId)
            .Select(region => new
            {
                RegionBoundary = EF.Property<Geometry?>(region, BoundaryGeometryPropertyName),
                TerritoryBoundary = EF.Property<Geometry?>(region.Territory, BoundaryGeometryPropertyName)
            })
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new KeyNotFoundException("Region not found.");

        var territoryBoundary = ToPolygonalGeometry(scope.TerritoryBoundary);
        if (territoryBoundary is null)
            throw new InvalidOperationException(MissingTerritoryBoundaryMessage);

        return new RegionalBoundaryScope(
            territoryBoundary,
            ToPolygonalGeometry(scope.RegionBoundary));
    }

    public static void EnsurePointInsideScope(
        double latitude,
        double longitude,
        RegionalBoundaryScope scope,
        string contentLabel)
    {
        var point = Wgs84GeometryFactory.CreatePoint(new Coordinate(longitude, latitude));
        point.SRID = 4326;

        if (!scope.TerritoryBoundary.Covers(point))
        {
            throw new InvalidOperationException($"{contentLabel} coordinates must be inside the assigned country/territory.");
        }

        if (scope.RegionBoundary is not null && !scope.RegionBoundary.Covers(point))
        {
            throw new InvalidOperationException($"{contentLabel} coordinates must be inside the assigned region.");
        }
    }

    public static void EnsureGeometryInsideScope(
        Geometry geometry,
        RegionalBoundaryScope scope,
        string contentLabel)
    {
        var polygonalGeometry = ToPolygonalGeometry(geometry)
            ?? throw new InvalidOperationException($"{contentLabel} boundary geometry is invalid.");

        if (!scope.TerritoryBoundary.Covers(polygonalGeometry))
        {
            throw new InvalidOperationException($"{contentLabel} boundary must be inside the assigned country/territory.");
        }

        if (scope.RegionBoundary is not null && !scope.RegionBoundary.Covers(polygonalGeometry))
        {
            throw new InvalidOperationException($"{contentLabel} boundary must be inside the assigned region.");
        }
    }

    private static Geometry? ToPolygonalGeometry(Geometry? geometry)
    {
        if (geometry is null || geometry.IsEmpty)
            return null;

        var copy = geometry.Copy();
        copy.SRID = copy.SRID > 0 ? copy.SRID : 4326;

        if (!copy.IsValid)
            return null;

        if (copy is Polygon or MultiPolygon)
            return copy;

        if (copy is GeometryCollection collection)
        {
            var polygons = Enumerable.Range(0, collection.NumGeometries)
                .Select(collection.GetGeometryN)
                .Where(item => item is Polygon or MultiPolygon)
                .ToList();

            if (polygons.Count == 0)
                return null;

            return polygons.Count == 1
                ? polygons[0]
                : ToPolygonalGeometry(UnaryUnionOp.Union(polygons));
        }

        return null;
    }

    public sealed record RegionalBoundaryScope(Geometry TerritoryBoundary, Geometry? RegionBoundary);
}

internal sealed class RegionalBoundaryScopeInfo
{
    public int RegionId { get; set; }
    public string RegionName { get; set; } = string.Empty;
    public int TerritoryId { get; set; }
    public string TerritoryName { get; set; } = string.Empty;
    public Geometry? RegionBoundary { get; set; }
    public Geometry? TerritoryBoundary { get; set; }
}
