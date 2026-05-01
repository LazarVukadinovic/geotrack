using Geotrack.Domain.Entities.Core;
using Geotrack.Domain.Entities.Reviews;
namespace Geotrack.Application.Interfaces.Repository;
public interface IEventRepository : IRepository<Event>
{
    Task<Event?> GetWithDetailsAsync(int eventId);
    Task<bool> IsEventSavedByUserAsync(int eventId, int userId);
    Task<List<int>> GetSavedCollectionIdsByUserAsync(int eventId, int
    userId);
    Task<List<EventReview>> GetReviewsByEventIdAsync(int eventId);
    Task<bool> HasUserReviewedEventAsync(int eventId, int userId);
    Task AddReviewAsync(EventReview review);
    Task<List<Event>> SearchByNameAsync(string query);
}
