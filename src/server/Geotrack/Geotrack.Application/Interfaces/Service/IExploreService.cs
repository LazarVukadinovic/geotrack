using Geotrack.Application.DTOs.Explore;

namespace Geotrack.Application.Interfaces.Service;

public interface IExploreService
{
    Task<ExplorePageDto> GetExplorePageAsync(
        double latitude,
        double longitude,
        int? userId = null,
        bool useCurrentLocationDistance = false);

    Task<ExplorePageDto> GetExplorePageForDestinationAsync(
        int destinationId,
        int? userId = null);

    Task<ExploreSectionPageDto> GetExploreSectionPageAsync(
        string sectionKey,
        int destinationId,
        int? userId = null,
        int skip = 0,
        int take = 20);
}
