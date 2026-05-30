using Geotrack.Domain.Entities.Users;

namespace Geotrack.Application.Interfaces.Repository;

public interface IUserNotificationRepository
{
    Task<List<UserNotification>> GetByUserAsync(int userId, int skip, int take);
    Task<int> CountByUserAsync(int userId);
    Task<int> CountUnreadAsync(int userId);
    Task<UserNotification?> GetByIdForUserAsync(int userId, int notificationId);
    Task<List<UserNotification>> GetUnreadByUserAsync(int userId);
    Task<bool> ExistsAsync(int userId, string type, string relatedEntityType, int relatedEntityId);

    void Add(UserNotification notification);

    Task SaveChangesAsync();
}
