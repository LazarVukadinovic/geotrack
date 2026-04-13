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

    public async Task<FavoriteToggleResponseDto> ToggleAsync(int userId, FavoriteToggleDto dto)
    {
        var existing = await _db.Favorites.FirstOrDefaultAsync(
            f => f.UserId == userId && f.EntityType == dto.EntityType && f.EntityId == dto.EntityId);

        if (existing != null)
        {
            _db.Favorites.Remove(existing);
            await _db.SaveChangesAsync();
            return new FavoriteToggleResponseDto { IsSaved = false };
        }

        _db.Favorites.Add(new Favorite
        {
            UserId = userId,
            EntityType = dto.EntityType,
            EntityId = dto.EntityId,
            CreatedAt = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();
        return new FavoriteToggleResponseDto { IsSaved = true };
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
