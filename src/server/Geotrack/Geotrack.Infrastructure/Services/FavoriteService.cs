using Geotrack.Application.DTOs.Favorites;
using Geotrack.Application.Interfaces.Service;
using Geotrack.Domain.Entities.Users;
using Geotrack.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Geotrack.Infrastructure.Services;

public class FavoriteService : IFavoriteService
{
    private readonly AppDbContext _db;

    public FavoriteService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<FavoriteToggleResult> ToggleAsync(int userId, FavoriteToggleDto dto)
    {
        var type = dto.EntityType?.Trim().ToLowerInvariant() ?? string.Empty;

        if (type != "place" && type != "event")
        {
            return new FavoriteToggleResult { Status = FavoriteToggleStatus.InvalidEntityType };
        }

        var exists = type == "place"
            ? await _db.Places.AnyAsync(p => p.Id == dto.EntityId)
            : await _db.Events.AnyAsync(e => e.Id == dto.EntityId);

        if (!exists)
        {
            return new FavoriteToggleResult { Status = FavoriteToggleStatus.EntityNotFound };
        }

        var existing = await _db.Favorites.FirstOrDefaultAsync(
            f => f.UserId == userId && f.EntityType == type && f.EntityId == dto.EntityId);

        if (existing != null)
        {
            _db.Favorites.Remove(existing);
            await _db.SaveChangesAsync();
            return new FavoriteToggleResult { Status = FavoriteToggleStatus.Success, IsSaved = false };
        }

        _db.Favorites.Add(new Favorite
        {
            UserId = userId,
            EntityType = type,
            EntityId = dto.EntityId,
            CreatedAt = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();
        return new FavoriteToggleResult { Status = FavoriteToggleStatus.Success, IsSaved = true };
    }

    public async Task<HashSet<int>> GetFavoriteIdsAsync(int userId, string entityType)
    {
        var ids = await _db.Favorites
            .Where(f => f.UserId == userId && f.EntityType == entityType)
            .Select(f => f.EntityId)
            .ToListAsync();
        return ids.ToHashSet();
    }

    public async Task<bool> IsFavoriteAsync(int userId, string entityType, int entityId)
    {
        return await _db.Favorites.AnyAsync(
            f => f.UserId == userId && f.EntityType == entityType && f.EntityId == entityId);
    }
}
