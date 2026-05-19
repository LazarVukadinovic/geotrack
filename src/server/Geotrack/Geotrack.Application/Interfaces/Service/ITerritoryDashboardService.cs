using Geotrack.Application.DTOs.Admin;

namespace Geotrack.Application.Interfaces.Service;

public interface ITerritoryDashboardService
{
    Task<List<TerritoryAssignedTerritoryDto>> GetAssignedTerritoriesAsync(int userId);
    Task<TerritorySidebarSummaryDto> GetSidebarSummaryAsync(int userId, int? territoryId);
    Task<TerritoryDashboardDto> GetDashboardAsync(int userId, int? territoryId);
    Task<TerritoryRegionDirectoryResponseDto> GetRegionsAsync(
        int userId,
        int? territoryId,
        string? search,
        string? sort,
        int skip,
        int take);
    Task<TerritoryContentDirectoryResponseDto> GetContentAsync(
        int userId,
        int? territoryId,
        string? type,
        string? search,
        string? sort,
        int skip,
        int take);
}
