using Geotrack.Application.DTOs.Territories;

namespace Geotrack.Application.Interfaces.Service;

public interface ITerritoryService
{
    Task<GetTerritoriesResponseDto> GetTerritoriesAsync();
    Task<GetTerritoryDetailsResponseDto> GetTerritoryDetailsAsync(int territoryId);
    Task<CreateTerritoryResponseDto> CreateTerritoryAsync(CreateTerritoryRequestDto request);
    Task DeleteTerritoryAsync(int territoryId);

    Task<CreateRegionInTerritoryResponseDto> CreateRegionAsync(int territoryId, CreateRegionInTerritoryRequestDto request);
    Task DeleteRegionAsync(int territoryId, int regionId);
}