using Geotrack.Application.DTOs.Destinations;
using Geotrack.Application.DTOs.Explore;

namespace Geotrack.Application.Interfaces.Service;

public interface IExploreService
{
    Task<ExplorePageDto> GetExplorePageAsync(double latitude, double longitude, int? userId = null);
    Task<ExplorePageDto> GetExplorePageForDestinationAsync(int destinationId, int? userId = null);
    Task<ExploreRecommendationsPageDto> GetRecommendationsAsync(
        double latitude,
        double longitude,
        int userId,
        int? tagId = null,
        int skip = 0,
        int take = 8);
    Task<DestinationScopedResult<LocalPlacesCategoriesPageDto>> GetLocalPlacesCategoriesAsync(
        int? destinationId,
        double? latitude = null,
        double? longitude = null,
        int skip = 0,
        int take = 10);
}
