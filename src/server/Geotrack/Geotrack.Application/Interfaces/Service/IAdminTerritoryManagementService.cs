using Geotrack.Application.DTOs.Admin;

namespace Geotrack.Application.Interfaces.Service;

public interface IAdminTerritoryManagementService
{
    Task<AdminTerritoryPageResponseDto> GetTerritoriesAsync(AdminTerritoryPageRequest request, CancellationToken cancellationToken = default);

    Task<AdminTerritoryDetailDto> GetTerritoryDetailAsync(
        int territoryId,
        CancellationToken cancellationToken = default);

    Task<AdminTerritoryManagerSearchResponseDto> SearchManagerCandidatesAsync(
        string? query,
        int take,
        int? countryTerritoryId = null,
        CancellationToken cancellationToken = default);

    Task<AdminTerritoryManagerSearchResponseDto> GetAssignableManagersAsync(
        int territoryId,
        string? query,
        int take,
        CancellationToken cancellationToken = default);

    Task<AdminTerritoryDto> CreateTerritoryAsync(AdminCreateTerritoryRequestDto request, CancellationToken cancellationToken = default);

    Task<AdminTerritoryDetailDto> AssignManagerAsync(
        int territoryId,
        AdminAssignTerritoryManagerRequestDto request,
        CancellationToken cancellationToken = default);

    Task DeleteTerritoryAsync(int territoryId, CancellationToken cancellationToken = default);
}
