using Geotrack.Application.DTOs.Map;

namespace Geotrack.Application.Interfaces.Service;

public interface IMapService
{
    Task<List<MapSearchResultDto>> SearchAsync(string query, CancellationToken cancellationToken = default);
    Task<List<MapSearchResultDto>> SearchAsync(string query, IEnumerable<string>? types, CancellationToken cancellationToken = default);
    Task<List<MapSearchResultDto>> GetAllDestinationsAsync(CancellationToken cancellationToken = default);
    Task<MapReverseGeocodeDto?> ReverseGeocodeAsync(double latitude, double longitude, CancellationToken cancellationToken = default);
    Task<MapRouteResponseDto?> GetWalkingRouteAsync(
        double originLatitude,
        double originLongitude,
        double destinationLatitude,
        double destinationLongitude,
        CancellationToken cancellationToken = default);
    Task<MapRouteResponseDto?> GetDrivingRouteAsync(
        double originLatitude,
        double originLongitude,
        double destinationLatitude,
        double destinationLongitude,
        CancellationToken cancellationToken = default);
    Task<GetMapEntitiesResponseDto> GetEntitiesAsync(
        double latitude,
        double longitude,
        IEnumerable<string>? categories,
        double? radiusKm,
        int? userId = null,
        CancellationToken cancellationToken = default);
    Task<GetMapMarkersResponseDto> GetMarkersInViewportAsync(
        double north,
        double south,
        double east,
        double west,
        double zoom,
        IEnumerable<string>? categories,
        int? userId = null,
        CancellationToken cancellationToken = default);
}
