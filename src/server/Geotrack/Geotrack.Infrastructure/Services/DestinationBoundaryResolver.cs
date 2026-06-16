using Geotrack.Application.DTOs.Admin;
using Geotrack.Application.DTOs.Contributions;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;

namespace Geotrack.Infrastructure.Services;

public sealed class DestinationBoundaryResolver
{
    private const string BoundaryGeometryPropertyName = "BoundaryGeometry";
    private const string PublishedStatus = "Published";
    private readonly AppDbContext _db;

    public DestinationBoundaryResolver(AppDbContext db)
    {
        _db = db;
    }

    public Task<DestinationBoundaryResolution> ResolveInRegionAsync(
        int regionId,
        double latitude,
        double longitude,
        CancellationToken cancellationToken = default) =>
        ResolveAsync(regionId, latitude, longitude, publishedOnly: false, cancellationToken);

    public async Task<DestinationBoundaryResolution> ResolveRequiredInRegionAsync(
        int regionId,
        double latitude,
        double longitude,
        CancellationToken cancellationToken = default)
    {
        var result = await ResolveInRegionAsync(regionId, latitude, longitude, cancellationToken);
        if (!result.IsResolved)
            throw new InvalidOperationException(result.Message);

        return result;
    }

    public Task<DestinationBoundaryResolution> ResolvePublicAsync(
        double latitude,
        double longitude,
        CancellationToken cancellationToken = default) =>
        ResolveAsync(null, latitude, longitude, publishedOnly: true, cancellationToken);

    public async Task<ResolveContributionLocationDto> ResolveContributionAsync(
        double latitude,
        double longitude,
        CancellationToken cancellationToken = default)
    {
        var resolution = await ResolvePublicAsync(latitude, longitude, cancellationToken);
        var response = new ResolveContributionLocationDto
        {
            IsResolved = resolution.IsResolved,
            IsAmbiguous = resolution.IsAmbiguous,
            DestinationId = resolution.DestinationId,
            DestinationName = resolution.DestinationName,
            RegionName = resolution.RegionName,
            Message = resolution.Message
        };

        if (!resolution.IsResolved || !resolution.DestinationId.HasValue)
            return response;

        response.Locations = await _db.Locations
            .AsNoTracking()
            .Where(location =>
                location.DestinationId == resolution.DestinationId.Value &&
                location.Status == PublishedStatus)
            .OrderBy(location => location.Name)
            .ThenBy(location => location.Id)
            .Select(location => new ContributionLocationOptionDto
            {
                Id = location.Id,
                Name = location.Name,
                DestinationId = location.DestinationId,
                DestinationName = location.Destination.Name,
                Status = location.Status
            })
            .ToListAsync(cancellationToken);

        return response;
    }

    public static DestinationCoordinateResolutionDto ToDto(DestinationBoundaryResolution resolution) => new()
    {
        IsResolved = resolution.IsResolved,
        IsAmbiguous = resolution.IsAmbiguous,
        DestinationId = resolution.DestinationId,
        DestinationName = resolution.DestinationName,
        RegionId = resolution.RegionId,
        RegionName = resolution.RegionName,
        Message = resolution.Message
    };

    private async Task<DestinationBoundaryResolution> ResolveAsync(
        int? regionId,
        double latitude,
        double longitude,
        bool publishedOnly,
        CancellationToken cancellationToken)
    {
        ValidateCoordinates(latitude, longitude);
        var point = new Point(longitude, latitude) { SRID = 4326 };

        var query = _db.Destinations
            .AsNoTracking()
            .Where(destination => EF.Property<Geometry?>(destination, BoundaryGeometryPropertyName) != null)
            .Where(destination => EF.Property<Geometry>(destination, BoundaryGeometryPropertyName).Covers(point));

        if (regionId.HasValue)
            query = query.Where(destination => destination.RegionId == regionId.Value);

        if (publishedOnly)
            query = query.Where(destination => destination.Status == PublishedStatus);

        var matches = await query
            .OrderBy(destination => destination.Id)
            .Select(destination => new DestinationBoundaryMatch(
                destination.Id,
                destination.Name,
                destination.RegionId,
                destination.Region.Name))
            .Take(3)
            .ToListAsync(cancellationToken);

        if (matches.Count == 0)
        {
            return DestinationBoundaryResolution.Unresolved(
                "No destination boundary contains the selected coordinates. Create or correct the destination boundary first.");
        }

        if (matches.Count > 1)
        {
            return DestinationBoundaryResolution.Ambiguous(
                "More than one destination boundary contains the selected coordinates. Resolve the overlapping destination boundaries before creating content there.");
        }

        var match = matches[0];
        return DestinationBoundaryResolution.Resolved(
            match.DestinationId,
            match.DestinationName,
            match.RegionId,
            match.RegionName);
    }

    private static void ValidateCoordinates(double latitude, double longitude)
    {
        if (!double.IsFinite(latitude) || latitude is < -90 or > 90)
            throw new InvalidOperationException("Latitude must be between -90 and 90.");

        if (!double.IsFinite(longitude) || longitude is < -180 or > 180)
            throw new InvalidOperationException("Longitude must be between -180 and 180.");

        if (latitude == 0d && longitude == 0d)
            throw new InvalidOperationException("Please choose valid coordinates.");
    }

    private sealed record DestinationBoundaryMatch(
        int DestinationId,
        string DestinationName,
        int RegionId,
        string RegionName);
}

public sealed record DestinationBoundaryResolution(
    bool IsResolved,
    bool IsAmbiguous,
    int? DestinationId,
    string? DestinationName,
    int? RegionId,
    string? RegionName,
    string Message)
{
    public static DestinationBoundaryResolution Resolved(
        int destinationId,
        string destinationName,
        int regionId,
        string regionName) =>
        new(true, false, destinationId, destinationName, regionId, regionName, "Destination resolved from its boundary.");

    public static DestinationBoundaryResolution Unresolved(string message) =>
        new(false, false, null, null, null, null, message);

    public static DestinationBoundaryResolution Ambiguous(string message) =>
        new(false, true, null, null, null, null, message);
}
