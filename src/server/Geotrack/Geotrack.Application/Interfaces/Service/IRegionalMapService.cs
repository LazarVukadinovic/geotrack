using Geotrack.Application.DTOs.Admin;

namespace Geotrack.Application.Interfaces.Service;

public interface IRegionalMapService
{
    Task<RegionalMapResponseDto> GetMapAsync(
        int userId,
        int regionId,
        IEnumerable<string>? types = null,
        CancellationToken cancellationToken = default);

    Task<RegionalMapViewportDto> GetViewportAsync(
        int userId,
        int regionId,
        CancellationToken cancellationToken = default);

    Task<List<RegionalMapZoneDto>> GetZonesAsync(
        int userId,
        int regionId,
        CancellationToken cancellationToken = default);

    Task<RegionalMapSearchResponseDto> SearchAsync(
        int userId,
        int regionId,
        string? query,
        CancellationToken cancellationToken = default);
}
