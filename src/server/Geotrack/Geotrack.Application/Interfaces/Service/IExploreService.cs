using Geotrack.Application.DTOs.Explore;

namespace Geotrack.Application.Interfaces.Service;

public interface IExploreService
{
    Task<ExplorePageDto> GetExplorePageAsync(double latitude, double longitude, int? userId = null);
    Task<List<LocalPlacesCategoryDto>> GetLocalPlacesCategoriesAsync(int destinationId);
}
