using Geotrack.Application.DTOs.Admin;
using Geotrack.Application.Interfaces.Service;
using Geotrack.Application.Options;
using Geotrack.Application.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NetTopologySuite.Geometries;
using NetTopologySuite.Operation.Union;

namespace Geotrack.Infrastructure.Services;

public sealed class RegionalScopeValidationService : IRegionalScopeValidationService
{
    private const string BoundaryGeometryPropertyName = "BoundaryGeometry";
    private const double EarthRadiusMeters = 6371008.8d;
    private const string AllowedInsideAssignedRegionMessage = "This location is inside your assigned region.";
    private const string AllowedToleranceMessage = "This location is slightly outside your assigned region but remains inside your assigned country/territory and does not belong to another region, so it is allowed.";
    private const string RejectedOutsideCountryMessage = "This location is outside your assigned country/territory.";
    private const string RejectedInsideOtherRegionMessage = "This location belongs to another region and cannot be managed from your account.";
    private const string RejectedTooFarMessage = "This location is outside your assigned region.";
    private const string MissingRegionBoundaryMessage = "Assigned region boundary is missing. Please contact an administrator.";
    private const string MissingCountryBoundaryMessage = "Assigned country/territory boundary is missing. Please contact an administrator.";
    private static readonly GeometryFactory Wgs84GeometryFactory = new(new PrecisionModel(), 4326);

    private readonly AppDbContext _db;
    private readonly RegionalScopeValidationOptions _options;

    public RegionalScopeValidationService(
        AppDbContext db,
        IOptions<RegionalScopeValidationOptions> options)
    {
        _db = db;
        _options = options.Value;
    }

    public async Task<RegionalScopeValidationResultDto> ValidatePointAsync(
        int userId,
        int regionId,
        string entityType,
        double latitude,
        double longitude,
        string operationType,
        CancellationToken cancellationToken = default)
    {
        var label = NormalizeEntityLabel(entityType);
        if (regionId <= 0)
            return RejectedMissingAssignedRegion(label);

        if (!HasValidCoordinates(latitude, longitude))
            return RejectedTooFar(label, null, $"{label} coordinates are invalid.");

        var scope = await LoadAssignedScopeAsync(userId, regionId, cancellationToken);
        var missingBoundary = ValidateScopeBoundaries(scope, label);
        if (missingBoundary is not null)
            return missingBoundary;

        var point = Wgs84GeometryFactory.CreatePoint(new Coordinate(longitude, latitude));
        point.SRID = 4326;

        if (!scope.TerritoryBoundary!.Covers(point))
        {
            return Rejected(
                RegionalScopeValidationModes.RejectedOutsideAssignedCountry,
                label,
                scope,
                RejectedOutsideCountryMessage);
        }

        if (scope.RegionBoundary!.Covers(point))
        {
            return Allowed(
                RegionalScopeValidationModes.InsideAssignedRegion,
                label,
                scope,
                null,
                AllowedInsideAssignedRegionMessage);
        }

        var distanceMeters = DistanceToBoundaryMeters(scope.RegionBoundary, point);
        if (distanceMeters > BoundaryToleranceMeters)
        {
            return RejectedTooFar(
                label,
                scope,
                RejectedTooFarMessage,
                distanceMeters);
        }

        var otherRegion = await FindOtherRegionContainingPointAsync(scope.RegionId, point, cancellationToken);
        if (otherRegion is not null)
        {
            return RejectedInsideOtherRegion(
                label,
                scope,
                otherRegion,
                distanceMeters);
        }

        return Allowed(
            RegionalScopeValidationModes.AllowedWithinToleranceInsideAssignedCountry,
            label,
            scope,
            distanceMeters,
            AllowedToleranceMessage);
    }

    public async Task<RegionalScopeValidationResultDto> ValidateGeometryAsync(
        int userId,
        int regionId,
        string entityType,
        Geometry geometry,
        string operationType,
        CancellationToken cancellationToken = default)
    {
        var label = NormalizeEntityLabel(entityType);
        if (regionId <= 0)
            return RejectedMissingAssignedRegion(label);

        var polygonalGeometry = ToPolygonalGeometry(geometry);
        if (polygonalGeometry is null)
            return RejectedTooFar(label, null, $"{label} boundary geometry is invalid.");

        var scope = await LoadAssignedScopeAsync(userId, regionId, cancellationToken);
        var missingBoundary = ValidateScopeBoundaries(scope, label);
        if (missingBoundary is not null)
            return missingBoundary;

        if (!scope.TerritoryBoundary!.Covers(polygonalGeometry))
        {
            return Rejected(
                RegionalScopeValidationModes.RejectedOutsideAssignedCountry,
                label,
                scope,
                "This boundary is outside your assigned country/territory.");
        }

        if (scope.RegionBoundary!.Covers(polygonalGeometry))
        {
            return Allowed(
                RegionalScopeValidationModes.InsideAssignedRegion,
                label,
                scope,
                null,
                $"{label} boundary is inside the assigned region.");
        }

        var distanceMeters = MaxOutsideDistanceToBoundaryMeters(scope.RegionBoundary, polygonalGeometry);
        if (distanceMeters > BoundaryToleranceMeters)
        {
            return RejectedTooFar(
                label,
                scope,
                "This boundary is outside your assigned region.",
                distanceMeters);
        }

        var otherRegion = await FindOtherRegionIntersectingGeometryAsync(
            scope.RegionId,
            polygonalGeometry,
            cancellationToken);
        if (otherRegion is not null)
        {
            return RejectedInsideOtherRegion(
                label,
                scope,
                otherRegion,
                distanceMeters);
        }

        return Allowed(
            RegionalScopeValidationModes.AllowedWithinToleranceInsideAssignedCountry,
            label,
            scope,
            distanceMeters,
            "This boundary is slightly outside your assigned region but remains inside your assigned country/territory and does not belong to another region, so it is allowed.");
    }

    public async Task EnsurePointAllowedAsync(
        int userId,
        int regionId,
        string entityType,
        double latitude,
        double longitude,
        string operationType,
        CancellationToken cancellationToken = default)
    {
        var result = await ValidatePointAsync(
            userId,
            regionId,
            entityType,
            latitude,
            longitude,
            operationType,
            cancellationToken);
        ThrowIfRejected(result);
    }

    public async Task EnsureGeometryAllowedAsync(
        int userId,
        int regionId,
        string entityType,
        Geometry geometry,
        string operationType,
        CancellationToken cancellationToken = default)
    {
        var result = await ValidateGeometryAsync(
            userId,
            regionId,
            entityType,
            geometry,
            operationType,
            cancellationToken);
        ThrowIfRejected(result);
    }

    private async Task<AssignedRegionalScope> LoadAssignedScopeAsync(
        int userId,
        int regionId,
        CancellationToken cancellationToken)
    {
        await EnsureRegionalManagerRoleAsync(userId, cancellationToken);

        var scope = await _db.RegionalManagerRegions
            .AsNoTracking()
            .Where(assignment =>
                assignment.UserId == userId &&
                assignment.RegionId == regionId &&
                assignment.IsActive)
            .Select(assignment => new AssignedRegionalScope
            {
                RegionId = assignment.RegionId,
                RegionName = assignment.Region.Name,
                RegionBoundary = EF.Property<Geometry?>(assignment.Region, BoundaryGeometryPropertyName),
                TerritoryId = assignment.Region.TerritoryId,
                TerritoryName = assignment.Region.Territory.Name,
                TerritoryBoundary = EF.Property<Geometry?>(assignment.Region.Territory, BoundaryGeometryPropertyName)
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (scope is null)
            throw new UnauthorizedAccessException("Region is not assigned to the current regional manager.");

        scope.RegionBoundary = ToPolygonalGeometry(scope.RegionBoundary);
        scope.TerritoryBoundary = ToPolygonalGeometry(scope.TerritoryBoundary);
        return scope;
    }

    private async Task EnsureRegionalManagerRoleAsync(int userId, CancellationToken cancellationToken)
    {
        var roles = await _db.UserRoles
            .AsNoTracking()
            .Where(userRole => userRole.UserId == userId)
            .Select(userRole => userRole.Role.Name)
            .ToListAsync(cancellationToken);

        if (!roles.Any(role => RoleNames.Normalize(role) == RoleNames.RegionalManager))
            throw new UnauthorizedAccessException("Regional manager role is required.");
    }

    private async Task<MatchedRegion?> FindOtherRegionContainingPointAsync(
        int assignedRegionId,
        Point point,
        CancellationToken cancellationToken)
    {
        var row = await _db.Regions
            .AsNoTracking()
            .Where(region => region.Id != assignedRegionId)
            .Where(region => EF.Property<Geometry?>(region, BoundaryGeometryPropertyName) != null)
            .Where(region => EF.Property<Geometry>(region, BoundaryGeometryPropertyName).Covers(point))
            .Select(region => new MatchedRegion(region.Id, region.Name))
            .FirstOrDefaultAsync(cancellationToken);

        return row;
    }

    private async Task<MatchedRegion?> FindOtherRegionIntersectingGeometryAsync(
        int assignedRegionId,
        Geometry geometry,
        CancellationToken cancellationToken)
    {
        var row = await _db.Regions
            .AsNoTracking()
            .Where(region => region.Id != assignedRegionId)
            .Where(region => EF.Property<Geometry?>(region, BoundaryGeometryPropertyName) != null)
            .Where(region => EF.Property<Geometry>(region, BoundaryGeometryPropertyName).Intersects(geometry))
            .Select(region => new MatchedRegion(region.Id, region.Name))
            .FirstOrDefaultAsync(cancellationToken);

        return row;
    }

    private RegionalScopeValidationResultDto? ValidateScopeBoundaries(
        AssignedRegionalScope scope,
        string label)
    {
        if (scope.RegionBoundary is null)
        {
            return Rejected(
                RegionalScopeValidationModes.RejectedMissingRegionBoundary,
                label,
                scope,
                MissingRegionBoundaryMessage);
        }

        if (scope.TerritoryBoundary is null)
        {
            return Rejected(
                RegionalScopeValidationModes.RejectedMissingAssignedCountryBoundary,
                label,
                scope,
                MissingCountryBoundaryMessage);
        }

        return null;
    }

    private double BoundaryToleranceMeters => Math.Max(0d, _options.BoundaryToleranceMeters);

    private static RegionalScopeValidationResultDto Allowed(
        string validationMode,
        string label,
        AssignedRegionalScope scope,
        double? distanceMeters,
        string message)
    {
        return new RegionalScopeValidationResultDto
        {
            IsAllowed = true,
            ValidationMode = validationMode,
            AssignedRegionId = scope.RegionId,
            AssignedRegionName = scope.RegionName,
            AssignedCountryOrTerritoryId = scope.TerritoryId,
            AssignedCountryOrTerritoryName = scope.TerritoryName,
            DistanceFromAssignedRegionMeters = RoundMeters(distanceMeters),
            Message = message
        };
    }

    private static RegionalScopeValidationResultDto Rejected(
        string validationMode,
        string label,
        AssignedRegionalScope? scope,
        string message,
        double? distanceMeters = null)
    {
        return new RegionalScopeValidationResultDto
        {
            IsAllowed = false,
            ValidationMode = validationMode,
            AssignedRegionId = scope?.RegionId,
            AssignedRegionName = scope?.RegionName,
            AssignedCountryOrTerritoryId = scope?.TerritoryId,
            AssignedCountryOrTerritoryName = scope?.TerritoryName,
            DistanceFromAssignedRegionMeters = RoundMeters(distanceMeters),
            Message = message
        };
    }

    private static RegionalScopeValidationResultDto RejectedMissingAssignedRegion(string label)
    {
        return Rejected(
            RegionalScopeValidationModes.RejectedMissingAssignedRegion,
            label,
            null,
            "Assigned region is missing. Please select one of your assigned regions.");
    }

    private static RegionalScopeValidationResultDto RejectedTooFar(
        string label,
        AssignedRegionalScope? scope,
        string message,
        double? distanceMeters = null)
    {
        return Rejected(
            RegionalScopeValidationModes.RejectedTooFarOutsideRegion,
            label,
            scope,
            message,
            distanceMeters);
    }

    private static RegionalScopeValidationResultDto RejectedInsideOtherRegion(
        string label,
        AssignedRegionalScope scope,
        MatchedRegion otherRegion,
        double distanceMeters)
    {
        return new RegionalScopeValidationResultDto
        {
            IsAllowed = false,
            ValidationMode = RegionalScopeValidationModes.RejectedInsideAnotherRegion,
            AssignedRegionId = scope.RegionId,
            AssignedRegionName = scope.RegionName,
            AssignedCountryOrTerritoryId = scope.TerritoryId,
            AssignedCountryOrTerritoryName = scope.TerritoryName,
            MatchedOtherRegionId = otherRegion.Id,
            MatchedOtherRegionName = otherRegion.Name,
            DistanceFromAssignedRegionMeters = RoundMeters(distanceMeters),
            Message = RejectedInsideOtherRegionMessage
        };
    }

    private static void ThrowIfRejected(RegionalScopeValidationResultDto result)
    {
        if (!result.IsAllowed)
            throw new InvalidOperationException(result.Message);
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

    private static bool HasValidCoordinates(double latitude, double longitude)
    {
        return double.IsFinite(latitude) &&
            double.IsFinite(longitude) &&
            latitude is >= -90d and <= 90d &&
            longitude is >= -180d and <= 180d &&
            (latitude != 0d || longitude != 0d);
    }

    private static double DistanceToBoundaryMeters(Geometry boundary, Point point)
    {
        if (boundary.Covers(point))
            return 0d;

        return MinDistanceToGeometryMeters(point.Coordinate, boundary.Boundary);
    }

    private static double MaxOutsideDistanceToBoundaryMeters(Geometry boundary, Geometry geometry)
    {
        var coordinates = geometry.Coordinates;
        if (coordinates.Length == 0)
            return DistanceToBoundaryMeters(boundary, geometry.Centroid);

        var maxDistance = 0d;
        var measuredOutsidePoint = false;
        foreach (var coordinate in coordinates)
        {
            var point = Wgs84GeometryFactory.CreatePoint(new Coordinate(coordinate.X, coordinate.Y));
            point.SRID = 4326;
            if (boundary.Covers(point))
                continue;

            measuredOutsidePoint = true;
            maxDistance = Math.Max(maxDistance, MinDistanceToGeometryMeters(coordinate, boundary.Boundary));
        }

        if (measuredOutsidePoint)
            return maxDistance;

        return DistanceToBoundaryMeters(boundary, geometry.Centroid);
    }

    private static double MinDistanceToGeometryMeters(Coordinate point, Geometry geometry)
    {
        var minDistance = double.PositiveInfinity;
        MeasureDistanceToGeometry(point, geometry, ref minDistance);
        return double.IsFinite(minDistance) ? minDistance : HaversineMeters(point, geometry.Centroid.Coordinate);
    }

    private static void MeasureDistanceToGeometry(Coordinate point, Geometry geometry, ref double minDistance)
    {
        switch (geometry)
        {
            case LineString lineString:
                MeasureDistanceToLineString(point, lineString, ref minDistance);
                break;
            case Polygon polygon:
                MeasureDistanceToGeometry(point, polygon.Boundary, ref minDistance);
                break;
            case GeometryCollection collection:
                for (var index = 0; index < collection.NumGeometries; index++)
                {
                    MeasureDistanceToGeometry(point, collection.GetGeometryN(index), ref minDistance);
                }
                break;
            default:
                foreach (var coordinate in geometry.Coordinates)
                {
                    minDistance = Math.Min(minDistance, HaversineMeters(point, coordinate));
                }
                break;
        }
    }

    private static void MeasureDistanceToLineString(Coordinate point, LineString lineString, ref double minDistance)
    {
        var coordinates = lineString.Coordinates;
        if (coordinates.Length == 1)
        {
            minDistance = Math.Min(minDistance, HaversineMeters(point, coordinates[0]));
            return;
        }

        for (var index = 1; index < coordinates.Length; index++)
        {
            minDistance = Math.Min(
                minDistance,
                DistancePointToSegmentMeters(point, coordinates[index - 1], coordinates[index]));
        }
    }

    private static double DistancePointToSegmentMeters(Coordinate point, Coordinate start, Coordinate end)
    {
        var originLatitudeRadians = ToRadians(point.Y);
        var pointProjected = Project(point, originLatitudeRadians);
        var startProjected = Project(start, originLatitudeRadians);
        var endProjected = Project(end, originLatitudeRadians);

        var segmentX = endProjected.X - startProjected.X;
        var segmentY = endProjected.Y - startProjected.Y;
        var lengthSquared = segmentX * segmentX + segmentY * segmentY;
        if (lengthSquared <= 0d)
            return HaversineMeters(point, start);

        var t = ((pointProjected.X - startProjected.X) * segmentX +
                 (pointProjected.Y - startProjected.Y) * segmentY) / lengthSquared;
        t = Math.Clamp(t, 0d, 1d);

        var closestX = startProjected.X + t * segmentX;
        var closestY = startProjected.Y + t * segmentY;
        var dx = pointProjected.X - closestX;
        var dy = pointProjected.Y - closestY;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    private static (double X, double Y) Project(Coordinate coordinate, double originLatitudeRadians)
    {
        return (
            EarthRadiusMeters * ToRadians(coordinate.X) * Math.Cos(originLatitudeRadians),
            EarthRadiusMeters * ToRadians(coordinate.Y));
    }

    private static double HaversineMeters(Coordinate first, Coordinate second)
    {
        var firstLat = ToRadians(first.Y);
        var secondLat = ToRadians(second.Y);
        var deltaLat = secondLat - firstLat;
        var deltaLon = ToRadians(second.X - first.X);
        var a = Math.Sin(deltaLat / 2d) * Math.Sin(deltaLat / 2d) +
            Math.Cos(firstLat) * Math.Cos(secondLat) *
            Math.Sin(deltaLon / 2d) * Math.Sin(deltaLon / 2d);
        var c = 2d * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1d - a));
        return EarthRadiusMeters * c;
    }

    private static double ToRadians(double degrees) => degrees * Math.PI / 180d;

    private static double? RoundMeters(double? meters) =>
        meters.HasValue ? Math.Round(meters.Value, 2, MidpointRounding.AwayFromZero) : null;

    private static string NormalizeEntityLabel(string? entityType)
    {
        var normalized = entityType?.Trim();
        return string.IsNullOrWhiteSpace(normalized) ? "Location" : normalized;
    }

    private sealed class AssignedRegionalScope
    {
        public int RegionId { get; set; }
        public string RegionName { get; set; } = string.Empty;
        public Geometry? RegionBoundary { get; set; }
        public int TerritoryId { get; set; }
        public string TerritoryName { get; set; } = string.Empty;
        public Geometry? TerritoryBoundary { get; set; }
    }

    private sealed record MatchedRegion(int Id, string Name);
}
