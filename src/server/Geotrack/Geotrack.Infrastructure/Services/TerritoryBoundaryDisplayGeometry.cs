using NetTopologySuite.Geometries;
using NetTopologySuite.Operation.Union;
using NetTopologySuite.Simplify;

namespace Geotrack.Infrastructure.Services;

internal static class TerritoryBoundaryDisplayGeometry
{
    public const string BoundaryDisplayGeometryPropertyName = "BoundaryDisplayGeometry";

    private const double DisplaySimplificationTolerance = 0.05d;
    private const double MinimumAreaRetentionRatio = 0.85d;
    private const double MinimumLargestPolygonRetentionRatio = 0.85d;
    private const double AreaComparisonTolerance = 0.0000001d;

    public static Geometry? Create(Geometry? source)
    {
        var polygonal = ToValidPolygonalGeometry(source, copyWhenAlreadyValid: false);
        if (polygonal is null)
            return null;

        try
        {
            var simplified = TopologyPreservingSimplifier.Simplify(
                polygonal,
                DisplaySimplificationTolerance);
            var display = ToValidPolygonalGeometry(simplified);
            return IsMeaningfulDisplayGeometry(polygonal, display)
                ? display
                : polygonal;
        }
        catch
        {
            return polygonal;
        }
    }

    public static bool IsValid(Geometry? geometry) =>
        ToValidPolygonalGeometry(geometry) is not null;

    public static bool IsValid(Geometry? source, Geometry? display)
    {
        var sourcePolygonal = ToValidPolygonalGeometry(source);
        if (sourcePolygonal is null)
            return false;

        var displayPolygonal = ToValidPolygonalGeometry(display);
        return IsMeaningfulDisplayGeometry(sourcePolygonal, displayPolygonal);
    }

    public static int CountCoordinates(Geometry? geometry)
    {
        var polygonal = ToValidPolygonalGeometry(geometry);
        return polygonal?.Coordinates.Length ?? 0;
    }

    private static bool IsMeaningfulDisplayGeometry(Geometry source, Geometry? display)
    {
        if (display is null || display.IsEmpty || !display.IsValid)
            return false;

        if (!RetainsMeaningfulArea(source.Area, display.Area, MinimumAreaRetentionRatio))
            return false;

        var sourceLargestPolygonArea = LargestPolygonArea(source);
        var displayLargestPolygonArea = LargestPolygonArea(display);
        return RetainsMeaningfulArea(
            sourceLargestPolygonArea,
            displayLargestPolygonArea,
            MinimumLargestPolygonRetentionRatio);
    }

    private static bool RetainsMeaningfulArea(double sourceArea, double displayArea, double ratio)
    {
        if (sourceArea <= AreaComparisonTolerance)
            return displayArea >= -AreaComparisonTolerance;

        return displayArea + AreaComparisonTolerance >= sourceArea * ratio;
    }

    private static double LargestPolygonArea(Geometry geometry) =>
        FlattenPolygons(geometry)
            .Select(polygon => polygon.Area)
            .DefaultIfEmpty(0d)
            .Max();

    private static Geometry? ToValidPolygonalGeometry(
        Geometry? geometry,
        bool copyWhenAlreadyValid = true)
    {
        if (geometry is null || geometry.IsEmpty)
            return null;

        geometry.SRID = geometry.SRID > 0 ? geometry.SRID : 4326;
        if (geometry.SRID == 4326 &&
            geometry.IsValid &&
            geometry is Polygon or MultiPolygon)
        {
            return copyWhenAlreadyValid ? geometry.Copy() : geometry;
        }

        var copy = geometry.Copy();
        copy.SRID = copy.SRID > 0 ? copy.SRID : 4326;

        if (copy.SRID != 4326)
            return null;

        if (!copy.IsValid)
        {
            copy = copy.Buffer(0);
            copy.SRID = 4326;
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
            union.SRID = 4326;
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
                if (multiPolygon.GetGeometryN(index) is Polygon item)
                    yield return item;
            }
        }
    }
}
