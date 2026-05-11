using Geotrack.Application.Interfaces.Repository;
using Geotrack.Domain.Entities.Users;
using Microsoft.EntityFrameworkCore;

namespace Geotrack.Infrastructure.Repositories;

public class UserNotificationRepository : IUserNotificationRepository
{
    private readonly AppDbContext _db;

    public UserNotificationRepository(AppDbContext db)
    {
        _db = db;
    }

    public async Task<List<UserNotification>> GetByUserAsync(int userId, int skip, int take)
    {
        return await _db.UserNotifications
            .AsNoTracking()
            .Where(notification => notification.UserId == userId)
            .OrderByDescending(notification => notification.CreatedAt)
            .ThenByDescending(notification => notification.Id)
            .Skip(skip)
            .Take(take)
            .ToListAsync();
    }

    public async Task<int> CountUnreadAsync(int userId)
    {
        return await _db.UserNotifications
            .AsNoTracking()
            .CountAsync(notification => notification.UserId == userId && notification.ReadAt == null);
    }

    public async Task<UserNotification?> GetByIdForUserAsync(int userId, int notificationId)
    {
        return await _db.UserNotifications
            .FirstOrDefaultAsync(notification => notification.Id == notificationId && notification.UserId == userId);
    }

    public async Task<List<UserNotification>> GetUnreadByUserAsync(int userId)
    {
        return await _db.UserNotifications
            .Where(notification => notification.UserId == userId && notification.ReadAt == null)
            .ToListAsync();
    }

    public void Add(UserNotification notification)
    {
        _db.UserNotifications.Add(notification);
    }

    public async Task SaveChangesAsync()
    {
        await _db.SaveChangesAsync();
    }
}
