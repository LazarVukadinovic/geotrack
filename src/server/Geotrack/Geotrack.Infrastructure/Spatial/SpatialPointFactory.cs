using NetTopologySuite.Geometries;

namespace Geotrack.Infrastructure.Spatial;

internal static class SpatialPointFactory
{
    internal const string CoordinatesPropertyName = "Coordinates";

    internal static Point Create(double latitude, double longitude)
    {
        return new Point(longitude, latitude) { SRID = 4326 };
    }
}
