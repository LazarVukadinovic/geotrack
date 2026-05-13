using Geotrack.Application.DTOs.Admin;
using Geotrack.Application.Interfaces.Service;
using Geotrack.Application.Security;
using Geotrack.Domain.Entities.Core;
using Geotrack.Infrastructure.Spatial;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;
using NetTopologySuite.Operation.Union;

namespace Geotrack.Infrastructure.Services;

public class RegionalMapService : IRegionalMapService
{
    private const int MaxMarkers = 500;
    private const int MaxSearchResults = 30;
    private const string PublishedStatus = "Published";
    private const string BoundaryGeometryPropertyName = "BoundaryGeometry";

    private static readonly HashSet<string> AllowedTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "destination",
        "location",
        "place"
    };

    private readonly AppDbContext _db;

    public RegionalMapService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<RegionalMapResponseDto> GetMapAsync(
        int userId,
        int regionId,
        IEnumerable<string>? types = null,
        CancellationToken cancellationToken = default)
    {
        var data = await LoadMapDataAsync(userId, regionId, cancellationToken);
        var normalizedTypes = NormalizeTypes(types);
        var zones = BuildZones(data.Region, data.Destinations, data.Locations);
        var markers = BuildMarkers(data, normalizedTypes);

        return new RegionalMapResponseDto
        {
            Viewport = BuildViewport(zones, markers),
            Markers = markers,
            Zones = zones,
            EmptyStateMessage = markers.Count == 0 && zones.Count == 0
                ? "This assigned region does not have mapped entities or saved boundaries yet."
                : null
        };
    }

    public async Task<RegionalMapViewportDto> GetViewportAsync(
        int userId,
        int regionId,
        CancellationToken cancellationToken = default)
    {
        var data = await LoadMapDataAsync(userId, regionId, cancellationToken);
        var zones = BuildZones(data.Region, data.Destinations, data.Locations);
        var markers = BuildMarkers(data, NormalizeTypes(null));
        return BuildViewport(zones, markers);
    }

    public async Task<List<RegionalMapZoneDto>> GetZonesAsync(
        int userId,
        int regionId,
        CancellationToken cancellationToken = default)
    {
        var data = await LoadMapDataAsync(userId, regionId, cancellationToken);
        return BuildZones(data.Region, data.Destinations, data.Locations);
    }

    public async Task<RegionalMapSearchResponseDto> SearchAsync(
        int userId,
        int regionId,
        string? query,
        CancellationToken cancellationToken = default)
    {
        _ = await EnsureAssignedRegionAsync(userId, regionId, cancellationToken);

        var normalizedQuery = query?.Trim() ?? string.Empty;
        if (normalizedQuery.Length < 2)
            return new RegionalMapSearchResponseDto();

        if (normalizedQuery.Length > 100)
            throw new InvalidOperationException("Search query must be 100 characters or fewer.");

        var lowered = normalizedQuery.ToLowerInvariant();

        var destinations = await _db.Destinations
            .AsNoTracking()
            .Where(destination => destination.RegionId == regionId)
            .Where(destination =>
                destination.Name.ToLower().Contains(lowered) ||
                (destination.Summary != null && destination.Summary.ToLower().Contains(lowered)))
            .OrderBy(destination => destination.Name)
            .Take(MaxSearchResults)
            .Select(destination => new RegionalMapSearchResultDto
            {
                Id = destination.Id,
                EntityType = "Destination",
                Title = destination.Name,
                Subtitle = destination.Summary,
                CategoryLabel = destination.DestinationType.Name,
                Latitude = destination.Latitude,
                Longitude = destination.Longitude,
                ImageUrl = destination.Images
                    .Where(image => image.TargetType == EntityImageTargetType.Destination)
                    .OrderByDescending(image => image.IsMain)
                    .ThenBy(image => image.SortOrder)
                    .ThenBy(image => image.CreatedAt)
                    .ThenBy(image => image.Id)
                    .Select(image => image.Url)
                    .FirstOrDefault(),
                DetailRoute = $"/destinations/detail/{destination.Id}"
            })
            .ToListAsync(cancellationToken);

        var locations = await _db.Locations
            .AsNoTracking()
            .Where(location => location.Destination.RegionId == regionId)
            .Where(location =>
                location.Name.ToLower().Contains(lowered) ||
                (location.Summary != null && location.Summary.ToLower().Contains(lowered)) ||
                location.Destination.Name.ToLower().Contains(lowered))
            .OrderBy(location => location.Name)
            .Take(MaxSearchResults)
            .Select(location => new RegionalMapSearchResultDto
            {
                Id = location.Id,
                EntityType = "Location",
                Title = location.Name,
                Subtitle = location.Destination.Name,
                CategoryLabel = location.LocationType.Name,
                Latitude = location.Latitude,
                Longitude = location.Longitude,
                ImageUrl = location.Images
                    .Where(image => image.TargetType == EntityImageTargetType.Location)
                    .OrderByDescending(image => image.IsMain)
                    .ThenBy(image => image.SortOrder)
                    .ThenBy(image => image.CreatedAt)
                    .ThenBy(image => image.Id)
                    .Select(image => image.Url)
                    .FirstOrDefault(),
                DetailRoute = $"/locations/detail/{location.Id}"
            })
            .ToListAsync(cancellationToken);

        var places = await _db.Places
            .AsNoTracking()
            .Where(place => place.Location.Destination.RegionId == regionId)
            .Where(place =>
                place.Name.ToLower().Contains(lowered) ||
                (place.Subtitle != null && place.Subtitle.ToLower().Contains(lowered)) ||
                place.Location.Name.ToLower().Contains(lowered) ||
                place.Location.Destination.Name.ToLower().Contains(lowered))
            .OrderBy(place => place.Name)
            .Take(MaxSearchResults)
            .Select(place => new RegionalMapSearchResultDto
            {
                Id = place.Id,
                EntityType = "Place",
                Title = place.Name,
                Subtitle = place.Location.Name,
                CategoryLabel = place.PlaceType.Name,
                Latitude = place.Latitude,
                Longitude = place.Longitude,
                ImageUrl = place.Images
                    .Where(image => image.TargetType == EntityImageTargetType.Place)
                    .OrderByDescending(image => image.IsMain)
                    .ThenBy(image => image.SortOrder)
                    .ThenBy(image => image.CreatedAt)
                    .ThenBy(image => image.Id)
                    .Select(image => image.Url)
                    .FirstOrDefault(),
                DetailRoute = $"/places/detail/{place.Id}"
            })
            .ToListAsync(cancellationToken);

        return new RegionalMapSearchResponseDto
        {
            Items = destinations
                .Concat(locations)
                .Concat(places)
                .Where(result => HasValidCoordinates(result.Latitude, result.Longitude))
                .OrderBy(result => result.Title)
                .ThenBy(result => result.EntityType)
                .Take(MaxSearchResults)
                .ToList()
        };
    }

    private async Task<RegionalMapData> LoadMapDataAsync(
        int userId,
        int regionId,
        CancellationToken cancellationToken)
    {
        var region = await EnsureAssignedRegionAsync(userId, regionId, cancellationToken);

        var destinations = await _db.Destinations
            .AsNoTracking()
            .Where(destination => destination.RegionId == regionId)
            .OrderBy(destination => destination.Name)
            .ThenBy(destination => destination.Id)
            .Take(MaxMarkers)
            .Select(destination => new DestinationMapProjection
            {
                Id = destination.Id,
                Name = destination.Name,
                Summary = destination.Summary,
                Status = destination.Status,
                DestinationTypeName = destination.DestinationType.Name,
                Latitude = destination.Latitude,
                Longitude = destination.Longitude,
                BoundaryGeometry = EF.Property<Geometry?>(destination, BoundaryGeometryPropertyName),
                ImageUrl = destination.Images
                    .Where(image => image.TargetType == EntityImageTargetType.Destination)
                    .OrderByDescending(image => image.IsMain)
                    .ThenBy(image => image.SortOrder)
                    .ThenBy(image => image.CreatedAt)
                    .ThenBy(image => image.Id)
                    .Select(image => image.Url)
                    .FirstOrDefault()
            })
            .ToListAsync(cancellationToken);

        var locations = await _db.Locations
            .AsNoTracking()
            .Where(location => location.Destination.RegionId == regionId)
            .OrderBy(location => location.Name)
            .ThenBy(location => location.Id)
            .Take(MaxMarkers)
            .Select(location => new LocationMapProjection
            {
                Id = location.Id,
                Name = location.Name,
                Summary = location.Summary,
                Status = location.Status,
                LocationTypeName = location.LocationType.Name,
                Latitude = location.Latitude,
                Longitude = location.Longitude,
                DestinationId = location.DestinationId,
                DestinationName = location.Destination.Name,
                BoundaryGeometry = EF.Property<Geometry?>(location, BoundaryGeometryPropertyName),
                ImageUrl = location.Images
                    .Where(image => image.TargetType == EntityImageTargetType.Location)
                    .OrderByDescending(image => image.IsMain)
                    .ThenBy(image => image.SortOrder)
                    .ThenBy(image => image.CreatedAt)
                    .ThenBy(image => image.Id)
                    .Select(image => image.Url)
                    .FirstOrDefault()
            })
            .ToListAsync(cancellationToken);

        var places = await _db.Places
            .AsNoTracking()
            .Where(place => place.Location.Destination.RegionId == regionId)
            .OrderBy(place => place.Name)
            .ThenBy(place => place.Id)
            .Take(MaxMarkers)
            .Select(place => new PlaceMapProjection
            {
                Id = place.Id,
                Name = place.Name,
                Subtitle = place.Subtitle,
                Status = place.Status,
                PlaceTypeName = place.PlaceType.Name,
                Latitude = place.Latitude,
                Longitude = place.Longitude,
                LocationId = place.LocationId,
                LocationName = place.Location.Name,
                DestinationId = place.Location.DestinationId,
                ImageUrl = place.Images
                    .Where(image => image.TargetType == EntityImageTargetType.Place)
                    .OrderByDescending(image => image.IsMain)
                    .ThenBy(image => image.SortOrder)
                    .ThenBy(image => image.CreatedAt)
                    .ThenBy(image => image.Id)
                    .Select(image => image.Url)
                    .FirstOrDefault()
            })
            .ToListAsync(cancellationToken);

        return new RegionalMapData(region, destinations, locations, places);
    }

    private async Task<RegionMapProjection> EnsureAssignedRegionAsync(
        int userId,
        int regionId,
        CancellationToken cancellationToken)
    {
        if (regionId <= 0)
            throw new InvalidOperationException("regionId is required.");

        await EnsureRegionalManagerRoleAsync(userId, cancellationToken);

        var region = await _db.RegionalManagerRegions
            .AsNoTracking()
            .Where(assignment =>
                assignment.UserId == userId &&
                assignment.RegionId == regionId &&
                assignment.IsActive)
            .Select(assignment => new RegionMapProjection
            {
                Id = assignment.RegionId,
                Name = assignment.Region.Name,
                TerritoryName = assignment.Region.Territory.Name
            })
            .SingleOrDefaultAsync(cancellationToken);

        return region ?? throw new UnauthorizedAccessException("Region is not assigned to the current regional manager.");
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

    private static List<RegionalMapMarkerDto> BuildMarkers(
        RegionalMapData data,
        HashSet<string> normalizedTypes)
    {
        var markers = new List<RegionalMapMarkerDto>();

        if (ShouldInclude(normalizedTypes, "destination"))
        {
            markers.AddRange(data.Destinations
                .Where(destination => HasValidCoordinates(destination.Latitude, destination.Longitude))
                .Select(destination => new RegionalMapMarkerDto
                {
                    Id = destination.Id,
                    EntityType = "Destination",
                    Title = destination.Name,
                    Subtitle = destination.Summary,
                    CategoryLabel = destination.DestinationTypeName,
                    Latitude = destination.Latitude,
                    Longitude = destination.Longitude,
                    ImageUrl = destination.ImageUrl,
                    Status = NormalizeStatus(destination.Status),
                    RegionId = data.Region.Id,
                    DestinationId = destination.Id,
                    DetailRoute = $"/destinations/detail/{destination.Id}",
                    EditRoute = "/destinations/new"
                }));
        }

        if (ShouldInclude(normalizedTypes, "location"))
        {
            markers.AddRange(data.Locations
                .Where(location => HasValidCoordinates(location.Latitude, location.Longitude))
                .Select(location => new RegionalMapMarkerDto
                {
                    Id = location.Id,
                    EntityType = "Location",
                    Title = location.Name,
                    Subtitle = location.DestinationName,
                    CategoryLabel = location.LocationTypeName,
                    Latitude = location.Latitude,
                    Longitude = location.Longitude,
                    ImageUrl = location.ImageUrl,
                    Status = NormalizeStatus(location.Status),
                    RegionId = data.Region.Id,
                    DestinationId = location.DestinationId,
                    LocationId = location.Id,
                    DetailRoute = $"/locations/detail/{location.Id}",
                    EditRoute = "/locations/new"
                }));
        }

        if (ShouldInclude(normalizedTypes, "place"))
        {
            markers.AddRange(data.Places
                .Where(place => HasValidCoordinates(place.Latitude, place.Longitude))
                .Select(place => new RegionalMapMarkerDto
                {
                    Id = place.Id,
                    EntityType = "Place",
                    Title = place.Name,
                    Subtitle = place.LocationName,
                    CategoryLabel = place.PlaceTypeName,
                    Latitude = place.Latitude,
                    Longitude = place.Longitude,
                    ImageUrl = place.ImageUrl,
                    Status = NormalizeStatus(place.Status),
                    RegionId = data.Region.Id,
                    DestinationId = place.DestinationId,
                    LocationId = place.LocationId,
                    PlaceId = place.Id,
                    DetailRoute = $"/places/detail/{place.Id}",
                    EditRoute = "/places/new"
                }));
        }

        return markers
            .OrderBy(marker => marker.EntityType)
            .ThenBy(marker => marker.Title)
            .ThenBy(marker => marker.Id)
            .Take(MaxMarkers)
            .ToList();
    }

    private static List<RegionalMapZoneDto> BuildZones(
        RegionMapProjection region,
        List<DestinationMapProjection> destinations,
        List<LocationMapProjection> locations)
    {
        var zones = new List<RegionalMapZoneDto>();

        var regionGeometry = BuildRegionGeometry(destinations, locations);
        var regionGeoJson = ToGeoJson(regionGeometry);
        if (regionGeoJson is not null)
        {
            var centroid = regionGeometry!.Centroid;
            zones.Add(new RegionalMapZoneDto
            {
                Id = region.Id,
                EntityType = "Region",
                Name = region.Name,
                DisplayName = BuildRegionDisplayName(region.Name, region.TerritoryName),
                Status = "Assigned",
                GeometryGeoJson = regionGeoJson,
                CenterLatitude = centroid.Y,
                CenterLongitude = centroid.X
            });
        }

        zones.AddRange(destinations
            .Select(destination => BuildZone(
                destination.Id,
                "Destination",
                destination.Name,
                destination.Name,
                destination.Status,
                destination.BoundaryGeometry,
                $"/destinations/detail/{destination.Id}",
                "/destinations/new"))
            .Where(zone => zone is not null)
            .Select(zone => zone!));

        zones.AddRange(locations
            .Select(location => BuildZone(
                location.Id,
                "Location",
                location.Name,
                location.Name,
                location.Status,
                location.BoundaryGeometry,
                $"/locations/detail/{location.Id}",
                "/locations/new"))
            .Where(zone => zone is not null)
            .Select(zone => zone!));

        return zones;
    }

    private static RegionalMapZoneDto? BuildZone(
        int id,
        string entityType,
        string name,
        string displayName,
        string? status,
        Geometry? geometry,
        string detailRoute,
        string editRoute)
    {
        var polygonalGeometry = ToPolygonalGeometry(geometry);
        var geoJson = ToGeoJson(polygonalGeometry);
        if (geoJson is null || polygonalGeometry is null)
            return null;

        var centroid = polygonalGeometry.Centroid;
        return new RegionalMapZoneDto
        {
            Id = id,
            EntityType = entityType,
            Name = name,
            DisplayName = displayName,
            Status = NormalizeStatus(status),
            GeometryGeoJson = geoJson,
            CenterLatitude = centroid.Y,
            CenterLongitude = centroid.X,
            DetailRoute = detailRoute,
            EditRoute = editRoute
        };
    }

    private static Geometry? BuildRegionGeometry(
        List<DestinationMapProjection> destinations,
        List<LocationMapProjection> locations)
    {
        var geometries = destinations
            .Select(destination => destination.BoundaryGeometry)
            .Concat(locations.Select(location => location.BoundaryGeometry))
            .Select(ToPolygonalGeometry)
            .Where(geometry => geometry is not null)
            .Select(geometry => geometry!)
            .ToList();

        if (geometries.Count == 0)
            return null;

        if (geometries.Count == 1)
            return geometries[0];

        try
        {
            return ToPolygonalGeometry(UnaryUnionOp.Union(geometries));
        }
        catch
        {
            return null;
        }
    }

    private static RegionalMapViewportDto BuildViewport(
        List<RegionalMapZoneDto> zones,
        List<RegionalMapMarkerDto> markers)
    {
        var accumulator = new BoundsAccumulator();

        foreach (var zone in zones)
        {
            IncludeGeometry(accumulator, zone.GeometryGeoJson);
        }

        foreach (var marker in markers)
        {
            accumulator.Include(marker.Latitude, marker.Longitude);
        }

        if (!accumulator.HasValue)
        {
            return new RegionalMapViewportDto
            {
                IsEmpty = true
            };
        }

        var centerLatitude = (accumulator.North + accumulator.South) / 2d;
        var centerLongitude = (accumulator.East + accumulator.West) / 2d;
        var latitudeSpan = Math.Abs(accumulator.North - accumulator.South);
        var longitudeSpan = Math.Abs(accumulator.East - accumulator.West);
        var isSinglePoint = latitudeSpan < 0.00001d && longitudeSpan < 0.00001d;

        return new RegionalMapViewportDto
        {
            CenterLatitude = centerLatitude,
            CenterLongitude = centerLongitude,
            Zoom = isSinglePoint ? 13d : EstimateZoom(Math.Max(latitudeSpan, longitudeSpan)),
            Bounds = isSinglePoint
                ? null
                : new RegionalMapBoundsDto
                {
                    North = accumulator.North,
                    South = accumulator.South,
                    East = accumulator.East,
                    West = accumulator.West
                },
            IsEmpty = false
        };
    }

    private static void IncludeGeometry(BoundsAccumulator accumulator, RegionalMapGeometryDto? geometry)
    {
        if (geometry?.Coordinates is null)
            return;

        if (geometry.Type.Equals("Polygon", StringComparison.OrdinalIgnoreCase) &&
            geometry.Coordinates is double[][][] polygon)
        {
            foreach (var ring in polygon)
            {
                foreach (var coordinate in ring)
                {
                    if (coordinate.Length >= 2)
                        accumulator.Include(coordinate[1], coordinate[0]);
                }
            }
        }
        else if (geometry.Type.Equals("MultiPolygon", StringComparison.OrdinalIgnoreCase) &&
                 geometry.Coordinates is double[][][][] multiPolygon)
        {
            foreach (var polygonCoordinates in multiPolygon)
            {
                foreach (var ring in polygonCoordinates)
                {
                    foreach (var coordinate in ring)
                    {
                        if (coordinate.Length >= 2)
                            accumulator.Include(coordinate[1], coordinate[0]);
                    }
                }
            }
        }
    }

    private static RegionalMapGeometryDto? ToGeoJson(Geometry? geometry)
    {
        var polygonalGeometry = ToPolygonalGeometry(geometry);
        if (polygonalGeometry is null)
            return null;

        return polygonalGeometry switch
        {
            Polygon polygon => new RegionalMapGeometryDto
            {
                Type = "Polygon",
                Coordinates = ToPolygonCoordinates(polygon)
            },
            MultiPolygon multiPolygon => new RegionalMapGeometryDto
            {
                Type = "MultiPolygon",
                Coordinates = Enumerable.Range(0, multiPolygon.NumGeometries)
                    .Select(index => ToPolygonCoordinates((Polygon)multiPolygon.GetGeometryN(index)))
                    .ToArray()
            },
            _ => null
        };
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
                .Select(index => collection.GetGeometryN(index))
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

    private static double[][][] ToPolygonCoordinates(Polygon polygon)
    {
        var rings = new List<double[][]>
        {
            ToRingCoordinates(polygon.ExteriorRing)
        };

        for (var index = 0; index < polygon.NumInteriorRings; index++)
        {
            rings.Add(ToRingCoordinates(polygon.GetInteriorRingN(index)));
        }

        return rings.ToArray();
    }

    private static double[][] ToRingCoordinates(LineString ring)
    {
        return ring.Coordinates
            .Select(coordinate => new[]
            {
                RoundCoordinate(coordinate.X),
                RoundCoordinate(coordinate.Y)
            })
            .ToArray();
    }

    private static HashSet<string> NormalizeTypes(IEnumerable<string>? types)
    {
        var normalized = (types ?? Array.Empty<string>())
            .SelectMany(type => type.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Where(type => AllowedTypes.Contains(type))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return normalized;
    }

    private static bool ShouldInclude(HashSet<string> normalizedTypes, string type) =>
        normalizedTypes.Count == 0 || normalizedTypes.Contains(type);

    private static bool HasValidCoordinates(double latitude, double longitude)
    {
        return !double.IsNaN(latitude) &&
               !double.IsNaN(longitude) &&
               !double.IsInfinity(latitude) &&
               !double.IsInfinity(longitude) &&
               latitude >= -90 &&
               latitude <= 90 &&
               longitude >= -180 &&
               longitude <= 180 &&
               (latitude != 0d || longitude != 0d);
    }

    private static string NormalizeStatus(string? status) =>
        string.IsNullOrWhiteSpace(status) ? PublishedStatus : status;

    private static string BuildRegionDisplayName(string name, string? territoryName) =>
        string.IsNullOrWhiteSpace(territoryName) ? name : $"{name}, {territoryName}";

    private static double EstimateZoom(double span)
    {
        if (span >= 8d) return 6d;
        if (span >= 4d) return 7d;
        if (span >= 2d) return 8d;
        if (span >= 1d) return 9d;
        if (span >= 0.5d) return 10d;
        if (span >= 0.2d) return 11d;
        if (span >= 0.08d) return 12d;
        return 13d;
    }

    private static double RoundCoordinate(double coordinate) =>
        Math.Round(coordinate, 7, MidpointRounding.AwayFromZero);

    private sealed record RegionalMapData(
        RegionMapProjection Region,
        List<DestinationMapProjection> Destinations,
        List<LocationMapProjection> Locations,
        List<PlaceMapProjection> Places);

    private sealed class RegionMapProjection
    {
        public int Id { get; init; }
        public string Name { get; init; } = string.Empty;
        public string? TerritoryName { get; init; }
    }

    private sealed class DestinationMapProjection
    {
        public int Id { get; init; }
        public string Name { get; init; } = string.Empty;
        public string? Summary { get; init; }
        public string? Status { get; init; }
        public string? DestinationTypeName { get; init; }
        public double Latitude { get; init; }
        public double Longitude { get; init; }
        public Geometry? BoundaryGeometry { get; init; }
        public string? ImageUrl { get; init; }
    }

    private sealed class LocationMapProjection
    {
        public int Id { get; init; }
        public string Name { get; init; } = string.Empty;
        public string? Summary { get; init; }
        public string? Status { get; init; }
        public string? LocationTypeName { get; init; }
        public double Latitude { get; init; }
        public double Longitude { get; init; }
        public int DestinationId { get; init; }
        public string DestinationName { get; init; } = string.Empty;
        public Geometry? BoundaryGeometry { get; init; }
        public string? ImageUrl { get; init; }
    }

    private sealed class PlaceMapProjection
    {
        public int Id { get; init; }
        public string Name { get; init; } = string.Empty;
        public string? Subtitle { get; init; }
        public string? Status { get; init; }
        public string? PlaceTypeName { get; init; }
        public double Latitude { get; init; }
        public double Longitude { get; init; }
        public int LocationId { get; init; }
        public string LocationName { get; init; } = string.Empty;
        public int DestinationId { get; init; }
        public string? ImageUrl { get; init; }
    }

    private sealed class BoundsAccumulator
    {
        public bool HasValue { get; private set; }
        public double North { get; private set; }
        public double South { get; private set; }
        public double East { get; private set; }
        public double West { get; private set; }

        public void Include(double latitude, double longitude)
        {
            if (!HasValidCoordinates(latitude, longitude))
                return;

            if (!HasValue)
            {
                North = latitude;
                South = latitude;
                East = longitude;
                West = longitude;
                HasValue = true;
                return;
            }

            North = Math.Max(North, latitude);
            South = Math.Min(South, latitude);
            East = Math.Max(East, longitude);
            West = Math.Min(West, longitude);
        }
    }
}
