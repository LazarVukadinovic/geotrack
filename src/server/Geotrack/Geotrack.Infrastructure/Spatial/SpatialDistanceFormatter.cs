namespace Geotrack.Infrastructure.Spatial;

internal static class SpatialDistanceFormatter
{
    internal static string? Format(double? distanceMeters)
    {
        if (!distanceMeters.HasValue)
        {
            return null;
        }

        if (distanceMeters.Value < 1000)
        {
            return $"{Math.Round(distanceMeters.Value)} m away";
        }

        return $"{distanceMeters.Value / 1000:0.#} km away";
    }

    internal static string? FormatBetween(
        double? originLatitude,
        double? originLongitude,
        double? destinationLatitude,
        double? destinationLongitude)
    {
        return Format(CalculateDistanceMeters(
            originLatitude,
            originLongitude,
            destinationLatitude,
            destinationLongitude));
    }

    internal static string? FormatBetween(
        double? originLatitude,
        double? originLongitude,
        double? destinationLatitude,
        double? destinationLongitude,
        bool useCurrentLocationDistance)
    {
        var label = Format(CalculateDistanceMeters(
            originLatitude,
            originLongitude,
            destinationLatitude,
            destinationLongitude));

        if (string.IsNullOrWhiteSpace(label) || useCurrentLocationDistance)
            return label;

        return label.EndsWith(" away", StringComparison.OrdinalIgnoreCase)
            ? label[..^5] + " from centre"
            : label + " from centre";
    }

    private static double? CalculateDistanceMeters(
        double? originLatitude,
        double? originLongitude,
        double? destinationLatitude,
        double? destinationLongitude)
    {
        if (!HasUsableCoordinates(originLatitude, originLongitude) ||
            !HasUsableCoordinates(destinationLatitude, destinationLongitude))
        {
            return null;
        }

        const double earthRadiusMeters = 6_371_000d;
        var dLat = DegreesToRadians(destinationLatitude!.Value - originLatitude!.Value);
        var dLon = DegreesToRadians(destinationLongitude!.Value - originLongitude!.Value);
        var startLat = DegreesToRadians(originLatitude.Value);
        var endLat = DegreesToRadians(destinationLatitude.Value);

        var a =
            Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
            Math.Sin(dLon / 2) * Math.Sin(dLon / 2) * Math.Cos(startLat) * Math.Cos(endLat);

        var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
        return earthRadiusMeters * c;
    }

    private static bool HasUsableCoordinates(double? latitude, double? longitude)
    {
        return latitude.HasValue &&
               longitude.HasValue &&
               !double.IsNaN(latitude.Value) &&
               !double.IsNaN(longitude.Value) &&
               !double.IsInfinity(latitude.Value) &&
               !double.IsInfinity(longitude.Value) &&
               (latitude.Value != 0d || longitude.Value != 0d);
    }

    private static double DegreesToRadians(double degrees) => degrees * Math.PI / 180d;
}
