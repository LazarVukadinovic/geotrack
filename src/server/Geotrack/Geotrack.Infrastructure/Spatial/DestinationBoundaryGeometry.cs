using System.Text;
using System.Text.Json;
using NetTopologySuite.Geometries;
using NetTopologySuite.Operation.Union;

namespace Geotrack.Infrastructure.Spatial;

internal static class DestinationBoundaryGeometry
{
    private const int MaxCoordinateCount = 100_000;
    private static readonly GeometryFactory Factory = new(new PrecisionModel(), 4326);

    public static Geometry ParseGeoJson(string geoJson)
    {
        if (string.IsNullOrWhiteSpace(geoJson))
            throw new InvalidOperationException("Boundary GeoJSON is required.");

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(geoJson, new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = 128
            });
        }
        catch (JsonException)
        {
            throw new InvalidOperationException("Boundary file is not valid GeoJSON.");
        }

        using (document)
        {
            var coordinateCount = 0;
            var geometries = ReadRoot(document.RootElement, ref coordinateCount);
            var normalized = NormalizePolygonal(geometries);
            if (normalized is null || normalized.IsEmpty || !normalized.IsValid)
                throw new InvalidOperationException("Boundary GeoJSON must contain a valid Polygon or MultiPolygon.");

            normalized.SRID = 4326;
            return normalized;
        }
    }

    public static string ToGeoJson(Geometry geometry)
    {
        var normalized = NormalizePolygonal(new[] { geometry })
            ?? throw new InvalidOperationException("Boundary geometry is invalid.");

        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("type", normalized.GeometryType);
            writer.WritePropertyName("coordinates");

            if (normalized is Polygon polygon)
            {
                WritePolygonCoordinates(writer, polygon);
            }
            else if (normalized is MultiPolygon multiPolygon)
            {
                writer.WriteStartArray();
                for (var i = 0; i < multiPolygon.NumGeometries; i++)
                    WritePolygonCoordinates(writer, (Polygon)multiPolygon.GetGeometryN(i));
                writer.WriteEndArray();
            }
            else
            {
                throw new InvalidOperationException("Boundary geometry is invalid.");
            }

            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    public static Geometry? NormalizePolygonal(IEnumerable<Geometry?> geometries)
    {
        var polygons = new List<Polygon>();
        foreach (var geometry in geometries)
            CollectPolygons(geometry, polygons);

        if (polygons.Count == 0)
            return null;

        Geometry merged = polygons.Count == 1
            ? polygons[0]
            : UnaryUnionOp.Union(polygons.Cast<Geometry>().ToList());

        if (!merged.IsValid)
            merged = merged.Buffer(0);

        polygons.Clear();
        CollectPolygons(merged, polygons);
        if (polygons.Count == 0)
            return null;

        foreach (var polygon in polygons)
            polygon.SRID = 4326;

        Geometry result = polygons.Count == 1
            ? Factory.CreateMultiPolygon(new[] { polygons[0] })
            : Factory.CreateMultiPolygon(polygons.ToArray());
        result.SRID = 4326;
        return result;
    }

    private static IReadOnlyList<Geometry> ReadRoot(JsonElement root, ref int coordinateCount)
    {
        var type = ReadRequiredType(root);
        return type switch
        {
            "Feature" => new[] { ReadFeatureGeometry(root, ref coordinateCount) },
            "FeatureCollection" => ReadFeatureCollection(root, ref coordinateCount),
            "Polygon" or "MultiPolygon" => new[] { ReadGeometry(root, ref coordinateCount) },
            _ => throw new InvalidOperationException("Boundary GeoJSON must be a Polygon, MultiPolygon, Feature, or FeatureCollection.")
        };
    }

    private static IReadOnlyList<Geometry> ReadFeatureCollection(JsonElement root, ref int coordinateCount)
    {
        if (!root.TryGetProperty("features", out var features) || features.ValueKind != JsonValueKind.Array)
            throw new InvalidOperationException("Boundary GeoJSON FeatureCollection does not contain a features array.");

        var result = new List<Geometry>();
        foreach (var feature in features.EnumerateArray())
        {
            if (!string.Equals(ReadRequiredType(feature), "Feature", StringComparison.Ordinal) ||
                !feature.TryGetProperty("geometry", out var geometry) ||
                geometry.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var geometryType = ReadRequiredType(geometry);
            if (geometryType is not ("Polygon" or "MultiPolygon"))
                continue;

            result.Add(ReadGeometry(geometry, ref coordinateCount));
        }

        if (result.Count == 0)
            throw new InvalidOperationException("Boundary GeoJSON does not contain any polygon features.");

        return result;
    }

    private static Geometry ReadFeatureGeometry(JsonElement feature, ref int coordinateCount)
    {
        if (!feature.TryGetProperty("geometry", out var geometry) || geometry.ValueKind != JsonValueKind.Object)
            throw new InvalidOperationException("Boundary GeoJSON feature does not contain geometry.");

        return ReadGeometry(geometry, ref coordinateCount);
    }

    private static Geometry ReadGeometry(JsonElement geometry, ref int coordinateCount)
    {
        var type = ReadRequiredType(geometry);
        if (!geometry.TryGetProperty("coordinates", out var coordinates) || coordinates.ValueKind != JsonValueKind.Array)
            throw new InvalidOperationException("Boundary GeoJSON geometry does not contain coordinates.");

        return type switch
        {
            "Polygon" => ReadPolygon(coordinates, ref coordinateCount),
            "MultiPolygon" => ReadMultiPolygon(coordinates, ref coordinateCount),
            _ => throw new InvalidOperationException("Boundary GeoJSON geometry must be Polygon or MultiPolygon.")
        };
    }

    private static Polygon ReadPolygon(JsonElement coordinates, ref int coordinateCount)
    {
        var rings = coordinates.EnumerateArray().ToList();
        if (rings.Count == 0)
            throw new InvalidOperationException("Boundary polygon does not contain an exterior ring.");

        var shell = ReadRing(rings[0], ref coordinateCount);
        var holes = new LinearRing[Math.Max(0, rings.Count - 1)];
        for (var i = 1; i < rings.Count; i++)
            holes[i - 1] = ReadRing(rings[i], ref coordinateCount);

        var polygon = Factory.CreatePolygon(shell, holes);
        polygon.SRID = 4326;
        return polygon;
    }

    private static MultiPolygon ReadMultiPolygon(JsonElement coordinates, ref int coordinateCount)
    {
        var polygonElements = coordinates.EnumerateArray().ToList();
        var polygons = new Polygon[polygonElements.Count];
        for (var i = 0; i < polygonElements.Count; i++)
            polygons[i] = ReadPolygon(polygonElements[i], ref coordinateCount);

        if (polygons.Length == 0)
            throw new InvalidOperationException("Boundary multipolygon does not contain polygons.");

        var result = Factory.CreateMultiPolygon(polygons);
        result.SRID = 4326;
        return result;
    }

    private static LinearRing ReadRing(JsonElement ring, ref int coordinateCount)
    {
        if (ring.ValueKind != JsonValueKind.Array)
            throw new InvalidOperationException("Boundary ring is invalid.");

        var points = new List<Coordinate>();
        foreach (var point in ring.EnumerateArray())
        {
            if (point.ValueKind != JsonValueKind.Array)
                throw new InvalidOperationException("Boundary coordinate is invalid.");

            var values = point.EnumerateArray().ToArray();
            if (values.Length < 2 ||
                values[0].ValueKind != JsonValueKind.Number ||
                values[1].ValueKind != JsonValueKind.Number ||
                !values[0].TryGetDouble(out var longitude) ||
                !values[1].TryGetDouble(out var latitude) ||
                !double.IsFinite(longitude) ||
                !double.IsFinite(latitude) ||
                longitude is < -180 or > 180 ||
                latitude is < -90 or > 90)
            {
                throw new InvalidOperationException("Boundary coordinates are invalid.");
            }

            coordinateCount++;
            if (coordinateCount > MaxCoordinateCount)
                throw new InvalidOperationException($"Boundary contains more than {MaxCoordinateCount:N0} coordinates.");

            points.Add(new Coordinate(longitude, latitude));
        }

        if (points.Count < 3)
            throw new InvalidOperationException("Boundary ring must contain at least three points.");

        if (!points[0].Equals2D(points[^1]))
            points.Add(new Coordinate(points[0]));

        if (points.Count < 4 || points.Take(points.Count - 1).Distinct(new Coordinate2DComparer()).Count() < 3)
            throw new InvalidOperationException("Boundary ring must contain at least three distinct points.");

        return Factory.CreateLinearRing(points.ToArray());
    }

    private static string ReadRequiredType(JsonElement element)
    {
        if (!element.TryGetProperty("type", out var type) || type.ValueKind != JsonValueKind.String)
            throw new InvalidOperationException("Boundary GeoJSON type is missing.");

        return type.GetString() ?? string.Empty;
    }

    private static void CollectPolygons(Geometry? geometry, ICollection<Polygon> polygons)
    {
        if (geometry is null || geometry.IsEmpty)
            return;

        switch (geometry)
        {
            case Polygon polygon:
                polygons.Add((Polygon)polygon.Copy());
                break;
            case MultiPolygon multiPolygon:
                for (var i = 0; i < multiPolygon.NumGeometries; i++)
                    polygons.Add((Polygon)multiPolygon.GetGeometryN(i).Copy());
                break;
            case GeometryCollection collection:
                for (var i = 0; i < collection.NumGeometries; i++)
                    CollectPolygons(collection.GetGeometryN(i), polygons);
                break;
        }
    }

    private static void WritePolygonCoordinates(Utf8JsonWriter writer, Polygon polygon)
    {
        writer.WriteStartArray();
        WriteRing(writer, polygon.ExteriorRing.Coordinates);
        for (var i = 0; i < polygon.NumInteriorRings; i++)
            WriteRing(writer, polygon.GetInteriorRingN(i).Coordinates);
        writer.WriteEndArray();
    }

    private static void WriteRing(Utf8JsonWriter writer, IEnumerable<Coordinate> coordinates)
    {
        writer.WriteStartArray();
        foreach (var coordinate in coordinates)
        {
            writer.WriteStartArray();
            writer.WriteNumberValue(Math.Round(coordinate.X, 7));
            writer.WriteNumberValue(Math.Round(coordinate.Y, 7));
            writer.WriteEndArray();
        }
        writer.WriteEndArray();
    }

    private sealed class Coordinate2DComparer : IEqualityComparer<Coordinate>
    {
        public bool Equals(Coordinate? x, Coordinate? y) =>
            x is not null && y is not null && x.Equals2D(y);

        public int GetHashCode(Coordinate obj) => HashCode.Combine(obj.X, obj.Y);
    }
}