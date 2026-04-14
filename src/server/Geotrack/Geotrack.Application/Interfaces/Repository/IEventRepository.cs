using Geotrack.Domain.Entities.Core;

namespace Geotrack.Application.Interfaces.Repository;

public interface IEventRepository : IRepository<Event>
{
    Task<Event?> GetWithDetailsAsync(int eventId);
    Task<bool> IsEventSavedByUserAsync(int eventId, int userId);
    Task<List<int>> GetSavedCollectionIdsByUserAsync(int eventId, int userId);
}