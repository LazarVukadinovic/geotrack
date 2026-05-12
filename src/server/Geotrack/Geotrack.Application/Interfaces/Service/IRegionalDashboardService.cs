using Geotrack.Application.DTOs.Admin;

namespace Geotrack.Application.Interfaces.Service;

public interface IRegionalDashboardService
{
    Task<List<RegionalAssignedRegionDto>> GetAssignedRegionsAsync(int userId);
    Task<RegionalDashboardDto> GetDashboardAsync(int userId, int regionId);
    Task<RegionalDashboardActivityResponseDto> GetActivityAsync(int userId, int regionId, int skip, int take);
}
