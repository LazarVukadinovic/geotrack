using Geotrack.Domain.Entities.Core;

namespace Geotrack.Application.Interfaces.Repository;

public interface ITerritoryRepository : IRepository<Territory>
{
    Task<bool> ExistsAsync(string name);
    Task<Territory?> GetByIdWithRegionsAsync(int territoryId);
    Task<Territory?> GetByIdAsync(int territoryId);
    Task DeleteAsync(Territory territory);
}