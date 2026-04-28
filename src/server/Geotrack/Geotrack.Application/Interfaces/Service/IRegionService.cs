using Geotrack.Application.DTOs.Region;
using Geotrack.Application.DTOs.Users;

namespace Geotrack.Application.Interfaces.Service
{
    public interface IRegionService
    {
        Task<GetRegionsResponseDto> GetRegionsAsync();
        Task<GetRegionDetailsResponseDto> GetRegionDetailsAsync(int regionId);
    }
}