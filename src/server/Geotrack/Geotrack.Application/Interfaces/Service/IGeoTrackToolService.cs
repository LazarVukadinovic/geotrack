using Geotrack.Application.DTOs.Agent;
using Geotrack.Application.DTOs.GeoTrackTools;

namespace Geotrack.Application.Interfaces.Service;

public interface IGeoTrackToolService
{
    Task<IReadOnlyList<GeoTrackToolItemDto>> SearchPlacesAsync(GeoTrackToolSearchRequest request, CancellationToken cancellationToken = default);
    Task<GeoTrackToolItemDto?> GetPlaceDetailsAsync(int id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<GeoTrackToolItemDto>> SearchStaysAsync(GeoTrackToolSearchRequest request, CancellationToken cancellationToken = default);
    Task<GeoTrackToolItemDto?> GetStayDetailsAsync(int id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<GeoTrackToolItemDto>> SearchEventsAsync(GeoTrackToolSearchRequest request, CancellationToken cancellationToken = default);
    Task<GeoTrackToolItemDto?> GetEventDetailsAsync(int id, CancellationToken cancellationToken = default);
    Task<GeoTrackMapFiltersDto> GetMapFiltersAsync(GeoTrackMapFiltersRequest request, CancellationToken cancellationToken = default);
    Task<GeoTrackAreaResolveResponseDto> ResolveAreaAsync(GeoTrackAreaResolveRequest request, CancellationToken cancellationToken = default);
    Task<GeoTrackCatalogSearchResponseDto> SearchCatalogAsync(GeoTrackCatalogSearchRequest request, CancellationToken cancellationToken = default);
    Task<GeoTrackAreaContentResponseDto> SearchByAreaAsync(GeoTrackAreaContentRequest request, CancellationToken cancellationToken = default);
    Task<GeoTrackAreaOverviewDto> GetAreaOverviewAsync(GeoTrackAreaOverviewRequest request, CancellationToken cancellationToken = default);
    Task<GeoTrackCategoryOptionsResponseDto> GetCategoryOptionsAsync(GeoTrackCategoryOptionsRequest request, CancellationToken cancellationToken = default);
    Task<GeoTrackAgentContextDto> BuildAgentContextAsync(
        string message,
        AgentContextOptionsDto? contextOptions,
        int maxItems,
        CancellationToken cancellationToken = default);
}
