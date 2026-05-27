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
    Task<TerritoryRegionCreateContextDto> GetRegionCreateContextAsync(int userId, int? territoryId);
    Task<BoundaryRegionCandidateResponseDto> GetRegionBoundaryCandidatesAsync(
        int userId,
        int? territoryId,
        string? admLevel,
        string? search,
        int skip,
        int? take);
    Task<TerritoryRegionCreateResponseDto> CreateRegionAsync(int userId, TerritoryRegionCreateRequestDto request);
    Task<TerritoryRegionDetailDto> GetRegionDetailAsync(
        int userId,
        int regionId,
        int? territoryId,
        string? period);
    Task<TerritoryRegionContentResponseDto> GetRegionContentAsync(
        int userId,
        int regionId,
        int? territoryId,
        string? entityType,
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
